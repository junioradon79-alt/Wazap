using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Dtos;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/auth/ui")]
public class AccountController : ControllerBase
{
    private readonly AuthService _authService;

    public AccountController(AuthService authService) => _authService = authService;

    /// <summary>
    /// Connexion au dashboard administrateur (délivre un cookie de session).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
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

        // Seul un administrateur accède au tableau de bord.
        if (!string.Equals(auth.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, auth.Username),
            new Claim(ClaimTypes.Role, auth.Role),
            new Claim(ClaimTypes.NameIdentifier, auth.UserId.ToString())
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
