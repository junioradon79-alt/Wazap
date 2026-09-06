using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wazap.Application.Configuration;

namespace Wazap.API.Controllers;

/// <summary>
/// Configuration publique de la page de vente (numéro WhatsApp pour le bouton CTA).
/// </summary>
[ApiController]
[Route("api/public/sales")]
[AllowAnonymous]
public class PublicSalesPageController : ControllerBase
{
    private readonly SalesPageOptions _options;

    public PublicSalesPageController(SalesPageOptions options)
    {
        _options = options;
    }

    [HttpGet("config")]
    [EnableRateLimiting("leads")]
    public IActionResult Config()
        => Ok(new { whatsappNumber = _options.WhatsAppNumber ?? string.Empty });
}
