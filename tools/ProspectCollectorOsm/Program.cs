// =============================================================
// WAZAP — Collecteur de prospects OpenStreetMap (Grand Abidjan)
// Source 100 % libre : API Overpass (aucune clé, aucun compte bancaire).
// Complète le collecteur Google Places quand aucune clé n'est possible.
//
// Usage :
//   dotnet run --project tools\ProspectCollectorOsm
//   dotnet run --project tools\ProspectCollectorOsm -- --zone=Marcory --types=restaurant,bar
//
// Options :
//   --zone=<nom>         une seule commune (défaut : 13 communes du Grand Abidjan)
//   --types=<a,b>        secteurs restreints (défaut : 33 secteurs livrant — restauration,
//                        alimentation, santé/beauté, maison, mode, services…)
//   --exclude=<fichier>  CSV des numéros déjà connus
//   --out-dir=<dossier>  dossier de sortie (défaut : cwd)
//   --timeout=<secondes> timeout HTTP client (défaut : 120 — la requête Overpass demande [timeout:120])
//   --reset              ignore l'état de reprise
//
// Sorties (même format que le collecteur Google, préfixe _osm) :
//   Prospects_Abidjan_osm_<ts>.csv        schéma standard
//   Prospects_Abidjan_osm_<ts>_detail.csv enrichi
//   prospect_collector_osm_errors.log
//   prospect_osm_state.json
// =============================================================
using System.Text;
using System.Text.Json;

var zones = new[] { "Cocody", "Marcory", "Yopougon", "Adjamé", "Treichville", "Plateau",
    "Abobo", "Koumassi", "Port-Bouët", "Bingerville", "Songon", "Attécoubé", "Anyama" };

string? Arg(string prefix) => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    ?.Substring(prefix.Length);

var zoneFilter = Arg("--zone=");
var excludeFile = Arg("--exclude=");
var outDir = Arg("--out-dir=") ?? Directory.GetCurrentDirectory();
var timeoutSec = int.TryParse(Arg("--timeout="), out var t) && t > 10 ? t : 120;
var reset = args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));
if (zoneFilter is not null) zones = [zoneFilter];

// --- Secteurs cibles WAZAP : tout commerce qui livre (restauration, alimentation,
// santé/beauté, maison, mode, services...). Chaque secteur est mappé vers des tags
// OpenStreetMap (amenity et/ou shop). Overpass ne remonte que les nœuds/chemins avec téléphone.
var sectors = new (string Label, string[] Amenity, string[] Shop)[]
{
    ("restaurant",      ["restaurant"],                                []),
    ("fast food",       ["fast_food"],                                  []),
    ("snack",           ["fast_food"],                                  []),
    ("bar",             ["bar", "pub"],                                 []),
    ("café",            ["cafe"],                                       []),
    ("traiteur",        ["caterer"],                                    []),
    ("boulangerie",     [],                                             ["bakery"]),
    ("pâtisserie",      [],                                             ["pastry"]),
    ("supermarché",     [],                                             ["supermarket", "department_store"]),
    ("épicerie",        [],                                             ["convenience", "general", "kiosk", "variety_store", "deli"]),
    ("boucherie",       [],                                             ["butcher"]),
    ("poissonnerie",    [],                                             ["seafood"]),
    ("primeur",         [],                                             ["greengrocer"]),
    ("crèmerie",        [],                                             ["cheese", "dairy"]),
    ("pharmacie",       ["pharmacy"],                                   []),
    ("parapharmacie",   [],                                             ["chemist", "cosmetics", "perfumery", "beauty"]),
    ("produits bio",    [],                                             ["health_food", "herbalist"]),
    ("optique",         [],                                             ["optician"]),
    ("vêtements",       [],                                             ["clothes", "fashion"]),
    ("chaussures",      [],                                             ["shoes"]),
    ("électroménager",  [],                                             ["electronics", "household_appliance"]),
    ("téléphonie",      [],                                             ["mobile_phone"]),
    ("informatique",    [],                                             ["computer"]),
    ("fleuriste",       [],                                             ["florist"]),
    ("quincaillerie",   [],                                             ["hardware", "doityourself"]),
    ("matériaux",       [],                                             ["trade"]),
    ("meubles & déco",  [],                                             ["furniture", "interior_decoration"]),
    ("librairie",       [],                                             ["books", "stationery"]),
    ("animalerie",      [],                                             ["pet"]),
    ("sport",           [],                                             ["sports"]),
    ("jouets",          [],                                             ["toys"]),
    ("bijouterie",      [],                                             ["jewelry"]),
    ("boissons",        [],                                             ["alcohol", "beverages", "wine"])
};

