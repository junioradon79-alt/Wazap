using Wazap.Domain.Entities;

namespace Wazap.API.Services;

/// <summary>
/// Commandes texte du <b>livreur</b> qui ne concernent pas une course en cours (P2 / C-13 :
/// extraites du contrôleur webhook, comme <see cref="RiderDeliveryCommands"/> et
/// <see cref="VendorTextCommands"/>) :
///
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
    private readonly RiderService _riderService;
    private readonly RiderRatingService _riderRatings;
    private readonly RiderProgramService _riderProgram;

    public RiderTextCommands(
        RiderService riderService,
        RiderRatingService riderRatings,
        RiderProgramService riderProgram)
    {
        _riderService = riderService;
        _riderRatings = riderRatings;
        _riderProgram = riderProgram;
    }

    /// <summary>
    /// Reconnaît les messages pris en charge ici. Les commandes d'avis sont reconnues par le
    /// service de notation lui-même (analyse du texte BRUT : accents et espaces), pour qu'une
    /// évolution de leur syntaxe ne se désynchronise pas du routage.
    /// </summary>
    public static bool Matches(string upperText, string rawText)
        => upperText is "DISPO" or "INDISPO"
           || upperText is "PROGRAMME" or "MA PROGRAMME" or "AMBASSADEUR" or "RECOMPENSE"
           || RiderRatingService.IsMyRatingsCommand(rawText)
           || RiderRatingService.IsReplyCommand(rawText);

    /// <summary>Traite la commande. <paramref name="reply"/> envoie la réponse WhatsApp au livreur.</summary>
    public async Task HandleAsync(User user, string rawText, Func<User, string, Task> reply)
    {
        var upper = rawText.Trim().ToUpperInvariant();

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
}
