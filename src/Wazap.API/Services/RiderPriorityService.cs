using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Catalogue des packs prioritaires LIVREUR + achat (paiement Mobile Money →
    /// <see cref="RiderPriorityPurchase"/> → priorité de proposition du livreur) + lecture de l'état.
    /// WAZAP vend la <b>visibilité</b> (être proposé en premier dans son rayon), jamais une
    /// attribution garantie — voir <see cref="RiderPriorityOptions"/>.
    /// </summary>
    public sealed class RiderPriorityService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentService _paymentService;
        private readonly IReadOnlyList<RiderPriorityPackConfiguration> _packs;
        private readonly RiderPriorityOptions _options;
        private readonly ILogger<RiderPriorityService> _logger;

        public RiderPriorityService(
            ApplicationDbContext context,
            IPaymentService paymentService,
            IReadOnlyList<RiderPriorityPackConfiguration> packs,
            RiderPriorityOptions options,
            ILogger<RiderPriorityService> logger)
        {
            _context = context;
            _paymentService = paymentService;
            _packs = packs;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// Catalogue des packs prioritaires. Toujours lisible (l'application peut l'afficher comme
        /// « bientôt disponible ») ; seul l'achat dépend de <see cref="RiderPriorityOptions.Enabled"/>.
        /// </summary>
        public IReadOnlyList<RiderPriorityPackDto> GetPacks()
            => _packs.Select(p => new RiderPriorityPackDto(p.Name, p.Price, p.Days)).ToList();

        /// <summary>
        /// État de priorité d'un livreur + catalogue (null si le compte n'est pas un livreur).
        /// </summary>
        public async Task<RiderPriorityStatusDto?> GetStatusAsync(Guid riderId)
        {
            var rider = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == riderId && u.Role == UserRole.Rider);

            if (rider is null)
                return null;

            var nowUtc = DateTime.UtcNow;
            return new RiderPriorityStatusDto(
                rider.Id,
                rider.HasActivePriority(nowUtc),
                rider.PriorityUntilUtc,
                rider.RemainingPriorityDays(nowUtc),
                _options.Enabled,
                GetPacks());
        }

        /// <summary>
        /// Achète un pack prioritaire : crée un <see cref="RiderPriorityPurchase"/> (Pending) et
        /// initie le paiement (même chaîne que les packs vendeurs).
        /// - Flux asynchrone (GeniusPay) : retourne le <see cref="PaymentResponseDto.PaymentLink"/> ;
        ///   la complétion est faite par le webhook (<see cref="CompletePurchaseAsync"/>).
        /// - Flux synchrone (mock) : complète immédiatement.
        /// </summary>
        public async Task<PaymentResponseDto> BuyAsync(BuyRiderPriorityRequest request)
        {
            if (!_options.Enabled)
                throw new InvalidOperationException(
                    $"Le pack prioritaire livreur n'est pas ouvert à l'achat ({RiderPriorityOptions.SectionName}:Enabled=false).");

            if (_packs.Count == 0)
                throw new InvalidOperationException("Aucun pack prioritaire n'est configuré (section « RiderPriorityPacks »).");

            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == request.RiderId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var pack = _packs.FirstOrDefault(p =>
                    string.Equals(p.Name, request.PackName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Pack inconnu.");

            // Référence provisoire (RDRP-PENDING-…) : l'agrégateur fournira la sienne.
            var purchase = new RiderPriorityPurchase(rider.Id, pack.Price, pack.Days, null, pack.Name);
            _context.RiderPriorityPurchases.Add(purchase);
            await _context.SaveChangesAsync();

            var payment = await _paymentService.RequestPaymentAsync(
                rider.Id, pack.Name, pack.Price, purchase.Id.ToString());

            if (!payment.Success)
            {
                purchase.MarkFailed();
                await _context.SaveChangesAsync();
                _logger.LogWarning("Paiement du pack prioritaire {Pack} refusé pour {Rider} : {Error}",
                    pack.Name, rider.Username, payment.ErrorMessage);

                return new PaymentResponseDto(
                    false,
                    payment.TransactionReference ?? string.Empty,
                    null,
                    $"Paiement refusé : {payment.ErrorMessage}");
            }

            if (!string.IsNullOrWhiteSpace(payment.TransactionReference))
                purchase.SetTransactionReference(payment.TransactionReference!);
            await _context.SaveChangesAsync();

            if (payment.PaymentLink is not null)
            {
                // Flux asynchrone : le livreur paie sur la page de l'agrégateur.
                return new PaymentResponseDto(
                    true,
                    purchase.TransactionReference,
                    payment.PaymentLink,
                    $"Redirection vers le paiement du pack « {pack.Name} » ({pack.Price:F0} FCFA).");
            }

            // Flux synchrone (mock) : complétion immédiate.
            await CompletePurchaseAsync(purchase.Id, purchase.TransactionReference);

            return new PaymentResponseDto(
                true,
                purchase.TransactionReference,
                null,
                $"Pack « {pack.Name} » activé : priorité de proposition pendant {pack.Days} jour(s).");
        }

        /// <summary>
        /// Complète un achat (webhook GeniusPay, réconciliation ou flux mock synchrone) :
        /// statut Completed puis priorité du livreur prolongée de la durée du pack. Idempotent.
        /// </summary>
        public async Task CompletePurchaseAsync(Guid purchaseId, string paymentReference)
        {
            var purchase = await _context.RiderPriorityPurchases.FirstOrDefaultAsync(p => p.Id == purchaseId)
                ?? throw new InvalidOperationException("Achat de pack prioritaire introuvable.");

            if (purchase.Status == TransactionStatus.Completed)
                return; // Webhook dupliqué : ne jamais prolonger la priorité deux fois.

            purchase.Complete(paymentReference);

            var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == purchase.RiderUserId);
            if (rider is null)
            {
                _logger.LogWarning("Livreur introuvable pour l'achat prioritaire {PurchaseId}.", purchaseId);
                return;
            }

            rider.GrantPriority(purchase.Days);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Pack prioritaire {Pack} activé pour {Rider} — {Days} jour(s), échéance {Until:u} (réf {Ref}).",
                purchase.PackName ?? "?", rider.Username, purchase.Days, rider.PriorityUntilUtc,
                purchase.TransactionReference);
        }

        /// <summary>
        /// Marque un achat en échec (webhook « payment.failed »). Idempotent.
        /// </summary>
        public async Task FailPurchaseAsync(Guid purchaseId)
        {
            var purchase = await _context.RiderPriorityPurchases.FirstOrDefaultAsync(p => p.Id == purchaseId);
            if (purchase is null || purchase.Status != TransactionStatus.Pending)
                return;

            purchase.MarkFailed();
            await _context.SaveChangesAsync();
            _logger.LogWarning("Achat prioritaire {PurchaseId} marqué en échec (webhook).", purchaseId);
        }
    }
}