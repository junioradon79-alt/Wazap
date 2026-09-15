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

public sealed record AuthResponse(
    Guid UserId,
    string? Token,
    string Username,
    string Role,
    bool MfaRequired = false,
    string? RefreshToken = null);

public sealed record RefreshRequest(string RefreshToken);

public sealed class ForgotPasswordRequest
{
    public string PhoneNumber { get; set; } = default!;
}

public sealed class ResetPasswordRequest
{
    public string PhoneNumber { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string NewPassword { get; set; } = default!;
}

public sealed record TwoFactorVerifyRequest(string Username, string Password, string Code);

/// <summary>
/// Activation de la 2FA : seul le PREMIER CODE est transmis. Le secret est généré et conservé
/// côté serveur par l'étape « setup » — un secret fourni par le client permettrait à un porteur
/// de jeton volé d'activer la 2FA avec son propre secret et de verrouiller le compte.
/// </summary>
public sealed record EnableTwoFactorRequest(string Code);

public sealed record DisableTwoFactorRequest(string Code);

public sealed record UserDto(Guid Id, string Username, string Role);

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = default!;
    public string NewPassword { get; set; } = default!;
}
