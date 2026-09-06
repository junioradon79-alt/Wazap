// Score les prospects d'après leurs interactions WhatsApp (export manuel ou webhook).
// Entrée : Prospects.csv (séparateur ';') — colonnes du modèle + Repondu;Clic_video;Demande_demo;Pas_interesse (oui/non)
// Sortie : Prospects_scored.csv avec Score (/100) et Priorite (Chaud/Tiède/Froid)
using System.Globalization;
using System.Text;

var input = args.Length > 0 ? args[0] : "Prospects.csv";
if (!File.Exists(input))
{
    Console.Error.WriteLine($"Fichier introuvable : {input}");
    return 1;
}

var lines = File.ReadAllLines(input, Encoding.UTF8);
if (lines.Length < 2)
{
    Console.Error.WriteLine("CSV vide ou incomplet.");
    return 1;
}

var header = lines[0].Split(';');
var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < header.Length; i++) idx[header[i].Trim()] = i;

int Col(string name) => idx.TryGetValue(name, out var i) ? i : -1;
string Val(string[] row, string name) => Col(name) >= 0 && Col(name) < row.Length ? row[Col(name)].Trim() : "";
bool Oui(string v) => v.Equals("oui", StringComparison.OrdinalIgnoreCase) || v == "1" || v == "true";

var output = new StringBuilder();
output.AppendLine("Nom;WhatsApp_Number;Score;Priorite");

foreach (var line in lines.Skip(1))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    var row = line.Split(';');

    var nom = Val(row, "Nom");
    var phone = Val(row, "WhatsApp_Number");
    var repondu = Oui(Val(row, "Repondu"));
    var clic = Oui(Val(row, "Clic_video"));
    var demo = Oui(Val(row, "Demande_demo"));
    var pasInteresse = Oui(Val(row, "Pas_interesse"));

    var score = 0;
    if (repondu) score += 30;
    if (clic) score += 15;
    if (demo) score += 40;
    if (clic && !repondu) score += 15;      // bon signal : a cliqué sans répondre
    if (pasInteresse) score -= 25;          // pas intéressé

    score = Math.Clamp(score, 0, 100);
    var priorite = score >= 50 ? "Chaud" : score >= 20 ? "Tiède" : "Froid";

    output.AppendLine($"{nom};{phone};{score};{priorite}");
}

var outputPath = "Prospects_scored.csv";
File.WriteAllText(outputPath, output.ToString(), Encoding.UTF8);
Console.WriteLine($"Terminé : {outputPath}");
Console.WriteLine(output.ToString());
return 0;
