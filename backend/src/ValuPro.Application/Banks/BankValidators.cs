using FluentValidation;
using ValuPro.Domain.Banks;

namespace ValuPro.Application.Banks;

public sealed class BankRulesValidator : AbstractValidator<BankRules>
{
    public BankRulesValidator()
    {
        RuleFor(x => x.RateBasis).IsInEnum();
        RuleFor(x => x.Depreciation).IsInEnum();
        RuleFor(x => x.DistressPercent).InclusiveBetween(1, 100).WithMessage("Distress value must be 1–100% of market value.");
        RuleFor(x => x.RealizablePercent).InclusiveBetween(1, 100).WithMessage("Realizable value must be 1–100% of market value.");
        RuleFor(x => x.DistressPercent).LessThanOrEqualTo(x => x.RealizablePercent)
            .WithMessage("Distress value should not be higher than realizable value.");
    }
}

public sealed class SaveBankRequestValidator : AbstractValidator<SaveBankRequest>
{
    public SaveBankRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the bank name.").MaximumLength(150);
        RuleFor(x => x.ShortCode).NotEmpty().WithMessage("Enter a short code, e.g. SBI.")
            .Matches("^[A-Za-z0-9]{2,10}$").WithMessage("2–10 letters or digits, e.g. SBI.");
        RuleFor(x => x.BrandColor).Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Use a colour like #2453A6.");
        RuleFor(x => x.IfscPrefix).Matches("^[A-Za-z]{4}$").WithMessage("The first 4 letters of the bank's IFSC, e.g. SBIN.")
            .When(x => !string.IsNullOrWhiteSpace(x.IfscPrefix));
        RuleFor(x => x.ReportFormat).MaximumLength(100);
        RuleFor(x => x.Rules).NotNull().SetValidator(new BankRulesValidator());
    }
}

public sealed class SaveBranchRequestValidator : AbstractValidator<SaveBranchRequest>
{
    public const string IfscPattern = "^[A-Z]{4}0[A-Z0-9]{6}$";

    public SaveBranchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the branch name.").MaximumLength(150);
        RuleFor(x => x.Ifsc).Must(i => System.Text.RegularExpressions.Regex.IsMatch(i!.Trim().ToUpperInvariant(), IfscPattern))
            .WithMessage("Enter a valid 11-character IFSC, e.g. SBIN0001234.")
            .When(x => !string.IsNullOrWhiteSpace(x.Ifsc));
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(60);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.ContactPerson).MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(20).Matches(@"^[0-9 +()-]*$").WithMessage("Use digits and spaces only.");
        RuleFor(x => x.Email).EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class UpdateQuestionnaireRequestValidator : AbstractValidator<UpdateQuestionnaireRequest>
{
    public UpdateQuestionnaireRequestValidator()
    {
        RuleFor(x => x.Questions).NotNull().Must(q => q.Count <= 100).WithMessage("At most 100 questions per bank.");
        RuleForEach(x => x.Questions).ChildRules(q =>
        {
            q.RuleFor(x => x.Text).NotEmpty().WithMessage("Every question needs text.").MaximumLength(300);
            q.RuleFor(x => x.AnswerType).IsInEnum();
            q.RuleFor(x => x.Options).Must(o => o.Count(s => !string.IsNullOrWhiteSpace(s)) >= 2)
                .WithMessage("A choice question needs at least two options.")
                .When(x => x.AnswerType == AnswerType.Choice);
            q.RuleForEach(x => x.Options).MaximumLength(100);
        });
    }
}

public sealed class UpdateRulesRequestValidator : AbstractValidator<UpdateRulesRequest>
{
    public UpdateRulesRequestValidator()
    {
        RuleFor(x => x.Banks).NotNull();
        RuleForEach(x => x.Banks).ChildRules(r => r.RuleFor(x => x.Rules).NotNull().SetValidator(new BankRulesValidator()));
    }
}
