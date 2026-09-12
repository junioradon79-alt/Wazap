// =============================================================
// WAZAP — Collecteur de prospects commerçants (Grand Abidjan)
// Sources Google Places :
//   --api=legacy : Text Search + Place Details (legacy, maps.googleapis.com)
//   --api=new    : Places API (New) — POST places:searchText (1 seul appel/lot)
//
// Usage :
//   $env:GOOGLE_PLACES_API_KEY='AIza...'
//   dotnet run --project tools\ProspectCollector -- --api=new           (défaut legacy)
//   dotnet run --project tools\ProspectCollector -- --zone=Marcory --types=restaurant
//
// Options :
//   --api=legacy|new   moteur Google (défaut : legacy)
//   --zone=<nom>       une seule zone (défaut : 13 zones)
//   --types=<a,b>      types restreints (défaut : 10 types métier)
//   --preset=livreurs  cibles de recrutement livreurs (sociétés de livraison, coursiers, moto-taxi)
//   --max=<n>          plafond de fiches par couple zone/type
//   --delay-ms=<n>     délai entre 2 appels (défaut 300 ms legacy)
//   --exclude=<fichier>  CSV des numéros déjà clients/contactés
//   --out-dir=<dossier>   dossier de sortie (défaut : cwd)
//   --reset            ignore l'état de reprise précédent
//
// Sorties (out-dir) :
//   Prospects_Abidjan_<ts>.csv            schéma standard (campagne/scoring)
//   Prospects_Abidjan_<ts>_detail.csv     enrichi (note, avis, site, lien Maps)
//   prospect_collector_errors.log         erreurs / quotas
//   prospect_collector_state.json         état de reprise + numéros déjà vus
// =============================================================
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var apiKey = Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("GOOGLE_PLACES_API_KEY manquante. Clé : console.cloud.google.com → Places API → Clés API.");
    return 1;
}

var apiMode = (Arg("--api=") ?? "legacy").ToLowerInvariant();
if (apiMode is not ("legacy" or "new"))
{
    Console.Error.WriteLine("--api doit être 'legacy' ou 'new'.");
    return 2;
}

var zones = new[] { "Cocody", "Marcory", "Yopougon", "Adjamé", "Treichville", "Plateau",
    "Abobo", "Koumassi", "Port-Bouët", "Bingerville", "Songon", "Attécoubé", "Anyama" };

// 33 secteurs qui livrent (défaut). Google Places Text Search accepte des libellés libres :
// "{secteur} in {zone}, Abidjan, Côte d'Ivoire".
var defaultTypes = new[] { "restaurant", "fast food", "snack", "bar", "café", "traiteur",
    "boulangerie", "pâtisserie", "supermarché", "épicerie", "boucherie", "poissonnerie",
    "primeur", "crèmerie", "pharmacie", "parapharmacie", "produits bio", "optique",
    "vêtements", "chaussures", "électroménager", "téléphonie", "informatique", "fleuriste",
    "quincaillerie", "matériaux", "meubles & déco", "librairie", "animalerie", "sport",
    "jouets", "bijouterie", "boissons" };

// Preset « livreurs » : cibles B2B de recrutement (sociétés de livraison, coursiers, moto-taxis…)
// -> à contacter avec le template Meta `rider_company_v2` (voir prospection/GUIDE_RECRUTEMENT_LIVREURS.md).
var livreurTypes = new[] { "service de livraison", "société de livraison", "coursier",
    "livraison de colis", "transport de colis", "taxi-moto", "moto-taxi", "mototaxi",
    "location de moto", "vente de moto", "garage moto", "école de conduite" };

string? Arg(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    ?.Substring(prefix.Length);

var zoneFilter = Arg("--zone=");
var preset = Arg("--preset=")?.Trim().ToLowerInvariant();
if (preset is not null && preset != "livreurs")
    Console.Error.WriteLine($"Preset inconnu ignoré : {preset} (valeurs : livreurs).");
var types = Arg("--types=")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? (preset == "livreurs" ? livreurTypes : defaultTypes);
if (zoneFilter is not null) zones = [zoneFilter];
var maxPerCombo = int.TryParse(Arg("--max="), out var mx) && mx > 0 ? mx : int.MaxValue;
var delayMs = int.TryParse(Arg("--delay-ms="), out var dl) && dl >= 0 ? dl : (apiMode == "new" ? 1500 : 300);
var excludeFile = Arg("--exclude=");
var outDir = Arg("--out-dir=") ?? Directory.GetCurrentDirectory();
var reset = args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));