var defaultTypes = sectors.Select(s => s.Label).ToArray();
var types = Arg("--types=")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(t => t.ToLowerInvariant()).ToArray() ?? defaultTypes;

Directory.CreateDirectory(outDir);
var statePath = Path.Combine(outDir, "prospect_osm_state.json");

// --- état / numéros déjà vus (fusion automatique Google + OSM via les CSV du dossier) ---
var state = new OsmState();
if (!reset && File.Exists(statePath))
{
    try { state = JsonSerializer.Deserialize<OsmState>(await File.ReadAllTextAsync(statePath)) ?? state; }
    catch { }
}

var known = new HashSet<string>(state.AllSeen ?? [], StringComparer.Ordinal);
foreach (var old in Directory.GetFiles(outDir, "Prospects_Abidjan_*.csv"))
    await LoadPhonesAsync(old, known);
if (excludeFile is not null)
    await LoadPhonesAsync(excludeFile, known);
Console.WriteLine($"Exclusions chargées : {known.Count} numéros déjà vus. Source : OpenStreetMap (Overpass).");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };
http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "wazap-prospecting-collector/1.0");

// Miroirs publics Overpass. Ordre réajusté dynamiquement par la sonde : les miroirs qui
// répondent correctement passent en tête ; les injoignables/occupés restent en secours.
var endpoints = new List<string>
{
    "https://overpass.osm.ch/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass-api.de/api/interpreter"
};

Console.WriteLine("Sonde de disponibilité des miroirs Overpass…");
var probeResults = await Task.WhenAll(endpoints.Select(async ep =>
{
    using var pc = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    pc.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "wazap-prospecting-collector/1.0");
    return (Ep: ep, Ok: await ProbeEndpointAsync(pc, ep));
}));
foreach (var p in probeResults)
    Console.WriteLine($"  {(p.Ok ? "✅ opérationnel" : "❌ injoignable/occupé")} {p.Ep}");
var okEndpoints = probeResults.Where(p => p.Ok).Select(p => p.Ep).ToList();
if (okEndpoints.Count == 0)
{
    Console.WriteLine("Aucun miroir Overpass ne répond correctement pour l'instant.");
    Console.WriteLine("Réessaie plus tard : dotnet run --project tools\\ProspectCollectorOsm -- --out-dir=prospection\\out_osm");
    return 2;
}
endpoints = okEndpoints.Concat(endpoints.Where(ep => !okEndpoints.Contains(ep))).ToList();

var results = new Dictionary<string, OsmProspect>(StringComparer.Ordinal);
var errors = new StringBuilder();
var calls = 0;
var done = 0;
var total = zones.Length;

// --- requête Overpass : une par commune, tous les types demandés en une seule passe ---
var wanted = types
    .Select(t => sectors.FirstOrDefault(s => s.Label.Equals(t, StringComparison.OrdinalIgnoreCase)))
    .Where(s => s.Label is not null)
    .ToList();
var unknown = types.Where(t => !wanted.Any(s => s.Label.Equals(t, StringComparison.OrdinalIgnoreCase))).ToList();
foreach (var u in unknown)
    Console.WriteLine($"  ⚠️  Type inconnu ignoré : {u}");

var amenityValues = wanted.SelectMany(s => s.Amenity).Distinct().ToArray();
var shopValues = wanted.SelectMany(s => s.Shop).Distinct().ToArray();

// Découpage en « bundles » de sélecteurs : Overpass public timeout si la requête (regex de
// tags) est trop longue → une petite requête par bundle (amenity en 1 lot, shop par 10).
var bundles = new List<string>();
if (amenityValues.Length > 0)
{
    var rx = $"^({string.Join("|", amenityValues.Select(Escape))})$";
    bundles.Add($"node[\"amenity\"~\"{rx}\"]");
    bundles.Add($"way[\"amenity\"~\"{rx}\"]");
}
foreach (var chunk in shopValues.Chunk(10))
{
    var rx = $"^({string.Join("|", chunk.Select(Escape))})$";
    bundles.Add($"node[\"shop\"~\"{rx}\"]");
    bundles.Add($"way[\"shop\"~\"{rx}\"]");
}
Console.WriteLine($"Types demandés : {string.Join(", ", types)} ({amenityValues.Length} amenity / {shopValues.Length} shop OSM → {bundles.Count} requêtes/commune)");

// Horodatage du fichier de sortie unique du run (écriture incrémentale après chaque zone).
var ts = DateTime.Now.ToString("yyyyMMdd_HHmm");
string stdPath = "", detPath = "";

