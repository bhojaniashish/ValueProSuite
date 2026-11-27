using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Reports;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

/// <summary>
/// Report formats (bank templates): sections and their order, headings, values shown, declaration and limiting
/// conditions. Anyone signed in can list them (the bank form picks one); changing them needs Settings: Manage.
/// </summary>
[Route("api/report-formats")]
[Authorize]
public sealed class ReportFormatsController(IReportFormatService formats) : ApiControllerBase
{
    /// <summary>Sections, placeholders and the standard wording, for the editor.</summary>
    [HttpGet("catalog")]
    [ProducesResponseType<ReportFormatCatalog>(StatusCodes.Status200OK)]
    public IActionResult Catalog() => Ok(formats.Catalog());

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReportFormatItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await formats.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReportFormatDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await formats.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<ReportFormatDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SaveReportFormatRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await formats.CreateAsync(request, ct));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<ReportFormatDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SaveReportFormatRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return From(await formats.UpdateAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/duplicate")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<ReportFormatDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct) => From(await formats.DuplicateAsync(id, ct));

    [HttpPost("{id:guid}/default")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<ReportFormatDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MakeDefault(Guid id, CancellationToken ct) => From(await formats.MakeDefaultAsync(id, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await formats.DeleteAsync(id, ct);
        return r.IsSuccess ? NoContent() : Problem(r.Error!);
    }

    /// <summary>A sample draft PDF in the (unsaved) format; ?stage=true shows a stage inspection.</summary>
    [HttpPost("preview")]
    [HasPermission(Permissions.Settings.View)]
    public async Task<IActionResult> Preview(SaveReportFormatRequest request, [FromQuery] bool stage, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        return File(await formats.PreviewAsync(request, stage, ct), "application/pdf");
    }
}
