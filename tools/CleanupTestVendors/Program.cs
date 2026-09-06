using Npgsql;

var connStr = Environment.GetEnvironmentVariable("WAZAP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("WAZAP_CONNECTION_STRING manquante.");
    return 1;
}

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

// Supprime d'abord les transactions d'octroi des comptes de test (contrainte FK Restrict).
await using var delTx = new NpgsqlCommand(
    "DELETE FROM \"CreditTransactions\" WHERE \"VendorId\" IN " +
    "(SELECT \"Id\" FROM \"Users\" WHERE \"Username\" LIKE 'vendeur_test%' OR \"Username\" LIKE 'test_%')", conn);
Console.WriteLine($"Transactions des comptes de test supprimées : {await delTx.ExecuteNonQueryAsync()}");

await using var del = new NpgsqlCommand(
    "DELETE FROM \"Users\" WHERE \"Username\" LIKE 'vendeur_test%' OR \"Username\" LIKE 'test_%'", conn);
Console.WriteLine($"Vendeurs de test supprimés : {await del.ExecuteNonQueryAsync()}");

await using var reset = new NpgsqlCommand(
    "UPDATE \"Users\" SET \"Credits\" = 0 WHERE \"Username\" = 'Pizzeria Bella Napoli'", conn);
Console.WriteLine($"Crédits Pizzeria remis à 0 : {await reset.ExecuteNonQueryAsync()}");

Console.WriteLine("Nettoyage terminé.");
return 0;
