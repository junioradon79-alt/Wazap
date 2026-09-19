using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos;

public class OrderDto
{
    public Guid Id { get; init; }
    public string ClientName { get; init; } = default!;
    public string Description { get; init; } = default!;
    /// <summary>Prix de la marchandise (FCFA).</summary>
    public decimal Amount { get; init; }

    /// <summary>Frais de livraison dus au livreur (FCFA, typiquement 1 000 à 2 000 FCFA).</summary>
    public decimal DeliveryFee { get; init; } = 1000m;

    /// <summary>Montant total (marchandise + livraison) en FCFA.</summary>
    public decimal TotalAmount => Amount + DeliveryFee;

    public OrderStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>Une photo de preuve de livraison accompagne la commande (litiges).</summary>
    public bool HasProofPhoto { get; init; }

    /// <summary>Motif d'annulation de la commande si annulée (T4).</summary>
    public OrderCancellationReason CancellationReason { get; init; }
    public string? CancellationComment { get; init; }
}
