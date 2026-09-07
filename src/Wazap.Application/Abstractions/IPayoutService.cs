namespace Wazap.Application.Abstractions;

/// <summary>Demande de versement d'une indemnisation à un vendeur (Mobile Money).</summary>
public sealed record PayoutRequest(
    Guid ClaimId,
    Guid VendorUserId,
    string? VendorPhoneNumber,
    decimal AmountFcfa,
    string Reason);

/// <summary>
/// Résultat d'une demande de versement. <paramref name="Reference"/> est renseignée quand
/// le versement est immédiat (agrégateur) ; <paramref name="RequiresManualTransfer"/> vaut
/// true quand le transfert doit être fait à la main puis confirmé dans /app/claims.
/// </summary>
public sealed record PayoutResult(
    bool RequiresManualTransfer,
    string? Reference = null,
    string? Error = null);

/// <summary>
/// Versement sortant d'une indemnisation « Garantie Colis Sûr ».
/// </summary>
/// <remarks>
/// Ce port existe pour que le jour où un endpoint de versement (disbursement) sera
/// disponible chez GeniusPay, il suffise d'en fournir une implémentation : rien du
/// domaine ni du service de sinistres n'aura à changer. À ce jour, l'intégration
/// GeniusPay ne couvre que l'ENCAISSEMENT (<c>/payments</c>) — d'où l'implémentation
/// manuelle par défaut.
/// </remarks>
public interface IPayoutService
{
    Task<PayoutResult> RequestPayoutAsync(PayoutRequest request, CancellationToken ct = default);
}
