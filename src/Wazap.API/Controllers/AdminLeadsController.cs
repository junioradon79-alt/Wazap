using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

/// <summary>
/// Gestion des leads d'acquisition (réservé à l'équipe/admin) : liste, statut, export CSV,
/// conversion d'un lead qualifié en compte vendeur.
/// </summary>
[ApiController]
[Route("api/admin/leads")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class AdminLeadsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly LeadConversionService _conversion;

    public AdminLeadsController(ApplicationDbContext context, LeadConversionService conversion)
    {
        _context = context;
        _conversion = conversion;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? zone,
        [FromQuery] string? source,
        [FromQuery] string? referral,
        [FromQuery] string? search,
        [FromQuery] LeadStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var query = _context.Leads.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(zone))
            query = query.Where(l => l.Zone == zone);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(l => l.Source == source);
        if (!string.IsNullOrWhiteSpace(referral))
            query = query.Where(l => l.ReferralCode != null && l.ReferralCode.StartsWith(referral.Trim()));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l => l.BusinessName.Contains(term)
                || (l.ContactName != null && l.ContactName.Contains(term))
                || l.WhatsAppNumber.Contains(term));
        }
        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);
        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value);

        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(l => new LeadListItem(
                l.Id, l.BusinessName, l.ContactName, l.WhatsAppNumber, l.Zone, l.Source,
                l.ReferralCode, l.Status, l.CreatedAt))
            .ToListAsync(ct);

        return Ok(leads);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetLeadStatusRequest request, CancellationToken ct)
    {
        var lead = await _context.Leads.FindAsync([id], ct);
        if (lead is null)
            return NotFound();

        if (!Enum.IsDefined(request.Status))
            return BadRequest(new { error = $"Statut invalide : {request.Status}." });

        lead.SetStatus(request.Status);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Convertit un lead qualifié en compte vendeur (crédits offerts, code parrainage,
    /// mot de passe temporaire, bienvenue WhatsApp). Idempotent.
    /// </summary>
    [HttpPost("{id:guid}/convert")]
    public async Task<IActionResult> Convert(Guid id, [FromBody] ConvertLeadRequest? request, CancellationToken ct)
    {
        try
        {
            var result = await _conversion.ConvertAsync(id, request?.SendWelcome ?? true, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? zone,
        [FromQuery] string? source,
        [FromQuery] string? referral,
        [FromQuery] string? search,
        [FromQuery] LeadStatus? status,
        CancellationToken ct)
    {
        var query = _context.Leads.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(zone))
            query = query.Where(l => l.Zone == zone);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(l => l.Source == source);
        if (!string.IsNullOrWhiteSpace(referral))
            query = query.Where(l => l.ReferralCode != null && l.ReferralCode.StartsWith(referral.Trim()));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l => l.BusinessName.Contains(term)
                || (l.ContactName != null && l.ContactName.Contains(term))
                || l.WhatsAppNumber.Contains(term));
        }
        if (status.HasValue)
            query = query.Where(l => l.Status == status.Value);

        var leads = await query.OrderByDescending(l => l.CreatedAt).Take(10_000).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("id;commerce;contact;whatsapp;zone;source;code_parrainage;statut;cree_le");
        foreach (var l in leads)
        {
            sb.AppendLine(string.Join(';',
                l.Id,
                Csv(l.BusinessName),
                Csv(l.ContactName ?? string.Empty),
                l.WhatsAppNumber,
                Csv(l.Zone),
                Csv(l.Source),
                Csv(l.ReferralCode ?? string.Empty),
                l.Status,
                l.CreatedAt.ToString("s")));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8",
            $"wazap-leads-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private static string Csv(string value)
        => value.Replace(";", ",").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
}

public sealed record LeadListItem(
    Guid Id,
    string BusinessName,
    string? ContactName,
    string WhatsAppNumber,
    string Zone,
    string Source,
    string? ReferralCode,
    LeadStatus Status,
    DateTime CreatedAt);

public sealed record SetLeadStatusRequest(LeadStatus Status);

public sealed record ConvertLeadRequest(bool SendWelcome = true);
