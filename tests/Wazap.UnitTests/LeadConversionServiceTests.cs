using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Contexte EF InMemory JETABLE : une seule instance partagée par test (service + assertions).
/// Un contexte unique évite les écueils de la racine InMemory partagée entre instances
/// et garantit que le change tracker reflète les écritures du service testé.
/// </summary>
public sealed class TestDbContext : IDisposable
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly ApplicationDbContext _context;

    public TestDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("test-" + Guid.NewGuid().ToString("N"), _root)
            .Options;
        _context = new ApplicationDbContext(options);
    }

    public ApplicationDbContext Context => _context;

    public void Dispose() => _context.Dispose();
}

/// <summary>
/// Tests du service de conversion Lead → compte vendeur (LeadConversionService) :
/// création, idempotence par numéro, parrainage (+5 tracé), cas refusés.
/// </summary>
public class LeadConversionServiceTests
{
    private static LeadConversionService CreateService(
        TestDbContext db,
        RecordingWhatsAppSender? sender = null)
    {
        var trial = new TrialOptions { Enabled = true, FreeCreditsOnRegistration = 15 };
        return new LeadConversionService(
            db.Context,
            new FakePasswordHasher(),
            trial,
            sender ?? new RecordingWhatsAppSender(),
            NullLogger<LeadConversionService>.Instance);
    }

    [Fact]
    public async Task ConvertLead_CreatesVendor_WithTrialCreditsTraced()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var lead = new Lead("Chez Awa", "+2250708091011", "Marcory", "page-vente", "Awa");
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var result = await service.ConvertAsync(lead.Id, sendWelcome: false);

        Assert.False(result.AlreadyExisted);
        Assert.Equal("chezawa", result.Username);
        Assert.Equal(15, result.Credits);
        Assert.Matches("^WA-[A-Z2-9]{4}$", result.ReferralCode);

        var storedLead = await context.Leads.FindAsync(lead.Id);
        Assert.Equal(LeadStatus.Converted, storedLead!.Status);

        var vendor = await context.Users.FindAsync(result.VendorId);
        Assert.NotNull(vendor);
        Assert.Equal(UserRole.Vendor, vendor!.Role);
        Assert.Equal(15, vendor.Credits);
        Assert.Equal("+2250708091011", vendor.PhoneNumber);
        Assert.Single(context.CreditTransactions.Where(t => t.TransactionReference.StartsWith("TRIAL-")));
    }

    [Fact]
    public async Task ConvertLead_WhenVendorAlreadyExists_IsIdempotent()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var existing = new User("awa", new FakePasswordHasher().Hash("x"), UserRole.Vendor, "+2250708091011");
        context.Users.Add(existing);
        var lead = new Lead("Chez Awa", "+2250708091011", "Marcory", "page-vente");
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.ConvertAsync(lead.Id, sendWelcome: false);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(existing.Id, result.VendorId);
        Assert.Equal(LeadStatus.Converted, (await context.Leads.FindAsync(lead.Id))!.Status);
        Assert.Single(context.Users.Where(u => u.Role == UserRole.Vendor));
    }


    [Fact]
    public async Task ConvertLead_DiscardedLead_Throws()
    {
        var db = new TestDbContext();
        var lead = new Lead("Chez Awa", "+2250708091011", "Marcory", "page-vente");
        lead.SetStatus(LeadStatus.Discarded);
        db.Context.Leads.Add(lead);
        await db.Context.SaveChangesAsync();

        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertAsync(lead.Id));
    }

    [Fact]
    public async Task ConvertLead_WhatsappLivreurSource_Throws()
    {
        var db = new TestDbContext();
        var lead = new Lead("Coursier", "+2250708091011", "Cocody", "whatsapp-livreur");
        db.Context.Leads.Add(lead);
        await db.Context.SaveChangesAsync();

        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertAsync(lead.Id));
        Assert.Contains("profil livreur", ex.Message);
    }

    [Fact]
    public async Task ConvertLead_WithReferralCode_LinksSponsor_AndTracesPlusFive()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var sponsor = new User("pizzeria", new FakePasswordHasher().Hash("x"), UserRole.Vendor, "+2250700000001");
        context.Users.Add(sponsor);
        var lead = new Lead("Chez Awa", "+2250708091011", "Marcory", "page-vente");
        lead.SetReferralCode(sponsor.ReferralCode);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);
        var result = await service.ConvertAsync(lead.Id, sendWelcome: false);

        var vendor = await context.Users.FindAsync(result.VendorId);
        Assert.Equal(sponsor.Id, vendor!.ReferredByUserId);
        Assert.Equal(5, sponsor.Credits);

        var refTxn = Assert.Single(context.CreditTransactions.Where(t => t.TransactionReference.StartsWith("REF-")));
        Assert.Equal(sponsor.Id, refTxn.VendorId);
        Assert.Equal(5, refTxn.CreditsPurchased);

        // Notification WhatsApp au parrain (best-effort).
        Assert.Contains(sender.TextMessages, m => m.Phone == sponsor.PhoneNumber && m.Message.Contains("5 crédits"));
    }

    [Fact]
    public async Task ConvertLead_UnknownReferralCode_IgnoresSponsor()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var lead = new Lead("Chez Awa", "+2250708091011", "Marcory", "page-vente");
        lead.SetReferralCode("WA-XXXX");
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.ConvertAsync(lead.Id, sendWelcome: false);

        var vendor = await context.Users.FindAsync(result.VendorId);
        Assert.Null(vendor!.ReferredByUserId);
    }
}
