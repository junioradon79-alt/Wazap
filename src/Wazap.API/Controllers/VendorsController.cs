using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Vendor")]
public class VendorsController : ControllerBase
{
    private readonly VendorService _vendorService;
    private readonly PackService _packService;
    private readonly ICurrentUser _currentUser;
    private readonly ApplicationDbContext _context;

    public VendorsController(VendorService vendorService, PackService packService, ICurrentUser currentUser,
        ApplicationDbContext context)
    {
        _vendorService = vendorService;
        _packService = packService;
        _currentUser = currentUser;
        _context = context;
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

    // GET: api/vendors/dashboard — espace vendeur connecté (crédits, parrainage, courses récentes).
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetMyDashboard()
    {
        if (_currentUser.Role == UserRole.Vendor && _currentUser.Id is null)
            return Forbid();

        var vendor = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == _currentUser.Id && u.Role == UserRole.Vendor);
        if (vendor is null)
            return NotFound();

        // Courses rattachées au compte vendeur + commandes clients en attente de confirmation
        // (numéro) pas encore liées à un compte.
        var orders = await _context.Orders.AsNoTracking()
            .Where(o => o.VendorUserId == vendor.Id)
            .ToListAsync();

        var phoneOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.VendorUserId == null && o.VendorWhatsAppNumber != null)
            .ToListAsync();

        orders.AddRange(phoneOrders.Where(o =>
            !string.IsNullOrWhiteSpace(vendor.PhoneNumber)
            && PhoneNumberNormalizer.SameSubscriber(o.VendorWhatsAppNumber, vendor.PhoneNumber)));

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var recent = orders
            .OrderByDescending(o => o.CreatedAt)
            .Take(15)
            .Select(o => new VendorOrderItem(
                o.Id,
                o.Id.ToString("N")[..8].ToUpperInvariant(),
                o.ClientName,
                o.Description,
                o.Status.ToString(),
                o.CreatedAt))
            .ToList();

        return Ok(new VendorDashboardDto(
            vendor.Id,
            vendor.Username,
            vendor.PhoneNumber,
            vendor.Zone,
            vendor.Credits,
            vendor.ReferralCode,
            orders.Count(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled),
            orders.Count(o => o.DeliveredAt.HasValue && o.DeliveredAt.Value >= monthStart),
            recent));
    }

    [HttpPut("{id:guid}/address")]
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] UpdateVendorAddressRequest request)
    {
        EnsureCanManage(id);
        await _vendorService.UpdateAddressAsync(id, request.Address);
        return NoContent();
    }

    // PUT: api/vendors/{id}/zone — zone/quartier (matching téléphones basiques)
    [HttpPut("{id:guid}/zone")]
    public async Task<IActionResult> SetZone(Guid id, [FromBody] SetVendorZoneRequest request)
    {
        EnsureCanManage(id);
        await _vendorService.SetZoneAsync(id, request.Zone);
        return NoContent();
    }

    // Top-up réservé aux administrateurs : octroyer des crédits sans paiement.
    [HttpPost("{id:guid}/credits/topup")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
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

public sealed record SetVendorZoneRequest(string Zone);

public sealed record VendorOrderItem(
    Guid Id,
    string Code,
    string? ClientName,
    string Description,
    string Status,
    DateTime CreatedAt);

public sealed record VendorDashboardDto(
    Guid Id,
    string Username,
    string? PhoneNumber,
    string? Zone,
    int Credits,
    string ReferralCode,
    int InProgressOrders,
    int DeliveredThisMonth,
    List<VendorOrderItem> RecentOrders);
