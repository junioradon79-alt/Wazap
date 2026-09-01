namespace Wazap.Application.Dtos
{
    public sealed record NearestRiderDto(Guid RiderUserId, double DistanceKm);

    public sealed record BroadcastResultDto(int OffersCreated, int BatchNumber);
}
