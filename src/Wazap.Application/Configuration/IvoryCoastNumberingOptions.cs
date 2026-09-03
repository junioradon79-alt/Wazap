namespace Wazap.Application.Configuration;

/// <summary>
/// Table de conversion de la numérotation ivoirienne (section « IvoryCoastNumbering »).
/// </summary>
/// <remarks>
/// Contexte : depuis la réforme ARTCI de 2021, la Côte d'Ivoire est passée de numéros à
/// 8 chiffres à des numéros à 10 chiffres. Le nouveau numéro = nouveau préfixe opérateur
/// (2 chiffres) + les 8 chiffres d'origine CONSERVÉS tels quels.
/// Exemple réel validé : ancien « 08323366 » → nouveau « 07 » + « 08323366 » = « 0708323366 ».
/// <para>
/// La conversion d'un ancien numéro vers le nouveau format est spécifique à chaque opérateur :
/// elle ne peut PAS être devinée sans la table officielle ARTCI (ancien préfixe → nouveau
/// préfixe). Tant que <see cref="Enabled"/> est <c>false</c> ou que la table est vide, aucune
/// conversion n'est appliquée : le comportement actuel (matching SameSubscriber sur les
/// 8 derniers chiffres + auto-réparation via wa_id) est conservé.
/// </para>
/// </remarks>
public sealed class IvoryCoastNumberingOptions
{
    public const string SectionName = "IvoryCoastNumbering";

    /// <summary>
    /// Active la conversion 8 → 10 chiffres à la saisie.
    /// À passer à <c>true</c> UNIQUEMENT quand la table officielle ARTCI est renseignée.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Table officielle ARTCI : clé = préfixe de l'ANCIEN numéro national (8 chiffres,
    /// ex. mobile « 08 »), valeur = préfixe de 2 chiffres du NOUVEAU format à préfixer
    /// aux 8 chiffres conservés (ex. « 07 »). Le préfixe le plus long connu gagne.
    /// </summary>
    public Dictionary<string, string> OldToNewPrefixMap { get; set; } = new();
}
