using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RidersController : ControllerBase
{
    private readonly RiderService _riderService;
    private readonly RiderProgramService _riderProgram;
    private readonly RiderPriorityService _riderPriority;
    private readonly IValidator<BuyRiderPriorityRequest> _buyPriorityValidator;
    private readonly ICurrentUser _currentUser;
    private readonly ApplicationDbContext _context;
    private readonly RiderRecruitmentService _riderRecruitment;
    private readonly IOcrService _ocrService;

    public RidersController(
        RiderService riderService,
        RiderProgramService riderProgram,
        RiderPriorityService riderPriority,
        IValidator<BuyRiderPriorityRequest> buyPriorityValidator,
        ICurrentUser currentUser,
        ApplicationDbContext context,
        RiderRecruitmentService riderRecruitment,
        IOcrService ocrService)
    {
        _riderService = riderService;
        _riderProgram = riderProgram;
        _riderPriority = riderPriority;
        _buyPriorityValidator = buyPriorityValidator;
        _currentUser = currentUser;
        _context = context;
        _riderRecruitment = riderRecruitment;
        _ocrService = ocrService;
    }

    // GET: api/riders — réservé à l'admin (RGPD : téléphones + positions exposés)
    [HttpGet]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        // Auto-synchronisation des leads livreurs en attente pour garantir leur visibilité immédiate :
        try
        {
            var pendingLeads = await _context.Leads
                .Where(l => (l.Source == "whatsapp-livreur" || l.Source == "dashboard-enrolement") && l.Status != LeadStatus.Discarded && l.Status != LeadStatus.Converted)
                .ToListAsync();

            foreach (var lead in pendingLeads)
            {
                await _riderRecruitment.CreateRiderAccountFromLeadAsync(lead);
            }
        }
        catch
        {
            // Tolérance aux pannes : la liste principale doit toujours répondre
        }

        return Ok(await _riderService.GetRidersAsync());
    }

    // POST: api/riders/location — le livreur (via son token) partage sa position ;
    // l'admin peut cibler un livreur via riderUserId.
    [HttpPost("location")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateRiderLocationRequest request)
    {
        var riderId = ResolveRiderId(request.RiderUserId);
        await _riderService.UpdateLocationAsync(riderId, request.Latitude, request.Longitude);
        return NoContent();
    }

    // PUT: api/riders/{id}/availability — le livreur ne gère que son propre compte.
    [HttpPut("{id:guid}/availability")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> SetAvailability(Guid id, [FromBody] SetAvailabilityRequest request)
    {
        EnsureOwnership(id);
        await _riderService.SetAvailabilityAsync(id, request.IsAvailable);
        return NoContent();
    }

    // PUT: api/riders/{id}/zone — zone/quartier déclaré (téléphones basiques sans GPS)
    [HttpPut("{id:guid}/zone")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> SetZone(Guid id, [FromBody] SetZoneRequest request)
    {
        EnsureOwnership(id);
        await _riderService.SetZoneAsync(id, request.Zone);
        return NoContent();
    }

    // PUT: api/riders/{id}/location-sharing — RGPD : désactivation volontaire.
    [HttpPut("{id:guid}/location-sharing")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> SetLocationSharing(Guid id, [FromBody] SetLocationSharingRequest request)
    {
        EnsureOwnership(id);
        await _riderService.SetLocationSharingAsync(id, request.IsEnabled);
        return NoContent();
    }

    // GET: api/riders/certifications — dossiers d'identité (Garantie Colis Sûr)
    [HttpGet("certifications")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> GetCertifications()
        => Ok(await _riderService.GetCertificationsAsync());

    // POST: api/riders/{id}/verify — certification après contrôle d'identité (badge « certifié »)
    [HttpPost("{id:guid}/verify")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> Verify(Guid id, [FromBody] VerifyRiderRequest request)
    {
        await _riderService.VerifyRiderAsync(id, request.FullName, request.IdNumber, request.Motorcycle,
            _currentUser.Id);
        return NoContent();
    }

    // POST: api/riders/{id}/scan — téléversement du scan de la pièce d'identité (photo reçue sur WhatsApp)
    [HttpPost("{id:guid}/scan")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> UploadScan(Guid id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Fichier manquant ou vide." });

        try
        {
            await _riderService.StoreScanAsync(id, file.OpenReadStream(), file.FileName);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET: api/riders/{id}/scan — lecture du scan stocké (réservé admin, déchiffré à la volée)
    [HttpGet("{id:guid}/scan")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> GetScan(Guid id)
    {
        var scan = await _riderService.GetScanContentAsync(id);
        if (scan is not { } content || content.Content is null || content.FileName is null)
            return NotFound();

        var extension = Path.GetExtension(content.FileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
        return File(content.Content, contentType, Path.GetFileName(content.FileName));
    }

    // POST: api/riders/{id}/reject — certification refusée (dossier incomplet/incohérent)
    [HttpPost("{id:guid}/reject")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRiderRequest request)
    {
        await _riderService.RejectRiderAsync(id, request.Reason, _currentUser.Id);
        return NoContent();
    }

    // POST: api/riders/{id}/blacklist — exclusion définitive (vol/fraude) : hors-ligne + plus d'offres
    [HttpPost("{id:guid}/blacklist")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> Blacklist(Guid id, [FromBody] BlacklistRiderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Un motif est requis pour exclure un livreur." });

        await _riderService.BlacklistRiderAsync(id, request.Reason.Trim(), _currentUser.Id);
        return NoContent();
    }

    // GET: api/riders/program — progression « Ambassadeur WAZAP » de tous les livreurs (admin)
    [HttpGet("program")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> GetProgram()
        => Ok(await _riderProgram.BuildAllAsync());

    // GET: api/riders/{id}/program — progression d'un livreur (lui-même ou admin)
    [HttpGet("{id:guid}/program")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> GetRiderProgram(Guid id)
    {
        EnsureOwnership(id);
        var progress = await _riderProgram.BuildProgressAsync(id);
        return progress is null ? NotFound() : Ok(progress);
    }

    // GET: api/riders/{id}/priority — état du « pack prioritaire » + catalogue
    // (être proposé en premier dans son rayon : WAZAP vend la visibilité, pas l'attribution)
    [HttpGet("{id:guid}/priority")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> GetPriority(Guid id)
    {
        EnsureOwnership(id);
        var status = await _riderPriority.GetStatusAsync(id);
        return status is null ? NotFound() : Ok(status);
    }

    // POST: api/riders/{id}/priority — achat d'un pack prioritaire (paiement Mobile Money)
    [HttpPost("{id:guid}/priority")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Rider,Admin")]
    public async Task<IActionResult> BuyPriority(Guid id, [FromBody] BuyRiderPriorityRequest request)
    {
        EnsureOwnership(id);

        // L'identifiant de la route fait foi : un livreur ne peut activer la priorité que
        // pour son propre compte (l'admin peut cibler un livreur).
        request.RiderId = id;

        var validation = await _buyPriorityValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = await _riderPriority.BuyAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new PaymentResponseDto(false, string.Empty, null, ex.Message));
        }
    }

    // POST: api/riders/ocr-scan — analyse OCR d'une CNI (Bouton Magique)
    [HttpPost("ocr-scan")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> ScanIdentityCard(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Fichier manquant ou vide." });

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();

            var result = await _ocrService.ParseIdentityCardAsync(imageBytes, file.ContentType, ct);
            return Ok(new
            {
                success = result.Success,
                fullName = result.FullName,
                idNumber = result.IdNumber,
                rawText = result.RawText,
                error = result.Error
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                success = false,
                fullName = (string?)null,
                idNumber = (string?)null,
                rawText = (string?)null,
                error = ex.Message
            });
        }
    }

    // POST: api/riders/sync-leads — conversion explicite de tous les leads livreurs en comptes actifs
    [HttpPost("sync-leads")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> SyncLeads()
    {
        var leads = await _context.Leads
            .Where(l => (l.Source == "whatsapp-livreur" || l.Source == "dashboard-enrolement") && l.Status != LeadStatus.Discarded && l.Status != LeadStatus.Converted)
            .ToListAsync();

        var converted = 0;
        foreach (var lead in leads)
        {
            var user = await _riderRecruitment.CreateRiderAccountFromLeadAsync(lead);
            if (user is not null) converted++;
        }

        return Ok(new { message = $"{converted} livreurs synchronisés avec succès.", total = leads.Count });
    }

    // POST: api/riders/enroll — enrôlement manuel immédiat depuis le dashboard
    [HttpPost("enroll")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> ManualEnroll([FromBody] ManualEnrollRiderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(new { error = "Numéro de téléphone requis." });

        var digits = PhoneNumberNormalizer.DigitsOnly(request.PhoneNumber);
        var normalized = "+" + digits;
        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.WhatsAppNumber == normalized);
        if (lead is null)
        {
            lead = new Lead("Livreur Enrôlé", normalized, request.Zone ?? "Cocody", "dashboard-enrolement", request.FullName);
            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.FullName))
                lead.Update(lead.BusinessName, request.FullName, lead.Source);
            if (!string.IsNullOrWhiteSpace(request.Zone))
                lead.SetZone(request.Zone);
            await _context.SaveChangesAsync();
        }

        var user = await _riderRecruitment.CreateRiderAccountFromLeadAsync(lead, request.FullName);
        return Ok(new { message = "Livreur enrôlé avec succès", userId = user?.Id, username = user?.Username });
    }

    // DELETE: api/riders/{id} — suppression d'un livreur (admin)
    [HttpDelete("{id:guid}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> DeleteRider(Guid id)
    {
        var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Rider);
        if (rider is null)
            return NotFound(new { error = "Livreur introuvable." });

        var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == id);
        if (identity is not null)
            _context.RiderIdentities.Remove(identity);

        var purchases = await _context.RiderPriorityPurchases.Where(p => p.RiderUserId == id).ToListAsync();
        if (purchases.Count > 0)
            _context.RiderPriorityPurchases.RemoveRange(purchases);

        var offers = await _context.DeliveryOffers.Where(o => o.RiderUserId == id).ToListAsync();
        if (offers.Count > 0)
            _context.DeliveryOffers.RemoveRange(offers);

        var ratings = await _context.RiderRatings.Where(r => r.RiderUserId == id).ToListAsync();
        if (ratings.Count > 0)
            _context.RiderRatings.RemoveRange(ratings);

        _context.Users.Remove(rider);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Livreur « {rider.Username} » supprimé avec succès." });
    }

    // POST: api/riders/purge-demo — purge spécifique des comptes démo de test
    [HttpPost("purge-demo")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public async Task<IActionResult> PurgeDemoRiders()
    {
        var demoPhones = new[] { "+33670112233", "+33760334455", "+33660445566", "+33770556677" };
        var demoNames = new[] { "Karim Diallo", "Sofiane Benali", "Lucas Martin", "Yann Le Goff" };

        var demoUsers = await _context.Users
            .Where(u => u.Role == UserRole.Rider && (demoPhones.Contains(u.PhoneNumber) || demoNames.Contains(u.Username)))
            .ToListAsync();

        if (demoUsers.Count == 0)
            return Ok(new { message = "Aucun compte de test détecté. La base est déjà propre.", purgedCount = 0 });

        var ids = demoUsers.Select(u => u.Id).ToList();

        var identities = await _context.RiderIdentities.Where(i => ids.Contains(i.UserId)).ToListAsync();
        if (identities.Count > 0)
            _context.RiderIdentities.RemoveRange(identities);

        var offers = await _context.DeliveryOffers.Where(o => ids.Contains(o.RiderUserId)).ToListAsync();
        if (offers.Count > 0)
            _context.DeliveryOffers.RemoveRange(offers);

        _context.Users.RemoveRange(demoUsers);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"{demoUsers.Count} compte(s) de test supprimé(s) avec succès.", purgedCount = demoUsers.Count });
    }

    private Guid ResolveRiderId(Guid? explicitId)
    {
        if (_currentUser.Role == UserRole.Admin && explicitId.HasValue)
            return explicitId.Value;

        return _currentUser.Id ?? throw new ForbiddenException("Identité livreur requise.");
    }

    private void EnsureOwnership(Guid id)
    {
        if (_currentUser.Role == UserRole.Admin)
            return;

        if (_currentUser.Id == id)
            return;

        throw new ForbiddenException("Vous ne pouvez modifier que votre propre compte livreur.");
    }
}

public sealed record UpdateRiderLocationRequest(Guid? RiderUserId, double Latitude, double Longitude);

public sealed record SetAvailabilityRequest(bool IsAvailable);

public sealed record SetLocationSharingRequest(bool IsEnabled);

public sealed record SetZoneRequest(string Zone);

public sealed record VerifyRiderRequest(string? FullName, string? IdNumber, string? Motorcycle);

public sealed record RejectRiderRequest(string? Reason);

public sealed record BlacklistRiderRequest(string Reason);

public sealed record ManualEnrollRiderRequest(string? FullName, string PhoneNumber, string? Zone);
