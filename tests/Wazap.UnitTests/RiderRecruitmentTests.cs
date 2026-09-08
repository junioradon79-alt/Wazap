using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Bot de recrutement livreur (piste A) : un inconnu exprime l'intention de livrer sur
/// WhatsApp, le bot collecte nom + quartier + photo CNI, puis crée le compte livreur avec
/// dossier d'identité (scan chiffré, consentement « whatsapp » tracé) prêt pour la
/// certification en 1 clic dans /app/certifications.
/// </summary>
public class RiderRecruitmentTests
{
    private const string CandidatePhone = "+2250700000003";

    [Fact]
    public async Task UnknownNumber_RiderIntent_CreatesLead_AndAsksSteps()
    {
        var harness = new WebhookHarness(teamPhone: "+2250500000000");

        await harness.SendAsync(CandidatePhone, "bonjour je veux livrer");

        var lead = await harness.Context.Leads.SingleAsync(l => l.WhatsAppNumber == CandidatePhone);
        Assert.Equal("whatsapp-livreur", lead.Source);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Contains("nom complet", harness.LastMessageTo(CandidatePhone));
        Assert.Contains(harness.Sender.TextMessages,
            m => m.Phone == "+2250500000000" && m.Message.Contains("Candidat livreur détecté"));
    }

    [Fact]
    public async Task Candidate_NameZoneThenPhoto_CreatesAccountIdentityScanAndConsent()
    {
        var harness = new WebhookHarness(teamPhone: "+2250500000000");

        await harness.SendAsync(CandidatePhone, "je veux livrer");
        await harness.SendAsync(CandidatePhone, "Ibrahim Koné Marcory");

        var lead = await harness.Context.Leads.SingleAsync(l => l.WhatsAppNumber == CandidatePhone);
        Assert.Equal("Ibrahim Koné", lead.ContactName);
        Assert.Equal("Marcory", lead.Zone);
        Assert.Contains("Dernière étape", harness.LastMessageTo(CandidatePhone));

        await harness.SendImageAsync(CandidatePhone, "https://media.example/cni-kone.jpg");

        // Compte livreur créé : identifiants dérivés du nom, zone reprise, code de parrainage.
        var rider = await harness.Context.Users.SingleAsync(u => u.Role == UserRole.Rider);
        Assert.Equal("ibrahimkone", rider.Username);
        Assert.Equal(CandidatePhone, rider.PhoneNumber);
        Assert.Equal("Marcory", rider.Zone);
        Assert.Matches(@"^WA-[A-Z0-9]{4}$", rider.ReferralCode);

        // Dossier d'identité : scan stocké (chiffré), provenance gardée, consentement WhatsApp tracé.
        var identity = await harness.Context.RiderIdentities.SingleAsync();
        Assert.Equal(RiderIdentityStatus.Pending, identity.Status);
        Assert.NotNull(identity.ScanFileName);
        Assert.Equal("https://media.example/cni-kone.jpg", identity.IdScanUrl);
        Assert.Equal("whatsapp", identity.ConsentMethod);
        Assert.NotNull(identity.ConsentGivenAt);
        Assert.True(Directory.Exists(Path.Combine(harness.TempDir, "App_Data", "rider-scans")));

        // Lead converti, identifiants envoyés, équipe alertée pour la certification.
        Assert.Equal(LeadStatus.Converted, (await harness.Context.Leads.SingleAsync()).Status);
        var welcome = harness.LastMessageTo(CandidatePhone);
        Assert.Contains("Identifiant", welcome);
        Assert.Contains("ibrahimkone", welcome);
        Assert.Contains("Mot de passe", welcome);
        Assert.Contains(harness.Sender.TextMessages,
            m => m.Phone == "+2250500000000" && m.Message.Contains("Candidature livreur COMPLÈTE"));
    }

    [Fact]
    public async Task Candidate_PhotoBeforeNameZone_AsksMissingInfo_NoAccount()
    {
        var harness = new WebhookHarness();

        await harness.SendAsync(CandidatePhone, "je veux livrer");
        await harness.SendImageAsync(CandidatePhone, "https://media.example/cni.jpg");

        Assert.False(await harness.Context.Users.AnyAsync());
        Assert.False(await harness.Context.RiderIdentities.AnyAsync());
        Assert.Contains("Envoyez ensuite", harness.LastMessageTo(CandidatePhone));
    }

