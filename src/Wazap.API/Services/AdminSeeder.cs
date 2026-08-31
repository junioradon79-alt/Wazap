using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class AdminSeeder : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminSeeder> _logger;
    private readonly IConfiguration _config;

    public AdminSeeder(IServiceScopeFactory scopeFactory, ILogger<AdminSeeder> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var username = _config["SeedAdmin:Username"];
        var password = _config["SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogInformation("Admin de seed non configuré (SeedAdmin:Username / SeedAdmin:Password). Création ignorée.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var exists = await context.Users.AnyAsync(u => u.Username == username, stoppingToken);
            if (exists)
            {
                _logger.LogInformation("L'admin de seed '{Username}' existe déjà.", username);
                return;
            }

            context.Users.Add(new User(username, hasher.Hash(password), UserRole.Admin));
            await context.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Admin de seed '{Username}' créé avec succès.", username);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de créer l'admin de seed. Vérifiez que la base est migrée (dotnet ef database update).");
        }
    }
}
