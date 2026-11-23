using FluentValidation;
using ValuPro.Domain.Security;

namespace ValuPro.Application.Users;

public sealed class SaveUserRequestValidator : AbstractValidator<SaveUserRequest>
{
    public SaveUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(20).Matches(@"^[0-9 +()-]*$").WithMessage("Use digits and spaces only.");
        RuleFor(x => x.RegistrationNo).MaximumLength(60);
        RuleFor(x => x.Role).NotEmpty().Must(r => Roles.All.Contains(r)).WithMessage("Choose a valid role.");
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128)
            .NotEqual(x => x.CurrentPassword).WithMessage("Choose a password different from the current one.");
    }
}

public sealed class UserQueryValidator : AbstractValidator<UserQuery>
{
    public UserQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
