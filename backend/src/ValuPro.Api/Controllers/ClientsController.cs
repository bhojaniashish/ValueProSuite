using System.Text;
using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Clients;

namespace ValuPro.Api.Controllers;

[Route("api/clients")]
[HasPermission(Permissions.Clients.View)]
public sealed class ClientsController(IClientService clients) : ApiControllerBase
{
    /// <summary>Clients with search (name, code, mobile, email, city, PAN, GSTIN), type/state filters, sorting and paging.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ClientItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ClientQuery query, CancellationToken ct) =>
        await ValidateAsync(query, ct) ?? Ok(await clients.ListAsync(query, ct));

    /// <summary>Counts per client type and the states in use (for the filter).</summary>
    [HttpGet("summary")]
    [ProducesResponseType<ClientSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await clients.GetSummaryAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClientItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await clients.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Clients.Manage)]
    [ProducesResponseType<ClientItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SaveClientRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await clients.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Clients.Manage)]
    [ProducesResponseType<ClientItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SaveClientRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await clients.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Clients.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await clients.DeleteAsync(id, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }

    /// <summary>A CSV with the expected columns and two example rows.</summary>
    [HttpGet("import/template")]
    [HasPermission(Permissions.Clients.Manage)]
    public IActionResult Template() =>
        File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ClientFileReader.Template())).ToArray(),
            "text/csv", "valupro-clients-template.csv");

    /// <summary>Reads a .csv or .xlsx file (up to 1,000 clients) and checks every row. Nothing is saved.</summary>
    [HttpPost("import/preview")]
    [HasPermission(Permissions.Clients.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<ImportPreview>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewImport(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Problem(Error.Validation("Choose a .csv or .xlsx file.", "file"));
        await using var stream = file.OpenReadStream();
        return From(await clients.PreviewImportAsync(stream, file.FileName, ct));
    }

    /// <summary>Saves the checked rows from a preview. Rows that no longer pass are returned, not saved.</summary>
    [HttpPost("import")]
    [HasPermission(Permissions.Clients.Manage)]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(ImportRequest request, CancellationToken ct) =>
        From(await clients.ImportAsync(request, ct));
}
