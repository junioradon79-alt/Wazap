using Npgsql;
using Wazap.Infrastructure.Services;

// Le mot de passe ne doit JAMAIS passer par la ligne de commande : `argv` est visible dans
// l'historique PowerShell (ConsoleHost_history.txt), dans la liste des processus et dans les
// journaux de commande. Ordre de résolution : variable d'environnement
// WAZAP_ADMIN_PASSWORD, sinon saisie masquée au clavier.
var password = Environment.GetEnvironmentVariable("WAZAP_ADMIN_PASSWORD");

if (string.IsNullOrWhiteSpace(password))
{
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine(
            "Mot de passe requis. Définissez WAZAP_ADMIN_PASSWORD, ou lancez la commande "
            + "dans un terminal interactif (saisie masquée).");
        return 1;
    }

    password = ReadPasswordMasked("Nouveau mot de passe admin : ");
    var confirmation = ReadPasswordMasked("Confirmer : ");
    if (!string.Equals(password, confirmation, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Les deux saisies diffèrent — aucune modification.");
        return 1;
    }
}

if (password.Length < 8)
{
    Console.Error.WriteLine("Le mot de passe doit contenir au moins 8 caractères.");
    return 1;
}

var connStr = Environment.GetEnvironmentVariable("WAZAP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("WAZAP_CONNECTION_STRING manquante.");
    return 1;
}

var hasher = new PasswordHasher();
var hash = hasher.Hash(password);

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();
await using var cmd = new NpgsqlCommand(
    "UPDATE \"Users\" SET \"PasswordHash\" = @h WHERE \"Username\" = 'admin'", conn);
cmd.Parameters.AddWithValue("h", hash);
var rows = await cmd.ExecuteNonQueryAsync();

// Aucun affichage du hash : il était écrit sur la sortie standard, donc recopié dans tout
// transcript ou fichier de log redirigé (et réutilisable hors ligne pour du craquage).
Console.WriteLine($"Rows updated: {rows}");
return rows > 0 ? 0 : 1;

static string ReadPasswordMasked(string prompt)
{
    Console.Write(prompt);
    var buffer = new System.Text.StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return buffer.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Length > 0)
                buffer.Length--;
            continue;
        }

        if (!char.IsControl(key.KeyChar))
            buffer.Append(key.KeyChar);
    }
}
