using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>Liens opaques de quinze minutes, distincts des jetons de renouvellement.</summary>
public static class PasswordlessAccess
{
    public static async Task<string> IssueAsync(ApplicationDbContext db, User user, ClientOptions? options = null)
    {
        var origin = new Uri((options ?? new ClientOptions()).TrackingBaseUrl).GetLeftPart(UriPartial.Authority);
        // Un lien WhatsApp ne peut pas contourner le second facteur ni ouvrir une session administrateur.
        if (user.Role == UserRole.Admin || user.TwoFactorEnabled)
            return origin + "/app/login";
        var token = SecurityHelper.GenerateOpaqueToken();
        db.RefreshTokens.Add(new RefreshToken(user.Id, Hash(token), DateTime.UtcNow.AddMinutes(15), user.SecurityStamp));
        await db.SaveChangesAsync();
        // Le fragment n'est envoyé ni au serveur HTTP ni dans l'en-tête Referer.
        return origin + "/app/login#access=" + token;
    }

    public static string Hash(string token) => SecurityHelper.Sha256Hex("wazap:login-link:" + token);
}
