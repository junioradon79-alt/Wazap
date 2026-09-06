using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Gestion des livreurs : liste, position live, disponibilité, partage RGPD et
    /// certification « Garantie Colis Sûr » (dossier d'identité + scan de la pièce).
    /// </summary>
    public sealed class RiderService
    {
        private const string ScanFolder = "App_Data/rider-scans";

        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IWhatsAppSender _whatsApp;
        private readonly ILogger<RiderService> _logger;

        public RiderService(ApplicationDbContext context, IWebHostEnvironment env,
            IWhatsAppSender whatsApp, ILogger<RiderService> logger)
        {
            _context = context;
            _env = env;
            _whatsApp = whatsApp;
            _logger = logger;
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
                        i?.IdNumber,
                        i?.Motorcycle,
                        i?.IdScanUrl,
                        i?.ScanFileName,
                        i?.ScanReceivedAt,
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
        public async Task VerifyRiderAsync(Guid riderUserId, string? fullName, string? idNumber,
            string? motorcycle, Guid? reviewerId)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == riderUserId);
            if (identity is null)
            {
                identity = new RiderIdentity(riderUserId, fullName, idNumber, motorcycle);
                _context.RiderIdentities.Add(identity);
            }

            identity.Verify(fullName, idNumber, motorcycle, reviewerId);
            await _context.SaveChangesAsync();

            await NotifyRiderAsync(rider,
                "✅ Félicitations ! Votre dossier est vérifié : vous êtes désormais Livreur certifié WAZAP. 🛵\n" +
                "Envoyez DISPO pour recevoir les courses près de chez vous, puis ZONE <quartier> pour définir votre zone.");
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

            await NotifyRiderAsync(rider,
                "ℹ️ Votre dossier de certification WAZAP n'a pas encore été validé.\n" +
                "Répondez à notre équipe ou renvoyez une photo claire de votre pièce d'identité pour réessayer.");
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

            await NotifyRiderAsync(rider,
                "Votre compte livreur WAZAP a été suspendu.\n" +
                "Contactez notre équipe pour toute question.");
        }


        /// <summary>
        /// Téléverse le scan de la pièce d'identité fourni par l'équipe (photo reçue
        /// sur WhatsApp). Un dossier refusé est rouvert « à vérifier ».
        /// </summary>
        public async Task StoreScanAsync(Guid riderUserId, Stream file, string fileName)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf"))
                throw new InvalidOperationException("Format non pris en charge (JPG, PNG, WEBP ou PDF attendu).");

            var dir = Path.Combine(_env.ContentRootPath, ScanFolder);
            Directory.CreateDirectory(dir);

            var storedName = $"{riderUserId:N}{extension}";
            var fullPath = Path.Combine(dir, storedName);
            await using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(output);
            }

            var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == riderUserId);
            if (identity is null)
            {
                identity = new RiderIdentity(riderUserId);
                _context.RiderIdentities.Add(identity);
            }

            identity.SubmitScanFile(storedName);
            await _context.SaveChangesAsync();
        }

        /// <summary>Chemin du scan local (null si aucun fichier téléversé).</summary>
        public async Task<string?> GetStoredScanPathAsync(Guid riderUserId)
        {
            var identity = await _context.RiderIdentities.AsNoTracking()
                .FirstOrDefaultAsync(i => i.UserId == riderUserId);

            if (identity?.ScanFileName is null)
                return null;

            var fullPath = Path.Combine(_env.ContentRootPath, ScanFolder, identity.ScanFileName);
            return File.Exists(fullPath) ? fullPath : null;
        }

        private async Task NotifyRiderAsync(User rider, string message)
        {
            if (string.IsNullOrWhiteSpace(rider.PhoneNumber))
                return;

            try
            {
                await _whatsApp.SendTextMessageAsync(rider.PhoneNumber, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification WhatsApp impossible pour le livreur {Rider} (certification).",
                    rider.Username);
            }
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
        string? IdNumber,
        string? Motorcycle,
        string? IdScanUrl,
        string? ScanFileName,
        DateTime? ScanReceivedAt,
        string? BlacklistReason,
        DateTime? CreatedAt,
        DateTime? ReviewedAt);
}

