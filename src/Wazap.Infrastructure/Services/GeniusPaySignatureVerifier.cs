using System.Security.Cryptography;
using System.Text;

namespace Wazap.Infrastructure.Services
{
    /// <summary>
    /// Vérification de la signature des webhooks GeniusPay :
    /// signature = HMAC-SHA256(timestamp + "." + payload, webhook_secret).
    /// Vérifie aussi l'anti-rejeu (timestamp max 5 min).
    /// </summary>
    public static class GeniusPaySignatureVerifier
    {
        public static bool IsValid(
            string? payload,
            string? signature,
            string? timestamp,
            string secret,
            int maxAgeSeconds = 300)
        {
            if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
                return false;
            if (string.IsNullOrEmpty(secret))
                return false;

            if (!long.TryParse(timestamp, out var sentAt))
                return false;

            if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - sentAt) > maxAgeSeconds)
                return false;

            var expected = ComputeHmacSha256(timestamp + "." + payload, secret);
            return FixedTimeEquals(expected, signature);
        }

        public static string ComputeHmacSha256(string data, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(actual);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
    }
}
