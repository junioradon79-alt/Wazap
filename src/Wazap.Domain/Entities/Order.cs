using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

public class Order
{
    private OrderStatus _status;

    public Guid Id { get; private set; }
    public string ClientName { get; private set; } = default!;
    public string ClientWhatsAppNumber { get; private set; } = default!;
    public string VendorWhatsAppNumber { get; private set; } = default!;
    public string? RiderWhatsAppNumber { get; private set; }
    public Guid? VendorUserId { get; private set; }
    public Guid? RiderUserId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? VendorConfirmedAt { get; private set; }
    public DateTime? RiderAssignedAt { get; private set; }
    public DateTime? PickedUpAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public OrderStatus Status
    {
        get => _status;
        private set => _status = value;
    }

    // Constructeur privé pour EF Core
    private Order() { }

    // Constructeur public pour la création
    public Order(string clientName, string clientWhatsAppNumber, string vendorWhatsAppNumber, string description, decimal amount)
    {
        Id = Guid.NewGuid();
        ClientName = clientName;
        ClientWhatsAppNumber = clientWhatsAppNumber;
        VendorWhatsAppNumber = vendorWhatsAppNumber;
        Description = description;
        Amount = amount;
        CreatedAt = DateTime.UtcNow;
        _status = OrderStatus.PendingVendorConfirmation;
    }

    public void ConfirmByVendor()
    {
        if (_status != OrderStatus.PendingVendorConfirmation)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de confirmer.");
        _status = OrderStatus.VendorConfirmed;
        VendorConfirmedAt = DateTime.UtcNow;
    }

    public void AwaitRiderAcceptance()
    {
        if (_status != OrderStatus.VendorConfirmed)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de passer en attente livreur.");
        _status = OrderStatus.AwaitingRiderAcceptance;
    }

    public void AssignRider(string riderWhatsAppNumber)
    {
        if (_status != OrderStatus.AwaitingRiderAcceptance)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible d'assigner un livreur.");
        RiderWhatsAppNumber = riderWhatsAppNumber;
        _status = OrderStatus.RiderAssigned;
        RiderAssignedAt = DateTime.UtcNow;
    }

    public void MarkReadyForPickup()
    {
        if (_status != OrderStatus.RiderAssigned)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de passer en prêt.");
        _status = OrderStatus.ReadyForPickup;
    }

    public void MarkPickedUp()
    {
        if (_status != OrderStatus.ReadyForPickup)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de marquer récupérée.");
        _status = OrderStatus.PickedUp;
        PickedUpAt = DateTime.UtcNow;
    }

    public void MarkInTransit()
    {
        if (_status != OrderStatus.PickedUp)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de passer en transit.");
        _status = OrderStatus.InTransit;
    }

    public void MarkDelivered()
    {
        if (_status != OrderStatus.InTransit)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible de marquer livrée.");
        _status = OrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (_status == OrderStatus.Delivered || _status == OrderStatus.InTransit)
            throw new InvalidOperationException($"Impossible d'annuler une commande en statut {_status}.");
        _status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
    }
    public void LinkVendor(Guid vendorUserId) => VendorUserId = vendorUserId;

    public void LinkRider(Guid riderUserId) => RiderUserId = riderUserId;
}
