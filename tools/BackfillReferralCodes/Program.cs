using Npgsql;

var connStr = Environment.GetEnvironmentVariable("WAZAP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("WAZAP_CONNECTION_STRING manquante.");
    return 1;
}

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

var ids = new List<Guid>();
await using (var select = new NpgsqlCommand("SELECT \"Id\" FROM \"Users\" WHERE \"ReferralCode\" IS NULL", conn))
{
    await using var reader = await select.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        ids.Add(reader.GetGuid(0));
}

var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
await using (var select = new NpgsqlCommand("SELECT \"ReferralCode\" FROM \"Users\" WHERE \"ReferralCode\" IS NOT NULL", conn))
{
    await using var reader = await select.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        existing.Add(reader.GetString(0));
}

const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
var updated = 0;
foreach (var id in ids)
{
    string code;
    do
    {
        var c = new char[4];
        for (var i = 0; i < c.Length; i++)
            c[i] = chars[Random.Shared.Next(chars.Length)];
        code = "WA-" + new string(c);
    } while (!existing.Add(code));

    await using var update = new NpgsqlCommand("UPDATE \"Users\" SET \"ReferralCode\" = @c WHERE \"Id\" = @id", conn);
    update.Parameters.AddWithValue("c", code);
    update.Parameters.AddWithValue("id", id);
    await update.ExecuteNonQueryAsync();
    updated++;
}

Console.WriteLine($"Codes de parrainage attribués : {updated}/{ids.Count} utilisateur(s).");
return 0;
