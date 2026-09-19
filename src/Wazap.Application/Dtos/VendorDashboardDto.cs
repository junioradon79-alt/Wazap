namespace Wazap.Application.Dtos;

/// <summary>
/// Espace vendeur connecté : crédits, analytics, parrainage, courses récentes.
/// </summary>
public sealed record VendorDashboardDto(
    Guid Id,
    string Username,
    string? PhoneNumber,
    string? Zone,
    int Credits,
    string ReferralCode,
    int InProgressOrders,
    int DeliveredThisMonth,
    List<VendorOrderItem> RecentOrders,
    int TotalReferrals,
    int ReferralCreditsEarned,
    List<ReferredVendorItem> Referrals,

    // ---- Nouveaux KPI analytics (chantier D) ----
    decimal MonthlyRevenue,
    decimal AverageBasket,
    double DeliveryRate,
    int OrdersThisWeek,
    int OrdersLastMonth,
    int DeliveredLastMonth,
    List<VendorClientItem> TopClients);

public sealed record VendorOrderItem(
    Guid Id,
    string Code,
    string? ClientName,
    string Description,
    string Status,
    DateTime CreatedAt,
    string? RiderName = null,
    string? RiderPhone = null,
    decimal Amount = 0m,
    decimal DeliveryFee = 1000m,
    decimal TotalAmount = 0m);

public sealed record ReferredVendorItem(
    Guid Id,
    string Username,
    string? PhoneNumber,
    string? Zone,
    DateTime CreatedAt);

/// <summary>Client fidèle du vendeur (top commandes).</summary>
public sealed record VendorClientItem(
    string ClientName,
    int OrderCount,
    decimal TotalSpent);