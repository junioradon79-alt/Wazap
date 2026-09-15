using System.Text.Json;
using Wazap.Application.Configuration;
using Wazap.Domain.Configuration;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Cohérence entre les valeurs par défaut du CODE et <c>appsettings.json</c>.
/// <para>
/// Ces deux sources coexistent : le fichier est lu en production, mais le code fait foi quand
/// il est absent ou remplacé au déploiement. Un écart silencieux produit donc un comportement
/// DIFFÉRENT selon l'environnement — l'audit du 15/09 en a trouvé un réel :
/// <c>Meta:ApiVersion</c> valait « v21.0 » dans le code et « v25.0 » dans le fichier, si bien
/// qu'un déploiement sans le fichier aurait appelé une version obsolète de l'API Graph.
/// </para>
/// <para>
/// Ce test échoue aussi si une section CRITIQUE disparaît du fichier : une section absente ne
/// fait pas échouer le démarrage (les options retombent sur leurs défauts), elle change
/// silencieusement le comportement.
/// </para>
/// </summary>
public class ConfigurationConsistencyTests
{
    /// <summary>Couples (type d'options, section JSON, propriété) à vérifier.</summary>
    private static readonly (Type Options, string Section, string Property)[] Cases =
    [
        // Passerelle WhatsApp : une version d'API obsolète fait échouer tous les envois.
        // (`PhoneNumberId` n'est PAS vérifié : c'est une valeur d'ENVIRONNEMENT — l'identifiant
        // du numéro d'envoi — et non un défaut de comportement du code.)
        (typeof(MetaApiOptions), "Meta", "ApiVersion"),
        (typeof(MetaApiOptions), "Meta", "LanguageCode"),
        (typeof(MetaApiOptions), "Meta", "GraphUrl"),

        // Géolocalisation : un seuil à 0 désactive le produit (aucune offre, ou purge du live).
        (typeof(GeoOptions), "Geo", "MaxDistanceKm"),
        (typeof(GeoOptions), "Geo", "LocationFreshnessMinutes"),
        (typeof(GeoOptions), "Geo", "ExclusivitySeconds"),
        (typeof(GeoOptions), "Geo", "GlobalTimeoutMinutes"),
        (typeof(GeoOptions), "Geo", "LocationRetentionHours"),

        // Groupage des tournées.
        (typeof(GroupingOptions), "Grouping", "WindowMinutes"),
        (typeof(GroupingOptions), "Grouping", "MaxOrdersPerBatch"),
        (typeof(GroupingOptions), "Grouping", "BuyerDispatchDelaySeconds"),

        // Sécurité des comptes et offre de découverte.
        (typeof(SecurityOptions), "Security", "MaxFailedLoginAttempts"),
        (typeof(SecurityOptions), "Security", "LockoutMinutes"),
        (typeof(TrialOptions), "Trial", "Enabled"),
        (typeof(TrialOptions), "Trial", "FreeCreditsOnRegistration"),

        // Rétention RGPD : une durée fausse = conservation illégale ou purge prématurée.
        (typeof(RetentionOptions), "Retention", "Enabled"),
        (typeof(RetentionOptions), "Retention", "DeliveredOrdersDays"),
        (typeof(RetentionOptions), "Retention", "EmptyBatchesDays"),
        (typeof(RetentionOptions), "Retention", "SentOutboxDays"),
        (typeof(RetentionOptions), "Retention", "RiderScansDays"),
        (typeof(RetentionOptions), "Retention", "RunIntervalHours"),

        // Garde-fous produit.
        (typeof(RiderSecurityOptions), "RiderSecurity", "RequireCertifiedRiders"),
        (typeof(DeliveryProofOptions), "DeliveryProof", "RequireClientCode"),
        (typeof(ClientPaymentOptions), "ClientPayments", "Enabled"),
        (typeof(ClientPaymentOptions), "ClientPayments", "CommissionPercent"),
        (typeof(ClientPaymentOptions), "ClientPayments", "RequirePaymentBeforeDispatch"),
        (typeof(RiderReputationOptions), "RiderReputation", "RatingWindowHours"),
        (typeof(RiderReputationOptions), "RiderReputation", "MinimumRatingsBeforeFiltering"),
        (typeof(RiderPriorityOptions), "RiderPriority", "MaxPriorityRidersPerWave"),
        (typeof(RiderProgramOptions), "RiderProgram", "DeliveriesTarget"),
        (typeof(RiderProgramOptions), "RiderProgram", "ReferralsTarget"),
        (typeof(RiderProgramOptions), "RiderProgram", "MinFilleulDeliveries"),
        (typeof(MonitoringOptions), "Monitoring", "AlertCooldownMinutes"),
        (typeof(PublicApiOptions), "PublicApi", "RateLimitPerMinute"),

        // Sécurité des webhooks entrants (fail closed par défaut).
        (typeof(WebhookSecurityOptions), "WebhookSecurity", "RequireAuthentication"),
    ];

