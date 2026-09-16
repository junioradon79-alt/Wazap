using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;

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
        // P2 / C-12 : dépendance au PORT (voir PackService) — le service n'a besoin que de
        // Users, RiderPriorityPurchases, SaveChanges et Database, tous exposés par le port.
        private readonly IApplicationDbContext _context;
        private readonly IPaymentService _paymentService;
        private readonly IReadOnlyList<RiderPriorityPackConfiguration> _packs;
        private readonly RiderPriorityOptions _options;
        private readonly WhatsAppOrchestrationService _whatsApp;
        private readonly ILogger<RiderPriorityService> _logger;

        public RiderPriorityService(
            IApplicationDbContext context,
            IPaymentService paymentService,
            IReadOnlyList<RiderPriorityPackConfiguration> packs,
            RiderPriorityOptions options,
            WhatsAppOrchestrationService whatsApp,
            ILogger<RiderPriorityService> logger)
        {
            _context = context;
            _paymentService = paymentService;
            _packs = packs;
            _options = options;
            _whatsApp = whatsApp;
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
        /// <para>
        /// La réclamation de l'achat est ATOMIQUE (UPDATE conditionnel + transaction) : sans
        /// cela, le webhook et le <c>PaymentReconciliationWorker</c> pouvaient prolonger la
        /// priorité DEUX FOIS pour un seul paiement, ou marquer l'achat complété sans jamais
        /// accorder la priorité si le livreur était introuvable.
        /// </para>
        /// </summary>
        public async Task CompletePurchaseAsync(Guid purchaseId, string paymentReference, CancellationToken ct = default)
        {
            var purchase = await _context.RiderPriorityPurchases.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == purchaseId)
                ?? throw new InvalidOperationException("Achat de pack prioritaire introuvable.");

            if (purchase.Status == TransactionStatus.Completed)
                return; // Webhook dupliqué : ne jamais prolonger la priorité deux fois.

            if (purchase.Status == TransactionStatus.Failed)
                throw new InvalidOperationException("Une transaction en échec ne peut pas être complétée.");

            var rider = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == purchase.RiderUserId);
            if (rider is null)
            {
                // Ne pas « compléter » un achat dont le bénéficiaire est introuvable : laissé
                // Pending, il reste rattrapable plutôt que perdu en silence.
                _logger.LogError("Livreur introuvable pour l'achat prioritaire {PurchaseId} : priorité non accordée.",
                    purchaseId);
                return;
            }

            var relational = _context.Database.IsRelational();
            await using var dbTransaction = relational
                ? await _context.Database.BeginTransactionAsync()
                : null;

            if (relational)
            {
                var claimed = await _context.RiderPriorityPurchases
                    .Where(p => p.Id == purchaseId && p.Status == TransactionStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.Status, TransactionStatus.Completed)
                        .SetProperty(p => p.TransactionReference, paymentReference)
                        .SetProperty(p => p.CompletedAt, (DateTime?)DateTime.UtcNow));

                if (claimed == 0)
                    return; // Un autre traitement a déjà complété cet achat.
            }
            else
            {
                // Fournisseur non relationnel (tests InMemory) : pas d'UPDATE conditionnel.
                var tracked = await _context.RiderPriorityPurchases.FirstOrDefaultAsync(p => p.Id == purchaseId)
                    ?? throw new InvalidOperationException("Achat de pack prioritaire introuvable.");
                if (tracked.Status != TransactionStatus.Pending)
                    return;

                tracked.Complete(paymentReference);
            }

            var trackedRider = await _context.Users.FirstOrDefaultAsync(u => u.Id == purchase.RiderUserId);
            if (trackedRider is null)
            {
                _logger.LogError("Livreur introuvable pour l'achat prioritaire {PurchaseId} : priorité non accordée.",
                    purchaseId);
                return;
            }

            trackedRider.GrantPriority(purchase.Days);
            await _context.SaveChangesAsync();

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync();

            var priorityUntil = trackedRider.PriorityUntilUtc;

            _logger.LogInformation(
                "Pack prioritaire {Pack} activé pour {Rider} — {Days} jour(s), échéance {Until:u} (réf {Ref}).",
                purchase.PackName ?? "?", trackedRider.Username, purchase.Days, priorityUntil,
                paymentReference);

            // Confirmation WhatsApp au livreur (best effort, comme les packs vendeurs) :
            // un échec d'envoi ne doit jamais invalider l'activation de la priorité.
            try
            {
                await _whatsApp.SendRiderPriorityPurchaseConfirmationAsync(
                    trackedRider, purchase.PackName ?? "Priorité livreur", purchase.Days, priorityUntil, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Confirmation d'activation prioritaire WhatsApp impossible pour {Rider}.", trackedRider.Username);
            }
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