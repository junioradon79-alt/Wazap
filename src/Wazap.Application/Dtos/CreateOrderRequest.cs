namespace Wazap.Application.Dtos;

public class CreateOrderRequest
{
    public string ClientName { get; set; } = default!;
    public string ClientWhatsAppNumber { get; set; } = default!;
    public string VendorWhatsAppNumber { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Amount { get; set; }
}