foreach (var zone in zones)
{
    done++;
    if (!reset && state.Completed?.Contains(zone) == true)
    {
        Console.WriteLine($"[{done}/{total}] SKIP {zone} (déjà terminé)");
        continue;
    }

    var zoneOk = true;
    var zoneCount = 0;
    foreach (var sel in bundles)
    {
        var q = new StringBuilder();
        q.Append("[out:json][timeout:120];");
        q.Append($"area[\"boundary\"=\"administrative\"][\"name\"~\"^{Escape(zone)}$\",i]->.a;");
        q.Append($"({sel}(area.a););out center;");

        try
        {
            using var doc = await PostWithRetryAsync(http, q.ToString(), errors, zone);
            calls++;
            var root = doc.RootElement;

            if (root.TryGetProperty("elements", out var elements))
            {
                foreach (var el in elements.EnumerateArray())
                {
                    if (!el.TryGetProperty("tags", out var tags)) continue;
                    var name = GetTag(tags, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var phoneRaw = GetTag(tags, "phone") ?? GetTag(tags, "contact:phone") ?? GetTag(tags, "contact:mobile");
                    var phone = NormalizeCI(phoneRaw);
                    if (phone is null || known.Contains(phone)) continue;

                    var cat = Classify(tags, types);
                    if (cat is null) continue;

                    var address = JoinAddr(GetTag(tags, "addr:housenumber"), GetTag(tags, "addr:street"), GetTag(tags, "addr:city"));
                    var website = GetTag(tags, "website") ?? GetTag(tags, "contact:website");
                    var mapsUrl = $"https://www.openstreetmap.org/?mlat={GetCoord(el, "lat"):0.00000}&mlon={GetCoord(el, "lon"):0.00000}#map=18";

                    results.TryAdd(phone, new OsmProspect(name, address, phone, cat, zone, website, mapsUrl,
                        DateTime.Now.ToString("yyyy-MM-dd")));
                    zoneCount++;
                }
            }
        }
        catch (Exception ex)
        {
            zoneOk = false;
            errors.AppendLine($"[{zone}] ERREUR : {ex.Message}");
        }
    }

    if (zoneCount == 0)
        errors.AppendLine($"[{zone}] 0 élément (commune non trouvée dans OSM ou peu cartographiée).");

    if (zoneOk)
    {
        state.Completed ??= [];
        state.Completed!.Add(zone);
    }

    Console.WriteLine($"[{done}/{total}] {zone} → {results.Count} uniques cumulés ({zoneCount} dans la commune, {calls} requêtes)");

    await SaveStateAsync(statePath, state, known, results);
    WriteOutputs(); // écriture incrémentale : pas de perte en cas d'interruption
    await Task.Delay(TimeSpan.FromSeconds(8)); // politesse Overpass
}


// ---------- écritures de sortie (format identique au collecteur Google) ----------
void WriteOutputs()
{
    var ordered = results.Values.OrderBy(p => p.Zone).ThenBy(p => p.Name).ToList();
    foreach (var p in ordered) known.Add(p.Phone);

    var std = new StringBuilder();
    std.AppendLine("Nom;WhatsApp_Number;Nom_Rue;Specialite;Zone;Source");
    foreach (var p in ordered)
        std.AppendLine($"{Csv(p.Name)};{p.Phone};{Csv(p.Address)};{Csv(p.Specialite)};{Csv(p.Zone)};OpenStreetMap");

    stdPath = Path.Combine(outDir, $"Prospects_Abidjan_osm_{ts}.csv");
    File.WriteAllText(stdPath, std.ToString(), new UTF8Encoding(true));

    var det = new StringBuilder();
    det.AppendLine("Nom;WhatsApp_Number;Adresse;Specialite;Zone;Note;Nb_avis;Site_web;Lien_GoogleMaps;Date_recensement;Source");
    foreach (var p in ordered)
        det.AppendLine($"{Csv(p.Name)};{p.Phone};{Csv(p.Address)};{Csv(p.Specialite)};{Csv(p.Zone)};" +
                       $"0;0;{Csv(p.Website)};{Csv(p.MapsLink)};{p.Date};OpenStreetMap");

    detPath = Path.Combine(outDir, $"Prospects_Abidjan_osm_{ts}_detail.csv");
    File.WriteAllText(detPath, det.ToString(), new UTF8Encoding(true));

    if (errors.Length > 0)
        File.WriteAllText(Path.Combine(outDir, "prospect_collector_osm_errors.log"), errors.ToString(), Encoding.UTF8);
}

WriteOutputs();
Console.WriteLine($"\nTerminé — {results.Count} prospects uniques OpenStreetMap (+225).");
Console.WriteLine($"→ {stdPath}\n→ {detPath}");
return 0;

// ---------- helpers ----------
async Task<bool> ProbeEndpointAsync(HttpClient client, string ep)
{
    // Sonde : une vraie réponse JSON avec « elements » ⇒ le miroir est utilisable.
    const string probe = "[out:json][timeout:5];node(around:150,5.3167,-4.0167)[amenity=cafe];out count;";
    try
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = probe });
        using var resp = await client.PostAsync(ep, content);
        if (!resp.IsSuccessStatusCode) return false;
        var body = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return false;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("elements", out _);
    }
    catch
    {
        return false;
    }
}

