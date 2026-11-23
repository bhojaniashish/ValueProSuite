using FluentValidation;
using ValuPro.Domain.Settings;

namespace ValuPro.Application.Settings;

public sealed class CompanySettingsValidator : AbstractValidator<CompanySettings>
{
    public CompanySettingsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RegisteredValuerNo).MaximumLength(60);
        RuleFor(x => x.IbbiRegistration).MaximumLength(60);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Phone).MaximumLength(30).Matches(@"^[0-9 +()-]*$").WithMessage("Use digits and spaces only.");
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.State).MaximumLength(60);
    }
}

public sealed class UpdateBrandingRequestValidator : AbstractValidator<UpdateBrandingRequest>
{
    private const string Hex = "^#[0-9A-Fa-f]{6}$";

    public UpdateBrandingRequestValidator()
    {
        RuleFor(x => x.PrimaryColor).NotEmpty().Matches(Hex).WithMessage("Use a colour like #2B50C4.");
        RuleFor(x => x.AccentColor).NotEmpty().Matches(Hex).WithMessage("Use a colour like #C2922E.");
    }
}

public sealed class UpdateCommunicationRequestValidator : AbstractValidator<UpdateCommunicationRequest>
{
    public UpdateCommunicationRequestValidator()
    {
        RuleFor(x => x.SmtpHost).MaximumLength(200).Matches(@"^[A-Za-z0-9.-]*$").WithMessage("Enter a host name like smtp.gmail.com.");
        RuleFor(x => x.SmtpPort).InclusiveBetween(1, 65535);
        RuleFor(x => x.SmtpUsername).MaximumLength(256);
        RuleFor(x => x.SmtpPassword).MaximumLength(256);
        RuleFor(x => x.FromEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.FromEmail));
        RuleFor(x => x.FromEmail).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.SmtpHost))
            .WithMessage("Enter the address emails are sent from.");
        RuleFor(x => x.FromName).MaximumLength(100);
        RuleFor(x => x.SmsProvider).IsInEnum();
        RuleFor(x => x.SmsSenderId).MaximumLength(11).Matches("^[A-Za-z0-9]*$").WithMessage("Up to 11 letters or digits.");
        RuleFor(x => x.SmsSenderId).NotEmpty().When(x => x.SmsProvider != SmsProvider.None).WithMessage("Enter the sender ID.");
        RuleFor(x => x.SmsApiKey).MaximumLength(256);
    }
}

public sealed class GstSettingsValidator : AbstractValidator<GstSettings>
{
    // 2-digit state code, 10-char PAN, entity number, 'Z', checksum.
    public const string GstinPattern = "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$";
    private static readonly decimal[] Rates = [0m, 5m, 12m, 18m];

    public GstSettingsValidator()
    {
        RuleFor(x => x.Gstin).Matches(GstinPattern).WithMessage("Enter a valid 15-character GSTIN, e.g. 24ABCDE1234F1Z5.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.DefaultGstRate).Must(r => Rates.Contains(r)).WithMessage("Choose 0%, 5%, 12% or 18%.");
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$").WithMessage("Use the 2-digit state code, e.g. 24 for Gujarat.")
            .When(x => !string.IsNullOrWhiteSpace(x.StateCode));
        RuleFor(x => x.StateCode).Must((x, code) => x.Gstin!.StartsWith(code!, StringComparison.Ordinal))
            .WithMessage("Must match the first two digits of the GSTIN.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gstin) && x.Gstin.Length >= 2 && !string.IsNullOrWhiteSpace(x.StateCode));
        RuleFor(x => x.PlaceOfSupply).MaximumLength(60);
        RuleFor(x => x.InvoicePrefix).NotEmpty().MaximumLength(10).Matches("^[A-Z0-9-]+$").WithMessage("Capital letters, digits or -.");
        RuleFor(x => x.AssignmentPrefix).NotEmpty().MaximumLength(10).Matches("^[A-Z0-9-]+$").WithMessage("Capital letters, digits or -.");
    }
}

public sealed class UpdateNotificationsRequestValidator : AbstractValidator<UpdateNotificationsRequest>
{
    public UpdateNotificationsRequestValidator()
    {
        RuleFor(x => x.Preferences).NotNull();
        RuleForEach(x => x.Preferences).Must(p => NotificationEvents.All.Any(e => e.Key == p.Event))
            .WithMessage("Unknown notification event.");
    }
}
