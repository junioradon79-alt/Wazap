namespace Wazap.Application.Dtos;

public sealed record OrderCreatedNotification(
    Guid OrderId,
    string ClientName,
    string ClientWhatsAppNumber,
    string VendorWhatsAppNumber,
    string Description,
    decimal Amount);
