using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using ValuPro.Infrastructure.Billing;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class BillingTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 26, 6, 0, 0, TimeSpan.Zero); }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Name => "Anjali Shah";
        public bool IsAuthenticated => true;
        public string? Email => null;
        public bool HasPermission(string permission) => true;
    }

    private sealed class NoFiles : IFileStorage
    {
        public Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly FixedTenant _tenant = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _hdfc, _sbi, _client;

    private IBillingService Svc => _scope.ServiceProvider.GetRequiredService<IBillingService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider, FixedClock>();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser, Me>();
        services.AddSingleton<IFileStorage, NoFiles>();
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddScoped<INumberSequence, NumberSequenceService>();
        services.AddSingleton<IInvoiceRenderer, InvoiceTemplate>();
        services.AddScoped<IBillingService, BillingService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var hdfc = new Bank { Name = "HDFC Bank", ShortCode = "HDFC" };
        var sbi = new Bank { Name = "State Bank of India", ShortCode = "SBI" };
        var branch = new BankBranch { BankId = hdfc.Id, Name = "Vesu", Address = "Vesu Main Road", City = "Surat", State = "Gujarat" };
        var client = new Client { Code = "CL0001", Name = "Suresh Agarwal", Mobile = "9825000000", Address = "A-902 Shanti Heights", City = "Mumbai", State = "Maharashtra" };
        Db.AddRange(hdfc, sbi, branch, client,
            new Tenant { Id = _tenant.TenantId, Name = "Om Engineers", Slug = "om", Address = "Surat", State = "Gujarat" },
            new FirmSettings { Gstin = "24ABCDE1234F1Z5", StateCode = "24", DefaultGstRate = 18, InvoicePrefix = "INV", PaymentDueDays = 15 });
        await Db.SaveChangesAsync();
        (_hdfc, _sbi, _client) = (hdfc.Id, sbi.Id, client.Id);
        _branch = branch.Id;
    }

    private Guid _branch;

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> CaseAsync(Guid bank, decimal fee = 7500, AssignmentStatus status = AssignmentStatus.Submitted, bool withClient = false)
    {
        var a = new Assignment
        {
            Number = $"VAL/2026/{Random.Shared.Next(1000, 9999)}", Status = status, BankId = bank, BranchId = bank == _hdfc ? _branch : null,
            ApplicantName = "Suresh", PropertySummary = "Flat, Vesu", ValuerId = Guid.NewGuid(), DueDate = new DateOnly(2026, 12, 1), Fee = fee,
            ClientId = withClient ? _client : null,
        };
        Db.Add(a);
        await Db.SaveChangesAsync();
        return a.Id;
    }

    private static SaveInvoiceRequest Edit(InvoiceDetail d) => new(d.Version, d.InvoiceDate, d.DueDate, d.BillTo, d.ClientId, d.BillToName,
        d.BillToAddress, d.BillToGstin, d.BillToStateCode, d.GstRate, [.. d.Lines.Select(l => new SaveInvoiceLine(l.AssignmentId, l.Description, l.Sac, l.Amount))], d.Notes);

    private async Task<AssignmentStatus> StatusAsync(Guid id) => (await Db.Set<Assignment>().AsNoTracking().SingleAsync(a => a.Id == id)).Status;

    [Fact]
    public void Gst_is_split_within_the_state_and_igst_across_states()
    {
        var intra = InvoiceMath.Totals([7500], 18, interState: false);
        Assert.Equal((675m, 675m, 0m, 8850m), (intra.Cgst, intra.Sgst, intra.Igst, intra.Total));
        var inter = InvoiceMath.Totals([7500], 18, interState: true);
        Assert.Equal((0m, 1350m, 8850m), (inter.Cgst, inter.Igst, inter.Total));
        var odd = InvoiceMath.Totals([3333.33m], 18, false); // 300.00 + 300.00 → 3933.33 → 3933 with -0.33 round-off
        Assert.Equal(3933m, odd.Total);
        Assert.Equal(-0.33m, odd.RoundOff);
        Assert.Equal("25-26", InvoiceMath.FinancialYear(new DateOnly(2026, 3, 31)));
        Assert.Equal("26-27", InvoiceMath.FinancialYear(new DateOnly(2026, 4, 1)));
        Assert.Equal("24", GstStates.CodeOf("gujarat"));
    }

    [Fact]
    public async Task A_draft_bills_one_banks_cases_to_the_branch()
    {
        var a = await CaseAsync(_hdfc, 7500);
        var b = await CaseAsync(_hdfc, 5000);
        var other = await CaseAsync(_sbi);
        Assert.Equal("assignmentIds", (await Svc.CreateAsync(new CreateInvoiceRequest([a, other]))).Error!.Field);

        var d = (await Svc.CreateAsync(new CreateInvoiceRequest([a, b]))).Value!;
        Assert.Equal(InvoiceStatus.Draft, d.Status);
        Assert.Null(d.Number);
        Assert.Equal("HDFC Bank", d.BillToName);
        Assert.Equal("Vesu Branch, Vesu Main Road, Surat, Gujarat", d.BillToAddress);
        Assert.Equal("24", d.BillToStateCode);
        Assert.Equal(2, d.Lines.Count);
        Assert.All(d.Lines, l => Assert.Equal("997222", l.Sac));
        Assert.Equal(12500m, d.Taxable);
        Assert.Equal(14750m, d.Total);
        Assert.False(d.InterState);
        Assert.Equal(new DateOnly(2026, 12, 11), d.DueDate); // 15 days

        var unbilled = await Svc.UnbilledAsync(_hdfc);
        Assert.All(unbilled, u => Assert.Equal(d.Id.ToString(), u.DraftInvoiceId));
    }

    [Fact]
    public async Task Billing_the_client_in_another_state_charges_igst()
    {
        var a = await CaseAsync(_hdfc, 10000, withClient: true);
        var d = (await Svc.CreateAsync(new CreateInvoiceRequest([a], BillTo.Client))).Value!;
        Assert.Equal("Suresh Agarwal", d.BillToName);
        Assert.Equal("27", d.BillToStateCode);
        Assert.True(d.InterState);
        Assert.Equal(1800m, d.Igst);
        Assert.Equal(2, d.BillToOptions.Count);
    }

    [Fact]
    public async Task Issuing_numbers_the_invoice_and_moves_the_cases()
    {
        var a = await CaseAsync(_hdfc);
        var d = (await Svc.CreateAsync(new CreateInvoiceRequest([a]))).Value!;
        var edited = (await Svc.SaveAsync(d.Id, Edit(d) with
        {
            Lines = [.. Edit(d).Lines, new SaveInvoiceLine(null, "Conveyance", "997222", 500)],
            Notes = "Thank you",
        })).Value!;
        Assert.Equal(8000m, edited.Taxable);
        Assert.Equal(ErrorKind.Conflict, (await Svc.SaveAsync(d.Id, Edit(d))).Error!.Kind); // stale version

        var issued = (await Svc.IssueAsync(d.Id)).Value!;
        Assert.Equal("INV/26-27/0001", issued.Number);
        Assert.Equal(PaymentState.Unpaid, issued.State);
        Assert.Equal(AssignmentStatus.InvoiceGenerated, await StatusAsync(a));
        Assert.True(await Db.Set<AssignmentEvent>().AnyAsync(e => e.AssignmentId == a && e.Title == "Invoice issued"));
        Assert.Equal(ErrorKind.Conflict, (await Svc.SaveAsync(d.Id, Edit(issued))).Error!.Kind); // issued = final

        Assert.Equal(ErrorKind.Conflict, (await Svc.CreateAsync(new CreateInvoiceRequest([a]))).Error!.Kind); // already billed
        var b = await CaseAsync(_sbi);
        var second = (await Svc.IssueAsync((await Svc.CreateAsync(new CreateInvoiceRequest([b]))).Value!.Id)).Value!;
        Assert.Equal("INV/26-27/0002", second.Number);
        Assert.Empty(await Svc.UnbilledAsync(null));
    }

    [Fact]
    public async Task Gst_needs_the_firms_gstin()
    {
        var s = await Db.Set<FirmSettings>().SingleAsync();
        s.Gstin = null;
        await Db.SaveChangesAsync();
        var d = (await Svc.CreateAsync(new CreateInvoiceRequest([await CaseAsync(_hdfc)]))).Value!;
        Assert.Contains("GSTIN", d.IssueBlockedReason);
        Assert.Contains("GSTIN", (await Svc.IssueAsync(d.Id)).Error!.Message);
        var exempt = (await Svc.SaveAsync(d.Id, Edit(d) with { GstRate = 0 })).Value!;
        Assert.Null(exempt.IssueBlockedReason);
        Assert.Equal(7500m, exempt.Total);
    }

    [Fact]
    public async Task Payments_with_tds_settle_the_invoice_and_block_cancelling()
    {
        var a = await CaseAsync(_hdfc);
        var id = (await Svc.CreateAsync(new CreateInvoiceRequest([a]))).Value!.Id;
        Assert.Equal(ErrorKind.Conflict, (await Svc.AddPaymentAsync(id, new AddPaymentRequest(new DateOnly(2026, 11, 20), 100, 0, PaymentMode.Upi, null, null))).Error!.Kind);
        await Svc.IssueAsync(id);

        var part = (await Svc.AddPaymentAsync(id, new AddPaymentRequest(new DateOnly(2026, 11, 25), 4000, 0, PaymentMode.BankTransfer, "UTR123", null))).Value!;
        Assert.Equal(PaymentState.PartlyPaid, part.State);
        Assert.Equal(4850m, part.Outstanding);
        Assert.Equal("amount", (await Svc.AddPaymentAsync(id, new AddPaymentRequest(new DateOnly(2026, 11, 25), 5000, 0, PaymentMode.Upi, null, null))).Error!.Field);
        Assert.Equal("receivedOn", (await Svc.AddPaymentAsync(id, new AddPaymentRequest(new DateOnly(2026, 12, 25), 1, 0, PaymentMode.Upi, null, null))).Error!.Field);

        var paid = (await Svc.AddPaymentAsync(id, new AddPaymentRequest(new DateOnly(2026, 11, 26), 4100, 750, PaymentMode.BankTransfer, null, "TDS 194J"))).Value!;
        Assert.Equal(PaymentState.Paid, paid.State);
        Assert.Equal(0m, paid.Outstanding);
        Assert.Equal(750m, paid.Tds);

        Assert.Contains("Payments", (await Svc.CancelAsync(id, new CancelInvoiceRequest("Wrong GSTIN"))).Error!.Message);
        foreach (var p in paid.Payments) await Svc.RemovePaymentAsync(id, p.Id);
        var cancelled = (await Svc.CancelAsync(id, new CancelInvoiceRequest("Wrong GSTIN"))).Value!;
        Assert.Equal(PaymentState.Cancelled, cancelled.State);
        Assert.Equal(AssignmentStatus.Submitted, await StatusAsync(a));
        Assert.Single(await Svc.UnbilledAsync(_hdfc)); // billable again
    }

    [Fact]
    public async Task The_summary_shows_outstanding_overdue_and_unbilled()
    {
        var a = await CaseAsync(_hdfc);
        var d = (await Svc.CreateAsync(new CreateInvoiceRequest([a]))).Value!;
        d = (await Svc.SaveAsync(d.Id, Edit(d) with { InvoiceDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 31) })).Value!;
        await Svc.IssueAsync(d.Id);
        await Svc.AddPaymentAsync(d.Id, new AddPaymentRequest(new DateOnly(2026, 11, 2), 2000, 0, PaymentMode.Cheque, "000123", null));
        await CaseAsync(_sbi, 6000); // unbilled

        var s = await Svc.SummaryAsync();
        Assert.Equal("26-27", s.FinancialYear);
        Assert.Equal(8850m, s.Invoiced);
        Assert.Equal(2000m, s.Collected);
        Assert.Equal(6850m, s.Outstanding);
        Assert.Equal(6850m, s.Overdue);
        Assert.Equal(1, s.OverdueCount);
        Assert.Equal((1, 6000m), (s.UnbilledCount, s.UnbilledFees));
        Assert.Equal("HDFC", Assert.Single(s.ByBank).ShortCode);
        Assert.Equal(8850m, s.Months.Single(m => m.Month == "Oct").Invoiced);

        var overdue = await Svc.ListAsync(new InvoiceQuery(State: "overdue"));
        Assert.Equal(PaymentState.Overdue, Assert.Single(overdue.Items).State);
        Assert.Single((await Svc.ListAsync(new InvoiceQuery(AssignmentId: a))).Items);
        Assert.Empty((await Svc.ListAsync(new InvoiceQuery(State: "paid"))).Items);
    }

    [Fact]
    public async Task A_draft_can_be_deleted_and_any_invoice_rendered()
    {
        var d = (await Svc.CreateAsync(new CreateInvoiceRequest([await CaseAsync(_hdfc)]))).Value!;
        var pdf = (await Svc.PdfAsync(d.Id)).Value!;
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        Assert.StartsWith("draft-invoice", pdf.FileName);
        Assert.True((await Svc.DeleteDraftAsync(d.Id)).Value);
        Assert.Equal(ErrorKind.NotFound, (await Svc.GetAsync(d.Id)).Error!.Kind);
    }

    [Fact]
    public void Requests_are_validated()
    {
        var v = new SaveInvoiceRequestValidator();
        var ok = new SaveInvoiceRequest(1, new DateOnly(2026, 11, 26), new DateOnly(2026, 12, 26), BillTo.Bank, null, "HDFC Bank", null,
            "24AAACH2702H1ZB", "24", 18, [new SaveInvoiceLine(null, "Fee", "997222", 7500)], null);
        Assert.True(v.Validate(ok).IsValid);
        Assert.False(v.Validate(ok with { BillToStateCode = "27" }).IsValid); // GSTIN says 24
        Assert.False(v.Validate(ok with { GstRate = 10 }).IsValid);
        Assert.False(v.Validate(ok with { DueDate = new DateOnly(2026, 11, 1) }).IsValid);
        Assert.False(v.Validate(ok with { Lines = [] }).IsValid);
        Assert.False(new AddPaymentRequestValidator().Validate(new AddPaymentRequest(new DateOnly(2026, 11, 1), 0, 0, PaymentMode.Upi, null, null)).IsValid);
    }
}
