using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Gestion des vendeurs : liste, adresse géocodée, rechargement de crédits (packs prépayés).
    /// </summary>
    public sealed class VendorService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGeocodingService _geocoding;
        private readonly ILogger<VendorService> _logger;

        public VendorService(
            ApplicationDbContext context,
            IGeocodingService geocoding,
            ILogger<VendorService> logger)
        {
            _context = context;
            _geocoding = geocoding;
            _logger = logger;
        }

        public async Task<List<UserSummaryDto>> GetVendorsAsync()
        {
            return await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Vendor)
                .OrderBy(u => u.Username)
                .Select(u => new UserSummaryDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    PhoneNumber = u.PhoneNumber,
                    Role = u.Role,
                    IsAvailable = u.IsAvailable,
                    LocationSharingEnabled = u.LocationSharingEnabled,
                    Zone = u.Zone,
                    Credits = u.Credits,
                    ReferralCode = u.ReferralCode,
                    Latitude = u.Latitude,
                    Longitude = u.Longitude,
                    LocationUpdatedAt = u.LocationUpdatedAt
                })
                .ToListAsync();
        }

        public async Task SetZoneAsync(Guid vendorId, string zone)
        {
            var vendor = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == vendorId && u.Role == UserRole.Vendor)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            vendor.SetZone(zone);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAddressAsync(Guid vendorId, string address)
        {
            var vendor = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == vendorId && u.Role == UserRole.Vendor)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            var result = await _geocoding.GeocodeAsync(address);
            if (result is null)
                throw new InvalidOperationException("Adresse introuvable.");

            vendor.UpdateLocation(result.Value.Latitude, result.Value.Longitude);
            await _context.SaveChangesAsync();
        }

        public async Task TopUpCreditsAsync(Guid vendorId, int credits)
        {
            if (credits <= 0)
                throw new ArgumentOutOfRangeException(nameof(credits), "Le nombre de crédits doit être positif.");

            var vendor = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == vendorId && u.Role == UserRole.Vendor)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            vendor.AddCredits(credits);
            await _context.SaveChangesAsync();
        }
    }
}
