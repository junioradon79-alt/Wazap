using System.Text;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Conversion d'un <see cref="Lead"/> qualifié en compte vendeur : création de l'utilisateur
/// (rôle Vendor), octroi des crédits d'offre découverte, mot de passe temporaire, code de
/// parrainage et message de bienvenue WhatsApp. Idempotent par numéro.
/// </summary>
public sealed class LeadConversionService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TrialOptions _trial;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ILogger<LeadConversionService> _logger;

    public LeadConversionService(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        TrialOptions trial,
        IWhatsAppSender whatsApp,
        ILogger<LeadConversionService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _trial = trial;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    public async Task<LeadConversionResult> ConvertAsync(Guid leadId, bool sendWelcome = true, CancellationToken ct = default)
    {
        var lead = await _context.Leads.FindAsync([leadId], ct)
            ?? throw new InvalidOperationException("Lead introuvable.");

        if (lead.Status == LeadStatus.Discarded)
            throw new InvalidOperationException("Un lead écarté ne peut pas être converti.");

        if (lead.Source == "whatsapp-livreur")
            throw new InvalidOperationException("Ce lead vise un profil livreur — la création d'un compte vendeur est impossible.");

        var phone = lead.WhatsAppNumber;
        if (string.IsNullOrWhiteSpace(phone))
            throw new InvalidOperationException("Le lead n'a pas de numéro WhatsApp.");

        // Idempotence : un vendeur existe déjà pour ce numéro ?
        var existing = await _context.Users.FirstOrDefaultAsync(
            u => u.Role == UserRole.Vendor && PhoneNumberNormalizer.SameSubscriber(u.PhoneNumber, phone), ct);

        if (existing is not null)
        {
            lead.SetStatus(LeadStatus.Converted);
            await _context.SaveChangesAsync(ct);
            return new LeadConversionResult(existing.Id, existing.Username, null, existing.Credits,
                existing.ReferralCode, existing.Zone ?? lead.Zone, true);
        }

        // Username dérivé du commerce (unique).
        var username = await BuildUniqueUsernameAsync(lead.BusinessName, ct);

        var tempPassword = "Wazap-" + Random.Shared.Next(100000, 999999);
        var user = new User(username, _passwordHasher.Hash(tempPassword), UserRole.Vendor, phone);
        if (!string.IsNullOrWhiteSpace(lead.Zone))
            user.SetZone(lead.Zone);

        // Unicité du code de parrainage.
        while (await _context.Users.AnyAsync(u => u.ReferralCode == user.ReferralCode, ct))
            user.RegenerateReferralCode();

        _context.Users.Add(user);

        // Onboarding vendeur séquencé : 1re étape programmée à J+1.
        user.StartVendorOnboarding();

        // Offre découverte : crédits offerts (même logique que l'inscription classique).
        var trialCredits = _trial.Enabled ? Math.Max(0, _trial.FreeCreditsOnRegistration) : 0;
        if (trialCredits > 0)
        {
            user.AddCredits(trialCredits);
            _context.CreditTransactions.Add(CreditTransaction.ForFreeGrant(
                user.Id, trialCredits, $"TRIAL-{user.ReferralCode}", "Offre découverte - 15 commandes offertes"));
        }

        lead.SetStatus(LeadStatus.Converted);
        await _context.SaveChangesAsync(ct);

        if (sendWelcome)
            await SendWelcomeAsync(lead, user, trialCredits);

        _logger.LogInformation("Lead {LeadId} converti en vendeur {UserId} ({Username}).", lead.Id, user.Id, user.Username);
        return new LeadConversionResult(user.Id, username, tempPassword, user.Credits,
            user.ReferralCode, lead.Zone, false);
    }

    private async Task<string> BuildUniqueUsernameAsync(string businessName, CancellationToken ct)
    {
        var normalized = RemoveDiacritics(businessName ?? "commerce").ToLowerInvariant();
        var builder = new StringBuilder();
        foreach (var ch in normalized)
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);

        var baseName = builder.ToString();
        if (baseName.Length < 3)
            baseName = "commerce";
        if (baseName.Length > 32)
            baseName = baseName[..32];

        var username = baseName;
        var suffix = 1;
        while (await _context.Users.AnyAsync(u => u.Username == username, ct))
            username = baseName[..Math.Min(baseName.Length, 28)] + suffix++.ToString("D2");

        return username;
    }

    private async Task SendWelcomeAsync(Lead lead, User user, int trialCredits)
    {
        var name = string.IsNullOrWhiteSpace(lead.ContactName) ? lead.BusinessName : lead.ContactName;
        var msg = $"Félicitations {name} ! 🎉 Votre compte vendeur WAZAP est actif.\n"
            + $"• Code parrainage : {user.ReferralCode}\n"
            + (trialCredits > 0 ? $"• {trialCredits} crédits offerts pour démarrer (15 premières commandes)\n" : "")
            + "Pour lancer votre 1re commande test, envoyez : LIVRAISON + votre produit + quartier\n"
            + "Ex. « LIVRAISON 2 poulets braisés à Marcory, rue Princesse » 🛵";

        try
        {
            await _whatsApp.SendTextMessageAsync(lead.WhatsAppNumber, msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Message de bienvenue impossible vers {Phone}.", lead.WhatsAppNumber);
        }
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
}

public sealed record LeadConversionResult(
    Guid VendorId,
    string Username,
    string? TemporaryPassword,
    int Credits,
    string ReferralCode,
    string? Zone,
    bool AlreadyExisted);

