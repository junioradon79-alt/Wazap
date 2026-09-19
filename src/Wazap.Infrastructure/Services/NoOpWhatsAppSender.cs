using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Implémentation no-op utilisée quand aucune passerelle WhatsApp n'est active
/// (évite de faire crasher l'authentification ou les opérations métier hors envoi).
/// </summary>
public sealed class NoOpWhatsAppSender : IWhatsAppSender
{
    private readonly ILogger<NoOpWhatsAppSender> _logger;

    public NoOpWhatsAppSender(ILogger<NoOpWhatsAppSender> logger) => _logger = logger;

    public Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        _logger.LogWarning("[WhatsApp NoOp] Aucun fournisseur WhatsApp configuré. Template '{Template}' vers {Phone} non distribué.", templateName, toPhoneNumber);
        return Task.CompletedTask;
    }

    public Task SendTextMessageAsync(string toPhoneNumber, string message, CancellationToken ct = default)
    {
        _logger.LogWarning("[WhatsApp NoOp] Aucun fournisseur WhatsApp configuré. Message texte vers {Phone} non distribué : {Message}", toPhoneNumber, message);
        return Task.CompletedTask;
    }
}

public sealed class NoOpWhatsAppMediaDownloader : IWhatsAppMediaDownloader
{
    public Task<(byte[] Content, string FileName)?> TryDownloadAsync(string? url, string? mediaId, string? mimeType, CancellationToken ct = default)
        => Task.FromResult<(byte[] Content, string FileName)?>(null);
}
