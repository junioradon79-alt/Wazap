using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests directs de <see cref="RiderDeliveryCommands"/> (commandes livreur « RECU » / « LIVRE »).
///
/// Ce bloc a été extrait du contrôleur webhook (P2 / C-13) précisément pour être testable sans
/// construire un contrôleur complet, un payload JSON ni une signature. Les cas couverts ici sont
/// ceux qu'aucun test de routage n'exerçait : le prédicat de reconnaissance, la tournée
/// multi-clients (qui ne doit JAMAIS clôturer toutes les courses d'un coup) et le verrouillage
/// anti-force brute du code client — c'est-à-dire la garantie « Colis Sûr ».
/// </summary>
public class RiderDeliveryCommandsTests
{
    private const string RiderPhone = "+2250700000011";
    private const string VendorPhone = "+2250700000012";
    private const string ClientPhone = "+2250700000013";

    private sealed class Fixture : IDisposable
    {
        public ApplicationDbContext Context { get; }
        public RecordingWhatsAppSender Sender { get; } = new();
        public RiderDeliveryCommands Commands { get; }
        public User Rider { get; }
        public List<string> Replies { get; } = new();

        public Fixture(DeliveryProofOptions? proof = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("rider-delivery-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            Context = new ApplicationDbContext(options);

            Rider = new User("livreur", "hash", UserRole.Rider, RiderPhone);
            Context.Users.Add(Rider);
            Context.SaveChanges();

            var whatsAppOptions = new WhatsAppOptions();
            var orchestrator = new WhatsAppOrchestrationService(Sender, whatsAppOptions,
                NullLogger<WhatsAppOrchestrationService>.Instance);

            Commands = new RiderDeliveryCommands(
                Context,
                proof ?? new DeliveryProofOptions(),
                orchestrator,
                new RiderProgramService(Context, new RiderProgramOptions(), Sender,
                    NullLogger<RiderProgramService>.Instance),
                NullLogger<RiderDeliveryCommands>.Instance);
        }

        /// <summary>Réponse capturée : on teste le message exact rendu au livreur.</summary>
        public Task Reply(User user, string message)
        {
            Replies.Add(message);
            return Task.CompletedTask;
        }

        public string LastReply => Replies.Count == 0 ? string.Empty : Replies[^1];

        /// <summary>Messages reçus par le client, toutes canaux confondus (texte + template).</summary>
        public int ClientNotifications
            => Sender.TextMessages.Count(m => m.Phone == ClientPhone)
               + Sender.TemplateMessages.Count(m => m.Phone == ClientPhone);

        public async Task<Order> AddOrderAsync(OrderStatus target)
        {
            var order = new Order("Awa", ClientPhone, VendorPhone, "1 pagne", 5000m);
            order.ConfirmByVendor();
            order.AwaitRiderAcceptance();
            order.AssignRider(RiderPhone);
            order.LinkRider(Rider.Id);

            if (target is OrderStatus.InTransit or OrderStatus.Delivered)
            {
                order.MarkReadyForPickup();
                order.MarkPickedUp();
                order.MarkInTransit();
            }

            if (target == OrderStatus.Delivered)
                order.MarkDelivered();

            Context.Orders.Add(order);
            await Context.SaveChangesAsync();
            return order;
        }

        public void Dispose() => Context.Dispose();
    }

    // ------------------------------------------------------------------ Reconnaissance

    [Theory]
    [InlineData("RECU", true)]
    [InlineData("RECU A1B2C3D4", true)]
    [InlineData("LIVRE", true)]
    [InlineData("LIVRE TOUT", true)]
    [InlineData("LIVRE A1B2C3D4 CODE 1234", true)]
    [InlineData("LIVREUR", false)]
    [InlineData("RECUPERE", false)]
    [InlineData("LIVRER", false)]
    [InlineData("", false)]
    public void Matches_ReconnaitLesCommandesEtRienDExtra(string upper, bool expected)
    {
        // Le contrôleur teste ce prédicat avant d'appeler le gestionnaire : une régression ici
        // détournerait des messages métier (ex. « LIVREUR … ») vers la clôture de courses.
        Assert.Equal(expected, RiderDeliveryCommands.Matches(upper));
    }

    // ------------------------------------------------------------------ Statut « RECU »

    [Fact]
    public async Task Recu_FaitPasserLaCourseAssigneeEnRoute()
    {
        using var f = new Fixture();
        var order = await f.AddOrderAsync(OrderStatus.RiderAssigned);

        await f.Commands.HandleAsync(f.Rider, "RECU", f.Reply);

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Contains("Colis récupéré", f.LastReply);
    }

    [Fact]
    public async Task Recu_SansCourse_RepondSansRienCasser()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Rider, "RECU", f.Reply);

