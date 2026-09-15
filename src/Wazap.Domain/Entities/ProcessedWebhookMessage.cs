namespace Wazap.Domain.Entities;

/// <summary>
/// Trace d'un message webhook ENTRANT déjà traité, identifié par l'identifiant fourni par la
/// passerelle (Meta : <c>entry[].changes[].value.messages[].id</c>).
/// <para>
/// La passerelle réessaie toute requête qui n'a pas répondu 2xx — et peut réémettre un
/// événement déjà livré. Sans ce marqueur, un « LIVRAISON » rejoué créait une SECONDE
/// commande et une seconde vague d'offres aux mêmes livreurs (le vendeur payait deux crédits
/// pour une seule course), et un « ACCEPTE » rejoué relançait l'acceptation d'une offre.
/// </para>
/// </summary>
public class ProcessedWebhookMessage
{
    /// <summary>Identifiant unique du message côté passerelle (clé primaire).</summary>
    public string Id { get; private set; } = default!;

    public DateTime ProcessedAtUtc { get; private set; }

    private ProcessedWebhookMessage() { }

    public ProcessedWebhookMessage(string id, DateTime? processedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("L'identifiant du message est requis.", nameof(id));

        // La longueur est bornée : la colonne l'est aussi (200), et un identifiant
        // anormalement long ne doit pas faire échouer la transaction.
        var trimmed = id.Trim();
        Id = trimmed.Length > 200 ? trimmed[..200] : trimmed;
        ProcessedAtUtc = processedAtUtc ?? DateTime.UtcNow;
    }
}
