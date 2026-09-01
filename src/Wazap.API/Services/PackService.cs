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
        /// Historique des achats de crédits d'un vendeur (du plus récent au plus ancien).
        /// </summary>
        public async Task<IReadOnlyList<CreditTransactionDto>> GetVendorTransactionsAsync(Guid vendorId)
        {
            return await _context.CreditTransactions.AsNoTracking()
                .Where(t => t.VendorId == vendorId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new CreditTransactionDto(
                    t.Id,
                    t.VendorId,
                    t.Amount,
                    t.CreditsPurchased,
                    t.CreatedAt,
                    t.TransactionReference,
                    t.Status))
                .ToListAsync();
        }

        /// <summary>
        /// Achète un pack : crée une <see cref="CreditTransaction"/> (Pending) et initie le paiement.
        /// - Flux asynchrone (GeniusPay) : retourne le <see cref="PaymentResponseDto.PaymentLink"/> ;
        ///   la complétion est faite par le webhook (<see cref="CompletePurchaseAsync"/>).
        /// - Flux synchrone (mock) : complète immédiatement.
        /// </summary>
        public async Task<PaymentResponseDto> BuyPackAsync(BuyPackRequest request)
        {
            var vendor = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == request.VendorId && u.Role == UserRole.Vendor)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            var pack = _packs.FirstOrDefault(p =>
                    string.Equals(p.Name, request.PackName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Pack inconnu.");

            // Transaction Pending avec une référence provisoire (l'agrégateur fournira la vraie référence).
            var transaction = new CreditTransaction(
                vendor.Id,
                pack.Price,
                pack.Credits,
                $"PENDING-{Guid.NewGuid():N}",
                pack.Name);

            _context.CreditTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            var payment = await _paymentService.RequestPaymentAsync(
                vendor.Id, pack.Name, pack.Price, transaction.Id.ToString());

            if (!payment.Success)
            {
                transaction.MarkFailed();
                await _context.SaveChangesAsync();
                _logger.LogWarning("Paiement du pack {Pack} refusé pour {Vendor} : {Error}",
                    pack.Name, vendor.Username, payment.ErrorMessage);

                return new PaymentResponseDto(
                    false,
                    payment.TransactionReference ?? string.Empty,
                    null,
                    $"Paiement refusé : {payment.ErrorMessage}");
            }

            // Mémoriser la référence de transaction de l'agrégateur.
            if (!string.IsNullOrWhiteSpace(payment.TransactionReference))
                transaction.SetTransactionReference(payment.TransactionReference!);
            await _context.SaveChangesAsync();

            if (payment.PaymentLink is not null)
            {
                // Flux asynchrone : le client paie sur la page de l'agrégateur.
                return new PaymentResponseDto(
                    true,
                    transaction.TransactionReference,
                    payment.PaymentLink,
                    $"Redirection vers le paiement du pack « {pack.Name} » ({pack.Price:F0} FCFA).");
            }

            // Flux synchrone (mock) : complétion immédiate.
            await CompletePurchaseAsync(transaction.Id, transaction.TransactionReference);

            return new PaymentResponseDto(
                true,
                transaction.TransactionReference,
                null,
                $"Pack « {pack.Name} » acheté : {pack.Credits} crédits ajoutés au vendeur.");
        }

        /// <summary>
        /// Complète une transaction (appelé par le webhook GeniusPay ou le flux mock synchrone) :
        /// statut Completed, crédits ajoutés au vendeur, notification WhatsApp. Idempotent.
        /// </summary>
        public async Task CompletePurchaseAsync(Guid transactionId, string paymentReference)
        {
            var transaction = await _context.CreditTransactions.FirstOrDefaultAsync(t => t.Id == transactionId)
                ?? throw new InvalidOperationException("Transaction introuvable.");

            if (transaction.Status == TransactionStatus.Completed)
                return; // Webhook dupliqué : ne rien re-créditer.

            transaction.Complete(paymentReference);

            var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == transaction.VendorId);
            if (vendor is null)
            {
                _logger.LogWarning("Vendeur introuvable pour la transaction {TransactionId}.", transactionId);
                return;
            }

            vendor.AddCredits(transaction.CreditsPurchased);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Pack {Pack} acheté par {Vendor} — {Credits} crédits ajoutés (réf {Ref}).",
                transaction.PackName ?? "?", vendor.Username, transaction.CreditsPurchased, transaction.TransactionReference);

            var pack = transaction.PackName is null
                ? null
                : _packs.FirstOrDefault(p => string.Equals(p.Name, transaction.PackName, StringComparison.OrdinalIgnoreCase));

            if (pack is not null)
            {
                try
                {
                    await _whatsApp.SendCreditPurchaseConfirmationAsync(vendor, pack);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Confirmation d'achat WhatsApp impossible pour {Vendor}.", vendor.Username);
                }
            }
        }

        /// <summary>
        /// Marque une transaction en échec (webhook GeniusPay « payment.failed »). Idempotent.
        /// </summary>
        public async Task FailPurchaseAsync(Guid transactionId)
        {
            var transaction = await _context.CreditTransactions.FirstOrDefaultAsync(t => t.Id == transactionId);
            if (transaction is null || transaction.Status != TransactionStatus.Pending)
                return;

            transaction.MarkFailed();
            await _context.SaveChangesAsync();
            _logger.LogWarning("Transaction {TransactionId} marquée en échec (webhook).", transactionId);
        }
    }
}
