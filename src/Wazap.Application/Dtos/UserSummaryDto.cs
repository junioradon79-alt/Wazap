using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos
{
    public sealed class UserSummaryDto
    {
        public Guid Id { get; init; }
        public string Username { get; init; } = default!;
        public string? PhoneNumber { get; init; }
        public UserRole Role { get; init; }
        public bool IsAvailable { get; init; }
        public bool LocationSharingEnabled { get; init; }
        public string? Zone { get; init; }
        public int Credits { get; init; }
        public string? ReferralCode { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public DateTime? LocationUpdatedAt { get; init; }
    }
}
