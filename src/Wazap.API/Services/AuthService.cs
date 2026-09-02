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

        // Bienvenue avec l'offre (best-effort, hors fenêtre 24 h l'envoi échouera silencieusement).
        if (trialGranted && !string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            try
            {
                await _whatsApp.SendTextAsync(user,
                    $"🎁 Bienvenue chez WAZAP, {user.Username} ! Vos {trialCredits} premières commandes de livraison sont offertes pour découvrir le service. Ensuite, choisissez un pack pour continuer !");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Message de bienvenue WhatsApp impossible pour {User}.", user.Username);
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
}
