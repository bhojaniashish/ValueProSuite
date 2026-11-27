using ValuPro.Application.Assignments;
using ValuPro.Domain.Assignments;

namespace ValuPro.Application.Dashboard;

/// <summary>Case counts that need a decision today.</summary>
public sealed record DashboardKpis(
    int Open, int Overdue, int DueThisWeek, int VisitsToday, int InReview,
    int ReceivedThisMonth, int CompletedThisMonth, int CompletedLastMonth);

/// <summary>Money at a glance, only for users who can see billing (and only for the whole firm).</summary>
public sealed record DashboardMoney(
    decimal InvoicedThisMonth, decimal CollectedThisMonth, decimal Outstanding, decimal Overdue, int OverdueInvoices,
    int ToBillCount, decimal ToBillFees);

/// <summary>A case worth looking at, with why.</summary>
public sealed record AttentionCase(
    Guid Id, string Number, AssignmentStatus Status, CaseType CaseType, string BankShortCode, string BankColor,
    string ApplicantName, string PropertySummary, string ValuerName, DateOnly DueDate,
    // overdue | today | soon | review
    string Reason, int Days);

public sealed record UpcomingVisit(
    Guid Id, string Number, DateTimeOffset At, string BankShortCode, string BankColor, string ApplicantName,
    string PropertySummary, string ValuerName);

public sealed record BankLoad(Guid BankId, string Name, string ShortCode, string Color, int Open, int Overdue);

public sealed record ValuerLoad(Guid UserId, string Name, int Open, int Overdue, int DueThisWeek);

/// <summary>One month: cases received, cases submitted to the bank, and their fees.</summary>
public sealed record MonthTrend(string Month, int Received, int Completed, decimal Fees);

public sealed record ActivityItem(Guid AssignmentId, string Number, DateTimeOffset At, string ByName, string Title, AssignmentStatus ToStatus);

public sealed record DashboardSummary(
    bool Mine,
    DateOnly Today,
    DashboardKpis Kpis,
    IReadOnlyList<PipelineStage> Pipeline,
    IReadOnlyList<AttentionCase> Attention,
    IReadOnlyList<UpcomingVisit> Visits,
    IReadOnlyList<BankLoad> ByBank,
    IReadOnlyList<ValuerLoad> Workload,
    IReadOnlyList<MonthTrend> Trend,
    IReadOnlyList<ActivityItem> Activity,
    DashboardMoney? Money);

public interface IDashboardService
{
    /// <param name="mine">Only cases where the signed-in user is the valuer or reviewer.</param>
    Task<DashboardSummary> SummaryAsync(bool mine, CancellationToken ct = default);
}
