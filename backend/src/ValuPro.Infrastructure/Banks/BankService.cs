using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Banks;
using ValuPro.Application.Common;
using ValuPro.Domain.Banks;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Banks;

public sealed class BankService(AppDbContext db) : IBankService
{
    // ---------------- banks ----------------

    public async Task<IReadOnlyList<BankSummary>> ListAsync(string? search, CancellationToken ct = default)
    {
        var q = db.Set<Bank>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(b => b.Name.Contains(s) || b.ShortCode.Contains(s) ||
                             b.Branches.Any(br => !br.IsDeleted && (br.Name.Contains(s) || br.Ifsc!.Contains(s) || br.City!.Contains(s))));
        }
        return await Project(q.OrderByDescending(b => b.IsActive).ThenBy(b => b.Name), ct);
    }

    public async Task<Result<BankDetail>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var summary = (await Project(db.Set<Bank>().Where(b => b.Id == id), ct)).FirstOrDefault();
        if (summary is null) return Error.NotFound("Bank not found.");
        var bank = await db.Set<Bank>().AsNoTracking().FirstAsync(b => b.Id == id, ct);
        return new BankDetail(summary, Questions(bank));
    }

    public async Task<Result<BankSummary>> CreateAsync(SaveBankRequest r, CancellationToken ct = default)
    {
        var code = r.ShortCode.Trim().ToUpperInvariant();
        if (await db.Set<Bank>().AnyAsync(b => b.ShortCode == code, ct))
            return Error.Conflict($"Another bank already uses the code {code}.", "shortCode");

        var bank = new Bank { Name = r.Name.Trim(), ShortCode = code };
        Apply(bank, r);
        db.Add(bank);
        await db.SaveChangesAsync(ct);
        return await SummaryAsync(bank.Id, ct);
    }

    public async Task<Result<BankSummary>> UpdateAsync(Guid id, SaveBankRequest r, CancellationToken ct = default)
    {
        var bank = await db.Set<Bank>().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null) return Error.NotFound("Bank not found.");

        var code = r.ShortCode.Trim().ToUpperInvariant();
        if (await db.Set<Bank>().AnyAsync(b => b.ShortCode == code && b.Id != id, ct))
            return Error.Conflict($"Another bank already uses the code {code}.", "shortCode");

        Apply(bank, r);
        await db.SaveChangesAsync(ct);
        return await SummaryAsync(id, ct);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var bank = await db.Set<Bank>().Include(b => b.Branches).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bank is null) return Error.NotFound("Bank not found.");
        // Assignments (next module) will refuse this while the bank has open cases.
        db.RemoveRange(bank.Branches); // soft delete via AuditingInterceptor
        db.Remove(bank);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Result<IReadOnlyList<BankSummary>>> UpdateRulesAsync(UpdateRulesRequest r, CancellationToken ct = default)
    {
        var ids = r.Banks.Select(x => x.BankId).ToList();
        var banks = await db.Set<Bank>().Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        if (banks.Count != ids.Distinct().Count()) return Error.NotFound("One or more banks were not found.");

        foreach (var row in r.Banks) SetRules(banks.First(b => b.Id == row.BankId), row.Rules);
        await db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<BankSummary>>.Ok(await ListAsync(null, ct));
    }

    public async Task<Result<IReadOnlyList<QuestionItem>>> UpdateQuestionnaireAsync(Guid bankId, UpdateQuestionnaireRequest r,
        CancellationToken ct = default)
    {
        var bank = await db.Set<Bank>().FirstOrDefaultAsync(b => b.Id == bankId, ct);
        if (bank is null) return Error.NotFound("Bank not found.");

        bank.Questions =
        [
            .. r.Questions.Select(q => new BankQuestion
            {
                Text = q.Text.Trim(),
                AnswerType = q.AnswerType,
                Required = q.Required,
                Options = q.AnswerType == AnswerType.Choice
                    ? [.. q.Options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)]
                    : [],
            }),
        ];
        await db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<QuestionItem>>.Ok(Questions(bank));
    }

    // ---------------- branches ----------------

    public async Task<Result<IReadOnlyList<BranchItem>>> ListBranchesAsync(Guid bankId, string? search, CancellationToken ct = default)
    {
        if (!await db.Set<Bank>().AnyAsync(b => b.Id == bankId, ct)) return Error.NotFound("Bank not found.");
        var q = db.Set<BankBranch>().Where(b => b.BankId == bankId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(b => b.Name.Contains(s) || b.Ifsc!.Contains(s) || b.City!.Contains(s) || b.ContactPerson!.Contains(s));
        }
        var list = await q.OrderByDescending(b => b.IsActive).ThenBy(b => b.City).ThenBy(b => b.Name).AsNoTracking().ToListAsync(ct);
        return Result<IReadOnlyList<BranchItem>>.Ok([.. list.Select(ToItem)]);
    }

    public async Task<Result<BranchItem>> CreateBranchAsync(Guid bankId, SaveBranchRequest r, CancellationToken ct = default)
    {
        var bank = await db.Set<Bank>().FirstOrDefaultAsync(b => b.Id == bankId, ct);
        if (bank is null) return Error.NotFound("Bank not found.");
        if (await CheckIfscAsync(bank, r.Ifsc, null, ct) is { } error) return error;

        var branch = new BankBranch { BankId = bankId, Name = r.Name.Trim() };
        Apply(branch, r);
        db.Add(branch);
        await db.SaveChangesAsync(ct);
        return ToItem(branch);
    }

    public async Task<Result<BranchItem>> UpdateBranchAsync(Guid bankId, Guid branchId, SaveBranchRequest r, CancellationToken ct = default)
    {
        var branch = await db.Set<BankBranch>().Include(b => b.Bank).FirstOrDefaultAsync(b => b.Id == branchId && b.BankId == bankId, ct);
        if (branch is null) return Error.NotFound("Branch not found.");
        if (await CheckIfscAsync(branch.Bank!, r.Ifsc, branchId, ct) is { } error) return error;

        Apply(branch, r);
        await db.SaveChangesAsync(ct);
        return ToItem(branch);
    }

    public async Task<Result<bool>> DeleteBranchAsync(Guid bankId, Guid branchId, CancellationToken ct = default)
    {
        var branch = await db.Set<BankBranch>().FirstOrDefaultAsync(b => b.Id == branchId && b.BankId == bankId, ct);
        if (branch is null) return Error.NotFound("Branch not found.");
        db.Remove(branch);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------- helpers ----------------

    private async Task<Error?> CheckIfscAsync(Bank bank, string? ifsc, Guid? exceptBranch, CancellationToken ct)
    {
        var code = Normalize(ifsc);
        if (code is null) return null;
        if (bank.IfscPrefix is { } prefix && !code.StartsWith(prefix, StringComparison.Ordinal))
            return Error.Validation($"{bank.ShortCode} IFSC codes start with {prefix}.", "ifsc");
        var other = await db.Set<BankBranch>().Include(b => b.Bank)
            .FirstOrDefaultAsync(b => b.Ifsc == code && b.Id != exceptBranch, ct);
        return other is null ? null : Error.Conflict($"IFSC {code} is already used by {other.Bank!.ShortCode} – {other.Name}.", "ifsc");
    }

    private async Task<IReadOnlyList<BankSummary>> Project(IQueryable<Bank> q, CancellationToken ct)
    {
        var rows = await q.Select(b => new
        {
            Bank = b,
            Branches = b.Branches.Count(br => !br.IsDeleted),
            Active = b.Branches.Count(br => !br.IsDeleted && br.IsActive),
        }).AsNoTracking().ToListAsync(ct);
        return [.. rows.Select(r => ToSummary(r.Bank, r.Branches, r.Active))];
    }

    private async Task<BankSummary> SummaryAsync(Guid id, CancellationToken ct) =>
        (await Project(db.Set<Bank>().Where(b => b.Id == id), ct)).First();

    private static BankSummary ToSummary(Bank b, int branches, int active) => new(b.Id, b.Name, b.ShortCode, b.BrandColor,
        b.IfscPrefix, b.ReportFormat, b.IsActive, new BankRules(b.RateBasis, b.DistressPercent, b.RealizablePercent, b.Depreciation),
        branches, active, b.Questions.Count);

    private static IReadOnlyList<QuestionItem> Questions(Bank b) =>
        [.. b.Questions.Select(q => new QuestionItem(q.Text, q.AnswerType, q.Options, q.Required))];

    private static BranchItem ToItem(BankBranch b) => new(b.Id, b.BankId, b.Name, b.Ifsc, b.City, b.State, b.Address,
        b.ContactPerson, b.Phone, b.Email, b.IsActive);

    private static void Apply(Bank b, SaveBankRequest r)
    {
        b.Name = r.Name.Trim();
        b.ShortCode = r.ShortCode.Trim().ToUpperInvariant();
        b.BrandColor = r.BrandColor.ToUpperInvariant();
        b.IfscPrefix = Normalize(r.IfscPrefix);
        b.ReportFormat = Clean(r.ReportFormat);
        b.IsActive = r.IsActive;
        SetRules(b, r.Rules);
    }

    private static void SetRules(Bank b, BankRules rules)
    {
        b.RateBasis = rules.RateBasis;
        b.DistressPercent = rules.DistressPercent;
        b.RealizablePercent = rules.RealizablePercent;
        b.Depreciation = rules.Depreciation;
    }

    private static void Apply(BankBranch b, SaveBranchRequest r)
    {
        b.Name = r.Name.Trim();
        b.Ifsc = Normalize(r.Ifsc);
        b.City = Clean(r.City);
        b.State = Clean(r.State);
        b.Address = Clean(r.Address);
        b.ContactPerson = Clean(r.ContactPerson);
        b.Phone = Clean(r.Phone);
        b.Email = Clean(r.Email)?.ToLowerInvariant();
        b.IsActive = r.IsActive;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Normalize(string? s) => Clean(s)?.Replace(" ", "").ToUpperInvariant();
}
