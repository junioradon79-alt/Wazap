// Campagne WhatsApp : envoie un template à chaque prospect du CSV.
// Entrée : CSV (Nom;WhatsApp_Number;…) — sortie : Prospects_relances.csv + relance_log.txt
//
// DEUX FOURNISSEURS (option --provider=…, défaut whatchimp) :
//   whatchimp (défaut) : passerelle WhatChimp (GET /send). Réservé aux numéros
//     SUBSCRIBERS du bot — tout envoi vers un numéro froid est traité en texte et
//     refusé « outside 24 hour window ». Prérequis : template approuvé Meta ET mappé
//     dans WhatChimp (diagnostic : scripts/campaign/Check-TemplateMapping.ps1 ;
//     guide : prospection/MAPPING_VARIABLES_WHATCHIMP.md).
//   meta : API Meta WhatsApp Cloud DIRECTE (POST /{version}/{phone_number_id}/messages).
//     Contourne totalement WhatChimp et accepte l'envoi d'un template à N'IMPORTE QUEL
//     numéro froid, sans subscriber ni fenêtre 24 h — c'est le mode à utiliser quand
//     l'import de subscribers WhatChimp échoue (cf. MEMOIRE.md §3.6).
//
// Config (env) :
//   communs            : TEMPLATE_NAME, COMMERCIAL, VIDEO_URL, LANGUAGE_CODE.
//   --provider=whatchimp: WHATCHIMP_API_TOKEN (obligatoire), WHATCHIMP_PHONE_NUMBER_ID.
//   --provider=meta    : META_API_TOKEN (obligatoire), META_PHONE_NUMBER_ID (obligatoire),
//                        META_API_VERSION (défaut v21.0).
// Options : <csv> [--provider=meta] [--zone=Marcory] [--limit=20] [--dry-run]
//           [--commercial=X] [--video-url=URL]
using System.Text;
using System.Text.Json;

var input = "Prospects.csv";
var zoneFilter = "";
var limit = 0;
var dryRun = false;
var preflightSubscribers = false;
var force = false;
var provider = "whatchimp";
var commercialArg = "";
var videoUrlArg = "";

foreach (var arg in args)
{
    if (arg.StartsWith("--zone=", StringComparison.OrdinalIgnoreCase)) zoneFilter = arg["--zone=".Length..];
    else if (arg.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase)) int.TryParse(arg["--limit=".Length..], out limit);
    else if (arg.StartsWith("--provider=", StringComparison.OrdinalIgnoreCase)) provider = arg["--provider=".Length..].ToLowerInvariant();
    else if (arg.StartsWith("--commercial=", StringComparison.OrdinalIgnoreCase)) commercialArg = arg["--commercial=".Length..];
    else if (arg.StartsWith("--video-url=", StringComparison.OrdinalIgnoreCase)) videoUrlArg = arg["--video-url=".Length..];
    else if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) dryRun = true;
    else if (arg.Equals("--preflight-subscribers", StringComparison.OrdinalIgnoreCase)) preflightSubscribers = true;
    else if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase)) force = true;
    else if (!arg.StartsWith("--", StringComparison.Ordinal)) input = arg;
    else Console.Error.WriteLine($"Option inconnue ignorée : {arg}");
}

if (provider is not ("whatchimp" or "meta"))
{
    Console.Error.WriteLine("--provider doit être 'whatchimp' (défaut) ou 'meta'.");
    return 2;
}
var metaProvider = provider == "meta";

var apiToken = Environment.GetEnvironmentVariable("WHATCHIMP_API_TOKEN");
var phoneNumberId = Environment.GetEnvironmentVariable("WHATCHIMP_PHONE_NUMBER_ID") ?? "735886129615120";
var baseUrl = Environment.GetEnvironmentVariable("WHATCHIMP_BASE_URL") ?? "https://app.whatchimp.com/api/v1/whatsapp/";
var template = Environment.GetEnvironmentVariable("TEMPLATE_NAME") ?? "prospect_approach_v2";
var commercial = !string.IsNullOrWhiteSpace(commercialArg)
    ? commercialArg
    : (Environment.GetEnvironmentVariable("COMMERCIAL") ?? "L'équipe WAZAP");
