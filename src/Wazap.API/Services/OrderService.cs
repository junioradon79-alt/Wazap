using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class OrderService
{
    private const int LowCreditThreshold = 5;

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly DeliveryOfferService _deliveryOfferService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ApplicationDbContext context,
        ICurrentUser currentUser,
        WhatsAppOrchestrationService whatsApp,
        DeliveryOfferService deliveryOfferService,
        ILogger<OrderService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _whatsApp = whatsApp;
        _deliveryOfferService = deliveryOfferService;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var vendor = await ResolveVendorAsync(request.VendorWhatsAppNumber);

        // Pay-per-use : un crédit est requis par commande.
        if (vendor is null || !vendor.TryConsumeCredit())
            throw new PaymentRequiredException("Crédits insuffisants. Achetez un pack sur /api/packs.");

        var order = new Order(
            request.ClientName,
            request.ClientWhatsAppNumber,
            request.VendorWhatsAppNumber,
            request.Description,
            request.Amount);

        // Le vendeur résolu (via son numéro WhatsApp) devient propriétaire de la commande.
        order.LinkVendor(vendor.Id);

        var notification = new OrderCreatedNotification(
            order.Id,
            order.ClientName,
            order.ClientWhatsAppNumber,
            order.VendorWhatsAppNumber,
            order.Description,
            order.Amount);

        // L'intention de notification est persistée dans le même commit que la commande (outbox).
        var outboxMessage = new OutboxMessage(
            "OrderCreated",
            JsonSerializer.Serialize(notification));

        await using var transaction = await _context.Database.BeginTransactionAsync();

        _context.Orders.Add(order);
        _context.OutboxMessages.Add(outboxMessage);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("Commande créée pour {VendorName}. Crédits restants : {Credits}.",
            vendor.Username, vendor.Credits);

        await NotifyCreditStatusAsync(vendor);

        return order;
    }

    /// <summary>
    /// Alerte WhatsApp quand les crédits restants atteignent un seuil bas (≤ 5) ou 0 (best effort).
    /// </summary>
    private async Task NotifyCreditStatusAsync(User vendor)
    {
        try
        {
            if (vendor.Credits == 0)
                await _whatsApp.SendNoCreditAlertAsync(vendor);
            else if (vendor.Credits <= LowCreditThreshold)
                await _whatsApp.SendLowCreditAlertAsync(vendor);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alerte WhatsApp impossible pour {Vendor} (crédits restants : {Credits}).",
                vendor.Username, vendor.Credits);
        }
    }

    private async Task<User?> ResolveVendorAsync(string vendorWhatsApp)
    {
        var normalized = new string((vendorWhatsApp ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length == 0) return null;

        var vendors = await _context.Users
            .Where(u => u.Role == UserRole.Vendor && u.PhoneNumber != null)
            .ToListAsync();

        return vendors.FirstOrDefault(v =>
            new string(v.PhoneNumber!.Where(char.IsDigit).ToArray()) == normalized);
    }

    public async Task<Order?> GetOrderAsync(Guid id) =>
        await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<PagedResult<OrderDto>> GetOrdersAsync(int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Orders.AsNoTracking();

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderDto
            {
                Id = o.Id,
                ClientName = o.ClientName,
                Description = o.Description,
                Amount = o.Amount,
                Status = o.Status,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<OrderDto>(items, total, page, pageSize);
    }

    public async Task<bool> UpdateStatusAsync(Guid id, UpdateStatusRequest request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return false;

        EnsureCanUpdate(order, request);

        switch (request.Status)
        {
            case OrderStatus.VendorConfirmed:
                order.ConfirmByVendor();
                if (order.VendorUserId is null && _currentUser.Id is not null)
                    order.LinkVendor(_currentUser.Id.Value);
                break;
            case OrderStatus.AwaitingRiderAcceptance:
                order.AwaitRiderAcceptance();
                break;
            case OrderStatus.RiderAssigned:
                order.AssignRider(request.RiderWhatsAppNumber ?? "");
                if (order.RiderUserId is null && _currentUser.Id is not null)
                    order.LinkRider(_currentUser.Id.Value);
                break;
            case OrderStatus.ReadyForPickup:
                order.MarkReadyForPickup();
                break;
            case OrderStatus.PickedUp:
                order.MarkPickedUp();
                break;
            case OrderStatus.InTransit:
                order.MarkInTransit();
                break;
            case OrderStatus.Delivered:
                order.MarkDelivered();
                break;
            case OrderStatus.Cancelled:
                order.Cancel();
                break;
            default:
                throw new ArgumentException("Statut non pris en charge.");
        }

        await _context.SaveChangesAsync();

        // Confirmation via l'app : la commande rejoint le lot groupé du vendeur
        // (le broadcast du lot est déclenché par le worker).
        if (request.Status == OrderStatus.VendorConfirmed)
        {
            try
            {
                await _deliveryOfferService.JoinOrCreateBatchAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groupage impossible après confirmation de la commande {OrderId}.", id);
            }
        }

        return true;
    }

    private void EnsureCanUpdate(Order order, UpdateStatusRequest request)
    {
        var userId = _currentUser.Id;
        var role = _currentUser.Role;

        if (role == UserRole.Admin)
            return;

        if (role == UserRole.Vendor)
        {
            // Première confirmation : le vendeur prend possession de la commande
            if (request.Status == OrderStatus.VendorConfirmed && order.VendorUserId is null)
                return;

            if (order.VendorUserId != userId)
                throw new ForbiddenException("Vous ne pouvez modifier que vos propres commandes.");
            return;
        }

        if (role == UserRole.Rider)
        {
            // Acceptation : le livreur prend possession de la course
            if (request.Status == OrderStatus.RiderAssigned && order.RiderUserId is null)
                return;

            if (order.RiderUserId != userId)
                throw new ForbiddenException("Vous ne pouvez modifier que vos propres courses.");
            return;
        }

        throw new ForbiddenException("Rôle non autorisé.");
    }
}
