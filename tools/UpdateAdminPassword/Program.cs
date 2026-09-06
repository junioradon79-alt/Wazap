using Npgsql;
using Wazap.Infrastructure.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: UpdateAdminPassword <password>");
    return 1;
}

var connStr = Environment.GetEnvironmentVariable("WAZAP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("WAZAP_CONNECTION_STRING manquante.");
    return 1;
}

var hasher = new PasswordHasher();
var hash = hasher.Hash(args[0]);

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();
await using var cmd = new NpgsqlCommand(
    "UPDATE \"Users\" SET \"PasswordHash\" = @h WHERE \"Username\" = 'admin'", conn);
cmd.Parameters.AddWithValue("h", hash);
var rows = await cmd.ExecuteNonQueryAsync();

Console.WriteLine($"Rows updated: {rows}");
Console.WriteLine($"New hash: {hash}");
return rows > 0 ? 0 : 1;
