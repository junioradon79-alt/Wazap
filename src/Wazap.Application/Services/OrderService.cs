using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;

namespace Wazap.Application.Services;

public sealed class OrderService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly DeliveryOfferService _deliveryOfferService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        DeliveryOfferService deliveryOfferService,
        ILogger<OrderService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _deliveryOfferService = deliveryOfferService;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        // Le vendeur doit être enregistré (propriétaire de la commande). Le crédit n'est
        // PAS débité ici : il ne l'est qu'à l'acceptation de la course par un livreur.
        var vendor = await ResolveVendorAsync(request.VendorWhatsAppNumber)
            ?? throw new InvalidOperationException("Vendeur non enregistré.");

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

        _logger.LogInformation("Commande créée pour {VendorName} (débit à l'acceptation d'un livreur).",
            vendor.Username);

        return order;
    }

    /// <summary>
    /// Livraison à la demande : le vendeur déclenche une course (commande WhatsApp
    /// « LIVRAISON … » ou via son espace), quel que soit le canal d'origine de la commande
    /// (téléphone, Facebook, boutique). 1 crédit consommé, commande confirmée, jointe à un
    /// lot puis diffusée immédiatement aux livreurs.
    /// </summary>
    public async Task<Order> CreateDispatchRequestAsync(Guid vendorUserId, string instruction, string? clientPhone)
    {
        var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == vendorUserId && u.Role == UserRole.Vendor)
            ?? throw new InvalidOperationException("Vendeur introuvable.");

        // Le matching exige une position GPS OU une zone déclarée.
        if ((vendor.Latitude is null || vendor.Longitude is null) && string.IsNullOrWhiteSpace(vendor.Zone))
            throw new InvalidOperationException("Définissez d'abord votre zone : envoyez ZONE <votre quartier> (ex : ZONE Cocody).");

        // NOTE : le crédit n'est PAS débité ici. Il ne l'est qu'à l'acceptation de la
        // course par un livreur (DeliveryOfferService.AcceptOfferAsync / AcceptBatchAsync).

        var order = new Order(
            "Client",
            string.IsNullOrWhiteSpace(clientPhone) ? string.Empty : clientPhone.Trim(),
            vendor.PhoneNumber ?? string.Empty,
            instruction,
            0m);

        order.LinkVendor(vendor.Id);
        order.ConfirmByVendor();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Course à la demande créée pour {Vendor} ({OrderId}). Débit à l'acceptation.",
            vendor.Username, order.Id);

        // Groupage + diffusion immédiate : un livreur proche est contacté sans intervention.
        var batchId = await _deliveryOfferService.JoinOrCreateBatchAsync(order.Id);
        await _deliveryOfferService.BroadcastBatchAsync(batchId);

        return order;
    }

    private async Task<User?> ResolveVendorAsync(string vendorWhatsApp)
    {
        if (string.IsNullOrWhiteSpace(vendorWhatsApp)) return null;

        var vendors = await _context.Users
            .Where(u => u.Role == UserRole.Vendor && u.PhoneNumber != null)
            .ToListAsync();

        return vendors.FirstOrDefault(v =>
            PhoneNumberNormalizer.SameSubscriber(v.PhoneNumber, vendorWhatsApp));
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
        // Annulation d'une commande appartenant à un lot : clôturer le lot si aucune
        // commande active n'y reste (et expirer ses offres en attente).
        else if (request.Status == OrderStatus.Cancelled && order.BatchId is { } batchId)
        {
            try
            {
                await _deliveryOfferService.HandleOrderCancelledInBatchAsync(batchId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nettoyage du lot impossible après annulation de la commande {OrderId}.", id);
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
