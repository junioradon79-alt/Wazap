using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// API publique v1 — endpoints d'écriture (POST /api/v1/orders).
/// </summary>
public class PublicApiV1Tests
{
    [Fact]
    public async Task CreateOrder_KnownVendor_CreatesAndReturnsOrderId()
    {
        var env = new Env();
        await env.SeedVendorAsync("+2250700000005", "Vendeur Test", credits: 15);

        var result = await env.Service.CreateOrderAsync(new PublicCreateOrderRequest(
            VendorWhatsAppNumber: "+2250700000005",
            Description: "2 poulets braisés à Marcory",
            Amount: 5000m,
            ClientName: "Client A",
            ClientWhatsAppNumber: "+2250700000006"));

        Assert.True(result.Success);
        Assert.NotNull(result.OrderId);
        Assert.NotNull(result.Message);
        Assert.Contains("créée", result.Message, StringComparison.OrdinalIgnoreCase);

        var order = await env.Context.Orders.FindAsync(result.OrderId.Value);
        Assert.NotNull(order);
        Assert.Equal(5000m, order!.Amount);
        Assert.Equal("Client A", order.ClientName);
        Assert.Equal("+2250700000006", order.ClientWhatsAppNumber);
    }

    [Fact]
    public async Task CreateOrder_UnknownVendor_ReturnsError()
    {
        var env = new Env();

        var result = await env.Service.CreateOrderAsync(new PublicCreateOrderRequest(
            VendorWhatsAppNumber: "+2250799999999",
            Description: "Colis test",
            Amount: 1000m,
            ClientName: "Client",
            ClientWhatsAppNumber: "+2250700000007"));

        Assert.False(result.Success);
        Assert.Null(result.OrderId);
        Assert.Contains("introuvable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOrder_VendorWithoutCredits_ReturnsError()
    {
        var env = new Env();
        await env.SeedVendorAsync("+2250700000010", "Vendeur Pauvre", credits: 0);

        var result = await env.Service.CreateOrderAsync(new PublicCreateOrderRequest(
            VendorWhatsAppNumber: "+2250700000010",
            Description: "Colis sans crédit",
            Amount: 2000m,
            ClientName: "Client",
            ClientWhatsAppNumber: "+2250700000011"));

        Assert.False(result.Success);
        Assert.Contains("crédits", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Env
    {
        public ApplicationDbContext Context { get; }
        public PublicApiService Service { get; }

        public Env()
        {
            Context = TestInfra.NewContext("publicapi-" + Guid.NewGuid().ToString("N"));

            var packs = new List<PackConfiguration>();
            var scopeFactory = new ServiceScopeFactoryStub(Context);
            Service = new PublicApiService(scopeFactory, packs.AsReadOnly());
        }

        public async Task SeedVendorAsync(string phone, string username, int credits)
        {
            var vendor = new User(username, "hash", UserRole.Vendor, phone);
            vendor.SetZone("Marcory");
            if (credits > 0)
                vendor.AddCredits(credits);
            Context.Users.Add(vendor);
            await Context.SaveChangesAsync();
        }
    }
}