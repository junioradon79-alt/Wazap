using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Commandes texte du livreur qui font avancer une course : « RECU » (colis récupéré, en route)
/// et « LIVRE [code course] [CODE &lt;4 chiffres&gt;] » (remise au client, avec preuve de livraison).
///
/// Extrait du contrôleur webhook : ce bloc était le plus gros du <c>switch</c> (plus de 120 lignes)
/// et il concentre trois responsabilités sensibles — la garantie « Colis Sûr » (code client exigé,
/// compteur anti-force brute), la clôture d'une tournée multi-clients (jamais tout d'un coup sans
/// « LIVRE TOUT » explicite) et le programme Ambassadeur (seuils de récompense). Le sortir du
/// contrôleur permet de le tester directement, sans passer par une requête HTTP signée.
/// </summary>
public sealed class RiderDeliveryCommands
{
    private readonly ApplicationDbContext _context;
    private readonly DeliveryProofOptions _deliveryProof;
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly RiderProgramService _riderProgram;
    private readonly ClientOptions _clientOptions;
    private readonly ILogger<RiderDeliveryCommands> _logger;

    public RiderDeliveryCommands(
        ApplicationDbContext context,
        DeliveryProofOptions deliveryProof,
        WhatsAppOrchestrationService whatsApp,
        RiderProgramService riderProgram,
        ILogger<RiderDeliveryCommands> logger,
        ClientOptions? clientOptions = null)
    {
        _context = context;
        _deliveryProof = deliveryProof;
        _whatsApp = whatsApp;
        _riderProgram = riderProgram;
        _logger = logger;
        _clientOptions = clientOptions ?? new ClientOptions();
    }

    /// <summary>
    /// Reconnaît les messages pris en charge ici :
    /// - Prise en charge / départ en livraison : RECU, EN ROUTE, PARTI, DECLENCHER, LIVRAISON DECLENCHEE
    /// - Remise au client : LIVRE [code course] [CODE <4 chiffres>]
    /// </summary>
    public static bool Matches(string upperText)
        => upperText == "RECU" || upperText.StartsWith("RECU ")
           || upperText == "EN ROUTE" || upperText.StartsWith("EN ROUTE ")
           || upperText == "PARTI" || upperText.StartsWith("PARTI ")
           || upperText == "DECLENCHER" || upperText.StartsWith("DECLENCHER ")
           || upperText == "LIVRAISON DECLENCHEE" || upperText.StartsWith("LIVRAISON DECLENCHEE ")
           || upperText == "LIVRE" || upperText.StartsWith("LIVRE ");

