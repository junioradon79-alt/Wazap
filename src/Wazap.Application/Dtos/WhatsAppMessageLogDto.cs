namespace Wazap.Application.Dtos;

public sealed record WhatsAppMessageLogDto(
    Guid Id,
    Guid? OrderId,
    Guid? RecipientUserId,
    string RecipientPhone,
    string? SenderPhone,
    string Direction,
    string MessageType,
    string? TemplateName,
    string? Category,
    string Provider,
    string? ProviderMessageId,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    decimal? EstimatedCostFcfa,
    DateTime CreatedAt);

public sealed record WhatsAppCostSummaryDto(
    int TotalMessages,
    int OutboundCount,
    int InboundCount,
    int FailedCount,
    decimal TotalEstimatedCostFcfa,
    Dictionary<string, int> MessagesByCategory,
    Dictionary<string, decimal> CostByCategory,
    decimal AverageCostPerOrder);