var videoUrl = !string.IsNullOrWhiteSpace(videoUrlArg)
    ? videoUrlArg
    : (Environment.GetEnvironmentVariable("VIDEO_URL") ?? "");
var languageCode = Environment.GetEnvironmentVariable("LANGUAGE_CODE") ?? "fr";

// API Meta WhatsApp Cloud (provider meta) : envoi de template à N'IMPORTE QUEL numéro
// froid, sans subscriber ni fenêtre 24 h — voie de contournement quand l'import de
// subscribers WhatChimp est hors service (cf. MEMOIRE.md §3.6). ⚠️ Politique Meta :
// les messages MARKETING n'atteignent que les numéros ayant opté-in ; un envoi vers
// un numéro froid peut être bloqué ou qualifier le compte (code 132000). Jeton :
// Meta for Developers → WhatsApp → Configuration → « Jeton d'accès permanent » (WABA).
var metaApiToken = Environment.GetEnvironmentVariable("META_API_TOKEN") ?? "";
var metaPhoneNumberId = Environment.GetEnvironmentVariable("META_PHONE_NUMBER_ID") ?? phoneNumberId;
var metaApiVersion = Environment.GetEnvironmentVariable("META_API_VERSION") ?? "v21.0";
var metaGraphUrl = Environment.GetEnvironmentVariable("META_GRAPH_URL") ?? "https://graph.facebook.com/";
var metaSendUrl = $"{metaGraphUrl}{metaApiVersion}/{metaPhoneNumberId}/messages";

// Les templates Meta approuvés portent un suffixe `_v2` ; la table ci-dessous matche sur
// le nom canonique (sans suffixe) pour éviter sa duplication.
static string CanonicalName(string templateName)
    => templateName.EndsWith("_v2", StringComparison.Ordinal)
        ? templateName[..^3]
        : templateName;

// Chaque template déclare son propre nombre de variables : en envoyer plus (ou moins)
// fait rejeter l'envoi par Meta (« parameter count mismatch »). La table reflète les
// corps soumis dans WhatsApp Manager — à mettre à jour si un corps change.
static string[] BuildVariables(string templateName, string nom, string commercial, string lien)
    => CanonicalName(templateName) switch
    {
        "prospect_approach" => [nom, commercial, lien],
        "prospect_followup" => [nom, commercial],
        "prospect_offer" => [nom],
        "rider_recruit" or "rider_company" => [nom, lien],
        _ => [nom, commercial, lien],
    };

var variableCount = BuildVariables(template, "x", "x", "x").Length;
var usesLink = BuildVariables(template, "", "", "LIEN").Contains("LIEN");

if (usesLink && string.IsNullOrWhiteSpace(videoUrl))
{
    Console.WriteLine($"⚠️  VIDEO_URL non définie — la dernière variable de « {template} » sera vide.");
}
if (CanonicalName(template) is not ("prospect_approach" or "prospect_followup" or "prospect_offer"
    or "rider_recruit" or "rider_company"))
{
    Console.WriteLine($"⚠️  Template « {template} » inconnu de la table : {variableCount} variables envoyées par défaut.");
}

// Lecture CSV (séparateur ';', guillemets tolérés).
var rawLines = File.ReadAllLines(input, Encoding.UTF8);
var prospects = new List<(string Nom, string Phone, string Zone)>();
foreach (var raw in rawLines.Skip(1))
{
    if (string.IsNullOrWhiteSpace(raw)) continue;
    var line = raw.Trim();

    // Découpe en respectant les champs entre guillemets.
    var fields = new List<string>();
    var cur = new StringBuilder();
    var inQuotes = false;
    foreach (var ch in line)
    {
        if (ch == '"') inQuotes = !inQuotes;
        else if (ch == ';' && !inQuotes) { fields.Add(cur.ToString().Trim()); cur.Clear(); }
        else cur.Append(ch);
    }
    fields.Add(cur.ToString().Trim());
    if (fields.Count < 2) continue;

    var nom = fields[0].Trim('"').Trim();
    var phone = fields[1].Trim('"').Trim();
    var zone = fields.Count >= 5 ? fields[4].Trim('"').Trim() : "";
    if (string.IsNullOrWhiteSpace(phone)) continue;

    // Filtre zone.
    if (!string.IsNullOrWhiteSpace(zoneFilter)
        && !string.Equals(zone, zoneFilter, StringComparison.OrdinalIgnoreCase))
        continue;

    prospects.Add((nom, phone, zone));
}

