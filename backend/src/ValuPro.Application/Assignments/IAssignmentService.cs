using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;

namespace ValuPro.Application.Assignments;

public interface IAssignmentService
{
    Task<PagedResult<AssignmentListItem>> ListAsync(AssignmentQuery query, CancellationToken ct = default);
    Task<AssignmentPipeline> GetPipelineAsync(bool mine, CancellationToken ct = default);
    Task<Result<AssignmentDetail>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<AssignmentDetail>> CreateAsync(SaveAssignmentRequest request, CancellationToken ct = default);
    Task<Result<AssignmentDetail>> UpdateAsync(Guid id, SaveAssignmentRequest request, CancellationToken ct = default);

    /// <summary>Moves along the workflow (forward, back with a reason, cancel, reopen) and records it on the timeline.</summary>
    Task<Result<AssignmentDetail>> MoveAsync(Guid id, MoveRequest request, CancellationToken ct = default);

    /// <summary>Only brand-new assignments can be deleted; others are cancelled instead.</summary>
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<AssignmentLookups> GetLookupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ClientOption>> SearchClientsAsync(string? search, CancellationToken ct = default);
    Task<IReadOnlyList<PropertyOption>> SearchPropertiesAsync(string? search, CancellationToken ct = default);
}

/// <summary>Asked by Clients, Banks and Properties before they are removed.</summary>
public interface IAssignmentUsage
{
    Task<int> OpenForClientAsync(Guid clientId, CancellationToken ct = default);
    Task<int> OpenForBankAsync(Guid bankId, CancellationToken ct = default);
    Task<int> OpenForBranchAsync(Guid branchId, CancellationToken ct = default);
    Task<int> OpenForPropertyAsync(Guid propertyId, CancellationToken ct = default);
}

public sealed class SaveAssignmentRequestValidator : AbstractValidator<SaveAssignmentRequest>
{
    public SaveAssignmentRequestValidator()
    {
        RuleFor(x => x.BankId).NotEmpty().WithMessage("Choose the bank.");
        RuleFor(x => x.BankReference).MaximumLength(60);
        RuleFor(x => x.Purpose).MaximumLength(100);
        RuleFor(x => x.ApplicantName).NotEmpty().WithMessage("Enter or pick the applicant.").MaximumLength(200);
        RuleFor(x => x.ApplicantPhone).MaximumLength(20).Matches(@"^[0-9 +()-]*$").WithMessage("Use digits and spaces only.");
        RuleFor(x => x.PropertySummary).NotEmpty().WithMessage("Describe or pick the property.").MaximumLength(300);
        RuleFor(x => x.ValuerId).NotEmpty().WithMessage("Choose the valuer.");
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.DueDate).NotEmpty().WithMessage("Choose the due date.");
        RuleFor(x => x.Fee).GreaterThanOrEqualTo(0).LessThan(100_000_000).WithMessage("Enter the fee in rupees.");
        RuleFor(x => x.Remarks).MaximumLength(1000);
    }
}

public sealed class MoveRequestValidator : AbstractValidator<MoveRequest>
{
    public MoveRequestValidator()
    {
        RuleFor(x => x.To).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class AssignmentQueryValidator : AbstractValidator<AssignmentQuery>
{
    public AssignmentQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}
