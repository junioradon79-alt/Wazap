using Wazap.Domain.Entities;

namespace Wazap.Application.Abstractions;

public interface IJwtTokenGenerator
{
    string Generate(User user);
}
