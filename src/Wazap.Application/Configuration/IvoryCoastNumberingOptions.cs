namespace Wazap.Application.Configuration;

/// <summary>
/// Table de conversion de la numérotation ivoirienne (section « IvoryCoastNumbering »).
/// </summary>
/// <remarks>
/// Contexte : depuis la réforme ARTCI du 31/01/2021, la Côte d'Ivoire est passée de numéros à
/// 8 chiffres à des numéros à 10 chiffres. Règle officielle : « just use the new operator prefix
/// before the old number » → nouveau numéro = nouveau préfixe opérateur (2 chiffres) + les
/// 8 chiffres d'origine CONSERVÉS tels quels.
/// Exemple réel validé en prod : ancien « 08323366 » → nouveau « 07 » + « 08323366 » = « 0708323366 ».
/// <para>
/// <see cref="OldToNewPrefixMap"/> contient la table par défaut reconstituée du plan ARTCI 2021
/// (source publique, croisée avec les wa_id réels observés) pour les LIGNES MOBILES — seules
/// joignables WhatsApp. Les lignes fixes (anciens préfixes 2x/3x → 27) ne sont volontairement PAS
/// dans la table (pas de WhatsApp). Tant que <see cref="Enabled"/> est <c>false</c>, aucune
/// conversion n'est appliquée : le comportement actuel (matching SameSubscriber sur les 8 derniers
/// chiffres + auto-réparation via wa_id) est conservé.
/// </para>
/// <para>
/// ⚠️ Statut : table DESACTIVEE par défaut. Avant de passer <see cref="Enabled"/> à <c>true</c>,
/// valider la table sur le document officiel ARTCI et faire un test réel (message WhatsApp vers un
/// numéro converti). Surcharge possible par environnement via la section config du même nom.
/// </para>
/// </remarks>
public sealed class IvoryCoastNumberingOptions
{
    public const string SectionName = "IvoryCoastNumbering";

    /// <summary>
    /// Active la conversion 8 → 10 chiffres à la saisie.
    /// À passer à <c>true</c> UNIQUEMENT après validation de la table sur le plan ARTCI officiel.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Table ARTCI (par défaut, surchargeable par config) : clé = préfixe de l'ANCIEN numéro
    /// national (8 chiffres, ex. mobile « 08 »), valeur = préfixe de 2 chiffres du NOUVEAU format
    /// à préfixer aux 8 chiffres conservés (ex. « 07 »). Le préfixe le plus long connu gagne.
    /// </summary>
    public Dictionary<string, string> OldToNewPrefixMap { get; set; } = new()
    {
        // Orange – Côte d'Ivoire → nouveau préfixe 07
        ["07"] = "07", ["08"] = "07", ["09"] = "07",
        ["47"] = "07", ["48"] = "07", ["49"] = "07",
        ["57"] = "07", ["58"] = "07", ["59"] = "07",
        ["77"] = "07", ["78"] = "07",
        ["87"] = "07", ["88"] = "07", ["89"] = "07", ["98"] = "07",

        // MTN – Côte d'Ivoire → nouveau préfixe 05
        ["04"] = "05", ["05"] = "05", ["06"] = "05",
        ["44"] = "05", ["45"] = "05", ["46"] = "05",
        ["55"] = "05", ["56"] = "05",
        ["84"] = "05", ["85"] = "05", ["86"] = "05",

        // Moov Africa CI (ex-Atlantique Telecom) → nouveau préfixe 01
        ["01"] = "01", ["02"] = "01", ["03"] = "01",
        ["40"] = "01", ["42"] = "01"
    };
}
