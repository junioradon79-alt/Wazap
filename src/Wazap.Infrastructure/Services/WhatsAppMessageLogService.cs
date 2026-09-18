using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;

namespace Wazap.Infrastructure.Services;

public class WhatsAppMessageLogService : IWhatsAppMessageLogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppMessageLogService> _logger;

    public WhatsAppMessageLogService(
        IServiceScopeFactory scopeFactory,
        ILogger<WhatsAppMessageLogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task LogOutboundAsync(
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
        CancellationToken ct = default)
    {
        try
        {
            var category = WhatsAppCostCalculator.DetermineCategory(templateName);
            var status = success ? "Sent" : "Failed";
            var direction = "Outbound";
            var messageType = string.IsNullOrWhiteSpace(templateName) ? "Text" : "Template";
            var cost = WhatsAppCostCalculator.CalculateEstimatedCost(category, direction, status, DateTime.UtcNow);

            var log = new WhatsAppMessageLog(
                recipientPhone: toPhoneNumber,
                direction: direction,
                messageType: messageType,
                provider: provider,
                status: status,
                templateName: templateName,
                category: category,
                providerMessageId: providerMessageId,
                errorCode: errorCode,
                errorMessage: errorMessage,
                estimatedCostFcfa: cost,
                orderId: orderId,
                recipientUserId: recipientUserId,
                createdAt: DateTime.UtcNow);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.WhatsAppMessageLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // L'audit est au « best-effort » et ne doit JAMAIS faire échouer les flux d'envoi WhatsApp
            _logger.LogWarning(ex, "Échec de l'enregistrement de l'audit WhatsApp sortant pour {Phone}.", toPhoneNumber);
        }
    }

    public async Task LogInboundAsync(
        string fromPhoneNumber,
        string? messageText,
        string messageType,
        string provider,
        string? providerMessageId = null,
        Guid? orderId = null,
        Guid? senderUserId = null,
        CancellationToken ct = default)
    {
        try
        {
            const string category = "service";
            const string direction = "Inbound";
            const string status = "Received";
            var cost = WhatsAppCostCalculator.CalculateEstimatedCost(category, direction, status, DateTime.UtcNow);

            var log = new WhatsAppMessageLog(
                recipientPhone: fromPhoneNumber, // Le numéro de contact WhatsApp
                direction: direction,
                messageType: messageType,
                provider: provider,
                status: status,
                templateName: null,
                category: category,
                providerMessageId: providerMessageId,
                errorCode: null,
                errorMessage: null,
                estimatedCostFcfa: cost,
                orderId: orderId,
                recipientUserId: senderUserId,
                createdAt: DateTime.UtcNow);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.WhatsAppMessageLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'enregistrement de l'audit WhatsApp entrant pour {Phone}.", fromPhoneNumber);
        }
    }

    public async Task<IReadOnlyList<WhatsAppMessageLogDto>> GetRecentLogsAsync(
        int count = 100,
        Guid? orderId = null,
        string? recipientPhone = null,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var query = db.WhatsAppMessageLogs.AsNoTracking().AsQueryable();

        if (orderId.HasValue)
            query = query.Where(l => l.OrderId == orderId.Value);

        if (!string.IsNullOrWhiteSpace(recipientPhone))
            query = query.Where(l => l.RecipientPhone.Contains(recipientPhone));

        var maxCount = Math.Clamp(count, 1, 500);

        var entities = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(maxCount)
            .ToListAsync(ct);

        return entities.Select(l => new WhatsAppMessageLogDto(
            l.Id,
            l.OrderId,
            l.RecipientUserId,
            l.RecipientPhone,
            l.SenderPhone,
            l.Direction,
            l.MessageType,
            l.TemplateName,
            l.Category,
            l.Provider,
            l.ProviderMessageId,
            l.Status,
            l.ErrorCode,
            l.ErrorMessage,
            l.EstimatedCostFcfa,
            l.CreatedAt)).ToList();
    }

    public async Task<WhatsAppCostSummaryDto> GetCostSummaryAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var query = db.WhatsAppMessageLogs.AsNoTracking().AsQueryable();

        if (fromUtc.HasValue)
            query = query.Where(l => l.CreatedAt >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(l => l.CreatedAt <= toUtc.Value);

        var logs = await query.ToListAsync(ct);

        var totalMessages = logs.Count;
        var outboundCount = logs.Count(l => l.Direction == "Outbound");
        var inboundCount = logs.Count(l => l.Direction == "Inbound");
        var failedCount = logs.Count(l => l.Status == "Failed");
        var totalCost = logs.Sum(l => l.EstimatedCostFcfa ?? 0m);

        var byCategory = logs
            .GroupBy(l => l.Category ?? "inconnu")
            .ToDictionary(g => g.Key, g => g.Count());

        var costByCategory = logs
            .GroupBy(l => l.Category ?? "inconnu")
            .ToDictionary(g => g.Key, g => g.Sum(l => l.EstimatedCostFcfa ?? 0m));

        var distinctOrdersWithLogs = logs
            .Where(l => l.OrderId.HasValue)
            .Select(l => l.OrderId!.Value)
            .Distinct()
            .Count();

        var avgCostPerOrder = distinctOrdersWithLogs > 0
            ? Math.Round(totalCost / distinctOrdersWithLogs, 2)
            : 0m;

        return new WhatsAppCostSummaryDto(
            TotalMessages: totalMessages,
            OutboundCount: outboundCount,
            InboundCount: inboundCount,
            FailedCount: failedCount,
            TotalEstimatedCostFcfa: Math.Round(totalCost, 2),
            MessagesByCategory: byCategory,
            CostByCategory: costByCategory,
            AverageCostPerOrder: avgCostPerOrder);
    }
}
