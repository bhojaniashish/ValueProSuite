using ValuPro.Application.Common;

namespace ValuPro.Application.Banks;

public interface IBankService
{
    Task<IReadOnlyList<BankSummary>> ListAsync(string? search, CancellationToken ct = default);
    Task<Result<BankDetail>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<BankSummary>> CreateAsync(SaveBankRequest request, CancellationToken ct = default);
    Task<Result<BankSummary>> UpdateAsync(Guid id, SaveBankRequest request, CancellationToken ct = default);

    /// <summary>Hides the bank and its branches; history is kept.</summary>
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Updates the valuation rules of several banks at once (the rules table).</summary>
    Task<Result<IReadOnlyList<BankSummary>>> UpdateRulesAsync(UpdateRulesRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<QuestionItem>>> UpdateQuestionnaireAsync(Guid bankId, UpdateQuestionnaireRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<BranchItem>>> ListBranchesAsync(Guid bankId, string? search, CancellationToken ct = default);
    Task<Result<BranchItem>> CreateBranchAsync(Guid bankId, SaveBranchRequest request, CancellationToken ct = default);
    Task<Result<BranchItem>> UpdateBranchAsync(Guid bankId, Guid branchId, SaveBranchRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteBranchAsync(Guid bankId, Guid branchId, CancellationToken ct = default);
}
