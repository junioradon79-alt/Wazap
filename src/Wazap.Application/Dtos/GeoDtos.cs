namespace Wazap.Application.Dtos
{
    public sealed record NearestRiderDto(Guid RiderUserId, double DistanceKm);

    public sealed record BroadcastResultDto(int OffersCreated, int BatchNumber);

    public sealed record DeliveryOfferDto(
        Guid Id,
        Guid RiderUserId,
        Domain.Enums.DeliveryOfferStatus Status,
        int BatchNumber,
        DateTime SentAt,
        DateTime? RespondedAt);
}
