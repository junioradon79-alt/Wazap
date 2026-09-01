using FluentValidation;
using Wazap.Application.Dtos;

namespace Wazap.Application.Validators
{
    public class BuyPackRequestValidator : AbstractValidator<BuyPackRequest>
    {
        public BuyPackRequestValidator()
        {
            RuleFor(x => x.VendorId)
                .NotEmpty()
                .WithMessage("L'identifiant du vendeur est requis.");

            RuleFor(x => x.PackName)
                .NotEmpty()
                .WithMessage("Le nom du pack est requis.")
                .MaximumLength(100)
                .WithMessage("Le nom du pack ne peut pas dépasser 100 caractères.");
        }
    }
}
