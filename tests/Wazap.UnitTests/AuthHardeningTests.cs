using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Durcissement des sessions et de la double authentification.
/// <para>
/// Deux défauts corrigés : ① le jeton d'accès durait 8 h et <b>n'était pas révocable</b> — un
/// jeton volé restait utilisable après un changement de mot de passe ; ② l'activation de la 2FA
/// acceptait le <b>secret envoyé par le client</b>, si bien qu'un porteur de jeton volé pouvait
/// activer la 2FA avec SON secret et verrouiller le compte du propriétaire légitime.
/// </para>
/// </summary>
public class AuthHardeningTests
{
    private const string JwtKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static AuthService CreateAuthService(ApplicationDbContext context)
    {
        var sender = new RecordingWhatsAppSender();
        var whatsApp = new WhatsAppOrchestrationService(
            sender, new WhatsAppOptions(), NullLogger<WhatsAppOrchestrationService>.Instance);

        return new AuthService(
            context,
            new FakePasswordHasher(),
            new JwtTokenGenerator(new ConfigStub(
                ("Jwt:Key", JwtKey), ("Jwt:Issuer", "WazapAPI"), ("Jwt:Audience", "WazapUsers"))),
            whatsApp,
            new SecurityOptions { MaxFailedLoginAttempts = 3, LockoutMinutes = 15 },
            new TrialOptions { Enabled = false },
            new IvoryCoastNumberingOptions { Enabled = false },
            NullLogger<AuthService>.Instance);
    }

    private static async Task<(ApplicationDbContext Context, User User)> NewUserAsync(string password = "Motdepasse123!")
    {
        var context = TestInfra.NewContext("auth-" + Guid.NewGuid().ToString("N"));
        var user = new User("admin-test", new FakePasswordHasher().Hash(password), UserRole.Admin, "+2250700000900");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return (context, user);
    }

    // ------------------------------------------------------------------ 2FA : le secret

    [Fact]
    public async Task SetupTwoFactor_ConserveLeSecretCoteServeur_SansActiverLa2Fa()
    {
        var (context, user) = await NewUserAsync();
        var service = CreateAuthService(context);

        var (secret, uri) = await service.SetupTwoFactorAsync(user.Id);

        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.Contains("otpauth://totp/", uri);

        var stored = await context.Users.FindAsync(user.Id);
        Assert.False(stored!.TwoFactorEnabled);
        Assert.True(stored.HasPendingTwoFactor());
        Assert.Equal(secret, stored.TwoFactorPendingSecret);
    }

    [Fact]
    public async Task EnableTwoFactor_AvecLeCodeDuSecretEnAttente_ActiveLa2Fa()
    {
        var (context, user) = await NewUserAsync();
        var service = CreateAuthService(context);

        var (secret, _) = await service.SetupTwoFactorAsync(user.Id);
        await service.EnableTwoFactorAsync(user.Id, Totp.CurrentCode(secret));

        var stored = await context.Users.FindAsync(user.Id);
        Assert.True(stored!.TwoFactorEnabled);
        Assert.Equal(secret, stored.TwoFactorSecret);
        Assert.Null(stored.TwoFactorPendingSecret); // consommé
    }

    [Fact]
    public async Task EnableTwoFactor_AvecUnSecretChoisiParLeClient_EstRefuse()
    {
        // L'ANCIENNE faille : le client envoyait le secret qu'il voulait. Un porteur de jeton
        // volé activait la 2FA avec SON secret et verrouillait le compte du propriétaire.
        var (context, user) = await NewUserAsync();
        var service = CreateAuthService(context);

        var (_, _) = await service.SetupTwoFactorAsync(user.Id);          // secret légitime en attente
        var secretAttaquant = Totp.GenerateSecret();                       // secret de l'attaquant

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnableTwoFactorAsync(user.Id, Totp.CurrentCode(secretAttaquant)));

