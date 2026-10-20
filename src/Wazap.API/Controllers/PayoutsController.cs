using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/admin/payouts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class PayoutsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("reconciliation")]
    public async Task<IActionResult> ReconciliationQueue()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            historicalCompletedWithoutPayout = await db.OrderPayments.CountAsync(p =>
                p.Status == Wazap.Domain.Enums.TransactionStatus.Completed && p.VendorPayoutDue > 0
                && !db.PaymentPayouts.Any(v => v.PaymentId == p.Id)),
            items = await db.OrderPayments.AsNoTracking().Where(p => p.Status == Wazap.Domain.Enums.TransactionStatus.Pending
                && p.PaymentLink == null).OrderBy(p => p.CreatedAt).Take(50)
                .Select(p => new { p.Id, p.OrderId, p.Amount, p.CreatedAt, p.ErrorMessage }).ToListAsync()
        });
    }

    [HttpPost("payments/{id:guid}/reconcile")]
    public async Task<IActionResult> ReconcilePayment(Guid id, ReconcilePaymentRequest request,
        [FromServices] Wazap.Application.Abstractions.IPaymentService gateway,
        [FromServices] Wazap.Application.Services.ClientPaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 200) return BadRequest();
        var payment = await db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound();
        if (payment.Status != Wazap.Domain.Enums.TransactionStatus.Pending) return Conflict();
        var reference = request.Reference.Trim();
        var status = await gateway.CheckPaymentStatusAsync(reference);
        if (status is null || !status.Confirms(payment.Amount, reference) || status.WazapTransactionId != payment.Id)
            return BadRequest(new { message = "Le prestataire ne confirme pas ce paiement exact en XOF avec ses métadonnées WAZAP. Aucun changement effectué." });
        await payments.CompletePaymentAsync(id, reference);
        return Ok(new { message = "Paiement rapproché après vérification serveur du prestataire." });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] bool pendingOnly = true)
    {
        Response.Headers.CacheControl = "no-store";
        var query = db.PaymentPayouts.AsNoTracking();
        if (pendingOnly) query = query.Where(p => p.ConfirmedAtUtc == null);
        return Ok(new
        {
            total = await query.CountAsync(),
            items = await query.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
                .Skip((Math.Clamp(page, 1, 100000) - 1) * 50).Take(50).ToListAsync()
        });
    }

    // Rapprochement d'un transfert déjà effectué : cet endpoint ne déplace aucun argent.
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, ConfirmPayoutRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewer) || reviewer == Guid.Empty)
            return Unauthorized();
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        if (db.Database.IsRelational())
            await db.PaymentPayouts.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Amount, p => p.Amount));
        var payout = await db.PaymentPayouts.FirstOrDefaultAsync(p => p.Id == id);
        if (payout is null) return NotFound();
        if (payout.ConfirmedAtUtc is not null) return Conflict(new { message = "Versement déjà rapproché." });
        try
        {
            payout.Confirm(reviewer, request.Provider, request.Reference, request.Evidence, request.Amount);
            await db.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return Ok(payout);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "Cette référence a déjà été utilisée. Vérifiez la preuve." }); }
    }
}

public sealed record ConfirmPayoutRequest(string Provider, string Reference, string Evidence, decimal Amount);
public sealed record ReconcilePaymentRequest(string Reference);
