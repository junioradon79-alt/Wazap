using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos;

public class UpdateStatusRequest
{
    public OrderStatus Status { get; set; }
    public string? RiderWhatsAppNumber { get; set; }
}
