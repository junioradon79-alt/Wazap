using Microsoft.EntityFrameworkCore;
using Wazap.Application.Dtos;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Gestion des livreurs : liste, position live, disponibilité, partage RGPD.
    /// </summary>
    public sealed class RiderService
    {
        private readonly ApplicationDbContext _context;

        public RiderService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<UserSummaryDto>> GetRidersAsync()
        {
            return await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Rider)
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
                    Latitude = u.Latitude,
                    Longitude = u.Longitude,
                    LocationUpdatedAt = u.LocationUpdatedAt
                })
                .ToListAsync();
        }

        public async Task SetZoneAsync(Guid riderUserId, string zone)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            rider.SetZone(zone);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateLocationAsync(Guid riderUserId, double latitude, double longitude)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            if (!rider.LocationSharingEnabled)
                throw new InvalidOperationException("Partage de position désactivé par l'utilisateur.");

            rider.UpdateLocation(latitude, longitude);
            await _context.SaveChangesAsync();
        }

        public async Task SetAvailabilityAsync(Guid riderUserId, bool isAvailable)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            rider.SetAvailability(isAvailable);
            await _context.SaveChangesAsync();
        }

        public async Task SetLocationSharingAsync(Guid riderUserId, bool isEnabled)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            rider.SetLocationSharing(isEnabled);
            await _context.SaveChangesAsync();
        }
    }
}