Directory.CreateDirectory(outDir);
var statePath = Path.Combine(outDir, "prospect_collector_state.json");

// ---------- état / reprise ----------
var state = new CollectorState();
if (!reset && File.Exists(statePath))
{
    try { state = JsonSerializer.Deserialize<CollectorState>(await File.ReadAllTextAsync(statePath)) ?? state; }
    catch { Console.Error.WriteLine("État illisible, démarrage à vide."); }
}

var known = new HashSet<string>(state.AllSeen ?? [], StringComparer.Ordinal);
foreach (var old in Directory.GetFiles(outDir, "Prospects_Abidjan_*.csv"))
    await LoadPhonesFromFileAsync(old, known);
if (excludeFile is not null)
    await LoadPhonesFromFileAsync(excludeFile, known);
Console.WriteLine($"Exclusions chargées : {known.Count} numéros déjà vus. API : {apiMode}.");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };

var results = new Dictionary<string, Prospect>(StringComparer.Ordinal); // clé : téléphone canonique
var errors = new StringBuilder();
var seenPlaceIds = new HashSet<string>(StringComparer.Ordinal);
var apiCalls = 0;
var combosDone = 0;
var combosTotal = zones.Length * types.Length;
var stopwatch = System.Diagnostics.Stopwatch.StartNew();


// ---------- boucle zones × types ----------
foreach (var zone in zones)
{
    foreach (var q in types)
    {
        combosDone++;
        var comboKey = $"{zone}|{q}";
        if (!reset && state.Completed?.Contains(comboKey) == true)
        {
            Console.WriteLine($"[{combosDone}/{combosTotal}] SKIP {comboKey} (déjà terminé)");
            continue;
        }

        var query = $"{q} in {zone}, Abidjan, Côte d'Ivoire";
        var pageToken = (string?)null;
        var page = 0;
        var treated = 0;
        var hardErrors = 0;

        do
        {
            var outcome = await FetchPageAsync(http, apiKey, apiMode, query, pageToken, errors);
            apiCalls++;
            if (outcome.Status == "REQUEST_DENIED")
            {
                Console.Error.WriteLine($"Clé refusée ({outcome.ErrorMessage}). Vérifiez l'activation de l'API dans la console, puis réessayez --api={(apiMode == "legacy" ? "new" : "legacy")}.");
                return 2;
            }
            if (outcome.Status != "OK")
            {
                if (outcome.Status == "OVER_QUERY_LIMIT")
                {
                    errors.AppendLine($"[{comboKey}] OVER_QUERY_LIMIT — pause 8 s.");
                    await Task.Delay(TimeSpan.FromSeconds(8));
                    continue; // on retente la même page (même pageToken)
                }
                hardErrors++;
                errors.AppendLine($"[{comboKey}] page {page}: {outcome.ErrorMessage}");
                break;
            }

            page++;
            foreach (var f in outcome.Items)
            {
                if (treated >= maxPerCombo) break;
                treated++;

                if (f.Id is not null && !seenPlaceIds.Add(f.Id)) continue;
                if (f.Phone is null && apiMode == "legacy")
                {
                    // L'API legacy ne fournit pas le téléphone dans la recherche → appel Place Details.
                    var details = await FetchLegacyDetailsAsync(http, apiKey, f.Id, errors);
                    apiCalls++;
                    if (f.Address is null) f.Address = details?.Address;
                    f.Phone = details?.Phone;
                    f.Website = details?.Website;
                    if (f.MapsLink is null) f.MapsLink = details?.MapsLink;
                }
                if (apiMode == "new" && f.MapsLink is null)
                    f.MapsLink = $"https://www.google.com/maps/place/?q=place_id:{Uri.EscapeDataString(f.Id ?? "")}";

                var phone = NormalizeCI(f.Phone);
                if (phone is null || known.Contains(phone)) continue;

                var address = string.IsNullOrWhiteSpace(f.Address) ? "?" : f.Address!;
                results.TryAdd(phone, new Prospect(f.Name ?? "?", address, phone, q, zone,
                    f.Rating, f.RatingCount, f.Website, f.MapsLink ?? "", DateTime.Now.ToString("yyyy-MM-dd")));
            }

            pageToken = outcome.NextToken;
            if (pageToken is not null)
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
        } while (pageToken is not null);

        if (hardErrors == 0 || treated == 0)
        {
            state.Completed ??= [];
            state.Completed!.Add(comboKey);
        }

        var eta = stopwatch.Elapsed.TotalSeconds / combosDone * (combosTotal - combosDone);
        Console.WriteLine($"[{combosDone}/{combosTotal}] {comboKey} → {results.Count} uniques cumulés " +
                          $"({apiCalls} appels) · ETA {eta / 60:0.0} min");

        await SaveStateAsync(statePath, state, known, results);
    }
}

