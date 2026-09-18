using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Services;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class WhatsAppMessageLogServiceTests
{
    [Fact]
    public async Task LogOutboundAsync_Success_PersistsLogWithCalculatedCost()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestInfra.NewContext(dbName);
        var scopeFactory = new ServiceScopeFactoryStub(context);
        var service = new WhatsAppMessageLogService(scopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        var orderId = Guid.NewGuid();
        await service.LogOutboundAsync(
            toPhoneNumber: "+2250701020304",
            templateName: "order_confirm",
            messageText: null,
            provider: "Meta",
            success: true,
            providerMessageId: "wamid.TEST12345",
            orderId: orderId);

        var log = await context.WhatsAppMessageLogs.SingleOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("+2250701020304", log.RecipientPhone);
        Assert.Equal("order_confirm", log.TemplateName);
        Assert.Equal("utility", log.Category);
        Assert.Equal("Outbound", log.Direction);
        Assert.Equal("Sent", log.Status);
        Assert.Equal("Meta", log.Provider);
        Assert.Equal("wamid.TEST12345", log.ProviderMessageId);
        Assert.Equal(2.27m, log.EstimatedCostFcfa);
        Assert.Equal(orderId, log.OrderId);
    }

    [Fact]
    public async Task LogOutboundAsync_Failure_PersistsLogWithZeroCostAndError()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestInfra.NewContext(dbName);
        var scopeFactory = new ServiceScopeFactoryStub(context);
        var service = new WhatsAppMessageLogService(scopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        await service.LogOutboundAsync(
            toPhoneNumber: "+2250501020304",
            templateName: "prospect_v1",
            messageText: null,
            provider: "Meta",
            success: false,
            errorCode: 131026,
            errorMessage: "Receiver phone number is not a valid WhatsApp user");

        var log = await context.WhatsAppMessageLogs.SingleOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("marketing", log.Category);
        Assert.Equal("Failed", log.Status);
        Assert.Equal(131026, log.ErrorCode);
        Assert.Contains("valid WhatsApp user", log.ErrorMessage);
        Assert.Equal(0m, log.EstimatedCostFcfa);
    }

    [Fact]
    public async Task LogInboundAsync_PersistsInboundMessageWithZeroCost()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestInfra.NewContext(dbName);
        var scopeFactory = new ServiceScopeFactoryStub(context);
        var service = new WhatsAppMessageLogService(scopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        await service.LogInboundAsync(
            fromPhoneNumber: "+2250102030405",
            messageText: "LIVRAISON 2 colis à Cocody tél 0708091011",
            messageType: "Text",
            provider: "Meta",
            providerMessageId: "wamid.INBOUND999");

        var log = await context.WhatsAppMessageLogs.SingleOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("+2250102030405", log.RecipientPhone);
        Assert.Equal("Inbound", log.Direction);
        Assert.Equal("Received", log.Status);
        Assert.Equal("service", log.Category);
        Assert.Equal(0m, log.EstimatedCostFcfa);
    }

    [Fact]
    public async Task GetCostSummaryAsync_CalculatesFinancialAggregationAccurately()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestInfra.NewContext(dbName);
        var scopeFactory = new ServiceScopeFactoryStub(context);
        var service = new WhatsAppMessageLogService(scopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();

        // 1 marketing outbound sent: 12.79 FCFA
        await service.LogOutboundAsync("+2250700000001", "prospect_v1", null, "Meta", true);
        // 1 utility outbound sent on order1: 2.27 FCFA
        await service.LogOutboundAsync("+2250700000002", "order_confirm", null, "Meta", true, orderId: order1);
        // 1 utility outbound sent on order1: 2.27 FCFA
        await service.LogOutboundAsync("+2250700000002", "order_buyer_tracking_v1", null, "Meta", true, orderId: order1);
        // 1 utility outbound sent on order2: 2.27 FCFA
        await service.LogOutboundAsync("+2250700000003", "order_confirm", null, "Meta", true, orderId: order2);
        // 1 outbound failed on order2: 0 FCFA
        await service.LogOutboundAsync("+2250700000003", "order_confirm", null, "Meta", false, orderId: order2);
        // 1 inbound: 0 FCFA
        await service.LogInboundAsync("+2250700000004", "Bonjour", "Text", "Meta");

        var summary = await service.GetCostSummaryAsync();

        Assert.Equal(6, summary.TotalMessages);
        Assert.Equal(5, summary.OutboundCount);
        Assert.Equal(1, summary.InboundCount);
        Assert.Equal(1, summary.FailedCount);

        // Expected total cost: 12.79 + 2.27 + 2.27 + 2.27 + 0 + 0 = 19.60 FCFA
        Assert.Equal(19.60m, summary.TotalEstimatedCostFcfa);
        Assert.Equal(1, summary.MessagesByCategory["marketing"]);
        Assert.Equal(4, summary.MessagesByCategory["utility"]);
        Assert.Equal(1, summary.MessagesByCategory["service"]);

        // 2 distinct orders (order1, order2). Average cost per order = 19.60 / 2 = 9.80 FCFA
        Assert.Equal(9.80m, summary.AverageCostPerOrder);
    }

    [Fact]
    public async Task GetRecentLogsAsync_FiltersByOrderIdAndRecipientPhone()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestInfra.NewContext(dbName);
        var scopeFactory = new ServiceScopeFactoryStub(context);
        var service = new WhatsAppMessageLogService(scopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        var targetOrder = Guid.NewGuid();
        await service.LogOutboundAsync("+2250701020304", "order_confirm", null, "Meta", true, orderId: targetOrder);
        await service.LogOutboundAsync("+2250505050505", "prospect_v1", null, "Meta", true);

        var forOrder = await service.GetRecentLogsAsync(orderId: targetOrder);
        Assert.Single(forOrder);
        Assert.Equal(targetOrder, forOrder[0].OrderId);

        var forPhone = await service.GetRecentLogsAsync(recipientPhone: "050505");
        Assert.Single(forPhone);
        Assert.Equal("+2250505050505", forPhone[0].RecipientPhone);
    }

    [Fact]
    public async Task LogOutboundAsync_WhenDatabaseFails_DoesNotThrowException()
    {
        // Une factory qui retourne null ou throw ne doit pas faire planter l'appelant
        var faultyScopeFactory = new FaultyScopeFactory();
        var service = new WhatsAppMessageLogService(faultyScopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        // Doit s'exécuter sans exception
        await service.LogOutboundAsync("+2250701020304", "order_confirm", null, "Meta", true);
    }

    private sealed class FaultyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Simulation panne DB");
    }
}
