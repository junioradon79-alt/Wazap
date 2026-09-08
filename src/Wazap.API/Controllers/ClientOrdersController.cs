using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Services;
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

    public ClientOrdersController(
        ApplicationDbContext context,
        DeliveryOfferService deliveryOfferService,
        ClientPaymentService clientPayments)
    {
        _context = context;
        _deliveryOfferService = deliveryOfferService;
        _clientPayments = clientPayments;
    }

    // GET: api/client/orders/{id} — état visible par le client (public, id non devinable)
    [HttpGet("{id:guid}")]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> Get(Guid id)
    {
        var order = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound();

        var vendorName = order.VendorUserId is { } vendorId
            ? await _context.Users.AsNoTracking()
                .Where(u => u.Id == vendorId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync()
            : null;

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
            status = order.Status.ToString(),
            description = order.Description,
            needsCoordinates = order.RequiresClientCoordinates,
            hasCoordinates = order.ClientLatitude is not null && order.ClientLongitude is not null,
            address = order.ClientAddress,
            riderAssigned = order.Status == OrderStatus.RiderAssigned,
            delivered = order.Status == OrderStatus.Delivered,
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
}

public sealed record SetClientCoordinatesRequest(double Latitude, double Longitude, string? Address);
