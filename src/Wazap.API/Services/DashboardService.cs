using Microsoft.EntityFrameworkCore;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Agrège les métriques du tableau de bord depuis la base de données (EF Core).
    /// </summary>
    public sealed class DashboardService
    {
        private readonly ApplicationDbContext _context;

        public DashboardService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DashboardSummaryDto> GetSummaryAsync()
        {
            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Commandes « en cours » : tout sauf livrées et annulées.
            var inProgressOrdersCount = await _context.Orders
                .CountAsync(o => o.Status != OrderStatus.Delivered
                              && o.Status != OrderStatus.Cancelled);

            // Livreurs actifs : livreurs distincts actuellement assignés à une commande en cours.
            var activeRiders = await _context.Orders
                .Where(o => o.Status != OrderStatus.Delivered
                         && o.Status != OrderStatus.Cancelled
                         && o.RiderWhatsAppNumber != null)
                .Select(o => o.RiderWhatsAppNumber)
                .Distinct()
                .CountAsync();

            // Chiffre d'affaires du mois : commandes livrées dans le mois courant.
            var monthlyRevenue = await _context.Orders
                .Where(o => o.Status == OrderStatus.Delivered
                         && o.DeliveredAt >= startOfMonth)
                .SumAsync(o => o.Amount);

            // Dernières commandes non annulées (les 10 plus récentes).
            var recentOrders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Status != OrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .ToListAsync();

            var vendorNames = await LoadVendorNamesAsync(recentOrders);

            return new DashboardSummaryDto
            {
                InProgressOrdersCount = inProgressOrdersCount,
                ActiveRiders = activeRiders,
                MonthlyRevenue = monthlyRevenue,
                RecentOrders = recentOrders.Select(o => Map(o, vendorNames)).ToList()
            };
        }

        private static OrderInProgressDto Map(
            Order order,
            IReadOnlyDictionary<string, string> vendorNames) => new()
        {
            Id = order.Id,
            VendorName = ResolveVendorName(order.VendorWhatsAppNumber, vendorNames),
            VendorWhatsApp = MaskPhone(order.VendorWhatsAppNumber),
            MaskedClientPhone = MaskPhone(order.ClientWhatsAppNumber),
            StatusCategory = ToCategory(order.Status)
        };

        private async Task<IReadOnlyDictionary<string, string>> LoadVendorNamesAsync(
            IReadOnlyCollection<Order> orders)
        {
            var vendorPhones = orders
                .Select(o => o.VendorWhatsAppNumber)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();

            if (vendorPhones.Count == 0)
                return new Dictionary<string, string>();

            var vendors = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.Vendor && u.PhoneNumber != null)
                .Select(u => new { u.PhoneNumber, u.Username })
                .ToListAsync();

            return vendors
                .Where(v => v.PhoneNumber != null)
                .GroupBy(v => Normalize(v.PhoneNumber))
                .ToDictionary(g => g.Key, g => g.First().Username);
        }

        private static string ResolveVendorName(
            string vendorWhatsApp,
            IReadOnlyDictionary<string, string> vendorNames)
        {
            var normalized = Normalize(vendorWhatsApp);
            return vendorNames.TryGetValue(normalized, out var name)
                ? name
                : MaskPhone(vendorWhatsApp);
        }

        private static string Normalize(string? phone)
            => new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());

        private static DashboardStatusCategory ToCategory(OrderStatus status) => status switch
        {
            OrderStatus.Delivered => DashboardStatusCategory.Livre,
            OrderStatus.PickedUp or OrderStatus.InTransit => DashboardStatusCategory.EnLivraison,
            _ => DashboardStatusCategory.RechercheLivreur
        };

        /// <summary>
        /// Masque un numéro WhatsApp pour ne jamais exposer le numéro complet côté front.
        /// </summary>
        private static string MaskPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return "—";

            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length < 8)
                return "•• •• ••";

            return $"+{digits[..2]} {digits[2..3]} {digits[3..5]} •• •• {digits[^2..]}";
        }
    }
}
