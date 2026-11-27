using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Billing;

namespace ValuPro.Application.Billing;

public sealed record InvoiceQuery(
    string? Search = null,
    /// <summary>draft, unpaid (incl. part-paid), overdue, paid, cancelled; empty = all.</summary>
    string? State = null,
    Guid? BankId = null,
    Guid? AssignmentId = null,
    int Page = 1,
    int PageSize = 20);

public sealed record InvoiceListItem(
    Guid Id, string? Number, InvoiceStatus Status, PaymentState State, DateOnly InvoiceDate, DateOnly DueDate, string BillToName,
    string? BankShortCode, string? BankColor, IReadOnlyList<string> Cases, decimal Taxable, decimal Gst, decimal Total, decimal Outstanding);

public sealed record InvoicePage(IReadOnlyList<InvoiceListItem> Items, int Total, int Page, int PageSize);

/// <summary>A submitted case with no issued invoice yet (or only a draft).</summary>
public sealed record UnbilledCase(Guid AssignmentId, string Number, AssignmentStatus Status, Guid BankId, string BankShortCode, string BankColor,
    string? BranchName, string ApplicantName, string PropertySummary, decimal Fee, DateTimeOffset? SubmittedAt, string? DraftInvoiceId);

public sealed record MonthAmount(string Month, decimal Invoiced, decimal Collected);

public sealed record BankOutstanding(Guid? BankId, string Name, string? ShortCode, string? Color, int Invoices, decimal Outstanding, decimal Overdue);

/// <summary>The billing dashboard for the current financial year.</summary>
public sealed record BillingSummary(
    string FinancialYear, decimal Invoiced, decimal Collected, decimal Tds, decimal Outstanding, decimal Overdue, int OverdueCount,
    int UnbilledCount, decimal UnbilledFees, IReadOnlyList<MonthAmount> Months, IReadOnlyList<BankOutstanding> ByBank);

public sealed record InvoiceLineItem(Guid? AssignmentId, string? AssignmentNumber, string Description, string? Sac, decimal Amount);

public sealed record PaymentItem(Guid Id, DateOnly ReceivedOn, decimal Amount, decimal Tds, PaymentMode Mode, string? Reference, string? Note,
    string RecordedByName, DateTimeOffset RecordedAt);

public sealed record FirmBilling(string Name, string? Address, string? Gstin, string? StateCode, string? StateName, string? Pan);

public sealed record InvoiceDetail(
    Guid Id, string? Number, InvoiceStatus Status, PaymentState State, int Version, DateOnly InvoiceDate, DateOnly DueDate,
    Guid? BankId, string? BankName, BillTo BillTo, Guid? ClientId, string BillToName, string? BillToAddress, string? BillToGstin,
    string? BillToStateCode, string? PlaceOfSupply,
    decimal GstRate, bool InterState, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal RoundOff, decimal Total,
    string TotalInWords, decimal Received, decimal Tds, decimal Outstanding, string? Notes,
    IReadOnlyList<InvoiceLineItem> Lines, IReadOnlyList<PaymentItem> Payments,
    DateTimeOffset? IssuedAt, string? IssuedByName, DateTimeOffset? CancelledAt, string? CancelledByName, string? CancelReason,
    FirmBilling Firm, IReadOnlyList<BillToOption> BillToOptions, bool CanEdit, string? IssueBlockedReason);

/// <summary>Who the invoice could go to, pre-filled from the bank branch or the client.</summary>
public sealed record BillToOption(BillTo BillTo, Guid? ClientId, string Name, string? Address, string? Gstin, string? StateCode, string Label);

public sealed record CreateInvoiceRequest(IReadOnlyList<Guid> AssignmentIds, BillTo? BillTo = null);

public sealed record SaveInvoiceLine(Guid? AssignmentId, string Description, string? Sac, decimal Amount);

public sealed record SaveInvoiceRequest(
    int Version, DateOnly InvoiceDate, DateOnly DueDate, BillTo BillTo, Guid? ClientId, string BillToName, string? BillToAddress,
    string? BillToGstin, string? BillToStateCode, decimal GstRate, IReadOnlyList<SaveInvoiceLine> Lines, string? Notes);

public sealed record CancelInvoiceRequest(string Reason);

public sealed record AddPaymentRequest(DateOnly ReceivedOn, decimal Amount, decimal Tds, PaymentMode Mode, string? Reference, string? Note);

