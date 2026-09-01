using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Catalogue des packs + achat (paiement → CreditTransaction → crédits vendeur).
    /// </summary>
    public sealed class PackService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentService _paymentService;
        private readonly IReadOnlyList<PackConfiguration> _packs;
        private readonly WhatsAppOrchestrationService _whatsApp;
        private readonly ILogger<PackService> _logger;

        public PackService(
            ApplicationDbContext context,
            IPaymentService paymentService,
            IReadOnlyList<PackConfiguration> packs,
            WhatsAppOrchestrationService whatsApp,
            ILogger<PackService> logger)
        {
            _context = context;
            _paymentService = paymentService;
            _packs = packs;
            _whatsApp = whatsApp;
            _logger = logger;
        }

        public IReadOnlyList<PackDto> GetPacks()
            => _packs.Select(p => new PackDto(p.Name, p.Price, p.Credits)).ToList();

        /// <summary>
        /// Achète un pack : crée une <see cref="CreditTransaction"/> (Pending), appelle le paiement,
        /// puis complète la transaction et crédite le vendeur en cas de succès.
        /// </summary>
        public async Task<PaymentResponseDto> BuyPackAsync(BuyPackRequest request)
        {
            var vendor = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == request.VendorId && u.Role == UserRole.Vendor)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            var pack = _packs.FirstOrDefault(p =>
                    string.Equals(p.Name, request.PackName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Pack inconnu.");

            // Transaction Pending avec une référence provisoire (le paiement fournira la vraie référence).
            var transaction = new CreditTransaction(
                vendor.Id,
                pack.Price,
                pack.Credits,
                $"PENDING-{Guid.NewGuid():N}");

            _context.CreditTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            var payment = await _paymentService.RequestPaymentAsync(vendor.Id, pack.Name, pack.Price);

            if (!payment.Success)
            {
                transaction.MarkFailed();
                await _context.SaveChangesAsync();
                _logger.LogWarning("Paiement du pack {Pack} refusé pour {Vendor} : {Error}",
                    pack.Name, vendor.Username, payment.ErrorMessage);

                return new PaymentResponseDto(
                    false,
                    payment.TransactionReference ?? string.Empty,
                    $"Paiement refusé : {payment.ErrorMessage}");
            }

            transaction.Complete(payment.TransactionReference ?? $"PAY-{Guid.NewGuid():N}");
            vendor.AddCredits(pack.Credits);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Pack {Pack} acheté par {Vendor} — {Credits} crédits ajoutés (réf {Ref}).",
                pack.Name, vendor.Username, pack.Credits, transaction.TransactionReference);

            // Confirmation WhatsApp (best effort).
            try
            {
                await _whatsApp.SendCreditPurchaseConfirmationAsync(vendor, pack);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Confirmation d'achat WhatsApp impossible pour {Vendor}.", vendor.Username);
            }

            return new PaymentResponseDto(
                true,
                transaction.TransactionReference,
                $"Pack « {pack.Name} » acheté : {pack.Credits} crédits ajoutés au vendeur.");
        }
    }
}
