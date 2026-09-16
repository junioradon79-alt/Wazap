using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Contexte de test sur **PostgreSQL réel** (B-19).
/// <para>
/// Les autres harnais relationnels tournent sur SQLite : ils exercent bien les UPDATE
/// conditionnels et les contraintes, mais <b>pas la traduction SQL du fournisseur réellement
/// utilisé en production</b> (Npgsql), ni les constructions qui n'existent que chez lui
/// (<c>FOR UPDATE SKIP LOCKED</c>, types/opérateurs propres à PostgreSQL). Une requête LINQ
/// valide sur SQLite peut échouer sur PostgreSQL — le vérifier demande le vrai moteur.
/// </para>
/// <para>
/// <b>Aucune dépendance NuGet n'est ajoutée</b> : le serveur est fourni par un <i>service
/// container</i> du pipeline GitHub Actions, et l'adresse arrive par la variable
/// d'environnement <c>WAZAP_TEST_POSTGRES</c>. En local, les tests s'annoncent « ignorés »
/// (avec la raison) au lieu d'échouer : la couverture réelle est apportée par la CI.
/// </para>
/// <para>
/// Chaque harnais crée une base <b>dédiée</b> puis applique les <b>migrations réelles</b> :
/// c'est aussi une vérification que les 32 migrations s'appliquent sur PostgreSQL, ce que
/// SQLite ne peut pas prouver.
/// </para>
/// </summary>
internal sealed class PostgresHarness : IDisposable
{
    /// <summary>Variable d'environnement portant la chaîne de connexion (base d'administration).</summary>
    public const string ConnectionVariable = "WAZAP_TEST_POSTGRES";

    /// <summary>Vrai si un serveur PostgreSQL est annoncé pour les tests.</summary>
    public static bool IsConfigured
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    /// <summary>Explication à afficher quand le serveur n'est pas annoncé (tests ignorés).</summary>
    public static string NotConfiguredReason =>
        $"PostgreSQL réel non configuré : renseignez {ConnectionVariable} (chaîne de connexion "
        + "d'administration) pour exécuter ces tests. La CI le fait via un service container.";

    /// <summary>Raison de non-exécution, ou <c>null</c> si le serveur est disponible.</summary>
    public string? SkipReason { get; }

    public ApplicationDbContext? Context { get; private set; }

    private readonly string _adminConnectionString;
    private string? _databaseName;

    public PostgresHarness()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            SkipReason = NotConfiguredReason;
            return;
        }

        try
        {
            _databaseName = "wazap_it_" + Guid.NewGuid().ToString("N")[..12];
            CreateDatabase();
            Context = BuildContext();
            // Les migrations RÉELLES : c'est aussi un test de leur applicabilité sur PostgreSQL.
            Context.Database.Migrate();
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException)
        {
            // Serveur annoncé mais injoignable : on échoue clairement au lieu de passer en silence.
            throw new InvalidOperationException(
                $"{ConnectionVariable} est renseignée mais le serveur PostgreSQL est injoignable.", ex);
        }
    }

    private void CreateDatabase()
    {
        // CREATE DATABASE ne peut pas s'exécuter dans une transaction : commande directe.
        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
        command.ExecuteNonQuery();
    }

    private string DatabaseConnectionString()
        => new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName }.ConnectionString;

    private ApplicationDbContext BuildContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(DatabaseConnectionString())
            .Options);

    /// <summary>Second contexte sur la même base : simule deux sessions applicatives distinctes.</summary>
    public ApplicationDbContext NewContext() => BuildContext();

    /// <summary>Recharge une entité depuis la base (hors suivi) : état réellement persisté.</summary>
    public T? Reload<T>(Guid id) where T : class
    {
        Context!.ChangeTracker.Clear();
        return Context.Find<T>(id);
    }

    public void Dispose()
    {
        Context?.Dispose();

        if (_databaseName is null)
            return;

        try
        {
            using var connection = new NpgsqlConnection(_adminConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
            command.ExecuteNonQuery();
        }
        catch (NpgsqlException)
        {
            // Base de test résiduelle : sans conséquence (le serveur de CI est éphémère).
        }
    }
}

/// <summary>
/// Test qui ne s'exécute que si un serveur PostgreSQL de test est annoncé (B-19).
/// <para>
/// xUnit v2 n'a pas de saut dynamique (<c>Assert.Skip</c> est arrivé avec la v3) : l'attribut
/// positionne donc <see cref="FactAttribute.Skip"/> à la découverte des tests. En local sans
/// serveur, les tests apparaissent <b>ignorés avec la raison</b> — jamais « verts » à tort.
/// </para>
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresHarness.IsConfigured)
            Skip = PostgresHarness.NotConfiguredReason;
    }
}
