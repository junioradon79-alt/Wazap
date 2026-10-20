namespace Wazap.Domain.Entities;

/// <summary>
/// Refresh token (rotation) : token opaque conservé hashé, lié à un utilisateur.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? SecurityStamp { get; private set; }

    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, DateTime expiresAtUtc, string? securityStamp = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Le hash du token est requis.", nameof(tokenHash));

        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
        SecurityStamp = securityStamp;
    }

    public bool IsActive => ExpiresAtUtc > DateTime.UtcNow && RevokedAtUtc is null;

    /// <summary>Révoque ce token (rotation) ; éventuellement remplacé par un nouveau.</summary>
    public void Revoke(string? replacedByTokenHash = null)
    {
        RevokedAtUtc = DateTime.UtcNow;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
