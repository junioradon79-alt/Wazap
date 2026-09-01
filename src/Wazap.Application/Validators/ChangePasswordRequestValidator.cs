using FluentValidation;
using Wazap.Application.Dtos;

namespace Wazap.Application.Validators;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Le mot de passe actuel est requis.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Le nouveau mot de passe est requis.")
            .MinimumLength(8).WithMessage("Le nouveau mot de passe doit contenir au moins 8 caractères.")
            .MaximumLength(100).WithMessage("Le nouveau mot de passe ne doit pas dépasser 100 caractères.")
            .NotEqual(x => x.CurrentPassword).WithMessage("Le nouveau mot de passe doit être différent de l'actuel.")
            .Matches("[A-Z]").WithMessage("Le nouveau mot de passe doit contenir au moins une majuscule.")
            .Matches("[a-z]").WithMessage("Le nouveau mot de passe doit contenir au moins une minuscule.")
            .Matches("[0-9]").WithMessage("Le nouveau mot de passe doit contenir au moins un chiffre.")
            .Matches("[^A-Za-z0-9]").WithMessage("Le nouveau mot de passe doit contenir au moins un caractère spécial.");
    }
}
