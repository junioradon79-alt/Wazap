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
    DbSet<VendorProduct> VendorProducts { get; }
    DbSet<OrderLine> OrderLines { get; }
    DbSet<DeliveryOffer> DeliveryOffers { get; }
    DbSet<DeliveryBatch> DeliveryBatches { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }
    DbSet<RiderIdentity> RiderIdentities { get; }
    DbSet<DeliveryClaim> DeliveryClaims { get; }
    DbSet<RiderRating> RiderRatings { get; }
    DbSet<OrderPayment> OrderPayments { get; }

    // Tables ajoutées depuis : sans elles dans le port, les services devaient dépendre du type
    // CONCRET de l'infrastructure pour les atteindre — ce qui annulait l'intérêt du port.
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<WebhookSubscriber> WebhookSubscribers { get; }
    DbSet<Lead> Leads { get; }
    DbSet<ClientOrderDraft> ClientOrderDrafts { get; }
    DbSet<RiderPriorityPurchase> RiderPriorityPurchases { get; }

    /// <summary>Messages webhook entrants déjà traités (déduplication des reprises).</summary>
    DbSet<ProcessedWebhookMessage> ProcessedWebhookMessages { get; }

    DatabaseFacade Database { get; }

    /// <summary>
    /// Indique si le fournisseur sait exécuter un UPDATE ensembliste conditionnel
    /// (<c>ExecuteUpdateAsync</c>) : indispensable aux opérations qui doivent rester
    /// atomiques (réclamation d'une offre, débit de crédits) face à des requêtes
    /// concurrentes. Faux pour le fournisseur InMemory utilisé par les tests.
    /// </summary>
    bool SupportsConditionalUpdates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