// ---------- écritures de sortie ----------
var ts = DateTime.Now.ToString("yyyyMMdd_HHmm");
var ordered = results.Values.OrderBy(p => p.Zone).ThenBy(p => p.Name).ToList();
foreach (var p in ordered) known.Add(p.Phone); // évite les doublons inter-types d'une même exécution

// 1) CSV standard (compatible WhatsAppCampaign / ProspectScoring)
var std = new StringBuilder();
std.AppendLine("Nom;WhatsApp_Number;Nom_Rue;Specialite;Zone;Source");
foreach (var p in ordered)
    std.AppendLine($"{Csv(p.Name)};{p.Phone};{Csv(p.Address)};{Csv(p.Specialite)};{Csv(p.Zone)};GooglePlaces");

var stdPath = Path.Combine(outDir, $"Prospects_Abidjan_{ts}.csv");
await File.WriteAllTextAsync(stdPath, std.ToString(), new UTF8Encoding(true));

// 2) CSV enrichi (note, nb avis, site web, lien Google Maps)
var detailCsv = new StringBuilder();
detailCsv.AppendLine("Nom;WhatsApp_Number;Adresse;Specialite;Zone;Note;Nb_avis;Site_web;Lien_GoogleMaps;Date_recensement;Source");
foreach (var p in ordered)
    detailCsv.AppendLine($"{Csv(p.Name)};{p.Phone};{Csv(p.Address)};{Csv(p.Specialite)};{Csv(p.Zone)};" +
                   $"{p.Rating:0.0};{p.RatingsTotal};{Csv(p.Website)};{Csv(p.MapsLink)};{p.Date};GooglePlaces");

var detPath = Path.Combine(outDir, $"Prospects_Abidjan_{ts}_detail.csv");
await File.WriteAllTextAsync(detPath, detailCsv.ToString(), new UTF8Encoding(true));

if (errors.Length > 0)
    await File.WriteAllTextAsync(Path.Combine(outDir, "prospect_collector_errors.log"), errors.ToString(), Encoding.UTF8);

await SaveStateAsync(statePath, state, known, results);

Console.WriteLine($"\nTerminé en {stopwatch.Elapsed.TotalMinutes:0.0} min — {results.Count} prospects uniques " +
                  $"(+225), {apiCalls} appels API ({apiMode}).");
Console.WriteLine($"→ {stdPath}\n→ {detPath}");
return 0;

// ---------- helpers ----------
async Task LoadPhonesFromFileAsync(string path, HashSet<string> target)
{
    try
    {
        var lines = await File.ReadAllLinesAsync(path);
        foreach (var raw in lines)
        {
            foreach (var token in raw.Split(';', ','))
            {
                var digits = new string(token.Where(char.IsDigit).ToArray());
                if (digits.Length >= 10 && digits.StartsWith("225"))
                {
                    target.Add("+225" + digits.Substring(3));
                    break;
                }
            }
        }
    }
    catch { /* fichier absent/verrouillé : ignoré */ }
}

