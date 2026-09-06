using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

/// <summary>
/// Capture publique de leads (page de vente) : formulaire → stockage, rate limité.
/// </summary>
[ApiController]
[Route("api/public/leads")]
[AllowAnonymous]
public class PublicLeadsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PublicLeadsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    [EnableRateLimiting("leads")]
    public async Task<IActionResult> Create([FromBody] CreateLeadRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName) || request.BusinessName.Length > 120)
            return BadRequest(new { error = "Le nom du commerce est requis (120 caractères max)." });
        if (string.IsNullOrWhiteSpace(request.Zone) || request.Zone.Length > 60)
            return BadRequest(new { error = "Indiquez votre quartier / commune." });

        if (!TryNormalizePhone(request.WhatsAppNumber, out var phone, out var phoneError))
            return BadRequest(new { error = phoneError });

        var lead = new Lead(
            request.BusinessName.Trim(),
            phone,
            request.Zone.Trim(),
            request.Source ?? "page-vente",
            request.ContactName);

        _context.Leads.Add(lead);
        await _context.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new { leadId = lead.Id, status = lead.Status.ToString() });
    }

    /// <summary>
    /// Normalise un numéro ivoirien (nouveau +225+10 ou historique +225+8, avec ou sans « + »/espaces).
    /// </summary>
    internal static bool TryNormalizePhone(string? input, out string phone, out string? error)
    {
        phone = string.Empty;
        error = null;

        var digits = new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            error = "Le numéro WhatsApp est requis.";
            return false;
        }

        string national = digits;
        if (digits.StartsWith("00"))
            national = digits[2..];

        if (national.StartsWith("225"))
        {
            var rest = national[3..];
            if (rest.Length is 8 or 10)
            {
                phone = "+225" + rest;
                return true;
            }
            error = "Numéro ivoirien invalide (8 ou 10 chiffres après 225).";
            return false;
        }

        if (national.StartsWith('0') && national.Length is 8 or 10) // 8 (ancien) ou 10 (nouveau) commençant par 0
        {
            phone = "+225" + national;
            return true;
        }

        error = "Numéro invalide — utilisez le format ivoirien (ex. 07 08 09 10 11).";
        return false;
    }
}

public sealed record CreateLeadRequest(string BusinessName, string WhatsAppNumber, string Zone, string? ContactName = null, string? Source = null);
