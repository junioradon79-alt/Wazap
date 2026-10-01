namespace Wazap.Application.Dtos;

/// <summary>
/// Espace livreur connecté : disponibilité, zone active, statistiques de courses, gains nets, progression ambassadeur.
/// </summary>
public sealed record RiderDashboardDto(
    Guid Id,
    string Username,
    string? FullName,
    string? PhoneNumber,
    string? Zone,
    bool IsAvailable,
    bool IsVerified,
    string IdentityStatus,
    string? IdNumber,
    string? DocumentType,
    int DeliveriesToday,
    int DeliveriesThisMonth,
    int TotalDeliveries,
    decimal TotalEarningsEstimated,
    double? RatingAverage,
    int RatingCount,
    string ReferralCode,
    int ValidatedReferrals,
    RiderProgramProgressDto? ProgramProgress,
    RiderActiveOrderDto? ActiveOrder,
    List<RiderRecentOrderItem> RecentOrders
);

public sealed record RiderProgramProgressDto(
    int Deliveries,
    int DeliveriesTarget,
    int ValidatedReferrals,
    int ReferralsTarget,
    double? AverageRating,
    bool Certified,
    bool RatingMet,
    string RewardLabel,
    int ConditionsMet,
    bool RewardUnlocked
);

public sealed record RiderActiveOrderDto(
    Guid OrderId,
    string Code,
    string? ClientName,
    string? ClientPhone,
    string? VendorName,
    string? VendorPhone,
    string? PickupAddress,
    string? DeliveryAddress,
    decimal Amount,
    decimal DeliveryFee,
    decimal TotalAmount,
    string Status,
    DateTime AssignedAt
);

public sealed record RiderRecentOrderItem(
    Guid Id,
    string Code,
    string? ClientName,
    string? DeliveryAddress,
    decimal DeliveryFee,
    string Status,
    DateTime? DeliveredAt
);