if (limit > 0) prospects = prospects.Take(limit).ToList();

Console.WriteLine($"Prospects sélectionnés : {prospects.Count}" + (string.IsNullOrWhiteSpace(zoneFilter) ? "" : $" (zone {zoneFilter})"));

// Vérification du format : +225 + 10 chiffres mobiles (01/05/07).
var valides = new List<(string Nom, string Phone, string Zone)>();
var ignores = new List<(string Nom, string Phone, string Raison)>();
foreach (var p in prospects)
{
    var digits = new string(p.Phone.Where(char.IsDigit).ToArray());
    if (digits.Length == 13 && digits.StartsWith("225") && (digits[3..].StartsWith("01") || digits[3..].StartsWith("05") || digits[3..].StartsWith("07")))
        valides.Add(p);
    else
        ignores.Add((p.Nom, p.Phone, "format non mobile 10 chiffres (attendu +225 0x...)"));
}

foreach (var i in ignores)
    Console.WriteLine($"  ⚠️  Ignoré : {i.Nom} — {i.Phone} ({i.Raison})");

if (valides.Count == 0)
{
    Console.Error.WriteLine("Aucun prospect valide à contacter.");
    return 1;
}

if (dryRun)
{
    Console.WriteLine("\n=== MODE DRY-RUN (aucun envoi) ===");
    foreach (var (nom, phone, zone) in valides)
        Console.WriteLine($"  -> {phone} | {nom} | {zone}");
    Console.WriteLine($"Total : {valides.Count} envoi(s) simulé(s).");
    return 0;
}

if (!File.Exists(input))
{
    Console.Error.WriteLine($"Fichier introuvable : {input}");
    return 1;
}
if (!metaProvider && string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("WHATCHIMP_API_TOKEN manquante (variable d'environnement).");
    return 1;
}
if (metaProvider && (string.IsNullOrWhiteSpace(metaApiToken) || string.IsNullOrWhiteSpace(metaPhoneNumberId)))
{
    Console.Error.WriteLine("Provider meta : META_API_TOKEN et META_PHONE_NUMBER_ID obligatoires (variables d'environnement).");
    return 1;
}

using var http = new HttpClient();
http.Timeout = TimeSpan.FromSeconds(30);

if (preflightSubscribers)
{
    if (metaProvider)
    {
        // Sans objet pour l'API Meta directe : elle délivre un template approuvé à
        // n'importe quel numéro, sans notion de subscriber ni fenêtre 24 h. Ce mode
        // ne sert que au provider whatchimp (vérifier l'import UI — guide
        // prospection/IMPORT_SUBSCRIBERS_WHATCHIMP.md).
        Console.WriteLine("\n=== PREFLIGHT SUBSCRIBERS (provider meta) ===");
        Console.WriteLine("Sans objet : l'API Meta envoie les templates à n'importe quel numéro froid (pas de subscriber).");
        Console.WriteLine("Simulation d'abord : dotnet run --project tools/WhatsAppCampaign -- <csv> --provider=meta --dry-run");
        return 0;
    }
    // La passerelle refuse tout envoi template vers un numéro qui n'est pas un
    // subscriber du bot (« outside 24 hour window » = traitement en texte).
    // Ce mode compare le CSV aux subscribers réels (GET subscriber/list, aucun envoi)
    // pour vérifier que l'import UI a bien été fait avant de lancer la campagne.
    Console.WriteLine("\n=== PREFLIGHT SUBSCRIBERS (aucun envoi) ===");
    var subscriberDigits = await FetchSubscriberDigitsAsync();
    Console.WriteLine($"Subscribers du bot : {subscriberDigits.Count}");
    var manquants = valides.Where(v => !MatchesSubscriber(v.Phone, subscriberDigits))
        .Select(v => (v.Nom, v.Phone)).ToList();

    Console.WriteLine($"Prospects subscribers    : {valides.Count - manquants.Count}/{valides.Count}");
    Console.WriteLine($"Prospects NON subscribers: {manquants.Count}");
    foreach (var m in manquants.Take(10))
        Console.WriteLine($"  ✗ {m.Phone} | {m.Nom}");
    if (manquants.Count > 10)
        Console.WriteLine($"  … et {manquants.Count - 10} autres (liste complète → Prospects_preflight.csv)");

    File.WriteAllText("Prospects_preflight.csv",
        "Nom;WhatsApp_Number;Subscriber\n"
        + string.Join("\n", valides.Select(v =>
            $"{v.Nom};{v.Phone};{(manquants.Any(m => m.Phone == v.Phone) ? "NON" : "OUI")}")),
        Encoding.UTF8);
    Console.WriteLine("\nDétail : Prospects_preflight.csv");
    Console.WriteLine("Si des prospects ne sont pas subscribers : importez-les d'abord dans WhatChimp");
    Console.WriteLine("(Subscriber Manager → Options → Import Subscribers — cf. prospection/IMPORT_SUBSCRIBERS_WHATCHIMP.md),");
    Console.WriteLine("créez un label (ex. « prospection-72 »), assignez-le, puis relancez la campagne.");
    return manquants.Count == 0 ? 0 : 2;
}

