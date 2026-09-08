namespace Wazap.Application.Configuration;

/// <summary>
/// Réputation des livreurs (section « RiderReputation ») : notes clients après livraison.
/// </summary>
public sealed class RiderReputationOptions
{
    public const string SectionName = "RiderReputation";

    /// <summary>
    /// Délai après la livraison pendant lequel le client peut encore noter sa course.
    /// Au-delà, « NOTE » ne rattache plus rien (et le message repart vers le bot prospects).
    /// </summary>
    public int RatingWindowHours { get; set; } = 48;

    /// <summary>
    /// Note moyenne en dessous de laquelle un livreur ne reçoit plus d'offres.
    /// <c>0</c> (défaut) = filtre désactivé : les notes sont collectées et affichées,
    /// sans jamais réduire le vivier de livreurs tant que vous ne l'avez pas décidé.
    /// </summary>
    public double MinimumAverageScore { get; set; }

    /// <summary>
    /// Nombre de notes en dessous duquel un livreur échappe au filtre ci-dessus :
    /// un nouveau livreur ne doit pas être écarté sur une seule mauvaise note.
    /// </summary>
    public int MinimumRatingsBeforeFiltering { get; set; } = 5;

    /// <summary>
    /// Pondération du matching par la réputation : quand <c>true</c>, les livreurs ayant
    /// assez d'avis (≥ <see cref="MinimumRatingsBeforeFiltering"/>) sont proposés avant les
    /// autres, ordonnés par note moyenne décroissante (distance ensuite). Les livreurs sans
    /// assez d'avis sont traités comme « neutres » et passent après. Défaut : <c>false</c> —
    /// le matching reste strictement géographique tant que vous n'avez pas décidé autrement.
    /// </summary>
    public bool PreferHigherRatedRiders { get; set; }
}
