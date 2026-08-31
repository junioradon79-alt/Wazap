using Wazap.Domain.Enums;

namespace Wazap.Application.Abstractions;

public interface ICurrentUser
{
    Guid? Id { get; }
    UserRole? Role { get; }
}
