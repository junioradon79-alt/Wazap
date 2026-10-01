using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Garde-fou architectural inviolable : Règle Canonique 10 (ZÉRO SAISIE TEXTE / 1-TAP ABSOLU).
/// <para>
/// Tout flux d'onboarding ou d'interaction avec les livreurs et commerçants d'Abidjan DOIT
/// impérativement passer par des liens cliquables « wa.me », des boutons interactifs ou l'OCR.
/// Aucun message système ne doit exiger de l'utilisateur qu'il compose du texte libre au clavier.
/// </para>
/// </summary>
public class ZeroTextInputRuleTests
{
    private const string OfficialPhone = "2250544051972";

    [Fact]
    public async Task WelcomeLivreur_ExigeLiens1TapWaMe_EtInterditSaisieNomManuelle()
    {
        var harness = new WebhookHarness();
        var phone = "+2250700000099";

        await harness.SendAsync(phone, "DISPO");
        var reply = harness.LastMessageTo(phone);

        // 1. Les 5 liens cliquables 1-tap indispensables doivent être présents
        Assert.Contains($"wa.me/{OfficialPhone}?text=1%20Cocody", reply);
        Assert.Contains($"wa.me/{OfficialPhone}?text=2%20Yopougon", reply);
        Assert.Contains($"wa.me/{OfficialPhone}?text=3%20Zone%20Sud", reply);
        Assert.Contains($"wa.me/{OfficialPhone}?text=4%20Abobo", reply);
        Assert.Contains($"wa.me/{OfficialPhone}?text=5%20Plateau", reply);

        // 2. Interdiction formelle d'ordonner la saisie manuelle de texte libre
        Assert.NotNull(reply);
        var lower = reply.ToLowerInvariant();
        Assert.DoesNotContain("écris ton nom", lower);
        Assert.DoesNotContain("tape ton nom", lower);
        Assert.DoesNotContain("envoie ton nom", lower);
        Assert.DoesNotContain("réponds avec ton nom", lower);
        Assert.Contains("0 frappe au clavier", lower);
    }

    [Theory]
    [InlineData("1 Cocody", "Cocody")]
    [InlineData("2 Yopougon", "Yopougon")]
    [InlineData("3 Zone Sud", "Marcory")]
    [InlineData("4 Abobo", "Abobo")]
    [InlineData("5 Plateau", "Plateau")]
    public async Task ChoixCommune_EnregistreDirectement_EtDemandePhotoSansExigerNom(string linkInput, string expectedZone)
    {
        var harness = new WebhookHarness();
        var phone = "+2250700000088";

        await harness.SendAsync(phone, "DISPO");
        await harness.SendAsync(phone, linkInput);

        var lead = await harness.Context.Leads.SingleAsync(l => l.WhatsAppNumber == phone);
        Assert.Equal(expectedZone, lead.Zone);

        var reply = harness.LastMessageTo(phone);
        Assert.NotNull(reply);
        var lower = reply.ToLowerInvariant();

        // Demande directe de la photo sans saisie de nom intermédiaire
        Assert.Contains("dernière étape (0 saisie texte)", lower);
        Assert.Contains("photo ta pièce d'identité", lower);
        Assert.DoesNotContain("écris maintenant ton nom", lower);
    }

    [Fact]
    public async Task PhotoRecueApresChoixCommune_TraiteeSansNomPrealable()
    {
        var harness = new WebhookHarness();
        var phone = "+2250700000077";

        await harness.SendAsync(phone, "DISPO");
        await harness.SendAsync(phone, "1 Cocody");

        // Envoi direct de la photo CNI alors que ContactName n'a pas été tapé au clavier
        await harness.SendImageAsync(phone, "https://media.example/cni-direct.jpg");

        var rider = await harness.Context.Users.SingleAsync(u => u.Role == UserRole.Rider && u.PhoneNumber == phone);
        Assert.Equal("Cocody", rider.Zone);

        var identity = await harness.Context.RiderIdentities.SingleAsync(i => i.UserId == rider.Id);
        Assert.NotNull(identity.ScanFileName);
    }
}
