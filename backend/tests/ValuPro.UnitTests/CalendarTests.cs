using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Assignments;
using ValuPro.Application.Calendar;
using ValuPro.Application.Clients;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Billing;
using ValuPro.Domain.Calendar;
using ValuPro.Domain.Security;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Assignments;
using ValuPro.Infrastructure.Calendar;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class CalendarTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    /// <summary>26 Nov 2026, 11:30 in India.</summary>
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 26, 6, 0, 0, TimeSpan.Zero); }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? Name => "Anjali Shah";
        public HashSet<string> Grants { get; set; } = [.. Permissions.All];
        public bool IsAuthenticated => true;
        public string? Email => null;
        public bool HasPermission(string permission) => Grants.Contains(permission);
    }

    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private static readonly DateOnly Today = new(2026, 11, 26);

    private readonly FixedTenant _tenant = new();
    private readonly Me _me = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _hdfc, _anjali, _rahul;

    private ICalendarService Svc => _scope.ServiceProvider.GetRequiredService<ICalendarService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider, FixedClock>();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_me);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddScoped<INumberSequence, NumberSequenceService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<ICalendarService, CalendarService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var hdfc = new Bank { Name = "HDFC Bank", ShortCode = "HDFC" };
        var anjali = new AppUser { FullName = "Anjali Shah", UserName = "anjali", TenantId = _tenant.TenantId };
        var rahul = new AppUser { FullName = "Rahul Mehta", UserName = "rahul", TenantId = _tenant.TenantId };
        Db.AddRange(hdfc, anjali, rahul, new Tenant { Id = _tenant.TenantId, Name = "Om Engineers", Slug = "om" });
        await Db.SaveChangesAsync();
        (_hdfc, _anjali, _rahul) = (hdfc.Id, anjali.Id, rahul.Id);
        _me.UserId = _anjali;
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Assignment> CaseAsync(string number, AssignmentStatus status, DateOnly due, Guid? valuer = null, DateTimeOffset? visit = null)
    {
        var a = new Assignment
        {
            Number = number, Status = status, BankId = _hdfc, ApplicantName = "Applicant " + number, PropertySummary = "Flat " + number,
            ValuerId = valuer ?? _anjali, DueDate = due, SiteVisitAt = visit, Fee = 5000,
        };
        Db.Add(a);
        await Db.SaveChangesAsync();
        return a;
    }

    private Task<CalendarFeed> MonthAsync(bool mine = false) => Svc.FeedAsync(new CalendarQuery(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), mine));

    [Fact]
    public async Task Feed_puts_visits_due_dates_invoices_and_entries_on_their_days()
    {
        await CaseAsync("V1", AssignmentStatus.SiteVisitScheduled, new DateOnly(2026, 11, 30), visit: new DateTimeOffset(2026, 11, 27, 10, 30, 0, India));
        await CaseAsync("V2", AssignmentStatus.SiteVisitScheduled, new DateOnly(2026, 12, 9), visit: new DateTimeOffset(2026, 11, 25, 15, 0, 0, India));
        await CaseAsync("V3", AssignmentStatus.InspectionDone, new DateOnly(2026, 12, 9), valuer: _rahul, visit: new DateTimeOffset(2026, 11, 20, 11, 0, 0, India));
        await CaseAsync("N1", AssignmentStatus.New, new DateOnly(2026, 11, 24), valuer: _rahul);
        await CaseAsync("S1", AssignmentStatus.Submitted, new DateOnly(2026, 11, 10)); // done: its due date is not shown
        Db.Add(new Invoice { Number = "INV/26-27/0001", Status = InvoiceStatus.Issued, InvoiceDate = new DateOnly(2026, 11, 1), DueDate = new DateOnly(2026, 11, 16),
            BankId = _hdfc, BillToName = "HDFC Bank", Total = 5900, Received = 1000 });
        Db.Add(new CalendarEntry { Title = "Chase HDFC for NOC", Date = new DateOnly(2026, 11, 26), Time = new TimeOnly(16, 0), CreatedById = _anjali, CreatedByName = "Anjali Shah" });
        await Db.SaveChangesAsync();

        var f = await MonthAsync();

        Assert.Equal(Today, f.Today);
        Assert.True(f.ShowsInvoices);
        var v1 = f.Items.Single(i => i.Key.StartsWith("visit") && i.AssignmentNumber == "V1");
        Assert.Equal((new DateOnly(2026, 11, 27), new TimeOnly(10, 30), false, false, true), (v1.Date, v1.Time, v1.Done, v1.Overdue, v1.CanMove));
        var v2 = f.Items.Single(i => i.Key.StartsWith("visit") && i.AssignmentNumber == "V2");
        Assert.True(v2.Overdue); // booked for yesterday and still not inspected
        var v3 = f.Items.Single(i => i.Key.StartsWith("visit") && i.AssignmentNumber == "V3");
        Assert.True(v3.Done);
        Assert.False(v3.CanMove);
        var due = f.Items.Where(i => i.Kind == CalendarKinds.Due).Select(i => (i.AssignmentNumber, i.Overdue)).ToList();
        Assert.Equal([("N1", true), ("V1", false)], due.OrderBy(d => d.AssignmentNumber));
        var inv = f.Items.Single(i => i.Kind == CalendarKinds.Invoice);
        Assert.Equal(("INV/26-27/0001 payment due", "HDFC Bank · ₹4,900", true), (inv.Title, inv.Subtitle, inv.Overdue));
        var entry = f.Items.Single(i => i.Kind == CalendarKinds.Entry);
        Assert.Equal(("Chase HDFC for NOC", "Anjali Shah", true), (entry.Title, entry.PersonName, entry.CanMove));
        Assert.Equal(["N1"], f.ToSchedule.Select(c => c.Number));
        Assert.Equal(2, f.People.Count);

        var mine = await MonthAsync(mine: true);
        Assert.DoesNotContain(mine.Items, i => i.AssignmentNumber is "V3" or "N1");
        Assert.DoesNotContain(mine.Items, i => i.Kind == CalendarKinds.Invoice);
        Assert.False(mine.ShowsInvoices);
        Assert.Contains(mine.Items, i => i.Kind == CalendarKinds.Entry);
    }

    [Fact]
    public async Task Booking_a_visit_moves_a_new_case_and_moving_it_again_is_logged()
    {
        var a = await CaseAsync("N1", AssignmentStatus.New, new DateOnly(2026, 12, 5));

        var booked = await Svc.ScheduleVisitAsync(a.Id, new ScheduleVisitRequest(new DateOnly(2026, 11, 28), new TimeOnly(11, 0)));
        Assert.True(booked.IsSuccess, booked.Error?.Message);
        Assert.Equal((new DateOnly(2026, 11, 28), new TimeOnly(11, 0)), (booked.Value!.Date, booked.Value.Time));
        var saved = await Db.Set<Assignment>().AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal(AssignmentStatus.SiteVisitScheduled, saved.Status);

        var moved = await Svc.ScheduleVisitAsync(a.Id, new ScheduleVisitRequest(new DateOnly(2026, 11, 30), new TimeOnly(9, 30)));
        Assert.True(moved.IsSuccess, moved.Error?.Message);
        var events = await Db.Set<AssignmentEvent>().AsNoTracking().Where(e => e.AssignmentId == a.Id).OrderBy(e => e.Title).Select(e => e.Title).ToListAsync();
        Assert.Contains("Site visit moved to 30 Nov, 9:30 AM", events);
        Assert.Contains(events, t => t.StartsWith("Site visit scheduled for 28 Nov"));

        Assert.Equal("date", (await Svc.ScheduleVisitAsync(a.Id, new ScheduleVisitRequest(new DateOnly(2026, 11, 25), new TimeOnly(9, 0)))).Error!.Field);
    }

    [Fact]
    public async Task Only_the_valuer_reviewer_or_a_manager_can_book_and_a_done_visit_stays_put()
    {
        var others = await CaseAsync("R1", AssignmentStatus.New, new DateOnly(2026, 12, 5), valuer: _rahul);
        var done = await CaseAsync("D1", AssignmentStatus.InspectionDone, new DateOnly(2026, 12, 5));
        _me.Grants.Remove(Permissions.Assignments.Manage);

        var r = await Svc.ScheduleVisitAsync(others.Id, new ScheduleVisitRequest(new DateOnly(2026, 11, 28), new TimeOnly(11, 0)));
        Assert.Equal(Application.Common.ErrorKind.Forbidden, r.Error!.Kind);
        Assert.Equal(Application.Common.ErrorKind.Conflict,
            (await Svc.ScheduleVisitAsync(done.Id, new ScheduleVisitRequest(new DateOnly(2026, 11, 28), new TimeOnly(11, 0)))).Error!.Kind);
        Assert.False((await MonthAsync()).ToSchedule.Single().CanSchedule);
    }

    [Fact]
    public async Task Entries_are_added_ticked_off_and_guarded()
    {
        var a = await CaseAsync("V1", AssignmentStatus.New, new DateOnly(2026, 12, 5));
        var created = await Svc.CreateEntryAsync(new SaveEntryRequest(" Collect cheque ", EntryKind.Reminder, new DateOnly(2026, 11, 29), null, a.Id, _rahul, null));
        Assert.True(created.IsSuccess);
        var e = created.Value!;
        Assert.Equal(("Collect cheque", "V1", "Rahul Mehta", (TimeOnly?)null), (e.Title, e.AssignmentNumber, e.PersonName, e.Time));

        var ticked = await Svc.UpdateEntryAsync(e.EntryId!.Value, new SaveEntryRequest("Collect cheque", EntryKind.Reminder, new DateOnly(2026, 11, 29), null, a.Id, _rahul, "Got it", Done: true));
        Assert.True(ticked.Value!.Done);
        Assert.NotNull((await Db.Set<CalendarEntry>().AsNoTracking().SingleAsync()).DoneAt);

        Assert.Equal("assigneeId", (await Svc.CreateEntryAsync(new SaveEntryRequest("X", EntryKind.Other, Today, null, null, Guid.NewGuid(), null))).Error!.Field);

        // Someone else's entry can't be changed without manage rights.
        Db.Add(new CalendarEntry { Title = "Rahul's own", Date = Today, CreatedById = _rahul, CreatedByName = "Rahul Mehta" });
        await Db.SaveChangesAsync();
        var theirs = await Db.Set<CalendarEntry>().AsNoTracking().SingleAsync(x => x.Title == "Rahul's own");
        _me.Grants.Remove(Permissions.Assignments.Manage);
        Assert.Equal(Application.Common.ErrorKind.Forbidden, (await Svc.DeleteEntryAsync(theirs.Id)).Error!.Kind);
        Assert.True((await Svc.DeleteEntryAsync(e.EntryId.Value)).Value); // the one I added
    }

    [Fact]
    public void Feed_range_is_limited()
    {
        var v = new CalendarQueryValidator();
        Assert.False(v.Validate(new CalendarQuery(new DateOnly(2026, 11, 1), new DateOnly(2027, 2, 1))).IsValid);
        Assert.False(v.Validate(new CalendarQuery(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 1))).IsValid);
        Assert.True(v.Validate(new CalendarQuery(new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 6))).IsValid);
    }
}
