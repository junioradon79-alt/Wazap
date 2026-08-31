using FluentValidation;
using Wazap.Application.Dtos;
using Wazap.Domain.Enums;

namespace Wazap.Application.Validators;

public class UpdateStatusRequestValidator : AbstractValidator<UpdateStatusRequest>
{
    public UpdateStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.RiderWhatsAppNumber)
            .NotEmpty()
            .When(x => x.Status == OrderStatus.RiderAssigned)
            .MaximumLength(30);
    }
}