// Garde-fou automatique (mode envoi, provider whatchimp) : tout envoi template vers
// un non-subscriber est traité en TEXTE par la passerelle → refus « outside 24 hour
// window ». Le broadcast, seul moyen d'atteindre des non-subscribers, est UI-only
// (aucun endpoint API de campagne : tous les sondages renvoient 401). On vérifie donc
// l'import avant le moindre envoi ; --force court-circuite (dépannage, en connaissance
// de cause). L'API Meta (provider meta) accepte les numéros froids → garde-fou inutile.
if (!metaProvider && !force)
{
    Console.WriteLine("Vérification des subscribers (GET subscriber/list, aucun envoi)…");
    var knownSubscribers = await FetchSubscriberDigitsAsync();
    var nonSubscribers = valides.Where(v => !MatchesSubscriber(v.Phone, knownSubscribers)).ToList();
    if (nonSubscribers.Count > 0)
    {
        Console.WriteLine($"\n⛔ {nonSubscribers.Count}/{valides.Count} prospect(s) ne sont PAS subscribers du bot (+225 75 80 38 01) — envoi annulé.");
        Console.WriteLine("    Un envoi direct serait refusé (« outside 24 hour window ») : la passerelle ne");
        Console.WriteLine("    délivre les templates qu'aux subscribers, et le broadcast est UI-only.");
        foreach (var m in nonSubscribers.Take(10))
            Console.WriteLine($"  ✗ {m.Phone} | {m.Nom}");
        Console.WriteLine("\nProcédure (cf. prospection/IMPORT_SUBSCRIBERS_WHATCHIMP.md) :");
        Console.WriteLine("  1. Importer le CSV dans WhatChimp : Subscriber Manager → Options → Import Subscribers");
        Console.WriteLine("     (fichier prêt : prospection/IMPORT_WHATCHIMP_72.csv) ;");
        Console.WriteLine("  2. Vérifier l'import : dotnet run -- <csv> --preflight-subscribers (0 manquant attendu) ;");
        Console.WriteLine("  3. Lancer la campagne : Broadcast Center → Create Campaign → WhatsApp");
        Console.WriteLine($"     (template « {template} »), ou relancer cet outil une fois l'import fait.");
        Console.WriteLine("     (contournement : --force)");
        return 2;
    }
    Console.WriteLine($"OK : {valides.Count}/{valides.Count} prospects sont subscribers du bot.");
}
else
{
    Console.WriteLine("⚠️  --force : garde-fou subscribers désactivé (risque de refus « outside 24 hour window »).");
}

Console.WriteLine($"Prospects à contacter : {valides.Count}");

var log = new StringBuilder();
var outLines = new StringBuilder();
outLines.AppendLine("WhatsApp_Number;Statut;Date_dernier_contact");
var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

