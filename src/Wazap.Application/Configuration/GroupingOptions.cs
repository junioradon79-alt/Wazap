namespace Wazap.Application.Configuration;

/// <summary>
/// Configuration du groupage des livraisons (section « Grouping »).
/// </summary>
public sealed class GroupingOptions
{
    public const string SectionName = "Grouping";

    /// <summary>Fenêtre d'accumulation des commandes confirmées d'un même vendeur (minutes).</summary>
    public int WindowMinutes { get; set; } = 2;

    /// <summary>Nombre maximal de commandes par lot (au-delà, le lot est diffusé immédiatement).</summary>
    public int MaxOrdersPerBatch { get; set; } = 5;
}
