using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos
{
    public sealed class DashboardSummaryDto
    {
        public int InProgressOrdersCount { get; init; }
        public int ActiveRiders { get; init; }
        public decimal MonthlyRevenue { get; init; }
        public IReadOnlyList<OrderInProgressDto> RecentOrders { get; init; } = [];

        public int TotalVendors { get; init; }
        public int NewVendors30d { get; init; }
        public int ActiveVendors30d { get; init; }
        public int TotalRiders { get; init; }
        public int OrdersThisWeek { get; init; }
        public int OrdersLast30d { get; init; }
        public IReadOnlyList<ZoneMetricDto> OrdersByZone30d { get; init; } = [];

        public decimal AverageBasket30d { get; init; }
        public double DeliveryRate30d { get; init; }
        public decimal Revenue30d { get; init; }
        public double RevenueChangePercent { get; init; }
        public IReadOnlyList<TopVendorDto> TopVendors30d { get; init; } = [];
        public double LeadConversionRate30d { get; init; }
        public IReadOnlyList<ZoneRevenueDto> RevenueByZone30d { get; init; } = [];
    }

    public sealed class ZoneMetricDto
    {
        public string Zone { get; init; } = "";
        public int Orders { get; init; }
    }

    public sealed class ZoneRevenueDto
    {
        public string Zone { get; init; } = "";
        public decimal Revenue { get; init; }
    }

    public sealed class TopVendorDto
    {
        public string Username { get; init; } = "";
        public int DeliveredOrders { get; init; }
        public decimal Revenue { get; init; }
    }

    public sealed class OrderInProgressDto
    {
        public Guid Id { get; init; }
        public string VendorName { get; init; } = default!;
        public string VendorWhatsApp { get; init; } = default!;
        public string MaskedClientPhone { get; init; } = default!;
        public DashboardStatusCategory StatusCategory { get; init; }

        public string StatusLabel => StatusCategory switch
        {
            DashboardStatusCategory.RechercheLivreur => "Recherche Livreur",
            DashboardStatusCategory.EnLivraison => "En livraison",
            DashboardStatusCategory.Livre => "Livré",
            _ => StatusCategory.ToString()
        };
    }
}
