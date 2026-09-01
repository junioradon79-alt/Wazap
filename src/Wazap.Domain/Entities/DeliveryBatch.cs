using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Lot de livraison groupée : plusieurs commandes confirmées d'un même vendeur,
/// proposées ensemble aux livreurs (pratique courante en Côte d'Ivoire : le même
/// livreur prend plusieurs colis pour une même tournée).
/// </summary>
public class DeliveryBatch
{
    private DeliveryBatchStatus _status;

    public Guid Id { get; private set; }

    /// <summary>Vendeur à l'origine du lot (les commandes groupées viennent de lui).</summary>
    public Guid VendorUserId { get; private set; }

    public DeliveryBatchStatus Status
    {
        get => _status;
        private set => _status = value;
    }

    public DateTime CreatedAt { get; private set; }
    public DateTime? AssignedAt { get; private set; }

    /// <summary>Livreur retenu pour la tournée (après acceptation du lot).</summary>
    public Guid? RiderUserId { get; private set; }

    public string? RiderWhatsAppNumber { get; private set; }

    // Navigation : commandes du lot
    public ICollection<Order> Orders { get; } = new List<Order>();

    // Constructeur privé pour EF Core
    private DeliveryBatch() { }

    public DeliveryBatch(Guid vendorUserId)
    {
        Id = Guid.NewGuid();
        VendorUserId = vendorUserId;
        CreatedAt = DateTime.UtcNow;
        _status = DeliveryBatchStatus.Open;
    }

    /// <summary>
    /// Assigne le livreur au lot (acceptation) : toutes les commandes du lot
    /// lui sont alors attribuées.
    /// </summary>
    public void AssignRider(Guid riderUserId, string riderWhatsAppNumber)
    {
        if (_status != DeliveryBatchStatus.Open)
            throw new InvalidOperationException($"État actuel : {_status}. Impossible d'assigner un livreur au lot.");

        RiderUserId = riderUserId;
        RiderWhatsAppNumber = riderWhatsAppNumber;
        AssignedAt = DateTime.UtcNow;
        _status = DeliveryBatchStatus.Assigned;
    }
}
