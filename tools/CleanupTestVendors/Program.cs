using Npgsql;

var connStr = Environment.GetEnvironmentVariable("WAZAP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("WAZAP_CONNECTION_STRING manquante.");
    return 1;
}

// Convention des outils WAZAP (cf. PurgeTestData) : dry-run par défaut, suppression sur --confirm.
// Ciblage INSENSIBLE à la casse (ILIKE) : attrape test_%, TestRider01, TestVendor01, vendeur_test%...
// mais PAS les comptes de démonstration (Pizzeria, Karim Diallo, admin...).
var confirm = args.Contains("--confirm");
var force = args.Contains("--force");
const string testFilter = "\"Username\" ILIKE 'test%' OR \"Username\" ILIKE '%\\_test%'";

// Tables dépendantes à purger AVANT les comptes (contraintes FK), dans l'ordre de dépendance.
var linkedTables = new (string Label, string Sql)[]
{
    ("RefreshTokens (UserId)", "DELETE FROM \"RefreshTokens\" WHERE \"UserId\" = ANY(@ids)"),
    ("RiderIdentities (UserId)", "DELETE FROM \"RiderIdentities\" WHERE \"UserId\" = ANY(@ids)"),
    ("VendorProducts (VendorId)", "DELETE FROM \"VendorProducts\" WHERE \"VendorId\" = ANY(@ids)"),
    ("CreditTransactions (VendorId)", "DELETE FROM \"CreditTransactions\" WHERE \"VendorId\" = ANY(@ids)"),
    ("RiderRatings (RiderUserId)", "DELETE FROM \"RiderRatings\" WHERE \"RiderUserId\" = ANY(@ids)"),
    ("DeliveryClaims (RiderUserId)", "DELETE FROM \"DeliveryClaims\" WHERE \"RiderUserId\" = ANY(@ids)"),
    ("DeliveryOffers (RiderUserId)", "DELETE FROM \"DeliveryOffers\" WHERE \"RiderUserId\" = ANY(@ids)"),
    ("DeliveryBatches (RiderUserId)", "DELETE FROM \"DeliveryBatches\" WHERE \"RiderUserId\" = ANY(@ids)")
};

static string ToCount(string deleteSql) => deleteSql.Replace("DELETE FROM", "SELECT COUNT(*) FROM");

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

// ---- 1. Inventaire des comptes de test (toujours, y compris en dry-run) ----
var ids = new List<Guid>();
Console.WriteLine("Comptes de test ciblés (Username ILIKE 'test%' OR '%_test%') :");
await using (var list = new NpgsqlCommand(
    $"SELECT \"Id\", \"Username\", \"Role\", \"Credits\", \"PhoneNumber\" FROM \"Users\" WHERE {testFilter} ORDER BY \"Username\"",
    conn))
await using (var reader = await list.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        ids.Add(reader.GetGuid(0));
        var username = reader.GetString(1);
        var role = reader.IsDBNull(2) ? "?" : reader.GetValue(2).ToString();
        var credits = reader.IsDBNull(3) ? "-" : reader.GetValue(3)?.ToString();
        var phone = reader.IsDBNull(4) ? "-" : reader.GetString(4);
        Console.WriteLine($"  - {username} (rôle={role}, crédits={credits}, téléphone={phone})");
    }

    if (ids.Count == 0)
        Console.WriteLine("  (aucun)");
    Console.WriteLine($"Total : {ids.Count} compte(s).");
}

// ---- 2. Lignes liées (audit des contraintes FK) ----
Console.WriteLine("Lignes liées (purgées AVEC les comptes en mode --confirm) :");
foreach (var (label, deleteSql) in linkedTables)
{
    await using var countCmd = new NpgsqlCommand(ToCount(deleteSql), conn);
    countCmd.Parameters.AddWithValue("ids", ids.ToArray());
    Console.WriteLine($"  {label} : {await countCmd.ExecuteScalarAsync()}");
}

await using (var ordersCount = new NpgsqlCommand(
    "SELECT COUNT(*) FROM \"Orders\" WHERE \"VendorUserId\" = ANY(@ids) OR \"RiderUserId\" = ANY(@ids)", conn))
{
    ordersCount.Parameters.AddWithValue("ids", ids.ToArray());
    Console.WriteLine($"  Orders (VendorUserId/RiderUserId) : {await ordersCount.ExecuteScalarAsync()}");
}

await using (var pizzeria = new NpgsqlCommand(
    "SELECT \"Credits\" FROM \"Users\" WHERE \"Username\" = 'Pizzeria Bella Napoli'", conn))
{
    var credits = await pizzeria.ExecuteScalarAsync();
    Console.WriteLine(credits is null
        ? "Vendeur démo 'Pizzeria Bella Napoli' : absent."
        : $"Vendeur démo 'Pizzeria Bella Napoli' : crédits = {credits} (remis à 0 en mode --confirm).");
}

if (!confirm)
{
    Console.WriteLine("Dry-run : rien n'a été supprimé. Relancez avec --confirm pour supprimer.");
    return 0;
}

if (ids.Count == 0)
{
    Console.WriteLine("Aucun compte de test à supprimer.");
    return 0;
}

// ---- 3. Garde-fou : commandes liées à ces comptes (FK Orders) ----
await using (var ordersGuard = new NpgsqlCommand(
    "SELECT COUNT(*) FROM \"Orders\" WHERE \"VendorUserId\" = ANY(@ids) OR \"RiderUserId\" = ANY(@ids)", conn))
{
    ordersGuard.Parameters.AddWithValue("ids", ids.ToArray());
    var linkedOrders = Convert.ToInt64(await ordersGuard.ExecuteScalarAsync());
    if (linkedOrders > 0)
    {
        Console.Error.WriteLine($"ATTENTION : {linkedOrders} commande(s) référencent ces comptes.");
        if (!force)
        {
            Console.Error.WriteLine("Purge annulée. Lancez d'abord PurgeTestData --confirm, ou relancez avec --force.");
            return 2;
        }
    }
}

// ---- 4. Suppression des lignes liées, puis des comptes ----
foreach (var (label, deleteSql) in linkedTables)
{
    await using var delLinked = new NpgsqlCommand(deleteSql, conn);
    delLinked.Parameters.AddWithValue("ids", ids.ToArray());
    Console.WriteLine($"Suppression {label} : {await delLinked.ExecuteNonQueryAsync()} ligne(s).");
}

await using var delUsers = new NpgsqlCommand(
    $"DELETE FROM \"Users\" WHERE {testFilter}", conn);
Console.WriteLine($"Comptes de test supprimés : {await delUsers.ExecuteNonQueryAsync()}");

await using var reset = new NpgsqlCommand(
    "UPDATE \"Users\" SET \"Credits\" = 0 WHERE \"Username\" = 'Pizzeria Bella Napoli'", conn);
Console.WriteLine($"Crédits Pizzeria remis à 0 : {await reset.ExecuteNonQueryAsync()}");

Console.WriteLine("Nettoyage terminé.");
return 0;
