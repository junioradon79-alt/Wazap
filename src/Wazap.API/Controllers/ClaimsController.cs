using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wazap.API.Services;
using Wazap.Application.Abstractions;

namespace Wazap.API.Controllers;

/// <summary>
/// Dossiers de sinistre « Garantie Colis Sûr » (réservé admin) : revue des déclarations
/// WhatsApp, remboursement/indemnisation ou rejet.
/// </summary>
[ApiController]
[Route("api/admin/claims")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class ClaimsController : ControllerBase
{
    private readonly ColisSurService _colisSur;
    private readonly ICurrentUser _currentUser;

    public ClaimsController(ColisSurService colisSur, ICurrentUser currentUser)
    {
        _colisSur = colisSur;
        _currentUser = currentUser;
    }

    // GET: api/admin/claims
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _colisSur.ListAsync());

    // POST: api/admin/claims/{id}/approve — rembourse + indemnise le vendeur, exclut le livreur
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveClaimRequest request)
    {
        try
        {
            await _colisSur.ApproveAsync(id, request.CompensationCredits, request.Note, _currentUser.Id!.Value);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST: api/admin/claims/{id}/reject — rejette le dossier (livreur dégelé, aucun remboursement)
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectClaimRequest request)
    {
        try
        {
            await _colisSur.RejectAsync(id, request.Note, _currentUser.Id!.Value);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed record ApproveClaimRequest(int CompensationCredits = 0, string? Note = null);

public sealed record RejectClaimRequest(string? Note = null);
