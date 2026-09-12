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
        "motard", "moto", "a moto", "à moto", "recrut", "gagner", "dispo"
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
    private readonly ILogger<RiderRecruitmentService> _logger;
    private readonly string? _teamPhone;

    public RiderRecruitmentService(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        RiderService riderService,
        IWhatsAppSender whatsApp,
        IWhatsAppMediaDownloader mediaDownloader,
        IConfiguration config,
        ILogger<RiderRecruitmentService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _riderService = riderService;
        _whatsApp = whatsApp;
        _mediaDownloader = mediaDownloader;
        _logger = logger;
        _teamPhone = config["Prospect:TeamPhone"];
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
            await ReplyAsync(normalized,
                "✅ Votre profil livreur WAZAP est déjà actif ! Envoyez DISPO pour recevoir les courses, " +
                "ou ZONE <quartier> pour définir votre zone 🛵");
            return true;
        }

        // Complète le dossier à partir du message (« Moussa Marcory » en un seul envoi).
        var capturedName = TryCaptureName(text);
        if (capturedName is not null && string.IsNullOrWhiteSpace(lead.ContactName))
            lead.Update(lead.BusinessName, capturedName, lead.Source);
        var capturedZone = CaptureZone(text);
        if (capturedZone.Length > 0 && string.IsNullOrWhiteSpace(lead.Zone))
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
            await ReplyAsync(normalized,
                "✅ Votre profil livreur est déjà actif — envoyez DISPO pour recevoir les courses 🛵");
            return true;
        }

        // Photo trop tôt : on redonne les étapes (la piste, elle, n'est pas perdue).
        if (string.IsNullOrWhiteSpace(lead.ContactName) || string.IsNullOrWhiteSpace(lead.Zone))
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
            var username = await BuildUniqueUsernameAsync(lead.ContactName);
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

            lead.SetStatus(LeadStatus.Converted);
            await _context.SaveChangesAsync();

            await ReplyAsync(normalized,
                "🎉 Votre profil livreur WAZAP est créé !\n"
                + $"• Identifiant : {username}\n"
                + $"• Mot de passe : {tempPassword}\n"
                + "🛵 Envoyez DISPO ici pour recevoir les courses, puis ZONE <quartier>.\n"
                + "🛡️ Votre photo CNI est en cours de vérification : notre équipe vous certifie sous 24 h.");
            await NotifyTeamAsync($"Candidature livreur COMPLÈTE : {normalized} — {lead.ContactName} · {lead.Zone}. " +
                (referrer is not null ? $"Parrain : {referrer.Username} ({referrer.ReferralCode}). " : string.Empty) +
                "Vérifier le dossier dans /app/certifications (certification en 1 clic).");

            _logger.LogInformation(
                "Candidat livreur {Phone} converti : compte {Username} créé, scan chiffré, consentement tracé.",
                normalized, username);
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

    /// <summary>Quartier connu mentionné dans le texte (casse normalisée), sinon vide.</summary>
    private static string CaptureZone(string text)
    {
        var lower = text.ToLowerInvariant();
        var zone = Zones.FirstOrDefault(z => lower.Contains(z)) ?? string.Empty;
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
        if (RiderKeywords.Any(lower.Contains))
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
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(lead.ContactName))
            missing.Add("votre nom complet");
        if (string.IsNullOrWhiteSpace(lead.Zone))
            missing.Add("votre quartier (ex : Marcory)");

        if (lead.Status == LeadStatus.New && missing.Count == 2)
            return "👋 Bienvenue chez WAZAP 🛵 Pour devenir livreur, envoyez-moi :\n"
                + "1️⃣ Votre nom complet\n"
                + "2️⃣ Votre quartier\n"
                + "3️⃣ Une photo de votre pièce d'identité (CNI)\n\n"
                + "Un message texte pour 1️⃣ et 2️⃣, puis la photo.\n\n"
                + "💡 Vous avez un code parrain WAZAP ? Envoyez-le aussi (ex. WA-AB12) pour le rejoindre.";

        if (missing.Count > 0)
            return "Merci ! Il me manque : " + string.Join(" et ", missing) + ".\n"
                + "Envoyez aussi la photo de votre CNI 🪪 (elle est indispensable à la certification « Garantie Colis Sûr »).";

        return "✅ Parfait ! Dernière étape : envoyez une photo de votre pièce d'identité (CNI) 🪪 — "
            + "elle est indispensable à la certification « Garantie Colis Sûr ».";
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
}
