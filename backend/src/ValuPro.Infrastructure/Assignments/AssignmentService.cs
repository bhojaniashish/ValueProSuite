using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Assignments;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Security;
using ValuPro.Domain.Settings;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Assignments;

public sealed class AssignmentService(
    AppDbContext db,
    INumberSequence numbers,
    ICurrentUser me,
    TimeProvider clock) : IAssignmentService
{
    /// <summary>Who can be assigned as valuer / reviewer.</summary>
    public static readonly string[] ValuerRoles = [Roles.Admin, Roles.SeniorValuer, Roles.JuniorValuer, Roles.Architect];
    public static readonly string[] ReviewerRoles = [Roles.Admin, Roles.Reviewer, Roles.SeniorValuer];

    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);

    /// <summary>"Today" for due dates is the firm's day in India, not the server's UTC day.</summary>
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);

    // ---------------- query building ----------------

    private sealed class Row
    {
        public required Assignment A { get; init; }
        public required string BankShortCode { get; init; }
        public required string BankName { get; init; }
        public required string BankColor { get; init; }
        public string? BranchName { get; init; }
        public required string ValuerName { get; init; }
    }

    /// <summary>
    /// Assignments with bank, branch and valuer names. Banks and branches are joined even when removed later,
    /// so closed cases keep showing who they were for.
    /// </summary>
    private IQueryable<Row> Rows()
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var branches = db.Set<BankBranch>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        return from a in db.Set<Assignment>()
               join b in banks on a.BankId equals b.Id
               join u in db.Users on a.ValuerId equals u.Id
               join br in branches on a.BranchId equals (Guid?)br.Id into brs
               from br in brs.DefaultIfEmpty()
               select new Row
               {
                   A = a,
                   BankShortCode = b.ShortCode,
                   BankName = b.Name,
                   BankColor = b.BrandColor,
                   BranchName = br != null ? br.Name : null,
                   ValuerName = u.FullName,
               };
    }

    private AssignmentListItem ToItem(Row r) => new(r.A.Id, r.A.Number, r.A.Status, r.A.Priority, r.A.BankId, r.BankShortCode,
        r.BankName, r.BankColor, r.A.BranchId, r.BranchName, r.A.ApplicantName, r.A.ClientId, r.A.PropertySummary, r.A.PropertyId,
        r.A.ValuerId, r.ValuerName, r.A.DueDate, r.A.Fee, r.A.IsOpen && r.A.DueDate < Today, r.A.CreatedAt);

    // ---------------- read ----------------

    public async Task<PagedResult<AssignmentListItem>> ListAsync(AssignmentQuery query, CancellationToken ct = default)
    {
        var rows = Filter(Rows(), query);

        rows = (query.Sort?.ToLowerInvariant(), query.Desc) switch
        {
            ("number", false) => rows.OrderBy(r => r.A.Number),
            ("number", true) => rows.OrderByDescending(r => r.A.Number),
            ("status", false) => rows.OrderBy(r => r.A.Status).ThenBy(r => r.A.DueDate),
            ("status", true) => rows.OrderByDescending(r => r.A.Status).ThenBy(r => r.A.DueDate),
            ("priority", false) => rows.OrderBy(r => r.A.Priority).ThenBy(r => r.A.DueDate),
            ("priority", true) => rows.OrderByDescending(r => r.A.Priority).ThenBy(r => r.A.DueDate),
            ("bank", false) => rows.OrderBy(r => r.BankShortCode).ThenBy(r => r.A.DueDate),
            ("bank", true) => rows.OrderByDescending(r => r.BankShortCode).ThenBy(r => r.A.DueDate),
            ("applicant", false) => rows.OrderBy(r => r.A.ApplicantName),
            ("applicant", true) => rows.OrderByDescending(r => r.A.ApplicantName),
            ("created", false) => rows.OrderBy(r => r.A.CreatedAt),
            ("created", true) => rows.OrderByDescending(r => r.A.CreatedAt),
            ("due", true) => rows.OrderByDescending(r => r.A.DueDate).ThenByDescending(r => r.A.Priority),
            _ => rows.OrderBy(r => r.A.DueDate).ThenByDescending(r => r.A.Priority), // most urgent first
        };

        var total = await rows.CountAsync(ct);
        var page = await rows.AsNoTracking().Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<AssignmentListItem>([.. page.Select(ToItem)], total, query.Page, query.PageSize);
    }

    private IQueryable<Row> Filter(IQueryable<Row> rows, AssignmentQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(r => r.A.Number.Contains(s) || r.A.ApplicantName.Contains(s) || r.A.PropertySummary.Contains(s) ||
                                   r.A.BankReference!.Contains(s) || r.BankName.Contains(s) || r.BankShortCode.Contains(s) ||
                                   r.BranchName!.Contains(s) || r.ValuerName.Contains(s));
        }
        if (q.Status is { } status) rows = rows.Where(r => r.A.Status == status);
        if (q.OpenOnly) rows = rows.Where(r => r.A.Status != AssignmentStatus.Submitted && r.A.Status != AssignmentStatus.InvoiceGenerated && r.A.Status != AssignmentStatus.Cancelled);
        if (q.BankId is { } bank) rows = rows.Where(r => r.A.BankId == bank);
        if (q.Priority is { } priority) rows = rows.Where(r => r.A.Priority == priority);
        if (q.ValuerId is { } valuer) rows = rows.Where(r => r.A.ValuerId == valuer);
        if (q.ClientId is { } client) rows = rows.Where(r => r.A.ClientId == client);
        if (q.PropertyId is { } property) rows = rows.Where(r => r.A.PropertyId == property);
        if (q.Mine && me.UserId is { } userId) rows = rows.Where(r => r.A.ValuerId == userId || r.A.ReviewerId == userId);
        if (q.Overdue)
        {
            var today = Today;
            rows = rows.Where(r => r.A.DueDate < today && r.A.Status != AssignmentStatus.Submitted &&
                                   r.A.Status != AssignmentStatus.InvoiceGenerated && r.A.Status != AssignmentStatus.Cancelled);
        }
        return rows;
    }

    public async Task<AssignmentPipeline> GetPipelineAsync(bool mine, CancellationToken ct = default)
    {
        var q = db.Set<Assignment>().AsQueryable();
        if (mine && me.UserId is { } userId) q = q.Where(a => a.ValuerId == userId || a.ReviewerId == userId);

        var counts = await q.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(AssignmentStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;

        var today = Today;
        var weekEnd = today.AddDays(7);
        var open = q.Where(a => a.Status != AssignmentStatus.Submitted && a.Status != AssignmentStatus.InvoiceGenerated && a.Status != AssignmentStatus.Cancelled);
        return new AssignmentPipeline(
            [.. AssignmentWorkflow.Stages.Select(s => new PipelineStage(s, Count(s)))],
            await open.CountAsync(ct),
            await open.CountAsync(a => a.DueDate < today, ct),
            await open.CountAsync(a => a.DueDate >= today && a.DueDate <= weekEnd, ct),
            Count(AssignmentStatus.Cancelled));
    }

    public async Task<Result<AssignmentDetail>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await Rows().AsNoTracking().FirstOrDefaultAsync(r => r.A.Id == id, ct);
        if (row is null) return Error.NotFound("Assignment not found.");

        var events = await db.Set<AssignmentEvent>().AsNoTracking().Where(e => e.AssignmentId == id)
            .OrderByDescending(e => e.At).ToListAsync(ct);
        var reviewer = row.A.ReviewerId is { } rid
            ? await db.Users.Where(u => u.Id == rid).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;

        var a = row.A;
        return new AssignmentDetail(ToItem(row), a.BankReference, a.Purpose, a.ApplicantPhone, a.ReviewerId, reviewer, a.SiteVisitAt,
            a.Remarks, [.. events.Select(e => new AssignmentEventItem(e.At, e.ByName, e.FromStatus, e.ToStatus, e.Title, e.Note))],
            Moves(a));
    }

    // ---------------- write ----------------

    public async Task<Result<AssignmentDetail>> CreateAsync(SaveAssignmentRequest r, CancellationToken ct = default)
    {
        if (!CanManage) return Error.Forbidden("You do not have permission to create assignments.");
        if (r.DueDate < Today) return Error.Validation("The due date cannot be in the past.", "dueDate");
        if (await CheckLinksAsync(r, null, ct) is { } error) return error;

        var number = await NextNumberAsync(ct);
        var a = new Assignment { Number = number, ApplicantName = "", PropertySummary = "" };
        Apply(a, r);
        db.Add(a);
        AddEvent(a, null, AssignmentStatus.New, "Assignment created", $"Assigned to {await ValuerNameAsync(r.ValuerId, ct)}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(a.Id, ct);
    }

    public async Task<Result<AssignmentDetail>> UpdateAsync(Guid id, SaveAssignmentRequest r, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (!Moves(a).CanEdit) return Error.Forbidden(a.Status == AssignmentStatus.Cancelled
            ? "Reopen the assignment before editing it."
            : "This assignment can no longer be edited.");
        if (await CheckLinksAsync(r, a, ct) is { } error) return error;

        var changes = new List<string>();
        if (a.ValuerId != r.ValuerId) changes.Add($"valuer → {await ValuerNameAsync(r.ValuerId, ct)}");
        if (a.DueDate != r.DueDate) changes.Add($"due date → {r.DueDate:dd MMM yyyy}");
        if (a.Priority != r.Priority) changes.Add($"priority → {r.Priority}");
        if (a.Fee != r.Fee) changes.Add($"fee → ₹{r.Fee:N0}");

        Apply(a, r);
        AddEvent(a, a.Status, a.Status, "Details updated", changes.Count > 0 ? string.Join(", ", changes) : null);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<AssignmentDetail>> MoveAsync(Guid id, MoveRequest r, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("Assignment not found.");

        var moves = Moves(a);
        var kind = AssignmentWorkflow.Classify(a.Status, r.To);
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        var label = AssignmentWorkflow.Label(r.To);

        switch (kind)
        {
            case MoveKind.Invalid:
                return Error.Validation($"An assignment in \"{AssignmentWorkflow.Label(a.Status)}\" cannot move to \"{label}\".");
            case MoveKind.Forward when !moves.CanMoveNext:
                return Error.Forbidden(moves.NextBlockedReason ?? "You cannot move this assignment.");
            case MoveKind.Back when moves.Back is null:
                return Error.Forbidden("You cannot send this assignment back.");
            case MoveKind.Back when note is null:
                return Error.Validation("Say why it is being sent back.", "note");
            case MoveKind.Cancel when !moves.CanCancel:
                return Error.Forbidden("You do not have permission to cancel assignments.");
            case MoveKind.Cancel when note is null:
                return Error.Validation("Give a reason for cancelling.", "note");
            case MoveKind.Reopen when !moves.CanReopen:
                return Error.Forbidden("You do not have permission to reopen assignments.");
        }

        if (r.To == AssignmentStatus.SiteVisitScheduled)
        {
            if (r.SiteVisitAt is { } visit) a.SiteVisitAt = visit;
            if (a.SiteVisitAt is null) return Error.Validation("Choose the site visit date and time.", "siteVisitAt");
        }

        var title = kind switch
        {
            MoveKind.Forward => r.To == AssignmentStatus.SiteVisitScheduled
                ? $"Site visit scheduled for {a.SiteVisitAt!.Value.ToOffset(India):dd MMM, h:mm tt}"
                : $"Moved to {label}",
            MoveKind.Back => $"Returned to {label}",
            MoveKind.Cancel => "Cancelled",
            _ => "Reopened",
        };
        var from = a.Status;
        a.Status = r.To;
        AddEvent(a, from, r.To, title, note);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (!CanManage) return Error.Forbidden("You do not have permission to delete assignments.");
        if (a.Status != AssignmentStatus.New)
            return Error.Conflict("Only new assignments can be deleted. Cancel this one instead, so its history is kept.");
        db.Remove(a); // soft delete; the number is not reused
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------- lookups ----------------

    public async Task<AssignmentLookups> GetLookupsAsync(CancellationToken ct = default)
    {
        var banks = await db.Set<Bank>().Where(b => b.IsActive).OrderBy(b => b.Name)
            .Select(b => new BankOption(b.Id, b.Name, b.ShortCode, b.BrandColor,
                b.Branches.Where(br => br.IsActive && !br.IsDeleted).OrderBy(br => br.City).ThenBy(br => br.Name)
                    .Select(br => new BranchOption(br.Id, br.Name, br.City)).ToList()))
            .ToListAsync(ct);

        var people = await (from u in db.Users
                            join ur in db.UserRoles on u.Id equals ur.UserId
                            join role in db.Roles on ur.RoleId equals role.Id
                            where u.Status != UserStatus.Inactive
                            orderby u.FullName
                            select new { u.Id, u.FullName, Role = role.Name! }).ToListAsync(ct);

        var key = SequenceKey(Today.Year);
        var last = await db.Set<Domain.Common.NumberSequence>().Where(s => s.Key == key).Select(s => (long?)s.Last).FirstOrDefaultAsync(ct) ?? 0;

        return new AssignmentLookups(
            banks,
            [.. people.Where(p => ValuerRoles.Contains(p.Role)).Select(p => new PersonOption(p.Id, p.FullName, p.Role))],
            [.. people.Where(p => ReviewerRoles.Contains(p.Role)).Select(p => new PersonOption(p.Id, p.FullName, p.Role))],
            Format(await PrefixAsync(ct), Today.Year, last + 1));
    }

    public async Task<IReadOnlyList<ClientOption>> SearchClientsAsync(string? search, CancellationToken ct = default)
    {
        var q = db.Set<Client>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s) || c.Mobile.Contains(s) || c.Pan!.Contains(s));
        }
        return await q.OrderBy(c => c.Name).Take(20)
            .Select(c => new ClientOption(c.Id, c.Code, c.Name, c.Mobile, c.Type, c.City)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PropertyOption>> SearchPropertiesAsync(string? search, CancellationToken ct = default)
    {
        var q = db.Set<Property>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Code.Contains(s) || p.OwnerName.Contains(s) || p.Address.Contains(s) || p.City!.Contains(s) ||
                             p.SurveyNo!.Contains(s) || p.CtsNo!.Contains(s));
        }
        var list = await q.OrderByDescending(p => p.Code).Take(20).ToListAsync(ct);
        return [.. list.Select(p => new PropertyOption(p.Id, p.Code, Summary(p), p.OwnerName, p.Type))];
    }

    /// <summary>"Residential Flat, Vesu, Surat" — used as the assignment's property line.</summary>
    public static string Summary(Property p)
    {
        var type = System.Text.RegularExpressions.Regex.Replace(p.Type.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
        var where = string.Join(", ", new[] { p.Address, p.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var text = $"{type}, {where}";
        return text.Length <= 300 ? text : text[..300];
    }

    // ---------------- rules ----------------

    private bool CanManage => me.HasPermission(Permissions.Assignments.Manage);

    /// <summary>
    /// Managers can do everything. The assigned valuer and reviewer can move their own case through the field and drafting stages.
    /// Passing quality review needs report-approval permission; marking invoiced needs billing or assignment management.
    /// </summary>
    private AllowedMoves Moves(Assignment a)
    {
        var mineAsValuer = me.UserId == a.ValuerId;
        var mineAsReviewer = a.ReviewerId is not null && me.UserId == a.ReviewerId;
        var canWork = CanManage || mineAsValuer || mineAsReviewer;
        var next = AssignmentWorkflow.Next(a.Status);
        var back = AssignmentWorkflow.Previous(a.Status);

        string? blocked = null;
        if (a.Status == AssignmentStatus.Cancelled) next = null;
        else if (next is null) blocked = null;
        else if (!canWork) blocked = "Only the assigned valuer, the reviewer or a manager can move this assignment.";
        else if (next == AssignmentStatus.Submitted && !me.HasPermission(Permissions.Reports.Approve))
            blocked = "Approving after quality review needs the 'Reports: Approve' permission (Reviewer or Admin).";
        else if (next == AssignmentStatus.InvoiceGenerated && !CanManage && !me.HasPermission(Permissions.Billing.Manage))
            blocked = "Marking as invoiced needs billing or assignment-management permission.";

        return new AllowedMoves(
            next,
            next is not null && blocked is null,
            blocked,
            canWork && a.Status != AssignmentStatus.Cancelled && a.Status != AssignmentStatus.InvoiceGenerated ? back : null,
            CanManage && AssignmentWorkflow.Classify(a.Status, AssignmentStatus.Cancelled) == MoveKind.Cancel,
            CanManage && a.Status == AssignmentStatus.Cancelled,
            CanManage && a.Status is not (AssignmentStatus.Cancelled or AssignmentStatus.InvoiceGenerated),
            CanManage && a.Status == AssignmentStatus.New);
    }

    private async Task<Error?> CheckLinksAsync(SaveAssignmentRequest r, Assignment? existing, CancellationToken ct)
    {
        var bank = await db.Set<Bank>().FirstOrDefaultAsync(b => b.Id == r.BankId, ct);
        if (bank is null) return Error.Validation("Choose a bank from the list.", "bankId");
        var bankChanged = existing is null || existing.BankId != r.BankId;
        if (!bank.IsActive && bankChanged) return Error.Validation($"{bank.ShortCode} is marked inactive. Activate it in Banks first.", "bankId");

        if (r.BranchId is { } branchId && !await db.Set<BankBranch>().AnyAsync(b => b.Id == branchId && b.BankId == r.BankId, ct))
            return Error.Validation($"Choose a branch of {bank.ShortCode}.", "branchId");

        if (!await IsInRoleAsync(r.ValuerId, ValuerRoles, ct))
            return Error.Validation("Choose an active valuer.", "valuerId");
        if (r.ReviewerId is { } reviewer && !await IsInRoleAsync(reviewer, ReviewerRoles, ct))
            return Error.Validation("Choose an active reviewer.", "reviewerId");
        if (r.ReviewerId is not null && r.ReviewerId == r.ValuerId)
            return Error.Validation("The reviewer should be someone other than the valuer.", "reviewerId");

        if (r.ClientId is { } clientId && !await db.Set<Client>().AnyAsync(c => c.Id == clientId, ct))
            return Error.Validation("The selected client no longer exists.", "clientId");
        if (r.PropertyId is { } propertyId && !await db.Set<Property>().AnyAsync(p => p.Id == propertyId, ct))
            return Error.Validation("The selected property no longer exists.", "propertyId");
        return null;
    }

    private Task<bool> IsInRoleAsync(Guid userId, string[] roles, CancellationToken ct) =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join role in db.Roles on ur.RoleId equals role.Id
         where u.Id == userId && u.Status != UserStatus.Inactive && roles.Contains(role.Name!)
         select u.Id).AnyAsync(ct);

    private async Task<string> ValuerNameAsync(Guid id, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == id).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "valuer";

    private void AddEvent(Assignment a, AssignmentStatus? from, AssignmentStatus to, string title, string? note) =>
        db.Add(new AssignmentEvent
        {
            AssignmentId = a.Id,
            At = clock.GetUtcNow(),
            ByUserId = me.UserId,
            ByName = me.Name ?? "System",
            FromStatus = from,
            ToStatus = to,
            Title = title,
            Note = note,
        });

    private static void Apply(Assignment a, SaveAssignmentRequest r)
    {
        a.BankId = r.BankId;
        a.BranchId = r.BranchId;
        a.BankReference = Clean(r.BankReference);
        a.Purpose = Clean(r.Purpose);
        a.ApplicantName = r.ApplicantName.Trim();
        a.ClientId = r.ClientId;
        a.ApplicantPhone = Clean(r.ApplicantPhone);
        a.PropertySummary = r.PropertySummary.Trim();
        a.PropertyId = r.PropertyId;
        a.ValuerId = r.ValuerId;
        a.ReviewerId = r.ReviewerId;
        a.Priority = r.Priority;
        a.DueDate = r.DueDate;
        a.SiteVisitAt = r.SiteVisitAt ?? a.SiteVisitAt;
        a.Fee = Math.Round(r.Fee, 2);
        a.Remarks = Clean(r.Remarks);
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var year = Today.Year;
        var prefix = await PrefixAsync(ct); // read before NextAsync saves, so nothing else is pending
        return Format(prefix, year, await numbers.NextAsync(SequenceKey(year), ct));
    }

    private async Task<string> PrefixAsync(CancellationToken ct) =>
        await db.Set<FirmSettings>().Select(s => s.AssignmentPrefix).FirstOrDefaultAsync(ct) ?? "VAL";

    private static string SequenceKey(int year) => $"assignment:{year}";
    private static string Format(string prefix, int year, long n) => $"{prefix}/{year}/{n:0000}";
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
