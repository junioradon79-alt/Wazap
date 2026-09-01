using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Purge RGPD : efface les positions des livreurs plus anciennes que la durée de
    /// rétention configurée (Geo:LocationRetentionHours, défaut 24 h). Les positions
    /// statiques des vendeurs (adresses géocodées) ne sont pas purgées ici.
    /// </summary>
    public sealed class LocationPurgeWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LocationPurgeWorker> _logger;
        private readonly GeoOptions _geo;

        public LocationPurgeWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<LocationPurgeWorker> logger,
            GeoOptions geo)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _geo = geo;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PurgeAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la purge RGPD des positions.");
                }
            }
        }

        private async Task PurgeAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cutoff = DateTime.UtcNow.AddHours(-_geo.LocationRetentionHours);

            var purged = await db.Users
                .Where(u => u.Role == UserRole.Rider
                         && u.Latitude != null
                         && u.LocationUpdatedAt < cutoff)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.Latitude, (double?)null)
                    .SetProperty(u => u.Longitude, (double?)null)
                    .SetProperty(u => u.LocationUpdatedAt, (DateTime?)null), ct);

            if (purged > 0)
                _logger.LogInformation("RGPD : {Count} position(s) de livreurs purgées (antérieures à {Cutoff:u}).", purged, cutoff);
        }
    }
}
