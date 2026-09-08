namespace Wazap.Domain.Entities;

/// <summary>
/// Note attribuée par le client à son livreur après une livraison (1 à 5 étoiles).
/// Une seule note par commande — le client note la course, pas le livreur en général.
/// </summary>
public class RiderRating
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid RiderUserId { get; private set; }

    /// <summary>Numéro du client au moment de la notation (traçabilité anti-fraude).</summary>
    public string ClientWhatsAppNumber { get; private set; } = default!;

    public int Score { get; private set; }
    public string? Comment { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Réponse du livreur à l'avis (visible côté équipe et vendeur).</summary>
    public string? Reply { get; private set; }

    /// <summary>Horodatage de la réponse du livreur (null tant qu'il n'a pas répondu).</summary>
    public DateTime? RepliedAt { get; private set; }

    // Constructeur privé pour EF Core
    private RiderRating() { }

    public RiderRating(Guid orderId, Guid riderUserId, string clientWhatsAppNumber, int score, string? comment = null)
    {
        if (score is < MinScore or > MaxScore)
            throw new ArgumentOutOfRangeException(nameof(score), $"La note doit être comprise entre {MinScore} et {MaxScore}.");

        Id = Guid.NewGuid();
        OrderId = orderId;
        RiderUserId = riderUserId;
        ClientWhatsAppNumber = clientWhatsAppNumber;
        Score = score;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Enregistre la réponse du livreur à l'avis (texte libre, tronqué à 500 caractères).
    /// Une mise à jour remplace la réponse précédente (le livreur peut corriger son message).
    /// </summary>
    public void ReplyAs(string reply)
    {
        var trimmed = (reply ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("La réponse ne peut pas être vide.", nameof(reply));

        Reply = trimmed.Length > MaxReplyLength ? trimmed[..MaxReplyLength] : trimmed;
        RepliedAt = DateTime.UtcNow;
    }

    public const int MaxReplyLength = 500;
}
