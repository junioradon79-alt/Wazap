using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos
{
    /// <summary>
    /// Vue d'ensemble du tableau de bord administrateur.
    /// </summary>
    public sealed class DashboardSummaryDto
    {
        public int InProgressOrdersCount { get; init; }
        public int ActiveRiders { get; init; }
        public decimal MonthlyRevenue { get; init; }
        public IReadOnlyList<OrderInProgressDto> RecentOrders { get; init; } = [];

        // KPI acquisition & activité (pilotage marketing)
        public int TotalVendors { get; init; }
        public int NewVendors30d { get; init; }
        public int ActiveVendors30d { get; init; }
        public int TotalRiders { get; init; }
        public int OrdersThisWeek { get; init; }
        public int OrdersLast30d { get; init; }
        public IReadOnlyList<ZoneMetricDto> OrdersByZone30d { get; init; } = [];
    }

    /// <summary>Métrique par zone (commandes des 30 derniers jours).</summary>
    public sealed class ZoneMetricDto
    {
        public string Zone { get; init; } = "Inconnue";
        public int Orders { get; init; }
    }

    /// <summary>
    /// Ligne « commande en cours » affichée dans le tableau de bord.
    /// </summary>
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