async Task SaveStateAsync(string path, CollectorState st, HashSet<string> seen, Dictionary<string, Prospect> fresh)
{
    st.AllSeen ??= [];
    foreach (var ph in seen) st.AllSeen.Add(ph);
    foreach (var ph in fresh.Keys) st.AllSeen.Add(ph);
    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(st, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(tmp, path, overwrite: true);
}

string? NormalizeCI(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    var digits = new string(raw.Where(char.IsDigit).ToArray());
    if (digits.Length == 0) return null;
    var rest = digits.StartsWith("225") ? digits[3..] : digits;
    if (digits.StartsWith("225") && rest.Length is 8 or 10) return "+225" + rest;
    if (!digits.StartsWith("225") && rest.Length is 8 or 10) return "+225" + rest; // n° local → +225
    return null;
}

string Csv(string? v)
    => (v ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ").Trim();

string MapsLink(string? placeId)
    => placeId is null ? "" : $"https://www.google.com/maps/place/?q=place_id:{Uri.EscapeDataString(placeId)}";


// ---------- appels réseau ----------
async Task<PageOutcome> FetchPageAsync(HttpClient client, string key, string mode, string query,
    string? pageToken, StringBuilder errs)
{
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            if (mode == "legacy")
            {
                var url = "https://maps.googleapis.com/maps/api/place/textsearch/json" +
                          $"?query={Uri.EscapeDataString(query)}&key={key}" +
                          (pageToken is null ? "" : $"&pagetoken={Uri.EscapeDataString(pageToken)}");
                var body = await client.GetStringAsync(url);
                var resp = JsonSerializer.Deserialize<TextSearchResponse>(body);
                if (resp is null) return new PageOutcome("ERROR", null, "Réponse vide", null);

                if (resp.Status == "OK")
                {
                    var items = (resp.Results ?? []).Select(r => new Found
                    {
                        Id = r.PlaceId,
                        Name = r.Name,
                        Address = !string.IsNullOrWhiteSpace(r.FormattedAddress) ? r.FormattedAddress : r.Vicinity,
                        Rating = r.Rating ?? 0,
                        RatingCount = r.UserRatingsTotal ?? 0
                    }).ToList();
                    return new PageOutcome("OK", resp.NextPageToken, null, items);
                }
                return new PageOutcome(resp.Status ?? "ERROR", null, resp.ErrorMessage, null);
            }

            // --- Places API (New) : POST places:searchText ---
            var payload = new Dictionary<string, object?>
            {
                ["textQuery"] = query,
                ["pageSize"] = 20,
                ["languageCode"] = "fr",
                ["regionCode"] = "CI"
            };
            if (pageToken is not null) payload["pageToken"] = pageToken;

            var req = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchText")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Goog-Api-Key", key);
            req.Headers.Add("X-Goog-FieldMask",
                "places.id,places.displayName,places.formattedAddress,places.internationalPhoneNumber," +
                "places.websiteUri,places.googleMapsUri,places.rating,places.userRatingCount,nextPageToken");

            using var respMsg = await client.SendAsync(req);
            var text = await respMsg.Content.ReadAsStringAsync();
            var nresp = JsonSerializer.Deserialize<NewSearchResponse>(text);
            var err = nresp?.Error;
            if (err is not null)
            {
                var msg = err.Message ?? err.Status ?? "ERREUR";
                var code = err.Code ?? 0;
                if (code == 429 || code == 8 || msg.Contains("quota", StringComparison.OrdinalIgnoreCase))
                    return new PageOutcome("OVER_QUERY_LIMIT", pageToken, msg, null);
                if (!respMsg.IsSuccessStatusCode || code == 16 ||
                    msg.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("denied", StringComparison.OrdinalIgnoreCase))
                    return new PageOutcome("REQUEST_DENIED", null, msg, null);
                return new PageOutcome("ERROR", null, msg, null);
            }
            if (nresp is null) return new PageOutcome("ERROR", null, "Réponse vide", null);

            var list = (nresp.Places ?? []).Select(p => new Found
            {
                Id = p.Id,
                Name = p.DisplayName?.Text,
                Address = p.FormattedAddress,
                Phone = p.InternationalPhoneNumber,
                Website = p.WebsiteUri,
                MapsLink = p.GoogleMapsUri,
                Rating = p.Rating ?? 0,
                RatingCount = p.UserRatingCount ?? 0
            }).ToList();
            return new PageOutcome("OK", nresp.NextPageToken, null, list);
        }
        catch (Exception ex)
        {
            if (attempt == 3)
            {
                errs.AppendLine($"ERREUR requête {query}: {ex.Message}");
                return new PageOutcome("ERROR", null, ex.Message, null);
            }
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
        }
    }
    return new PageOutcome("ERROR", null, "Abandon", null);
}

