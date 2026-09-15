using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Rapprochement d'un compte par son numéro WhatsApp.
/// <para>
/// Chaque message entrant, chaque création de commande, chaque diffusion et chaque demande de
/// réinitialisation chargeait TOUTE la table des utilisateurs pour comparer les numéros en
/// mémoire. Le rapprochement s'appuie désormais sur une clé indexée (les 8 derniers chiffres,
/// <c>Users.PhoneSuffix</c>) — la seule forme commune à l'ancien format ivoirien
/// (<c>+225</c> + 8 chiffres) et au nouveau (<c>+225</c> + 10 chiffres).
/// </para>
/// <para>
/// Ces tests tournent sur un fournisseur RELATIONNEL (SQLite) : ils vérifient la traduction
/// SQL de la requête et, surtout, que la confirmation exacte en mémoire écarte bien deux
/// numéros qui partageraient la même terminaison sans être la même ligne.
/// </para>
/// </summary>
public class PhoneSuffixLookupTests
{
    // ------------------------------------------------------------------ Clé de rapprochement

    [Theory]
    [InlineData("+2250758380011", "58380011")]  // nouveau format ivoirien (13 chiffres)
    [InlineData("+22558380011", "58380011")]    // ancien format ivoirien (11 chiffres)
    [InlineData("2250758380011", "58380011")]   // sans « + »
    [InlineData("07 58 38 00 11", "58380011")]  // espaces
    [InlineData("+3361234", "3361234")]         // numéro plus court que 8 chiffres
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SubscriberSuffix_RetientLesHuitDerniersChiffres(string? phone, string expected)
        => Assert.Equal(expected, PhoneNumberNormalizer.SubscriberSuffix(phone));

    [Fact]
    public void AncienEtNouveauFormatIvoirien_PartagentLaMemeCle()
    {
        // C'est la propriété qui rend la clé utilisable : les deux formats de la MÊME ligne
        // doivent produire la même valeur indexée.
        Assert.Equal(
            PhoneNumberNormalizer.SubscriberSuffix("+2250758380011"),
            PhoneNumberNormalizer.SubscriberSuffix("+22558380011"));
    }

    [Fact]
    public void PhoneSuffix_EstMaintenuParLeDomaine()
    {
        var user = new User("vendeur", "hash", UserRole.Vendor, "+2250758380011");
        Assert.Equal("58380011", user.PhoneSuffix);

        user.UpdatePhoneNumber("+2250701020304");
        Assert.Equal("01020304", user.PhoneSuffix);

        var sansNumero = new User("sans-numero", "hash", UserRole.Vendor);
        Assert.Null(sansNumero.PhoneSuffix);
    }

    // ------------------------------------------------------------------- Résolution en base

    private static OrderService NewOrderService(ApplicationDbContext context)
    {
        var sender = new RecordingWhatsAppSender();
        var whatsAppOptions = new Wazap.Application.Configuration.WhatsAppOptions();
        var orchestrator = new WhatsAppOrchestrationService(
            sender, whatsAppOptions, NullLogger<WhatsAppOrchestrationService>.Instance);

        var offers = new DeliveryOfferService(context, sender, whatsAppOptions,
            new Wazap.Application.Configuration.GeoOptions(),
            new Wazap.Application.Configuration.GroupingOptions(),
            new Wazap.Application.Configuration.ClientOptions(), orchestrator,
            new Wazap.Application.Configuration.RiderSecurityOptions(),
            new Wazap.Application.Configuration.RiderReputationOptions(),
            new Wazap.Application.Configuration.ClientPaymentOptions(),
            new Wazap.Application.Configuration.RiderPriorityOptions(),
            NullLogger<DeliveryOfferService>.Instance);

        return new OrderService(context, new AnonymousUser(), offers,
            new Wazap.Application.Configuration.DeliveryProofOptions(),
            NullLogger<OrderService>.Instance);
    }

    [Fact]
    public async Task CreateOrder_RetrouveLeVendeur_EcritEnNouveauFormat_EtAppeleEnAncien()
    {
        using var harness = new SqliteHarness();

        // Vendeur enregistré au format COURANT (+225 + 10 chiffres).
        var vendor = new User("vendeur-ci", "hash", UserRole.Vendor, "+2250758380011");
        harness.Context.Users.Add(vendor);
        harness.Context.SaveChanges();

        var service = NewOrderService(harness.Context);

        // Le vendeur écrit depuis l'ANCIEN format (+225 + 8 chiffres) : même ligne WhatsApp.
        var order = await service.CreateOrderAsync(new CreateOrderRequest
        {
            ClientName = "Client",
            ClientWhatsAppNumber = "+2250700000009",
            VendorWhatsAppNumber = "+22558380011",
            Description = "1 colis",
            Amount = 2000m
        });

        Assert.Equal(vendor.Id, order.VendorUserId);
    }

    [Fact]
    public async Task CreateOrder_MemeTerminaison_MaisAutrePays_NeConfondPasLesComptes()
    {
        using var harness = new SqliteHarness();

        // Deux numéros qui partagent leurs 8 derniers chiffres sans être la même ligne :
        // le pré-filtre indexé les retourne TOUS LES DEUX, la confirmation exacte doit
        // écarter le mauvais.
        var vendeurFrancais = new User("vendeur-fr", "hash", UserRole.Vendor, "+33658380011");
        harness.Context.Users.Add(vendeurFrancais);
        harness.Context.SaveChanges();

        var service = NewOrderService(harness.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOrderAsync(new CreateOrderRequest
        {
            ClientName = "Client",
            ClientWhatsAppNumber = "+2250700000009",
            VendorWhatsAppNumber = "+2250758380011",
            Description = "1 colis",
            Amount = 2000m
        }));
    }

    private sealed class AnonymousUser : Wazap.Application.Abstractions.ICurrentUser
    {
        public Guid? Id => null;
        public UserRole? Role => null;
    }
}
