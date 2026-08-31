using System.Security.Claims;
using Wazap.Application.Abstractions;
using Wazap.Domain.Enums;

namespace Wazap.API.Services;

public sealed class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? Id
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null)
                return null;

            var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? user.FindFirstValue("sub");
            return Guid.TryParse(value, out var guid) ? guid : null;
        }
    }

    public UserRole? Role
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null)
                return null;

            var value = user.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role) ? role : null;
        }
    }
}
