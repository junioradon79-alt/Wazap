using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wazap.Domain.Entities;

namespace Wazap.Application.Abstractions;

/// <summary>
/// Port d'accès aux données pour les services d'application (implémenté par
/// <c>ApplicationDbContext</c> dans Wazap.Infrastructure). Permet de conserver
/// la logique métier hors de la couche API et de la tester sans la base réelle.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Order> Orders { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<User> Users { get; }
    DbSet<DeliveryOffer> DeliveryOffers { get; }
    DbSet<DeliveryBatch> DeliveryBatches { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
