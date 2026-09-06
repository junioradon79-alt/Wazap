using Npgsql;
using System.Text.Json;

var connStr = Environment.GetEnvironmentVariable("WAZAP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("WAZAP_CONNECTION_STRING manquante.");
    return 1;
}

var confirm = args.Contains("--confirm");
var backupDir = @"c:\Dev\Wazap\backups";
Directory.CreateDirectory(backupDir);
var backupPath = Path.Combine(backupDir, $"purge_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");

var tables = new[] { "DeliveryOffers", "OutboxMessages", "CreditTransactions", "Orders", "DeliveryBatches", "RefreshTokens", "Users" };
var backup = new Dictionary<string, object?>();

// ---- 1. Sauvegarde (toujours, même en dry-run) ----
await using (var conn = new NpgsqlConnection(connStr))
{
    await conn.OpenAsync();
    foreach (var table in tables)
    {
        await using var cmd = new NpgsqlCommand($"SELECT * FROM \"{table}\"", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        backup[table] = rows;
    }
}
await File.WriteAllTextAsync(backupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

var count = (string t) => ((List<Dictionary<string, object?>>)backup[t]).Count;
Console.WriteLine($"Sauvegarde écrite : {backupPath}");
Console.WriteLine($"  Users={count("Users")} | Orders={count("Orders")} | CreditTransactions={count("CreditTransactions")} | DeliveryOffers={count("DeliveryOffers")} | OutboxMessages={count("OutboxMessages")}");

if (!confirm)
{
    Console.WriteLine("Dry-run : rien n'a été supprimé. Relancez avec --confirm pour purger.");
    return 0;
}

// ---- 2. Purge (ordre respectant les FK) ----
await using (var conn = new NpgsqlConnection(connStr))
{
    await conn.OpenAsync();
    foreach (var (name, sql) in new[]
    {
        ("DeliveryOffers", "DELETE FROM \"DeliveryOffers\""),
        ("OutboxMessages", "DELETE FROM \"OutboxMessages\""),
        ("CreditTransactions", "DELETE FROM \"CreditTransactions\""),
        ("Orders", "DELETE FROM \"Orders\""),
        ("DeliveryBatches", "DELETE FROM \"DeliveryBatches\""),
        ("RefreshTokens", "DELETE FROM \"RefreshTokens\""),
    })
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        Console.WriteLine($"Purge {name} : {await cmd.ExecuteNonQueryAsync()} ligne(s) supprimée(s).");
    }

    await using var update = new NpgsqlCommand("UPDATE \"Users\" SET \"Credits\" = 0", conn);
    Console.WriteLine($"Crédits remis à 0 : {await update.ExecuteNonQueryAsync()} utilisateur(s).");
}

Console.WriteLine("Purge terminée.");
return 0;
