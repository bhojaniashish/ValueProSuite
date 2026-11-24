using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Assignments;
using ValuPro.Domain.Assignments;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Assignments;

/// <summary>Counts open assignments that use a record, so masters are not removed from under live cases.</summary>
public sealed class AssignmentUsage(AppDbContext db) : IAssignmentUsage
{
    private IQueryable<Assignment> Open => db.Set<Assignment>().Where(a =>
        a.Status != AssignmentStatus.Submitted && a.Status != AssignmentStatus.InvoiceGenerated && a.Status != AssignmentStatus.Cancelled);

    public Task<int> OpenForClientAsync(Guid clientId, CancellationToken ct = default) => Open.CountAsync(a => a.ClientId == clientId, ct);
    public Task<int> OpenForBankAsync(Guid bankId, CancellationToken ct = default) => Open.CountAsync(a => a.BankId == bankId, ct);
    public Task<int> OpenForBranchAsync(Guid branchId, CancellationToken ct = default) => Open.CountAsync(a => a.BranchId == branchId, ct);
    public Task<int> OpenForPropertyAsync(Guid propertyId, CancellationToken ct = default) => Open.CountAsync(a => a.PropertyId == propertyId, ct);

    public static string Message(string what, int count) =>
        $"{what} has {count} open assignment{(count == 1 ? "" : "s")}. Finish or cancel {(count == 1 ? "it" : "them")} first.";
}
