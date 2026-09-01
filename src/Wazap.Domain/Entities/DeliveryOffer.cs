using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Offre de livraison envoyée à un livreur (suivi du matching géographique).
/// </summary>
public class DeliveryOffer
{
    private DeliveryOfferStatus _status;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid RiderUserId { get; private set; }
    public int BatchNumber { get; private set; }
    public DateTime SentAt { get; private set; }
    public DateTime? RespondedAt { get; private set; }

    public DeliveryOfferStatus Status
    {
        get => _status;
        private set => _status = value;
    }

    private DeliveryOffer() { }

    public DeliveryOffer(Guid orderId, Guid riderUserId, int batchNumber)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        RiderUserId = riderUserId;
        BatchNumber = batchNumber;
        SentAt = DateTime.UtcNow;
        _status = DeliveryOfferStatus.Pending;
    }

    public void Accept()
    {
        if (_status != DeliveryOfferStatus.Pending)
            throw new InvalidOperationException($"Offre {_status} : impossible d'accepter.");

        _status = DeliveryOfferStatus.Accepted;
        RespondedAt = DateTime.UtcNow;
    }

    public void Decline()
    {
        if (_status != DeliveryOfferStatus.Pending)
            throw new InvalidOperationException($"Offre {_status} : impossible de décliner.");

        _status = DeliveryOfferStatus.Declined;
        RespondedAt = DateTime.UtcNow;
    }

    public void Expire()
    {
        if (_status != DeliveryOfferStatus.Pending)
            throw new InvalidOperationException($"Offre {_status} : impossible d'expirer.");

        _status = DeliveryOfferStatus.Expired;
        RespondedAt = DateTime.UtcNow;
    }
}