async Task<JsonDocument> PostWithRetryAsync(HttpClient client, string query, StringBuilder errs, string zone)
{
    // On essaie d'abord les miroirs validés par la sonde. Une réponse « serveur occupé /
    // query timed out » (corps non-JSON ou champ remark, HTTP 200) est un échec réessayable —
    // jamais un succès vide : sinon la zone serait marquée « terminée » sans données.
    Exception? last = null;
    for (var e = 0; e < endpoints.Count; e++)
    {
        // Un miroir « vivant » (qui a répondu au moins une fois) mérite une 2e tentative ;
        // un miroir muet (connexion qui timeout) est laissé après la 1re pour ne pas gâcher
        // timeoutSec × 2 sur chaque bundle.
        var serverAlive = false;
        for (var attempt = 1; attempt <= 2 && (attempt == 1 || serverAlive); attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
                using var resp = await client.PostAsync(endpoints[e], content);
                serverAlive = true;
                var status = (int)resp.StatusCode;
                if (status is 429 or 502 or 503 or 504)
                {
                    errs.AppendLine($"[{zone}] {endpoints[e]} HTTP {status} (essai {attempt}/2, endpoint {e + 1}/{endpoints.Count}).");
                    await Task.Delay(TimeSpan.FromSeconds(10 * attempt));
                    continue;
                }
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {status}");

                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body))
                    throw new HttpRequestException("Réponse vide");

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(body);
                }
                catch (JsonException)
                {
                    throw new HttpRequestException("Réponse non-JSON (serveur occupé / erreur Overpass)");
                }

                if (doc.RootElement.TryGetProperty("remark", out var remark))
                {
                    errs.AppendLine($"[{zone}] {endpoints[e]} remarque Overpass : {remark.GetString()} (essai {attempt}/2)");
                    doc.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(10 * attempt));
                    continue;
                }
                return doc;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                errs.AppendLine($"[{zone}] {endpoints[e]} échec (essai {attempt}/2) : {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
    throw new HttpRequestException($"Tous les endpoints Overpass ont échoué. Dernière erreur : {last?.Message}");
}

async Task LoadPhonesAsync(string path, HashSet<string> target)
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
    catch { }
}

async Task SaveStateAsync(string path, OsmState st, HashSet<string> seen, Dictionary<string, OsmProspect> fresh)
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
    if (rest.Length is 8 or 10) return "+225" + rest;
    return null;
}

string? GetTag(JsonElement tags, string key)
    => tags.TryGetProperty(key, out var v) ? v.GetString() : null;

string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

string JoinAddr(string? h, string? street, string? city)
    => string.Join(", ", new[] { h, street, city }.Where(x => !string.IsNullOrWhiteSpace(x)));

double GetCoord(JsonElement el, string key)
{
    if (el.TryGetProperty("center", out var c) && c.TryGetProperty(key, out var cv))
        return cv.GetDouble();
    return el.TryGetProperty(key, out var v) ? v.GetDouble() : 0;
}

string Csv(string? v)
    => (v ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ").Trim();

// Classifie un commerce selon les secteurs demandés (ordre = priorité).
string? Classify(JsonElement tags, string[] wanted)
{
    var tv = GetTag(tags, "amenity");
    var sv = GetTag(tags, "shop");
    foreach (var w in wanted)
    {
        var sec = sectors.FirstOrDefault(s => s.Label.Equals(w, StringComparison.OrdinalIgnoreCase));
        if (sec.Label is null) continue;
        if (tv is not null && sec.Amenity.Contains(tv)) return sec.Label;
        if (sv is not null && sec.Shop.Contains(sv)) return sec.Label;
    }
    return null;
}

record OsmProspect(string Name, string Address, string Phone, string Specialite, string Zone,
    string? Website, string MapsLink, string Date);

class OsmState
{
    public List<string>? Completed { get; set; }
    public HashSet<string>? AllSeen { get; set; }
}

