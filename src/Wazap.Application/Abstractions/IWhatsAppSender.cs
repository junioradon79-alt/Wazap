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
}