        Assert.Contains("Aucune course à récupérer", f.LastReply);
    }

    // ------------------------------------------------- Tournée multi-clients (non couvert avant)

    [Fact]
    public async Task Livre_SansCodeAvecPlusieursCourses_NeClotureRienEtExpliqueLaTournee()
    {
        using var f = new Fixture();
        var first = await f.AddOrderAsync(OrderStatus.InTransit);
        var second = await f.AddOrderAsync(OrderStatus.InTransit);

        await f.Commands.HandleAsync(f.Rider, "LIVRE", f.Reply);

        // Aucune des deux courses ne doit être clôturée : chaque client est notifié au bon moment.
        Assert.Equal(OrderStatus.InTransit, first.Status);
        Assert.Equal(OrderStatus.InTransit, second.Status);
        Assert.Contains("Plusieurs livraisons", f.LastReply);
        Assert.Contains("LIVRE TOUT", f.LastReply);
    }

    [Fact]
    public async Task LivreTout_ClotureToutesLesCoursesEtNotifieChaqueClient()
    {
        using var f = new Fixture();
        var first = await f.AddOrderAsync(OrderStatus.InTransit);
        var second = await f.AddOrderAsync(OrderStatus.InTransit);

        await f.Commands.HandleAsync(f.Rider, "LIVRE TOUT", f.Reply);

        Assert.Equal(OrderStatus.Delivered, first.Status);
        Assert.Equal(OrderStatus.Delivered, second.Status);
        Assert.Equal(2, f.ClientNotifications);
        Assert.Contains("2 course(s) livrée(s)", f.LastReply);
    }

    [Fact]
    public async Task Livre_AvecCodeDeCourse_NeClotureQueLaCourseVisee()
    {
        using var f = new Fixture();
        var target = await f.AddOrderAsync(OrderStatus.InTransit);
        var other = await f.AddOrderAsync(OrderStatus.InTransit);

        var shortCode = target.Id.ToString("N")[..8].ToUpperInvariant();
        await f.Commands.HandleAsync(f.Rider, $"LIVRE {shortCode}", f.Reply);

        Assert.Equal(OrderStatus.Delivered, target.Status);
        Assert.Equal(OrderStatus.InTransit, other.Status);
        Assert.Equal(1, f.ClientNotifications);
    }

    // --------------------------------------------- Preuve de livraison (garantie Colis Sûr)

    [Fact]
    public async Task Livre_CodeClientCorrect_ClotureEtEnregistreLaPreuve()
    {
        using var f = new Fixture(new DeliveryProofOptions { RequireClientCode = true });
        var order = await f.AddOrderAsync(OrderStatus.InTransit);
        var expected = order.EnsureDeliveryCode();

        await f.Commands.HandleAsync(f.Rider, $"LIVRE CODE {expected}", f.Reply);

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveryCodeVerifiedAt);
    }

    [Fact]
    public async Task Livre_CodeClientFaux_IncrementeLesTentativesSansCloturer()
    {
        using var f = new Fixture(new DeliveryProofOptions { RequireClientCode = true });
        var order = await f.AddOrderAsync(OrderStatus.InTransit);
        var wrong = WrongCodeFor(order);

        await f.Commands.HandleAsync(f.Rider, $"LIVRE CODE {wrong}", f.Reply);

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Equal(1, order.DeliveryCodeAttempts);
        Assert.Contains("Code incorrect", f.LastReply);
    }

    [Fact]
    public async Task Livre_TropDeTentativesFausses_VerrouilleLaCourse()
    {
        using var f = new Fixture(new DeliveryProofOptions { RequireClientCode = true });
        var order = await f.AddOrderAsync(OrderStatus.InTransit);
        var wrong = WrongCodeFor(order);

        // MaxDeliveryCodeAttempts = 5 : la 6e tentative est refusée par verrouillage,
        // sans nouvelle incrémentation (anti-force brute).
        for (var i = 0; i < Order.MaxDeliveryCodeAttempts + 1; i++)
            await f.Commands.HandleAsync(f.Rider, $"LIVRE CODE {wrong}", f.Reply);

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Equal(Order.MaxDeliveryCodeAttempts, order.DeliveryCodeAttempts);
        Assert.Contains("Trop de tentatives", f.LastReply);
    }

    /// <summary>
    /// Code à 4 chiffres garanti différent de celui de la course : la génération est aléatoire,
    /// un « 0000 » en dur pourrait tomber juste une fois sur dix mille.
    /// </summary>
    private static string WrongCodeFor(Order order)
    {
        var expected = order.EnsureDeliveryCode();
        return expected == "0000" ? "0001" : "0000";
    }

    [Fact]
    public async Task Livre_ToutInterditQuandLaPreuveEstObligatoire()
    {
        using var f = new Fixture(new DeliveryProofOptions { RequireClientCode = true });
        var order = await f.AddOrderAsync(OrderStatus.InTransit);

        await f.Commands.HandleAsync(f.Rider, "LIVRE TOUT", f.Reply);

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Contains("Clôture groupée impossible", f.LastReply);
    }

    [Fact]
    public async Task Livre_CodeSansCourseCorrespondante_RefuseDeDeviner()
    {
        using var f = new Fixture();
        await f.AddOrderAsync(OrderStatus.InTransit);

        await f.Commands.HandleAsync(f.Rider, "LIVRE ZZZZZZZZ", f.Reply);

        Assert.Contains("Aucune course en cours de livraison", f.LastReply);
    }
}
