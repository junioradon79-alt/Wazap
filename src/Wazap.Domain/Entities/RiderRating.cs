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
}
