using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wazap.API.Services;
using Wazap.Domain.Enums;

namespace Wazap.API.Controllers;

/// <summary>
/// API publique v1 (lecture seule, clé API via en-tête X-Api-Key, middleware dédié).
/// Aucune donnée personnelle (clients/téléphones/adresses) n'est exposée.
/// </summary>
[ApiController]
[Route("api/v1")]
[EnableRateLimiting("publicapi")]
public class PublicApiV1Controller : ControllerBase
{
    private readonly PublicApiService _service;

    public PublicApiV1Controller(PublicApiService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/overview — compteurs globaux (vendeurs, livreurs, commandes).</summary>
    [HttpGet("overview")]
    public Task<PublicOverviewDto> Overview(CancellationToken ct)
        => _service.GetOverviewAsync(ct);

    /// <summary>GET /api/v1/zones — zones avec nombre de vendeurs et commandes 30 j.</summary>
    [HttpGet("zones")]
    public Task<IReadOnlyList<PublicZoneDto>> Zones(CancellationToken ct)
        => _service.GetZonesAsync(ct);

    /// <summary>GET /api/v1/vendors?zone=&amp;active30d= — vendeurs (sans données personnelles).</summary>
    [HttpGet("vendors")]
    public Task<IReadOnlyList<PublicVendorDto>> Vendors([FromQuery] string? zone, [FromQuery] bool? active30d, CancellationToken ct)
        => _service.GetVendorsAsync(zone, active30d, ct);

    /// <summary>
    /// GET /api/v1/orders?zone=&amp;from=&amp;to=&amp;status=&amp;limit=
    /// Commandes sans données personnelles (zone, montant, statut, dates).
    /// </summary>
    [HttpGet("orders")]
    public async Task<IActionResult> Orders(
        [FromQuery] string? zone,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? status,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        OrderStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var s))
                return BadRequest(new { error = $"Statut invalide : {status}. Valeurs : {string.Join(", ", Enum.GetNames<OrderStatus>())}." });
            parsedStatus = s;
        }

        return Ok(await _service.GetOrdersAsync(zone, from, to, parsedStatus, limit, ct));
    }

    /// <summary>GET /api/v1/packs — catalogue des packs de crédits.</summary>
    [HttpGet("packs")]
    public IReadOnlyList<PublicPackDto> Packs()
        => _service.GetPacks();
}
