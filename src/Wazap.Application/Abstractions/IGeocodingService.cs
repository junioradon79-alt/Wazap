namespace Wazap.Application.Abstractions
{
    public interface IGeocodingService
    {
        Task<(double Latitude, double Longitude)?> GeocodeAsync(string address, CancellationToken cancellationToken = default);
    }
}
