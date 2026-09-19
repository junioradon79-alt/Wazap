using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

/// <summary>
/// Parcours acheteur (page de suivi PWA) : le client consulte sa commande, valide
/// ses coordonnées — ce qui déclenche AUTOMATIQUEMENT la recherche des livreurs —
/// et peut payer son panier par Mobile Money (non bloquant par défaut).
/// </summary>
[ApiController]
[Route("api/client/orders")]
public class ClientOrdersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly DeliveryOfferService _deliveryOfferService;
    private readonly ClientPaymentService _clientPayments;
    private readonly RiderService _riderService;
    private readonly WhatsAppOrchestrationService? _whatsApp;
    private readonly ClientOptions _clientOptions;

    public ClientOrdersController(
        ApplicationDbContext context,
        DeliveryOfferService deliveryOfferService,
        ClientPaymentService clientPayments,
        RiderService riderService,
        WhatsAppOrchestrationService? whatsApp = null,
        ClientOptions? clientOptions = null)
    {
        _context = context;
        _deliveryOfferService = deliveryOfferService;
        _clientPayments = clientPayments;
        _riderService = riderService;
        _whatsApp = whatsApp;
        _clientOptions = clientOptions ?? new ClientOptions();
    }

    // GET: api/client/orders/{id} — état visible par le client (public, id non devinable)
    [HttpGet("{id:guid}")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> Get(Guid id)
    {
        var order = await _context.Orders.AsNoTracking()
            .Include(o => o.OrderLines)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound();

        var vendorName = order.VendorUserId is { } vendorId
            ? await _context.Users.AsNoTracking()
                .Where(u => u.Id == vendorId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync()
            : null;

        var riderName = order.RiderUserId is { } riderId
            ? await _context.Users.AsNoTracking()
                .Where(u => u.Id == riderId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync()
            : null;

        var rating = await _context.RiderRatings.AsNoTracking()
            .Where(r => r.OrderId == id)
            .Select(r => new { r.Score, r.Comment, r.CreatedAt })
            .FirstOrDefaultAsync();

        var payment = await _context.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Status, p.Amount, p.PaymentLink })
            .FirstOrDefaultAsync();

        return Ok(new
        {
            id = order.Id,
            code = order.Id.ToString("N")[..8].ToUpperInvariant(),
            vendorName,
            riderName,
            status = order.Status.ToString(),
            description = order.Description,
            amount = order.Amount,
            deliveryFee = order.DeliveryFee,
            totalAmount = order.TotalAmount,
            deliveryCode = order.DeliveryCode,
            qrUrl = $"/api/client/orders/{order.Id}/qr",
            trackingUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}",
            hasProofPhoto = order.DeliveryProofPhotoFileName != null,
            riderPhone = order.RiderWhatsAppNumber,
            needsCoordinates = order.RequiresClientCoordinates,
            hasCoordinates = order.ClientLatitude is not null && order.ClientLongitude is not null,
            address = order.ClientAddress,
            riderAssigned = order.Status == OrderStatus.RiderAssigned,
            delivered = order.Status == OrderStatus.Delivered,
            cancellationReason = order.CancellationReason != OrderCancellationReason.None ? order.CancellationReason.ToString() : null,
            cancellationComment = order.CancellationComment,
            timestamps = new
            {
                createdAt = order.CreatedAt,
                vendorConfirmedAt = order.VendorConfirmedAt,
                riderAssignedAt = order.RiderAssignedAt,
                pickedUpAt = order.PickedUpAt,
                deliveredAt = order.DeliveredAt,
                cancelledAt = order.CancelledAt
            },
            orderLines = order.OrderLines.Select(l => new
            {
                productName = l.ProductName,
                quantity = l.Quantity,
                unitPrice = l.UnitPrice,
                totalPrice = l.TotalPrice
            }),
            rating = rating is null ? null : new { score = rating.Score, comment = rating.Comment, createdAt = rating.CreatedAt },
            payment = payment is null
                ? null
                : new
                {
                    status = payment.Status.ToString(),
                    amount = payment.Amount,
                    paymentLink = payment.PaymentLink
                }
        });
    }

    // POST: api/client/orders/{id}/coordinates — le client valide → diffusion auto des livreurs
    [HttpPost("{id:guid}/coordinates")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> SubmitCoordinates(Guid id, [FromBody] SetClientCoordinatesRequest request)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (!order.RequiresClientCoordinates)
            return BadRequest("Le suivi n'est pas activé pour cette commande.");

        if (order.ClientLatitude is not null && order.ClientLongitude is not null)
            return Conflict(new { message = "Coordonnées déjà envoyées." });

        if (order.Status != OrderStatus.VendorConfirmed)
            return BadRequest(new { message = $"Commande {order.Status} : la validation n'est plus possible." });

        order.SetClientCoordinates(request.Latitude, request.Longitude, request.Address);
        await _context.SaveChangesAsync();

        // Déclenchement AUTOMATIQUE : recherche des livreurs avec les coordonnées client.
        var result = await _deliveryOfferService.DispatchConfirmedOrderAsync(order.Id);

        return Ok(new
        {
            status = order.Status.ToString(),
            offersCreated = result.OffersCreated,
            code = order.Id.ToString("N")[..8].ToUpperInvariant()
        });
    }

    // POST: api/client/orders/{id}/pay — le client paie son panier par Mobile Money
    // (idempotent : un paiement en attente renvoie le même lien).
    [HttpPost("{id:guid}/pay")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> Pay(Guid id)
    {
        try
        {
            var result = await _clientPayments.RequestPaymentAsync(id);
            if (!result.Success)
                return BadRequest(new { message = result.ErrorMessage });

            return Ok(new
            {
                status = result.Status,
                amount = result.Amount,
                paymentLink = result.PaymentLink
            });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "Le paiement ne peut pas être initié pour cette commande." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Erreur lors de l'initiation du paiement." });
        }
    }

    // GET: api/client/orders/{id}/rider-location — position live du livreur (suivi client)
    [HttpGet("{id:guid}/rider-location")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> RiderLocation(Guid id)
    {
        var order = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound();

        var inTransit = order.Status is OrderStatus.RiderAssigned
            or OrderStatus.ReadyForPickup
            or OrderStatus.PickedUp
            or OrderStatus.InTransit;

        if (!inTransit || order.RiderUserId is null)
            return Ok(new { tracking = false });

        var rider = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == order.RiderUserId.Value);

        if (rider is null)
            return Ok(new { tracking = true, location = (object?)null });

        return Ok(new
        {
            tracking = true,
            location = rider.Latitude is { } lat && rider.Longitude is { } lng
                ? new { latitude = lat, longitude = lng, updatedAt = rider.LocationUpdatedAt }
                : null,
            riderName = rider.Username
        });
    }

    // GET: api/client/orders/{id}/proof-photo — photo de preuve de livraison déchiffrée
    [HttpGet("{id:guid}/proof-photo")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> GetProofPhoto(Guid id)
    {
        var photo = await _riderService.GetDeliveryProofPhotoAsync(id);
        if (photo is null || photo.Value.Content is null)
            return NotFound();

        var contentType = Path.GetExtension(photo.Value.FileName) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return File(photo.Value.Content, contentType);
    }

    // POST: api/client/orders/{id}/rate — note et avis du client pour son livreur
    [HttpPost("{id:guid}/rate")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> SubmitRating(Guid id, [FromBody] SubmitClientRatingRequest request)
    {
        if (request.Score is < RiderRating.MinScore or > RiderRating.MaxScore)
            return BadRequest(new { message = $"La note doit être comprise entre {RiderRating.MinScore} et {RiderRating.MaxScore} étoiles." });

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (order.Status != OrderStatus.Delivered)
            return BadRequest(new { message = "Seule une commande livrée peut être notée." });

        if (order.RiderUserId is null)
            return BadRequest(new { message = "Aucun livreur associé à cette commande." });

        if (await _context.RiderRatings.AnyAsync(r => r.OrderId == id))
            return Conflict(new { message = "Vous avez déjà noté cette livraison." });

        var rating = new RiderRating(
            order.Id,
            order.RiderUserId.Value,
            order.ClientWhatsAppNumber,
            request.Score,
            request.Comment);

        _context.RiderRatings.Add(rating);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            score = rating.Score,
            comment = rating.Comment
        });
    }

    // GET: api/client/orders/{id}/qr — QR Code de livraison à scanner par le livreur
    [HttpGet("{id:guid}/qr")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> GetDeliveryQr(Guid id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        var code = order.EnsureDeliveryCode();
        await _context.SaveChangesAsync();

        var targetUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}?valider=1&code={code}";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qrData);
        var bytes = png.GetGraphic(10, drawQuietZones: true);

        return File(bytes, "image/png", $"wazap-delivery-qr-{order.Id.ToString("N")[..8]}.png");
    }

    // POST: api/client/orders/{id}/start-delivery — le livreur déclenche la course ("Livraison déclenchée")
    [HttpPost("{id:guid}/start-delivery")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> StartDelivery(Guid id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (order.Status == OrderStatus.InTransit)
            return Ok(new { status = order.Status.ToString(), message = "Livraison déjà en cours." });

        if (order.Status is not (OrderStatus.RiderAssigned or OrderStatus.ReadyForPickup or OrderStatus.PickedUp))
            return BadRequest(new { message = $"Impossible de démarrer la course en statut {order.Status}." });

        if (order.Status == OrderStatus.RiderAssigned)
            order.MarkReadyForPickup();
        if (order.Status == OrderStatus.ReadyForPickup)
            order.MarkPickedUp();
        if (order.Status == OrderStatus.PickedUp)
            order.MarkInTransit();

        await _context.SaveChangesAsync();

        if (_whatsApp is not null)
        {
            var rider = order.RiderUserId is { } rId
                ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == rId)
                : null;

            if (rider is not null)
            {
                var trackingUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}";
                try
                {
                    await _whatsApp.SendInTransitNotificationAsync(order, rider, trackingUrl);
                }
                catch (Exception)
                {
                    // Best effort
                }
            }
        }

        return Ok(new { status = order.Status.ToString(), message = "Livraison déclenchée avec succès." });
    }

    // POST: api/client/orders/{id}/validate-delivery — validation de remise (scan QR ou code PIN)
    [HttpPost("{id:guid}/validate-delivery")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> ValidateDelivery(Guid id, [FromBody] ValidateDeliveryRequest request)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (order.Status == OrderStatus.Delivered)
            return Ok(new { status = order.Status.ToString(), message = "Commande déjà validée et livrée." });

        if (order.Status is not (OrderStatus.RiderAssigned or OrderStatus.ReadyForPickup or OrderStatus.PickedUp or OrderStatus.InTransit))
            return BadRequest(new { message = $"Impossible de valider la livraison en statut {order.Status}." });

        var verifyResult = order.VerifyDeliveryCode(request.Code);
        if (verifyResult is DeliveryCodeResult.Mismatch or DeliveryCodeResult.Locked)
        {
            await _context.SaveChangesAsync();
            var remaining = Order.MaxDeliveryCodeAttempts - order.DeliveryCodeAttempts;
            return BadRequest(new
            {
                message = verifyResult == DeliveryCodeResult.Mismatch
                    ? "Code PIN incorrect."
                    : "Trop de tentatives erronées. Contactez le vendeur.",
                remainingAttempts = Math.Max(0, remaining)
            });
        }

        if (order.Status == OrderStatus.RiderAssigned)
            order.MarkReadyForPickup();
        if (order.Status == OrderStatus.ReadyForPickup)
            order.MarkPickedUp();
        if (order.Status == OrderStatus.PickedUp)
            order.MarkInTransit();
        if (order.Status == OrderStatus.InTransit)
            order.MarkDelivered();

        await _context.SaveChangesAsync();

        if (_whatsApp is not null)
        {
            var rider = order.RiderUserId is { } rId
                ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == rId)
                : null;

            if (rider is not null)
            {
                var vendorDashboardUrl = "https://junioradon79gm-001-site1.jtempurl.com/app/vendor/dashboard";
                try
                {
                    await _whatsApp.SendDeliveredNotificationsAsync(order, rider, vendorDashboardUrl);
                }
                catch (Exception)
                {
                    // Best effort
                }
            }
        }

        return Ok(new
        {
            status = order.Status.ToString(),
            message = "Livraison validée avec succès !",
            amount = order.Amount,
            deliveryFee = order.DeliveryFee,
            totalAmount = order.TotalAmount
        });
    }
}

public sealed record SetClientCoordinatesRequest(double Latitude, double Longitude, string? Address);
public sealed record SubmitClientRatingRequest(int Score, string? Comment);
public sealed record ValidateDeliveryRequest(string Code);
