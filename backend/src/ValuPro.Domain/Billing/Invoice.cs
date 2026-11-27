using ValuPro.Domain.Common;

namespace ValuPro.Domain.Billing;

public enum InvoiceStatus
{
    /// <summary>Being prepared: editable, no number yet (so issued numbers have no gaps).</summary>
    Draft = 0,
    /// <summary>Numbered and final. Corrections are made by cancelling and issuing a new one.</summary>
    Issued = 1,
    Cancelled = 2,
}

public enum BillTo
{
    Bank = 0,
    Client = 1,
    Other = 2,
}

public enum PaymentMode
{
    BankTransfer = 0,
    Upi = 1,
    Cheque = 2,
    Cash = 3,
    Other = 4,
}

/// <summary>Where an issued invoice stands with payments (derived, never stored).</summary>
public enum PaymentState
{
    Draft,
    Unpaid,
    PartlyPaid,
    Paid,
    Overdue,
    Cancelled,
}

/// <summary>A GST tax invoice for one or more cases. The recipient's details are copied in, so later edits elsewhere don't change it.</summary>
public class Invoice : TenantEntity
{
    /// <summary>e.g. INV/26-27/0001; set when issued.</summary>
    public string? Number { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }

    /// <summary>The bank whose cases are billed (for filtering and the bank-wise outstanding).</summary>
    public Guid? BankId { get; set; }
    public BillTo BillTo { get; set; }
    public Guid? ClientId { get; set; }
    public required string BillToName { get; set; }
    public string? BillToAddress { get; set; }
    public string? BillToGstin { get; set; }
    /// <summary>2-digit GST state code of the recipient; decides CGST + SGST or IGST.</summary>
    public string? BillToStateCode { get; set; }

