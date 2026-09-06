using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// « Garantie Colis Sûr » — dossiers de sinistre : un vendeur déclare un colis perdu/volé
/// (commande WhatsApp SINISTRE +code), le livreur est suspendu pendant l'enquête, l'équipe
/// valide via /app/claims : remboursement du crédit + indemnisation en crédits et exclusion
/// définitive du livreur si le sinistre est confirmé.
/// </summary>
public sealed class ColisSurService
{
    // Statuts où le colis a quitté le vendeur (sinistre possible).
    private static readonly OrderStatus[] ClaimableStatuses =
    {
        OrderStatus.RiderAssigned, OrderStatus.ReadyForPickup,
        OrderStatus.PickedUp, OrderStatus.InTransit
    };

    private readonly ApplicationDbContext _context;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ILogger<ColisSurService> _logger;
    private readonly string? _teamPhone;

    public ColisSurService(ApplicationDbContext context, IWhatsAppSender whatsApp,
        IConfiguration config, ILogger<ColisSurService> logger)
    {
        _context = context;
        _whatsApp = whatsApp;
        _logger = logger;
        _teamPhone = config["Prospect:TeamPhone"];
    }

    /// <summary>Déclare un sinistre depuis une commande WhatsApp « SINISTRE &lt;code&gt; ».</summary>
    public async Task<SinistreResult> DeclareAsync(Guid vendorId, string command)
    {
        var code = command.Length > "SINISTRE".Length
            ? command["SINISTRE".Length..].Trim()
            : string.Empty;

        if (code.Length < 4)
            return new SinistreResult(false,
                "📦 Format : SINISTRE <code de la course>\nEx. : SINISTRE A1B2C3D4");

        var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == vendorId)
            ?? throw new InvalidOperationException("Vendeur introuvable.");

        var vendorOrders = await _context.Orders
            .Where(o => o.VendorUserId == vendorId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var order = vendorOrders.FirstOrDefault(o =>
            o.Id.ToString("N").StartsWith(code, StringComparison.OrdinalIgnoreCase));

        if (order is null)
            return new SinistreResult(false,
                $"❌ Aucune course #{code.ToUpperInvariant()} trouvée pour votre compte.");

        var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();

        var existing = await _context.DeliveryClaims
            .FirstOrDefaultAsync(c => c.OrderId == order.Id);

        if (existing is not null)
            return new SinistreResult(false, existing.Status == DeliveryClaimStatus.Rejected
                ? $"ℹ️ La course #{orderCode} a déjà fait l'objet d'un dossier REJETÉ après enquête."
                : $"⏳ Un dossier est déjà ouvert pour la course #{orderCode} — notre équipe vous répond sous 48 h.");

        if (!ClaimableStatuses.Contains(order.Status))
            return new SinistreResult(false, order.Status switch
            {
                OrderStatus.Delivered => $"ℹ️ La course #{orderCode} est marquée LIVRÉE. Pour un litige, contactez directement notre équipe.",
                OrderStatus.Cancelled => $"ℹ️ La course #{orderCode} a été annulée.",
                _ => $"ℹ️ La course #{orderCode} n'a pas encore été remise à un livreur."
            });

        if (order.RiderUserId is not { } riderId)
            return new SinistreResult(false, "ℹ️ Aucun livreur n'est rattaché à cette course.");

        // La garantie ne s'applique qu'aux livreurs certifiés.
        var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == riderId)
            ?? throw new InvalidOperationException("Livreur introuvable.");

        var identity = await _context.RiderIdentities.AsNoTracking()
            .FirstOrDefaultAsync(i => i.UserId == riderId);
        if (identity?.Status != RiderIdentityStatus.Verified)
            return new SinistreResult(false,
                "⚠️ Le livreur de cette course n'était pas certifié — la Garantie Colis Sûr ne s'applique pas. Contactez notre équipe.");

        var claim = new DeliveryClaim(order.Id, vendorId, riderId, command);
        _context.DeliveryClaims.Add(claim);
        await _context.SaveChangesAsync();

        await NotifyTeamAsync($"🚨 [Sinistre] #{orderCode} — {vendor.Username} {vendor.PhoneNumber} · livreur : {rider.Username}. À traiter dans /app/claims.");

