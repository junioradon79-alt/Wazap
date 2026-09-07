using System.Globalization;
using Wazap.API.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Le SQL de réclamation de l'outbox est passé à <c>SqlQueryRaw</c>, qui le traite comme un
/// gabarit de composition (<c>String.Format</c>). Un placeholder qui n'est pas un indice
/// numérique lève une <c>FormatException</c> À L'EXÉCUTION seulement : le worker attrape,
/// journalise et repart en boucle — l'outbox cesse silencieusement de traiter les messages.
/// </summary>
public class OutboxClaimSqlTests
{
    [Fact]
    public void ClaimSql_IsAValidCompositeFormatString()
    {
        // Régression : « LIMIT {_batchSize} » dans un littéral NON interpolé (b2a298d)
        // → « Input string was not in a correct format. Expected an ASCII digit. »
        var formatted = string.Format(CultureInfo.InvariantCulture, OutboxBackgroundWorker.ClaimSql, 25);

        Assert.Contains("LIMIT 25", formatted);
    }

    [Fact]
    public void ClaimSql_HasNoUnsubstitutedPlaceholder()
    {
        var formatted = string.Format(CultureInfo.InvariantCulture, OutboxBackgroundWorker.ClaimSql, 10);

        // Toute accolade restante trahit un placeholder oublié (« {_batchSize} », « {limit} »…).
        Assert.DoesNotContain("{", formatted);
        Assert.DoesNotContain("}", formatted);
    }

    [Fact]
    public void ClaimSql_KeepsMultiInstanceSafety()
    {
        // Sûreté multi-instances : sans SKIP LOCKED, deux instances traiteraient le même message.
        Assert.Contains("FOR UPDATE SKIP LOCKED", OutboxBackgroundWorker.ClaimSql);
    }
}
