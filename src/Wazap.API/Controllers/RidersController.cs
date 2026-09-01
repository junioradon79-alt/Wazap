using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Exceptions;
using Wazap.Domain.Enums;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RidersController : ControllerBase
{
    private readonly RiderService _riderService;
    private readonly ICurrentUser _currentUser;

    public RidersController(RiderService riderService, ICurrentUser currentUser)
    {
        _riderService = riderService;
        _currentUser = currentUser;
    }

    // GET: api/riders — réservé à l'admin (RGPD : téléphones + positions exposés)
    [HttpGet]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> GetAll()
        => Ok(await _riderService.GetRidersAsync());

    // POST: api/riders/location — le livreur (via son token) partage sa position ;
    // l'admin peut cibler un livreur via riderUserId.
    [HttpPost("location")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateRiderLocationRequest request)
    {
        var riderId = ResolveRiderId(request.RiderUserId);
        await _riderService.UpdateLocationAsync(riderId, request.Latitude, request.Longitude);
        return NoContent();
    }

    // PUT: api/riders/{id}/availability — le livreur ne gère que son propre compte.
    [HttpPut("{id:guid}/availability")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> SetAvailability(Guid id, [FromBody] SetAvailabilityRequest request)
    {
        EnsureOwnership(id);
        await _riderService.SetAvailabilityAsync(id, request.IsAvailable);
        return NoContent();
    }

    // PUT: api/riders/{id}/location-sharing — RGPD : désactivation volontaire.
    [HttpPut("{id:guid}/location-sharing")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> SetLocationSharing(Guid id, [FromBody] SetLocationSharingRequest request)
    {
        EnsureOwnership(id);
        await _riderService.SetLocationSharingAsync(id, request.IsEnabled);
        return NoContent();
    }

    private Guid ResolveRiderId(Guid? explicitId)
    {
        if (_currentUser.Role == UserRole.Admin && explicitId.HasValue)
            return explicitId.Value;

        return _currentUser.Id ?? throw new ForbiddenException("Identité livreur requise.");
    }

    private void EnsureOwnership(Guid id)
    {
        if (_currentUser.Role == UserRole.Admin)
            return;

        if (_currentUser.Id == id)
            return;

        throw new ForbiddenException("Vous ne pouvez modifier que votre propre compte livreur.");
    }
}

public sealed record UpdateRiderLocationRequest(Guid? RiderUserId, double Latitude, double Longitude);

public sealed record SetAvailabilityRequest(bool IsAvailable);

public sealed record SetLocationSharingRequest(bool IsEnabled);
