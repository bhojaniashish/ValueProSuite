using FluentValidation;
using ValuPro.Application.Settings;

namespace ValuPro.Application.Clients;

public sealed class SaveClientRequestValidator : AbstractValidator<SaveClientRequest>
{
    public const string PanPattern = "^[A-Z]{5}[0-9]{4}[A-Z]$";

    public SaveClientRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Choose a client type.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the client name.").MaximumLength(200);
        RuleFor(x => x.Mobile).NotEmpty().WithMessage("Enter a mobile or phone number.")
            .Matches(@"^[0-9 +()-]+$").WithMessage("Use digits and spaces only.")
            .Must(m => Digits(m) is >= 10 and <= 15).WithMessage("Enter a 10-digit mobile or a landline with STD code.");
        RuleFor(x => x.Email).EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Pan).Must(p => System.Text.RegularExpressions.Regex.IsMatch(p!.Trim().ToUpperInvariant(), PanPattern))
            .WithMessage("Enter a valid PAN, e.g. AGRPS4521K.")
            .When(x => !string.IsNullOrWhiteSpace(x.Pan));
        RuleFor(x => x.Gstin).Must(g => System.Text.RegularExpressions.Regex.IsMatch(g!.Trim().ToUpperInvariant(), GstSettingsValidator.GstinPattern))
            .WithMessage("Enter a valid 15-character GSTIN.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.Gstin).Must((x, g) => g!.Trim().ToUpperInvariant().Substring(2, 10) == x.Pan!.Trim().ToUpperInvariant())
            .WithMessage("The GSTIN should contain the client's PAN (characters 3 to 12).")
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin) && x.Gstin.Trim().Length == 15 && !string.IsNullOrWhiteSpace(x.Pan));
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(60);
        RuleFor(x => x.Pincode).Matches("^[1-9][0-9]{5}$").WithMessage("Enter a 6-digit pincode.")
            .When(x => !string.IsNullOrWhiteSpace(x.Pincode));
        RuleFor(x => x.Remarks).MaximumLength(1000);
    }

    private static int Digits(string? s) => s?.Count(char.IsDigit) ?? 0;
}

public sealed class ClientQueryValidator : AbstractValidator<ClientQuery>
{
    public ClientQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
