using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private User() { }

    public User(string username, string passwordHash, UserRole role, string? phoneNumber = null)
    {
        Id = Guid.NewGuid();
        Username = username;
        PasswordHash = passwordHash;
        Role = role;
        PhoneNumber = phoneNumber;
        CreatedAt = DateTime.UtcNow;
    }
}
