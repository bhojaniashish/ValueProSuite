using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Calendar;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>Site visits, due dates, invoice due dates and hand-made entries (follow-ups, reminders, meetings).</summary>
[Route("api/calendar")]
[HasPermission(Permissions.Assignments.View)]
public sealed class CalendarController(ICalendarService calendar) : ApiControllerBase
{
    /// <summary>Everything between two days (at most about two months), plus new cases still waiting for a visit.</summary>
    [HttpGet]
    [ProducesResponseType<CalendarFeed>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Feed([FromQuery] CalendarQuery query, CancellationToken ct)
    {
        if (await ValidateAsync(query, ct) is { } invalid) return invalid;
        return Ok(await calendar.FeedAsync(query, ct));
    }

    /// <summary>Book a new case's site visit, or move a booked one.</summary>
    [HttpPut("visits/{assignmentId:guid}")]
    [ProducesResponseType<CalendarItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ScheduleVisit(Guid assignmentId, ScheduleVisitRequest request, CancellationToken ct) =>
        From(await calendar.ScheduleVisitAsync(assignmentId, request, ct));

    [HttpPost("entries")]
    [ProducesResponseType<CalendarItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SaveEntryRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await calendar.CreateEntryAsync(request, ct));
    }

    [HttpPut("entries/{id:guid}")]
    [ProducesResponseType<CalendarItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SaveEntryRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await calendar.UpdateEntryAsync(id, request, ct));
    }

    [HttpDelete("entries/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await calendar.DeleteEntryAsync(id, ct);
        return r.Error is { } e ? Problem(e) : NoContent();
    }
}
