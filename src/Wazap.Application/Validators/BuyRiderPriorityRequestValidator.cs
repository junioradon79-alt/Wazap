using FluentValidation;
using Wazap.Application.Dtos;

namespace Wazap.Application.Validators
{
    public class BuyRiderPriorityRequestValidator : AbstractValidator<BuyRiderPriorityRequest>
    {
        public BuyRiderPriorityRequestValidator()
        {
            RuleFor(x => x.RiderId)
                .NotEmpty()
                .WithMessage("L'identifiant du livreur est requis.");

            RuleFor(x => x.PackName)
                .NotEmpty()
                .WithMessage("Le nom du pack est requis.")
                .MaximumLength(100)
                .WithMessage("Le nom du pack ne peut pas dépasser 100 caractères.");
        }
    }
}