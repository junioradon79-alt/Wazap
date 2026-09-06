using Microsoft.EntityFrameworkCore;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
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
                    ReferralCode = u.ReferralCode,
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

        /// <summary>Dossiers de certification de tous les livreurs (ordre : à vérifier d'abord).</summary>
        public async Task<List<RiderCertificationDto>> GetCertificationsAsync()
        {
            var riders = await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Rider)
                .Select(u => new { u.Id, u.Username, u.PhoneNumber, u.Zone, u.IsAvailable, u.CreatedAt })
                .ToListAsync();

            var identities = await _context.RiderIdentities.AsNoTracking().ToListAsync();
            var byUser = identities.ToDictionary(i => i.UserId);

            return riders
                .Select(u =>
                {
                    byUser.TryGetValue(u.Id, out var i);
                    return new RiderCertificationDto(
                        u.Id,
                        u.Username,
                        u.PhoneNumber,
                        u.Zone,
                        u.IsAvailable,
                        i?.Status.ToString() ?? RiderIdentityStatus.Pending.ToString(),
                        i?.FullName,
                        i?.CniNumber,
                        i?.MotorcyclePlate,
                        i?.BlacklistReason,
                        i?.CreatedAt,
                        i?.ReviewedAt);
                })
                .OrderBy(x => x.Status == RiderIdentityStatus.Pending.ToString() ? 0
                    : x.Status == RiderIdentityStatus.Rejected.ToString() ? 1
                    : x.Status == RiderIdentityStatus.Verified.ToString() ? 2 : 3)
                .ThenBy(x => x.Username)
                .ToList();
        }

        /// <summary>Certifie un livreur après contrôle d'identité par l'équipe (badge « certifié »).</summary>
        public async Task VerifyRiderAsync(Guid riderUserId, string? fullName, string? cniNumber,
            string? motorcyclePlate, Guid? reviewerId)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == riderUserId);
            if (identity is null)
            {
                identity = new RiderIdentity(riderUserId, fullName, cniNumber, motorcyclePlate);
                _context.RiderIdentities.Add(identity);
            }

            identity.Verify(fullName, cniNumber, motorcyclePlate, reviewerId);
            await _context.SaveChangesAsync();
        }

        /// <summary>Refuse la certification (dossier incomplet, incohérences…).</summary>
        public async Task RejectRiderAsync(Guid riderUserId, string? reason, Guid? reviewerId)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == riderUserId);
            if (identity is null)
            {
                identity = new RiderIdentity(riderUserId);
                _context.RiderIdentities.Add(identity);
            }

            identity.Reject(reason, reviewerId);
            await _context.SaveChangesAsync();
        }

        /// <summary>Exclut définitivement un livreur (vol/fraude) : hors-ligne + plus aucune offre.</summary>
        public async Task BlacklistRiderAsync(Guid riderUserId, string reason, Guid? reviewerId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Un motif est requis pour exclure un livreur.", nameof(reason));

            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == riderUserId);
            if (identity is null)
            {
                identity = new RiderIdentity(riderUserId);
                _context.RiderIdentities.Add(identity);
            }

            identity.Blacklist(reason, reviewerId);
            rider.SetAvailability(false);
            await _context.SaveChangesAsync();
        }
    }

    public sealed record RiderCertificationDto(
        Guid RiderId,
        string Username,
        string? PhoneNumber,
        string? Zone,
        bool IsAvailable,
        string Status,
        string? FullName,
        string? CniNumber,
        string? MotorcyclePlate,
        string? BlacklistReason,
        DateTime? CreatedAt,
        DateTime? ReviewedAt);
}
