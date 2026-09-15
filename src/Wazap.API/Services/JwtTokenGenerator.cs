using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;

namespace Wazap.API.Services;

public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    /// <summary>
    /// Claim portant l'empreinte de sécurité du compte. Vérifiée à CHAQUE requête : un
    /// changement de mot de passe ou de 2FA la régénère, ce qui invalide immédiatement tous les
    /// jetons déjà émis (un jeton volé ne survit plus à une réinitialisation).
    /// </summary>
    public const string SecurityStampClaim = "stamp";

    /// <summary>Durée de vie par défaut du jeton d'accès (courte : le renouvellement est pris en charge par le client).</summary>
    private const int DefaultAccessTokenMinutes = 30;

    private readonly IConfiguration _config;

    public JwtTokenGenerator(IConfiguration config)
    {
        _config = config;
    }

    public string Generate(User user)
    {
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key manquante.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var minutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var configured) && configured > 0
            ? configured
            : DefaultAccessTokenMinutes;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(SecurityStampClaim, user.SecurityStamp)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
