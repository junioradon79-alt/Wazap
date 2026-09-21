using System.Text;
using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Commandes texte du <b>livreur</b> qui ne concernent pas une course en cours (P2 / C-13 :
/// extraites du contrôleur webhook, comme <see cref="RiderDeliveryCommands"/> et
/// <see cref="VendorTextCommands"/>) :
///
///  - <c>DASHBOARD</c> / <c>STATS</c> / <c>SOLDE</c> : vue synthétique (statut, zone, gains, note, défi Redmi 15C) ;
///  - <c>DISPO</c> / <c>INDISPO</c> : se mettre en ligne / hors ligne (détermine s'il reçoit
///    des offres — un livreur « hors ligne » qui reçoit des courses est un bug perçu) ;
///  - <c>AVIS</c> / <c>REPONDRE &lt;n°&gt; &lt;texte&gt;</c> : consulter ses avis et y répondre ;
///  - <c>PROGRAMME</c> (et variantes) : progression du programme « Ambassadeur WAZAP ».
///
/// ZONE reste géré par le contrôleur : c'est la seule commande partagée entre les deux rôles
/// (livreur → <c>RiderService</c>, tout autre rôle → <c>VendorService</c> comme dans la version
/// d'origine), et la déplacer aurait changé le traitement des rôles inattendus.
/// </summary>
public sealed class RiderTextCommands
{
    private readonly ApplicationDbContext? _context;
    private readonly RiderService _riderService;
    private readonly RiderRatingService _riderRatings;
    private readonly RiderProgramService _riderProgram;

    public RiderTextCommands(
        RiderService riderService,
        RiderRatingService riderRatings,
        RiderProgramService riderProgram,
        ApplicationDbContext? context = null)
    {
        _riderService = riderService;
        _riderRatings = riderRatings;
        _riderProgram = riderProgram;
        _context = context;
    }

    /// <summary>
    /// Reconnaît les messages pris en charge ici. Les commandes d'avis sont reconnues par le
    /// service de notation lui-même (analyse du texte BRUT : accents et espaces), pour qu'une
    /// évolution de leur syntaxe ne se désynchronise pas du routage.
    /// </summary>
    public static bool Matches(string upperText, string rawText)
        => upperText is "DISPO" or "INDISPO"
           || upperText is "DASHBOARD" or "STATS" or "STATISTIQUES" or "SOLDE" or "COMPTE" or "TABLEAU DE BORD"
           || upperText is "PROGRAMME" or "MA PROGRAMME" or "AMBASSADEUR" or "RECOMPENSE"
           || RiderRatingService.IsMyRatingsCommand(rawText)
           || RiderRatingService.IsReplyCommand(rawText);

    /// <summary>Traite la commande. <paramref name="reply"/> envoie la réponse WhatsApp au livreur.</summary>
    public async Task HandleAsync(User user, string rawText, Func<User, string, Task> reply)
    {
        var upper = rawText.Trim().ToUpperInvariant();

        if (upper is "DASHBOARD" or "STATS" or "STATISTIQUES" or "SOLDE" or "COMPTE" or "TABLEAU DE BORD")
        {
            await reply(user, await BuildDashboardTextAsync(user));
            return;
        }

        if (upper == "DISPO")
        {
            await _riderService.SetAvailabilityAsync(user.Id, true);
            await reply(user, "✅ Vous êtes en ligne.");
            return;
        }

        if (upper == "INDISPO")
        {
            await _riderService.SetAvailabilityAsync(user.Id, false);
            await reply(user, "🚫 Vous êtes hors ligne.");
            return;
        }

        // Réputation livreur : « AVIS » liste les avis reçus (numérotés) et
        // « REPONDRE <n°> <texte> » enregistre la réponse du livreur à l'avis n°.
        if (RiderRatingService.IsMyRatingsCommand(rawText))
        {
            await reply(user, await _riderRatings.ListMyRatingsTextAsync(user.Id));
            return;
        }

        if (RiderRatingService.IsReplyCommand(rawText))
        {
            if (!RiderRatingService.TryParseReplyCommand(rawText, out var index, out var text))
            {
                await reply(user,
                    "❓ Format : REPONDRE <n°> <votre message>.\n" +
                    "Consultez d'abord vos avis avec AVIS, puis répondez par exemple : REPONDRE 1 Merci pour votre confiance !");
                return;
            }

            await reply(user, await _riderRatings.ReplyAsync(user.Id, index, text));
            return;
        }

        // Programme « Ambassadeur WAZAP » : le livreur consulte sa progression.
        var progress = await _riderProgram.BuildProgressAsync(user.Id);
        await reply(user, progress is null
            ? "ℹ️ Le programme Ambassadeur n'est pas actif pour le moment."
            : RiderProgramService.BuildProgressText(progress));
    }

    private async Task<string> BuildDashboardTextAsync(User user)
    {
        var progress = await _riderProgram.BuildProgressAsync(user.Id);

        var zone = string.IsNullOrWhiteSpace(user.Zone) ? "Non définie (tapez ZONE <quartier>)" : user.Zone;
        var status = user.IsAvailable ? "🟢 EN LIGNE (DISPO)" : "🔴 HORS LIGNE (INDISPO)";
        var certified = progress?.Certified == true ? "✅ Identité CNI vérifiée" : "⏳ Non certifié (envoyez photo CNI)";
        var ratingText = progress?.AverageRating is { } r ? $"★ {r:0.0}/5" : "Nouveau livreur";

        var deliveriesCount = progress?.Deliveries ?? 0;
        var totalEarnings = 0m;
        var activeOrderText = "Aucune (prêt à recevoir)";

        if (_context is not null)
        {
            try
            {
                totalEarnings = await _context.Orders.AsNoTracking()
                    .Where(o => o.RiderUserId == user.Id && o.Status == OrderStatus.Delivered)
                    .SumAsync(o => (decimal?)o.DeliveryFee) ?? 0m;

                var active = await _context.Orders.AsNoTracking()
                    .Where(o => o.RiderUserId == user.Id && (o.Status == OrderStatus.RiderAssigned || o.Status == OrderStatus.InTransit))
                    .Select(o => new { o.Id, o.Status })
                    .FirstOrDefaultAsync();

                if (active is not null)
                {
                    var code = active.Id.ToString("N")[..8].ToUpperInvariant();
                    var step = active.Status == OrderStatus.RiderAssigned ? "Colis à récupérer chez commerçant" : "En cours de livraison au client";
                    activeOrderText = $"#{code} ({step})";
                }
            }
            catch
            {
                // Repli sans erreur
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("📊 *TABLEAU DE BORD LIVREUR* — WAZAP");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"👤 *{user.Username}* ({(user.PhoneNumber ?? "WhatsApp")})");
        sb.AppendLine($"📍 Zone active : {zone}");
        sb.AppendLine($"📶 Statut : {status}");
        sb.AppendLine($"🛡️ Colis Sûr : {certified}");
        sb.AppendLine($"⭐ Note client : {ratingText}");
        sb.AppendLine();
        sb.AppendLine("💰 *ACTIVITÉ & GAINS*");
        sb.AppendLine($"• Livraisons effectuées : {deliveriesCount} courses");
        if (totalEarnings > 0)
            sb.AppendLine($"• Gains cumulés : {totalEarnings:N0} FCFA");
        sb.AppendLine($"• Course en cours : {activeOrderText}");

        if (progress is not null)
        {
            var delivBar = BuildBar(progress.Deliveries, progress.DeliveriesTarget);
            var refBar = BuildBar(progress.ValidatedReferrals, progress.ReferralsTarget);

            sb.AppendLine();
            sb.AppendLine("🎁 *DÉFI REDMI 15C (Ambassadeur)*");
            sb.AppendLine($"• Livraisons : {progress.Deliveries}/{progress.DeliveriesTarget} {delivBar}");
            sb.AppendLine($"• Filleuls validés : {progress.ValidatedReferrals}/{progress.ReferralsTarget} {refBar}");
            sb.AppendLine($"• Récompense : 📱 {progress.RewardLabel}");
            if (!string.IsNullOrWhiteSpace(user.ReferralCode))
            {
                sb.AppendLine($"• Code parrainage : *{user.ReferralCode}*");
                sb.AppendLine("_(Partagez ce code à d'autres livreurs pour débloquer votre smartphone !)_");
            }
        }

        sb.AppendLine();
        sb.AppendLine("⚡ *ACTIONS RAPIDES*");
        sb.AppendLine("• DISPO / INDISPO : se connecter / se déconnecter");
        sb.AppendLine("• ZONE <quartier> : changer de commune");
        sb.AppendLine("• AVIS : lire vos notes et commentaires");
        sb.AppendLine("• AIDE : menu complet");

        return sb.ToString().TrimEnd();
    }

    private static string BuildBar(int current, int target, int totalBlocks = 8)
    {
        if (target <= 0) return string.Empty;
        var ratio = Math.Clamp((double)current / target, 0.0, 1.0);
        var filled = (int)Math.Round(ratio * totalBlocks);
        var empty = totalBlocks - filled;
        var pct = (int)Math.Round(ratio * 100);
        return $"[{new string('█', filled)}{new string('░', empty)}] {pct}%";
    }
}

