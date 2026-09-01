using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Insère des données de démonstration (vendeurs, livreurs) uniquement si la base
    /// ne contient encore aucun utilisateur de ces rôles. Les mots de passe sont hachés
    /// (PBKDF2) pour permettre la connexion des comptes démo.
    /// </summary>
    public sealed class DemoDataSeeder : BackgroundService
    {
        private const string DemoPassword = "demo";
        private const int InitialVendorCredits = 10;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DemoDataSeeder> _logger;

        public DemoDataSeeder(IServiceScopeFactory scopeFactory, ILogger<DemoDataSeeder> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

                if (await db.Users.AnyAsync(u => u.Role == UserRole.Vendor, stoppingToken)
                    || await db.Users.AnyAsync(u => u.Role == UserRole.Rider, stoppingToken))
                {
                    return;
                }

                var demoPasswordHash = passwordHasher.Hash(DemoPassword);

                db.Users.AddRange(CreateVendors(demoPasswordHash));
                db.Users.AddRange(CreateRiders(demoPasswordHash));

                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Données de démonstration WAZAP insérées (vendeurs + livreurs).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible d'insérer les données de démonstration.");
            }
        }

        private static IEnumerable<User> CreateVendors(string passwordHash)
        {
            yield return CreateVendor("Rôtisserie du Marché", "+33612456789", passwordHash);
            yield return CreateVendor("Traiteur Chez Momo", "+33745893210", passwordHash);
            yield return CreateVendor("Pizzeria Bella Napoli", "+33623567841", passwordHash);
        }

        private static IEnumerable<User> CreateRiders(string passwordHash)
        {
            yield return CreateRider("Karim Diallo", "+33670112233", passwordHash);
            yield return CreateRider("Sofiane Benali", "+33760334455", passwordHash);
            yield return CreateRider("Lucas Martin", "+33660445566", passwordHash);
            yield return CreateRider("Yann Le Goff", "+33770556677", passwordHash);
        }

        private static User CreateVendor(string name, string phone, string passwordHash)
        {
            var vendor = new User(name, passwordHash, UserRole.Vendor, phone);
            vendor.AddCredits(InitialVendorCredits);
            return vendor;
        }

        private static User CreateRider(string name, string phone, string passwordHash)
        {
            var rider = new User(name, passwordHash, UserRole.Rider, phone);
            rider.SetAvailability(true);
            return rider;
        }
    }
}
