using Microsoft.AspNetCore.Mvc;
using ValuPro.Api.Security;
using ValuPro.Application.Banks;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

[Route("api/banks")]
[HasPermission(Permissions.Banks.View)]
public sealed class BanksController(IBankService banks) : ApiControllerBase
{
    /// <summary>Empanelled banks with branch counts and valuation rules. Search also matches branch name, IFSC and city.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BankSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct) => Ok(await banks.ListAsync(search, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BankDetail>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await banks.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Banks.Manage)]
    [ProducesResponseType<BankSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(SaveBankRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await banks.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Banks.Manage)]
    [ProducesResponseType<BankSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, SaveBankRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await banks.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Banks.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await banks.DeleteAsync(id, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }

    /// <summary>Saves the valuation rules table (several banks at once).</summary>
    [HttpPut("rules")]
    [HasPermission(Permissions.Banks.Manage)]
    [ProducesResponseType<IReadOnlyList<BankSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRules(UpdateRulesRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await banks.UpdateRulesAsync(request, ct));

    /// <summary>Replaces the bank's questionnaire (order is kept as sent).</summary>
    [HttpPut("{id:guid}/questionnaire")]
    [HasPermission(Permissions.Banks.Manage)]
    [ProducesResponseType<IReadOnlyList<QuestionItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateQuestionnaire(Guid id, UpdateQuestionnaireRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await banks.UpdateQuestionnaireAsync(id, request, ct));

    [HttpGet("{id:guid}/branches")]
    [ProducesResponseType<IReadOnlyList<BranchItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Branches(Guid id, [FromQuery] string? search, CancellationToken ct) =>
        From(await banks.ListBranchesAsync(id, search, ct));

    [HttpPost("{id:guid}/branches")]
    [HasPermission(Permissions.Banks.Manage)]
    [ProducesResponseType<BranchItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateBranch(Guid id, SaveBranchRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await banks.CreateBranchAsync(id, request, ct));

    [HttpPut("{id:guid}/branches/{branchId:guid}")]
    [HasPermission(Permissions.Banks.Manage)]
    [ProducesResponseType<BranchItem>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBranch(Guid id, Guid branchId, SaveBranchRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await banks.UpdateBranchAsync(id, branchId, request, ct));

    [HttpDelete("{id:guid}/branches/{branchId:guid}")]
    [HasPermission(Permissions.Banks.Manage)]
    public async Task<IActionResult> DeleteBranch(Guid id, Guid branchId, CancellationToken ct)
    {
        var result = await banks.DeleteBranchAsync(id, branchId, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }
}
