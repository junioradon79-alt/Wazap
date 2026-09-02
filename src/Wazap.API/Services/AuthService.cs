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

        // 2FA activée : on demande le code avant de délivrer les jetons.
        if (user.TwoFactorEnabled)
            return new AuthResponse(user.Id, null, user.Username, user.Role.ToString(), MfaRequired: true);

        return await IssueAuthAsync(user);
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

    /// <summary>Émet le couple (access JWT 8 h + refresh token 30 j, stocké hashé).</summary>
    private async Task<AuthResponse> IssueAuthAsync(User user)
    {
        var access = _jwtTokenGenerator.Generate(user);
        var rawRefresh = SecurityHelper.GenerateOpaqueToken();
        var refresh = new RefreshToken(user.Id, SecurityHelper.Sha256Hex(rawRefresh), DateTime.UtcNow.AddDays(30));
        _context.RefreshTokens.Add(refresh);
        await _context.SaveChangesAsync();

        return new AuthResponse(user.Id, access, user.Username, user.Role.ToString(), RefreshToken: rawRefresh);
    }

    /// <summary>Rafraîchit la session : rotation du refresh token (l'ancien est révoqué).</summary>
    public async Task<AuthResponse> RefreshAsync(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException("Refresh token manquant.");

        var hash = SecurityHelper.Sha256Hex(refreshToken);
        var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash)
            ?? throw new UnauthorizedAccessException("Session expirée.");

        if (!stored.IsActive)
            throw new UnauthorizedAccessException("Session expirée ou révoquée.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId)
            ?? throw new UnauthorizedAccessException("Utilisateur introuvable.");

        stored.Revoke();
        await _context.SaveChangesAsync();

        var pair = await IssueAuthAsync(user);
        stored.Revoke(SecurityHelper.Sha256Hex(pair.RefreshToken!));
        await _context.SaveChangesAsync();

        return pair;
    }

    /// <summary>Révoque un refresh token (déconnexion).</summary>
    public async Task LogoutAsync(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var hash = SecurityHelper.Sha256Hex(refreshToken);
        var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (stored is not null)
        {
            stored.Revoke();
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Demande de réinitialisation du mot de passe : code à 6 chiffres envoyé par WhatsApp
    /// (best-effort). Toujours 200 (pas d'énumération de numéros).
    /// </summary>
    public async Task RequestPasswordResetAsync(string? phoneNumber)
    {
        var user = await FindUserByPhoneAnyRoleAsync(phoneNumber);
        if (user is null) return;

        var code = SecurityHelper.GenerateNumericCode();
        user.SetResetCode(SecurityHelper.Sha256Hex(code), DateTime.UtcNow.AddMinutes(15));
        await _context.SaveChangesAsync();

        try
        {
            await _whatsApp.SendTextAsync(user, $"🔐 Code de réinitialisation WAZAP : {code} (valable 15 minutes).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Envoi du code de réinitialisation impossible pour {User}.", user.Username);
        }
    }

    /// <summary>Valide le code reçu puis change le mot de passe.</summary>
    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await FindUserByPhoneAnyRoleAsync(request.PhoneNumber)
            ?? throw new InvalidOperationException("Aucun compte associé à ce numéro.");

        if (!user.HasActiveResetCode()
            || user.ResetCodeHash is null
            || !SecurityHelper.FixedTimeEquals(user.ResetCodeHash, SecurityHelper.Sha256Hex(request.Code.Trim())))
        {
            throw new InvalidOperationException("Code invalide ou expiré.");
        }

        if (request.NewPassword.Length < 8)
            throw new ArgumentException("Le nouveau mot de passe doit contenir au moins 8 caractères.");

        user.ChangePassword(_passwordHasher.Hash(request.NewPassword));
        user.ClearResetCode();
        user.ResetLoginFailures();
        await _context.SaveChangesAsync();
        await RevokeAllRefreshTokensAsync(user.Id);
    }

    /// <summary>Étape 2 d'un login 2FA : vérifie le code TOTP puis émet les jetons.</summary>
    public async Task<AuthResponse> VerifyTwoFactorAsync(TwoFactorVerifyRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username)
            ?? throw new UnauthorizedAccessException("Identifiants invalides.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Identifiants invalides.");

        if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.TwoFactorSecret))
            throw new UnauthorizedAccessException("La double authentification n'est pas active.");

        if (!Totp.Verify(user.TwoFactorSecret, request.Code))
            throw new UnauthorizedAccessException("Code de validation invalide.");

        if (user.FailedLoginAttempts != 0 || user.LockedUntilUtc is not null)
        {
            user.ResetLoginFailures();
            await _context.SaveChangesAsync();
        }

        return await IssueAuthAsync(user);
    }

    /// <summary>Prépare la 2FA : génère un secret TOTP (non encore activé).</summary>
    public async Task<(string Secret, string OtpauthUri)> SetupTwoFactorAsync(Guid userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        var secret = Totp.GenerateSecret();
        return (secret, Totp.BuildOtpauthUri("WAZAP", user.Username, secret));
    }

    /// <summary>Active la 2FA après vérification du premier code.</summary>
    public async Task EnableTwoFactorAsync(Guid userId, string code, string secret)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (!Totp.Verify(secret, code))
            throw new InvalidOperationException("Code de validation invalide.");

        user.EnableTwoFactor(secret);
        await _context.SaveChangesAsync();
    }

    /// <summary>Désactive la 2FA (vérification du code requise).</summary>
    public async Task DisableTwoFactorAsync(Guid userId, string code)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.TwoFactorSecret))
            throw new InvalidOperationException("La double authentification n'est pas active.");

        if (!Totp.Verify(user.TwoFactorSecret, code))
            throw new InvalidOperationException("Code de validation invalide.");

        user.DisableTwoFactor();
        await _context.SaveChangesAsync();
    }

    private async Task RevokeAllRefreshTokensAsync(Guid userId)
    {
        var active = await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
            .ToListAsync();

        if (active.Count == 0) return;

        foreach (var token in active) token.Revoke();
        await _context.SaveChangesAsync();
    }

    private async Task<User?> FindUserByPhoneAnyRoleAsync(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return null;

        var users = await _context.Users.AsNoTracking()
            .Where(u => u.PhoneNumber != null)
            .ToListAsync();

        return users.FirstOrDefault(u =>
            PhoneNumberNormalizer.SameSubscriber(u.PhoneNumber, phoneNumber));
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
                "1. Une commande à livrer ? Envoyez LIVRAISON + l'adresse du client (ou passez par votre espace)\n" +
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
