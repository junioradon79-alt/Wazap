using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Versement manuel : aucune API de disbursement n'étant disponible chez GeniusPay,
/// l'indemnisation est virée à la main (Orange Money / MTN MoMo) puis confirmée dans
/// <c>/app/claims</c> avec sa référence de transaction.
/// </summary>
/// <remarks>
/// Le dossier reste en <c>Pending</c> jusqu'à cette confirmation : un versement dû ne
/// disparaît donc jamais de la liste tant qu'il n'a pas été effectué et tracé.
/// </remarks>
public sealed class ManualPayoutService : IPayoutService
{
    private readonly ILogger<ManualPayoutService> _logger;

    public ManualPayoutService(ILogger<ManualPayoutService> logger) => _logger = logger;

    public Task<PayoutResult> RequestPayoutAsync(PayoutRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Versement À EFFECTUER À LA MAIN : {Amount} FCFA au vendeur {Vendor} ({Phone}) — sinistre {Claim}, motif : {Reason}.",
            request.AmountFcfa, request.VendorUserId, request.VendorPhoneNumber ?? "numéro inconnu",
            request.ClaimId, request.Reason);

        return Task.FromResult(new PayoutResult(RequiresManualTransfer: true));
    }
}
