using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Réputation des livreurs : le client note sa course (« NOTE 5 », option : commentaire)
/// après avoir reçu la confirmation de livraison. La moyenne est ensuite affichée au
/// vendeur avant qu'il ne remette le colis.
/// </summary>
public sealed class RiderRatingService
{
    private const string Command = "NOTE";

    private readonly ApplicationDbContext _context;
    private readonly RiderReputationOptions _options;
    private readonly ILogger<RiderRatingService> _logger;

    public RiderRatingService(ApplicationDbContext context, RiderReputationOptions options,
        ILogger<RiderRatingService> logger)
    {
        _context = context;
        _options = options;
        _logger = logger;
    }

    /// <summary>Vrai si le message est une tentative de notation (« NOTE … »).</summary>
    public static bool IsRatingCommand(string? text)
    {
        var upper = (text ?? string.Empty).Trim().ToUpperInvariant();
        return upper == Command || upper.StartsWith(Command + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Enregistre la note du client pour sa dernière course livrée. Retourne <c>null</c>
    /// quand aucune course notable n'existe pour ce numéro : l'appelant poursuit alors son
    /// traitement normal (bot prospects), plutôt que d'avaler le message.
    /// </summary>
    public async Task<string?> TryRateAsync(string? clientPhone, string text)
    {
        if (string.IsNullOrWhiteSpace(clientPhone))
            return null;

        var since = DateTime.UtcNow.AddHours(-Math.Max(1, _options.RatingWindowHours));

        // Pré-filtre EN SQL sur les 8 derniers chiffres : c'est exactement la base de
        // SameSubscriber (ancienne numérotation ivoirienne = 8 chiffres, nouvelle = préfixe
        // + les mêmes 8), donc un sur-ensemble sûr. Sans ce filtre il faudrait ramener
        // toutes les livraisons de la fenêtre — impraticable au volume visé.
        var digits = PhoneNumberNormalizer.DigitsOnly(clientPhone);
        var suffix = digits.Length >= 8 ? digits[^8..] : digits;
        if (suffix.Length == 0)
            return null;

        var candidates = await _context.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Delivered
                     && o.DeliveredAt != null && o.DeliveredAt >= since
                     && o.RiderUserId != null
                     && o.ClientWhatsAppNumber.EndsWith(suffix))
            .OrderByDescending(o => o.DeliveredAt)
            .Take(20)
            .Select(o => new { o.Id, o.RiderUserId, o.ClientWhatsAppNumber })
            .ToListAsync();

        // Confirmation en mémoire : SameSubscriber tranche les cas de numérotation.
        var recent = candidates;

        var order = recent.FirstOrDefault(o => PhoneNumberNormalizer.SameSubscriber(o.ClientWhatsAppNumber, clientPhone));
        if (order is null)
            return null;

        if (await _context.RiderRatings.AnyAsync(r => r.OrderId == order.Id))
            return "ℹ️ Vous avez déjà noté cette livraison. Merci !";

        if (!TryParseScore(text, out var score, out var comment))
            return $"❓ Note invalide. Répondez par exemple : NOTE 5 (de {RiderRating.MinScore} à {RiderRating.MaxScore}).";

        _context.RiderRatings.Add(new RiderRating(
            order.Id, order.RiderUserId!.Value, clientPhone, score, comment));

        await _context.SaveChangesAsync();

        _logger.LogInformation("Note {Score}/5 enregistrée pour la commande {OrderId}.", score, order.Id);

        return score >= 4
            ? $"⭐ Merci ! Note de {score}/5 enregistrée pour votre livreur."
            : $"⭐ Merci, note de {score}/5 enregistrée. Nous transmettons votre retour à l'équipe.";
    }

    /// <summary>« NOTE 4 » ou « NOTE 4 trop lent » → score + commentaire optionnel.</summary>
    internal static bool TryParseScore(string text, out int score, out string? comment)
    {
        score = 0;
        comment = null;

        var rest = (text ?? string.Empty).Trim();
        if (rest.Length <= Command.Length)
            return false;

        rest = rest[Command.Length..].Trim();
        if (rest.Length == 0)
            return false;

        var separator = rest.IndexOf(' ');
        var scorePart = separator < 0 ? rest : rest[..separator];
        comment = separator < 0 ? null : rest[(separator + 1)..].Trim();

        if (!int.TryParse(scorePart, out score) || score is < RiderRating.MinScore or > RiderRating.MaxScore)
        {
            score = 0;
            comment = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(comment))
            comment = null;
        else if (comment.Length > 300)
            comment = comment[..300];

        return true;
    }
}