    [Fact]
    public async Task UnknownNumber_NoRiderIntent_LeftToProspectBot()
    {
        var harness = new WebhookHarness();

        await harness.SendAsync(CandidatePhone, "restaurant chez awa poulet braisé");

        Assert.False(await harness.Context.Leads.AnyAsync(l => l.Source == "whatsapp-livreur"));
        Assert.True(await harness.Context.Leads.AnyAsync());
    }

    [Fact]
    public async Task KnownRider_Text_NotConsumedByRecruitment()
    {
        var harness = new WebhookHarness();
        var rider = new User("rider-existant", "hash", UserRole.Rider, CandidatePhone);
        harness.Context.Users.Add(rider);
        await harness.Context.SaveChangesAsync();

        var service = NewService(harness);
        var consumed = await service.TryHandleCandidateAsync(CandidatePhone, "je veux livrer");

        Assert.False(consumed);
        Assert.False(await harness.Context.Leads.AnyAsync());
    }

    [Fact]
    public async Task Candidate_DownloadFailure_NoAccount_ErrorReply()
    {
        var downloader = new WebhookHarness.FakeMediaDownloader { Fail = true };
        var harness = new WebhookHarness(mediaDownloader: downloader);

        await harness.SendAsync(CandidatePhone, "je veux livrer");
        await harness.SendAsync(CandidatePhone, "Awa Marcory");
        await harness.SendImageAsync(CandidatePhone, "https://media.example/cni.jpg");

        Assert.False(await harness.Context.Users.AnyAsync());
        Assert.Contains("Impossible de récupérer", harness.LastMessageTo(CandidatePhone));
    }

    [Fact]
    public async Task ConvertedCandidate_SecondPhoto_UpdatesScan_NoDuplicateAccount()
    {
        var harness = new WebhookHarness();

        await harness.SendAsync(CandidatePhone, "je veux livrer");
        await harness.SendAsync(CandidatePhone, "Awa Kouassi Yopougon");
        await harness.SendImageAsync(CandidatePhone, "https://media.example/cni-1.jpg");
        // Après conversion, le candidat est un livreur connu : une nouvelle photo
        // met à jour son scan au lieu de créer un second compte.
        await harness.SendImageAsync(CandidatePhone, "https://media.example/cni-2.jpg");

        Assert.Equal(1, await harness.Context.Users.CountAsync(u => u.Role == UserRole.Rider));
        var identity = await harness.Context.RiderIdentities.SingleAsync();
        Assert.Equal("https://media.example/cni-2.jpg", identity.IdScanUrl);
        Assert.Equal(LeadStatus.Converted, (await harness.Context.Leads.SingleAsync()).Status);
        Assert.Contains("reçue", harness.LastMessageTo(CandidatePhone));
    }

    [Fact]
    public async Task KnownRider_Photo_ConsentRecordedAsWhatsApp()
    {
        var harness = new WebhookHarness();
        var rider = new User("rider-media", "hash", UserRole.Rider, CandidatePhone);
        harness.Context.Users.Add(rider);
        await harness.Context.SaveChangesAsync();

        await harness.SendImageAsync(CandidatePhone, "https://media.example/cni.jpg");

        var identity = await harness.Context.RiderIdentities.SingleAsync();
        Assert.Equal("whatsapp", identity.ConsentMethod);
        Assert.NotNull(identity.ConsentGivenAt);
    }

    private static RiderRecruitmentService NewService(WebhookHarness harness)
    {
        var riderService = new RiderService(harness.Context,
            new FakeWebHostEnvironment(harness.TempDir), harness.Sender,
            new RiderScansOptions { AllowUnencryptedStorage = true },
            NullLogger<RiderService>.Instance);
        return new RiderRecruitmentService(harness.Context, new FakePasswordHasher(), riderService,
            harness.Sender, new WebhookHarness.FakeMediaDownloader(), new ConfigStub(),
            NullLogger<RiderRecruitmentService>.Instance);
    }
}
