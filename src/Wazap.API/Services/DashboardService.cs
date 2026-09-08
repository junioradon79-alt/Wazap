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
            var now30d = now.AddDays(-30);
            var now60d = now.AddDays(-60);
            var now7d = now.AddDays(-7);

            var inProgressOrdersCount = await _context.Orders
                .CountAsync(o => o.Status != OrderStatus.Delivered
                              && o.Status != OrderStatus.Cancelled);

            var activeRiders = await _context.Orders
                .Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled && o.RiderWhatsAppNumber != null)
                .Select(o => o.RiderWhatsAppNumber)
                .Distinct()
                .CountAsync();

            var monthlyRevenue = await _context.Orders
                .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= startOfMonth)
                .SumAsync(o => o.Amount);
            var revenue30d = await _context.Orders
                .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= now30d)
                .SumAsync(o => o.Amount);
            var revenuePrev30d = await _context.Orders
                .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= now60d && o.DeliveredAt < now30d)
                .SumAsync(o => o.Amount);
            var revenueChangePercent = revenuePrev30d > 0 ? (double)((revenue30d - revenuePrev30d) / revenuePrev30d * 100) : 0;

            var totalVendors = await _context.Users.CountAsync(u => u.Role == UserRole.Vendor);
            var totalRiders = await _context.Users.CountAsync(u => u.Role == UserRole.Rider);
            var newVendors30d = await _context.Users.CountAsync(u => u.Role == UserRole.Vendor && u.CreatedAt >= now30d);

            var activeVendorIds30d = await _context.Orders
                .Where(o => o.CreatedAt >= now30d && o.VendorUserId != null)
                .Select(o => o.VendorUserId!.Value)
                .Distinct()
                .ToListAsync();
            var activeVendors30d = activeVendorIds30d.Count;

            var ordersThisWeek = await _context.Orders.CountAsync(o => o.CreatedAt >= now7d);
            var ordersLast30d = await _context.Orders.CountAsync(o => o.CreatedAt >= now30d);

            var delivered30d = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= now30d);
            var confirmed30d = await _context.Orders.CountAsync(o => o.CreatedAt >= now30d
                && o.Status != OrderStatus.PendingVendorConfirmation);
            var deliveryRate30d = confirmed30d > 0 ? (double)delivered30d / confirmed30d : 0;
            var avgBasket30d = delivered30d > 0 ? revenue30d / delivered30d : 0;

            var topVendors = await _context.Orders
                .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= now30d && o.VendorUserId != null)
                .GroupBy(o => o.VendorUserId!.Value)
                .Select(g => new { VendorId = g.Key, Delivered = g.Count(), Revenue = g.Sum(o => o.Amount) })
                .OrderByDescending(x => x.Delivered)
                .Take(5)
                .ToListAsync();
            var topVendorIds = topVendors.Select(x => x.VendorId).ToList();
            var vendorNameLookup = new Dictionary<Guid, string>();
            if (topVendorIds.Count > 0)
            {
                vendorNameLookup = await _context.Users
                    .Where(u => topVendorIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.Username);
            }

            var leadsConverted30d = await _context.Leads
                .CountAsync(l => l.Status == LeadStatus.Converted && l.CreatedAt >= now30d);
            var leadsTotal30d = await _context.Leads
                .CountAsync(l => l.CreatedAt >= now30d);
            var leadConversionRate30d = leadsTotal30d > 0 ? (double)leadsConverted30d / leadsTotal30d : 0;

            var recentVendorZones = await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Vendor)
                .Select(u => new { u.Id, u.Zone })
                .ToListAsync();
            var zoneMap = recentVendorZones
                .GroupBy(v => v.Zone ?? "Inconnue")
                .ToDictionary(g => g.Key, g => g.Select(v => v.Id).ToHashSet());

            var orders30d = await _context.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= now30d && o.VendorUserId != null)
                .Select(o => o.VendorUserId!.Value)
                .ToListAsync();
            var ordersByZone = zoneMap
                .Select(kv => new ZoneMetricDto
                {
                    Zone = string.IsNullOrWhiteSpace(kv.Key) ? "Inconnue" : kv.Key,
                    Orders = orders30d.Count(id => kv.Value.Contains(id))
                })
                .Where(z => z.Orders > 0)
                .OrderByDescending(z => z.Orders)
                .ToList();

            var ordersWithZone = await _context.Orders.AsNoTracking()
                .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= now30d && o.VendorUserId != null)
                .ToListAsync();
            var zoneRevenue = zoneMap
                .Select(kv => new ZoneRevenueDto
                {
                    Zone = string.IsNullOrWhiteSpace(kv.Key) ? "Inconnue" : kv.Key,
                    Revenue = ordersWithZone
                        .Where(o => o.VendorUserId != null && kv.Value.Contains(o.VendorUserId.Value))
                        .Sum(o => o.Amount)
                })
                .Where(z => z.Revenue > 0)
                .OrderByDescending(z => z.Revenue)
                .ToList();

            var recentOrders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Status != OrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .ToListAsync();
            var recentVendorNames = await LoadVendorNamesAsync(recentOrders);

            return new DashboardSummaryDto
            {
                InProgressOrdersCount = inProgressOrdersCount,
                ActiveRiders = activeRiders,
                MonthlyRevenue = monthlyRevenue,
                RecentOrders = recentOrders.Select(o => Map(o, recentVendorNames)).ToList(),
                TotalVendors = totalVendors,
                NewVendors30d = newVendors30d,
                ActiveVendors30d = activeVendors30d,
                TotalRiders = totalRiders,
                OrdersThisWeek = ordersThisWeek,
                OrdersLast30d = ordersLast30d,
                OrdersByZone30d = ordersByZone,
                AverageBasket30d = avgBasket30d,
                DeliveryRate30d = deliveryRate30d,
                Revenue30d = revenue30d,
                RevenueChangePercent = Math.Round(revenueChangePercent, 1),
                TopVendors30d = topVendors.Select(x => new TopVendorDto
                {
                    Username = vendorNameLookup.TryGetValue(x.VendorId, out var n) ? n : "—",
                    DeliveredOrders = x.Delivered,
                    Revenue = x.Revenue
                }).ToList(),
                LeadConversionRate30d = Math.Round(leadConversionRate30d, 3),
                RevenueByZone30d = zoneRevenue
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