        return new SinistreResult(true,
            $"🚨 Sinistre #{orderCode} enregistré.\nLe livreur {rider.Username} est suspendu le temps de l'enquête.\n" +
            "Si le sinistre est confirmé : remboursement du crédit + indemnisation sous 48 h (notre équipe vous contacte).");
    }
    /// <summary>Confirme un sinistre : rembourse + indemnise le vendeur, exclut le livreur.</summary>
    public async Task ApproveAsync(Guid claimId, int compensationCredits, string? note, Guid reviewerId)
    {
        var claim = await _context.DeliveryClaims.FirstOrDefaultAsync(c => c.Id == claimId)
            ?? throw new InvalidOperationException("Dossier introuvable.");
        if (claim.Status != DeliveryClaimStatus.Pending)
            throw new InvalidOperationException("Ce dossier a déjà été traité.");

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == claim.OrderId)
            ?? throw new InvalidOperationException("Commande introuvable.");
        var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == claim.VendorUserId)
            ?? throw new InvalidOperationException("Vendeur introuvable.");
        var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == claim.RiderUserId)
            ?? throw new InvalidOperationException("Livreur introuvable.");

        var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();

        // 1. Remboursement du crédit consommé pour la course.
        vendor.AddCredits(1);
        _context.CreditTransactions.Add(CreditTransaction.ForFreeGrant(
            vendor.Id, 1, $"CLAIM-{orderCode}-REFUND", "Remboursement Garantie Colis Sûr - crédit de la course"));

        // 2. Indemnisation décidée par l'équipe (en crédits).
        var compensation = Math.Max(0, compensationCredits);
        if (compensation > 0)
        {
            vendor.AddCredits(compensation);
            _context.CreditTransactions.Add(CreditTransaction.ForFreeGrant(
                vendor.Id, compensation, $"CLAIM-{orderCode}-COMP", "Indemnisation Garantie Colis Sûr"));
        }

        // 3. Exclusion définitive du livreur (colis perdu/volé).
        var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == rider.Id);
        if (identity is null)
        {
            identity = new RiderIdentity(rider.Id);
            _context.RiderIdentities.Add(identity);
        }
        identity.Blacklist($"Sinistre #{orderCode} confirmé — colis perdu/volé.", reviewerId);
        rider.SetAvailability(false);

        claim.Approve(compensation, note, reviewerId);
        await _context.SaveChangesAsync();

        await NotifyVendorAsync(vendor,
            $"✅ Garantie Colis Sûr — sinistre #{orderCode} CONFIRMÉ.\n" +
            "Vous avez été remboursé : 1 crédit (course)"
            + (compensation > 0 ? $" + {compensation} crédit(s) d'indemnisation" : "") + ".\n" +
            "Merci de votre confiance.");
    }

    /// <summary>Rejette un sinistre après enquête (livreur dégelé, aucun remboursement).</summary>
    public async Task RejectAsync(Guid claimId, string? note, Guid reviewerId)
    {
        var claim = await _context.DeliveryClaims.FirstOrDefaultAsync(c => c.Id == claimId)
            ?? throw new InvalidOperationException("Dossier introuvable.");
        if (claim.Status != DeliveryClaimStatus.Pending)
            throw new InvalidOperationException("Ce dossier a déjà été traité.");

        claim.Reject(note, reviewerId);
        await _context.SaveChangesAsync();
    }


    /// <summary>Liste des dossiers de sinistre pour l'équipe (/app/claims).</summary>
    public async Task<List<ClaimListItem>> ListAsync()
    {
        var claims = await _context.DeliveryClaims.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var userIds = claims.Select(c => c.VendorUserId)
            .Concat(claims.Select(c => c.RiderUserId))
            .Distinct()
            .ToList();
        var users = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var orderIds = claims.Select(c => c.OrderId).Distinct().ToList();
        var orders = await _context.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id);

        return claims
            .Select(c =>
            {
                users.TryGetValue(c.VendorUserId, out var vendor);
                users.TryGetValue(c.RiderUserId, out var rider);
                orders.TryGetValue(c.OrderId, out var order);
                var orderCode = order?.Id.ToString("N")[..8].ToUpperInvariant()
                               ?? c.OrderId.ToString("N")[..8].ToUpperInvariant();
                var description = (order?.Description ?? string.Empty).Trim();
                return new ClaimListItem(
                    c.Id,
                    c.OrderId,
                    orderCode,
                    c.VendorUserId,
                    vendor?.Username ?? "?",
                    vendor?.PhoneNumber,
                    c.RiderUserId,
                    rider?.Username ?? "?",
                    c.Status.ToString(),
                    c.CompensationCredits,
                    description.Length > 60 ? description[..60] : description,
                    c.VendorNote,
                    c.ReviewNote,
                    c.CreatedAt,
                    c.ReviewedAt);
            })
            .ToList();
    }

    private async Task NotifyVendorAsync(User vendor, string message)
    {
        if (string.IsNullOrWhiteSpace(vendor.PhoneNumber))
            return;

        try
        {
            await _whatsApp.SendTextMessageAsync(vendor.PhoneNumber, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification WhatsApp impossible pour le vendeur {Vendor}.", vendor.Username);
        }
    }

    private async Task NotifyTeamAsync(string summary)
    {
        if (string.IsNullOrWhiteSpace(_teamPhone))
            return;

        try
        {
            await _whatsApp.SendTextMessageAsync("+" + PhoneNumberNormalizer.DigitsOnly(_teamPhone), summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alerte sinistre impossible vers l'équipe.");
        }
    }
}

public sealed record SinistreResult(bool Success, string Message);

public sealed record ClaimListItem(
    Guid ClaimId,
    Guid OrderId,
    string OrderCode,
    Guid VendorUserId,
    string VendorName,
    string? VendorPhone,
    Guid RiderUserId,
    string RiderName,
    string Status,
    int? CompensationCredits,
    string? Description,
    string? VendorNote,
    string? ReviewNote,
    DateTime CreatedAt,
    DateTime? ReviewedAt);

