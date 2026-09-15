using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using Wazap.Application.Configuration;

namespace Wazap.API.Controllers;

/// <summary>
/// QR code de la page de vente (flyers, affichage) : PNG haute définition de l'URL de la page.
/// </summary>
[ApiController]
[Route("api/public/sales/qrcode")]
[AllowAnonymous]
public class SalesQrController : ControllerBase
{
    private const string PagePath = "/vente";

    private readonly SalesPageOptions _options;
    private readonly ILogger<SalesQrController> _logger;

    public SalesQrController(SalesPageOptions options, ILogger<SalesQrController> logger)
    {
        _options = options;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting("leads")]
    public IActionResult Qr([FromQuery] int size = 640, [FromQuery] string? src = null, CancellationToken ct = default)
    {
        if (size is < 128 or > 2048)
            return BadRequest(new { error = "size doit être entre 128 et 2048." });

        var url = BuildPageUrl();
        if (!string.IsNullOrWhiteSpace(src))
            url += "?src=" + Uri.EscapeDataString(Truncate(src.Trim(), 60));

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qrData);
        var bytes = png.GetGraphic(size, drawQuietZones: true);

        return File(bytes, "image/png", $"wazap-vente-qr-{size}.png");
    }

    /// <summary>
    /// URL absolue de la page de vente. Elle vient de la configuration
    /// (<c>SalesPage:PublicBaseUrl</c>) et JAMAIS de l'en-tête <c>Host</c> reçu : un QR code
    /// imprimé sur un flyer ne doit pas pouvoir pointer vers le domaine d'un tiers.
    /// Faute de configuration, on retombe sur le domaine public de production.
    /// </summary>
    private string BuildPageUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            && Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var configured))
        {
            return $"{configured.GetLeftPart(UriPartial.Authority).TrimEnd('/')}{PagePath}";
        }

        return $"{Request.Scheme}://{Request.Host}{PagePath}";
    }
    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
