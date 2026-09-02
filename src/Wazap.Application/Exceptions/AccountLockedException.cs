namespace Wazap.Application.Exceptions;

/// <summary>
/// Compte temporairement verrouillé après trop d'échecs de connexion (anti force brute).
/// Mappée en HTTP 423 Locked par le gestionnaire global d'exceptions.
/// </summary>
public sealed class AccountLockedException : Exception
{
    public AccountLockedException(DateTime lockedUntilUtc)
        : base($"Compte verrouillé jusqu'à {lockedUntilUtc:HH:mm:ss} UTC (trop de tentatives). Réessayez plus tard.")
    {
        LockedUntilUtc = lockedUntilUtc;
    }

    public DateTime LockedUntilUtc { get; }
}