    /// <summary>Traite la commande. <paramref name="reply"/> envoie la réponse WhatsApp au livreur.</summary>
    public async Task HandleAsync(User user, string command, Func<User, string, Task> reply)
    {
        var upper = command.ToUpperInvariant();
        var isPickup = upper == "RECU" || upper.StartsWith("RECU ")
                       || upper == "EN ROUTE" || upper.StartsWith("EN ROUTE ")
                       || upper == "PARTI" || upper.StartsWith("PARTI ")
                       || upper == "DECLENCHER" || upper.StartsWith("DECLENCHER ")
                       || upper == "LIVRAISON DECLENCHEE" || upper.StartsWith("LIVRAISON DECLENCHEE ");
        var marker = isPickup ? "RECU" : "LIVRE";
        var prefix = isPickup
            ? (upper.StartsWith("LIVRAISON DECLENCHEE") ? "LIVRAISON DECLENCHEE"
               : upper.StartsWith("EN ROUTE") ? "EN ROUTE"
               : upper.Split(' ')[0])
            : "LIVRE";
        var code = command.Length > prefix.Length ? command[prefix.Length..].Trim() : string.Empty;

        // Preuve de livraison : « LIVRE <code course> CODE <4 chiffres> ».
        string? clientCode = null;
        if (marker == "LIVRE")
            (code, clientCode) = RiderCommandParser.SplitDeliveryCommand(code);

        var targetStatus = marker == "RECU"
            ? OrderStatus.RiderAssigned
            : OrderStatus.InTransit;

        var orders = await _context.Orders
            .Where(o => o.RiderUserId == user.Id && o.Status == targetStatus)
            .ToListAsync();

        var closeAll = marker == "LIVRE" && code.Equals("TOUT", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(code) && !closeAll)
            orders = orders.Where(o => o.Id.ToString("N").StartsWith(code, StringComparison.OrdinalIgnoreCase)).ToList();

        // Tournée multi-clients : on ne clôture JAMAIS toutes les livraisons d'un coup
        // (sauf « LIVRE TOUT » explicite) pour notifier chaque client au bon moment.
        if (marker == "LIVRE" && orders.Count > 1 && string.IsNullOrWhiteSpace(code))
        {
            var proofActive = _deliveryProof.RequireClientCode || clientCode is not null;
            await reply(user,
                "ℹ️ Plusieurs livraisons en cours.\n" +
                (proofActive
                    ? "Envoyez LIVRE <code> CODE <4 chiffres> après CHAQUE livraison (ex : LIVRE A1B2C3D4 CODE 1234)."
                    : "Envoyez LIVRE <code> après CHAQUE livraison (ex : LIVRE A1B2C3D4), ou LIVRE TOUT pour tout clôturer."));
            return;
        }

        if (orders.Count == 0)
        {
            await reply(user, marker == "RECU"
                ? "ℹ️ Aucune course à récupérer pour le moment."
                : "ℹ️ Aucune course en cours de livraison.");
            return;
        }

        // Preuve de remise : le code du client est vérifié dès qu'il est fourni, et
        // exigé quand « DeliveryProof:RequireClientCode » est actif.
        if (marker == "LIVRE" && (_deliveryProof.RequireClientCode || clientCode is not null))
        {
            if (_deliveryProof.RequireClientCode && closeAll)
            {
                await reply(user,
                    "🔐 Clôture groupée impossible : chaque livraison se confirme avec le code de son client.\n" +
                    "Envoyez LIVRE <code> CODE <4 chiffres> après chaque remise.");
                return;
            }

            if (orders.Count != 1)
            {
                await reply(user,
                    "ℹ️ Précisez la course : LIVRE <code> CODE <4 chiffres> (ex : LIVRE A1B2C3D4 CODE 1234).");
                return;
            }

            // NotSet = course antérieure à la preuve de livraison (aucun code envoyé au
            // client) : on laisse clôturer, sans quoi ces courses resteraient bloquées.
            var verification = orders[0].VerifyDeliveryCode(clientCode);
            if (verification is DeliveryCodeResult.Mismatch or DeliveryCodeResult.Locked)
            {
                // Persiste la tentative erronée (compteur anti-force brute).
                await _context.SaveChangesAsync();

                var remaining = Order.MaxDeliveryCodeAttempts - orders[0].DeliveryCodeAttempts;
                await reply(user, verification == DeliveryCodeResult.Mismatch
                    ? $"❌ Code incorrect. Demandez au client le code à 4 chiffres reçu par WhatsApp.\n" +
                      $"Tentative(s) restante(s) : {remaining}."
                    : "🔒 Trop de tentatives erronées. Contactez le vendeur : lui seul peut clôturer cette course.");
                return;
            }
        }

        foreach (var order in orders)
        {
            if (marker == "RECU")
            {
                order.MarkReadyForPickup();
                order.MarkPickedUp();
                order.MarkInTransit();
            }
            else
            {
                order.MarkDelivered();
            }
        }

        await _context.SaveChangesAsync();

        // 1) Événement « Livraison déclenchée » : notifications GPS en temps réel (Client + Vendeur).
        if (marker == "RECU")
        {
            foreach (var order in orders)
            {
                try
                {
                    var trackingUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}";
                    await _whatsApp.SendInTransitNotificationAsync(order, user, trackingUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Notification en transit impossible pour {OrderId}.", order.Id);
                }
            }
        }

        // 2) Notification « livré » à CHAQUE client et vendeur concerné (best-effort).
        if (marker == "LIVRE")
        {
            var vendorDashboardUrl = "https://junioradon79gm-001-site1.jtempurl.com/app/vendor/dashboard";
            foreach (var order in orders)
            {
                try
                {
                    await _whatsApp.SendDeliveredNotificationsAsync(order, user, vendorDashboardUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Notification de livraison impossible pour {OrderId}.", order.Id);
                }
            }

            // Programme « Ambassadeur WAZAP » : franchissement de seuil (livreur + parrain).
            try
            {
                await _riderProgram.NotifyMilestonesAsync(user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification du programme Ambassadeur impossible pour {Rider}.", user.Username);
            }
        }

        await reply(user, marker == "RECU"
            ? $"✅ Colis récupéré ({orders.Count} course(s)) — en route !"
            : $"✅ {orders.Count} course(s) livrée(s). Merci ! 🎉");
    }
}
