using System.Security.Cryptography;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Le mot de passe est requis.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hashed)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(hashed))
            return false;

        var parts = hashed.Split('.');
        if (parts.Length != 2)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expected = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            // Empreinte corrompue : refus, et non exception 500 à la connexion.
            return false;
        }

        // Contrôle de FORME indispensable : une empreinte vide (par exemple la chaîne « . »,
        // qui donne deux tableaux d'octets vides) ferait calculer une dérivée de longueur 0
        // et FixedTimeEquals(vide, vide) renverrait TRUE — n'importe quel mot de passe
        // ouvrirait alors le compte. Une empreinte valide a toujours 16 octets de sel et
        // 32 octets de clé.
        if (salt.Length != SaltSize || expected.Length != KeySize)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