    public decimal GstRate { get; set; }
    public bool InterState { get; set; }
    public decimal Taxable { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal RoundOff { get; set; }
    public decimal Total { get; set; }

    /// <summary>Sum of payments received (net of TDS) and of TDS deducted; kept for quick lists.</summary>
    public decimal Received { get; set; }
    public decimal Tds { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public string? IssuedByName { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancelReason { get; set; }
    public int Version { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
    public List<InvoicePayment> Payments { get; set; } = [];

    public decimal Outstanding => Status == InvoiceStatus.Issued ? Math.Max(0, Total - Received - Tds) : 0;

    public PaymentState State(DateOnly today) => Status switch
    {
        InvoiceStatus.Draft => PaymentState.Draft,
        InvoiceStatus.Cancelled => PaymentState.Cancelled,
        _ when Outstanding <= 0 => PaymentState.Paid,
        _ when DueDate < today => PaymentState.Overdue,
        _ when Received + Tds > 0 => PaymentState.PartlyPaid,
        _ => PaymentState.Unpaid,
    };

    /// <summary>Works out the taxes and total from the lines (call after any change).</summary>
    public void Recalculate(string? firmStateCode)
    {
        InterState = !string.IsNullOrWhiteSpace(firmStateCode) && !string.IsNullOrWhiteSpace(BillToStateCode) && firmStateCode != BillToStateCode;
        var t = InvoiceMath.Totals(Lines.Select(l => l.Amount), GstRate, InterState);
        (Taxable, Cgst, Sgst, Igst, RoundOff, Total) = (t.Taxable, t.Cgst, t.Sgst, t.Igst, t.RoundOff, t.Total);
    }

    public void RecalculatePayments()
    {
        Received = Payments.Where(p => !p.IsDeleted).Sum(p => p.Amount);
        Tds = Payments.Where(p => !p.IsDeleted).Sum(p => p.Tds);
    }
}

public class InvoiceLine : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public int Sort { get; set; }
    /// <summary>The case billed on this line (null for travel, extra visits and the like).</summary>
    public Guid? AssignmentId { get; set; }
    public required string Description { get; set; }
    /// <summary>Service accounting code, e.g. 997222 (real estate appraisal).</summary>
    public string? Sac { get; set; }
    public decimal Amount { get; set; }
}

public class InvoicePayment : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public DateOnly ReceivedOn { get; set; }
    /// <summary>Money received.</summary>
    public decimal Amount { get; set; }
    /// <summary>Income tax deducted at source by the payer (banks deduct 10% u/s 194J); settles the invoice like money.</summary>
    public decimal Tds { get; set; }
    public PaymentMode Mode { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public required string RecordedByName { get; set; }
}

public sealed record InvoiceTotals(decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal RoundOff, decimal Total);

public static class InvoiceMath
{
    /// <summary>
    /// GST on the taxable value: IGST across states, else CGST and SGST half each (to the paisa). The total is
    /// rounded to the rupee, and the difference shown as round-off, as on most tax invoices.
    /// </summary>
    public static InvoiceTotals Totals(IEnumerable<decimal> amounts, decimal rate, bool interState)
    {
        var taxable = Math.Round(amounts.Sum(), 2, MidpointRounding.AwayFromZero);
        decimal cgst = 0, sgst = 0, igst = 0;
        if (interState) igst = Math.Round(taxable * rate / 100, 2, MidpointRounding.AwayFromZero);
        else cgst = sgst = Math.Round(taxable * rate / 200, 2, MidpointRounding.AwayFromZero);
        var exact = taxable + cgst + sgst + igst;
        var total = Math.Round(exact, 0, MidpointRounding.AwayFromZero);
        return new InvoiceTotals(taxable, cgst, sgst, igst, total - exact, total);
    }

    /// <summary>Indian financial year (April–March) of a date as "26-27".</summary>
    public static string FinancialYear(DateOnly d)
    {
        var start = d.Month >= 4 ? d.Year : d.Year - 1;
        return $"{start % 100:00}-{(start + 1) % 100:00}";
    }

    public static DateOnly FinancialYearStart(DateOnly d) => new(d.Month >= 4 ? d.Year : d.Year - 1, 4, 1);
}

/// <summary>GST state codes (first two digits of a GSTIN).</summary>
public static class GstStates
{
    public static readonly IReadOnlyDictionary<string, string> ByCode = new Dictionary<string, string>
    {
        ["01"] = "Jammu and Kashmir", ["02"] = "Himachal Pradesh", ["03"] = "Punjab", ["04"] = "Chandigarh", ["05"] = "Uttarakhand",
        ["06"] = "Haryana", ["07"] = "Delhi", ["08"] = "Rajasthan", ["09"] = "Uttar Pradesh", ["10"] = "Bihar", ["11"] = "Sikkim",
        ["12"] = "Arunachal Pradesh", ["13"] = "Nagaland", ["14"] = "Manipur", ["15"] = "Mizoram", ["16"] = "Tripura", ["17"] = "Meghalaya",
        ["18"] = "Assam", ["19"] = "West Bengal", ["20"] = "Jharkhand", ["21"] = "Odisha", ["22"] = "Chhattisgarh", ["23"] = "Madhya Pradesh",
        ["24"] = "Gujarat", ["26"] = "Dadra and Nagar Haveli and Daman and Diu", ["27"] = "Maharashtra", ["29"] = "Karnataka", ["30"] = "Goa",
        ["31"] = "Lakshadweep", ["32"] = "Kerala", ["33"] = "Tamil Nadu", ["34"] = "Puducherry", ["35"] = "Andaman and Nicobar Islands",
        ["36"] = "Telangana", ["37"] = "Andhra Pradesh", ["38"] = "Ladakh",
    };

    public static string? Name(string? code) => code is not null && ByCode.TryGetValue(code, out var n) ? n : null;

    /// <summary>The code for a state name as people type it ("gujarat", "Delhi NCR" → no match is fine).</summary>
    public static string? CodeOf(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var s = state.Trim();
        return ByCode.FirstOrDefault(kv => string.Equals(kv.Value, s, StringComparison.OrdinalIgnoreCase)).Key;
    }
}
