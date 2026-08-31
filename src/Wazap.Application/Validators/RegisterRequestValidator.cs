using FluentValidation;
using Wazap.Application.Dtos;
using Wazap.Domain.Enums;

namespace Wazap.Application.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .When(x => x.Role != UserRole.Admin)
            .MaximumLength(30);
    }
}
