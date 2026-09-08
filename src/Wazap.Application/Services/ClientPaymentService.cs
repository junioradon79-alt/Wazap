using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;

namespace Wazap.Application.Services;

/// <summary>
/// Paiement du panier client par Mobile Money (GeniusPay) : initiation du lien de paiement,
/// complétion idempotente (webhook + réconciliation), commission WAZAP et montant net dû
/// au vendeur (versement tracé). Non bloquant par défaut : le client peut toujours payer
/// en espèces à la livraison ; l'option <c>RequirePaymentBeforeDispatch</c> gèle la
/// diffusion des livreurs tant que rien n'est payé.
/// </summary>
public sealed class ClientPaymentService
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IWhatsAppSender _whatsApp;
    private readonly DeliveryOfferService _deliveryOfferService;
    private readonly ClientPaymentOptions _options;
    private readonly ILogger<ClientPaymentService> _logger;

    public ClientPaymentService(
        IApplicationDbContext context,
        IPaymentService paymentService,
        IWhatsAppSender whatsApp,
        DeliveryOfferService deliveryOfferService,
        ClientPaymentOptions options,
        ILogger<ClientPaymentService> logger)
    {
        _context = context;
        _paymentService = paymentService;
        _whatsApp = whatsApp;
        _deliveryOfferService = deliveryOfferService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Initialise (ou renvoie) le lien de paiement du panier client. Idempotent : une
    /// session d'encaissement en attente avec un lien renvoie le MÊME lien — le client
    /// qui a quitté WhatsApp ne doit pas générer une seconde session chez l'agrégateur.
    /// </summary>
    public async Task<ClientPaymentResultDto> RequestPaymentAsync(Guid orderId)
    {
        if (!_options.Enabled)
            return new ClientPaymentResultDto(false, "Disabled", 0, null,
                "Le paiement client n'est pas activé.");

        try
        {
            var order = await _context.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order is null)
                return new ClientPaymentResultDto(false, "NotFound", 0, null, "Commande introuvable.");

            if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
                return new ClientPaymentResultDto(false, order.Status.ToString(), order.Amount, null,
                    $"Commande {order.Status} : le paiement n'est plus possible.");

            var pending = await _context.OrderPayments
                .Where(p => p.OrderId == orderId && p.Status == TransactionStatus.Pending)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (pending is not null && !string.IsNullOrWhiteSpace(pending.PaymentLink))
                return new ClientPaymentResultDto(true, "Pending", pending.Amount, pending.PaymentLink, null);

            // --- SUITE-INITIATION ---

            var payment = new OrderPayment(orderId, order.Amount);
            _context.OrderPayments.Add(payment);

            // La référence passée à l'agrégateur est l'ID interne (metadata wazap_transaction_id) :
            // c'est elle qui permet au webhook de retrouver le paiement.
            var paymentResult = await _paymentService.RequestPaymentAsync(
                order.VendorUserId ?? Guid.Empty,
                $"Commande WAZAP #{OrderCode(orderId)}",
                order.Amount,
                payment.Id.ToString());

            if (!paymentResult.Success)
            {
                payment.MarkFailed();
                await _context.SaveChangesAsync();
                _logger.LogWarning("Initiation du paiement client impossible pour {OrderId} : {Error}",
                    orderId, paymentResult.ErrorMessage);
                return new ClientPaymentResultDto(false, "Failed", order.Amount, null,
                    paymentResult.ErrorMessage ?? "Erreur de la passerelle de paiement.");
            }

            if (!string.IsNullOrWhiteSpace(paymentResult.TransactionReference))
                payment.SetTransactionReference(paymentResult.TransactionReference);
            if (!string.IsNullOrWhiteSpace(paymentResult.PaymentLink))
                payment.SetPaymentLink(paymentResult.PaymentLink);

            await _context.SaveChangesAsync();

            // Lien au client (best effort) : le texte n'aboutit que dans la fenêtre 24 h,
            // mais le lien reste disponible sur la page de suivi.
            if (!string.IsNullOrWhiteSpace(payment.PaymentLink))
            {
                try
                {
                    await _whatsApp.SendTextMessageAsync(
                        order.ClientWhatsAppNumber,
                        $"💳 Payez votre commande #{OrderCode(orderId)} en Mobile Money " +
                        $"({Format(payment.Amount)} FCFA) :\n{payment.PaymentLink}\n" +
                        "Ou en espèces à la livraison.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Envoi du lien de paiement WhatsApp impossible pour {OrderId}.", orderId);
                }
            }

            _logger.LogInformation("Paiement client initié pour la commande {OrderId} ({Amount} FCFA).",
                orderId, payment.Amount);

            return new ClientPaymentResultDto(true, "Pending", payment.Amount, payment.PaymentLink, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'initiation du paiement client pour {OrderId}.", orderId);
            return new ClientPaymentResultDto(false, "Error", 0, null,
                "Une erreur est survenue lors de l'initiation du paiement.");
        }
    }

    /// <summary>
    /// Complète un paiement (webhook GeniusPay ou réconciliation) : statut Completed,
    /// commission calculée, montant net dû au vendeur, notifications WhatsApp. Idempotent.
    /// </summary>
    public async Task CompletePaymentAsync(Guid paymentId, string paymentReference)
    {
        var payment = await _context.OrderPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null)
        {
            _logger.LogWarning("Paiement client {PaymentId} introuvable (webhook).", paymentId);
            return;
        }

        if (payment.Status != TransactionStatus.Pending)
            return; // Webhook dupliqué ou paiement déjà réconcilié : ne rien refaire.

        // Garde-fou double encaissement : une autre session pour la même commande est
        // déjà complétée. L'argent touché deux fois devra être remboursé côté agrégateur.
        var alreadyPaid = await _context.OrderPayments.AsNoTracking().AnyAsync(p =>
            p.OrderId == payment.OrderId
            && p.Id != paymentId
            && p.Status == TransactionStatus.Completed);
        if (alreadyPaid)
        {
            _logger.LogWarning(
                "Paiement {PaymentId} complété alors que la commande {OrderId} l'était déjà — remboursement à traiter chez l'agrégateur.",
                paymentId, payment.OrderId);
            payment.MarkFailed();
            await _context.SaveChangesAsync();
            return;
        }

        payment.Complete(paymentReference, _options.CommissionPercent);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Paiement client complété : {PaymentId} pour la commande {OrderId} ({Amount} FCFA, commission {Commission}).",
            payment.Id, payment.OrderId, payment.Amount, payment.CommissionAmount);

        await NotifySuccessAsync(payment);
        await TryDispatchAfterPaymentAsync(payment.OrderId);
    }

    /// <summary>Marque un paiement en échec (webhook GeniusPay « payment.failed »). Idempotent.</summary>
    public async Task FailPaymentAsync(Guid paymentId)
    {
        var payment = await _context.OrderPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null || payment.Status != TransactionStatus.Pending)
            return;

        payment.MarkFailed();
        await _context.SaveChangesAsync();
        _logger.LogWarning("Paiement client {PaymentId} marqué en échec.", paymentId);
    }

    /// <summary>
    /// La diffusion des livreurs doit-elle attendre un paiement ? (option bloquante :
    /// un paiement client complété est requis pour la commande).
    /// </summary>
    public async Task<bool> IsDispatchBlockedAsync(Guid orderId)
    {
        if (!_options.RequirePaymentBeforeDispatch)
            return false;

        return !await _context.OrderPayments.AsNoTracking()
            .AnyAsync(p => p.OrderId == orderId && p.Status == TransactionStatus.Completed);
    }

    /// <summary>
    /// Paiement bloquant : une fois payé, la course reprend son cours — diffusion si les
    /// coordonnées du client sont déjà connues, sinon routage classique (lien de suivi).
    /// </summary>
    private async Task TryDispatchAfterPaymentAsync(Guid orderId)
    {
        if (!_options.RequirePaymentBeforeDispatch)
            return;

        var order = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null || order.Status != OrderStatus.VendorConfirmed)
            return;

        try
        {
            if (order.ClientLatitude is not null && order.ClientLongitude is not null)
                await _deliveryOfferService.DispatchConfirmedOrderAsync(orderId);
            else
                await _deliveryOfferService.ConfirmAndRouteAsync(orderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Diffusion impossible après paiement de la commande {OrderId}.", orderId);
        }
    }

    private async Task NotifySuccessAsync(OrderPayment payment)
    {
        var order = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId);
        if (order is null)
        {
            _logger.LogWarning("Commande {OrderId} introuvable pour la notification de paiement.",
                payment.OrderId);
            return;
        }

        try
        {
            await _whatsApp.SendTextMessageAsync(
                order.ClientWhatsAppNumber,
                $"✅ Paiement reçu pour la commande #{OrderCode(payment.OrderId)} " +
                $"({Format(payment.Amount)} FCFA). Le vendeur a été informé, la livraison suit son cours.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Confirmation client impossible pour la commande {OrderId}.", payment.OrderId);
        }

        try
        {
            await _whatsApp.SendTextMessageAsync(
                order.VendorWhatsAppNumber,
                $"💰 Commande #{OrderCode(payment.OrderId)} PAYÉE par le client : " +
                $"{Format(payment.Amount)} FCFA encaissés par WAZAP. " +
                $"Montant net à vous reverser : {Format(payment.VendorPayoutDue)} FCFA.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification vendeur impossible pour la commande {OrderId}.", payment.OrderId);
        }
    }

    private static string OrderCode(Guid orderId)
        => orderId.ToString("N")[..8].ToUpperInvariant();

    private static string Format(decimal amount)
        => amount.ToString("0", CultureInfo.InvariantCulture);
}