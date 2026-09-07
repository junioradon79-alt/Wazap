using Wazap.Infrastructure.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.UnitTests;

/// <summary>
/// Monte un <see cref="WebhookWhatsAppController"/> complet sur une base InMemory :
/// c'est le point d'entrée réel de tout le produit (commandes vendeur, statuts livreur,
/// notes client, bot prospects) et il n'avait aucune couverture. Les tests qui s'appuient
/// dessus exercent le ROUTAGE des commandes WhatsApp de bout en bout.
/// </summary>
internal sealed class WebhookHarness : IDisposable
{
    public ApplicationDbContext Context { get; }
    public RecordingWhatsAppSender Sender { get; } = new();
    public WebhookWhatsAppController Controller { get; }

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wazap-webhook-" + Guid.NewGuid().ToString("N"));

    public WebhookHarness(DeliveryProofOptions? deliveryProof = null, string? teamPhone = null)
    {
        Directory.CreateDirectory(_tempDir);

        // InMemory ignore les transactions : OrderService en ouvre une, l'avertissement
        // serait sinon promu en exception.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("webhook-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Context = new ApplicationDbContext(options);

        var config = teamPhone is null
            ? new ConfigStub()
            : new ConfigStub(("Prospect:TeamPhone", teamPhone));

        var whatsAppOptions = new WhatsAppOptions();
        var orchestrator = new WhatsAppOrchestrationService(Sender, whatsAppOptions,
            NullLogger<WhatsAppOrchestrationService>.Instance);

        var offers = new DeliveryOfferService(Context, Sender, whatsAppOptions,
            new GeoOptions(), new GroupingOptions(), new ClientOptions(), orchestrator,
            new RiderSecurityOptions(), new RiderReputationOptions(),
            NullLogger<DeliveryOfferService>.Instance);

        var proof = deliveryProof ?? new DeliveryProofOptions();

        var orders = new OrderService(Context, new CurrentUserStub(), offers, proof,
            NullLogger<OrderService>.Instance);

        Controller = new WebhookWhatsAppController(
            Context,
            new RiderService(Context, new FakeWebHostEnvironment(_tempDir), Sender,
                new RiderScansOptions { AllowUnencryptedStorage = true },
                NullLogger<RiderService>.Instance),
            new VendorService(Context, new NoGeocoding(), NullLogger<VendorService>.Instance),
            offers,
            orders,
            orchestrator,
            new ProspectAutoService(Context, Sender, config, NullLogger<ProspectAutoService>.Instance),
            new LeadConversionService(Context, new FakePasswordHasher(), new TrialOptions(), Sender,
                NullLogger<LeadConversionService>.Instance),
            new ColisSurService(Context, Sender, config, new ColisSurOptions(),
                new ManualPayoutService(NullLogger<ManualPayoutService>.Instance),
                NullLogger<ColisSurService>.Instance),
            Sender,
            proof,
            new RiderRatingService(Context, new RiderReputationOptions(), NullLogger<RiderRatingService>.Instance),
            NullLogger<WebhookWhatsAppController>.Instance,
            config);
    }

    /// <summary>Simule un message texte entrant au format réel de WhatChimp.</summary>
    public Task SendAsync(string phone, string text)
    {
        var payload = JsonSerializer.SerializeToElement(new { chat_id = phone, user_message = text });
        return Controller.Handle(payload);
    }

    /// <summary>Dernier message envoyé au numéro indiqué (null si aucun).</summary>
    public string? LastMessageTo(string phone)
        => Sender.TextMessages.LastOrDefault(m => m.Phone == phone).Message;

    public void Dispose()
    {
        Context.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public Guid? Id => null;
        public UserRole? Role => null;
    }

    private sealed class NoGeocoding : IGeocodingService
    {
        public Task<(double Latitude, double Longitude)?> GeocodeAsync(string address, CancellationToken ct = default)
            => Task.FromResult<(double, double)?>(null);
    }
}
