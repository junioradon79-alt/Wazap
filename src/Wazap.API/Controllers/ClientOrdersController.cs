using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Services;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

/// <summary>
/// Parcours acheteur (page de suivi PWA) : le client consulte sa commande et valide
/// ses coordonnées — ce qui déclenche AUTOMATIQUEMENT la recherche des livreurs.
/// </summary>
[ApiController]
[Route("api/client/orders")]
public class ClientOrdersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly DeliveryOfferService _deliveryOfferService;

    public ClientOrdersController(ApplicationDbContext context, DeliveryOfferService deliveryOfferService)
    {
        _context = context;
        _deliveryOfferService = deliveryOfferService;
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
            delivered = order.Status == OrderStatus.Delivered
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
}

public sealed record SetClientCoordinatesRequest(double Latitude, double Longitude, string? Address);
