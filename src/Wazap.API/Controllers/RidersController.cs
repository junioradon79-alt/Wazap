using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RidersController : ControllerBase
{
    private readonly RiderService _riderService;

    public RidersController(RiderService riderService) => _riderService = riderService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _riderService.GetRidersAsync());

    [HttpPost("location")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateRiderLocationRequest request)
    {
        await _riderService.UpdateLocationAsync(request.RiderUserId, request.Latitude, request.Longitude);
        return NoContent();
    }

    [HttpPut("{id:guid}/availability")]
    public async Task<IActionResult> SetAvailability(Guid id, [FromBody] SetAvailabilityRequest request)
    {
        await _riderService.SetAvailabilityAsync(id, request.IsAvailable);
        return NoContent();
    }

    [HttpPut("{id:guid}/location-sharing")]
    public async Task<IActionResult> SetLocationSharing(Guid id, [FromBody] SetLocationSharingRequest request)
    {
        await _riderService.SetLocationSharingAsync(id, request.IsEnabled);
        return NoContent();
    }
}

public sealed record UpdateRiderLocationRequest(Guid RiderUserId, double Latitude, double Longitude);

public sealed record SetAvailabilityRequest(bool IsAvailable);

public sealed record SetLocationSharingRequest(bool IsEnabled);
