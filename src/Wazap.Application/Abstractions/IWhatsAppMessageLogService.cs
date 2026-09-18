using Wazap.Application.Dtos;

namespace Wazap.Application.Abstractions;

public interface IWhatsAppMessageLogService
{
    Task LogOutboundAsync(
        string toPhoneNumber,
        string? templateName,
        string? messageText,
        string provider,
        bool success,
        string? providerMessageId = null,
        int? errorCode = null,
        string? errorMessage = null,
        Guid? orderId = null,
        Guid? recipientUserId = null,
        CancellationToken ct = default);

    Task LogInboundAsync(
        string fromPhoneNumber,
        string? messageText,
        string messageType,
        string provider,
        string? providerMessageId = null,
        Guid? orderId = null,
        Guid? senderUserId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<WhatsAppMessageLogDto>> GetRecentLogsAsync(
        int count = 100,
        Guid? orderId = null,
        string? recipientPhone = null,
        CancellationToken ct = default);

    Task<WhatsAppCostSummaryDto> GetCostSummaryAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken ct = default);
}
