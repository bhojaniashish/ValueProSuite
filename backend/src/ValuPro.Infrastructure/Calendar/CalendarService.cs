using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Assignments;
using ValuPro.Application.Calendar;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Billing;
using ValuPro.Domain.Calendar;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Calendar;

/// <summary>
/// The firm's calendar. Site visits and due dates come from cases, invoice due dates from billing, and the rest are
/// entries people add (follow-ups, reminders, meetings). Visits can be booked and moved from here.
/// </summary>
public sealed class CalendarService(AppDbContext db, ICurrentUser me, IAssignmentService assignments, TimeProvider clock) : ICalendarService
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private DateTimeOffset Now => clock.GetUtcNow().ToOffset(India);
    private DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    private bool CanManage => me.HasPermission(Permissions.Assignments.Manage);

    private static DateTimeOffset At(DateOnly d, TimeOnly t) => new(d.ToDateTime(t), India);

    private bool CanWork(Guid valuerId, Guid? reviewerId) =>
        CanManage || (me.UserId is { } u && (u == valuerId || u == reviewerId));

    private bool CanEdit(CalendarEntry e) =>
        CanManage || (me.UserId is { } u && (u == e.CreatedById || u == e.AssigneeId));

    private static bool VisitPending(AssignmentStatus s) => s is AssignmentStatus.New or AssignmentStatus.SiteVisitScheduled;

    // ---------------- feed ----------------

    public async Task<CalendarFeed> FeedAsync(CalendarQuery q, CancellationToken ct = default)
    {
        var today = Today;
        var userId = me.UserId;
        var mine = q.Mine && userId is not null;
        var fromAt = At(q.From, TimeOnly.MinValue);
        var toAt = At(q.To.AddDays(1), TimeOnly.MinValue);

        var cases = db.Set<Assignment>().AsNoTracking().Where(a => a.Status != AssignmentStatus.Cancelled);
        if (mine) cases = cases.Where(a => a.ValuerId == userId || a.ReviewerId == userId);

        var banks = await db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking()
            .Select(b => new { b.Id, b.ShortCode, b.BrandColor }).ToDictionaryAsync(b => b.Id, ct);
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName, u.Status }).ToListAsync(ct);
        var names = users.ToDictionary(u => u.Id, u => u.FullName);
        string? Name(Guid? id) => id is { } x ? names.GetValueOrDefault(x) : null;
        (string? Code, string? Color) Bank(Guid id) => banks.TryGetValue(id, out var b) ? (b.ShortCode, b.BrandColor) : (null, null);

        var items = new List<CalendarItem>();

        var visits = await cases.Where(a => a.SiteVisitAt >= fromAt && a.SiteVisitAt < toAt).ToListAsync(ct);
        items.AddRange(visits.Select(Visit));

        var due = await cases.Where(a => a.DueDate >= q.From && a.DueDate <= q.To &&
                                         a.Status != AssignmentStatus.Submitted && a.Status != AssignmentStatus.InvoiceGenerated).ToListAsync(ct);
        items.AddRange(due.Select(a =>
        {
            var b = Bank(a.BankId);
            return new CalendarItem($"due:{a.Id}", CalendarKinds.Due, a.DueDate, null, a.PropertySummary, a.ApplicantName, a.Id, a.Number, a.Status,
                b.Code, b.Color, Name(a.ValuerId), false, a.DueDate < today, false, null, null, null, null, null);
        }));

        var showsInvoices = !mine && me.HasPermission(Permissions.Billing.View);
        if (showsInvoices)
        {
            var invoices = await db.Set<Invoice>().AsNoTracking()
                .Where(i => i.Status == InvoiceStatus.Issued && i.DueDate >= q.From && i.DueDate <= q.To && i.Received + i.Tds < i.Total)
                .Select(i => new { i.Id, i.Number, i.DueDate, i.BillToName, i.BankId, Owed = i.Total - i.Received - i.Tds }).ToListAsync(ct);
            items.AddRange(invoices.Select(i =>
            {
                var b = i.BankId is { } id ? Bank(id) : (null, null);
                return new CalendarItem($"invoice:{i.Id}", CalendarKinds.Invoice, i.DueDate, null, $"{i.Number} payment due",
                    $"{i.BillToName} · ₹{i.Owed.ToString("N0", new System.Globalization.CultureInfo("en-IN"))}", null, null, null, b.Code, b.Color, null,
                    false, i.DueDate < today, false, null, null, null, null, i.Id);
            }));
        }

        var entries = db.Set<CalendarEntry>().AsNoTracking().Where(e => e.Date >= q.From && e.Date <= q.To);
        if (mine) entries = entries.Where(e => e.AssigneeId == userId || (e.AssigneeId == null && e.CreatedById == userId));
        var entryList = await entries.ToListAsync(ct);
        var linked = await CaseNumbersAsync(entryList.Select(e => e.AssignmentId), ct);
        items.AddRange(entryList.Select(e => Entry(e, linked, Name(e.AssigneeId ?? e.CreatedById) ?? e.CreatedByName)));

        // Cases waiting for a visit to be booked, soonest due first.
        var waiting = await cases.Where(a => a.Status == AssignmentStatus.New && a.SiteVisitAt == null)
            .OrderBy(a => a.DueDate).Take(25).ToListAsync(ct);
        var toSchedule = waiting.Select(a =>
        {
            var b = Bank(a.BankId);
            return new CaseToSchedule(a.Id, a.Number, b.Code ?? "—", b.Color ?? "#8a94a6", a.ApplicantName, a.PropertySummary,
                Name(a.ValuerId) ?? "—", a.DueDate, CanWork(a.ValuerId, a.ReviewerId));
        }).ToList();

        var people = users.Where(u => u.Status != UserStatus.Inactive).OrderBy(u => u.FullName).Select(u => new CalendarPerson(u.Id, u.FullName)).ToList();

        return new CalendarFeed(q.From, q.To, today, mine,
            [.. items.OrderBy(i => i.Date).ThenBy(i => i.Time is null ? 0 : 1).ThenBy(i => i.Time).ThenBy(i => i.Kind).ThenBy(i => i.Title)],
            toSchedule, people, showsInvoices);

        CalendarItem Visit(Assignment a)
        {
            var at = a.SiteVisitAt!.Value.ToOffset(India);
            var b = Bank(a.BankId);
            var pending = VisitPending(a.Status);
            return new CalendarItem($"visit:{a.Id}", CalendarKinds.Visit, DateOnly.FromDateTime(at.DateTime), TimeOnly.FromDateTime(at.DateTime),
                a.PropertySummary, a.ApplicantName, a.Id, a.Number, a.Status, b.Code, b.Color, Name(a.ValuerId), !pending,
                pending && at < Now, pending && CanWork(a.ValuerId, a.ReviewerId), null, null, null, null, null);
        }
    }

    private CalendarItem Entry(CalendarEntry e, IReadOnlyDictionary<Guid, string> numbers, string? person) =>
        new($"entry:{e.Id}", CalendarKinds.Entry, e.Date, e.Time, e.Title, null, e.AssignmentId,
            e.AssignmentId is { } a ? numbers.GetValueOrDefault(a) : null, null, null, null, person, e.Done, !e.Done && e.Date < Today, CanEdit(e),
            e.Id, e.Kind, e.Notes, e.AssigneeId, null);

    private async Task<Dictionary<Guid, string>> CaseNumbersAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Set<Assignment>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(a => list.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Number, ct);
    }

    // ---------------- visits ----------------

    public async Task<Result<CalendarItem>> ScheduleVisitAsync(Guid assignmentId, ScheduleVisitRequest r, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (!VisitPending(a.Status))
            return Error.Conflict(a.Status == AssignmentStatus.Cancelled
                ? "This case is cancelled."
                : "The site visit for this case is already done.");
        if (!CanWork(a.ValuerId, a.ReviewerId))
            return Error.Forbidden("Only the assigned valuer, the reviewer or a manager can book this visit.");
        var at = At(r.Date, r.Time);
        if (r.Date < Today) return Error.Validation("Choose today or a later day.", "date");

        if (a.Status == AssignmentStatus.New)
        {
            var moved = await assignments.MoveAsync(a.Id, new MoveRequest(AssignmentStatus.SiteVisitScheduled, null, at), ct);
            if (moved.Error is { } err) return err;
        }
        else
        {
            var before = a.SiteVisitAt;
            a.SiteVisitAt = at;
            db.Add(new AssignmentEvent
            {

                AssignmentId = a.Id,
                At = clock.GetUtcNow(),
                ByUserId = me.UserId,
                ByName = me.Name ?? me.Email ?? "Someone",
                FromStatus = a.Status,
                ToStatus = a.Status,
                Title = $"Site visit moved to {at:dd MMM, h:mm tt}",
                Note = before is { } b ? $"Was {b.ToOffset(India):dd MMM, h:mm tt}" : null,
            });
            await db.SaveChangesAsync(ct);
        }

        var feed = await FeedAsync(new CalendarQuery(r.Date, r.Date), ct);
        return feed.Items.FirstOrDefault(i => i.Key == $"visit:{a.Id}") is { } item ? item : Error.NotFound("Assignment not found.");
    }

    // ---------------- entries ----------------

    private async Task<Error?> CheckEntryLinksAsync(SaveEntryRequest r, CancellationToken ct)
    {
        if (r.AssignmentId is { } a && !await db.Set<Assignment>().AnyAsync(x => x.Id == a, ct))
            return Error.Validation("That case was not found.", "assignmentId");
        if (r.AssigneeId is { } u && !await db.Users.AnyAsync(x => x.Id == u && x.Status != UserStatus.Inactive, ct))
            return Error.Validation("Choose someone from the list.", "assigneeId");
        return null;
    }

    private void Apply(CalendarEntry e, SaveEntryRequest r)
    {
        e.Title = r.Title.Trim();
        e.Kind = r.Kind;
        e.Date = r.Date;
        e.Time = r.Time;
        e.AssignmentId = r.AssignmentId;
        e.AssigneeId = r.AssigneeId == me.UserId ? null : r.AssigneeId;
        e.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        if (r.Done != e.Done) e.DoneAt = r.Done ? clock.GetUtcNow() : null;
        e.Done = r.Done;
    }

    private async Task<CalendarItem> ItemAsync(CalendarEntry e, CancellationToken ct)
    {
        var numbers = await CaseNumbersAsync([e.AssignmentId], ct);
        var personId = e.AssigneeId ?? e.CreatedById;
        var person = personId is { } p ? await db.Users.Where(u => u.Id == p).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
        return Entry(e, numbers, person ?? e.CreatedByName);
    }

    public async Task<Result<CalendarItem>> CreateEntryAsync(SaveEntryRequest r, CancellationToken ct = default)
    {
        if (await CheckEntryLinksAsync(r, ct) is { } err) return err;
        var e = new CalendarEntry { Title = r.Title.Trim(), CreatedById = me.UserId, CreatedByName = me.Name ?? me.Email ?? "Someone" };
        Apply(e, r);
        db.Add(e);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(e, ct);
    }

    public async Task<Result<CalendarItem>> UpdateEntryAsync(Guid id, SaveEntryRequest r, CancellationToken ct = default)
    {
        var e = await db.Set<CalendarEntry>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Error.NotFound("That entry was not found.");
        if (!CanEdit(e)) return Error.Forbidden("Only the person who added it, the person it is for, or a manager can change it.");
        if (await CheckEntryLinksAsync(r, ct) is { } err) return err;
        Apply(e, r);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(e, ct);
    }

    public async Task<Result<bool>> DeleteEntryAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.Set<CalendarEntry>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Error.NotFound("That entry was not found.");
        if (!CanEdit(e)) return Error.Forbidden("Only the person who added it, the person it is for, or a manager can delete it.");
        db.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
