using ValuPro.Domain.Banks;

namespace ValuPro.Application.Banks;

public sealed record BankRules(RateBasis RateBasis, decimal DistressPercent, decimal RealizablePercent, DepreciationMethod Depreciation);

public sealed record BankSummary(
    Guid Id,
    string Name,
    string ShortCode,
    string BrandColor,
    string? IfscPrefix,
    Guid? ReportFormatId,
    string? ReportFormatName,
    string? PanelCode,
    bool IsActive,
    BankRules Rules,
    int Branches,
    int ActiveBranches,
    int Questions);

public sealed record QuestionItem(string Text, AnswerType AnswerType, IReadOnlyList<string> Options, bool Required);

public sealed record BankDetail(BankSummary Bank, IReadOnlyList<QuestionItem> Questions);

public sealed record SaveBankRequest(
    string Name,
    string ShortCode,
    string BrandColor,
    string? IfscPrefix,
    Guid? ReportFormatId,
    string? PanelCode,
    bool IsActive,
    BankRules Rules);

public sealed record BranchItem(
    Guid Id,
    Guid BankId,
    string Name,
    string? Ifsc,
    string? City,
    string? State,
    string? Address,
    string? ContactPerson,
    string? Phone,
    string? Email,
    bool IsActive);

public sealed record SaveBranchRequest(
    string Name,
    string? Ifsc,
    string? City,
    string? State,
    string? Address,
    string? ContactPerson,
    string? Phone,
    string? Email,
    bool IsActive);

public sealed record UpdateQuestionnaireRequest(IReadOnlyList<QuestionItem> Questions);

public sealed record RulesRow(Guid BankId, BankRules Rules);

public sealed record UpdateRulesRequest(IReadOnlyList<RulesRow> Banks);
