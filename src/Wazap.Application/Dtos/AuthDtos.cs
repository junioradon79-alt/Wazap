using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos;

public class RegisterRequest
{
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public UserRole Role { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ReferralCode { get; set; }
}

public class LoginRequest
{
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
}

public sealed record AuthResponse(Guid UserId, string Token, string Username, string Role);

public sealed record UserDto(Guid Id, string Username, string Role);

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = default!;
    public string NewPassword { get; set; } = default!;
}
