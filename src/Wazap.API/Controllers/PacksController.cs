using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Dtos;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PacksController : ControllerBase
{
    private readonly PackService _packService;
    private readonly IValidator<BuyPackRequest> _buyValidator;

    public PacksController(PackService packService, IValidator<BuyPackRequest> buyValidator)
    {
        _packService = packService;
        _buyValidator = buyValidator;
    }

    // GET: api/packs — catalogue des packs prépayés
    [HttpGet]
    public IActionResult GetAll() => Ok(_packService.GetPacks());

    // POST: api/packs/buy — achat d'un pack (paiement + crédits vendeur)
    [HttpPost("buy")]
    [Authorize(Roles = "Admin,Vendor")]
    public async Task<IActionResult> Buy([FromBody] BuyPackRequest request)
    {
        var validation = await _buyValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = await _packService.BuyPackAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new PaymentResponseDto(false, string.Empty, ex.Message));
        }
    }
}
