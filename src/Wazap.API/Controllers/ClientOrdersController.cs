using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
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
    private readonly PaymentSplitService? _paymentSplit;
    private readonly RiderService _riderService;
    private readonly WhatsAppOrchestrationService? _whatsApp;
    private readonly ClientOptions _clientOptions;

    public ClientOrdersController(
        ApplicationDbContext context,
        DeliveryOfferService deliveryOfferService,
        ClientPaymentService clientPayments,
        RiderService riderService,
        WhatsAppOrchestrationService? whatsApp = null,
        ClientOptions? clientOptions = null,
        PaymentSplitService? paymentSplit = null)
    {
        _context = context;
        _deliveryOfferService = deliveryOfferService;
        _clientPayments = clientPayments;
        _riderService = riderService;
        _whatsApp = whatsApp;
        _clientOptions = clientOptions ?? new ClientOptions();
        _paymentSplit = paymentSplit;
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

        var riderCertified = order.RiderUserId is { } assignedRider
            && await _context.RiderIdentities.AsNoTracking().AnyAsync(i => i.UserId == assignedRider
                && i.Status == RiderIdentityStatus.Verified);

        var payment = await _context.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Status, p.Amount, p.PaymentLink, p.QuotedVendorAmount, p.QuotedRiderAmount, p.QuotedGatewayFee, p.FeePayer })
            .FirstOrDefaultAsync();

        SplitPaymentCalculation? quote = null;
        if (payment?.QuotedVendorAmount is { } merchant)
        {
            quote = new SplitPaymentCalculation(payment.Amount, merchant, payment.QuotedRiderAmount ?? 0,
                order.VendorWhatsAppNumber, order.RiderWhatsAppNumber, "", "", payment.QuotedGatewayFee ?? 0,
                Enum.Parse<SplitFeePayer>(payment.FeePayer!));
        }
        else if (payment is null)
        {
            try { quote = PaymentSplitService.CalculateSplit(order, SplitFeePayer.Shared); }
            catch (InvalidOperationException) { /* Panier trop petit : aucun devis de paiement proposé. */ }
        }

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
            deliveryCode = (string?)null,
            qrUrl = (string?)null,
            trackingUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}",
            hasProofPhoto = order.DeliveryProofPhotoFileName != null,
            riderPhone = order.RiderWhatsAppNumber,
            riderCertified,
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
                },
            splitPayment = quote is null ? null : new
            {
                merchantAmount = quote.MerchantAmount,
                riderDeliveryFee = quote.RiderDeliveryFee,
                gatewayFee = quote.GatewayFee,
                feePayer = quote.FeePayer.ToString(),
                totalAmount = quote.TotalAmount,
                qrPaymentUrl = $"/api/client/orders/{order.Id}/qr-payment",
                supportedOperators = new[] { "Wave", "OrangeMoney", "MTN", "Moov", "Visa" }
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
    public async Task<IActionResult> GetDeliveryQr(Guid id, [FromQuery] string? receipt = null)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (!order.VerifyReceiptToken(receipt)) return Unauthorized();
        Response.Headers.CacheControl = "no-store";

        var targetUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}#receipt={receipt}";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qrData);
        var bytes = png.GetGraphic(10, drawQuietZones: true);

        return File(bytes, "image/png", $"wazap-delivery-qr-{order.Id.ToString("N")[..8]}.png");
    }

    // POST: api/client/orders/{id}/start-delivery — le livreur déclenche la course ("Livraison déclenchée")
    [HttpPost("{id:guid}/start-delivery")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> StartDelivery(Guid id)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (!IsAssignedRider(order)) return Forbid();

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
        await using var transaction = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;
        if (_context.Database.IsRelational())
            await _context.Orders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, o => o.Status));
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (!order.VerifyReceiptToken(request.ReceiptToken)) return Unauthorized();

        if (order.Status == OrderStatus.Delivered)
            return Ok(new { status = order.Status.ToString(), message = "Commande déjà validée et livrée." });

        if (order.Status is not (OrderStatus.RiderAssigned or OrderStatus.ReadyForPickup or OrderStatus.PickedUp or OrderStatus.InTransit))
            return BadRequest(new { message = $"Impossible de valider la livraison en statut {order.Status}." });

        order.RecordRecipientReceipt(request.ReceiptToken);
        if (order.Status == OrderStatus.RiderAssigned)
            order.MarkReadyForPickup();
        if (order.Status == OrderStatus.ReadyForPickup)
            order.MarkPickedUp();
        if (order.Status == OrderStatus.PickedUp)
            order.MarkInTransit();
        if (order.Status == OrderStatus.InTransit)
            order.MarkDelivered();

        await _context.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();

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

    // GET: api/client/orders/{id}/qr-handover — QR Code de ramassage au magasin (affiché par le vendeur, scanné par le livreur)
    [HttpGet("{id:guid}/qr-handover")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Vendor")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> GetHandoverQr(Guid id)
    {
        var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (!User.IsInRole("Admin") && (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var vendorId)
            || order.VendorUserId != vendorId)) return Forbid();

        var targetUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}?ramasser=1";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qrData);
        var bytes = png.GetGraphic(10, drawQuietZones: true);

        return File(bytes, "image/png", $"wazap-handover-qr-{order.Id.ToString("N")[..8]}.png");
    }

    // POST: api/client/orders/{id}/scan-handover — Le livreur scanne le QR code du colis/comptoir au magasin
    [HttpPost("{id:guid}/scan-handover")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> ScanHandover(Guid id)
    {
        var assigned = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (assigned is null) return NotFound();
        if (!IsAssignedRider(assigned)) return Forbid();
        if (_paymentSplit is not null)
        {
            var result = await _paymentSplit.ProcessHandoverScanAsync(id);
            if (!result.Success && result.Status != "InTransit")
                return BadRequest(new { message = result.Message, status = result.Status });

            return Ok(new { success = true, status = result.Status, message = result.Message });
        }

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (order.Status == OrderStatus.InTransit)
            return Ok(new { status = order.Status.ToString(), message = "Colis déjà en cours d'acheminement." });

        if (order.Status is not (OrderStatus.RiderAssigned or OrderStatus.ReadyForPickup or OrderStatus.PickedUp))
            return BadRequest(new { message = $"Impossible de ramasser en statut {order.Status}." });

        if (order.Status == OrderStatus.RiderAssigned)
            order.MarkReadyForPickup();
        if (order.Status == OrderStatus.ReadyForPickup)
            order.MarkPickedUp();
        if (order.Status == OrderStatus.PickedUp)
            order.MarkInTransit();

        await _context.SaveChangesAsync();
        return Ok(new { success = true, status = order.Status.ToString(), message = "Colis récupéré avec succès par scan QR !" });
    }

    // GET: api/client/orders/{id}/qr-payment — QR Code de paiement direct Mobile Money (Wave / OM) pour le client
    [HttpGet("{id:guid}/qr-payment")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> GetPaymentQr(Guid id)
    {
        var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        var targetUrl = $"{_clientOptions.TrackingBaseUrl.TrimEnd('/')}/{order.Id}?payer=1&mode=split";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(targetUrl, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qrData);
        var bytes = png.GetGraphic(10, drawQuietZones: true);

        return File(bytes, "image/png", $"wazap-pay-qr-{order.Id.ToString("N")[..8]}.png");
    }

    // POST: api/client/orders/{id}/pay-split — Initialise le paiement fractionné direct (Digital COD Anti-Fuite)
    [HttpPost("{id:guid}/pay-split")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> PaySplit(Guid id, [FromBody] PaySplitRequest? request = null)
    {
        var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        if (_paymentSplit is null)
            return StatusCode(503, new { message = "Le paiement sécurisé n'est pas disponible." });
        var result = await _paymentSplit.InitiateSplitPaymentAsync(id, _clientOptions.TrackingBaseUrl, request?.FeePayer);
        if (!result.Success)
            return BadRequest(new { message = result.ErrorMessage });
        return Ok(new
        {
            success = true, totalAmount = result.TotalAmount, merchantAmount = result.MerchantAmount,
            riderDeliveryFee = result.RiderDeliveryFee, gatewayFee = result.GatewayFee,
            feePayer = result.FeePayer.ToString(), paymentLink = result.PaymentLink,
            qrPayload = result.QrPayload, status = result.Status
        });
    }

    // Anciennes applications : aucune déclaration de paiement par le client n'est acceptée.
    [HttpPost("{id:guid}/complete-split")]
    [EnableRateLimiting("client")]
    public IActionResult CompleteSplit(Guid id, [FromBody] CompleteSplitRequest request)
        => StatusCode(StatusCodes.Status410Gone, new { message = "Seul le prestataire peut confirmer le paiement." });

    private bool IsAssignedRider(Order order) => User?.Identity?.IsAuthenticated == true
        && User.IsInRole("Rider")
        && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var riderId)
        && order.RiderUserId == riderId;
}

public sealed record SetClientCoordinatesRequest(double Latitude, double Longitude, string? Address);
public sealed record SubmitClientRatingRequest(int Score, string? Comment);
public sealed record ValidateDeliveryRequest(string? Code = null, string? ReceiptToken = null);
public sealed record CompleteSplitRequest(string? PaymentReference);
public sealed record PaySplitRequest(SplitFeePayer? FeePayer = null);

