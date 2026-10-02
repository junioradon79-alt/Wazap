using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

/// <summary>
/// Contrôleur dédié à la passerelle Android WAZAP Gateway (WazapRelay).
/// Reçoit les messages WhatsApp interceptés par l'application Android sur le smartphone officiel,
/// traite la logique métier et retourne instantanément le texte de réponse pour envoi en direct via RemoteInput.
/// </summary>
[ApiController]
[Route("api/gateway/whatsapp")]
public class GatewayWhatsAppController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly RiderRecruitmentService _riderRecruitment;
    private readonly RiderService _riderService;
    private readonly RiderProgramService _riderProgram;
    private readonly IOcrService? _ocrService;
    private readonly ILogger<GatewayWhatsAppController> _logger;

    public GatewayWhatsAppController(
        ApplicationDbContext context,
        RiderRecruitmentService riderRecruitment,
        RiderService riderService,
        RiderProgramService riderProgram,
        ILogger<GatewayWhatsAppController> logger,
        IOcrService? ocrService = null)
    {
        _context = context;
        _riderRecruitment = riderRecruitment;
        _riderService = riderService;
        _riderProgram = riderProgram;
        _logger = logger;
        _ocrService = ocrService;
    }

    private static readonly string[] ProtectedNumberSuffixes = new[]
    {
        "0708323366", // Numéro WhatsApp personnel du propriétaire (strictement exclu de toute automatisation)
        "708323366",
        "0544051972", // Numéro officiel WAZAP (anti-boucle réflexive)
        "544051972"
    };

    private static bool IsProtectedOrBlacklisted(string phoneDigits)
    {
        if (string.IsNullOrWhiteSpace(phoneDigits)) return true;
        foreach (var suffix in ProtectedNumberSuffixes)
        {
            if (phoneDigits.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Ping de diagnostic pour tester la connectivité depuis l'application Android.</summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new
        {
            status = "online",
            gateway = "WAZAP Gateway API",
            timestamp = DateTime.UtcNow,
            server = "wazap.ci"
        });
    }

    /// <summary>
    /// Traite un message WhatsApp entrant intercepté sur le smartphone Android et retourne la réponse à expédier.
    /// </summary>
    [HttpPost("process")]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> Process([FromBody] GatewayInboundRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Sender) || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new GatewayInboundResponse(false, false, null, "invalid_request", null));
        }

        var rawPhone = request.Sender.Trim();
        var digits = PhoneNumberNormalizer.DigitsOnly(rawPhone);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return Ok(new GatewayInboundResponse(true, false, null, "ignored", rawPhone));
        }

        var normalizedPhone = "+" + digits;

        // GARDE-FOU 1 : Rejet formel des applications autres que WhatsApp Business (ex: WhatsApp personnel com.whatsapp)
        if (!string.IsNullOrWhiteSpace(request.PackageName) &&
            !string.Equals(request.PackageName, "com.whatsapp.w4b", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Passerelle Android : Rejet notification issue d'une app non autorisée : {Package}", request.PackageName);
            return Ok(new GatewayInboundResponse(true, false, null, "ignored_unsupported_app", normalizedPhone));
        }

        // GARDE-FOU 2 : Rejet formel du numéro personnel du propriétaire / admin (+225 07 08 32 33 66)
        if (IsProtectedOrBlacklisted(digits))
        {
            _logger.LogWarning("Passerelle Android : Numéro personnel protégé/propriétaire ignoré sans réponse automatique : {Phone}", normalizedPhone);
            return Ok(new GatewayInboundResponse(true, false, null, "ignored_protected_number", normalizedPhone));
        }

        var text = request.Text.Trim();
        var upper = text.ToUpperInvariant();

        _logger.LogInformation("Passerelle Android WAZAP : Message de {Phone} : {Text}", normalizedPhone, text);

        // 1. Candidat Livreur (Enrôlement, Commune, Choix 1-6)
        var candidateReply = await _riderRecruitment.GetCandidateResponseTextAsync(normalizedPhone, text, request.SenderName);
        if (candidateReply is not null)
        {
            return Ok(new GatewayInboundResponse(
                Success: true,
                ShouldReply: true,
                ReplyText: candidateReply,
                Category: "rider_recruitment",
                Sender: normalizedPhone));
        }

        // 2. Utilisateur existant (Livreur certifié)
        var last8 = digits.Length >= 8 ? digits.Substring(digits.Length - 8) : digits;
        var rider = await _context.Users
            .Where(u => u.Role == UserRole.Rider && u.PhoneNumber != null && u.PhoneNumber.EndsWith(last8))
            .FirstOrDefaultAsync();

        if (rider is not null && PhoneNumberNormalizer.SameSubscriber(rider.PhoneNumber, normalizedPhone))
        {
            if (upper is "INDISPO" || upper.Contains("INDISPO") || upper.Contains("HORS LIGNE") || upper.Contains("PAUSE") || upper is "2" or "2️⃣" or "🔴")
            {
                await _riderService.SetAvailabilityAsync(rider.Id, false);
                var reply = "🔴 WAZAP : Tu es désormais HORS LIGNE (en pause).\n\n"
                    + "👉 Liens 1-clic rapides :\n"
                    + "🟢 Reprendre les courses :\nhttps://wa.me/2250544051972?text=DISPO\n\n"
                    + "💰 Voir mon argent :\nhttps://wa.me/2250544051972?text=SOLDE";
                return Ok(new GatewayInboundResponse(true, true, reply, "rider_status", normalizedPhone));
            }

            if (upper is "DISPO" || upper.Contains("DISPO") || upper.Contains("EN LIGNE") || upper is "1" or "1️⃣" or "🟢")
            {
                await _riderService.SetAvailabilityAsync(rider.Id, true);
                var zoneName = !string.IsNullOrWhiteSpace(rider.Zone) ? $" à {rider.Zone}" : "";
                var reply = $"🟢 WAZAP : Vous êtes désormais EN LIGNE{zoneName} !\n"
                    + "Tes courses vont arriver ici directement 🛵💨\n\n"
                    + "👉 Liens 1-clic rapides :\n"
                    + "🔴 Me mettre en pause :\nhttps://wa.me/2250544051972?text=INDISPO\n\n"
                    + "💰 Voir mon argent :\nhttps://wa.me/2250544051972?text=SOLDE\n\n"
                    + "🏆 Mon smartphone cadeau :\nhttps://wa.me/2250544051972?text=PROGRAMME";
                return Ok(new GatewayInboundResponse(true, true, reply, "rider_status", normalizedPhone));
            }

            if (upper is "PROGRAMME" or "AMBASSADEUR" or "REDMI" or "CHALLENGE" or "SMARTPHONE" or "4" or "4️⃣" or "📱" or "CADEAU")
            {
                var progress = await _riderProgram.BuildProgressAsync(rider.Id);
                var deliveries = progress?.Deliveries ?? 0;
                var targetDeliveries = progress?.DeliveriesTarget ?? 250;
                var referrals = progress?.ValidatedReferrals ?? 0;
                var targetReferrals = progress?.ReferralsTarget ?? 5;
                var isEligible = progress?.RewardUnlocked == true;

                var reply = $"🏆 Challenge Ambassadeur WAZAP (Xiaomi Redmi 15C) :\n\n"
                    + $"• Livraisons : {deliveries} / {targetDeliveries} courses\n"
                    + $"• Filleuls actifs : {referrals} / {targetReferrals} livreurs parrainés\n"
                    + $"• Statut : {(isEligible ? "🎉 CHALLENGE REMPORTÉ ! Contactez l'équipe WAZAP pour votre smartphone." : "⏳ En cours d'accomplissement")}\n\n";

                if (!string.IsNullOrWhiteSpace(rider.ReferralCode))
                {
                    reply += "📲 *Ton Lien Parrain à transférer à tes collègues (1 Clic) :*\n"
                        + $"https://wa.me/2250544051972?text=DISPO%20{rider.ReferralCode}\n"
                        + "_(Ton collègue touche le lien et envoie : il est automatiquement lié à toi !)_\n\n";
                }

                reply += "👉 Liens 1-clic rapides :\n"
                    + "🟢 Me mettre en ligne :\nhttps://wa.me/2250544051972?text=DISPO\n\n"
                    + "💰 Voir mon argent :\nhttps://wa.me/2250544051972?text=SOLDE";
                return Ok(new GatewayInboundResponse(true, true, reply, "rider_program", normalizedPhone));
            }

            if (upper is "DASHBOARD" or "STATS" or "SOLDE" or "3" or "3️⃣" or "💰" or "GAINS" or "ARGENT")
            {
                var zoneName = rider.Zone ?? "Non définie";
                var isOnline = rider.IsAvailable ? "🟢 En ligne" : "🔴 Hors ligne";
                var completedCount = await _context.Orders.CountAsync(o => o.RiderUserId == rider.Id && o.Status == OrderStatus.Delivered);

                var reply = $"📊 Mon Espace Livreur WAZAP :\n\n"
                    + $"• Statut : {isOnline}\n"
                    + $"• Zone principale : {zoneName}\n"
                    + $"• Courses terminées : {completedCount}\n\n"
                    + "👉 Liens 1-clic rapides :\n"
                    + "🟢 Me mettre en ligne :\nhttps://wa.me/2250544051972?text=DISPO\n\n"
                    + "🔴 Me mettre en pause :\nhttps://wa.me/2250544051972?text=INDISPO\n\n"
                    + "🏆 Mon smartphone cadeau :\nhttps://wa.me/2250544051972?text=PROGRAMME";
                return Ok(new GatewayInboundResponse(true, true, reply, "rider_dashboard", normalizedPhone));
            }
        }

        // 3. Mots-clés Commerçant / Expédition
        if (upper is "COLIS" or "EXPEDIER" or "VENDEUR" or "BOUTIQUE" or "COMMERCANT" or "LIVRAISON")
        {
            var reply = "🏪 Bienvenue sur WAZAP Commerçant !\n\n"
                + "🎁 Vos 15 premières livraisons sont OFFERTES (0% commission).\n\n"
                + "👉 Touchez le lien pour expédier un colis en 1 clic :\n"
                + "https://wa.me/2250544051972?text=LIVRAISON\n\n"
                + "💰 Consulter la grille des tarifs :\n"
                + "https://wa.me/2250544051972?text=TARIFS\n\n"
                + "🎙️ Astuce : Vous pouvez aussi nous envoyer directement une NOTE VOCALE avec le lieu de retrait et de livraison !";
            return Ok(new GatewayInboundResponse(true, true, reply, "merchant_welcome", normalizedPhone));
        }

        // 4. Mots-clés Tarifs
        if (upper is "TARIF" or "TARIFS" or "PRIX" or "COMBIEN" or "COUT")
        {
            var reply = "📋 Grille Tarifaire WAZAP (Grand Abidjan) :\n\n"
                + "🛵 Frais de course (100% au livreur indépendant) :\n"
                + "• Même commune : 1 000 FCFA net\n"
                + "• Commune voisine : 1 500 FCFA\n"
                + "• Traversée de pont / Longue distance : 2 000 FCFA\n\n"
                + "🛡️ Garantie Colis Sûr : 0 FCFA d'espèces sur la marchandise, règlement 100% par Scan QR Code Universel (Wave, Orange Money, MTN, Moov, Carte bancaire).";
            return Ok(new GatewayInboundResponse(true, true, reply, "pricing_info", normalizedPhone));
        }

        // 5. Message de secours si notification photo reçue sous forme texte
        if (text.Contains("photo", StringComparison.OrdinalIgnoreCase) || text.Contains("📷") || text.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            var photoReply = "📸 Photo bien reçue ! Notre système vérifie votre document 🪪⚡\n\n"
                + "👉 Pour voir vos courses en direct : https://wa.me/2250544051972?text=DISPO";
            return Ok(new GatewayInboundResponse(true, true, photoReply, "photo_acknowledgement", normalizedPhone));
        }

        // 6. Menu d'aide et orientation - UNIQUEMENT sur demande d'assistance explicite (AIDE, MENU, INFOS, WAZAP)
        var isExplicitHelp = upper is "AIDE" or "HELP" or "MENU" or "INFOS" or "INFO" or "WAZAP";

        if (isExplicitHelp)
        {
            var defaultReply = "👋 Menu WAZAP Abidjan ⚡\n\n"
                + "🛵 ESPACE LIVREURS (0% commission) :\n"
                + "• Me mettre en ligne ➔ https://wa.me/2250544051972?text=DISPO\n"
                + "• Me mettre en pause ➔ https://wa.me/2250544051972?text=INDISPO\n"
                + "• Voir mes gains ➔ https://wa.me/2250544051972?text=SOLDE\n\n"
                + "🏪 ESPACE COMMERÇANTS (15 courses offertes) :\n"
                + "• Expédier un colis ➔ https://wa.me/2250544051972?text=COLIS\n"
                + "• Grille des tarifs ➔ https://wa.me/2250544051972?text=TARIFS";

            return Ok(new GatewayInboundResponse(true, true, defaultReply, "help_menu", normalizedPhone));
        }

        // Pour tout autre message non reconnu (discussions personnelles, salutations courantes, messages ordinaires),
        // STRICTEMENT AUCUNE réponse automatique n'est expédiée pour garantir l'absence totale d'interférence.
        _logger.LogInformation("Passerelle Android : Message sans commande WAZAP de {Phone} ('{Text}'). Ignoré sans réponse automatique.", normalizedPhone, text);
        return Ok(new GatewayInboundResponse(
            Success: true,
            ShouldReply: false,
            ReplyText: null,
            Category: "ignored_unrecognized_message",
            Sender: normalizedPhone));
    }

    /// <summary>
    /// Téléverse et analyse par OCR la photo d'identité (CNI / Permis) reçue sur WhatsApp via la passerelle Android,
    /// stocke le scan chiffré, certifie le livreur et renvoie le message de confirmation avec les liens 1-tap.
    /// </summary>
    [HttpPost("upload-photo")]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> UploadPhoto(
        [FromForm] IFormFile? file,
        [FromForm] string sender,
        [FromForm] string? senderName)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new GatewayInboundResponse(false, false, null, "missing_file", sender));
        }

        var rawPhone = sender?.Trim() ?? string.Empty;
        var digits = PhoneNumberNormalizer.DigitsOnly(rawPhone);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return BadRequest(new GatewayInboundResponse(false, false, null, "invalid_phone", sender));
        }

        var normalizedPhone = "+" + digits;

        // GARDE-FOU : Rejet formel du numéro personnel du propriétaire / admin (+225 07 08 32 33 66)
        if (IsProtectedOrBlacklisted(digits))
        {
            _logger.LogWarning("Passerelle Android : Téléversement photo ignoré pour numéro personnel protégé/propriétaire : {Phone}", normalizedPhone);
            return Ok(new GatewayInboundResponse(true, false, null, "ignored_protected_number", normalizedPhone));
        }

        _logger.LogInformation("Passerelle Android WAZAP : Photo CNI/Permis reçue de {Phone} ({SenderName}), taille: {Size} octets",
            normalizedPhone, senderName, file.Length);

        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        OcrIdentityResult? ocr = null;
        if (_ocrService != null)
        {
            try
            {
                ocr = await _ocrService.ParseIdentityCardAsync(imageBytes, file.ContentType ?? "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR Google Vision en échec pour {Phone}", normalizedPhone);
            }
        }

        var extractedName = ocr?.Success == true && !string.IsNullOrWhiteSpace(ocr.FullName)
            ? ocr.FullName
            : (!string.IsNullOrWhiteSpace(senderName) ? senderName.Trim() : null);

        var extractedIdNumber = ocr?.Success == true && !string.IsNullOrWhiteSpace(ocr.IdNumber)
            ? ocr.IdNumber
            : null;

        var docType = ocr?.DocumentType ?? "Pièce d'identité";

        var last8 = digits.Length >= 8 ? digits[^8..] : digits;
        var rider = await _context.Users
            .Where(u => u.Role == UserRole.Rider && u.PhoneNumber != null && u.PhoneNumber.EndsWith(last8))
            .FirstOrDefaultAsync();

        var lead = await _context.Leads
            .Where(l => l.WhatsAppNumber == normalizedPhone && l.Status != LeadStatus.Discarded)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();

        if (rider is null && lead is not null)
        {
            rider = await _riderRecruitment.CreateRiderAccountFromLeadAsync(lead, extractedName, extractedIdNumber);
        }
        else if (rider is null)
        {
            lead = new Lead("Candidat Livreur Photo", normalizedPhone, "Cocody", "whatsapp-livreur-photo", extractedName);
            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();
            rider = await _riderRecruitment.CreateRiderAccountFromLeadAsync(lead, extractedName, extractedIdNumber);
        }

        if (rider is null)
        {
            return StatusCode(500, new GatewayInboundResponse(false, false, null, "rider_creation_failed", normalizedPhone));
        }

        try
        {
            using var scanStream = new MemoryStream(imageBytes);
            await _riderService.StoreScanAsync(rider.Id, scanStream, file.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stockage scan non bloquant en échec pour {UserId}", rider.Id);
        }

        var finalVerifiedName = extractedName ?? rider.Username;
        var finalIdNumber = extractedIdNumber ?? ("AUTO-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        await _riderService.VerifyRiderAsync(rider.Id, finalVerifiedName, finalIdNumber, null, Guid.Empty);

        if (lead != null)
        {
            lead.SetStatus(LeadStatus.Converted);
            await _context.SaveChangesAsync();
        }

        var idNotice = !string.IsNullOrWhiteSpace(extractedIdNumber) ? $" (N° {extractedIdNumber})" : "";
        var riderDashboardUrl = $"https://junioradon79gm-001-site1.jtempurl.com/app/login?u={Uri.EscapeDataString(rider.Username)}";
        var replyText = $"🎉 Félicitations {finalVerifiedName} !\n\n"
            + $"✅ Votre {docType}{idNotice} a été analysée et validée avec succès par notre système 🪪⚡\n\n"
            + "🛡️ Vous avez désormais le statut officiel de LIVREUR CERTIFIÉ WAZAP (Assurance Colis Sûr activée) !\n\n"
            + "📊 Touchez ce lien pour ouvrir votre Cockpit Livreur (courses, gains nets et challenge smartphone) :\n"
            + $"👉 {riderDashboardUrl}\n\n"
            + "🛵 Pour vous mettre en ligne et recevoir vos premières courses immédiatement, cliquez ci-dessous :\n"
            + "👉 https://wa.me/2250544051972?text=DISPO";

        return Ok(new GatewayInboundResponse(
            Success: true,
            ShouldReply: true,
            ReplyText: replyText,
            Category: "rider_photo_verified",
            Sender: normalizedPhone));
    }
}

public sealed class GatewayInboundRequest
{
    public string Sender { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? MessageId { get; set; }
    public long? Timestamp { get; set; }
    public string? PackageName { get; set; }
}

public record GatewayInboundResponse(
    bool Success,
    bool ShouldReply,
    string? ReplyText,
    string? Category,
    string? Sender);
