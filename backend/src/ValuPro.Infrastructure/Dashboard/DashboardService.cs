using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Assignments;
using ValuPro.Application.Billing;
using ValuPro.Application.Dashboard;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Dashboard;

/// <summary>
/// The day's picture of the practice: what is late or due, who has visits, how the pipeline and banks look,
/// and (for billing users) what is owed. "Mine" narrows it to cases where the user is the valuer or reviewer.
/// </summary>
public sealed class DashboardService(AppDbContext db, ICurrentUser me, IBillingService billing, TimeProvider clock) : IDashboardService
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private const int SoonDays = 3;
    private const int ListSize = 8;

    private sealed record Open(
        Guid Id, string Number, AssignmentStatus Status, CaseType CaseType, Guid BankId, Guid ValuerId, Guid? ReviewerId,
        DateOnly DueDate, DateTimeOffset? SiteVisitAt, string ApplicantName, string PropertySummary);

    public async Task<DashboardSummary> SummaryAsync(bool mine, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().ToOffset(India);
        var today = DateOnly.FromDateTime(now.DateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var userId = me.UserId;
        mine = mine && userId is not null;

        var cases = db.Set<Assignment>().AsNoTracking();
        if (mine) cases = cases.Where(a => a.ValuerId == userId || a.ReviewerId == userId);

        var counts = await cases.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var open = await cases
            .Where(a => a.Status != AssignmentStatus.Submitted && a.Status != AssignmentStatus.InvoiceGenerated && a.Status != AssignmentStatus.Cancelled)
            .Select(a => new Open(a.Id, a.Number, a.Status, a.CaseType, a.BankId, a.ValuerId, a.ReviewerId, a.DueDate, a.SiteVisitAt,
                a.ApplicantName, a.PropertySummary))
            .ToListAsync(ct);

        var banks = await db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).AsNoTracking()
            .Select(b => new { b.Id, b.Name, b.ShortCode, b.BrandColor }).ToDictionaryAsync(b => b.Id, ct);
        var people = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        string Person(Guid id) => people.GetValueOrDefault(id) ?? "—";
        (string Code, string Color, string Name) BankOf(Guid id) =>
            banks.TryGetValue(id, out var b) ? (b.ShortCode, b.BrandColor, b.Name) : ("—", "#8a94a6", "Unknown bank");

        // Trend and "completed" both come from the timeline: the move to Submitted is when the report went to the bank.
        var trendStart = monthStart.AddMonths(-5);
        var trendFrom = new DateTimeOffset(trendStart.ToDateTime(TimeOnly.MinValue), India);
        var scoped = cases.Select(a => a.Id);
        var submittedEvents = await db.Set<AssignmentEvent>().AsNoTracking()
            .Where(e => e.ToStatus == AssignmentStatus.Submitted && e.At >= trendFrom && scoped.Contains(e.AssignmentId))
            .Select(e => new { e.AssignmentId, e.At }).ToListAsync(ct);
        var received = await cases.Where(a => a.CreatedAt >= trendFrom).Select(a => a.CreatedAt).ToListAsync(ct);
        var completedIds = submittedEvents.Select(e => e.AssignmentId).Distinct().ToList();
        var fees = await db.Set<Assignment>().AsNoTracking().Where(a => completedIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Fee }).ToDictionaryAsync(a => a.Id, a => a.Fee, ct);

        static DateOnly MonthOf(DateTimeOffset at)
        {
            var d = at.ToOffset(India);
            return new DateOnly(d.Year, d.Month, 1);
        }
        var completedByMonth = submittedEvents.GroupBy(e => MonthOf(e.At))
            .ToDictionary(g => g.Key, g => g.Select(e => e.AssignmentId).Distinct().ToList());
        var trend = Enumerable.Range(0, 6).Select(k => trendStart.AddMonths(k)).Select(m =>
        {
            var done = completedByMonth.GetValueOrDefault(m) ?? [];
            return new MonthTrend(m.ToString("MMM", CultureInfo.InvariantCulture), received.Count(r => MonthOf(r) == m), done.Count,
                done.Sum(id => fees.GetValueOrDefault(id)));
        }).ToList();

        // ---- KPIs ----
        var weekEnd = today.AddDays(7);
        var dayStart = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), India);
        var kpis = new DashboardKpis(
            open.Count,
            open.Count(a => a.DueDate < today),
            open.Count(a => a.DueDate >= today && a.DueDate <= weekEnd),
            open.Count(a => a.SiteVisitAt is { } v && v >= dayStart && v < dayStart.AddDays(1)),
            open.Count(a => a.Status == AssignmentStatus.QualityReview),
            trend[^1].Received,
            trend[^1].Completed,
            trend[^2].Completed);

        // ---- needs attention: late first, then due soon, then reports waiting on this user's review ----
        var attention = open
            .Select(a => (Case: a, Reason:
                a.DueDate < today ? "overdue"
                : a.DueDate == today ? "today"
                : a.Status == AssignmentStatus.QualityReview && a.ReviewerId == userId && userId is not null ? "review"
                : a.DueDate <= today.AddDays(SoonDays) ? "soon"
                : null))
            .Where(x => x.Reason is not null)
            .OrderBy(x => x.Reason switch { "overdue" => 0, "today" => 1, "review" => 2, _ => 3 })
            .ThenBy(x => x.Case.DueDate)
            .Take(ListSize)
            .Select(x =>
            {
                var b = BankOf(x.Case.BankId);
                return new AttentionCase(x.Case.Id, x.Case.Number, x.Case.Status, x.Case.CaseType, b.Code, b.Color, x.Case.ApplicantName,
                    x.Case.PropertySummary, Person(x.Case.ValuerId), x.Case.DueDate, x.Reason!, Math.Abs(x.Case.DueDate.DayNumber - today.DayNumber));
            }).ToList();

        var visits = open.Where(a => a.SiteVisitAt is { } v && v >= dayStart && v < dayStart.AddDays(8))
            .OrderBy(a => a.SiteVisitAt).Take(ListSize)
            .Select(a =>
            {
                var b = BankOf(a.BankId);
                return new UpcomingVisit(a.Id, a.Number, a.SiteVisitAt!.Value, b.Code, b.Color, a.ApplicantName, a.PropertySummary, Person(a.ValuerId));
            }).ToList();

        var byBank = open.GroupBy(a => a.BankId).Select(g =>
        {
            var b = BankOf(g.Key);
            return new BankLoad(g.Key, b.Name, b.Code, b.Color, g.Count(), g.Count(a => a.DueDate < today));
        }).OrderByDescending(b => b.Open).ThenBy(b => b.Name).ToList();

        var workload = mine ? [] : open.GroupBy(a => a.ValuerId)
            .Select(g => new ValuerLoad(g.Key, Person(g.Key), g.Count(), g.Count(a => a.DueDate < today),
                g.Count(a => a.DueDate >= today && a.DueDate <= weekEnd)))
            .OrderByDescending(v => v.Open).ThenBy(v => v.Name).ToList();

        var activity = await (from e in db.Set<AssignmentEvent>().AsNoTracking()
                              join a in cases on e.AssignmentId equals a.Id
                              orderby e.At descending
                              select new ActivityItem(a.Id, a.Number, e.At, e.ByName, e.Title, e.ToStatus)).Take(10).ToListAsync(ct);

        int Count(AssignmentStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        var pipeline = AssignmentWorkflow.Stages.Select(s => new PipelineStage(s, Count(s))).ToList();

        DashboardMoney? money = null;
        if (!mine && me.HasPermission(Permissions.Billing.View))
        {
            var s = await billing.SummaryAsync(ct);
            var month = s.Months.Count > 0 ? s.Months[^1] : null;
            money = new DashboardMoney(month?.Invoiced ?? 0, month?.Collected ?? 0, s.Outstanding, s.Overdue, s.OverdueCount, s.UnbilledCount, s.UnbilledFees);
        }

        return new DashboardSummary(mine, today, kpis, pipeline, attention, visits, byBank, workload, trend, activity, money);
    }
}
