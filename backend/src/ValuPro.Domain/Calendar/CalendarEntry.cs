using ValuPro.Domain.Common;

namespace ValuPro.Domain.Calendar;

public enum EntryKind
{
    FollowUp = 0,
    Reminder = 1,
    Meeting = 2,
    Other = 9,
}

/// <summary>
/// Something put on the calendar by hand: chase a bank for documents, a reminder to collect a cheque, a meeting.
/// Site visits, due dates and invoice due dates are not stored here; the calendar reads them from their cases and invoices.
/// </summary>
public class CalendarEntry : TenantEntity
{
    public required string Title { get; set; }
    public EntryKind Kind { get; set; } = EntryKind.FollowUp;

    /// <summary>The day, in India.</summary>
    public DateOnly Date { get; set; }
    /// <summary>Blank for an all-day entry.</summary>
    public TimeOnly? Time { get; set; }

    public Guid? AssignmentId { get; set; }
    /// <summary>Who it is for. Blank means the person who added it.</summary>
    public Guid? AssigneeId { get; set; }
    public string? Notes { get; set; }

    public bool Done { get; set; }
    public DateTimeOffset? DoneAt { get; set; }

    public Guid? CreatedById { get; set; }
    public required string CreatedByName { get; set; }
}
