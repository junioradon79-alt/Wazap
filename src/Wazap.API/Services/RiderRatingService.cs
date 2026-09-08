using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Réputation des livreurs : le client note sa course (« NOTE 5 », option : commentaire)
/// après avoir reçu la confirmation de livraison. La moyenne est ensuite affichée au
/// vendeur avant qu'il ne remette le colis. Le livreur peut consulter ses avis (« AVIS »)
/// et y répondre (« REPONDRE <n> <texte> »).
/// </summary>
public sealed class RiderRatingService
{
    private const string Command = "NOTE";
    private const string MyRatingsCommand = "AVIS";
    private const string ReplyCommand = "REPONDRE";

    /// <summary>Nombre d'avis les plus récents proposés au livreur (liste numérotée).</summary>
    public const int MyRatingsCount = 5;

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

    /// <summary>Vrai si le message demande la liste des avis du livreur (« AVIS »).</summary>
    public static bool IsMyRatingsCommand(string? text)
        => (text ?? string.Empty).Trim().Equals(MyRatingsCommand, StringComparison.OrdinalIgnoreCase);

    /// <summary>Vrai si le message est une réponse du livreur (« REPONDRE … »).</summary>
    public static bool IsReplyCommand(string? text)
    {
        var upper = (text ?? string.Empty).Trim().ToUpperInvariant();
        return upper == ReplyCommand || upper.StartsWith(ReplyCommand + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// « REPONDRE 2 Merci pour tout » → (2, « Merci pour tout »).
    /// Un index manquant ou non numérique retourne <c>false</c>.
    /// </summary>
    internal static bool TryParseReplyCommand(string text, out int index, out string reply)
    {
        index = 0;
        reply = string.Empty;

        var rest = (text ?? string.Empty).Trim();
        if (rest.Length <= ReplyCommand.Length)
            return false;

        rest = rest[ReplyCommand.Length..].Trim();
        var separator = rest.IndexOf(' ');
        var indexPart = separator < 0 ? rest : rest[..separator];
        reply = separator < 0 ? string.Empty : rest[(separator + 1)..].Trim();

        if (!int.TryParse(indexPart, out index) || index is < 1 or > MyRatingsCount)
        {
            index = 0;
            reply = string.Empty;
            return false;
        }

        if (reply.Length == 0)
        {
            index = 0;
            return false;
        }

        return true;
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

        var order = candidates.FirstOrDefault(o => PhoneNumberNormalizer.SameSubscriber(o.ClientWhatsAppNumber, clientPhone));
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

        /// <summary>
    /// Liste numérotée des <see cref="MyRatingsCount"/> avis les plus récents du livreur,
    /// prête à être envoyée sur WhatsApp. Retourne un message « aucun avis » si besoin.
    /// </summary>
    public async Task<string> ListMyRatingsTextAsync(Guid riderUserId)
    {
        var recent = await RecentRatingsForRiderAsync(riderUserId, MyRatingsCount);
        if (recent.Count == 0)
            return "⭐ Aucun avis pour le moment. Vos clients pourront vous noter après chaque livraison.";

        var lines = new string[recent.Count];
        for (var i = 0; i < recent.Count; i++)
        {
            var rating = recent[i];
            var comment = string.IsNullOrWhiteSpace(rating.Comment)
                ? string.Empty
                : $" — « {rating.Comment} »";
            lines[i] = $"{i + 1}. ⭐ {rating.Score}/5{comment} ({ShortDate(rating.CreatedAt)})";
        }

        return "⭐ Vos avis récents — répondez avec REPONDRE <n°> <votre message> :\n"
            + string.Join("\n", lines);
    }

    /// <summary>
    /// Enregistre la réponse du livreur à l'un de ses avis récents (index de la liste AVIS).
    /// Retourne le message WhatsApp de confirmation / d'erreur.
    /// </summary>
    public async Task<string> ReplyAsync(Guid riderUserId, int index, string reply)
    {
        // Chargement AVEC suivi : les entités modifiées doivent être persistées par SaveChanges
        // (une lecture AsNoTracking rendrait la réponse muette — aucun changement détecté).
        var recent = await _context.RiderRatings
            .Where(r => r.RiderUserId == riderUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(MyRatingsCount)
            .ToListAsync();
        if (index < 1 || index > recent.Count)
            return $"❓ Aucun avis n°{index}. Consultez vos avis avec AVIS, puis répondez : REPONDRE <n°> <votre message>.";

        var rating = recent[index - 1];
        rating.ReplyAs(reply);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Réponse du livreur {RiderId} sur l'avis {RatingId}.", riderUserId, rating.Id);

        var exists = await _context.Orders.AsNoTracking().AnyAsync(o => o.Id == rating.OrderId);
        var orderCode = exists ? rating.OrderId.ToString("N")[..8].ToUpperInvariant() : string.Empty;

        return $"✅ Votre réponse est enregistrée sur l'avis n°{index} (course {orderCode}). "
            + "Elle est visible par l'équipe et par le vendeur avant la remise de vos prochains colis.";
    }

    /// <summary>
    /// Liste complète + synthèse par livreur pour la page admin. Le numéro du client est
    /// TOUJOURS masqué (RGPD) — l'équipe voit la course, la note, le commentaire et la réponse.
    /// </summary>
    public async Task<RiderRatingAdminBoardDto> ListForAdminAsync()
    {
        var ratings = await _context.RiderRatings.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(500)
            .ToListAsync();

        var riderIds = ratings.Select(r => r.RiderUserId).Distinct().ToList();
        var riderNames = await LoadRiderNamesAsync(riderIds);
        var orderCodes = await LoadOrderCodesAsync(ratings.Select(r => r.OrderId).Distinct().ToList());

        var board = new RiderRatingAdminBoardDto
        {
            Ratings = ratings.Select(r => new RiderRatingAdminDto
            {
                RatingId = r.Id,
                OrderCode = orderCodes.TryGetValue(r.OrderId, out var code) ? code : string.Empty,
                RiderId = r.RiderUserId,
                RiderName = riderNames.TryGetValue(r.RiderUserId, out var riderName) ? riderName : "—",
                Score = r.Score,
                Comment = r.Comment,
                Reply = r.Reply,
                RepliedAt = r.RepliedAt,
                MaskedClientPhone = MaskPhone(r.ClientWhatsAppNumber),
                CreatedAt = r.CreatedAt
            }).ToList(),
            RiderSummaries = riderIds.Select(riderId =>
            {
                var scores = ratings.Where(r => r.RiderUserId == riderId).Select(r => r.Score).ToList();
                return new RiderRatingSummaryDto
                {
                    RiderId = riderId,
                    RiderName = riderNames.TryGetValue(riderId, out var summaryName) ? summaryName : "—",
                    AverageScore = scores.Count == 0 ? 0.0 : scores.Average(),
                    RatingCount = scores.Count
                };
            }).ToList()
        };

        return board;
    }

    /// <summary>Avis les plus récents du livreur (ordre de la liste AVIS, index 1-based).</summary>
    private async Task<IReadOnlyList<RiderRating>> RecentRatingsForRiderAsync(
        Guid riderUserId, int limit)
    {
        return await _context.RiderRatings.AsNoTracking()
            .Where(r => r.RiderUserId == riderUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadRiderNamesAsync(IReadOnlyCollection<Guid> riderIds)
    {
        if (riderIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await _context.Users.AsNoTracking()
            .Where(u => riderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username })
            .ToDictionaryAsync(u => u.Id, u => u.Username);
    }

    /// <summary>Code court de course : les 8 premiers caractères hexadécimaux de l'Id.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadOrderCodesAsync(IReadOnlyCollection<Guid> orderIds)
    {
        if (orderIds.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = await _context.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => o.Id)
            .ToListAsync();

        return ids.ToDictionary(o => o, o => o.ToString("N")[..8].ToUpperInvariant());
    }

    private static string ShortDate(DateTime date)
        => $"{date.Day:0}{date.Month:0}";

    /// <summary>Masque un numéro de téléphone (jamais exposé complet côté front).</summary>
    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "—";

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 8)
            return "•• •• ••";

        return $"+{digits[..2]} {digits[2..3]} {digits[3..5]} •• •• {digits[^2..]}";
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
