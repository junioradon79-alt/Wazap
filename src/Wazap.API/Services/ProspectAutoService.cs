using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Automatisation des échanges avec les PROSPECTS (numéros WhatsApp inconnus) :
/// détection d'intention (commerçant / livreur / parrainage), création/qualification d'un
/// <see cref="Lead"/> en base (visible dans /app/leads), réponses contextuelles et alerte
/// de l'équipe (numéro optionnel « Prospect:TeamPhone »).
/// </summary>
public sealed class ProspectAutoService
{
    private static readonly string[] RiderKeywords =
        { "je veux livrer", "veux livrer", "livreur", "livrer", "coursier", "motard", "courses", "gagner", "dispo", "a moto", "à moto" };
    private static readonly string[] VendorKeywords =
        { "commer", "vente", "livraison", "restaurant", "boutique", "inscri", "activer", "offre", "intéress", "pâtisserie", "snack", "commande" };
    private static readonly string[] Zones =
        { "cocody", "marcory", "yopougon", "adjamé", "adjame", "treichville", "koumassi", "plateau", "abobo", "port-bouët", "port bouet", "bingerville", "songon", "anyama", "attécoubé", "attecoube" };

    private readonly ApplicationDbContext _context;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ILogger<ProspectAutoService> _logger;
    private readonly string? _teamPhone;

    public ProspectAutoService(ApplicationDbContext context, IWhatsAppSender whatsApp,
        IConfiguration config, ILogger<ProspectAutoService> logger)
    {
        _context = context;
        _whatsApp = whatsApp;
        _logger = logger;
        _teamPhone = config["Prospect:TeamPhone"];
    }

    /// <summary>Traite un message d'un numéro inconnu. Retourne true si consommé.</summary>
    public async Task<bool> HandleAsync(string? phone, string? text)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = "+" + PhoneNumberNormalizer.DigitsOnly(phone);
        var lower = text.ToLowerInvariant();
        var rider = RiderKeywords.Any(lower.Contains);
        var zone = Zones.FirstOrDefault(z => lower.Contains(z)) ?? string.Empty;
        if (zone.Length > 0)
            zone = char.ToUpperInvariant(zone[0]) + zone[1..];

        try
        {
            var existing = await _context.Leads
                .Where(l => l.WhatsAppNumber == normalized && l.Status != LeadStatus.Discarded)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            if (existing is null)
                return await StartConversationAsync(normalized, text, rider, zone);

            if (existing.Status == LeadStatus.New)
                return await QualifyAsync(existing, text, rider, zone);

            // Déjà Contacté/Converti : plus d'auto-réponse (évite les boucles).
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatisation prospect {Phone} en échec.", phone);
            return false;
        }
    }

    private async Task<bool> StartConversationAsync(string phone, string text, bool rider, string zone)
    {
        var lower = text.ToLowerInvariant();
        var source = rider ? "whatsapp-livreur"
            : lower.Contains("parrain") ? "whatsapp-parrainage"
            : "whatsapp-prospect";

        var businessName = rider ? "Prospect livreur" : CaptureName(text);
        var lead = new Lead(businessName, phone, zone, source, contactName: null);
        _context.Leads.Add(lead);
        await _context.SaveChangesAsync();

        var reply = rider
            ? "Bonjour 👋 Bienvenue chez WAZAP Livreur 🛵 Pour vous inscrire, répondez en un seul message :\n1️⃣ À moto ou à pied ?\n2️⃣ Votre quartier principal ?\n3️⃣ Vos disponibilités ?\nEx. « moto, Marcory, soirs »."
            : "Bonjour 👋 Bienvenue chez WAZAP ⚡ La livraison de votre quartier, 100 % WhatsApp.\nPour activer vos 15 premières commandes OFFERTES, répondez en un message :\n1️⃣ Le nom de votre commerce\n2️⃣ Votre quartier (ex. Marcory)\nEx. « Chez Awa, Marcory ».\n(Psst : écrivez « je veux livrer » si vous cherchez à livrer 🛵)";

        await SendBestEffortAsync(phone, reply);
        var convertHint = source == "whatsapp-livreur"
            ? string.Empty
            : $"\n👉 Répondez « CONVERTIR {phone} » pour créer son compte vendeur en 1 clic.";
        await NotifyTeamAsync($"Nouveau prospect ({source}) — {phone}{(zone.Length > 0 ? " · " + zone : "")}{convertHint}");
        _logger.LogInformation("Lead WhatsApp créé : {Phone} (source {Source}).", phone, source);
        return true;
    }

    private async Task<bool> QualifyAsync(Lead lead, string text, bool rider, string zone)
    {
        var capturedName = CaptureName(text);
        if (rider && capturedName.Length > 0)
            lead.Update(lead.BusinessName, capturedName, lead.Source);
        else if (!rider && capturedName.Length > 0 && lead.BusinessName.StartsWith("Commerce"))
            lead.Update(capturedName, lead.ContactName, lead.Source);

        if (!rider && lead.Zone.Length == 0 && zone.Length > 0)
            lead.SetZone(zone);

        lead.SetStatus(LeadStatus.Contacted);
        await _context.SaveChangesAsync();

        var name = rider ? lead.ContactName : lead.BusinessName;
        var reply = rider
            ? $"Merci{(name is null ? "" : " " + name)} ! ✅ Votre profil livreur est en préparation — notre équipe vous confirme sous 24 h (identifiants + zone). Une fois activé, écrivez DISPO pour recevoir les courses 🛵"
            : $"Merci{(name is null ? "" : " " + name)} ! ✅ J’ai bien noté votre demande d’activation. Notre équipe vous confirme sous 24 h (vérifiez aussi vos spams). En attendant, envoyez « LIVRAISON + produit + quartier » pour un test !";

        await SendBestEffortAsync(lead.WhatsAppNumber, reply);
        var convertHint = lead.Source == "whatsapp-livreur"
            ? string.Empty
            : $"\n👉 Répondez « CONVERTIR {lead.WhatsAppNumber} » pour créer son compte vendeur en 1 clic.";
        await NotifyTeamAsync($"Lead qualifié ✅ {lead.WhatsAppNumber} — {lead.BusinessName}{(zone.Length > 0 ? " · " + zone : "")}{convertHint}");
        return true;
    }

    /// <summary>Capture un nom plausible : 1re ligne courte, sans mots-clés d'intention ni de zone.</summary>
    private static string CaptureName(string text)
    {
        var line = (text ?? string.Empty)
            .Trim()
            .Split('\n', ',', ';')[0]
            .Trim()
            .TrimEnd('.', '!', '?');

        if (line.Length is > 45 or < 3)
            return string.Empty;
        var lower = line.ToLowerInvariant();
        if (RiderKeywords.Any(lower.Contains) || VendorKeywords.Any(lower.Contains))
            return string.Empty;
        if (Zones.Any(lower.Contains))
            return string.Empty;
        if (lower.StartsWith("je ") || lower.StartsWith("mon ") || lower.StartsWith("ma ")
            || lower is "bonjour" or "salut" or "ok" or "merci")
            return string.Empty;

        return line;
    }

    private async Task SendBestEffortAsync(string phone, string message)
    {
        try
        {
            await _whatsApp.SendTextMessageAsync(phone, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réponse prospect impossible vers {Phone}.", phone);
        }
    }

    private async Task NotifyTeamAsync(string summary)
    {
        if (string.IsNullOrWhiteSpace(_teamPhone))
            return;
        await SendBestEffortAsync("+" + PhoneNumberNormalizer.DigitsOnly(_teamPhone), "🤖 [Prospect] " + summary);
    }
}

