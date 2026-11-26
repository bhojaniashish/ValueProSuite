using ValuPro.Domain.Common;

namespace ValuPro.Domain.Assignments;

/// <summary>The prototype's 7-stage pipeline, plus Cancelled.</summary>
public enum AssignmentStatus
{
    New = 0,
    SiteVisitScheduled = 1,
    InspectionDone = 2,
    DraftPrepared = 3,
    QualityReview = 4,
    Submitted = 5,
    InvoiceGenerated = 6,
    Cancelled = 9,
}

/// <summary>
/// What the bank asked for. Stage inspections and re-inspections are visits during construction for construction-linked loans:
/// how far the work has progressed and how much can be released.
/// </summary>
public enum CaseType
{
    Valuation = 0,
    StageInspection = 1,
    Reinspection = 2,
}

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>A valuation case from intake to invoice.</summary>
public class Assignment : TenantEntity
{
    /// <summary>E.g. VAL/2026/0042: prefix from Settings, year, per-year counter. Never reused.</summary>
    public required string Number { get; set; }

    public CaseType CaseType { get; set; } = CaseType.Valuation;
    /// <summary>The earlier case this one follows (the original valuation, or the previous stage visit).</summary>
    public Guid? PreviousAssignmentId { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.New;
    public Priority Priority { get; set; } = Priority.Medium;

    public Guid BankId { get; set; }
    public Guid? BranchId { get; set; }
    /// <summary>The bank's own case / loan reference.</summary>
    public string? BankReference { get; set; }
    /// <summary>Home loan, loan against property, project finance…</summary>
    public string? Purpose { get; set; }

    public required string ApplicantName { get; set; }
    public Guid? ClientId { get; set; }
    public string? ApplicantPhone { get; set; }

    public required string PropertySummary { get; set; }
    public Guid? PropertyId { get; set; }

    public Guid ValuerId { get; set; }
    public Guid? ReviewerId { get; set; }

    public DateOnly DueDate { get; set; }
    public DateTimeOffset? SiteVisitAt { get; set; }
    public decimal Fee { get; set; }
    public string? Remarks { get; set; }

    public List<AssignmentEvent> Events { get; set; } = [];

    public bool IsOpen => AssignmentWorkflow.IsOpen(Status);
}

/// <summary>One line of an assignment's timeline: created, moved, returned, cancelled, edited.</summary>
public class AssignmentEvent : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid AssignmentId { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? ByUserId { get; set; }
    public required string ByName { get; set; }
    public AssignmentStatus? FromStatus { get; set; }
    public AssignmentStatus ToStatus { get; set; }
    public required string Title { get; set; }
    public string? Note { get; set; }
}

public enum MoveKind
{
    Invalid,
    Forward,
    Back,
    Cancel,
    Reopen,
}

/// <summary>Which status changes are allowed: one step forward, one step back (with a reason), cancel and reopen.</summary>
public static class AssignmentWorkflow
{
    public static readonly IReadOnlyList<AssignmentStatus> Stages =
    [
        AssignmentStatus.New,
        AssignmentStatus.SiteVisitScheduled,
        AssignmentStatus.InspectionDone,
        AssignmentStatus.DraftPrepared,
        AssignmentStatus.QualityReview,
        AssignmentStatus.Submitted,
        AssignmentStatus.InvoiceGenerated,
    ];

    /// <summary>Work is still in progress (Submitted and later are done from the firm's side).</summary>
    public static bool IsOpen(AssignmentStatus s) => s is not (AssignmentStatus.Submitted or AssignmentStatus.InvoiceGenerated or AssignmentStatus.Cancelled);

    public static MoveKind Classify(AssignmentStatus from, AssignmentStatus to)
    {
        if (from == to) return MoveKind.Invalid;
        if (to == AssignmentStatus.Cancelled) return from == AssignmentStatus.InvoiceGenerated ? MoveKind.Invalid : MoveKind.Cancel;
        if (from == AssignmentStatus.Cancelled) return to == AssignmentStatus.New ? MoveKind.Reopen : MoveKind.Invalid;

        var a = Index(from);
        var b = Index(to);
        return b == a + 1 ? MoveKind.Forward : b == a - 1 ? MoveKind.Back : MoveKind.Invalid;
    }

    public static int Index(AssignmentStatus s) => Stages.ToList().IndexOf(s);

    public static AssignmentStatus? Next(AssignmentStatus s) =>
        Index(s) is var i && i >= 0 && i < Stages.Count - 1 ? Stages[i + 1] : null;

    public static AssignmentStatus? Previous(AssignmentStatus s) =>
        Index(s) is var i && i > 0 ? Stages[i - 1] : null;

    public static string Label(AssignmentStatus s) => s switch
    {
        AssignmentStatus.New => "New Assignment",
        AssignmentStatus.SiteVisitScheduled => "Site Visit Scheduled",
        AssignmentStatus.InspectionDone => "Inspection Done",
        AssignmentStatus.DraftPrepared => "Draft Prepared",
        AssignmentStatus.QualityReview => "Quality Review",
        AssignmentStatus.Submitted => "Submitted",
        AssignmentStatus.InvoiceGenerated => "Invoice Generated",
        AssignmentStatus.Cancelled => "Cancelled",
        _ => s.ToString(),
    };
}
