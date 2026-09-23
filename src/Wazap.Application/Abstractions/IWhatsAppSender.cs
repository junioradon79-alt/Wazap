namespace Wazap.Application.Abstractions;

/// <summary>
/// Envoi de messages WhatsApp (Meta Cloud API ou passerelle legacy).
///
/// P2 / C-14 : les deux méthodes acceptent un <see cref="CancellationToken"/>. Il est optionnel
/// pour ne pas alourdir les appels qui n'ont pas de jeton à fournir, mais il doit être transmis
/// partout où il existe : sans lui, un envoi vers une passerelle lente immobilise la requête
/// jusqu'au timeout HTTP (100 s) même si le demandeur a déjà raccroché — c'était le cas du
/// webhook, qui n'utilisait jamais <c>RequestAborted</c>.
/// </summary>
public interface IWhatsAppSender
{
    Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables,
        CancellationToken ct = default);
    Task SendTextMessageAsync(string toPhoneNumber, string message, CancellationToken ct = default);

    /// <summary>
    /// Envoi de boutons cliquables interactifs WhatsApp (Quick Reply buttons, max 3 boutons).
    /// En l'absence de support natif de la passerelle, un repli texte automatique est assuré.
    /// </summary>
    Task SendInteractiveButtonsAsync(
        string toPhoneNumber,
        string bodyText,
        IReadOnlyList<(string Id, string Title)> buttons,
        string? headerText = null,
        string? footerText = null,
        CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(headerText))
            sb.AppendLine(headerText).AppendLine();
        sb.AppendLine(bodyText).AppendLine();
        foreach (var btn in buttons)
            sb.AppendLine($"👉 {btn.Title}");
        if (!string.IsNullOrWhiteSpace(footerText))
            sb.AppendLine().AppendLine($"_{footerText}_");
        return SendTextMessageAsync(toPhoneNumber, sb.ToString().TrimEnd(), ct);
    }

    /// <summary>
    /// Envoi d'une image avec légende sur WhatsApp (visuels produits, cartes vitrines sublimées).
    /// </summary>
    Task SendImageMessageAsync(
        string toPhoneNumber,
        string imageUrl,
        string? caption = null,
        CancellationToken ct = default)
    {
        var msg = string.IsNullOrWhiteSpace(caption) ? imageUrl : $"{caption}\n\n📸 {imageUrl}";
        return SendTextMessageAsync(toPhoneNumber, msg, ct);
    }
}
