using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Wazap.Application.Helpers;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class SessionRevalidationProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var stamp = state.User.FindFirstValue(JwtTokenGenerator.SecurityStampClaim);
        if (string.IsNullOrWhiteSpace(stamp)) return false;
        using var scope = scopes.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is not null && !user.IsLocked() && SecurityHelper.FixedTimeEquals(stamp, user.SecurityStamp);
    }
}
