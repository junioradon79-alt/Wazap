namespace Wazap.Application.Configuration;

/// <summary>
/// Pack prioritaire LIVREUR (section « RiderPriority ») : option payante permettant à un livreur
/// d'être <b>proposé en premier</b> dans son rayon.
/// WAZAP ne vend ni le transport ni une attribution garantie : la priorité ne contourne
/// <b>jamais</b> le rayon de diffusion (<c>Geo:MaxDistanceKm</c>), la zone déclarée, la
/// disponibilité, la fraîcheur GPS ni les exclusions (blacklist, sinistre en cours,
/// certification Colis Sûr) — et l'attribution reste à l'acceptation du livreur.
/// Référence : <c>marketing/POLITIQUE_COMMERCIALE_ET_POSITIONNEMENT.md</c> §6.
/// </summary>
public sealed class RiderPriorityOptions
{
    public const string SectionName = "RiderPriority";

    /// <summary>
    /// Ouverture de l'<b>achat</b> du pack (catalogue + endpoint). Défaut <c>false</c> : on ne
    /// commercialise pas l'option tant que vous ne l'avez pas décidé — vendre une priorité
    /// non implémentée serait une promesse non tenue. La priorité déjà accordée à un livreur
    /// reste, elle, toujours appliquée au matching (utile pour tester ou pour offrir l'option).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Plafond de livreurs prioritaires remontés en tête par vague (équité : le pack ne doit pas
    /// assécher les propositions des non-abonnés). <c>0</c> = aucun plafond ; défaut <c>2</c>.
    /// Les prioritaires au-delà du plafond restent dans la vague à leur rang géographique.
    /// </summary>
    public int MaxPriorityRidersPerWave { get; set; } = 2;
}