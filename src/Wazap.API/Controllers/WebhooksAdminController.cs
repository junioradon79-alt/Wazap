using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Entities;
using Wazap.Domain.Services;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

/// <summary>
/// Gestion des abonnés aux webhooks sortants (réservé à l'admin).
/// </summary>
[ApiController]
[Route("api/admin/webhooks")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class WebhooksAdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public WebhooksAdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IReadOnlyList<WebhookSubscriberListItem>> List(CancellationToken ct)
        => await _context.WebhookSubscribers
            .AsNoTracking()
            .OrderBy(s => s.CreatedAt)
            .Select(s => new WebhookSubscriberListItem(s.Id, s.Name, s.Url, s.Events, s.Enabled, s.CreatedAt))
            .ToListAsync(ct);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WebhookSubscriberRequest request, CancellationToken ct)
    {
        if (!TryValidateEvents(request.Events, out var error))
            return BadRequest(new { error });

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return BadRequest(new { error = "URL invalide (http/https requis)." });

        var subscriber = new WebhookSubscriber(request.Name, request.Url, request.Secret, request.Events);
        _context.WebhookSubscribers.Add(subscriber);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "Une URL identique est déjà enregistrée." });
        }

        return Ok(new WebhookSubscriberListItem(subscriber.Id, subscriber.Name, subscriber.Url, subscriber.Events, subscriber.Enabled, subscriber.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] WebhookSubscriberRequest request, CancellationToken ct)
    {
        var subscriber = await _context.WebhookSubscribers.FindAsync([id], ct);
        if (subscriber is null)
            return NotFound();

        if (!TryValidateEvents(request.Events, out var error))
            return BadRequest(new { error });

        subscriber.Update(request.Name, request.Secret, request.Events);
        if (request.Enabled.HasValue)
            subscriber.SetEnabled(request.Enabled.Value);

        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/enabled")]
    public async Task<IActionResult> SetEnabled(Guid id, [FromBody] SetWebhookEnabledRequest request, CancellationToken ct)
    {
        var subscriber = await _context.WebhookSubscribers.FindAsync([id], ct);
        if (subscriber is null)
            return NotFound();

        subscriber.SetEnabled(request.Enabled);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var subscriber = await _context.WebhookSubscribers.FindAsync([id], ct);
        if (subscriber is null)
            return NotFound();

        _context.WebhookSubscribers.Remove(subscriber);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    private static bool TryValidateEvents(IEnumerable<string> events, out string? error)
    {
        var list = events.ToList();
        if (list.Count == 0)
        {
            error = "Au moins un événement est requis.";
            return false;
        }

        var unknown = list.FirstOrDefault(e => !WebhookEvents.IsKnown(e));
        if (unknown is not null)
        {
            error = $"Événement inconnu : {unknown}. Valeurs : {string.Join(", ", WebhookEvents.All)}.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed record WebhookSubscriberRequest(string Name, string Url, string? Secret, string[] Events, bool? Enabled = null);
public sealed record SetWebhookEnabledRequest(bool Enabled);
public sealed record WebhookSubscriberListItem(Guid Id, string Name, string Url, string Events, bool Enabled, DateTime CreatedAt);
