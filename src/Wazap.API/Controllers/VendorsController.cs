using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
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
    private readonly DeliveryOfferService? _deliveryOfferService;
    private readonly WhatsAppOrchestrationService? _whatsApp;

    public VendorsController(VendorService vendorService, PackService packService,
        ClientPaymentService clientPayments, VendorProductService products,
        ICurrentUser currentUser, ApplicationDbContext context,
        DeliveryOfferService? deliveryOfferService = null,
        WhatsAppOrchestrationService? whatsApp = null)
    {
        _vendorService = vendorService;
        _packService = packService;
        _clientPayments = clientPayments;
        _products = products;
        _currentUser = currentUser;
        _context = context;
        _deliveryOfferService = deliveryOfferService;
        _whatsApp = whatsApp;
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

        var riderIds = orders.Where(o => o.RiderUserId.HasValue).Select(o => o.RiderUserId!.Value).Distinct().ToList();
        var ridersMap = new Dictionary<Guid, (string Username, string? PhoneNumber)>();
        if (riderIds.Count > 0)
        {
            var riderList = await _context.Users.AsNoTracking()
                .Where(u => riderIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.PhoneNumber })
                .ToListAsync();
            foreach (var r in riderList)
            {
                ridersMap[r.Id] = (r.Username, r.PhoneNumber);
            }
        }

        var recent = orders
            .OrderByDescending(o => o.CreatedAt)
            .Take(15)
            .Select(o =>
            {
                var riderName = o.RiderUserId.HasValue && ridersMap.TryGetValue(o.RiderUserId.Value, out var r) ? r.Username : null;
                var riderPhone = o.RiderUserId.HasValue && ridersMap.TryGetValue(o.RiderUserId.Value, out var r2) ? (r2.PhoneNumber ?? o.RiderWhatsAppNumber) : o.RiderWhatsAppNumber;
                return new VendorOrderItem(
                    o.Id,
                    o.Id.ToString("N")[..8].ToUpperInvariant(),
                    o.ClientName,
                    o.Description,
                    o.Status.ToString(),
                    o.CreatedAt,
                    riderName,
                    riderPhone,
                    o.Amount,
                    o.DeliveryFee,
                    o.TotalAmount,
                    o.ClientWhatsAppNumber,
                    o.ClientAddress);
            })
            .ToList();

        var pendingOrders = orders
            .Where(o => o.Status == OrderStatus.PendingVendorConfirmation)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new VendorOrderItem(
                o.Id,
                o.Id.ToString("N")[..8].ToUpperInvariant(),
                o.ClientName,
                o.Description,
                o.Status.ToString(),
                o.CreatedAt,
                null,
                null,
                o.Amount,
                o.DeliveryFee,
                o.TotalAmount,
                o.ClientWhatsAppNumber,
                o.ClientAddress))
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
            topClients,
            pendingOrders));
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

    // --- Vitrine Publique Marchande (Boutique Client 1-Tap) -------------------------------

    // GET: api/vendors/public/{identifier} — vitrine publique de la boutique pour le client (AllowAnonymous)
    [HttpGet("public/{identifier}")]
    [AllowAnonymous]
    [EnableRateLimiting("client")]
    public async Task<IActionResult> GetPublicShop(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return BadRequest("Identifiant de boutique requis.");

        User? vendor = null;
        if (Guid.TryParse(identifier, out var vendorGuid))
        {
            vendor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorGuid && u.Role == UserRole.Vendor);
        }

        if (vendor is null)
        {
            var lower = identifier.Trim().ToLowerInvariant();
            vendor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == lower && u.Role == UserRole.Vendor);
        }

        if (vendor is null && identifier.Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                vendorId = Guid.Empty,
                shopName = "Boutique Wax & Élégance",
                phone = "2250544051972",
                zone = "Cocody Deux-Plateaux",
                products = new[]
                {
                    new { id = Guid.NewGuid(), name = "Boubou Brodé Grand Modèle", price = 15000, emoji = "👗", description = "Tissu wax de première qualité, broderie dorée artisanale.", isAvailable = true, imageUrl = (string?)null },
                    new { id = Guid.NewGuid(), name = "Foulard en Soie Assorti", price = 5000, emoji = "🧣", description = "Foulard doux et élégant assorti à toutes tenues.", isAvailable = true, imageUrl = (string?)null },
                    new { id = Guid.NewGuid(), name = "Collier Perles Traditionnelles", price = 8000, emoji = "✨", description = "Parure artisanale confectionnée à Abidjan.", isAvailable = true, imageUrl = (string?)null },
                    new { id = Guid.NewGuid(), name = "Sac à Main Cuir & Wax", price = 12000, emoji = "👜", description = "Finition soignée, pochette intérieure zippée.", isAvailable = true, imageUrl = (string?)null }
                }
            });
        }

        if (vendor is null)
            return NotFound(new { message = "Boutique marchande introuvable." });

        var products = await _products.GetProductsAsync(vendor.Id);
        var availableProducts = products.Where(p => p.IsAvailable).ToList();

        return Ok(new
        {
            vendorId = vendor.Id,
            shopName = vendor.Username,
            phone = vendor.PhoneNumber ?? "2250544051972",
            zone = vendor.Zone ?? "Abidjan",
            products = availableProducts
        });
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

    [HttpPatch("{id:guid}/products/{productId:guid}/availability")]
    public async Task<IActionResult> UpdateProductAvailability(Guid id, Guid productId, [FromBody] UpdateProductAvailabilityRequest request)
    {
        EnsureCanManage(id);
        return await _products.SetAvailabilityAsync(id, productId, request.IsAvailable) ? NoContent() : NotFound();
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

    // POST: api/vendors/orders/parse — analyse automatique intelligente du message WhatsApp client
    [HttpPost("orders/parse")]
    public async Task<IActionResult> ParseOrderText([FromBody] ParseOrderTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.RawText))
            return BadRequest("Le texte est requis pour l'analyse.");

        var vendor = _currentUser.Id.HasValue
            ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _currentUser.Id)
            : null;

        var parsed = VendorCommandParser.ParseFreeTextOrder(request.RawText, vendor?.Zone);
        return Ok(parsed);
    }

    // POST: api/vendors/orders/{id}/confirm — confirmation directe d'une commande par le vendeur
    [HttpPost("orders/{id:guid}/confirm")]
    public async Task<IActionResult> ConfirmOrder(Guid id, [FromBody] ConfirmVendorOrderRequest? request = null)
    {
        var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.Id && u.Role == UserRole.Vendor);
        if (vendor is null && _currentUser.Role != UserRole.Admin)
            return Forbid();

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        var isOwner = (vendor is not null && order.VendorUserId == vendor.Id)
            || (vendor is not null && !string.IsNullOrWhiteSpace(vendor.PhoneNumber) && PhoneNumberNormalizer.SameSubscriber(order.VendorWhatsAppNumber, vendor.PhoneNumber));

        if (!isOwner && _currentUser.Role != UserRole.Admin)
            return Forbid();

        if (order.Status != OrderStatus.PendingVendorConfirmation)
            return BadRequest(new { message = $"La commande est en statut {order.Status} et ne peut plus être confirmée." });

        order.ConfirmByVendor();
        await _context.SaveChangesAsync();

        var calculation = PaymentSplitService.CalculateSplit(order, request?.FeePayer);
        var code = order.Id.ToString("N")[..8].ToUpperInvariant();
        var vendorName = vendor?.Username ?? "le vendeur";

        if (_whatsApp is not null && !string.IsNullOrWhiteSpace(order.ClientWhatsAppNumber))
        {
            try
            {
                await _whatsApp.SendOrderConfirmedByVendorAsync(order.ClientWhatsAppNumber, code, vendorName);
            }
            catch
            {
                // Best effort
            }
        }

        if (_deliveryOfferService is not null)
        {
            try
            {
                await _deliveryOfferService.ConfirmAndRouteAsync(order.Id);
            }
            catch
            {
                // Best effort
            }
        }

        return Ok(new
        {
            status = order.Status.ToString(),
            message = "Commande confirmée avec succès ! Recherche des livreurs déclenchée.",
            feePayer = calculation.FeePayer.ToString(),
            merchantAmount = calculation.MerchantAmount,
            riderDeliveryFee = calculation.RiderDeliveryFee,
            gatewayFee = calculation.GatewayFee,
            totalClient = calculation.TotalAmount
        });
    }

    // POST: api/vendors/orders/{id}/handover — confirmation par le vendeur de la remise du colis au livreur
    [HttpPost("orders/{id:guid}/handover")]
    public async Task<IActionResult> HandoverOrder(Guid id)
    {
        var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.Id && u.Role == UserRole.Vendor);
        if (vendor is null && _currentUser.Role != UserRole.Admin)
            return Forbid();

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return NotFound();

        var isOwner = (vendor is not null && order.VendorUserId == vendor.Id)
            || (vendor is not null && !string.IsNullOrWhiteSpace(vendor.PhoneNumber) && PhoneNumberNormalizer.SameSubscriber(order.VendorWhatsAppNumber, vendor.PhoneNumber));

        if (!isOwner && _currentUser.Role != UserRole.Admin)
            return Forbid();

        if (order.Status == OrderStatus.InTransit)
            return Ok(new { status = order.Status.ToString(), message = "Colis déjà en cours d'acheminement." });

        if (order.Status is not (OrderStatus.RiderAssigned or OrderStatus.ReadyForPickup or OrderStatus.PickedUp))
            return BadRequest(new { message = $"Impossible de marquer le colis remis en statut {order.Status}." });

        if (order.Status == OrderStatus.RiderAssigned)
            order.MarkReadyForPickup();
        if (order.Status == OrderStatus.ReadyForPickup)
            order.MarkPickedUp();
        if (order.Status == OrderStatus.PickedUp)
            order.MarkInTransit();

        await _context.SaveChangesAsync();

        var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();

        if (_whatsApp is not null)
        {
            var trackingUrl = $"https://wazap.ci/app/suivi/{order.Id}";
            var rider = order.RiderUserId.HasValue
                ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.RiderUserId.Value)
                : null;

            try
            {
                await _whatsApp.SendInTransitNotificationAsync(order, rider, trackingUrl);
            }
            catch
            {
                // Best effort
            }
        }

        return Ok(new
        {
            status = order.Status.ToString(),
            message = $"Colis #{orderCode} remis au livreur avec succès ! La livraison est en cours.",
            code = orderCode
        });
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

public sealed record ParseOrderTextRequest(string RawText);

public sealed record ConfirmVendorOrderRequest(SplitFeePayer? FeePayer = null);
