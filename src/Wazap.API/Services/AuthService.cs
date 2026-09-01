using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class AuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        WhatsAppOrchestrationService whatsApp,
        ILogger<AuthService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _whatsApp = whatsApp;
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

        return new UserDto(user.Id, user.Username, user.Role.ToString());
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Identifiants invalides.");

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
