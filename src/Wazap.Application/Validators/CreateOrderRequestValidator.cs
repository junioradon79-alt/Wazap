using FluentValidation;
using Wazap.Application.Dtos;

namespace Wazap.Application.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ClientName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ClientWhatsAppNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.VendorWhatsAppNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
