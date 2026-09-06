using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Domain.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Lecture seule pour les partenaires (API publique v1, protégée par clé).
/// Aucune donnée personnelle n'est exposée (ni nom client, ni téléphones, ni adresses).
/// </summary>
public sealed class PublicApiService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<PackConfiguration> _packs;

    public PublicApiService(IServiceScopeFactory scopeFactory, IReadOnlyList<PackConfiguration> packs)
    {
        _scopeFactory = scopeFactory;
        _packs = packs;
    }

    public async Task<PublicOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var week = now.AddDays(-7);
        var month = now.AddDays(-30);

        var vendorsTotal = await db.Users.CountAsync(u => u.Role == UserRole.Vendor, ct);
        var ridersTotal = await db.Users.CountAsync(u => u.Role == UserRole.Rider, ct);
        var ordersWeek = await db.Orders.CountAsync(o => o.CreatedAt >= week, ct);
        var orders30d = await db.Orders.CountAsync(o => o.CreatedAt >= month, ct);
        var vendorsActive30d = await db.Orders
            .Where(o => o.VendorUserId != null && o.CreatedAt >= month)
            .Select(o => o.VendorUserId!.Value)
            .Distinct()
            .CountAsync(ct);
        var delivered30d = await db.Orders
            .CountAsync(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= month, ct);

        return new PublicOverviewDto(
            vendorsTotal,
            ridersTotal,
            vendorsActive30d,
            ordersWeek,
            orders30d,
            delivered30d);
    }

    public async Task<IReadOnlyList<PublicZoneDto>> GetZonesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var month = DateTime.UtcNow.AddDays(-30);

        var vendorsByZone = await db.Users
            .Where(u => u.Role == UserRole.Vendor && u.Zone != null)
            .GroupBy(u => u.Zone!)
            .Select(g => new { Zone = g.Key, Vendors = g.Count() })
            .ToListAsync(ct);

        var ordersByZone = await (
            from o in db.Orders
            join v in db.Users.Where(u => u.Role == UserRole.Vendor && u.Zone != null)
                on o.VendorUserId equals v.Id
            where o.CreatedAt >= month
            group o by v.Zone! into g
            select new { Zone = g.Key, Orders30d = g.Count() })
            .ToListAsync(ct);

        var orders = ordersByZone.ToDictionary(x => x.Zone, x => x.Orders30d);
        return vendorsByZone
            .Select(v => new PublicZoneDto(v.Zone, v.Vendors, orders.TryGetValue(v.Zone, out var n) ? n : 0))
            .OrderByDescending(z => z.Vendors)
            .ThenByDescending(z => z.Orders30d)
            .ToList();
    }

    public async Task<IReadOnlyList<PublicVendorDto>> GetVendorsAsync(string? zone, bool? active30d, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var month = DateTime.UtcNow.AddDays(-30);

        var query = db.Users.Where(u => u.Role == UserRole.Vendor);
        if (!string.IsNullOrWhiteSpace(zone))
            query = query.Where(u => u.Zone == zone);

        var vendors = await query
            .OrderByDescending(u => u.CreatedAt)
            .Take(500)
            .Select(u => new { u.Id, u.Username, u.Zone, u.Credits, u.CreatedAt })
            .ToListAsync(ct);

        var ids = vendors.Select(v => v.Id).ToList();
        var orderCounts = await db.Orders
            .Where(o => o.VendorUserId != null && ids.Contains(o.VendorUserId.Value) && o.CreatedAt >= month)
            .GroupBy(o => o.VendorUserId!.Value)
            .Select(g => new { VendorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VendorId, x => x.Count, ct);

        var result = vendors.Select(v =>
        {
            var orders30 = orderCounts.TryGetValue(v.Id, out var n) ? n : 0;
            return new PublicVendorDto(v.Id, v.Username, v.Zone, v.Credits, v.CreatedAt, orders30, orders30 > 0);
        }).ToList();

        return active30d.HasValue ? result.Where(v => v.Active30d == active30d.Value).ToList() : result;
    }

    public async Task<IReadOnlyList<PublicOrderDto>> GetOrdersAsync(
        string? zone, DateTime? from, DateTime? to, OrderStatus? status, int limit, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var fromUtc = from ?? now.AddDays(-30);
        var toUtc = to ?? now;
        limit = Math.Clamp(limit, 1, 500);

        var orders = await db.Orders
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc
                && (status == null || o.Status == status))
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new
            {
                o.Id,
                o.CreatedAt,
                o.Amount,
                o.Status,
                o.VendorUserId,
                o.RiderUserId,
                o.BatchId
            })
            .ToListAsync(ct);

        // Zones des vendeurs (seconde requête bornée aux IDs retournés).
        var vendorIds = orders.Where(o => o.VendorUserId != null).Select(o => o.VendorUserId!.Value).Distinct().ToList();
        var zoneByVendor = new Dictionary<Guid, string?>();
        if (vendorIds.Count > 0)
        {
            zoneByVendor = await db.Users
                .Where(u => vendorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Zone })
                .ToDictionaryAsync(u => u.Id, u => u.Zone, ct);
        }

        return orders
            .Where(o => string.IsNullOrWhiteSpace(zone)
                || (o.VendorUserId != null && zoneByVendor.TryGetValue(o.VendorUserId.Value, out var z) && z == zone))
            .Select(o => new PublicOrderDto(
                o.Id,
                o.CreatedAt,
                o.Amount,
                o.Status,
                o.VendorUserId != null && zoneByVendor.TryGetValue(o.VendorUserId.Value, out var z) ? z : null,
                o.RiderUserId != null,
                o.BatchId != null))
            .ToList();
    }

    public IReadOnlyList<PublicPackDto> GetPacks()
        => _packs.Select(p => new PublicPackDto(p.Name, p.Price, p.Credits)).ToList();
}

// --- DTOs publics (aucune donnée personnelle) --------------------------------

public sealed record PublicOverviewDto(
    int VendorsTotal,
    int RidersTotal,
    int VendorsActive30d,
    int OrdersWeek,
    int Orders30d,
    int Delivered30d);

public sealed record PublicZoneDto(string Zone, int Vendors, int Orders30d);

public sealed record PublicVendorDto(
    Guid Id,
    string Username,
    string? Zone,
    int Credits,
    DateTime CreatedAt,
    int Orders30d,
    bool Active30d);

public sealed record PublicOrderDto(
    Guid Id,
    DateTime CreatedAt,
    decimal Amount,
    OrderStatus Status,
    string? Zone,
    bool HasRider,
    bool IsBatched);

public sealed record PublicPackDto(string Name, decimal Price, int Credits);
