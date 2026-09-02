using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class AuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly SecurityOptions _security;
    private readonly TrialOptions _trial;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        WhatsAppOrchestrationService whatsApp,
        SecurityOptions security,
        TrialOptions trial,
        ILogger<AuthService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _whatsApp = whatsApp;
        _security = security;
        _trial = trial;
        _logger = logger;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request)
    {
        var exists = await _context.Users.AnyAsync(u => u.Username == request.Username);
        if (exists)
            throw new InvalidOperationException("Ce nom d'utilisateur est déjà utilisé.");

        var user = new User(request.Username, _passwordHasher.Hash(request.Password), request.Role, PhoneNumberNormalizer.Normalize(request.PhoneNumber));

        // Garantir l'unicité du code de parrainage avant sauvegarde.
        while (await _context.Users.AnyAsync(u => u.ReferralCode == user.ReferralCode))
            user.RegenerateReferralCode();

        _context.Users.Add(user);

        // Offre de découverte : N premières commandes offertes aux nouveaux vendeurs
        // (pour découvrir la solution avant d'acheter un pack). Tracée dans l'historique.
        var trialCredits = request.Role == UserRole.Vendor && _trial.Enabled
            ? Math.Max(0, _trial.FreeCreditsOnRegistration)
            : 0;
        var trialGranted = false;

        if (trialCredits > 0)
        {
            user.AddCredits(trialCredits);
            _context.CreditTransactions.Add(CreditTransaction.ForFreeGrant(
                user.Id,
                trialCredits,
                $"TRIAL-{user.ReferralCode}",
                "Offre découverte - premières commandes offertes"));
            trialGranted = true;
            _logger.LogInformation("Offre de découverte : {Credits} crédits offerts au vendeur {Username} ({Ref}).",
                trialCredits, user.Username, $"TRIAL-{user.ReferralCode}");
        }

        // Parrainage : si un code promo est fourni, créditer le parrain (+5 crédits) et le notifier.
        if (!string.IsNullOrWhiteSpace(request.ReferralCode))
        {
            var code = request.ReferralCode.Trim();
            var sponsor = await _context.Users.FirstOrDefaultAsync(u => u.ReferralCode == code)
                ?? throw new InvalidOperationException("Code de parrainage invalide.");
            if (sponsor.Id == user.Id)
                throw new InvalidOperationException("Impossible d'utiliser son propre code de parrainage.");

            user.SetReferral(sponsor.Id);
            sponsor.AddCredits(5);
            await _context.SaveChangesAsync();

            try
            {
                await _whatsApp.SendTextAsync(sponsor,
                    $"Félicitations ! {user.Username} s'est inscrit grâce à vous. Vous avez reçu 5 crédits supplémentaires.");
            }
            catch (Exception ex)
            {
                // Best-effort : l'inscription ne doit pas échouer si WhatsApp est indisponible.
                _logger.LogWarning(ex, "Notification de parrainage WhatsApp impossible pour {Sponsor}.", sponsor.Username);
            }
        }
        else
        {
            await _context.SaveChangesAsync();
        }

        // Mode d'emploi post-enrôlement (best-effort) : guide en ≤ 3 étapes simples selon
        // le rôle. Hors fenêtre 24 h (prospect jamais contacté), l'envoi échouera et sera loggé.
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            try
            {
                await _whatsApp.SendTextAsync(user, BuildOnboardingGuide(user, trialGranted, trialCredits));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Guide d'onboarding WhatsApp impossible pour {User}.", user.Username);
            }
        }

        return new UserDto(user.Id, user.Username, user.Role.ToString());
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        // Verrouillage anti force-brute : après N échecs, le compte est bloqué
        // pendant Security:LockoutMinutes (HTTP 423).
        if (user is not null && user.IsLocked() && user.LockedUntilUtc is { } lockedUntil)
            throw new AccountLockedException(lockedUntil);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.RegisterFailedLogin(
                    Math.Max(1, _security.MaxFailedLoginAttempts),
                    TimeSpan.FromMinutes(Math.Max(1, _security.LockoutMinutes)));
                await _context.SaveChangesAsync();

                if (user.IsLocked() && user.LockedUntilUtc is { } until)
                    throw new AccountLockedException(until);
            }

            throw new UnauthorizedAccessException("Identifiants invalides.");
        }

        if (user.FailedLoginAttempts != 0 || user.LockedUntilUtc is not null)
        {
            user.ResetLoginFailures();
            await _context.SaveChangesAsync();
        }

        var token = _jwtTokenGenerator.Generate(user);
        return new AuthResponse(user.Id, token, user.Username, user.Role.ToString());
    }

    /// <summary>
    /// Change le mot de passe d'un utilisateur après vérification de son mot de passe actuel.
    /// </summary>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Le mot de passe actuel est incorrect.");

        user.ChangePassword(_passwordHasher.Hash(request.NewPassword));
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Guide de prise en main envoyé après un enrôlement réussi (≤ 3 étapes simples),
    /// adapté au rôle du compte. Les crédits offerts (Trial) sont rappelés aux vendeurs.
    /// </summary>
    private static string BuildOnboardingGuide(User user, bool trialGranted, int trialCredits)
    {
        var offerLine = trialGranted
            ? $"🎁 Vos {trialCredits} premières commandes de livraison sont offertes pour découvrir le service !\n"
            : string.Empty;

        return user.Role switch
        {
            UserRole.Vendor =>
                $"🎉 Bienvenue chez WAZAP, {user.Username} !\n{offerLine}" +
                "Vos clients commandent par téléphone, Facebook ou en boutique ? " +
                "WAZAP s'occupe de la LIVRAISON.\n" +
                "Mode d'emploi :\n" +
                "1. Une commande à livrer ? Demandez une course WAZAP depuis votre espace\n" +
                "2. Le livreur le plus proche vient chercher le colis chez vous\n" +
                "3. Il livre votre client : vous suivez l'avancement sur WhatsApp\n" +
                "📩 Envoyez AIDE pour le menu.",

            UserRole.Rider =>
                $"🚴 Bienvenue chez WAZAP, {user.Username} !\n" +
                "Mode d'emploi :\n" +
                "1. Envoyez ZONE <votre quartier> (ex : ZONE Cocody)\n" +
                "2. Envoyez DISPO pour passer en ligne\n" +
                "3. Répondez ACCEPTE <code> pour prendre une course\n" +
                "📩 Envoyez AIDE pour le menu complet.",

            _ =>
                $"👋 Bienvenue chez WAZAP, {user.Username} ! Envoyez AIDE pour découvrir les services disponibles."
        };
    }
}
