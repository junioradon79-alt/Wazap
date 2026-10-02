using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Recrutement livreur 100 % WhatsApp, sans intervention manuelle :
///  1. texte d'intention (« je veux livrer »…) → Lead livreur + demande nom / quartier / photo CNI ;
///  2. compléments par texte (nom, quartier) → dernière étape : la photo de la pièce d'identité ;
///  3. photo reçue → téléchargement, création du compte livreur (identifiants envoyés sur
///     WhatsApp), dossier RiderIdentity avec scan chiffré + consentement « whatsapp » tracé,
///     et alerte équipe (certification en 1 clic dans /app/certifications).
/// Le Lead reste visible dans /app/leads (source « whatsapp-livreur ») : la piste n'est jamais
/// perdue, même si le candidat abandonne en cours de route.
/// </summary>
public sealed class RiderRecruitmentService
{
    private static readonly string[] RiderKeywords =
    {
        "je veux livrer", "veux livrer", "devenir livreur", "livreur", "livrer", "coursier",
        "motard", "moto", "a moto", "à moto", "recrut", "gagner", "dispo", "btn_moto", "btn_velo",
        "btn_livreur", "à moto", "a pied", "en moto"
    };

    private static readonly string[] Zones =
    {
        "cocody", "marcory", "yopougon", "adjamé", "adjame", "treichville", "koumassi",
        "plateau", "abobo", "port-bouët", "port bouet", "bingerville", "songon", "anyama",
        "attécoubé", "attecoube"
    };

    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly RiderService _riderService;
    private readonly IWhatsAppSender _whatsApp;
    private readonly IWhatsAppMediaDownloader _mediaDownloader;
    private readonly IOcrService? _ocrService;
    private readonly ILogger<RiderRecruitmentService> _logger;
    private readonly string? _teamPhone;

