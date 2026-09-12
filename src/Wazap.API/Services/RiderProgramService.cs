using System.Text;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Programme « Ambassadeur WAZAP » — moteur de suivi des 3 conditions de récompense :
///  1. enrôlement **certifié** (dossier d'identité « Garantie Colis Sûr » vérifié) ;
///  2. volume de **livraisons** terminées ;
///  3. nombre de **filleuls livreurs validés** (parrainage + activité minimale du filleul).
/// Le calcul est fait à la demande (aucun état persisté) ; les notifications de franchissement
/// de seuil sont déclenchées par les événements (livraison, enrôlement).
/// </summary>
public sealed class RiderProgramService
{
    private readonly ApplicationDbContext _context;
    private readonly RiderProgramOptions _options;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ILogger<RiderProgramService> _logger;

    public RiderProgramService(ApplicationDbContext context, RiderProgramOptions options,
        IWhatsAppSender whatsApp, ILogger<RiderProgramService> logger)
    {
        _context = context;
        _options = options;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    /// <summary>Le programme est-il activé (sinon aucun suivi).</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>Progression d'un livreur — <c>null</c> si le programme est désactivé ou le livreur inconnu.</summary>
    public async Task<RiderProgramProgress?> BuildProgressAsync(Guid riderId, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return null;

        var rider = await _context.Users.AsNoTracking()
            .Where(u => u.Id == riderId && u.Role == UserRole.Rider)
            .Select(u => new { u.Id, u.Username, u.PhoneNumber })
            .FirstOrDefaultAsync(ct);

        return rider is null
            ? null
            : await BuildForAsync(rider.Id, rider.Username, rider.PhoneNumber, ct);
    }

    /// <summary>Progression de tous les livreurs (page admin), classée par conditions remplies.</summary>
    public async Task<IReadOnlyList<RiderProgramProgress>> BuildAllAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return Array.Empty<RiderProgramProgress>();

        var riders = await _context.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Rider)
            .Select(u => new { u.Id, u.Username, u.PhoneNumber })
            .ToListAsync(ct);

        var result = new List<RiderProgramProgress>(riders.Count);
        foreach (var r in riders)
            result.Add(await BuildForAsync(r.Id, r.Username, r.PhoneNumber, ct));