for (var i = 0; i < valides.Count; i++)
{
    var (nom, phone, _) = valides[i];
    var variables = BuildVariables(template, nom, commercial, videoUrl);

    try
    {
        string? refus = null;
        string statut = "";
        string content = "";

        if (metaProvider)
        {
            // --- Envoi direct via l'API Meta WhatsApp Cloud (POST JSON) ---
            var parameters = new List<object?>();
            for (var v = 0; v < variables.Length; v++)
                parameters.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = variables[v] });

            // L'API attend le numéro en E.164 sans « + » (ex. 2250747639363).
            var payload = new Dictionary<string, object?>
            {
                ["messaging_product"] = "whatsapp",
                ["to"] = new string(phone.Where(char.IsDigit).ToArray()),
                ["type"] = "template",
                ["template"] = new Dictionary<string, object?>
                {
                    ["name"] = template,
                    ["language"] = new Dictionary<string, object?> { ["code"] = languageCode },
                    ["components"] = new[] { new Dictionary<string, object?>
                    {
                        ["type"] = "body",
                        ["parameters"] = parameters
                    } }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, metaSendUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Authorization", $"Bearer {metaApiToken}");

            using var metaRes = await http.SendAsync(req);
            content = await metaRes.Content.ReadAsStringAsync();
            // Réussite : HTTP 200 + {"messages":[{…}]}. Échec : corps {"error":{…}}
            // (codes utiles : 131026 jeton invalide · 131042 niveau messagerie/paiement ·
            //  132000 destinataire sans opt-in · 131030 paramètres du template).
            refus = metaRes.IsSuccessStatusCode
                ? MetaGatewayRefusal(content)
                : (MetaGatewayRefusal(content) ?? $"HTTP {(int)metaRes.StatusCode}");
            statut = !metaRes.IsSuccessStatusCode ? $"ECHEC_{metaRes.StatusCode}"
                : refus is not null ? "REFUSE"
                : "OK";
        }
        else
        {
            // --- Envoi via la passerelle WhatChimp (GET /send) ---
            var sb = new StringBuilder($"{baseUrl}send?apiToken={Uri.EscapeDataString(apiToken ?? "")}")
                .Append($"&phone_number_id={Uri.EscapeDataString(phoneNumberId)}")
                .Append($"&phone_number={Uri.EscapeDataString(phone)}")
                .Append($"&template_name={Uri.EscapeDataString(template)}&language_code={Uri.EscapeDataString(languageCode)}");
            for (var v = 0; v < variables.Length; v++)
                sb.Append($"&variable{v + 1}={Uri.EscapeDataString(variables[v])}");

            var res = await http.GetAsync(sb.ToString());
            content = await res.Content.ReadAsStringAsync();
            // WhatChimp répond HTTP 200 même quand la passerelle refuse l'envoi : le
            // verdict est dans le corps ({"status":"0","message":"…"}). Sans cette
            // lecture, une campagne rejetée serait journalisée « OK » sur tout le lot.
            refus = GatewayRefusal(content);
            statut = !res.IsSuccessStatusCode ? $"ECHEC_{res.StatusCode}"
                : refus is not null ? "REFUSE"
                : "OK";
        }

        log.AppendLine($"{now} | {phone} | {statut} | {content[..Math.Min(200, content.Length)]}");
        outLines.AppendLine($"{phone};{statut};{now}");
        Console.WriteLine($"[{i + 1}/{valides.Count}] {phone} : {statut}" + (refus is null ? "" : $" — {refus}"));
    }
    catch (Exception ex)
    {
        log.AppendLine($"{now} | {phone} | ERREUR | {ex.Message}");
        outLines.AppendLine($"{phone};ERREUR;{now}");
        Console.WriteLine($"[{i + 1}/{valides.Count}] {phone} : ERREUR {ex.Message}");
    }

    if (i < valides.Count - 1)
        await Task.Delay(TimeSpan.FromSeconds(2)); // anti rate-limiting
}

File.WriteAllText("relance_log.txt", log.ToString(), Encoding.UTF8);
File.WriteAllText("Prospects_relances.csv", outLines.ToString(), Encoding.UTF8);

var lignes = outLines.ToString().Split('\n').Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
var ok = lignes.Count(l => l.Contains(";OK;"));
var refuses = lignes.Count(l => l.Contains(";REFUSE;"));
Console.WriteLine($"Terminé : {ok} délivré(s), {refuses} refusé(s) par la passerelle, "
    + $"{lignes.Count - ok - refuses} en erreur → relance_log.txt + Prospects_relances.csv");
if (refuses > 0)
{
    if (metaProvider)
        Console.WriteLine("⚠️  Refus API Meta : voir le code (131026 jeton · 131042 messagerie/paiement · 132000 opt-in · 131030 paramètres).");
    else
        Console.WriteLine("⚠️  Refus de passerelle : template non approuvé, nombre de variables incorrect, ou fenêtre 24 h.");
}
return 0;

