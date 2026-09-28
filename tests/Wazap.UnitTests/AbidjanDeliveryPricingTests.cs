using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Services;
using Xunit;

namespace Wazap.UnitTests;

public class AbidjanDeliveryPricingTests
{
    [Fact]
    public void MinimumFee_IsStrictly1000FCFA()
    {
        Assert.Equal(1000m, AbidjanDeliveryPricing.MinimumFee);
        Assert.Equal(1000m, Order.MinimumDeliveryFee);
    }

    [Theory]
    [InlineData("Cocody", "Cocody", 1000)]
    [InlineData("Yopougon", "Yopougon", 1000)]
    [InlineData("Marcory", "Marcory", 1000)]
    [InlineData("Cocody Angré", "Riviera", 1000)]
    [InlineData(null, null, 1000)]
    [InlineData("", "Cocody", 1000)]
    public void CalculateFee_IntraCommune_Returns1000FCFA(string? origin, string? dest, decimal expected)
    {
        var fee = AbidjanDeliveryPricing.CalculateFee(origin, dest);
        Assert.Equal(expected, fee);
    }

    [Theory]
    [InlineData("Plateau", "Treichville", 1500)]
    [InlineData("Cocody", "Plateau", 1500)]
    [InlineData("Cocody", "Adjamé", 1500)]
    [InlineData("Marcory", "Koumassi", 1500)]
    [InlineData("Treichville", "Marcory", 1500)]
    [InlineData("Adjamé", "Abobo", 1500)]
    [InlineData("Yopougon", "Attécoubé", 1500)]
    public void CalculateFee_AdjacentCommunes_Returns1500FCFA(string origin, string dest, decimal expected)
    {
        var fee = AbidjanDeliveryPricing.CalculateFee(origin, dest);
        Assert.Equal(expected, fee);
    }

    [Theory]
    [InlineData("Yopougon", "Cocody", 2000)]
    [InlineData("Yopougon", "Marcory", 2000)]
    [InlineData("Abobo", "Marcory", 2000)]
    [InlineData("Cocody", "Port-Bouët", 2000)]
    public void CalculateFee_CrossLagoonOrLongDistance_Returns2000FCFA(string origin, string dest, decimal expected)
    {
        var fee = AbidjanDeliveryPricing.CalculateFee(origin, dest);
        Assert.Equal(expected, fee);
    }

    [Theory]
    [InlineData("Cocody", "Songon", 2500)]
    [InlineData("Grand-Bassam", "Marcory", 2500)]
    [InlineData("Yopougon", "Anyama", 2500)]
    public void CalculateFee_Periphery_Returns2500FCFA(string origin, string dest, decimal expected)
    {
        var fee = AbidjanDeliveryPricing.CalculateFee(origin, dest);
        Assert.Equal(expected, fee);
    }

    [Theory]
    [InlineData(2.5, 1000)]
    [InlineData(4.0, 1000)]
    [InlineData(5.5, 1500)]
    [InlineData(8.0, 1500)]
    [InlineData(12.0, 2000)]
    [InlineData(16.0, 2000)]
    [InlineData(22.0, 2500)]
    public void CalculateFeeByDistance_ReturnsCorrectTier(double distanceKm, decimal expected)
    {
        var fee = AbidjanDeliveryPricing.CalculateFeeByDistance(distanceKm);
        Assert.Equal(expected, fee);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(450, 1000)]
    [InlineData(999, 1000)]
    [InlineData(1000, 1000)]
    [InlineData(1500, 1500)]
    [InlineData(2000, 2000)]
    public void EnforceFloor_GuaranteesAtLeast1000FCFA(decimal input, decimal expected)
    {
        var fee = AbidjanDeliveryPricing.EnforceFloor(input);
        Assert.Equal(expected, fee);
    }

    [Fact]
    public void Order_Constructors_Enforce1000FCFAFloor()
    {
        var order1 = new Order("Client", "+2250700000000", "+2250711111111", "Colis", 5000m, deliveryFee: 450m);
        Assert.Equal(1000m, order1.DeliveryFee);

        var order2 = new Order("Client", "+2250700000000", "+2250711111111", "Colis", 5000m, deliveryFee: -100m);
        Assert.Equal(1000m, order2.DeliveryFee);

        var order3 = new Order("Client", "+2250700000000", "+2250711111111", "Colis", 5000m, deliveryFee: 1500m);
        Assert.Equal(1500m, order3.DeliveryFee);
    }

    [Fact]
    public void Order_SetDeliveryFee_RejectsBelow1000FCFA()
    {
        var order = new Order("Client", "+2250700000000", "+2250711111111", "Colis", 5000m);
        Assert.Throws<ArgumentOutOfRangeException>(() => order.SetDeliveryFee(500m));
        Assert.Throws<ArgumentOutOfRangeException>(() => order.SetDeliveryFee(0m));

        order.SetDeliveryFee(1000m);
        Assert.Equal(1000m, order.DeliveryFee);

        order.SetDeliveryFee(2000m);
        Assert.Equal(2000m, order.DeliveryFee);
    }

    [Fact]
    public void VendorCommandParser_DetectCommune_DetectsCorrectly()
    {
        Assert.Equal("Cocody", VendorCommandParser.DetectCommune("Livraison à Angré 8ème tranche"));
        Assert.Equal("Yopougon", VendorCommandParser.DetectCommune("Colis pour Maroc Siporex"));
        Assert.Equal("Marcory", VendorCommandParser.DetectCommune("Envoi vers Zone 4"));
        Assert.Null(VendorCommandParser.DetectCommune("Rue sans nom"));
    }

    [Fact]
    public void VendorCommandParser_ParseFreeTextOrder_UsesPricingGrid()
    {
        var orderText = "Nom: Fatou\nTel: 0708091011\nArticle: Robe\nPrix: 15000 F\nLieu: Marcory Zone 4";
        var parsed = VendorCommandParser.ParseFreeTextOrder(orderText, vendorZone: "Cocody");

        Assert.Equal("Fatou", parsed.ClientName);
        Assert.Equal("+2250708091011", parsed.ClientPhone);
        Assert.Equal(15000m, parsed.Amount);
        Assert.Equal("Marcory", parsed.Zone);
        Assert.Equal(2000m, parsed.DeliveryFee); // Cocody <-> Marcory : 2000 FCFA
    }
}
