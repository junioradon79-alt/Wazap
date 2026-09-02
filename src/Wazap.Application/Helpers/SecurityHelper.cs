using System.Security.Cryptography;
using System.Text;

namespace Wazap.Application.Helpers;

/// <summary>
/// Helpers sécurité : hachage SHA-256, génération de tokens opaques et TOTP (RFC 6238,
/// 6 chiffres, fenêtre 30 s) — aucune dépendance externe (compatible Google Authenticator).
/// </summary>
public static class SecurityHelper
{
    /// <summary>Hash SHA-256 hexadécimal (tokens de rafraîchissement, codes de reset).</summary>
    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Génère un token opaque (32 octets, base64url) — jamais stocké en clair.</summary>
    public static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Code de vérification à 6 chiffres aléatoire.</summary>
    public static string GenerateNumericCode(int length = 6)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes) sb.Append((char)('0' + (b % 10)));
        return sb.ToString();
    }

    /// <summary>Comparaison à temps constant.</summary>
    public static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

public static class Totp
{
    private const int Digits = 6;
    private const int StepSeconds = 30;

    /// <summary>Secret base32 (20 octets) pour une app d'authentification.</summary>
    public static string GenerateSecret()
        => Base32Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>URI otpauth:// pour ajout dans Google Authenticator / Authy.</summary>
    public static string BuildOtpauthUri(string issuer, string accountName, string secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    /// <summary>Code TOTP courant pour un secret donné.</summary>
    public static string CurrentCode(string secret)
    {
        var counter = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        return ComputeCode(secret, counter);
    }

    /// <summary>Vérifie un code avec une tolérance d'une fenêtre avant/après.</summary>
    public static bool Verify(string secret, string? code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var counter = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        for (var i = -window; i <= window; i++)
        {
            if (SecurityHelper.FixedTimeEquals(ComputeCode(secret, counter + i), code.Trim()))
                return true;
        }
        return false;
    }

    private static string ComputeCode(string secret, long counter)
    {
        var key = Base32Decode(secret);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString().PadLeft(Digits, '0');
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0) sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = new string(input.ToUpperInvariant().Where(c => c != '=' && c != ' ' && c != '-').ToArray());
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in cleaned)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) continue;
            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((value >> bits) & 0xFF));
            }
        }
        return output.ToArray();
    }
}
