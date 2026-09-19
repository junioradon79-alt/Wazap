namespace Wazap.Infrastructure.Data;

/// <summary>
/// Convertit automatiquement les chaînes de connexion PostgreSQL fournies sous forme d'URI
/// (format standard Render, Railway, Heroku, Supabase: postgresql://user:pass@host:port/db)
/// vers le format standard clé-valeur attendu nativement par Npgsql et ADO.NET
/// (Host=...;Port=...;Database=...;Username=...;Password=...).
/// </summary>
public static class PostgresConnectionStringNormalizer
{
    public static string Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);
            var userInfo = uri.UserInfo.Split(':', 2);
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;

            return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Prefer;Trust Server Certificate=true";
        }

        return trimmed;
    }
}
