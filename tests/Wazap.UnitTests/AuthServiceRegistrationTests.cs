using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Test de l'inscription avec code de parrainage (AuthService) : le nouveau vendeur
/// est lié au parrain, le parrain reçoit +5 crédits et l'octroi est tracé (REF-…)
/// pour le « suivi des filleuls » de l'espace vendeur.
/// </summary>
public class AuthServiceRegistrationTests
{
    private static AuthService CreateService(TestDbContext db, RecordingWhatsAppSender sender)
    {
        var whatsApp = new WhatsAppOrchestrationService(sender, new WhatsAppOptions(), NullLogger<WhatsAppOrchestrationService>.Instance);
        return new AuthService(
            db.Context,
            new FakePasswordHasher(),
            new FakeJwtTokenGenerator(),
            whatsApp,
            new SecurityOptions(),
            new TrialOptions { Enabled = true, FreeCreditsOnRegistration = 15 },
            new IvoryCoastNumberingOptions { Enabled = false },
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task Register_WithReferralCode_CreditsSponsor_AndTracesGrant()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var sponsor = new User("pizzeria", "hash", UserRole.Vendor, "+2250700000001");
        context.Users.Add(sponsor);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var user = await service.RegisterAsync(new RegisterRequest
        {
            Username = "nouveau_vendeur",
            Password = "Motdepasse123!",
            Role = UserRole.Vendor,
            PhoneNumber = "+2250700000002",
            ReferralCode = sponsor.ReferralCode
        });

        var stored = await context.Users.FindAsync(user.Id);
        Assert.Equal(sponsor.Id, stored!.ReferredByUserId);
        Assert.Equal(5, sponsor.Credits);

        var refTxn = Assert.Single(context.CreditTransactions.Where(t => t.TransactionReference.StartsWith("REF-")));
        Assert.Equal(sponsor.Id, refTxn.VendorId);
        Assert.Equal(5, refTxn.CreditsPurchased);

        // Notification au parrain + guide d'onboarding au nouveau vendeur.
        Assert.Contains(sender.TextMessages, m => m.Phone == sponsor.PhoneNumber && m.Message.Contains("5 crédits"));
        Assert.Contains(sender.TextMessages, m => m.Phone == "+2250700000002" && m.Message.Contains("Bienvenue chez WAZAP"));
    }

    [Fact]
    public async Task Register_WithoutReferralCode_DoesNotCreditAnyone()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var user = await service.RegisterAsync(new RegisterRequest
        {
            Username = "vendeur_solo",
            Password = "Motdepasse123!",
            Role = UserRole.Vendor,
            PhoneNumber = "+2250700000003"
        });

        var stored = await context.Users.FindAsync(user.Id);
        Assert.Null(stored!.ReferredByUserId);
        Assert.DoesNotContain(context.CreditTransactions, t => t.TransactionReference.StartsWith("REF-"));
        // Le trial reste tracé mais aucun octroi parrainage.
        Assert.Single(context.CreditTransactions.Where(t => t.TransactionReference.StartsWith("TRIAL-")));
    }

    private sealed class FakeJwtTokenGenerator : IJwtTokenGenerator
    {
        public string Generate(User user) => "fake-jwt";
    }
}