async Task<DetailResult?> FetchLegacyDetailsAsync(HttpClient client, string key, string? placeId, StringBuilder errs)
{
    if (placeId is null) return null;
    try
    {
        var url = "https://maps.googleapis.com/maps/api/place/details/json" +
                  $"?place_id={Uri.EscapeDataString(placeId)}" +
                  "&fields=formatted_address,international_phone_number,website&key=" + key;
        var body = await client.GetStringAsync(url);
        var resp = JsonSerializer.Deserialize<PlaceDetailsResponse>(body);
        if (resp?.Status != "OK") return null;
        return new DetailResult(resp.Result?.FormattedAddress, resp.Result?.InternationalPhoneNumber,
            resp.Result?.Website, MapsLink(placeId));
    }
    catch (Exception ex)
    {
        errs.AppendLine($"ERREUR details {placeId}: {ex.Message}");
        return null;
    }
}


// ---------- records & modèles JSON ----------
record Prospect(string Name, string Address, string Phone, string Specialite, string Zone,
    double Rating, int RatingsTotal, string? Website, string MapsLink, string Date);

class CollectorState
{
    public List<string>? Completed { get; set; }
    public HashSet<string>? AllSeen { get; set; }
}

class PageOutcome
{
    public PageOutcome(string status, string? nextToken, string? errorMessage, List<Found>? items)
    {
        Status = status; NextToken = nextToken; ErrorMessage = errorMessage; Items = items ?? [];
    }
    public string Status { get; }
    public string? NextToken { get; }
    public string? ErrorMessage { get; }
    public List<Found> Items { get; }
}

class Found
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? MapsLink { get; set; }
    public double Rating { get; set; }
    public int RatingCount { get; set; }
}

record DetailResult(string? Address, string? Phone, string? Website, string? MapsLink);

// ----- Google Places legacy -----
class TextSearchResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
    [JsonPropertyName("results")] public List<PlaceSummary>? Results { get; set; }
}

class PlaceSummary
{
    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("formatted_address")] public string? FormattedAddress { get; set; }
    [JsonPropertyName("vicinity")] public string? Vicinity { get; set; }
    [JsonPropertyName("rating")] public double? Rating { get; set; }
    [JsonPropertyName("user_ratings_total")] public int? UserRatingsTotal { get; set; }
}

class PlaceDetailsResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("result")] public PlaceDetails? Result { get; set; }
}

class PlaceDetails
{
    [JsonPropertyName("formatted_address")] public string? FormattedAddress { get; set; }
    [JsonPropertyName("international_phone_number")] public string? InternationalPhoneNumber { get; set; }
    [JsonPropertyName("website")] public string? Website { get; set; }
}

// ----- Places API (New) -----
class NewSearchResponse
{
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    [JsonPropertyName("places")] public List<NewPlace>? Places { get; set; }
    [JsonPropertyName("error")] public GoogleApiError? Error { get; set; }
}

class GoogleApiError
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

class NewPlace
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("displayName")] public NewDisplayName? DisplayName { get; set; }
    [JsonPropertyName("formattedAddress")] public string? FormattedAddress { get; set; }
    [JsonPropertyName("internationalPhoneNumber")] public string? InternationalPhoneNumber { get; set; }
    [JsonPropertyName("websiteUri")] public string? WebsiteUri { get; set; }
    [JsonPropertyName("googleMapsUri")] public string? GoogleMapsUri { get; set; }
    [JsonPropertyName("rating")] public double? Rating { get; set; }
    [JsonPropertyName("userRatingCount")] public int? UserRatingCount { get; set; }
}

class NewDisplayName
{
    [JsonPropertyName("text")] public string? Text { get; set; }
}

