using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class OrderService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public OrderService(ApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var order = new Order(
            request.ClientName,
            request.ClientWhatsAppNumber,
            request.VendorWhatsAppNumber,
            request.Description,
            request.Amount);

        // Un vendeur qui crée la commande en devient le propriétaire
        if (_currentUser.Role == UserRole.Vendor && _currentUser.Id is not null)
            order.LinkVendor(_currentUser.Id.Value);

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

        return order;
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
