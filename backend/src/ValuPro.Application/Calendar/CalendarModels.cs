using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Calendar;

namespace ValuPro.Application.Calendar;

/// <summary>visit | due | invoice | entry</summary>
public static class CalendarKinds
{
    public const string Visit = "visit";
    public const string Due = "due";
    public const string Invoice = "invoice";
    public const string Entry = "entry";
}

/// <summary>
/// One thing on the calendar. Visits and timed entries have a time; due dates, invoices and all-day entries don't.
/// </summary>
public sealed record CalendarItem(
    string Key,
    string Kind,
    DateOnly Date,
    TimeOnly? Time,
    string Title,
    string? Subtitle,
    Guid? AssignmentId,
    string? AssignmentNumber,
    AssignmentStatus? Status,
    string? BankShortCode,
    string? BankColor,
    string? PersonName,
    // a visit that has happened, or an entry ticked off
    bool Done,
    bool Overdue,
    // can be moved to another day or time from the calendar
    bool CanMove,
    Guid? EntryId,
    EntryKind? EntryKind,
    string? Notes,
    Guid? AssigneeId,
    Guid? InvoiceId);

/// <summary>A new case with no visit booked yet.</summary>
public sealed record CaseToSchedule(Guid Id, string Number, string BankShortCode, string BankColor, string ApplicantName, string PropertySummary,
    string ValuerName, DateOnly DueDate, bool CanSchedule);

public sealed record CalendarPerson(Guid Id, string Name);

public sealed record CalendarFeed(
    DateOnly From, DateOnly To, DateOnly Today, bool Mine, IReadOnlyList<CalendarItem> Items, IReadOnlyList<CaseToSchedule> ToSchedule,
    IReadOnlyList<CalendarPerson> People, bool ShowsInvoices);

public sealed record CalendarQuery(DateOnly From, DateOnly To, bool Mine = false);

/// <summary>Book or move a case's site visit (India time).</summary>
public sealed record ScheduleVisitRequest(DateOnly Date, TimeOnly Time);

public sealed record SaveEntryRequest(
    string Title, EntryKind Kind, DateOnly Date, TimeOnly? Time, Guid? AssignmentId, Guid? AssigneeId, string? Notes, bool Done = false);

public sealed class CalendarQueryValidator : AbstractValidator<CalendarQuery>
{
    public CalendarQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date is before the start date.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber <= 62).WithMessage("Ask for at most two months at a time.").OverridePropertyName("to");
    }
}

public sealed class SaveEntryValidator : AbstractValidator<SaveEntryRequest>
{
    public SaveEntryValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Say what it is.").MaximumLength(160);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public interface ICalendarService
{
    Task<CalendarFeed> FeedAsync(CalendarQuery query, CancellationToken ct = default);
    /// <summary>Books the visit for a new case (moving it to Site Visit Scheduled) or moves a booked one.</summary>
    Task<Result<CalendarItem>> ScheduleVisitAsync(Guid assignmentId, ScheduleVisitRequest request, CancellationToken ct = default);
    Task<Result<CalendarItem>> CreateEntryAsync(SaveEntryRequest request, CancellationToken ct = default);
    Task<Result<CalendarItem>> UpdateEntryAsync(Guid id, SaveEntryRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteEntryAsync(Guid id, CancellationToken ct = default);
}