/// <summary>
/// Motif de refus renvoyé par WhatChimp dans un corps HTTP 200, ou <c>null</c> si l'envoi
/// est accepté. Comme <c>WhatChimpService.EnsureGatewayAccepted</c>, ne conclut au refus
/// que si <c>status</c> vaut explicitement « 0 » : un corps inattendu n'est pas un échec.
/// </summary>
static string? GatewayRefusal(string? content)
{
    if (string.IsNullOrWhiteSpace(content)) return null;
    try
    {
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("status", out var status)) return null;

        var value = status.ValueKind == JsonValueKind.String ? status.GetString() : status.ToString();
        if (value != "0") return null;

        return doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? (m.GetString() ?? "refus non détaillé")
            : "refus non détaillé";
    }
    catch (JsonException)
    {
        return null;
    }
}

/// <summary>
/// Motif de refus renvoyé par l'API Meta WhatsApp Cloud dans le corps d'une réponse
/// d'erreur ({"error":{"code":…,"message":"…"}}), ou <c>null</c> si l'envoi est accepté
/// (corps « 200 + messages[] »). Un corps inattendu n'est pas un échec.
/// </summary>
static string? MetaGatewayRefusal(string? content)
{
    if (string.IsNullOrWhiteSpace(content)) return null;
    try
    {
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("error", out var err)) return null;
        if (err.ValueKind != JsonValueKind.Object) return "erreur non détaillée";

        var code = err.TryGetProperty("code", out var c) ? c.ToString() : "?";
        var message = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? (m.GetString() ?? "")
            : "";
        return message.Length == 0 ? $"erreur {code}" : $"{message} (code {code})";
    }
    catch (JsonException)
    {
        return null;
    }
}

/// <summary>
/// Récupère les subscribers réels du bot (GET <c>subscriber/list</c>, aucun envoi),
/// pagination complète, sous forme de numéros en chiffres bruts.
/// </summary>
async Task<List<string>> FetchSubscriberDigitsAsync()
{
    var subscriberDigits = new List<string>();
    var offset = 0;
    while (true)
    {
        var url = $"{baseUrl}subscriber/list?apiToken={Uri.EscapeDataString(apiToken ?? "")}"
            + $"&phone_number_id={Uri.EscapeDataString(phoneNumberId)}&limit=1000&offset={offset}";
        var res = await http.GetAsync(url);
        var content = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"subscriber/list : HTTP {res.StatusCode} — {content[..Math.Min(200, content.Length)]}");
            Environment.Exit(1);
        }
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String && st.GetString() != "1")
        {
            Console.Error.WriteLine("subscriber/list : statut « 0 » (cf. message dans la réponse).");
            Environment.Exit(1);
        }
        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in msg.EnumerateArray())
            {
                if (s.TryGetProperty("chat_id", out var chat) && chat.ValueKind == JsonValueKind.String)
                {
                    var digits = new string(chat.GetString()!.Where(char.IsDigit).ToArray());
                    if (digits.Length > 0) subscriberDigits.Add(digits);
                }
            }
        }
        if (root.TryGetProperty("nextOffset", out var next) && next.ValueKind == JsonValueKind.Number)
        {
            offset = next.GetInt32();
        }
        else break;
    }
    return subscriberDigits;
}

/// <summary>
/// Correspondance tolérante entre un prospect (+225 + 10 chiffres) et un subscriber :
/// WhatChimp peut stocker l'ancien format ivoirien à 8 chiffres — on compare donc
/// aussi les 8 derniers chiffres (numéro national).
/// </summary>
static bool MatchesSubscriber(string prospectPhone, List<string> subscriberDigits)
{
    var digits = new string(prospectPhone.Where(char.IsDigit).ToArray()); // 225 + 10 chiffres
    var suffix = digits[^8..]; // 8 derniers chiffres = numéro national
    return subscriberDigits.Any(s =>
        s == digits
        || (s.Length >= 8 && s.EndsWith(suffix, StringComparison.Ordinal))
        || (digits.EndsWith(s[^Math.Min(8, s.Length)..], StringComparison.Ordinal)));
}
