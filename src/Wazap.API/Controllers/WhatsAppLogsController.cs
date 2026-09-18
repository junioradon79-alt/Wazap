using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;

namespace Wazap.API.Controllers;

/// <summary>
/// Journalisation et suivi financier des échanges WhatsApp (chantier T2 / C1).
/// Permet à l'administrateur d'inspecter les messages entrants/sortants et d'analyser
/// le coût réel Meta en FCFA (Utility, Marketing, Authentication, Service).
/// </summary>
[ApiController]
[Route("api/admin/whatsapp")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class WhatsAppLogsController : ControllerBase
{
    private readonly IWhatsAppMessageLogService _logService;

    public WhatsAppLogsController(IWhatsAppMessageLogService logService)
    {
        _logService = logService;
    }

    /// <summary>
    /// Récupère la liste des derniers messages WhatsApp audités avec filtres optionnels.
    /// </summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(IReadOnlyList<WhatsAppMessageLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int limit = 100,
        [FromQuery] Guid? orderId = null,
        [FromQuery] string? phone = null,
        CancellationToken ct = default)
    {
        var logs = await _logService.GetRecentLogsAsync(limit, orderId, phone, ct);
        return Ok(logs);
    }

    /// <summary>
    /// Calcule le résumé financier des coûts WhatsApp pour une période donnée.
    /// </summary>
    [HttpGet("costs")]
    [ProducesResponseType(typeof(WhatsAppCostSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCosts(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var summary = await _logService.GetCostSummaryAsync(from, to, ct);
        return Ok(summary);
    }
}
