using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Billing;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Billing;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Security;
using ValuPro.Domain.Settings;
using ValuPro.Domain.Tenancy;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Billing;

public sealed class BillingService(
    AppDbContext db,
    ICurrentUser me,
    ITenantContext tenant,
    INumberSequence numbers,
    IFileStorage files,
    IInvoiceRenderer renderer,
    TimeProvider clock) : IBillingService
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);

    // ---------------- dashboard and lists ----------------

    public async Task<BillingSummary> SummaryAsync(CancellationToken ct = default)
    {
        var today = Today;
        var fyStart = InvoiceMath.FinancialYearStart(today);
        var fyEnd = fyStart.AddYears(1);
        var issued = await db.Set<Invoice>().AsNoTracking().Where(i => i.Status == InvoiceStatus.Issued)
            .Select(i => new { i.BankId, i.InvoiceDate, i.DueDate, i.Total, i.Received, i.Tds }).ToListAsync(ct);
        var payments = await (from p in db.Set<InvoicePayment>()
                              join i in db.Set<Invoice>() on p.InvoiceId equals i.Id
                              where i.Status == InvoiceStatus.Issued && p.ReceivedOn >= fyStart && p.ReceivedOn < fyEnd
                              select new { p.ReceivedOn, p.Amount, p.Tds }).ToListAsync(ct);
        var banks = await db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking()
            .Select(b => new { b.Id, b.Name, b.ShortCode, b.BrandColor }).ToDictionaryAsync(b => b.Id, ct);

        decimal Out(decimal total, decimal received, decimal tds) => Math.Max(0, total - received - tds);
        var thisYear = issued.Where(i => i.InvoiceDate >= fyStart && i.InvoiceDate < fyEnd).ToList();
        var months = Enumerable.Range(0, 12).Select(k => fyStart.AddMonths(k)).Where(m => m <= today).Select(m => new MonthAmount(
            m.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture),
            thisYear.Where(i => i.InvoiceDate.Year == m.Year && i.InvoiceDate.Month == m.Month).Sum(i => i.Total),
            payments.Where(p => p.ReceivedOn.Year == m.Year && p.ReceivedOn.Month == m.Month).Sum(p => p.Amount + p.Tds))).ToList();

        var byBank = issued.Where(i => Out(i.Total, i.Received, i.Tds) > 0).GroupBy(i => i.BankId).Select(g =>
        {
            var b = g.Key is { } id && banks.TryGetValue(id, out var x) ? x : null;
            return new BankOutstanding(g.Key, b?.Name ?? "Other clients", b?.ShortCode, b?.BrandColor, g.Count(),
                g.Sum(i => Out(i.Total, i.Received, i.Tds)), g.Where(i => i.DueDate < today).Sum(i => Out(i.Total, i.Received, i.Tds)));
        }).OrderByDescending(b => b.Outstanding).ToList();

        var unbilled = await UnbilledAsync(null, ct);
        var overdue = issued.Where(i => i.DueDate < today && Out(i.Total, i.Received, i.Tds) > 0).ToList();
        return new BillingSummary(InvoiceMath.FinancialYear(today), thisYear.Sum(i => i.Total), payments.Sum(p => p.Amount), payments.Sum(p => p.Tds),
            issued.Sum(i => Out(i.Total, i.Received, i.Tds)), overdue.Sum(i => Out(i.Total, i.Received, i.Tds)), overdue.Count,
            unbilled.Count, unbilled.Sum(u => u.Fee), months, byBank);
    }

    public async Task<InvoicePage> ListAsync(InvoiceQuery q, CancellationToken ct = default)
    {
        var today = Today;
        var rows = db.Set<Invoice>().AsNoTracking();
        if (q.BankId is { } bank) rows = rows.Where(i => i.BankId == bank);
        if (q.AssignmentId is { } a) rows = rows.Where(i => i.Lines.Any(l => l.AssignmentId == a));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            var caseIds = db.Set<Assignment>().Where(x => x.Number.Contains(s) || x.ApplicantName.Contains(s)).Select(x => (Guid?)x.Id);
            rows = rows.Where(i => i.Number!.Contains(s) || i.BillToName.Contains(s) || i.Lines.Any(l => caseIds.Contains(l.AssignmentId)));
        }
        rows = q.State switch
        {
            "draft" => rows.Where(i => i.Status == InvoiceStatus.Draft),
            "cancelled" => rows.Where(i => i.Status == InvoiceStatus.Cancelled),
            "paid" => rows.Where(i => i.Status == InvoiceStatus.Issued && i.Received + i.Tds >= i.Total),
            "unpaid" => rows.Where(i => i.Status == InvoiceStatus.Issued && i.Received + i.Tds < i.Total),
            "overdue" => rows.Where(i => i.Status == InvoiceStatus.Issued && i.Received + i.Tds < i.Total && i.DueDate < today),
            _ => rows,
        };
        var total = await rows.CountAsync(ct);
        var size = Math.Clamp(q.PageSize, 1, 100);
        var page = Math.Max(1, q.Page);
        var list = await rows.OrderBy(i => i.Status == InvoiceStatus.Draft ? 0 : 1).ThenByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Number)
            .Skip((page - 1) * size).Take(size).Include(i => i.Lines).ToListAsync(ct);
        var caseNumbers = await CaseNumbersAsync(list.SelectMany(i => i.Lines), ct);
        var banks = await BanksAsync(ct);
        return new InvoicePage([.. list.Select(i => new InvoiceListItem(i.Id, i.Number, i.Status, i.State(today), i.InvoiceDate, i.DueDate, i.BillToName,
            i.BankId is { } b && banks.TryGetValue(b, out var bk) ? bk.ShortCode : null, i.BankId is { } b2 && banks.TryGetValue(b2, out var bk2) ? bk2.BrandColor : null,
            [.. i.Lines.Where(l => l.AssignmentId != null).Select(l => caseNumbers.GetValueOrDefault(l.AssignmentId!.Value) ?? "—").Distinct()],
            i.Taxable, i.Cgst + i.Sgst + i.Igst, i.Total, i.Outstanding))], total, page, size);
    }

    public async Task<IReadOnlyList<UnbilledCase>> UnbilledAsync(Guid? bankId, CancellationToken ct = default)
    {
        var issuedLines = from l in db.Set<InvoiceLine>()
                          join i in db.Set<Invoice>() on l.InvoiceId equals i.Id
                          where i.Status == InvoiceStatus.Issued && l.AssignmentId != null
                          select l.AssignmentId;
        var q = db.Set<Assignment>().AsNoTracking().Where(a => a.Status == AssignmentStatus.Submitted && !issuedLines.Contains(a.Id));
        if (bankId is { } b) q = q.Where(a => a.BankId == b);
        var rows = await (from a in q
                          join bk in db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]) on a.BankId equals bk.Id
                          join br in db.Set<BankBranch>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]) on a.BranchId equals (Guid?)br.Id into brs
                          from br in brs.DefaultIfEmpty()
                          orderby a.UpdatedAt
                          select new { A = a, Bank = bk, Branch = br != null ? br.Name : null }).ToListAsync(ct);
        var ids = rows.Select(r => (Guid?)r.A.Id).ToList();
        var drafts = await (from l in db.Set<InvoiceLine>()
                            join i in db.Set<Invoice>() on l.InvoiceId equals i.Id
                            where i.Status == InvoiceStatus.Draft && ids.Contains(l.AssignmentId)
                            select new { l.AssignmentId, i.Id }).ToListAsync(ct);
        return [.. rows.Select(r => new UnbilledCase(r.A.Id, r.A.Number, r.A.Status, r.Bank.Id, r.Bank.ShortCode, r.Bank.BrandColor, r.Branch,
            r.A.ApplicantName, r.A.PropertySummary, r.A.Fee, r.A.UpdatedAt ?? r.A.CreatedAt,
            drafts.FirstOrDefault(d => d.AssignmentId == r.A.Id)?.Id.ToString()))];
    }

    // ---------------- one invoice ----------------

    public async Task<Result<InvoiceDetail>> CreateAsync(CreateInvoiceRequest r, CancellationToken ct = default)
    {
        var ids = r.AssignmentIds.Distinct().ToList();
        if (ids.Count == 0) return Error.Validation("Choose the cases to bill.", "assignmentIds");
        var cases = await db.Set<Assignment>().AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Number).ToListAsync(ct);
        if (cases.Count != ids.Count) return Error.NotFound("Some of those cases were not found.");
        if (cases.Any(a => a.Status == AssignmentStatus.Cancelled)) return Error.Validation("Cancelled cases can't be billed.", "assignmentIds");
        if (cases.Select(a => a.BankId).Distinct().Count() > 1) return Error.Validation("One invoice is for one bank's cases. Choose cases of a single bank.", "assignmentIds");
        var billed = await IssuedCaseNumbersAsync(ids, null, ct);
        if (billed.Count > 0) return Error.Conflict($"{string.Join(", ", billed)} already has an issued invoice.");

        var settings = await SettingsAsync(ct);
        var options = await OptionsAsync(cases, ct);
        var pick = r.BillTo == BillTo.Client ? options.FirstOrDefault(o => o.BillTo == BillTo.Client) ?? options[0] : options[0];
        var today = Today;
        var invoice = new Invoice
        {
            Status = InvoiceStatus.Draft, InvoiceDate = today, DueDate = today.AddDays(settings.PaymentDueDays), BankId = cases[0].BankId,
            BillTo = pick.BillTo, ClientId = pick.ClientId, BillToName = pick.Name, BillToAddress = pick.Address, BillToGstin = pick.Gstin,
            BillToStateCode = pick.StateCode, GstRate = settings.DefaultGstRate,
        };
        var n = 0;
        foreach (var a in cases)
            invoice.Lines.Add(new InvoiceLine
            {
                Sort = n++, AssignmentId = a.Id, Sac = settings.SacCode, Amount = a.Fee,
                Description = $"{CaseKind(a.CaseType)} fee: {a.Number}, {a.ApplicantName}, {a.PropertySummary}" is var d && d.Length > 300 ? d[..300] : d,
            });
        invoice.Recalculate(settings.StateCode);
        db.Add(invoice);
        await db.SaveChangesAsync(ct);
        return await GetAsync(invoice.Id, ct);
    }

    public async Task<Result<InvoiceDetail>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct);
        return i is null ? Error.NotFound("Invoice not found.") : await DetailAsync(i, ct);
    }

    public async Task<Result<InvoiceDetail>> SaveAsync(Guid id, SaveInvoiceRequest r, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Error.NotFound("Invoice not found.");
        if (i.Status != InvoiceStatus.Draft) return Error.Conflict("Only a draft can be changed. Cancel the invoice and issue a new one to correct it.");
        if (i.Version != r.Version) return Error.Conflict("Someone else changed this invoice. Reload to see their changes.");

        var caseIds = r.Lines.Where(l => l.AssignmentId != null).Select(l => l.AssignmentId!.Value).Distinct().ToList();
        if (caseIds.Count > 0)
        {
            var found = await db.Set<Assignment>().Where(a => caseIds.Contains(a.Id)).Select(a => new { a.Id, a.BankId }).ToListAsync(ct);
            if (found.Count != caseIds.Count) return Error.Validation("A line refers to a case that no longer exists.", "lines");
            if (i.BankId is { } bank && found.Any(f => f.BankId != bank)) return Error.Validation("All cases on an invoice must be the same bank's.", "lines");
            var billed = await IssuedCaseNumbersAsync(caseIds, id, ct);
            if (billed.Count > 0) return Error.Conflict($"{string.Join(", ", billed)} already has an issued invoice.");
        }
        if (r.ClientId is { } cid && !await db.Set<Client>().AnyAsync(c => c.Id == cid, ct)) return Error.Validation("Client not found.", "clientId");

        i.InvoiceDate = r.InvoiceDate;
        i.DueDate = r.DueDate;
        i.BillTo = r.BillTo;
        i.ClientId = r.BillTo == BillTo.Client ? r.ClientId : null;
        i.BillToName = r.BillToName.Trim();
        i.BillToAddress = Clean(r.BillToAddress);
        i.BillToGstin = Clean(r.BillToGstin)?.ToUpperInvariant();
        i.BillToStateCode = Clean(r.BillToStateCode) ?? i.BillToGstin?[..2];
        i.GstRate = r.GstRate;
        i.Notes = Clean(r.Notes);
        db.RemoveRange(i.Lines);
        i.Lines = [.. r.Lines.Select((l, k) => new InvoiceLine
        {
            InvoiceId = i.Id, Sort = k, AssignmentId = l.AssignmentId, Description = l.Description.Trim(), Sac = Clean(l.Sac),
            Amount = Math.Round(l.Amount, 2, MidpointRounding.AwayFromZero),
        })];
        foreach (var l in i.Lines) db.Add(l);
        i.Recalculate((await SettingsAsync(ct)).StateCode);
        i.Version++;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<InvoiceDetail>> IssueAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Error.NotFound("Invoice not found.");
        if (i.Status != InvoiceStatus.Draft) return Error.Conflict("This invoice has already been issued.");
        var settings = await SettingsAsync(ct);
        if (IssueBlocked(i, settings) is { } why) return Error.Validation(why);
        var caseIds = i.Lines.Where(l => l.AssignmentId != null).Select(l => l.AssignmentId!.Value).Distinct().ToList();
        var billed = await IssuedCaseNumbersAsync(caseIds, id, ct);
        if (billed.Count > 0) return Error.Conflict($"{string.Join(", ", billed)} already has an issued invoice.");

        // Numbered per financial year, without gaps: the number is taken only now.
        var fy = InvoiceMath.FinancialYear(i.InvoiceDate);
        var seq = await numbers.NextAsync($"invoice:{fy}", ct);
        i.Number = $"{settings.InvoicePrefix}/{fy}/{seq:0000}";
        i.Status = InvoiceStatus.Issued;
        i.IssuedAt = clock.GetUtcNow();
        i.IssuedByName = me.Name ?? "Unknown";
        i.Recalculate(settings.StateCode);
        i.Version++;

        var cases = await db.Set<Assignment>().Where(a => caseIds.Contains(a.Id)).ToListAsync(ct);
        foreach (var a in cases.Where(a => a.Status == AssignmentStatus.Submitted))
        {
            AddEvent(a, AssignmentStatus.InvoiceGenerated, "Invoice issued", $"{i.Number} for ₹{IndianNumber.Format(i.Total)}.");
            a.Status = AssignmentStatus.InvoiceGenerated;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<InvoiceDetail>> CancelAsync(Guid id, CancelInvoiceRequest r, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().Include(x => x.Lines).Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Error.NotFound("Invoice not found.");
        if (i.Status != InvoiceStatus.Issued) return Error.Conflict(i.Status == InvoiceStatus.Draft ? "A draft is deleted, not cancelled." : "Already cancelled.");
        if (i.Payments.Count > 0) return Error.Conflict("Payments are recorded against this invoice. Remove them first.");

        i.Status = InvoiceStatus.Cancelled;
        i.CancelledAt = clock.GetUtcNow();
        i.CancelledByName = me.Name ?? "Unknown";
        i.CancelReason = r.Reason.Trim();
        i.Version++;
        // Cases go back to "Submitted" unless another issued invoice still bills them.
        var caseIds = i.Lines.Where(l => l.AssignmentId != null).Select(l => l.AssignmentId!.Value).Distinct().ToList();
        var stillBilled = await IssuedCaseIdsAsync(caseIds, id, ct);
        var cases = await db.Set<Assignment>().Where(a => caseIds.Contains(a.Id) && a.Status == AssignmentStatus.InvoiceGenerated).ToListAsync(ct);
        foreach (var a in cases.Where(a => !stillBilled.Contains(a.Id)))
        {
            AddEvent(a, AssignmentStatus.Submitted, "Invoice cancelled", $"{i.Number}: {i.CancelReason}");
            a.Status = AssignmentStatus.Submitted;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<bool>> DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Error.NotFound("Invoice not found.");
        if (i.Status != InvoiceStatus.Draft) return Error.Conflict("Issued invoices are kept; cancel it instead.");
        db.RemoveRange(i.Lines);
        db.Remove(i);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Result<InvoiceDetail>> AddPaymentAsync(Guid id, AddPaymentRequest r, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Error.NotFound("Invoice not found.");
        if (i.Status != InvoiceStatus.Issued) return Error.Conflict("Payments are recorded against issued invoices.");
        if (r.ReceivedOn > Today) return Error.Validation("The payment date can't be in the future.", "receivedOn");
        if (r.Amount + r.Tds > i.Outstanding + 0.5m)
            return Error.Validation($"That is more than the ₹{IndianNumber.Format(i.Outstanding)} outstanding.", "amount");
        var p = new InvoicePayment
        {
            InvoiceId = i.Id, ReceivedOn = r.ReceivedOn, Amount = Math.Round(r.Amount, 2), Tds = Math.Round(r.Tds, 2), Mode = r.Mode,
            Reference = Clean(r.Reference), Note = Clean(r.Note), RecordedByName = me.Name ?? "Unknown",
        };
        db.Add(p);
        if (!i.Payments.Contains(p)) i.Payments.Add(p); // EF usually adds it already (relationship fix-up)
        i.RecalculatePayments();
        i.Version++;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<InvoiceDetail>> RemovePaymentAsync(Guid id, Guid paymentId, CancellationToken ct = default)
    {
        var i = await db.Set<Invoice>().Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct);
        var p = i?.Payments.FirstOrDefault(x => x.Id == paymentId);
        if (i is null || p is null) return Error.NotFound("Payment not found.");
        db.Remove(p); // soft delete: the record stays for the audit trail
        i.Payments.Remove(p);
        i.RecalculatePayments();
        i.Version++;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<InvoicePdf>> PdfAsync(Guid id, CancellationToken ct = default)
    {
        var r = await GetAsync(id, ct);
        if (!r.IsSuccess) return r.Error!;
        var d = r.Value!;
        var settings = await SettingsAsync(ct);
        var t = await db.Set<Tenant>().AsNoTracking().FirstAsync(x => x.Id == tenant.TenantId, ct);
        byte[]? logo = null;
        if (t.LogoBlobPath is { } path)
        {
            await using var s = await files.OpenReadAsync(path, ct);
            if (s is not null)
            {
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                logo = ms.ToArray();
            }
        }
        var pdf = renderer.Render(d, new InvoiceExtras(logo, t.PrimaryColor, t.Phone, t.ContactEmail, settings.PaymentDetails, settings.InvoiceTerms));
        var name = d.Number is { } n ? n.Replace('/', '-') : $"draft-invoice-{d.InvoiceDate:yyyyMMdd}";
        return new InvoicePdf(pdf, $"{name}.pdf");
    }

    // ---------------- helpers ----------------

    private async Task<InvoiceDetail> DetailAsync(Invoice i, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var t = await db.Set<Tenant>().AsNoTracking().FirstAsync(x => x.Id == tenant.TenantId, ct);
        var caseNumbers = await CaseNumbersAsync(i.Lines, ct);
        var bank = i.BankId is { } b ? await db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking().FirstOrDefaultAsync(x => x.Id == b, ct) : null;
        var caseIds = i.Lines.Where(l => l.AssignmentId != null).Select(l => l.AssignmentId!.Value).ToList();
        var cases = await db.Set<Assignment>().AsNoTracking().Where(a => caseIds.Contains(a.Id)).ToListAsync(ct);
        var options = i.Status == InvoiceStatus.Draft && cases.Count > 0 ? await OptionsAsync(cases, ct) : [];
        var canEdit = i.Status == InvoiceStatus.Draft && me.HasPermission(Permissions.Billing.Manage);
        var stateCode = i.BillToStateCode ?? settings.StateCode;
        var firmGstin = settings.Gstin;
        return new InvoiceDetail(
            i.Id, i.Number, i.Status, i.State(Today), i.Version, i.InvoiceDate, i.DueDate, i.BankId, bank?.Name, i.BillTo, i.ClientId, i.BillToName,
            i.BillToAddress, i.BillToGstin, i.BillToStateCode, GstStates.Name(stateCode) is { } pos ? $"{pos} ({stateCode})" : null,
            i.GstRate, i.InterState, i.Taxable, i.Cgst, i.Sgst, i.Igst, i.RoundOff, i.Total, IndianNumber.ToWords(i.Total),
            i.Received, i.Tds, i.Outstanding, i.Notes,
            [.. i.Lines.OrderBy(l => l.Sort).Select(l => new InvoiceLineItem(l.AssignmentId, l.AssignmentId is { } a ? caseNumbers.GetValueOrDefault(a) : null, l.Description, l.Sac, l.Amount))],
            [.. i.Payments.OrderBy(p => p.ReceivedOn).ThenBy(p => p.CreatedAt).Select(p => new PaymentItem(p.Id, p.ReceivedOn, p.Amount, p.Tds, p.Mode, p.Reference, p.Note, p.RecordedByName, p.CreatedAt))],
            i.IssuedAt, i.IssuedByName, i.CancelledAt, i.CancelledByName, i.CancelReason,
            new FirmBilling(t.Name, string.Join(", ", new[] { t.Address, t.State }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } addr ? addr : null,
                firmGstin, settings.StateCode, GstStates.Name(settings.StateCode), firmGstin is { Length: 15 } g ? g[2..12] : null),
            options, canEdit, i.Status == InvoiceStatus.Draft ? IssueBlocked(i, settings) : null);
    }

    private static string? IssueBlocked(Invoice i, FirmSettings s) =>
        i.Total <= 0 ? "The invoice total is zero."
        : i.GstRate > 0 && string.IsNullOrWhiteSpace(s.Gstin) ? "Add the firm's GSTIN (Settings → GST) before charging GST, or bill at 0%."
        : null;

    /// <summary>Who the cases could be billed to: the bank branch first, then each client on the cases.</summary>
    private async Task<List<BillToOption>> OptionsAsync(List<Assignment> cases, CancellationToken ct)
    {
        var first = cases[0];
        var bank = await db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking().FirstAsync(b => b.Id == first.BankId, ct);
        var branchIds = cases.Where(a => a.BranchId != null).Select(a => a.BranchId!.Value).Distinct().ToList();
        var branch = branchIds.Count == 1
            ? await db.Set<BankBranch>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchIds[0], ct)
            : null;
        var list = new List<BillToOption>
        {
            new(BillTo.Bank, null, bank.Name,
                Join(", ", branch is null ? null : branch.Name.Contains("branch", StringComparison.OrdinalIgnoreCase) ? branch.Name : $"{branch.Name} Branch", branch?.Address, branch?.City, branch?.State),
                null, GstStates.CodeOf(branch?.State), branch is null ? bank.Name : $"{bank.Name}, {branch.Name}"),
        };
        var clientIds = cases.Where(a => a.ClientId != null).Select(a => a.ClientId!.Value).Distinct().ToList();
        var clients = await db.Set<Client>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToListAsync(ct);
        list.AddRange(clients.Select(c => new BillToOption(BillTo.Client, c.Id, c.Name, Join(", ", c.Address, c.City, c.State, c.Pincode), c.Gstin,
            c.Gstin is { Length: >= 2 } g ? g[..2] : GstStates.CodeOf(c.State), $"{c.Name} (client {c.Code})")));
        return list;
    }

    private async Task<List<string>> IssuedCaseNumbersAsync(List<Guid> caseIds, Guid? except, CancellationToken ct)
    {
        var ids = await IssuedCaseIdsAsync(caseIds, except, ct);
        return ids.Count == 0 ? [] : await db.Set<Assignment>().Where(a => ids.Contains(a.Id)).Select(a => a.Number).ToListAsync(ct);
    }

    private async Task<List<Guid>> IssuedCaseIdsAsync(List<Guid> caseIds, Guid? except, CancellationToken ct) =>
        await (from l in db.Set<InvoiceLine>()
               join i in db.Set<Invoice>() on l.InvoiceId equals i.Id
               where i.Status == InvoiceStatus.Issued && i.Id != except && l.AssignmentId != null && caseIds.Contains(l.AssignmentId.Value)
               select l.AssignmentId!.Value).Distinct().ToListAsync(ct);

    private async Task<Dictionary<Guid, string>> CaseNumbersAsync(IEnumerable<InvoiceLine> lines, CancellationToken ct)
    {
        var ids = lines.Where(l => l.AssignmentId != null).Select(l => l.AssignmentId!.Value).Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Set<Assignment>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Number, ct);
    }

    private async Task<Dictionary<Guid, Bank>> BanksAsync(CancellationToken ct) =>
        await db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking().ToDictionaryAsync(b => b.Id, ct);

    private async Task<FirmSettings> SettingsAsync(CancellationToken ct) =>
        await db.Set<FirmSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new FirmSettings();

    private void AddEvent(Assignment a, AssignmentStatus to, string title, string? note) =>
        db.Add(new AssignmentEvent { AssignmentId = a.Id, At = clock.GetUtcNow(), ByUserId = me.UserId, ByName = me.Name ?? "System", FromStatus = a.Status, ToStatus = to, Title = title, Note = note });

    private static string CaseKind(CaseType t) => t switch
    {
        CaseType.StageInspection => "Stage inspection",
        CaseType.Reinspection => "Re-inspection",
        _ => "Valuation",
    };

    private static string? Join(string sep, params string?[] parts)
    {
        var s = string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return s.Length == 0 ? null : s;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
