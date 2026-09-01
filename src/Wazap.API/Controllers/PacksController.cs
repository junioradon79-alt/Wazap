using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Domain.Enums;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PacksController : ControllerBase
{
    private readonly PackService _packService;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<BuyPackRequest> _buyValidator;

    public PacksController(PackService packService, ICurrentUser currentUser, IValidator<BuyPackRequest> buyValidator)
    {
        _packService = packService;
        _currentUser = currentUser;
        _buyValidator = buyValidator;
    }

    // GET: api/packs — catalogue des packs prépayés
    [HttpGet]
    public IActionResult GetAll() => Ok(_packService.GetPacks());

    // POST: api/packs/buy — achat d'un pack (paiement + crédits vendeur)
    [HttpPost("buy")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin,Vendor")]
    public async Task<IActionResult> Buy([FromBody] BuyPackRequest request)
    {
        var validation = await _buyValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        // Un vendeur ne peut acheter des packs que pour son propre compte.
        if (_currentUser.Role == UserRole.Vendor
            && _currentUser.Id is not null
            && _currentUser.Id.Value != request.VendorId)
        {
            throw new ForbiddenException("Vous ne pouvez acheter des packs que pour votre propre compte.");
        }

        try
        {
            var result = await _packService.BuyPackAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new PaymentResponseDto(false, string.Empty, null, ex.Message));
        }
    }
}

