using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Preuve de livraison : code à 4 chiffres remis au client à l'assignation du livreur,
/// restitué par celui-ci pour clôturer la course.
/// </summary>
public class OrderDeliveryProofTests
{
    private static Order CreateOrder() =>
        new("Client", "123", "456", "Description", 10m);

    [Fact]
    public void EnsureDeliveryCode_GeneratesFourDigits()
    {
        var order = CreateOrder();

        var code = order.EnsureDeliveryCode();

        Assert.Equal(4, code.Length);
        Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.Equal(code, order.DeliveryCode);
    }

    [Fact]
    public void EnsureDeliveryCode_IsIdempotent()
    {
        var order = CreateOrder();

        var first = order.EnsureDeliveryCode();
        var second = order.EnsureDeliveryCode();

        // Une re-notification ne doit JAMAIS invalider le code que le client a sous les yeux.
        Assert.Equal(first, second);
    }

    [Fact]
    public void NewOrder_HasNoDeliveryCode()
    {
        var order = CreateOrder();

        Assert.Null(order.DeliveryCode);
        Assert.Null(order.DeliveryCodeVerifiedAt);
        Assert.Equal(0, order.DeliveryCodeAttempts);
    }

    [Fact]
    public void VerifyDeliveryCode_WithoutGeneratedCode_ReturnsNotSet()
    {
        var order = CreateOrder();

        // Courses antérieures à la fonctionnalité : aucun code n'a été envoyé au client.
        Assert.Equal(DeliveryCodeResult.NotSet, order.VerifyDeliveryCode("1234"));
    }

    [Fact]
    public void VerifyDeliveryCode_WithCorrectCode_ReturnsOkAndStampsVerifiedAt()
    {
        var order = CreateOrder();
        var code = order.EnsureDeliveryCode();

        var result = order.VerifyDeliveryCode(code);

        Assert.Equal(DeliveryCodeResult.Ok, result);
        Assert.NotNull(order.DeliveryCodeVerifiedAt);
        Assert.Equal(0, order.DeliveryCodeAttempts);
    }

    [Fact]
    public void VerifyDeliveryCode_IgnoresSurroundingWhitespace()
    {
        var order = CreateOrder();
        var code = order.EnsureDeliveryCode();

        Assert.Equal(DeliveryCodeResult.Ok, order.VerifyDeliveryCode($"  {code} "));
    }

    [Fact]
    public void VerifyDeliveryCode_WithWrongCode_CountsAttemptAndDoesNotVerify()
    {
        var order = CreateOrder();
        var code = order.EnsureDeliveryCode();
        var wrong = code == "0000" ? "1111" : "0000";

        var result = order.VerifyDeliveryCode(wrong);

        Assert.Equal(DeliveryCodeResult.Mismatch, result);
        Assert.Equal(1, order.DeliveryCodeAttempts);
        Assert.Null(order.DeliveryCodeVerifiedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    public void VerifyDeliveryCode_WithEmptyOrPartialCode_IsMismatch(string? candidate)
    {
        var order = CreateOrder();
        order.EnsureDeliveryCode();

        Assert.Equal(DeliveryCodeResult.Mismatch, order.VerifyDeliveryCode(candidate));
    }

    [Fact]
    public void VerifyDeliveryCode_AfterMaxAttempts_LocksEvenTheCorrectCode()
    {
        var order = CreateOrder();
        var code = order.EnsureDeliveryCode();
        var wrong = code == "0000" ? "1111" : "0000";

        for (var i = 0; i < Order.MaxDeliveryCodeAttempts; i++)
            Assert.Equal(DeliveryCodeResult.Mismatch, order.VerifyDeliveryCode(wrong));

        // Verrouillage anti-force brute : la clôture passe alors par le vendeur ou l'admin.
        Assert.Equal(DeliveryCodeResult.Locked, order.VerifyDeliveryCode(code));
        Assert.Null(order.DeliveryCodeVerifiedAt);
    }

    [Fact]
    public void VerifyDeliveryCode_LockedOrder_DoesNotKeepCountingAttempts()
    {
        var order = CreateOrder();
        order.EnsureDeliveryCode();

        for (var i = 0; i < Order.MaxDeliveryCodeAttempts + 3; i++)
            order.VerifyDeliveryCode("____");

        Assert.Equal(Order.MaxDeliveryCodeAttempts, order.DeliveryCodeAttempts);
    }
}
