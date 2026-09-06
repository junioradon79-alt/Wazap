using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;

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

    [HttpGet]
    [EnableRateLimiting("leads")]
    public IActionResult Qr([FromQuery] int size = 640, [FromQuery] string? src = null, CancellationToken ct = default)
    {
        if (size is < 128 or > 2048)
            return BadRequest(new { error = "size doit être entre 128 et 2048." });

        var url = $"{Request.Scheme}://{Request.Host}{PagePath}";
        if (!string.IsNullOrWhiteSpace(src))
            url += "?src=" + Uri.EscapeDataString(src.Trim());

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qrData);
        var bytes = png.GetGraphic(size, drawQuietZones: true);

        return File(bytes, "image/png", $"wazap-vente-qr-{size}.png");
    }
}
