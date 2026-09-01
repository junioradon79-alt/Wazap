using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Exceptions;
using Wazap.Domain.Enums;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Vendor")]
public class VendorsController : ControllerBase
{
    private readonly VendorService _vendorService;
    private readonly PackService _packService;
    private readonly ICurrentUser _currentUser;

    public VendorsController(VendorService vendorService, PackService packService, ICurrentUser currentUser)
    {
        _vendorService = vendorService;
        _packService = packService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var vendors = await _vendorService.GetVendorsAsync();

        // Un vendeur ne voit que sa propre fiche (l'admin voit tout).
        if (_currentUser.Role == UserRole.Vendor && _currentUser.Id is not null)
            vendors = vendors.Where(v => v.Id == _currentUser.Id.Value).ToList();

        return Ok(vendors);
    }

    [HttpPut("{id:guid}/address")]
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] UpdateVendorAddressRequest request)
    {
        EnsureCanManage(id);
        await _vendorService.UpdateAddressAsync(id, request.Address);
        return NoContent();
    }

    // Top-up réservé aux administrateurs : octroyer des crédits sans paiement.
    [HttpPost("{id:guid}/credits/topup")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TopUpCredits(Guid id, [FromBody] TopUpCreditsRequest request)
    {
        await _vendorService.TopUpCreditsAsync(id, request.Credits);
        return NoContent();
    }

    // Historique des achats de crédits du vendeur.
    [HttpGet("{id:guid}/transactions")]
    public async Task<IActionResult> GetTransactions(Guid id)
    {
        EnsureCanManage(id);
        return Ok(await _packService.GetVendorTransactionsAsync(id));
    }

    private void EnsureCanManage(Guid vendorId)
    {
        if (_currentUser.Role == UserRole.Admin)
            return;

        if (_currentUser.Role == UserRole.Vendor && _currentUser.Id == vendorId)
            return;

        throw new ForbiddenException("Vous ne pouvez gérer que votre propre compte vendeur.");
    }
}

public sealed record UpdateVendorAddressRequest(string Address);

public sealed record TopUpCreditsRequest(int Credits);
