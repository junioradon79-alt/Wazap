using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class AuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request)
    {
        var exists = await _context.Users.AnyAsync(u => u.Username == request.Username);
        if (exists)
            throw new InvalidOperationException("Ce nom d'utilisateur est déjà utilisé.");

        var user = new User(request.Username, _passwordHasher.Hash(request.Password), request.Role, PhoneNumberNormalizer.Normalize(request.PhoneNumber));
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

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
        return new AuthResponse(token, user.Username, user.Role.ToString());
    }
}
