using Wazap.Application.Services;
using Xunit;

namespace Wazap.UnitTests;

public class WhatsAppCostCalculatorTests
{
    [Theory]
    [InlineData("prospect_v1", "marketing")]
    [InlineData("prospect_welcome", "marketing")]
    [InlineData("rider_recruit_v1", "marketing")]
    [InlineData("rider_company_v1", "marketing")]
    [InlineData("rider_offer_v2", "marketing")]
    [InlineData("delivery_code", "authentication")]
    [InlineData("auth_code", "authentication")]
    [InlineData("order_confirm", "utility")]
    [InlineData("order_buyer_tracking_v1", "utility")]
    [InlineData("order_delivered", "utility")]
    [InlineData(null, "service")]
    [InlineData("", "service")]
    [InlineData("   ", "service")]
    public void DetermineCategory_ReturnsExpectedCategory(string? templateName, string expectedCategory)
    {
        var category = WhatsAppCostCalculator.DetermineCategory(templateName);
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void CalculateEstimatedCost_Marketing_Returns12_79Fcfa()
    {
        var cost = WhatsAppCostCalculator.CalculateEstimatedCost("marketing", "Outbound", "Sent");
        Assert.Equal(12.79m, cost);
    }

    [Theory]
    [InlineData("utility")]
    [InlineData("authentication")]
    public void CalculateEstimatedCost_UtilityAndAuth_Returns2_27Fcfa(string category)
    {
        var cost = WhatsAppCostCalculator.CalculateEstimatedCost(category, "Outbound", "Sent");
        Assert.Equal(2.27m, cost);
    }

    [Fact]
    public void CalculateEstimatedCost_ServiceBeforeOct2026_Returns0Fcfa()
    {
        var date = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var cost = WhatsAppCostCalculator.CalculateEstimatedCost("service", "Outbound", "Sent", date);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void CalculateEstimatedCost_ServiceAfterOct2026_Returns2_27Fcfa()
    {
        var date = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var cost = WhatsAppCostCalculator.CalculateEstimatedCost("service", "Outbound", "Sent", date);
        Assert.Equal(2.27m, cost);
    }

    [Theory]
    [InlineData("Inbound", "marketing", "Received")]
    [InlineData("Inbound", "service", "Received")]
    [InlineData("Inbound", "utility", "Received")]
    public void CalculateEstimatedCost_Inbound_Returns0Fcfa(string direction, string category, string status)
    {
        var cost = WhatsAppCostCalculator.CalculateEstimatedCost(category, direction, status);
        Assert.Equal(0m, cost);
    }

    [Theory]
    [InlineData("Outbound", "marketing")]
    [InlineData("Outbound", "utility")]
    [InlineData("Outbound", "authentication")]
    public void CalculateEstimatedCost_Failed_Returns0Fcfa(string direction, string category)
    {
        var cost = WhatsAppCostCalculator.CalculateEstimatedCost(category, direction, "Failed");
        Assert.Equal(0m, cost);
    }
}
