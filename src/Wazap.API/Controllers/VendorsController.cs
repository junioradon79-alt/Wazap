using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
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
    private readonly ClientPaymentService _clientPayments;
    private readonly VendorProductService _products;
    private readonly ICurrentUser _currentUser;
    private readonly ApplicationDbContext _context;

    public VendorsController(VendorService vendorService, PackService packService,
        ClientPaymentService clientPayments, VendorProductService products,
        ICurrentUser currentUser, ApplicationDbContext context)
    {
        _vendorService = vendorService;
        _packService = packService;
        _clientPayments = clientPayments;
        _products = products;
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

    // GET: api/vendors/dashboard — espace vendeur connecté (crédits, analytics, parrainage, courses).
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetMyDashboard()
    {
        if (_currentUser.Role == UserRole.Vendor && _currentUser.Id is null)
            return Forbid();

        var vendor = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == _currentUser.Id && u.Role == UserRole.Vendor);
        if (vendor is null)
            return NotFound();

        var orders = await _context.Orders.AsNoTracking()
            .Where(o => o.VendorUserId == vendor.Id)
            .ToListAsync();

        var phoneOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.VendorUserId == null && o.VendorWhatsAppNumber != null)
            .ToListAsync();

        orders.AddRange(phoneOrders.Where(o =>
            !string.IsNullOrWhiteSpace(vendor.PhoneNumber)
            && PhoneNumberNormalizer.SameSubscriber(o.VendorWhatsAppNumber, vendor.PhoneNumber)));

        // Analytics mensuels
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var now30d = now.AddDays(-30);

        var monthlyRevenue = orders
            .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= monthStart)
            .Sum(o => o.Amount);
        var ordersThisWeek = orders.Count(o => o.CreatedAt >= now.AddDays(-7));
        var ordersLastMonth = orders.Count(o => o.CreatedAt >= now30d);
        var deliveredLastMonth = orders.Count(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= now30d);
        var avgBasket = deliveredLastMonth > 0 ? monthlyRevenue / deliveredLastMonth : 0;
        var deliveryRate = ordersLastMonth > 0 ? (double)deliveredLastMonth / ordersLastMonth : 0;

        // Top clients (max 5)
        var topClients = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.ClientName))
            .GroupBy(o => o.ClientName)
            .Select(g => new VendorClientItem(
                g.Key,
                g.Count(),
                g.Sum(o => o.Amount)))
            .OrderByDescending(c => c.OrderCount)
            .Take(5)
            .ToList();

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

        // Parrainage
        var referralTotal = await _context.Users.AsNoTracking()
            .CountAsync(u => u.ReferredByUserId == vendor.Id && u.Role == UserRole.Vendor);

        var referrals = await _context.Users.AsNoTracking()
            .Where(u => u.ReferredByUserId == vendor.Id && u.Role == UserRole.Vendor)
            .OrderByDescending(u => u.CreatedAt)
            .Take(50)
            .Select(u => new ReferredVendorItem(
                u.Id, u.Username, u.PhoneNumber, u.Zone, u.CreatedAt))
            .ToListAsync();

        var referralTransactions = await _context.CreditTransactions.AsNoTracking()
            .Where(t => t.VendorId == vendor.Id && t.TransactionReference.StartsWith("REF-"))
            .ToListAsync();

        return Ok(new VendorDashboardDto(
            vendor.Id,
            vendor.Username,
            vendor.PhoneNumber,
            vendor.Zone,
            vendor.Credits,
            vendor.ReferralCode ?? string.Empty,
            orders.Count(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled),
            orders.Count(o => o.DeliveredAt.HasValue && o.DeliveredAt.Value >= monthStart),
            recent,
            referralTotal,
            referralTransactions.Sum(t => t.CreditsPurchased),
            referrals,
            monthlyRevenue,
            Math.Round(avgBasket, 0),
            Math.Round(deliveryRate, 2),
            ordersThisWeek,
            ordersLastMonth,
            deliveredLastMonth,
            topClients));
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
    // POST: api/vendors/orders/{id}/pay — le vendeur demande le lien de paiement Mobile Money
    // de son client (commandes créées par téléphone, sans page de suivi). Le lien est envoyé
    // au client sur WhatsApp ; l'initiation est idempotente (même lien tant que Pending).
    [HttpPost("orders/{id:guid}/pay")]
    public async Task<IActionResult> RequestClientPayment(Guid id)
    {
        var result = await _clientPayments.RequestPaymentFromVendorAsync(id, _currentUser);
        return result.Status switch
        {
            "NotFound" => NotFound(),
            "Forbidden" => StatusCode(403, result.ErrorMessage),
            _ => Ok(result)
        };
    }

    [HttpGet("{id:guid}/transactions")]
    public async Task<IActionResult> GetTransactions(Guid id)
    {
        EnsureCanManage(id);
        return Ok(await _packService.GetVendorTransactionsAsync(id));
    }

    // --- Catalogue produits du vendeur (menu du bot de commande client) -------------------

    // GET: api/vendors/{id}/products — catalogue produits (admin : tout vendeur, vendeur : le sien).
    [HttpGet("{id:guid}/products")]
    public async Task<IActionResult> GetProducts(Guid id)
    {
        EnsureCanManage(id);
        return Ok(await _products.GetProductsAsync(id));
    }

    [HttpPost("{id:guid}/products")]
    public async Task<IActionResult> CreateProduct(Guid id, [FromBody] VendorProductRequest request)
    {
        EnsureCanManage(id);
        var product = await _products.CreateAsync(id, request);
        return CreatedAtAction(nameof(GetProducts), new { id }, product);
    }

    [HttpPut("{id:guid}/products/{productId:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, Guid productId, [FromBody] VendorProductRequest request)
    {
        EnsureCanManage(id);
        return await _products.UpdateAsync(id, productId, request) ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}/products/{productId:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, Guid productId)
    {
        EnsureCanManage(id);

        return await _products.DeleteAsync(id, productId) switch
        {
            VendorProductDeleteResult.Deleted => NoContent(),
            VendorProductDeleteResult.InUse => Conflict(
                "Ce produit figure dans des commandes passées : il ne peut pas être supprimé (modifiez-le plutôt)."),
            _ => NotFound()
        };
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
