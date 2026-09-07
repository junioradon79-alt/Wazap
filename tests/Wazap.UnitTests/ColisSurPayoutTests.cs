using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// « Garantie Colis Sûr » étape 3 : barème d'indemnisation en FCFA, caution du livreur
/// et cycle de vie du versement sortant.
/// </summary>
public class ColisSurPayoutTests
{
    private static ColisSurService CreateService(TestDbContext db, ColisSurOptions options)
        => new(db.Context, new RecordingWhatsAppSender(), new ConfigStub(), options,
            new ManualPayoutService(NullLogger<ManualPayoutService>.Instance),
            NullLogger<ColisSurService>.Instance);

    // ------------------------------------------------------------------------- Barème

    [Theory]
    // Sans franchise ni plafond atteint : la valeur de la commande.
    [InlineData(10_000, 0, 50_000, 10_000)]
    // Franchise déduite.
    [InlineData(10_000, 2_000, 50_000, 8_000)]
    // Plafond appliqué : l'exposition de WAZAP est bornée.
    [InlineData(500_000, 0, 50_000, 50_000)]
    // Franchise supérieure à la valeur : rien à indemniser, jamais de négatif.
    [InlineData(1_000, 5_000, 50_000, 0)]
    // Plafond à 0 = pas de plafond.
    [InlineData(500_000, 0, 0, 500_000)]
    public void Compute_AppliesDeductibleThenCap(decimal orderAmount, decimal deductible, decimal cap, decimal expected)
    {
        var service = CreateService(new TestDbContext(), new ColisSurOptions
        {
            DeductibleFcfa = deductible,
            MaxCompensationFcfa = cap
        });

        Assert.Equal(expected, service.ComputeCompensationFcfa(orderAmount));
    }

    // ----------------------------------------------------------------- Caution livreur

    [Fact]
    public void Deposit_IsDebitedUpToAvailableBalance()
    {
        var identity = new RiderIdentity(Guid.NewGuid());
        identity.SetDeposit(20_000m);

        var debited = identity.DebitDeposit(8_000m);

        Assert.Equal(8_000m, debited);
        Assert.Equal(12_000m, identity.DepositFcfa);
    }

    [Fact]
    public void Deposit_NeverGoesNegative()
    {
        var identity = new RiderIdentity(Guid.NewGuid());
        identity.SetDeposit(5_000m);

        // Indemnisation supérieure à la caution : le reste est à la charge de WAZAP.
        var debited = identity.DebitDeposit(30_000m);

        Assert.Equal(5_000m, debited);
        Assert.Equal(0m, identity.DepositFcfa);
    }

    [Fact]
    public void Deposit_WithoutBalance_DebitsNothing()
    {
        var identity = new RiderIdentity(Guid.NewGuid());

        Assert.Equal(0m, identity.DebitDeposit(10_000m));
    }

    [Fact]
    public void Deposit_RejectsNegativeAmount()
    {
        var identity = new RiderIdentity(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() => identity.SetDeposit(-1m));
    }

    // ------------------------------------------------------------- Cycle du versement

    private static DeliveryClaim NewClaim()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SINISTRE A1B2C3D4");

    [Fact]
    public void Approve_WithAmount_CreatesAPendingPayout()
    {
        var claim = NewClaim();

        claim.Approve(2, "colis volé", Guid.NewGuid(), compensationAmountFcfa: 12_000m, riderDepositDebitedFcfa: 5_000m);

        Assert.Equal(ClaimPayoutStatus.Pending, claim.PayoutStatus);
        Assert.Equal(12_000m, claim.CompensationAmountFcfa);
        Assert.Equal(5_000m, claim.RiderDepositDebitedFcfa);
    }

    [Fact]
    public void Approve_WithoutAmount_CreatesNoPayout()
    {
        var claim = NewClaim();

        // Indemnisation en crédits seuls : aucune ligne de versement à suivre.
        claim.Approve(2, "geste commercial", Guid.NewGuid());

        Assert.Equal(ClaimPayoutStatus.None, claim.PayoutStatus);
    }

    [Fact]
    public void MarkPaid_RecordsReferenceAndDate()
    {
        var claim = NewClaim();
        claim.Approve(0, null, Guid.NewGuid(), compensationAmountFcfa: 12_000m);

        claim.MarkPayoutPaid("OM-20260907-001");

        Assert.Equal(ClaimPayoutStatus.Paid, claim.PayoutStatus);
        Assert.Equal("OM-20260907-001", claim.PayoutReference);
        Assert.NotNull(claim.PaidAt);
    }

    [Fact]
    public void MarkPaid_RequiresAReference()
    {
        var claim = NewClaim();
        claim.Approve(0, null, Guid.NewGuid(), compensationAmountFcfa: 12_000m);

        // Sans référence de virement, aucune traçabilité comptable.
        Assert.Throws<ArgumentException>(() => claim.MarkPayoutPaid("  "));
        Assert.Equal(ClaimPayoutStatus.Pending, claim.PayoutStatus);
    }

    [Fact]
    public void MarkPaid_WithoutPendingPayout_Throws()
    {
        var claim = NewClaim();
        claim.Approve(2, null, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => claim.MarkPayoutPaid("OM-1"));
    }

    [Fact]
    public void MarkPaid_Twice_Throws()
    {
        var claim = NewClaim();
        claim.Approve(0, null, Guid.NewGuid(), compensationAmountFcfa: 12_000m);
        claim.MarkPayoutPaid("OM-1");

        // Protège d'un double virement enregistré par inadvertance.
        Assert.Throws<InvalidOperationException>(() => claim.MarkPayoutPaid("OM-2"));
    }

    [Fact]
    public void FailedPayout_CanBeRetriedAndPaid()
    {
        var claim = NewClaim();
        claim.Approve(0, null, Guid.NewGuid(), compensationAmountFcfa: 12_000m);

        claim.MarkPayoutFailed("numéro Mobile Money invalide");
        Assert.Equal(ClaimPayoutStatus.Failed, claim.PayoutStatus);

        claim.MarkPayoutPaid("OM-2");
        Assert.Equal(ClaimPayoutStatus.Paid, claim.PayoutStatus);
        Assert.Null(claim.PayoutError);
    }

    [Fact]
    public void PaidPayout_CannotBeMarkedFailed()
    {
        var claim = NewClaim();
        claim.Approve(0, null, Guid.NewGuid(), compensationAmountFcfa: 12_000m);
        claim.MarkPayoutPaid("OM-1");

        Assert.Throws<InvalidOperationException>(() => claim.MarkPayoutFailed("erreur"));
    }

    [Fact]
    public void Reject_ClearsAnyCompensation()
    {
        var claim = NewClaim();

        claim.Reject("sinistre non confirmé", Guid.NewGuid());

        Assert.Null(claim.CompensationAmountFcfa);
        Assert.Equal(ClaimPayoutStatus.None, claim.PayoutStatus);
    }
}
