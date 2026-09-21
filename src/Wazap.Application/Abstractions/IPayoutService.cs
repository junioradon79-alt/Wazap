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
/// Demande de reversement automatique fractionné (Option A - Split Disbursement) :
/// Déclenche les virements Mobile Money sortants vers le commerçant et le livreur
/// dès la validation du paiement universel client (GeniusPay).
/// </summary>
public sealed record SplitDisbursementRequest(
    Guid OrderId,
    string VendorPhone,
    decimal VendorAmount,
    string? RiderPhone,
    decimal RiderFee,
    string OrderCode);

/// <summary>
/// Résultat du double versement sortant (Marchand + Livreur).
/// </summary>
public sealed record SplitDisbursementResult(
    bool Success,
    string? VendorTransferRef = null,
    string? RiderTransferRef = null,
    bool RequiresManualTransfer = false,
    string? Error = null);

/// <summary>
/// Versement sortant d'une indemnisation « Garantie Colis Sûr » ou reversement direct fractionné.
/// </summary>
/// <remarks>
/// Ce port permet d'exécuter les virements Mobile Money sortants (disbursement API)
/// dès que l'agrégateur (GeniusPay) est connecté et activé sur le compte marchand WAZAP.
/// </remarks>
public interface IPayoutService
{
    Task<PayoutResult> RequestPayoutAsync(PayoutRequest request, CancellationToken ct = default);
    Task<SplitDisbursementResult> DisburseSplitAsync(SplitDisbursementRequest request, CancellationToken ct = default);
}