    [Fact]
    public void ValeursParDefautDuCode_Et_AppSettings_Concordent()
    {
        var path = FindAppSettings();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var problems = new List<string>();

        foreach (var (optionsType, section, property) in Cases)
        {
            if (!root.TryGetProperty(section, out var sectionElement))
            {
                problems.Add($"{section} : section ABSENTE de appsettings.json (le code retomberait sur ses défauts)");
                continue;
            }

            // La liaison de configuration .NET est insensible à la casse : appsettings.json
            // utilise ici les noms PascalCase des propriétés, mais un camelCase reste valide.
            var jsonValue = FindProperty(sectionElement, property);
            if (jsonValue is not { } found)
            {
                problems.Add($"{section}:{property} : clé ABSENTE de appsettings.json");
                continue;
            }

            var instance = Activator.CreateInstance(optionsType)
                ?? throw new InvalidOperationException($"Options non instanciables : {optionsType.Name}");
            var clrValue = optionsType.GetProperty(property)?.GetValue(instance);

            if (!ValuesMatch(clrValue, found))
            {
                problems.Add(
                    $"{section}:{property} : appsettings.json = « {found} » mais défaut du code = « {clrValue} »");
            }
        }

        Assert.True(problems.Count == 0,
            "Divergence(s) entre le code et appsettings.json (le comportement changerait selon "
            + "que le fichier est présent ou non) :\n - " + string.Join("\n - ", problems));
    }

    /// <summary>Champ d'un objet JSON, sans tenir compte de la casse (comme la liaison .NET).</summary>
    private static JsonElement? FindProperty(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static bool ValuesMatch(object? clrValue, JsonElement jsonValue)
    {
        if (clrValue is null)
            return jsonValue.ValueKind == JsonValueKind.Null;

        return clrValue switch
        {
            bool boolean => jsonValue.ValueKind == JsonValueKind.True || jsonValue.ValueKind == JsonValueKind.False
                ? jsonValue.GetBoolean() == boolean
                : false,
            int number => jsonValue.ValueKind == JsonValueKind.Number && Math.Abs(jsonValue.GetDouble() - number) < 0.0001,
            double real => jsonValue.ValueKind == JsonValueKind.Number && Math.Abs(jsonValue.GetDouble() - real) < 0.0001,
            decimal money => jsonValue.ValueKind == JsonValueKind.Number && Math.Abs(jsonValue.GetDecimal() - money) < 0.0001m,
            _ => jsonValue.ValueKind == JsonValueKind.String
                 && string.Equals(jsonValue.GetString(), clrValue.ToString(), StringComparison.Ordinal)
        };
    }

    /// <summary>Remonte jusqu'à <c>src/Wazap.API/appsettings.json</c> depuis le dossier d'exécution des tests.</summary>
    private static string FindAppSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Wazap.API", "appsettings.json");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "appsettings.json introuvable depuis " + AppContext.BaseDirectory
            + " : le test de cohérence de configuration ne peut pas s'exécuter.");
    }
}
