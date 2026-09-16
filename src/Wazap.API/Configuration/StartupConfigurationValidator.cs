using Wazap.Application.Configuration;
using Wazap.Domain.Configuration;

namespace Wazap.API.Configuration;

/// <summary>
/// Contrôle de cohérence de la configuration AVANT que l'application ne serve du trafic.
/// <para>
/// Jusqu'ici, chaque section était lue par <c>GetSection(...).Get&lt;T&gt;() ?? new T()</c> sans
/// aucune validation : une valeur absente ou aberrante ne se manifestait qu'au premier appel
/// du chemin concerné, souvent par un comportement silencieusement faux. Modes de défaillance
/// identifiés lors de l'audit du 15/09 :
/// <list type="bullet">
/// <item><c>Geo:LocationRetentionHours = 0</c> → toutes les positions GPS, y compris live,
/// étaient purgées à chaque passage (matching effondré) ;</item>
/// <item><c>Geo:GlobalTimeoutMinutes = 0</c> → toutes les commandes en attente étaient
/// annulées avec « aucun livreur » ;</item>
/// <item>section <c>GeniusPay</c> absente ou mal orthographiée → bascule <b>silencieuse</b> sur
/// le paiement simulé, qui réussit toujours : des crédits étaient accordés <b>sans paiement</b> ;</item>
/// <item><c>ClientPayments:CommissionPercent &gt; 100</c> → exception levée <b>après</b> que le
/// client a réellement payé chez l'agrégateur.</item>
/// </list>
/// </para>
/// <para>
/// Le contrôle est volontairement limité à des invariants qui NE PEUVENT PAS être faux dans une
/// configuration saine : un déploiement valide ne peut donc pas être bloqué par un faux positif.
/// </para>
/// </summary>
public static class StartupConfigurationValidator
{
    /// <summary>
    /// Retourne la liste des problèmes bloquants (vide si la configuration est saine).
    /// </summary>
    public static IReadOnlyList<string> FindProblems(
        IConfiguration configuration,
        IHostEnvironment environment,
        GeoOptions geo,
        ClientPaymentOptions clientPayments,
        RiderReputationOptions reputation,
        RetentionOptions retention,
        GeniusPayOptions geniusPay,
        IReadOnlyList<PackConfiguration> packs,
        IReadOnlyList<RiderPriorityPackConfiguration> riderPriorityPacks)
    {
        var problems = new List<string>();

        // --- Persistance : sans chaîne de connexion, l'échec survient à la première requête.
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            problems.Add("ConnectionStrings:DefaultConnection est absente ou vide.");

        // --- JWT : une clé courte serait devinable (HMAC-SHA256).
        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
            problems.Add("Jwt:Key est absente ou trop courte (32 caractères minimum).");

        // --- Géolocalisation : ces quatre valeurs à 0 ou négatives désactivent le produit.
        if (geo.MaxDistanceKm <= 0)
            problems.Add("Geo:MaxDistanceKm doit être strictement positif (sinon aucune offre n'est diffusée).");
        if (geo.LocationFreshnessMinutes <= 0)
            problems.Add("Geo:LocationFreshnessMinutes doit être strictement positif (sinon aucun livreur n'est « frais »).");
        if (geo.GlobalTimeoutMinutes <= 0)
            problems.Add("Geo:GlobalTimeoutMinutes doit être strictement positif (sinon les commandes sont annulées immédiatement).");
        if (geo.LocationRetentionHours <= 0)
            problems.Add("Geo:LocationRetentionHours doit être strictement positif (sinon les positions LIVE sont purgées).");

        // --- Rétention : une durée négative n'a pas de sens ; 0 pour les scans = conservation
        //     illimitée, ce qui doit rester un choix EXPLICITE (avertissement, pas blocage).
        if (retention.DeliveredOrdersDays < 0 || retention.EmptyBatchesDays < 0
            || retention.SentOutboxDays < 0 || retention.RiderScansDays < 0)
            problems.Add("Retention : les durées de conservation ne peuvent pas être négatives.");

        // --- Paiement client : une commission hors 0-100 fait échouer la complétion APRÈS
        //     l'encaissement réel chez l'agrégateur.
        if (clientPayments.CommissionPercent is < 0 or > 100)
            problems.Add("ClientPayments:CommissionPercent doit être compris entre 0 et 100.");

        // --- Réputation : un seuil hors 0-5 écarte ou retient tout le monde en silence.
        if (reputation.MinimumAverageScore is < 0 or > 5)
            problems.Add("RiderReputation:MinimumAverageScore doit être compris entre 0 et 5.");

        // --- Catalogues : un pack inutilisable encaisse de l'argent sans rien livrer.
        foreach (var pack in packs)
        {
            if (string.IsNullOrWhiteSpace(pack.Name) || pack.Credits <= 0 || pack.Price <= 0)
                problems.Add($"Packs : l'entrée « {pack.Name ?? "(sans nom)"} » doit avoir un nom, un prix > 0 et des crédits > 0.");
        }

        foreach (var pack in riderPriorityPacks)
        {
            if (string.IsNullOrWhiteSpace(pack.Name) || pack.Price <= 0 || pack.Days <= 0)
                problems.Add($"RiderPriorityPacks : l'entrée « {pack.Name ?? "(sans nom)"} » doit avoir un nom, un prix > 0 et une durée > 0 jour.");
        }

        // --- Paiement simulé en PRODUCTION : le mock réussit toujours, donc des crédits
        //     seraient accordés sans aucun encaissement. On l'interdit explicitement
        //     (échappatoire assumée : Payments:AllowSimulatedPayments=true pour un staging).
        var allowSimulated = configuration.GetValue<bool?>("Payments:AllowSimulatedPayments") ?? false;
        if (environment.IsProduction() && !geniusPay.Enabled && !allowSimulated)
            problems.Add(
                "GeniusPay:Enabled est désactivé en production : le paiement SIMULÉ (qui réussit "
                + "toujours) serait utilisé et des crédits seraient accordés SANS paiement. "
                + "Activez GeniusPay, ou assumez explicitement Payments:AllowSimulatedPayments=true.");

        // --- Meta : une version Graph mal formée fait échouer tous les appels d'envoi.
        if (configuration.GetValue<bool?>("Meta:Enabled") ?? false)
        {
            var version = configuration["Meta:ApiVersion"];
            if (string.IsNullOrWhiteSpace(version) || !System.Text.RegularExpressions.Regex.IsMatch(version, @"^v\d+\.\d+$"))
                problems.Add("Meta:ApiVersion doit avoir la forme « vNN.N » (ex. v25.0).");
        }

        // --- B-16 : les en-têtes de proxy ne doivent JAMAIS être crus sans liste de proxies.
        //     `X-Forwarded-For` est fourni par le client : en hébergement direct (IIS in-process),
        //     faire confiance à cet en-tête permettrait à n'importe qui de changer d'adresse à
        //     volonté — donc de changer de compartiment de limitation de débit (les 5 politiques
        //     sont partitionnées par IP) et de contourner le verrouillage anti force-brute.
        if (configuration.GetValue<bool?>("Networking:TrustForwardedHeaders") ?? false)
        {
            var proxies = configuration.GetSection("Networking:KnownProxies").Get<string[]>() ?? [];
            if (proxies.Length == 0)
                problems.Add(
                    "Networking:TrustForwardedHeaders=true exige Networking:KnownProxies (adresses IP "
                    + "des proxies de confiance) : sans cette liste, n'importe quel client peut usurper "
                    + "son adresse et contourner la limitation de débit et le verrouillage des connexions.");
            else if (proxies.Any(p => !System.Net.IPAddress.TryParse(p, out _)))
                problems.Add("Networking:KnownProxies ne doit contenir que des adresses IP valides.");
        }

        return problems;
    }
}
