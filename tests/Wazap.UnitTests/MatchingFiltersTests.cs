using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Domain.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Filtres du matching des livreurs, exécutés en base.
/// <para>
/// Le rayon de diffusion chargeait auparavant <b>tous</b> les livreurs géolocalisés et
/// <b>toutes</b> les identités blacklistées / tous les sinistres en cours de la plateforme,
/// puis croisait le tout en mémoire. Les exclusions et la zone sont désormais des prédicats
/// SQL, et une boîte englobante limite les candidats avant le calcul de distance.
/// </para>
/// <para>
/// La boîte doit <b>contenir</b> le cercle : une boîte trop serrée écarterait un livreur
/// réellement dans le rayon (course non proposée), c'est pourquoi elle est testée ici.
/// </para>
/// </summary>
public class MatchingFiltersTests
{
    // ------------------------------------------------------------ Boîte englobante

    [Theory]
    [InlineData(5.3599, -4.0083)]    // Abidjan
    [InlineData(48.8566, 2.3522)]    // Paris
    [InlineData(-33.8688, 151.2093)] // Sydney (hémisphère sud)
    public void BoundingBox_ContientLesQuatrePointsCardinaux_DuRayon(double lat, double lon)
    {
        const double radiusKm = 15;
        var box = DeliveryOfferService.BoundingBox(lat, lon, radiusKm);

        // Points situés exactement à la distance du rayon, au nord, au sud, à l'est et à l'ouest.
        var deltaLat = radiusKm / 111.32;
        var cosinus = Math.Cos(lat * Math.PI / 180.0);
        var deltaLon = radiusKm / (111.32 * cosinus);

        Assert.InRange(lat + deltaLat, box.MinLat, box.MaxLat);
        Assert.InRange(lat - deltaLat, box.MinLat, box.MaxLat);
        Assert.InRange(lon + deltaLon, box.MinLon, box.MaxLon);
        Assert.InRange(lon - deltaLon, box.MinLon, box.MaxLon);
    }

    [Fact]
    public void BoundingBox_AuxHautesLatitudes_NeFiltrePas()
    {
        // Au-delà de 89°, un degré de longitude tend vers zéro : la boîte n'est plus fiable,
        // on préfère ne pas filtrer (le rayon Haversine reste l'arbitre).
        var box = DeliveryOfferService.BoundingBox(89.5, 10, 15);

        Assert.Equal(-90, box.MinLat);
        Assert.Equal(90, box.MaxLat);
        Assert.Equal(-180, box.MinLon);
        Assert.Equal(180, box.MaxLon);
    }

    [Fact]
    public void BoundingBox_ProcheDeLAntimeridien_NeFiltrePasEnLongitude()
    {
        // Franchir ±180° rendrait la comparaison « min <= lon <= max » fausse pour les points
        // situés de l'autre côté : on élargit la longitude.
        var box = DeliveryOfferService.BoundingBox(0, 179.9, 15);

        Assert.Equal(-180, box.MinLon);
        Assert.Equal(180, box.MaxLon);
    }

    // ------------------------------------------------- Matching complet sur base relationnelle

    [Fact]
    public async Task Broadcast_N_exclutQueLesLivreursIneligibles()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        // Vendeur géolocalisé (Abidjan, Cocody) avec des crédits.
        var vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000600");
        vendor.SetZone("Cocody");
        vendor.AddCredits(5);
        vendor.UpdateLocation(5.3599, -4.0083);
        context.Users.Add(vendor);

        const double proche = 5.3600;   // ~10 m du vendeur
        const double tresLoin = 7.5000; // plusieurs centaines de km

        var eligible = NewRider("livreur-eligible", "+2250700000601", proche, -4.0084);
        var blacklisted = NewRider("livreur-blackliste", "+2250700000602", proche, -4.0084);
        var sousEnquete = NewRider("livreur-enquete", "+2250700000603", proche, -4.0084);
        var horsRayon = NewRider("livreur-loin", "+2250700000604", tresLoin, -4.0083);
        var indisponible = NewRider("livreur-indispo", "+2250700000605", proche, -4.0084);
        indisponible.SetAvailability(false);
        var sansPartage = NewRider("livreur-sans-partage", "+2250700000606", proche, -4.0084);
        sansPartage.SetLocationSharing(false);

        context.Users.AddRange(eligible, blacklisted, sousEnquete, horsRayon, indisponible, sansPartage);

        var order = new Order("Client", "+2250700000607", vendor.PhoneNumber!, "1 colis", 3000m);
        order.LinkVendor(vendor.Id);
        order.ConfirmByVendor();
        context.Orders.Add(order);

        // Dossier blacklisté et sinistre en cours d'enquête : les deux doivent être écartés.
        var identity = new RiderIdentity(blacklisted.Id);
        identity.Blacklist("vol constaté", reviewerId: null);
        context.RiderIdentities.Add(identity);

        context.DeliveryClaims.Add(new DeliveryClaim(order.Id, vendor.Id, sousEnquete.Id, "colis disparu"));
        context.SaveChanges();

        var sender = new RecordingWhatsAppSender();
        var offers = new DeliveryOfferService(context, sender, new WhatsAppOptions(),
            new GeoOptions { MaxDistanceKm = 15 }, new GroupingOptions(), new ClientOptions(),
            new WhatsAppOrchestrationService(sender, new WhatsAppOptions(),
                NullLogger<WhatsAppOrchestrationService>.Instance),
            new RiderSecurityOptions(), new RiderReputationOptions(), new ClientPaymentOptions(),
            new RiderPriorityOptions(), NullLogger<DeliveryOfferService>.Instance);

        var result = await offers.BroadcastAsync(order.Id);

        Assert.Equal(1, result.OffersCreated);

        var offered = context.DeliveryOffers.Select(o => o.RiderUserId).ToList();
        Assert.Contains(eligible.Id, offered);
        Assert.DoesNotContain(blacklisted.Id, offered);
        Assert.DoesNotContain(sousEnquete.Id, offered);
        Assert.DoesNotContain(horsRayon.Id, offered);
        Assert.DoesNotContain(indisponible.Id, offered);
        Assert.DoesNotContain(sansPartage.Id, offered);
    }

    [Fact]
    public void Distance_AuDelaDuRayon_EstBienSuperieureAuSeuil()
    {
        // Garde-fou de cohérence : la boîte englobante ne doit jamais écarter un point que le
        // calcul Haversine considérerait comme dans le rayon.
        var distance = GeoDistance.DistanceKm(5.3599, -4.0083, 7.5000, -4.0083);
        Assert.True(distance > 15, $"distance calculée : {distance:0.0} km");
    }

    private static User NewRider(string username, string phone, double latitude, double longitude)
    {
        var rider = new User(username, "hash", UserRole.Rider, phone);
        rider.SetAvailability(true);
        rider.UpdateLocation(latitude, longitude);
        return rider;
    }
}