    public RiderRecruitmentService(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        RiderService riderService,
        IWhatsAppSender whatsApp,
        IWhatsAppMediaDownloader mediaDownloader,
        IConfiguration config,
        ILogger<RiderRecruitmentService> logger,
        IOcrService? ocrService = null)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _riderService = riderService;
        _whatsApp = whatsApp;
        _mediaDownloader = mediaDownloader;
        _logger = logger;
        _teamPhone = config["Prospect:TeamPhone"];
        _ocrService = ocrService;
    }

    /// <summary>Traite un message TEXTE d'un candidat livreur (numéro sans compte). True si consommé.</summary>
    public async Task<bool> TryHandleCandidateAsync(string? phone, string? text)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            return await HandleCandidateTextCoreAsync(phone, text);
        }
        catch (Exception ex)
        {
            // Jamais de 500 sur le webhook : en cas d'incident, le bot prospects prend le relais.
            _logger.LogError(ex, "Recrutement livreur en échec pour {Phone}.", phone);
            return false;
        }
    }

    /// <summary>
    /// Traite un message d'un candidat livreur et retourne le message texte de réponse directement (utilisé par la passerelle Android WAZAP Gateway).
    /// </summary>
    public async Task<string?> GetCandidateResponseTextAsync(string phone, string text, string? senderName = null)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var normalized = "+" + PhoneNumberNormalizer.DigitsOnly(phone);

            if (await HasRiderAccountAsync(normalized))
                return null;

            var lead = await _context.Leads
                .Where(l => l.WhatsAppNumber == normalized && l.Status != LeadStatus.Discarded)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            var fallbackName = !string.IsNullOrWhiteSpace(senderName) && senderName.Trim().Length is >= 3 and <= 45
                ? senderName.Trim()
                : null;

            if (lead is null)
            {
                if (!HasRiderIntent(text))
                    return null;

                var name = TryCaptureName(text) ?? fallbackName;
                var zone = CaptureZone(text);
                var created = new Lead("Candidat livreur", normalized, zone, "whatsapp-livreur", name);
                created.SetReferralCode(TryCaptureReferralCode(text));
                _context.Leads.Add(created);
                await _context.SaveChangesAsync();

                await NotifyTeamAsync($"Candidat livreur détecté : {normalized}" +
                    $"{(name is not null ? " — " + name : "")}{(zone.Length > 0 ? " · " + zone : "")}" +
                    $"{(created.ReferralCode is not null ? " · parrain " + created.ReferralCode : "")}");

                if (!string.IsNullOrWhiteSpace(created.Zone))
                {
                    await CreateRiderAccountFromLeadAsync(created);
                    var riderName = !string.IsNullOrWhiteSpace(created.ContactName) ? $" {created.ContactName}" : "";
                    return $"🎉 Félicitations{riderName} ! Ton profil livreur WAZAP est activé à {created.Zone} !\n\n"
                        + "Tu es désormais EN LIGNE 🟢 pour recevoir les courses.\n\n"
                        + "🪪 Dernière étape (0 saisie texte) :\n"
                        + "Prends en PHOTO ta pièce d'identité (CNI, Permis de conduire ou Passeport) 🪪 et envoie-la directement ici.\n\n"
                        + "⚡ Notre scanner OCR lit automatiquement ton document pour valider ton badge Livreur Certifié et activer tes courses Colis Sûr !";
                }

                return BuildAskMessage(created);
            }

            if (lead.Source != "whatsapp-livreur")
                return null;

            if (lead.Status == LeadStatus.Converted)
            {
                return "✅ Votre profil livreur WAZAP est déjà actif ! Envoyez DISPO pour vous mettre en ligne et recevoir des courses 🛵";
            }

            var capturedName = TryCaptureName(text) ?? fallbackName;
            if (capturedName is not null && (string.IsNullOrWhiteSpace(lead.ContactName) || lead.ContactName == "Candidat livreur"))
                lead.Update(lead.BusinessName, capturedName, lead.Source);
            var capturedZone = CaptureZone(text);
            if (capturedZone.Length > 0)
                lead.SetZone(capturedZone);
            if (string.IsNullOrWhiteSpace(lead.ReferralCode))
                lead.SetReferralCode(TryCaptureReferralCode(text));
            lead.SetStatus(LeadStatus.Contacted);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(lead.Zone))
            {
                await CreateRiderAccountFromLeadAsync(lead);
                var riderName = !string.IsNullOrWhiteSpace(lead.ContactName) ? $" {lead.ContactName}" : "";
                return $"🎉 Félicitations{riderName} ! Ton profil livreur WAZAP est activé à {lead.Zone} !\n\n"
                    + "Tu es désormais EN LIGNE 🟢 pour recevoir les courses.\n\n"
                    + "🪪 Dernière étape (0 saisie texte) :\n"
                    + "Prends en PHOTO ta pièce d'identité (CNI, Permis de conduire ou Passeport) 🪪 et envoie-la directement ici.\n\n"
                    + "⚡ Notre scanner OCR lit automatiquement ton document pour valider ton badge Livreur Certifié et activer tes courses Colis Sûr !";
            }

            return BuildAskMessage(lead);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur Gateway GetCandidateResponseTextAsync pour {Phone}", phone);
            return null;
        }
    }

    private async Task<bool> HandleCandidateTextCoreAsync(string phone, string text)
    {

        var normalized = "+" + PhoneNumberNormalizer.DigitsOnly(phone);

        // Livreur déjà enregistré : le routage normal des commandes s'en charge.
        if (await HasRiderAccountAsync(normalized))
            return false;

        var lead = await _context.Leads
            .Where(l => l.WhatsAppNumber == normalized && l.Status != LeadStatus.Discarded)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();

        if (lead is null)
        {
            if (!HasRiderIntent(text))
                return false;

            var name = TryCaptureName(text);
            var zone = CaptureZone(text);
            var created = new Lead("Candidat livreur", normalized, zone, "whatsapp-livreur", name);
            created.SetReferralCode(TryCaptureReferralCode(text));
            _context.Leads.Add(created);
            await _context.SaveChangesAsync();

            await ReplyAsync(normalized, BuildAskMessage(created));
            await NotifyTeamAsync($"Candidat livreur détecté : {normalized}" +
                $"{(name is not null ? " — " + name : "")}{(zone.Length > 0 ? " · " + zone : "")}" +
                $"{(created.ReferralCode is not null ? " · parrain " + created.ReferralCode : "")}");
            return true;
        }

        if (lead.Source != "whatsapp-livreur")
            return false; // lead commerçant : le bot prospects continue de le gérer

        if (lead.Status == LeadStatus.Converted)
        {
            await ReplyInteractiveAsync(
                normalized,
                "✅ Votre profil livreur WAZAP est déjà actif ! Cliquez ci-dessous pour vous mettre en ligne et recevoir les courses 🛵",
                new[] { ("DISPO", "🟢 DISPO") },
                footerText: "WAZAP Livreur");
            return true;
        }

        // Complète le dossier à partir du message (« Moussa Marcory » en un seul envoi).
        var capturedName = TryCaptureName(text);
        if (capturedName is not null && string.IsNullOrWhiteSpace(lead.ContactName))
            lead.Update(lead.BusinessName, capturedName, lead.Source);
        var capturedZone = CaptureZone(text);
        if (capturedZone.Length > 0)
            lead.SetZone(capturedZone);
        // Code parrain (« WA-XXXX ») : rattaché au dossier, appliqué à la création du compte.
        if (string.IsNullOrWhiteSpace(lead.ReferralCode))
            lead.SetReferralCode(TryCaptureReferralCode(text));
        lead.SetStatus(LeadStatus.Contacted);
        await _context.SaveChangesAsync();

        await ReplyAsync(normalized, BuildAskMessage(lead));
        return true;
    }

    /// <summary>
    /// Traite une PHOTO reçue d'un candidat livreur (numéro sans compte) : création du compte,
    /// stockage chiffré du scan et consentement tracé. True si consommé.
    /// </summary>
    public async Task<bool> HandleCandidatePhotoAsync(string? phone, string? mediaUrl, string? mediaId, string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(phone) || (mediaUrl is null && mediaId is null))
            return false;

        var normalized = "+" + PhoneNumberNormalizer.DigitsOnly(phone);

        // Un livreur connu a déjà son propre traitement (HandleRiderScanPhotoAsync).
        if (await HasRiderAccountAsync(normalized))
            return false;

        var lead = await _context.Leads
            .Where(l => l.WhatsAppNumber == normalized && l.Source == "whatsapp-livreur" && l.Status != LeadStatus.Discarded)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();

        if (lead is null)
            return false;

        if (lead.Status == LeadStatus.Converted)
        {
            await ReplyInteractiveAsync(
                normalized,
                "✅ Votre profil livreur est déjà actif — cliquez ci-dessous pour recevoir les courses 🛵",
                new[] { ("DISPO", "🟢 DISPO") },
                footerText: "WAZAP Livreur");
            return true;
        }

        // Photo trop tôt : la zone doit d'abord être définie (via le lien 1-clic wa.me)
        if (string.IsNullOrWhiteSpace(lead.Zone))
        {
            await ReplyAsync(normalized, BuildAskMessage(lead) + "\nEnvoyez ensuite à nouveau la photo de votre CNI 🪪.");
            return true;
        }

        var download = await _mediaDownloader.TryDownloadAsync(mediaUrl, mediaId, mimeType);
        if (download is null)
        {
            await ReplyAsync(normalized,
                "❌ Impossible de récupérer votre photo. Réessayez dans un instant — elle est indispensable à la certification.");
            return true;
        }

        try
        {
            // Analyse OCR de la pièce d'identité (CNI, Permis de conduire ou Passeport) :
            string resolvedName = !string.IsNullOrWhiteSpace(lead.ContactName) && lead.ContactName != "Candidat livreur"
                ? lead.ContactName
                : "Livreur WAZAP";
            string? detectedCni = null;
            string? detectedDocType = null;

            if (_ocrService != null)
            {
                try
                {
                    var ocr = await _ocrService.ParseIdentityCardAsync(download.Value.Content, mimeType);
                    if (ocr.Success)
                    {
                        if (!string.IsNullOrWhiteSpace(ocr.FullName) && (string.IsNullOrWhiteSpace(lead.ContactName) || lead.ContactName == "Candidat livreur" || lead.ContactName == "Livreur WAZAP"))
                        {
                            resolvedName = ocr.FullName;
                            lead.Update(lead.BusinessName, resolvedName, lead.Source);
                        }
                        detectedCni = ocr.IdNumber;
                        detectedDocType = ocr.DocumentType ?? "Pièce d'identité";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analyse OCR non bloquante en échec pour le livreur {Phone}", normalized);
                }
            }

            var username = await BuildUniqueUsernameAsync(resolvedName);
            var tempPassword = "Wazap-" + Random.Shared.Next(100000, 999999);
            var user = new User(username, _passwordHasher.Hash(tempPassword), UserRole.Rider, normalized);
            user.SetZone(lead.Zone);
            while (await _context.Users.AnyAsync(u => u.ReferralCode == user.ReferralCode))
                user.RegenerateReferralCode();

            // Parrainage : rattache le nouveau livreur à son parrain (code capté plus tôt).
            var referrer = string.IsNullOrWhiteSpace(lead.ReferralCode)
                ? null
                : await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ReferralCode == lead.ReferralCode && u.Id != user.Id);
            if (referrer is not null)
                user.SetReferral(referrer.Id);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await using (var stream = new MemoryStream(download.Value.Content))
                await _riderService.StoreScanAsync(user.Id, stream, download.Value.FileName, mediaUrl);
            await _riderService.RecordWhatsAppConsentAsync(user.Id);

            // Mise à jour de l'identité avec les données extraites par l'OCR :
            if (!string.IsNullOrWhiteSpace(detectedCni))
            {
                var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == user.Id);
                identity?.UpdateParsedInfo(resolvedName, detectedCni);
                await _context.SaveChangesAsync();
            }

            lead.SetStatus(LeadStatus.Converted);
            await _context.SaveChangesAsync();

            var cniNotice = !string.IsNullOrWhiteSpace(detectedCni) ? $"\n🪪 {detectedDocType ?? "Pièce d'identité"} reconnue : {detectedCni}" : "";
            var riderDashboardUrl = $"https://junioradon79gm-001-site1.jtempurl.com/app/login?u={Uri.EscapeDataString(username)}&p={Uri.EscapeDataString(tempPassword)}";
            var welcomeText = $"🎉 Félicitations {resolvedName} ! Ton profil livreur WAZAP est créé !{cniNotice}\n\n"
                + $"📍 Commune active : {lead.Zone}\n"
                + $"• Identifiant : {username}\n"
                + $"• Mot de passe : {tempPassword}\n\n"
                + "📊 Ton Cockpit Livreur en direct (1 clic sans rien taper) :\n"
                + $"{riderDashboardUrl}\n\n"
                + "🛡️ Pièce d'identité enregistrée en sécurité (Assurance Colis Sûr).\n\n"
                + "👉 Pour commencer à recevoir les courses maintenant, clique sur le bouton ci-dessous :";

            await ReplyInteractiveAsync(
                normalized,
                welcomeText,
                new[] { ("DISPO", "🟢 DISPO") },
                footerText: "WAZAP Livreur");

            await NotifyTeamAsync($"Candidature livreur COMPLÈTE (OCR) : {normalized} — {resolvedName} · {lead.Zone} · {detectedCni ?? "scan reçu"}. " +
                (referrer is not null ? $"Parrain : {referrer.Username} ({referrer.ReferralCode}). " : string.Empty) +
                "Vérifier le dossier dans /app/certifications (certification en 1 clic).");

            _logger.LogInformation(
                "Candidat livreur {Phone} converti via OCR : compte {Username} ({FullName}), scan chiffré, CNI {Cni}.",
                normalized, username, resolvedName, detectedCni);
        }
        catch (InvalidOperationException ex)
        {
            // Échec de stockage (clé de chiffrement absente…) : le message du domaine est lisible
            // par le candidat ; le compte créé reste certifiable via l'upload admin.
            await ReplyAsync(normalized, "❌ " + ex.Message);
        }
        catch (Exception ex)
        {
            // Jamais de 500 sur le webhook pour une erreur imprévue : on répond proprement.
            _logger.LogError(ex, "Conversion du candidat livreur {Phone} en échec.", normalized);
            await ReplyAsync(normalized, "❌ Une erreur est survenue. Réessayez dans un instant ou contactez l'équipe WAZAP.");
        }

        return true;
    }

    /// <summary>
    /// Crée ou active un compte User (Role = Rider) dès qu'un lead livreur a fourni sa zone ou son nom.
    /// Garantit que le livreur apparaît immédiatement sur le tableau de bord et peut recevoir des courses.
    /// </summary>
    public async Task<User?> CreateRiderAccountFromLeadAsync(Lead lead, string? resolvedName = null, string? cniNumber = null)
    {
        var normalized = lead.WhatsAppNumber.StartsWith("+")
            ? lead.WhatsAppNumber
            : "+" + PhoneNumberNormalizer.DigitsOnly(lead.WhatsAppNumber);

        var existing = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalized);
        if (existing is not null)
        {
            if (existing.Role == UserRole.Rider)
            {
                if (!string.IsNullOrWhiteSpace(lead.Zone))
                    existing.SetZone(lead.Zone);
                existing.SetAvailability(true);
                lead.SetStatus(LeadStatus.Converted);
                await _context.SaveChangesAsync();
                return existing;
            }
            _logger.LogWarning("Compte {Phone} déjà existant avec rôle non-Rider: {Role}", normalized, existing.Role);
            return existing;
        }

        var candidateName = resolvedName ?? lead.ContactName ?? "Livreur WAZAP";
        var username = await BuildUniqueUsernameAsync(candidateName);
        var tempPassword = "Wazap-" + Random.Shared.Next(100000, 999999);
        var user = new User(username, _passwordHasher.Hash(tempPassword), UserRole.Rider, normalized);
        if (!string.IsNullOrWhiteSpace(lead.Zone))
            user.SetZone(lead.Zone);
        user.SetAvailability(true);

        while (await _context.Users.AnyAsync(u => u.ReferralCode == user.ReferralCode))
            user.RegenerateReferralCode();

        var referrer = string.IsNullOrWhiteSpace(lead.ReferralCode)
            ? null
            : await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.ReferralCode == lead.ReferralCode && u.Id != user.Id);
        if (referrer is not null)
            user.SetReferral(referrer.Id);

        _context.Users.Add(user);
        lead.SetStatus(LeadStatus.Converted);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(cniNumber) || !string.IsNullOrWhiteSpace(candidateName))
        {
            try
            {
                var identity = await _context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == user.Id);
                if (identity is null)
                {
                    identity = new RiderIdentity(user.Id, candidateName, cniNumber);
                    _context.RiderIdentities.Add(identity);
                }
                else
                {
                    identity.UpdateParsedInfo(candidateName, cniNumber);
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Création RiderIdentity non bloquante en échec pour {Username}", username);
            }
        }

        _logger.LogInformation("Livreur auto-enrôlé depuis Lead {Phone} -> {Username} ({Name}, Zone: {Zone})",
            normalized, username, candidateName, user.Zone);

        return user;
    }

    private async Task<bool> HasRiderAccountAsync(string normalizedPhone)
    {
        // SameSubscriber est du C# pur : pré-filtre SQL sur les 8 derniers chiffres.
        var digits = PhoneNumberNormalizer.DigitsOnly(normalizedPhone);
        var suffix = digits.Length >= 8 ? digits[^8..] : digits;
        var candidates = await _context.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Rider && u.PhoneNumber != null && u.PhoneNumber.EndsWith(suffix))
            .Select(u => u.PhoneNumber)
            .ToListAsync();
        return candidates.Any(p => PhoneNumberNormalizer.SameSubscriber(p, normalizedPhone));
    }

    private static bool HasRiderIntent(string text)
    {
        var lower = text.ToLowerInvariant();
        return RiderKeywords.Any(lower.Contains);
    }

    /// <summary>Code de parrainage « WA-XXXX » mentionné dans un message (ex. « parrain WA-AB12 »).</summary>
    private static readonly Regex ReferralCodePattern =
        new(@"\bWA[-\s]?([A-Z0-9]{4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? TryCaptureReferralCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = ReferralCodePattern.Match(text);
        return match.Success ? "WA-" + match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Quartier connu mentionné dans le texte ou sélectionné par numéro (1 à 6), sinon vide.</summary>
    private static string CaptureZone(string text)
    {
        var trimmed = text.Trim().ToLowerInvariant();

        // Normalisation préfixes courants (ex: "choix 1", "#1", "n° 1", "numéro 1", "zone 1", "le 1", "c'est le 1")
        var normalized = trimmed;
        foreach (var prefix in new[] { "choix ", "option ", "zone ", "numero ", "numéro ", "n°", "#", "c'est le ", "c'est la ", "c'est ", "le " })
        {
            if (normalized.StartsWith(prefix))
            {
                normalized = normalized[prefix.Length..].Trim();
                break;
            }
        }

        // Liens 1-clic wa.me et raccourcis par numéros pour les livreurs (chiffres standards ou emojis) :
        if (normalized is "1" or "1️⃣" or "zone_1" or "cocody" || normalized.StartsWith("1 ") || normalized.StartsWith("1-") || normalized.StartsWith("1.") || normalized.StartsWith("1️⃣")) return "Cocody";
        if (normalized is "2" or "2️⃣" or "zone_2" or "yopougon" || normalized.StartsWith("2 ") || normalized.StartsWith("2-") || normalized.StartsWith("2.") || normalized.StartsWith("2️⃣")) return "Yopougon";
        if (normalized is "3" or "3️⃣" or "zone_3" or "marcory" or "zone sud" or "zonesud" || normalized.StartsWith("3 ") || normalized.StartsWith("3-") || normalized.StartsWith("3.") || normalized.StartsWith("3️⃣")) return "Marcory";
        if (normalized is "4" or "4️⃣" or "zone_4" or "abobo" || normalized.StartsWith("4 ") || normalized.StartsWith("4-") || normalized.StartsWith("4.") || normalized.StartsWith("4️⃣")) return "Abobo";
        if (normalized is "5" or "5️⃣" or "zone_5" or "adjame" or "adjamé" or "plateau" || normalized.StartsWith("5 ") || normalized.StartsWith("5-") || normalized.StartsWith("5.") || normalized.StartsWith("5️⃣")) return "Plateau";
        if (normalized is "6" or "6️⃣" or "zone_6" or "koumassi" or "treichville" || normalized.StartsWith("6 ") || normalized.StartsWith("6-") || normalized.StartsWith("6.") || normalized.StartsWith("6️⃣")) return "Marcory";

        // Détection par sous-quartiers connus du Grand Abidjan :
        if (normalized.Contains("angre") || normalized.Contains("angré") || normalized.Contains("riviera") || normalized.Contains("plateaux")) return "Cocody";
        if (normalized.Contains("maroc") || normalized.Contains("siporex") || normalized.Contains("bel air") || normalized.Contains("niangon") || normalized.Contains("toits rouges") || normalized.Contains("selmer")) return "Yopougon";
        if (normalized.Contains("bietry") || normalized.Contains("biétry") || normalized.Contains("zone 4") || normalized.Contains("zone 3") || normalized.Contains("anoumabo") || normalized.Contains("camp militaire")) return "Marcory";
        if (normalized.Contains("anador") || normalized.Contains("abobo baoulé") || normalized.Contains("pk18") || normalized.Contains("samaké") || normalized.Contains("avocatier") || normalized.Contains("agban")) return "Abobo";

        var zone = Zones.FirstOrDefault(z => normalized.Contains(z) || trimmed.Contains(z)) ?? string.Empty;
        return zone.Length > 0 ? char.ToUpperInvariant(zone[0]) + zone[1..] : string.Empty;
    }

    /// <summary>
    /// Capture un nom plausible : 1re ligne courte, sans mots-clés d'intention ;
    /// les mots de zone sont retirés (« Moussa Marcory » → « Moussa »).
    /// </summary>
    private static string? TryCaptureName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var line = text.Trim().Split('\n', ',', ';')[0].Trim().TrimEnd('.', '!', '?');
        if (line.Length is > 45 or < 3)
            return null;

        var lower = line.ToLowerInvariant();
        if (RiderKeywords.Any(lower.Contains) || lower.Length <= 2)
            return null;
        if (lower.StartsWith("je ") || lower.StartsWith("mon ") || lower.StartsWith("ma ")
            || lower is "bonjour" or "salut" or "ok" or "merci" or "oui" or "non")
            return null;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Zones.Contains(w.ToLowerInvariant()))
            .ToArray();
        var name = string.Join(" ", words).Trim();
        return name.Length is >= 3 and <= 45 ? name : null;
    }

    private static string BuildAskMessage(Lead lead)
    {
        // Étape 1 : Si la zone n'est pas encore choisie, proposer les 5 liens 1-clic wa.me par commune :
        if (string.IsNullOrWhiteSpace(lead.Zone))
        {
            return "👋 Bienvenue chez WAZAP Livreur 🛵\n"
                + "Gagne 1 000 à 2 000 FCFA net par course (0% commission) !\n"
                + "(0 frappe au clavier, ton nom complet sera extrait automatiquement de ta pièce)\n\n"
                + "👉 Réponds directement avec le chiffre (1, 2, 3, 4 ou 5) ou touche le lien de ta commune :\n\n"
                + "📍 1. COCODY (Angré, 2 Plateaux, Riviera) :\n"
                + "https://wa.me/2250544051972?text=1%20Cocody\n\n"
                + "📍 2. YOPOUGON (Maroc, Siporex, Bel Air) :\n"
                + "https://wa.me/2250544051972?text=2%20Yopougon\n\n"
                + "📍 3. ZONE SUD (Marcory, Koumassi, Treichville) :\n"
                + "https://wa.me/2250544051972?text=3%20Zone%20Sud\n\n"
                + "📍 4. ABOBO :\n"
                + "https://wa.me/2250544051972?text=4%20Abobo\n\n"
                + "📍 5. PLATEAU / ADJAMÉ :\n"
                + "https://wa.me/2250544051972?text=5%20Plateau\n\n"
                + "⚡ Tape simplement 1, 2, 3, 4 ou 5 pour démarrer immédiatement !";
        }

        // Étape 2 : Zone choisie -> Demande de photo CNI directe (ZÉRO saisie de texte)
        var riderGreeting = !string.IsNullOrWhiteSpace(lead.ContactName) && lead.ContactName != "Candidat livreur"
            ? $" {lead.ContactName}"
            : "";
        return $"🎉 Félicitations{riderGreeting} ! Commune enregistrée : {lead.Zone} !\n\n"
            + "Tu es désormais EN LIGNE 🟢 pour recevoir les courses.\n\n"
            + "🪪 Dernière étape (0 saisie texte) :\n"
            + "Prends en PHOTO ta pièce d'identité (CNI, Permis de conduire ou Passeport) 🪪 et envoie-la directement ici.\n\n"
            + "⚡ Notre scanner OCR lit automatiquement ton document pour valider ton badge Livreur Certifié et activer tes courses Colis Sûr !";
    }

    private async Task<string> BuildUniqueUsernameAsync(string fullName)
    {
        var normalized = RemoveDiacritics(fullName).ToLowerInvariant();
        var builder = new StringBuilder();
        foreach (var ch in normalized)
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);

        var baseName = builder.ToString();
        if (baseName.Length < 3)
            baseName = "livreur";
        if (baseName.Length > 32)
            baseName = baseName[..32];

        var username = baseName;
        var suffix = 1;
        while (await _context.Users.AnyAsync(u => u.Username == username))
            username = baseName[..Math.Min(baseName.Length, 28)] + suffix++.ToString("D2");

        return username;
    }

    private static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task ReplyAsync(string phone, string message)
    {
        try
        {
            await _whatsApp.SendTextMessageAsync(phone, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réponse candidat livreur impossible vers {Phone}.", phone);
        }
    }

    private async Task NotifyTeamAsync(string summary)
    {
        if (string.IsNullOrWhiteSpace(_teamPhone))
            return;
        await ReplyAsync("+" + PhoneNumberNormalizer.DigitsOnly(_teamPhone), "🤖 [Livreur] " + summary);
    }

    private async Task ReplyInteractiveAsync(string phone, string message, IReadOnlyList<(string Id, string Title)> buttons, string? footerText = null)
    {
        try
        {
            await _whatsApp.SendInteractiveButtonsAsync(phone, message, buttons, footerText: footerText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réponse interactive candidat livreur impossible vers {Phone}.", phone);
        }
    }
}
