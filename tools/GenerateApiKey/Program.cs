// Générateur de clé API partenaire (API publique v1).
// Usage : dotnet run --project tools/GenerateApiKey
// Puis renseigner en production : PublicApi__Keys__0=<clé>
using System.Security.Cryptography;

byte[] bytes = new byte[32];
RandomNumberGenerator.Fill(bytes);

string hex = Convert.ToHexString(bytes);
string base64Url = Convert.ToBase64String(bytes)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

Console.WriteLine("=== WAZAP — Clé API partenaire ===");
Console.WriteLine();
Console.WriteLine("hex       : " + hex);
Console.WriteLine("base64url : " + base64Url);
Console.WriteLine();
Console.WriteLine("À configurer en prod (variable d'environnement) :");
Console.WriteLine("  PublicApi__Keys__0=" + base64Url);
Console.WriteLine();
Console.WriteLine("Conservez-la précieusement : elle ne sera plus affichée.");
