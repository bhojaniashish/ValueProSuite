using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Assignments;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Application.Properties;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Assignments;
using ValuPro.Infrastructure.Clients;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Properties;

namespace ValuPro.UnitTests;

public class AssignmentTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    /// <summary>12 Jun 2026 from 10:00 IST, one second later on every reading (so timeline entries have distinct times).</summary>
    private sealed class FixedClock : TimeProvider
    {
        private int _ticks;
        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 6, 12, 4, 30, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref _ticks));
    }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? Name { get; set; } = "Test User";
        public HashSet<string> Grants { get; set; } = [.. Permissions.All];
        public bool IsAuthenticated => true;
        public string? Email => null;
        public bool HasPermission(string permission) => Grants.Contains(permission);
    }

    private static readonly DateOnly Today = new(2026, 6, 12);

    private readonly Me _me = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _admin, _senior, _junior, _reviewer, _bankUser;
    private Guid _sbi, _sbiVesu, _hdfcBranch;

    private IAssignmentService Svc => _scope.ServiceProvider.GetRequiredService<IAssignmentService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<TimeProvider, FixedClock>();
        services.AddSingleton<ITenantContext, FixedTenant>();
        services.AddSingleton<ICurrentUser>(_me);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddIdentityCore<AppUser>(o => o.Password.RequireNonAlphanumeric = false).AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<INumberSequence, NumberSequenceService>();
        services.AddScoped<IAssignmentUsage, AssignmentUsage>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<FluentValidation.IValidator<SaveClientRequest>, SaveClientRequestValidator>();
        services.AddScoped<IClientService, ClientService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        foreach (var r in Roles.All) await roles.CreateAsync(new AppRole(r));
        _admin = await UserAsync("Rajesh Patel", Roles.Admin);
        _senior = await UserAsync("Sneha Kulkarni", Roles.SeniorValuer);
        _junior = await UserAsync("Priya Sharma", Roles.JuniorValuer);
        _reviewer = await UserAsync("Neha Joshi", Roles.Reviewer);
        _bankUser = await UserAsync("Anjali Rao", Roles.BankUser);
        _me.UserId = _admin;

        var sbi = new Bank { Name = "State Bank of India", ShortCode = "SBI" };
        var hdfc = new Bank { Name = "HDFC Bank", ShortCode = "HDFC" };
        var vesu = new BankBranch { Bank = sbi, Name = "Vesu" };
        var hdfcBranch = new BankBranch { Bank = hdfc, Name = "Adajan" };
        Db.AddRange(sbi, hdfc, vesu, hdfcBranch);
        await Db.SaveChangesAsync();
        (_sbi, _sbiVesu, _hdfcBranch) = (sbi.Id, vesu.Id, hdfcBranch.Id);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> UserAsync(string name, string role)
    {
        var users = _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var u = new AppUser { FullName = name, UserName = name.Replace(" ", "") + "@x.in", Email = name.Replace(" ", "") + "@x.in" };
        await users.CreateAsync(u);
        await users.AddToRoleAsync(u, role);
        return u.Id;
    }

    private SaveAssignmentRequest Req(Guid? valuer = null, Guid? branch = null, DateOnly? due = null, Guid? reviewer = null, Guid? client = null) =>
        new(_sbi, branch ?? _sbiVesu, "HL-2026-118", "Home loan", "Suresh Agarwal", client, null, "Residential Flat, Vesu, Surat", null,
            valuer ?? _senior, reviewer, Priority.High, due ?? Today.AddDays(5), null, 5000m, null);

    private async Task<AssignmentDetail> CreateAsync(SaveAssignmentRequest? r = null) => (await Svc.CreateAsync(r ?? Req())).Value!;

    private async Task<Result<AssignmentDetail>> MoveAsync(Guid id, AssignmentStatus to, string? note = null, DateTimeOffset? visit = null) =>
        await Svc.MoveAsync(id, new MoveRequest(to, note, visit));

    [Fact]
    public async Task Numbers_follow_prefix_year_and_counter_and_creation_is_logged()
    {
        var a = await CreateAsync();
        var b = await CreateAsync();

        Assert.Equal("VAL/2026/0001", a.Assignment.Number);
        Assert.Equal("VAL/2026/0002", b.Assignment.Number);
        Assert.Equal("Assignment created", a.Events.Single().Title);
        Assert.Equal("Assigned to Sneha Kulkarni", a.Events.Single().Note);
        Assert.Equal("VAL/2026/0003", (await Svc.GetLookupsAsync()).NumberPreview);
    }

    [Fact]
    public async Task Links_are_checked()
    {
        Assert.Equal("dueDate", (await Svc.CreateAsync(Req(due: Today.AddDays(-1)))).Error!.Field);
        Assert.Equal("branchId", (await Svc.CreateAsync(Req(branch: _hdfcBranch))).Error!.Field);
        Assert.Equal("valuerId", (await Svc.CreateAsync(Req(valuer: _bankUser))).Error!.Field);
        Assert.Equal("reviewerId", (await Svc.CreateAsync(Req(valuer: _senior, reviewer: _senior))).Error!.Field);
        Assert.Equal("reviewerId", (await Svc.CreateAsync(Req(reviewer: _junior))).Error!.Field);
    }

    [Fact]
    public async Task Moves_one_step_at_a_time_and_site_visit_needs_a_date()
    {
        var a = await CreateAsync();

        Assert.Equal(ErrorKind.Validation, (await MoveAsync(a.Assignment.Id, AssignmentStatus.InspectionDone)).Error!.Kind);
        Assert.Equal("siteVisitAt", (await MoveAsync(a.Assignment.Id, AssignmentStatus.SiteVisitScheduled)).Error!.Field);

        var visit = new DateTimeOffset(2026, 6, 14, 11, 0, 0, TimeSpan.FromHours(5.5));
        var moved = (await MoveAsync(a.Assignment.Id, AssignmentStatus.SiteVisitScheduled, visit: visit)).Value!;
        Assert.Equal(AssignmentStatus.SiteVisitScheduled, moved.Assignment.Status);
        Assert.Equal("Site visit scheduled for 14 Jun, 11:00 AM", moved.Events.First().Title);
        Assert.Equal(AssignmentStatus.InspectionDone, moved.Moves.Next);
    }

    [Fact]
    public async Task Going_back_or_cancelling_needs_a_reason_and_cancelled_can_be_reopened()
    {
        var a = await CreateAsync(Req() with { SiteVisitAt = DateTimeOffset.UtcNow });
        var id = a.Assignment.Id;
        await MoveAsync(id, AssignmentStatus.SiteVisitScheduled);

        Assert.Equal("note", (await MoveAsync(id, AssignmentStatus.New)).Error!.Field);
        Assert.Equal("Returned to New Assignment", (await MoveAsync(id, AssignmentStatus.New, "Bank changed the address")).Value!.Events.First().Title);

        Assert.Equal("note", (await MoveAsync(id, AssignmentStatus.Cancelled)).Error!.Field);
        var cancelled = (await MoveAsync(id, AssignmentStatus.Cancelled, "Loan withdrawn")).Value!;
        Assert.False(cancelled.Moves.CanEdit);
        Assert.True(cancelled.Moves.CanReopen);
        Assert.Equal(AssignmentStatus.New, (await MoveAsync(id, AssignmentStatus.New)).Value!.Assignment.Status);
    }

    [Fact]
    public async Task Approving_after_quality_review_needs_report_approval()
    {
        var a = await CreateAsync(Req() with { SiteVisitAt = DateTimeOffset.UtcNow });
        var id = a.Assignment.Id;
        foreach (var s in new[] { AssignmentStatus.SiteVisitScheduled, AssignmentStatus.InspectionDone, AssignmentStatus.DraftPrepared, AssignmentStatus.QualityReview })
            Assert.True((await MoveAsync(id, s)).IsSuccess);

        _me.Grants.Remove(Permissions.Reports.Approve);
        var blocked = await MoveAsync(id, AssignmentStatus.Submitted);
        Assert.Equal(ErrorKind.Forbidden, blocked.Error!.Kind);

        _me.Grants.Add(Permissions.Reports.Approve);
        Assert.Equal(AssignmentStatus.Submitted, (await MoveAsync(id, AssignmentStatus.Submitted)).Value!.Assignment.Status);
    }

    [Fact]
    public async Task Assigned_valuer_can_move_own_case_but_others_cannot()
    {
        var a = await CreateAsync(Req(valuer: _junior) with { SiteVisitAt = DateTimeOffset.UtcNow });
        _me.Grants = [Permissions.Assignments.View]; // Junior Valuer: view only

        _me.UserId = _senior; // not assigned
        Assert.Equal(ErrorKind.Forbidden, (await MoveAsync(a.Assignment.Id, AssignmentStatus.SiteVisitScheduled)).Error!.Kind);

        _me.UserId = _junior; // assigned
        var moved = (await MoveAsync(a.Assignment.Id, AssignmentStatus.SiteVisitScheduled)).Value!;
        Assert.False(moved.Moves.CanCancel);
        Assert.False(moved.Moves.CanEdit);
    }

    [Fact]
    public async Task Only_new_assignments_can_be_deleted()
    {
        var a = await CreateAsync(Req() with { SiteVisitAt = DateTimeOffset.UtcNow });
        await MoveAsync(a.Assignment.Id, AssignmentStatus.SiteVisitScheduled);
        Assert.Equal(ErrorKind.Conflict, (await Svc.DeleteAsync(a.Assignment.Id)).Error!.Kind);

        var b = await CreateAsync();
        Assert.True((await Svc.DeleteAsync(b.Assignment.Id)).IsSuccess);
        Assert.Equal("VAL/2026/0003", (await CreateAsync()).Assignment.Number); // numbers are not reused
    }

    [Fact]
    public async Task Overdue_filter_and_pipeline_counts()
    {
        var overdue = await CreateAsync();
        // Make it overdue by moving the due date into the past directly (the API refuses past dates on create).
        var entity = await Db.Set<Assignment>().FirstAsync(x => x.Id == overdue.Assignment.Id);
        entity.DueDate = Today.AddDays(-2);
        await Db.SaveChangesAsync();
        await CreateAsync(Req(due: Today.AddDays(3)));
        await CreateAsync(Req(due: Today.AddDays(30)));

        var list = await Svc.ListAsync(new AssignmentQuery(Overdue: true));
        Assert.True(list.Items.Single().IsOverdue);

        var p = await Svc.GetPipelineAsync(false);
        Assert.Equal((3, 1, 1), (p.Open, p.Overdue, p.DueThisWeek));
        Assert.Equal(3, p.Stages.Single(s => s.Status == AssignmentStatus.New).Count);
    }

    [Fact]
    public async Task Clients_and_banks_with_open_cases_cannot_be_removed()
    {
        var client = new Client { Code = "CL0001", Name = "Suresh Agarwal", Mobile = "9825041201" };
        Db.Add(client);
        await Db.SaveChangesAsync();
        await CreateAsync(Req(client: client.Id));

        var clientDelete = await _scope.ServiceProvider.GetRequiredService<IClientService>().DeleteAsync(client.Id);
        Assert.Equal(ErrorKind.Conflict, clientDelete.Error!.Kind);
        Assert.Contains("1 open assignment", clientDelete.Error.Message);

        var bankDelete = await new ValuPro.Infrastructure.Banks.BankService(Db, new AssignmentUsage(Db)).DeleteAsync(_sbi);
        Assert.Equal(ErrorKind.Conflict, bankDelete.Error!.Kind);
    }

    [Fact]
    public async Task Property_history_lists_its_assignments()
    {
        var props = _scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var p = (await props.CreateAsync(new SavePropertyRequest(PropertyType.ResidentialFlat, "Suresh Agarwal", null, "Flat 904, Vesu",
            "Surat", "Gujarat", "395007", "Sy. 120/1", null, null, 1200, 950, "Residential R1", PropertyUsage.SelfOccupied, 21.1702m, 72.8311m, null))).Value!;
        Assert.Equal("PR0001", p.Code);

        var option = (await Svc.SearchPropertiesAsync("Vesu")).Single();
        Assert.Equal("Residential Flat, Flat 904, Vesu, Surat", option.Summary);

        await CreateAsync(Req() with { PropertyId = p.Id, PropertySummary = option.Summary });
        Assert.Single((await props.GetAsync(p.Id)).Value!.Assignments);
        Assert.Equal(ErrorKind.Conflict, (await props.DeleteAsync(p.Id)).Error!.Kind);
    }

    [Theory]
    [InlineData(21.17, 72.83, true)]
    [InlineData(72.83, 21.17, false)] // swapped
    public void Property_coordinates_must_be_in_india(double lat, double lng, bool valid) =>
        Assert.Equal(valid, new SavePropertyRequestValidator().Validate(new SavePropertyRequest(PropertyType.OpenPlot, "Owner", null, "Plot 12",
            null, null, null, null, null, null, null, null, null, null, (decimal)lat, (decimal)lng, null)).IsValid);
}
