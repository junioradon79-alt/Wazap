using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VendorsController : ControllerBase
{
    private readonly VendorService _vendorService;

    public VendorsController(VendorService vendorService) => _vendorService = vendorService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _vendorService.GetVendorsAsync());

    [HttpPut("{id:guid}/address")]
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] UpdateVendorAddressRequest request)
    {
        await _vendorService.UpdateAddressAsync(id, request.Address);
        return NoContent();
    }

    [HttpPost("{id:guid}/credits/topup")]
    public async Task<IActionResult> TopUpCredits(Guid id, [FromBody] TopUpCreditsRequest request)
    {
        await _vendorService.TopUpCreditsAsync(id, request.Credits);
        return NoContent();
    }
}

public sealed record UpdateVendorAddressRequest(string Address);

public sealed record TopUpCreditsRequest(int Credits);
