using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Billing;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Application.Dashboard;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Settings;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Billing;
using ValuPro.Infrastructure.Dashboard;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class DashboardTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    /// <summary>26 Nov 2026, 11:30 in India.</summary>
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 26, 6, 0, 0, TimeSpan.Zero); }

    private sealed class Me : ICurrentUser
    {
        public static readonly Guid Id = Guid.NewGuid();
        public static bool Billing = true;
        public Guid? UserId => Id;
        public string? Name => "Anjali Shah";
        public bool IsAuthenticated => true;
        public string? Email => null;
        public bool HasPermission(string permission) => Billing || !permission.StartsWith("billing", StringComparison.Ordinal);
    }

    private sealed class NoFiles : IFileStorage
    {
        public Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private static readonly DateOnly Today = new(2026, 11, 26);

    private readonly FixedTenant _tenant = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _hdfc, _sbi, _other;

    private IDashboardService Svc => _scope.ServiceProvider.GetRequiredService<IDashboardService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        Me.Billing = true;
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
        services.AddScoped<IDashboardService, DashboardService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var hdfc = new Bank { Name = "HDFC Bank", ShortCode = "HDFC" };
        var sbi = new Bank { Name = "State Bank of India", ShortCode = "SBI" };
        var other = new AppUser { FullName = "Rahul Mehta", UserName = "rahul", TenantId = _tenant.TenantId };
        Db.AddRange(hdfc, sbi, other,
            new AppUser { Id = Me.Id, FullName = "Anjali Shah", UserName = "anjali", TenantId = _tenant.TenantId },
            new Tenant { Id = _tenant.TenantId, Name = "Om Engineers", Slug = "om", Address = "Surat", State = "Gujarat" },
            new FirmSettings { Gstin = "24ABCDE1234F1Z5", StateCode = "24", DefaultGstRate = 18, InvoicePrefix = "INV" });
        await Db.SaveChangesAsync();
        (_hdfc, _sbi, _other) = (hdfc.Id, sbi.Id, other.Id);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Assignment> CaseAsync(string number, Guid bank, AssignmentStatus status, DateOnly due, Guid? valuer = null,
        Guid? reviewer = null, DateTimeOffset? visit = null, decimal fee = 5000)
    {
        var a = new Assignment
        {
            Number = number, Status = status, BankId = bank, ApplicantName = "Applicant " + number, PropertySummary = "Flat, Vesu",
            ValuerId = valuer ?? Me.Id, ReviewerId = reviewer, DueDate = due, SiteVisitAt = visit, Fee = fee,
        };
        Db.Add(a);
        await Db.SaveChangesAsync();
        return a;
    }

    private async Task SubmittedOnAsync(Assignment a, DateTimeOffset at)
    {
        Db.Add(new AssignmentEvent { TenantId = _tenant.TenantId, AssignmentId = a.Id, At = at, ByName = "Anjali Shah", FromStatus = AssignmentStatus.QualityReview, ToStatus = AssignmentStatus.Submitted, Title = "Moved to Submitted" });
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Counts_attention_visits_banks_and_workload_for_the_firm()
    {
        await CaseAsync("A1", _hdfc, AssignmentStatus.DraftPrepared, Today.AddDays(-2));                       // overdue
        await CaseAsync("A2", _hdfc, AssignmentStatus.New, Today, valuer: _other);                              // due today
        await CaseAsync("A3", _sbi, AssignmentStatus.SiteVisitScheduled, Today.AddDays(2),
            visit: new DateTimeOffset(2026, 11, 26, 15, 0, 0, India));                                           // soon + visit today
        await CaseAsync("A4", _sbi, AssignmentStatus.QualityReview, Today.AddDays(10), valuer: _other, reviewer: Me.Id); // my review
        await CaseAsync("A5", _sbi, AssignmentStatus.InspectionDone, Today.AddDays(20), valuer: _other,
            visit: new DateTimeOffset(2026, 11, 20, 10, 0, 0, India));                                           // past visit: not listed
        var done = await CaseAsync("A6", _hdfc, AssignmentStatus.Submitted, Today.AddDays(-5), fee: 7500);
        await SubmittedOnAsync(done, new DateTimeOffset(2026, 11, 24, 12, 0, 0, India));
        var lastMonth = await CaseAsync("A7", _hdfc, AssignmentStatus.InvoiceGenerated, Today.AddDays(-30));
        await SubmittedOnAsync(lastMonth, new DateTimeOffset(2026, 10, 31, 23, 0, 0, India)); // 31 Oct in India, though 17:30 UTC
        await CaseAsync("A8", _hdfc, AssignmentStatus.Cancelled, Today.AddDays(-9));

        var s = await Svc.SummaryAsync(mine: false);

        Assert.False(s.Mine);
        Assert.Equal(Today, s.Today);
        Assert.Equal(new DashboardKpis(5, 1, 2, 1, 1, 8, 1, 1), s.Kpis);
        Assert.Equal(["A1", "A2", "A4", "A3"], s.Attention.Select(a => a.Number));
        Assert.Equal(["overdue", "today", "review", "soon"], s.Attention.Select(a => a.Reason));
        Assert.Equal(2, s.Attention[0].Days);
        Assert.Equal("Rahul Mehta", s.Attention[1].ValuerName);
        Assert.Equal(["A3"], s.Visits.Select(v => v.Number));
        Assert.Equal([("SBI", 3, 0), ("HDFC", 2, 1)], s.ByBank.Select(b => (b.ShortCode, b.Open, b.Overdue)));
        Assert.Equal([("Rahul Mehta", 3, 0, 1), ("Anjali Shah", 2, 1, 1)], s.Workload.Select(w => (w.Name, w.Open, w.Overdue, w.DueThisWeek)));
        Assert.Equal(6, s.Trend.Count);
        Assert.Equal(("Nov", 8, 1, 7500m), (s.Trend[^1].Month, s.Trend[^1].Received, s.Trend[^1].Completed, s.Trend[^1].Fees));
        Assert.Equal(("Oct", 1), (s.Trend[^2].Month, s.Trend[^2].Completed));
        Assert.Equal(1, s.Pipeline.Single(p => p.Status == AssignmentStatus.Submitted).Count);
        Assert.Equal("A6", s.Activity[0].Number);
        Assert.NotNull(s.Money);
        Assert.Equal(1, s.Money!.ToBillCount);
        Assert.Equal(7500m, s.Money.ToBillFees);
    }

    [Fact]
    public async Task Mine_keeps_only_my_cases_and_hides_workload_and_money()
    {
        await CaseAsync("M1", _hdfc, AssignmentStatus.New, Today.AddDays(-1));
        await CaseAsync("M2", _sbi, AssignmentStatus.QualityReview, Today.AddDays(9), valuer: _other, reviewer: Me.Id);
        await CaseAsync("O1", _sbi, AssignmentStatus.New, Today.AddDays(-3), valuer: _other);

        var s = await Svc.SummaryAsync(mine: true);

        Assert.True(s.Mine);
        Assert.Equal(2, s.Kpis.Open);
        Assert.Equal(1, s.Kpis.Overdue);
        Assert.Equal(["M1", "M2"], s.Attention.Select(a => a.Number));
        Assert.Empty(s.Workload);
        Assert.Null(s.Money);
    }

    [Fact]
    public async Task Money_needs_billing_permission()
    {
        Me.Billing = false;
        await CaseAsync("A1", _hdfc, AssignmentStatus.New, Today);
        Assert.Null((await Svc.SummaryAsync(mine: false)).Money);
    }
}