public sealed record InvoicePdf(byte[] Content, string FileName);

public sealed class SaveInvoiceRequestValidator : AbstractValidator<SaveInvoiceRequest>
{
    private static readonly decimal[] Rates = [0m, 5m, 12m, 18m];
    public const string GstinPattern = "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$";

    public SaveInvoiceRequestValidator()
    {
        RuleFor(x => x.BillToName).NotEmpty().WithMessage("Who is the invoice for?").MaximumLength(200);
        RuleFor(x => x.BillToAddress).MaximumLength(400);
        RuleFor(x => x.BillToGstin).Matches(GstinPattern).WithMessage("Enter a valid 15-character GSTIN, e.g. 24AAACH2702H1ZB.")
            .When(x => !string.IsNullOrWhiteSpace(x.BillToGstin));
        RuleFor(x => x.BillToStateCode).Must(c => GstStates.Name(c) is not null).WithMessage("Choose the recipient's state.")
            .When(x => !string.IsNullOrWhiteSpace(x.BillToStateCode));
        RuleFor(x => x.BillToStateCode).Must((x, c) => x.BillToGstin!.StartsWith(c!, StringComparison.Ordinal))
            .WithMessage("The state must match the first two digits of the GSTIN.")
            .When(x => !string.IsNullOrWhiteSpace(x.BillToGstin) && x.BillToGstin.Length >= 2 && !string.IsNullOrWhiteSpace(x.BillToStateCode));
        RuleFor(x => x.GstRate).Must(r => Rates.Contains(r)).WithMessage("Choose 0%, 5%, 12% or 18%.");
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate).WithMessage("The due date can't be before the invoice date.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line.").Must(l => l.Count <= 100).WithMessage("At most 100 lines.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Description).NotEmpty().WithMessage("Describe each line.").MaximumLength(300);
            l.RuleFor(x => x.Sac).MaximumLength(10);
            l.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).LessThan(1_00_00_00_000m);
        });
        RuleFor(x => x.Lines).Must(l => l.Sum(x => x.Amount) > 0).WithMessage("The invoice total can't be zero.").When(x => x.Lines.Count > 0);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class AddPaymentRequestValidator : AbstractValidator<AddPaymentRequest>
{
    public AddPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).LessThan(1_00_00_00_000m);
        RuleFor(x => x.Tds).GreaterThanOrEqualTo(0).LessThan(1_00_00_00_000m);
        RuleFor(x => x).Must(x => x.Amount + x.Tds > 0).WithMessage("Enter the amount received or the TDS deducted.").OverridePropertyName("amount");
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public sealed class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Say why it is cancelled.").MaximumLength(300);
}

public interface IBillingService
{
    Task<BillingSummary> SummaryAsync(CancellationToken ct = default);
    Task<InvoicePage> ListAsync(InvoiceQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<UnbilledCase>> UnbilledAsync(Guid? bankId, CancellationToken ct = default);
    /// <summary>A draft for the given cases, filled from the bank branch (or the client) and the case fees.</summary>
    Task<Result<InvoiceDetail>> CreateAsync(CreateInvoiceRequest request, CancellationToken ct = default);
    Task<Result<InvoiceDetail>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<InvoiceDetail>> SaveAsync(Guid id, SaveInvoiceRequest request, CancellationToken ct = default);
    /// <summary>Gives the draft its number; submitted cases on it move to "Invoice Generated".</summary>
    Task<Result<InvoiceDetail>> IssueAsync(Guid id, CancellationToken ct = default);
    Task<Result<InvoiceDetail>> CancelAsync(Guid id, CancelInvoiceRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteDraftAsync(Guid id, CancellationToken ct = default);
    Task<Result<InvoiceDetail>> AddPaymentAsync(Guid id, AddPaymentRequest request, CancellationToken ct = default);
    Task<Result<InvoiceDetail>> RemovePaymentAsync(Guid id, Guid paymentId, CancellationToken ct = default);
    Task<Result<InvoicePdf>> PdfAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Renders the tax invoice PDF.</summary>
public interface IInvoiceRenderer
{
    byte[] Render(InvoiceDetail invoice, InvoiceExtras extras);
}

/// <summary>What the PDF needs beyond the invoice itself.</summary>
public sealed record InvoiceExtras(byte[]? Logo, string Color, string? Phone, string? Email, string? PaymentDetails, string? Terms);