        var stored = await context.Users.FindAsync(user.Id);
        Assert.False(stored!.TwoFactorEnabled);
    }

    [Fact]
    public async Task EnableTwoFactor_SansEtapeDeConfiguration_EstRefuse()
    {
        var (context, user) = await NewUserAsync();
        var service = CreateAuthService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnableTwoFactorAsync(user.Id, Totp.CurrentCode(Totp.GenerateSecret())));
    }

    [Fact]
    public async Task VerifyTwoFactor_ApresTroisCodesFaux_VerrouilleLeCompte()
    {
        var (context, user) = await NewUserAsync();
        var service = CreateAuthService(context);

        var (secret, _) = await service.SetupTwoFactorAsync(user.Id);
        await service.EnableTwoFactorAsync(user.Id, Totp.CurrentCode(secret));

        // Trois codes invalides (le verrouillage est configuré à 3 tentatives dans ce test).
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VerifyTwoFactorAsync(
                new TwoFactorVerifyRequest("admin-test", "Motdepasse123!", "000000")));
        }

        // La 3ᵉ échec verrouille : l'erreur change de nature (423 côté API).
        await Assert.ThrowsAsync<AccountLockedException>(() => service.VerifyTwoFactorAsync(
            new TwoFactorVerifyRequest("admin-test", "Motdepasse123!", "000000")));

        var stored = await context.Users.FindAsync(user.Id);
        Assert.NotNull(stored!.LockedUntilUtc);
    }

    // ------------------------------------------------------- Révocation des jetons d'accès

    [Fact]
    public void Jwt_PorteL_EmpreinteDeSecurite_EtUneDureeCourte()
    {
        var user = new User("vendeur", "hash", UserRole.Vendor, "+2250700000901");
        var generator = new JwtTokenGenerator(new ConfigStub(
            ("Jwt:Key", JwtKey), ("Jwt:Issuer", "WazapAPI"), ("Jwt:Audience", "WazapUsers")));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(generator.Generate(user));

        var stamp = token.Claims.FirstOrDefault(c => c.Type == JwtTokenGenerator.SecurityStampClaim);
        Assert.NotNull(stamp);
        Assert.Equal(user.SecurityStamp, stamp!.Value);

        // 30 minutes par défaut, et non plus 8 heures.
        var lifetime = token.ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, 25, 35);
    }

    [Fact]
    public void ChangementDeMotDePasse_ChangeL_EmpreinteDoncRevoqueLesJetons()
    {
        var user = new User("vendeur", "hash", UserRole.Vendor, "+2250700000902");
        var avant = user.SecurityStamp;

        user.ChangePassword("nouveau-hash");

        Assert.NotEqual(avant, user.SecurityStamp);
    }

    [Fact]
    public void ActivationPuisDesactivation2Fa_RevoquentLesSessions()
    {
        var user = new User("admin", "hash", UserRole.Admin, "+2250700000903");

        var avantActivation = user.SecurityStamp;
        user.SetPendingTwoFactor(Totp.GenerateSecret(), DateTime.UtcNow.AddMinutes(10));
        user.EnableTwoFactorFromPending();
        Assert.NotEqual(avantActivation, user.SecurityStamp);

        var avantDesactivation = user.SecurityStamp;
        user.DisableTwoFactor();
        Assert.NotEqual(avantDesactivation, user.SecurityStamp);
    }

    [Fact]
    public async Task SetupTwoFactor_Expire_ApresLeDelai()
    {
        var (context, user) = await NewUserAsync();

        // Secret en attente déjà expiré : la configuration doit être relancée.
        user.SetPendingTwoFactor(Totp.GenerateSecret(), DateTime.UtcNow.AddMinutes(-1));
        await context.SaveChangesAsync();

        var service = CreateAuthService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnableTwoFactorAsync(user.Id, Totp.CurrentCode(user.TwoFactorPendingSecret!)));
    }

    // ---------------------------------------- Vérification de bout en bout (application réelle)

    [Fact]
    public async Task JetonEmis_EstRefuse_ApresRevocationDeLaSession()
    {
        using var factory = new WazapAppFactory();
        var hasher = new PasswordHasher();
        var userId = Guid.NewGuid();

        // Compte de test inséré directement (l'application de test ne seed aucun compte).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new User("admin-revocation", hasher.Hash("Motdepasse123!"), UserRole.Admin, "+2250700000904");
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = "admin-revocation", Password = "Motdepasse123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth?.Token);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        // Le jeton fonctionne…
        var before = await client.PostAsync("/api/auth/2fa/setup", content: null);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // …puis une révocation (changement de mot de passe, réinitialisation, 2FA) l'invalide
        // IMMÉDIATEMENT : c'était tout l'objet de l'empreinte de sécurité.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.RevokeSessions();
            await db.SaveChangesAsync();
        }

        var after = await client.PostAsync("/api/auth/2fa/setup", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