        return result
            .OrderByDescending(p => p.RewardUnlocked)
            .ThenByDescending(p => p.ConditionsMet)
            .ThenByDescending(p => p.Deliveries)
            .ToList();
    }

    private async Task<RiderProgramProgress> BuildForAsync(Guid riderId, string username, string? phone, CancellationToken ct)
    {
        var deliveries = await _context.Orders.AsNoTracking()
            .CountAsync(o => o.RiderUserId == riderId && o.Status == OrderStatus.Delivered, ct);

        // Filleuls (livreurs parrainés par ce livreur) ayant atteint l'activité minimale.
        var filleulIds = await _context.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Rider && u.ReferredByUserId == riderId)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var validatedReferrals = 0;
        if (filleulIds.Count > 0)
        {
            var counts = await _context.Orders.AsNoTracking()
                .Where(o => o.RiderUserId != null
                         && o.Status == OrderStatus.Delivered
                         && filleulIds.Contains(o.RiderUserId.Value))
                .GroupBy(o => o.RiderUserId!.Value)
                .Select(g => g.Count())
                .ToListAsync(ct);

            validatedReferrals = counts.Count(c => c >= _options.MinFilleulDeliveries);
        }

        var ratings = await _context.RiderRatings.AsNoTracking()
            .Where(r => r.RiderUserId == riderId)
            .Select(r => (double)r.Score)
            .ToListAsync(ct);
        double? average = ratings.Count > 0 ? ratings.Average() : null;

        var certified = await _context.RiderIdentities.AsNoTracking()
            .AnyAsync(i => i.UserId == riderId && i.Status == RiderIdentityStatus.Verified, ct);

        var ratingMet = _options.MinAverageRating <= 0
            || (average is not null && average.Value >= _options.MinAverageRating);

        return new RiderProgramProgress(
            riderId, username, phone,
            deliveries, _options.DeliveriesTarget,
            validatedReferrals, _options.ReferralsTarget,
            average, certified, ratingMet, _options.RewardLabel);
    }

    /// <summary>Message WhatsApp de progression (réponse à la commande « PROGRAMME »).</summary>
    public static string BuildProgressText(RiderProgramProgress p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🛵 Programme Ambassadeur WAZAP");
        sb.AppendLine();
        sb.AppendLine($"1️⃣ Enrôlement certifié : {(p.EnrolledMet ? "✅" : "⏳ à certifier")}");
        sb.AppendLine($"2️⃣ Livraisons : {p.Deliveries}/{p.DeliveriesTarget} {(p.DeliveriesMet ? "✅" : "⏳")}");
        sb.AppendLine($"3️⃣ Filleuls validés : {p.ValidatedReferrals}/{p.ReferralsTarget} {(p.ReferralsMet ? "✅" : "⏳")}");
        sb.AppendLine();
        sb.AppendLine($"🎁 Récompense : {p.RewardLabel}");
        sb.AppendLine();
        sb.Append(p.RewardUnlocked
            ? "🎉 Les 3 conditions sont remplies ! L'équipe WAZAP vous contacte pour la remise."
            : $"Conditions validées : {p.ConditionsMet}/3 — continuez, vos courses comptent !");
        return sb.ToString();
    }

    /// <summary>
    /// Notifications best-effort après une livraison : franchissement de seuil pour le livreur,
    /// puis pour son parrain (un nouveau filleul vient d'être « validé »).
    /// </summary>
    public async Task NotifyMilestonesAsync(Guid riderId, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return;

        try
        {
            var progress = await BuildProgressAsync(riderId, ct);
            if (progress is not null)
                await NotifyAsync(progress);

            var referrerId = await _context.Users.AsNoTracking()
                .Where(u => u.Id == riderId)
                .Select(u => u.ReferredByUserId)
                .FirstOrDefaultAsync(ct);

            if (referrerId is { } id && id != riderId)
            {
                var referrer = await BuildProgressAsync(id, ct);
                if (referrer is not null)
                    await NotifyAsync(referrer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notifications du programme livreurs impossibles pour {RiderId}.", riderId);
        }
    }

    /// <summary>Envoie la notification UNIQUEMENT au franchissement exact d'un seuil (une seule fois).</summary>
    private async Task NotifyAsync(RiderProgramProgress p)
    {
        if (string.IsNullOrWhiteSpace(p.RiderPhone))
            return;

        string? message = null;

        if (p.RewardUnlocked)
            message = $"🎉 Félicitations {p.RiderUsername} ! Les 3 conditions du programme Ambassadeur WAZAP sont remplies.\n"
                    + $"🎁 Récompense : {p.RewardLabel} — l'équipe WAZAP vous contacte pour la remise.";
        else if (p.DeliveriesMet && p.Deliveries == p.DeliveriesTarget)
            message = $"🎉 {p.DeliveriesTarget} livraisons atteintes ! Il ne reste que le parrainage "
                    + $"({p.ValidatedReferrals}/{p.ReferralsTarget} filleuls validés) pour gagner {p.RewardLabel}.";
        else if (p.ReferralsMet && p.ValidatedReferrals == p.ReferralsTarget)
            message = $"🎉 {p.ReferralsTarget} filleuls validés ! Il ne reste que les livraisons "
                    + $"({p.Deliveries}/{p.DeliveriesTarget}) pour gagner {p.RewardLabel}.";

        if (message is null)
            return;

        try
        {
            await _whatsApp.SendTextMessageAsync(p.RiderPhone, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification du programme impossible pour {Rider}.", p.RiderUsername);
        }
    }
}

/// <summary>Progression d'un livreur dans le programme « Ambassadeur WAZAP ».</summary>
public sealed record RiderProgramProgress(
    Guid RiderId,
    string RiderUsername,
    string? RiderPhone,
    int Deliveries,
    int DeliveriesTarget,
    int ValidatedReferrals,
    int ReferralsTarget,
    double? AverageRating,
    bool Certified,
    bool RatingMet,
    string RewardLabel)
{
    /// <summary>Condition 1 — compte enrôlé ET dossier d'identité vérifié.</summary>
    public bool EnrolledMet => Certified;

    /// <summary>Condition 2 — volume de livraisons atteint.</summary>
    public bool DeliveriesMet => Deliveries >= DeliveriesTarget;

    /// <summary>Condition 3 — nombre de filleuls validés atteint.</summary>
    public bool ReferralsMet => ValidatedReferrals >= ReferralsTarget;

    /// <summary>Nombre de conditions remplies (sur 3).</summary>
    public int ConditionsMet => (EnrolledMet ? 1 : 0) + (DeliveriesMet ? 1 : 0) + (ReferralsMet ? 1 : 0);

    /// <summary>Les 3 conditions (et la qualité éventuelle) sont remplies → récompense due.</summary>
    public bool RewardUnlocked => EnrolledMet && DeliveriesMet && ReferralsMet && RatingMet;
}

