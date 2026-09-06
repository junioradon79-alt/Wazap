namespace Wazap.Application.Configuration;

/// <summary>
/// Options de rétention / archivage des données (section « Retention »).
/// Désactivé par défaut : la purge ne s'exécute qu'après activation explicite.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Active la purge périodique (rétention).</summary>
    public bool Enabled { get; set; }

    /// <summary>Commandes livrées conservées (jours) avant archivage/suppression.</summary>
    public int DeliveredOrdersDays { get; set; } = 90;

    /// <summary>Lots vides (sans commande restante) conservés (jours).</summary>
    public int EmptyBatchesDays { get; set; } = 90;

    /// <summary>Messages outbox envoyés conservés (jours).</summary>
    public int SentOutboxDays { get; set; } = 30;

    /// <summary>Fréquence du passage du worker (heures).</summary>
    public int RunIntervalHours { get; set; } = 24;
}
