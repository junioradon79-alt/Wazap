using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Gestion des livreurs : liste, position live, disponibilité, partage RGPD et
    /// certification « Garantie Colis Sûr » (dossier d'identité + scan de la pièce).
    /// Les scans sont CHIFFRÉS AU REPOS (AES-GCM, clé « RiderScans:EncryptionKey »).
    /// Sans clé exploitable, le téléversement est REFUSÉ : il n'existe pas de repli
    /// silencieux vers l'écriture en clair (voir <see cref="RiderScansOptions"/>).
    /// </summary>
    public sealed class RiderService
    {
        private const string ScanFolder = "App_Data/rider-scans";
        private const string ScanEncryptionHeader = "WZSCN1";
        private static readonly byte[] ScanEncryptionMagic = Encoding.ASCII.GetBytes(ScanEncryptionHeader);

        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IWhatsAppSender _whatsApp;
        private readonly ILogger<RiderService> _logger;
        private readonly RiderScansOptions _scans;

        public RiderService(ApplicationDbContext context, IWebHostEnvironment env,
            IWhatsAppSender whatsApp, RiderScansOptions scans, ILogger<RiderService> logger)
        {
            _context = context;
            _env = env;
            _whatsApp = whatsApp;
            _logger = logger;
            _scans = scans;
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
        /// Téléverse le scan de la pièce d'identité (photo de l'équipe ou reçue du livreur
        /// sur WhatsApp). Un dossier refusé est rouvert « à vérifier ». <paramref name="sourceUrl"/>,
        /// s'il est fourni (média messagerie), est conservé comme provenance du document.
        /// </summary>
        public async Task StoreScanAsync(Guid riderUserId, Stream file, string fileName, string? sourceUrl = null)
        {
            var rider = await _context.Users.FirstOrDefaultAsync(
                    u => u.Id == riderUserId && u.Role == UserRole.Rider)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf"))
                throw new InvalidOperationException("Format non pris en charge (JPG, PNG, WEBP ou PDF attendu).");

            // Garde-fou RGPD : pas de clé exploitable = pas d'écriture. Le contrôle a lieu AVANT
            // toute création de dossier ou de fichier, pour ne laisser aucune trace sur le disque.
            var hasKey = _scans.TryResolveKey(out var key, out var keyProblem);
            if (!hasKey && !_scans.AllowUnencryptedStorage)
            {
                _logger.LogError(
                    "ALERTE [config] Téléversement du scan d'identité refusé pour le livreur {Rider} : {Problem}. "
                    + "Renseignez RiderScans:EncryptionKey ; hors production uniquement, "
                    + "RiderScans:AllowUnencryptedStorage=true autorise le stockage en clair.",
                    riderUserId, keyProblem);

                throw new InvalidOperationException(
                    "Le stockage sécurisé des scans d'identité n'est pas configuré "
                    + "(clé de chiffrement absente ou invalide). Téléversement refusé : "
                    + "contactez l'administrateur système.");
            }

            if (!hasKey)
                _logger.LogWarning(
                    "Scan d'identité du livreur {Rider} stocké EN CLAIR : {Problem}, "
                    + "et RiderScans:AllowUnencryptedStorage=true l'autorise explicitement.",
                    riderUserId, keyProblem);

            var dir = Path.Combine(_env.ContentRootPath, ScanFolder);
            Directory.CreateDirectory(dir);

            var storedName = $"{riderUserId:N}{extension}";
            var fullPath = Path.Combine(dir, storedName);
            await using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                if (!hasKey)
                {
                    // Stockage en clair, autorisé explicitement par la configuration.
                    await file.CopyToAsync(output);
                }
                else
                {
                    using var memory = new MemoryStream();
                    await file.CopyToAsync(memory);
                    var encrypted = EncryptBytes(memory.ToArray(), key);
                    await output.WriteAsync(encrypted);
                }
            }

            var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == riderUserId);
            if (identity is null)
            {
                identity = new RiderIdentity(riderUserId);
                _context.RiderIdentities.Add(identity);
            }

            identity.SubmitScanFile(storedName);
            if (!string.IsNullOrWhiteSpace(sourceUrl))
                identity.SubmitScanUrl(sourceUrl);
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

        /// <summary>
        /// Contenu déchiffré du scan (null si absent). Les fichiers chiffrés (en-tête
        /// « WZSCN1 ») sont déchiffrés avec la clé configurée ; les fichiers historiques
        /// en clair sont servis tels quels (compatibilité ascendante).
        /// </summary>
        public async Task<(byte[]? Content, string? FileName)?> GetScanContentAsync(Guid riderUserId)
        {
            var identity = await _context.RiderIdentities.AsNoTracking()
                .FirstOrDefaultAsync(i => i.UserId == riderUserId);

            if (identity?.ScanFileName is null)
                return null;

            var fullPath = Path.Combine(_env.ContentRootPath, ScanFolder, identity.ScanFileName);
            if (!File.Exists(fullPath))
                return null;

            var bytes = await File.ReadAllBytesAsync(fullPath);
            var content = bytes.AsSpan().StartsWith(ScanEncryptionMagic)
                ? DecryptBytes(bytes)
                : bytes;

            // Contenu null = scan présent mais illisible (mauvaise clé / fichier corrompu) :
            // distinct de « null » qui signifie scan absent. Le contrôleur répond 404 dans les deux cas.
            return (content, identity.ScanFileName);
        }

        /// <summary>
        /// Purge RGPD : supprime du disque les scans de pièce d'identité dont la décision de
        /// certification remonte à plus de <paramref name="retentionDays"/> jours, et efface
        /// leur référence en base. Les dossiers encore <c>Pending</c> sont épargnés — leur
        /// scan sert toujours à l'examen. Retourne le nombre de dossiers réellement purgés.
        /// </summary>
        public async Task<int> PurgeExpiredScansAsync(int retentionDays, CancellationToken ct = default)
        {
            if (retentionDays <= 0)
                return 0;

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            var expired = await _context.RiderIdentities
                .Where(i => i.Status != RiderIdentityStatus.Pending
                         && i.ReviewedAt != null && i.ReviewedAt < cutoff
                         && i.ScanPurgedAt == null
                         && (i.ScanFileName != null || i.IdScanUrl != null))
                .ToListAsync(ct);

            var purged = 0;

            foreach (var identity in expired)
            {
                if (identity.ScanFileName is { } fileName)
                {
                    var fullPath = Path.Combine(_env.ContentRootPath, ScanFolder, fileName);
                    try
                    {
                        if (File.Exists(fullPath))
                            File.Delete(fullPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Le fichier résiste : on n'efface PAS la référence, sinon il
                        // resterait sur le disque sans que rien ne le désigne plus.
                        _logger.LogWarning(ex, "Scan {File} non supprimé — purge reportée.", fileName);
                        continue;
                    }
                }

                identity.PurgeScan();
                purged++;
            }

            if (purged > 0)
                await _context.SaveChangesAsync(ct);

            return purged;
        }

        /// <summary>Chiffre AES-GCM : [en-tête WZSCN1][nonce 12][ciphertext + tag 16].</summary>
        private static byte[] EncryptBytes(byte[] plaintext, byte[] key)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];

            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            using var output = new MemoryStream();
            output.Write(ScanEncryptionMagic);
            output.Write(nonce);
            output.Write(tag);
            output.Write(ciphertext);
            return output.ToArray();
        }

        /// <summary>Déchiffre un scan chiffré (null si clé absente ou authentification refusée).</summary>
        private byte[]? DecryptBytes(byte[] encrypted)
        {
            if (!_scans.TryResolveKey(out var key, out var keyProblem))
            {
                _logger.LogError(
                    "Scan d'identité chiffré illisible : {Problem} (RiderScans:EncryptionKey). "
                    + "La clé d'origine est indispensable — ne la remplacez jamais sans "
                    + "rechiffrer les scans déjà stockés.",
                    keyProblem);
                return null;
            }

            try
            {
                var offset = ScanEncryptionHeader.Length;
                var nonce = encrypted.AsSpan(offset, 12);
                var tag = encrypted.AsSpan(offset + 12, 16);
                var ciphertext = encrypted.AsSpan(offset + 28);

                var plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return plaintext;
            }
            catch (CryptographicException ex)
            {
                _logger.LogError(ex, "Échec de déchiffrement d'un scan (mauvaise clé ou fichier corrompu).");
                return null;
            }
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

