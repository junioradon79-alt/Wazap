using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests directs de <see cref="RiderTextCommands"/> — commandes du livreur hors course
/// (P2 / C-13, dernier temps). Le routage de bout en bout était déjà couvert pour `ZONE` et
/// `INDISPO` par les tests du webhook ; ce qui ne l'était pas, ce sont les branches de refus
/// (format de réponse invalide, programme inactif) et la bascule en ligne, sur laquelle repose
/// la réception des offres : un livreur « DISPO » qui ne reçoit rien est un bug perçu comme
/// une perte de revenus.
/// </summary>
public class RiderTextCommandsTests
{
    private const string RiderPhone = "+2250700000031";

    private sealed class Fixture : IDisposable
    {
        public ApplicationDbContext Context { get; }
        public RecordingWhatsAppSender Sender { get; } = new();
        public RiderTextCommands Commands { get; }
        public User Rider { get; }
        public List<string> Replies { get; } = new();

        private readonly string _tempDir =
            Path.Combine(Path.GetTempPath(), "wazap-ridercmd-" + Guid.NewGuid().ToString("N"));

        public Fixture(bool programEnabled = false)
        {
            Directory.CreateDirectory(_tempDir);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("rider-commands-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            Context = new ApplicationDbContext(options);

            Rider = new User("livreur", "hash", UserRole.Rider, RiderPhone);
            Context.Users.Add(Rider);
            Context.SaveChanges();

            var scans = new RiderScansOptions { AllowUnencryptedStorage = true };
            var riderService = new RiderService(Context, new FakeWebHostEnvironment(_tempDir), Sender,
                scans, NullLogger<RiderService>.Instance);
            var ratings = new RiderRatingService(Context, new RiderReputationOptions(),
                NullLogger<RiderRatingService>.Instance);
            var program = new RiderProgramService(Context,
                new RiderProgramOptions { Enabled = programEnabled }, Sender,
                NullLogger<RiderProgramService>.Instance);

            Commands = new RiderTextCommands(riderService, ratings, program);
        }

        public Task Reply(User user, string message)
        {
            Replies.Add(message);
            return Task.CompletedTask;
        }

        public string LastReply => Replies.Count == 0 ? string.Empty : Replies[^1];

        public void Dispose()
        {
            Context.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* nettoyage au mieux */ }
        }
    }

    // ------------------------------------------------------------------ Reconnaissance

    [Theory]
    [InlineData("DISPO", true)]
    [InlineData("INDISPO", true)]
    [InlineData("PROGRAMME", true)]
    [InlineData("AMBASSADEUR", true)]
    [InlineData("AVIS", true)]
    [InlineData("REPONDRE 1 Merci !", true)]
    [InlineData("RECU", false)]
    [InlineData("LIVRE", false)]
    [InlineData("ZONE Cocody", false)]
    [InlineData("LIVRAISON 2 poulets", false)]
    [InlineData("", false)]
    public void Matches_ReconnaitLesCommandesLivreurEtRienDExtra(string message, bool expected)
    {
        // Un faux positif ici détournerait une commande d'un autre gestionnaire : « ZONE » est
        // traité par le contrôleur (partagé entre rôles) et « LIVRAISON » par les vendeurs.
        Assert.Equal(expected, RiderTextCommands.Matches(message.ToUpperInvariant(), message));
    }

    // ------------------------------------------------------------ Disponibilité

    [Fact]
    public async Task Dispo_PasseLeLivreurEnLigne()
    {
        using var f = new Fixture();
        Assert.False(f.Rider.IsAvailable);

        await f.Commands.HandleAsync(f.Rider, "DISPO", f.Reply);

        Assert.True(f.Rider.IsAvailable);
        Assert.Contains("en ligne", f.LastReply);
    }

    [Fact]
    public async Task Indispo_PasseLeLivreurHorsLigne()
    {
        using var f = new Fixture();
        f.Rider.SetAvailability(true);
        await f.Context.SaveChangesAsync();

        await f.Commands.HandleAsync(f.Rider, "INDISPO", f.Reply);

        Assert.False(f.Rider.IsAvailable);
        Assert.Contains("hors ligne", f.LastReply);
    }

    [Fact]
    public async Task Dispo_EstReconnuSansEspaceNiAccentDifferent()
    {
        using var f = new Fixture();

        // Message tel qu'un téléphone basique l'envoie : espaces autour, minuscules.
        await f.Commands.HandleAsync(f.Rider, "  dispo  ", f.Reply);

        Assert.True(f.Rider.IsAvailable);
    }

    // ------------------------------------------------------------ Avis et réponses

    [Fact]
    public async Task Avis_SansNote_RepondQuIlNyEnAPasEncore()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Rider, "AVIS", f.Reply);

        Assert.Contains("Aucun avis", f.LastReply);
    }

    [Fact]
    public async Task Repondre_SansNumeroNiTexte_ExpliqueLeFormat()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Rider, "REPONDRE", f.Reply);

        Assert.Contains("Format : REPONDRE", f.LastReply);
    }

    [Fact]
    public async Task Repondre_AvecNumeroInconnu_RepondSansLever()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Rider, "REPONDRE 3 Merci pour votre confiance", f.Reply);

        // Aucun avis n°3 : le livreur reçoit une explication, jamais une exception technique.
        Assert.NotEmpty(f.LastReply);
        Assert.DoesNotContain("Exception", f.LastReply);
    }

    // ------------------------------------------------------------ Programme Ambassadeur

    [Fact]
    public async Task Programme_Desactive_AnnonceQueLeProgrammeNestPasActif()
    {
        using var f = new Fixture(programEnabled: false);

        await f.Commands.HandleAsync(f.Rider, "PROGRAMME", f.Reply);

        Assert.Contains("n'est pas actif", f.LastReply);
    }

    [Fact]
    public async Task Programme_Actif_AfficheLaProgression()
    {
        using var f = new Fixture(programEnabled: true);

        await f.Commands.HandleAsync(f.Rider, "AMBASSADEUR", f.Reply);

        // Le livreur n'a encore rien livré : la progression existe malgré tout (objectifs à 0).
        Assert.NotEmpty(f.LastReply);
        Assert.DoesNotContain("n'est pas actif", f.LastReply);
    }
}
