namespace Wazap.Application.Dtos
{
    /// <summary>
    /// Candidat du matching. <see cref="PriorityUntilUtc"/> = échéance du « pack prioritaire »
    /// du livreur (null = aucune priorité) : ce critère passe AVANT la distance, sans jamais
    /// élargir le rayon ni la disponibilité (voir <c>RiderPriorityOptions</c>).
    /// </summary>
    public sealed record NearestRiderDto(Guid RiderUserId, double DistanceKm, DateTime? PriorityUntilUtc = null);

    public sealed record BroadcastResultDto(int OffersCreated, int BatchNumber);

    public sealed record DeliveryOfferDto(
        Guid Id,
        Guid RiderUserId,
        Domain.Enums.DeliveryOfferStatus Status,
        int BatchNumber,
        DateTime SentAt,
        DateTime? RespondedAt);
}
