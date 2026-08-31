using Wazap.Domain.Enums;

namespace Wazap.Application.Dtos;

public class OrderDto
{
    public Guid Id { get; init; }
    public string ClientName { get; init; } = default!;
    public string Description { get; init; } = default!;
    public decimal Amount { get; init; }
    public OrderStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}
