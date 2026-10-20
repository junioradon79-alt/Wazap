using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wazap.API.Services;
using Wazap.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/auth/ui")]
public class AccountController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ApplicationDbContext _context;

    public AccountController(AuthService authService, ApplicationDbContext context)
    {
        _authService = authService;
        _context = context;
    }

    /// <summary>
    /// Connexion au dashboard administrateur (délivre un cookie de session).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        AuthResponse auth;
        try
        {
            auth = await _authService.LoginAsync(request);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }

        return await SignInAdminAsync(auth);
    }

    [AllowAnonymous]
    [HttpPost("login-2fa")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginTwoFactor([FromBody] TwoFactorVerifyRequest request)
    {
        try { return await SignInAdminAsync(await _authService.VerifyTwoFactorAsync(request)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    private async Task<IActionResult> SignInAdminAsync(AuthResponse auth)
    {
        // Aucun cookie avant la validation de tous les facteurs.
        if (!string.Equals(auth.Role, "Admin", StringComparison.OrdinalIgnoreCase)
            || auth.MfaRequired || string.IsNullOrWhiteSpace(auth.Token))
            return Unauthorized();

        // Conserver l'empreinte réellement authentifiée : un changement concurrent de mot
        // de passe ne doit pas transformer une ancienne authentification en nouveau cookie.
        var stamp = new JwtSecurityTokenHandler().ReadJwtToken(auth.Token).Claims
            .FirstOrDefault(c => c.Type == JwtTokenGenerator.SecurityStampClaim)?.Value;
        if (string.IsNullOrWhiteSpace(stamp)
            || !await _context.Users.AnyAsync(u => u.Id == auth.UserId && u.SecurityStamp == stamp))
            return Unauthorized();

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, auth.Username),
            new Claim(ClaimTypes.Role, auth.Role),
            new Claim(ClaimTypes.NameIdentifier, auth.UserId.ToString()),
            new Claim(JwtTokenGenerator.SecurityStampClaim, stamp)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Ok(new { auth.Username, auth.Role });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }
}
