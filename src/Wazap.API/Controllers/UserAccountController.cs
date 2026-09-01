using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/account")]
public class UserAccountController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;

    public UserAccountController(
        AuthService authService,
        ICurrentUser currentUser,
        IValidator<ChangePasswordRequest> changePasswordValidator)
    {
        _authService = authService;
        _currentUser = currentUser;
        _changePasswordValidator = changePasswordValidator;
    }

    // POST: api/account/change-password — changer son propre mot de passe
    [HttpPost("change-password")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var validation = await _changePasswordValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        if (_currentUser.Id is null)
            return Unauthorized();

        try
        {
            await _authService.ChangePasswordAsync(_currentUser.Id.Value, request);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }

        return NoContent();
    }
}
