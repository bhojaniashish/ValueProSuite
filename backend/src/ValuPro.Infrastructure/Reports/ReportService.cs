using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Reports;
using ValuPro.Application.Signatures;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Reports;
using ValuPro.Domain.Security;
using ValuPro.Domain.Tenancy;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Reports;

public sealed class ReportService(
    AppDbContext db,
    ICurrentUser me,
    ITenantContext tenant,
    IFileStorage files,
    ISignatureService signatures,
    IReportRenderer renderer,
    TimeProvider clock) : IReportService
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    /// <summary>Photos beyond this are left out of the PDF (banks want a handful; the rest stay on the case).</summary>
    public const int MaxPhotos = 24;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);

    private sealed class CaseRow
    {
        public required Assignment A { get; init; }
        public required Bank Bank { get; init; }
        public BankBranch? Branch { get; init; }
        public Property? Property { get; init; }
        public required string ValuerName { get; init; }
        public string? ReviewerName { get; init; }
    }

    private IQueryable<CaseRow> Cases()
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var branches = db.Set<BankBranch>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var properties = db.Set<Property>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        return from a in db.Set<Assignment>()
               join b in banks on a.BankId equals b.Id
               join u in db.Users on a.ValuerId equals u.Id
               join br in branches on a.BranchId equals (Guid?)br.Id into brs
               from br in brs.DefaultIfEmpty()
               join p in properties on a.PropertyId equals (Guid?)p.Id into ps
               from p in ps.DefaultIfEmpty()
               join r in db.Users on a.ReviewerId equals (Guid?)r.Id into rs
               from r in rs.DefaultIfEmpty()
               select new CaseRow { A = a, Bank = b, Branch = br, Property = p, ValuerName = u.FullName, ReviewerName = r != null ? r.FullName : null };
    }

    // ---------------- rules ----------------

    private static readonly AssignmentStatus[] Editable = [AssignmentStatus.InspectionDone, AssignmentStatus.DraftPrepared, AssignmentStatus.QualityReview];

    private bool CanWork(Assignment a) =>
        me.HasPermission(Permissions.Reports.Manage)
        && (me.UserId == a.ValuerId || me.UserId == a.ReviewerId || me.HasPermission(Permissions.Assignments.Manage));

    private string? ReadOnlyReason(Assignment a)
    {
        if (!CanWork(a)) return "Only the case's valuer, its reviewer or a manager can prepare the report.";
        return a.Status switch
        {
            AssignmentStatus.Cancelled => "This assignment is cancelled.",
            AssignmentStatus.Submitted or AssignmentStatus.InvoiceGenerated => "The report has been approved and submitted.",
            AssignmentStatus.New or AssignmentStatus.SiteVisitScheduled => "The report can be prepared once the site inspection is done.",
            _ => null,
        };
    }

    // ---------------- list ----------------

    public async Task<IReadOnlyList<ReportListItem>> ListAsync(ReportQuery q, CancellationToken ct = default)
    {
        var rows = Cases();
        if (q.Mine && me.UserId is { } uid) rows = rows.Where(r => r.A.ValuerId == uid || r.A.ReviewerId == uid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(r => r.A.Number.Contains(s) || r.A.ApplicantName.Contains(s) || r.A.PropertySummary.Contains(s));
        }
        rows = q.Stage switch
        {
            "review" => rows.Where(r => r.A.Status == AssignmentStatus.QualityReview),
            "done" => rows.Where(r => r.A.Status == AssignmentStatus.Submitted || r.A.Status == AssignmentStatus.InvoiceGenerated),
            _ => rows.Where(r => r.A.Status == AssignmentStatus.InspectionDone || r.A.Status == AssignmentStatus.DraftPrepared),
        };
        var list = await rows.OrderBy(r => r.A.DueDate).Take(200)
            .Select(r => new
            {
                r.A.Id, r.A.Number, r.A.Status, r.Bank.ShortCode, r.Bank.BrandColor, r.A.ApplicantName, r.A.PropertySummary, r.ValuerName, r.ReviewerName, r.A.DueDate,
                Fmv = db.Set<Valuation>().Where(v => v.AssignmentId == r.A.Id).Select(v => (decimal?)v.FairMarketValue).FirstOrDefault(),
                Drafts = db.Set<ReportFile>().Count(f => f.AssignmentId == r.A.Id && f.Kind == ReportKind.Draft),
                Last = db.Set<ReportFile>().Where(f => f.AssignmentId == r.A.Id).Max(f => (DateTimeOffset?)f.CreatedAt),
            })
            .ToListAsync(ct);
        return list.Select(x => new ReportListItem(x.Id, x.Number, x.Status, x.ShortCode, x.BrandColor, x.ApplicantName, x.PropertySummary,
            x.ValuerName, x.ReviewerName, x.DueDate, x.Fmv, x.Drafts, x.Last)).ToList();
    }

    // ---------------- detail ----------------

    private sealed record Loaded(CaseRow Row, Inspection? Inspection, Valuation? Valuation, CaseReport? Report, List<CaseDocument> Documents,
        List<AssignmentPhoto> ReportPhotos, List<ReportFile> Files, bool ValuerHasSignature);

    private async Task<Loaded?> LoadAsync(Guid assignmentId, CancellationToken ct)
    {
        var row = await Cases().FirstOrDefaultAsync(r => r.A.Id == assignmentId, ct);
        if (row is null) return null;
        var inspection = await db.Set<Inspection>().AsNoTracking().FirstOrDefaultAsync(i => i.AssignmentId == assignmentId, ct);
        var valuation = await db.Set<Valuation>().AsNoTracking().FirstOrDefaultAsync(v => v.AssignmentId == assignmentId, ct);
        var report = await db.Set<CaseReport>().FirstOrDefaultAsync(r => r.AssignmentId == assignmentId, ct);
        var docs = await db.Set<CaseDocument>().AsNoTracking().Where(d => d.AssignmentId == assignmentId).ToListAsync(ct);
        var expected = DocumentRules.Expected(row.Property?.Type).ToList();
        docs = docs.OrderBy(d => expected.IndexOf(d.Type) is var i and >= 0 ? i : 1000 + (int)d.Type).ThenBy(d => d.CreatedAt).ToList();
        var photos = await db.Set<AssignmentPhoto>().AsNoTracking().Where(p => p.AssignmentId == assignmentId && p.IncludeInReport)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.TakenAt).ToListAsync(ct);
        var reportFiles = await db.Set<ReportFile>().AsNoTracking().Where(f => f.AssignmentId == assignmentId).OrderByDescending(f => f.Number).ToListAsync(ct);
        var hasSignature = await db.Users.AnyAsync(u => u.Id == row.A.ValuerId && u.SignatureBlobPath != null, ct);
        return new Loaded(row, inspection, valuation, report, docs, photos, reportFiles, hasSignature);
    }

    private static List<ReportCheck> Checks(Loaded l)
    {
        var expected = DocumentRules.Expected(l.Row.Property?.Type);
        var pending = expected.Where(t => !l.Documents.Any(d => d.Type == t)).Select(DocumentRules.Label).ToList();
        var hasFront = l.ReportPhotos.Any(p => p.Tag == PhotoTag.Front);
        return
        [
            new("inspection", "Site inspection submitted", l.Inspection?.Status == InspectionStatus.Submitted, true,
                l.Inspection is null ? "Not started." : l.Inspection.Status == InspectionStatus.Submitted ? null : "Started but not submitted."),
            new("valuation", Construction(l.Row.A) ? "Progress and release worked out" : "Valuation saved", l.Valuation is not null, true,
                l.Valuation is null ? null
                : l.Valuation.Input.Progress is { } pr && Construction(l.Row.A)
                    ? $"{IndianNumber.Format(pr.CompletionPercent)}% complete, release ₹{IndianNumber.Format(ValuationCalculator.Progress(pr).RecommendedRelease)}"
                    : $"Fair market value ₹{IndianNumber.Format(l.Valuation.FairMarketValue)}"),
            new("documents", "Documents checklist", pending.Count == 0, false, pending.Count == 0 ? null : $"Not recorded: {string.Join(", ", pending)}."),
            new("photos", "Report photos", l.ReportPhotos.Count > 0 && hasFront, false,
                l.ReportPhotos.Count == 0 ? "No photos chosen for the report." : hasFront ? $"{l.ReportPhotos.Count} photo(s)." : $"{l.ReportPhotos.Count} photo(s), but no front elevation."),
            new("signature", "Valuer's signature", l.ValuerHasSignature, false,
                l.ValuerHasSignature ? null : $"{l.Row.ValuerName} has not saved a signature yet (needed to approve)."),
        ];
    }

    private static bool Construction(Assignment a) => a.CaseType is CaseType.StageInspection or CaseType.Reinspection;

    private ReportFields Fields(Loaded l)
    {
        var r = l.Report;
        var inspected = InspectedOn(l);
        var declaration = r?.Declaration ?? ReportText.DefaultDeclaration(inspected is { } d ? d.ToString("dd MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-IN")) : "the date of inspection");
        return new ReportFields(
            r?.ReportDate,
            r?.ValuationDate ?? inspected,
            r?.Opinion ?? DefaultOpinion(l),
            declaration,
            r?.Declaration is null,
            r?.Place ?? l.Row.Branch?.City ?? l.Row.Property?.City,
            r?.PhotosPerPage ?? 4);
    }

    private static DateOnly? InspectedOn(Loaded l) =>
        l.Inspection is { } i ? DateOnly.FromDateTime((i.SubmittedAt ?? i.StartedAt).ToOffset(India).DateTime)
        : l.Row.A.SiteVisitAt is { } v ? DateOnly.FromDateTime(v.ToOffset(India).DateTime) : null;

    /// <summary>Starts from what the valuer already wrote: valuation remarks, then the inspection's observations.</summary>
    private static string? DefaultOpinion(Loaded l) =>
        new[] { l.Valuation?.Input.Remarks, l.Inspection?.Data.Remarks.Text }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private ReportDetail Detail(Loaded l)
    {
        var a = l.Row.A;
        var checks = Checks(l);
        var readOnly = ReadOnlyReason(a);
        var blocking = checks.FirstOrDefault(c => c.Blocking && !c.Ok);
        string? generateBlocked = readOnly ?? (blocking is null ? null : $"{blocking.Label}: required before the report.");
        string? approveBlocked =
            a.Status != AssignmentStatus.QualityReview ? "The case needs to be in Quality Review to approve the report."
            : !me.HasPermission(Permissions.Reports.Approve) ? "Approving needs the \"Reports: Approve\" permission (reviewer or admin)."
            : generateBlocked
              ?? (l.ValuerHasSignature ? null : $"{l.Row.ValuerName} needs to save a signature first (account menu → My signature).");

        return new ReportDetail(a.Id, a.Number, a.Status, l.Row.Bank.ShortCode, l.Row.Bank.BrandColor, a.ApplicantName, a.PropertySummary,
            l.Row.ValuerName, l.Row.ReviewerName, l.Valuation?.FairMarketValue, checks, Fields(l),
            l.Files.Select(f => new ReportFileItem(f.Id, f.Number, f.Kind, f.SizeBytes, f.CreatedAt, f.GeneratedByName, f.FairMarketValue, f.SignedByName, f.ApprovedByName)).ToList(),
            readOnly is null, generateBlocked is null, generateBlocked, approveBlocked is null, approveBlocked, readOnly,
            l.Row.A.CaseType, Construction(l.Row.A) && l.Valuation?.Input.Progress is { } pr ? ValuationCalculator.Progress(pr).RecommendedRelease : null);
    }

    public async Task<Result<ReportDetail>> GetAsync(Guid assignmentId, CancellationToken ct = default) =>
        await LoadAsync(assignmentId, ct) is { } l ? Detail(l) : Error.NotFound("Assignment not found.");

    // ---------------- changes ----------------

    public async Task<Result<ReportDetail>> SaveAsync(Guid assignmentId, SaveReportRequest r, CancellationToken ct = default)
    {
        var l = await LoadAsync(assignmentId, ct);
        if (l is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(l.Row.A) is { } reason) return Error.Forbidden(reason);
        if (r.PhotosPerPage is not (2 or 4)) return Error.Validation("Choose 2 or 4 photos per page.", "photosPerPage");
        if (r.ReportDate is { } rd && rd > Today) return Error.Validation("The report date can't be in the future.", "reportDate");
        if (r.ValuationDate is { } vd && r.ReportDate is { } rd2 && vd > rd2) return Error.Validation("The valuation date is after the report date.", "valuationDate");
        if (r.Opinion?.Length > 4000 || r.Declaration?.Length > 4000) return Error.Validation("Keep each text under 4,000 characters.");

        var report = l.Report ?? db.Add(new CaseReport { AssignmentId = assignmentId }).Entity;
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        report.ReportDate = r.ReportDate;
        report.ValuationDate = r.ValuationDate;
        report.Opinion = T(r.Opinion);
        // Keeping the standard wording (or clearing it) stores nothing, so it follows the inspection date.
        var standard = Fields(l with { Report = null }).Declaration;
        report.Declaration = T(r.Declaration) is { } dec && dec != standard ? dec : null;
        report.Place = T(r.Place);
        report.PhotosPerPage = r.PhotosPerPage;
        await db.SaveChangesAsync(ct);
        return await GetAsync(assignmentId, ct);
    }

    public async Task<Result<ReportDetail>> GenerateDraftAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var l = await LoadAsync(assignmentId, ct);
        if (l is null) return Error.NotFound("Assignment not found.");
        var detail = Detail(l);
        if (!detail.CanGenerate) return Error.Validation(detail.GenerateBlockedReason!);

        var number = (l.Files.Count == 0 ? 0 : l.Files.Max(f => f.Number)) + 1;
        var signer = await signatures.SignerAsync(l.Row.A.ValuerId, ct);
        var pdf = renderer.Render(await ModelAsync(l, draft: true, number, signer is null ? null : new SignerBlock(signer.FullName, signer.RegistrationNo, null, "Image"), null, ct));
        await StoreAsync(l, number, ReportKind.Draft, pdf, null, ct);

        var a = await db.Set<Assignment>().FirstAsync(x => x.Id == assignmentId, ct);
        if (a.Status == AssignmentStatus.InspectionDone)
        {
            AddEvent(a, a.Status, AssignmentStatus.DraftPrepared, "Draft report prepared", $"Version {number}.");
            a.Status = AssignmentStatus.DraftPrepared;
        }
        else
        {
            AddEvent(a, a.Status, a.Status, $"Draft report version {number} generated", null);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(assignmentId, ct);
    }

    public async Task<Result<ReportDetail>> ApproveAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var l = await LoadAsync(assignmentId, ct);
        if (l is null) return Error.NotFound("Assignment not found.");
        var detail = Detail(l);
        if (!detail.CanApprove) return Error.Forbidden(detail.ApproveBlockedReason!);

        var signer = await signatures.SignerAsync(l.Row.A.ValuerId, ct);
        if (signer?.SignatureImage is null) return Error.Forbidden($"{l.Row.ValuerName} needs to save a signature first.");
        var number = (l.Files.Count == 0 ? 0 : l.Files.Max(f => f.Number)) + 1;
        var approver = me.Name ?? "Reviewer";
        var pdf = renderer.Render(await ModelAsync(l, draft: false, number,
            new SignerBlock(signer.FullName, signer.RegistrationNo, signer.SignatureImage, signer.Method.ToString()), approver, ct));
        await StoreAsync(l, number, ReportKind.Final, pdf, (signer, approver), ct);

        var a = await db.Set<Assignment>().FirstAsync(x => x.Id == assignmentId, ct);
        AddEvent(a, a.Status, AssignmentStatus.Submitted, "Report approved and signed", $"Signed by {signer.FullName}, approved by {approver}.");
        a.Status = AssignmentStatus.Submitted;
        await db.SaveChangesAsync(ct);
        return await GetAsync(assignmentId, ct);
    }

    private async Task StoreAsync(Loaded l, int number, ReportKind kind, byte[] pdf, (Signer Signer, string Approver)? final, CancellationToken ct)
    {
        var name = $"{l.Row.A.Number.Replace('/', '-')}-v{number}-{(kind == ReportKind.Final ? "final" : "draft")}.pdf";
        await using var stream = new MemoryStream(pdf);
        var path = await files.UploadAsync($"assignments/{l.Row.A.Id:N}/reports", name, stream, "application/pdf", ct);
        db.Add(new ReportFile
        {
            AssignmentId = l.Row.A.Id,
            Number = number,
            Kind = kind,
            BlobPath = path,
            SizeBytes = pdf.Length,
            GeneratedByName = me.Name ?? "Unknown",
            FairMarketValue = l.Valuation!.FairMarketValue,
            SignedByUserId = final?.Signer.UserId,
            SignedByName = final?.Signer.FullName,
            SignatureMethod = final?.Signer.Method.ToString(),
            ApprovedByName = final?.Approver,
        });
    }

    private void AddEvent(Assignment a, AssignmentStatus from, AssignmentStatus to, string title, string? note) =>
        db.Add(new AssignmentEvent { AssignmentId = a.Id, At = clock.GetUtcNow(), ByUserId = me.UserId, ByName = me.Name ?? "System", FromStatus = from, ToStatus = to, Title = title, Note = note });

    // ---------------- the model the template prints ----------------

    private async Task<ReportModel> ModelAsync(Loaded l, bool draft, int version, SignerBlock? signer, string? approvedBy, CancellationToken ct)
    {
        var row = l.Row;
        var a = row.A;
        var p = row.Property;
        var t = await db.Set<Tenant>().AsNoTracking().FirstAsync(x => x.Id == tenant.TenantId, ct);
        var fields = Fields(l);

        var photos = new List<PhotoBlock>();
        foreach (var ph in l.ReportPhotos.Take(MaxPhotos))
            if (await ReadAsync(ph.BlobPath, ct) is { } bytes)
                photos.Add(new PhotoBlock(photos.Count + 1, InspectionRules.Label(ph.Tag), ph.Caption, ph.TakenAt, ph.Latitude, ph.Longitude, bytes));

        var d = l.Inspection?.Data;
        var address = p is null ? a.PropertySummary
            : string.Join(", ", new[] { p.Address, p.City, p.State, p.Pincode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var branchAddress = row.Branch is null ? null
            : string.Join(", ", new[] { row.Branch.Address, row.Branch.City }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } ba ? ba : null;
        var person = d?.Visit.PersonMet is { } pm ? d.Visit.PersonRelation is { } rel ? $"{pm} ({rel})" : pm : null;
        var valuation = l.Valuation!;
        var previous = a.PreviousAssignmentId is { } prevId ? await PreviousAsync(prevId, ct) : null;

        return new ReportModel(
            draft, version,
            new FirmBlock(t.Name, string.Join(", ", new[] { t.Address, t.State }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } addr ? addr : null,
                t.Phone, t.ContactEmail, t.RegisteredValuerNo, t.IbbiRegistration, t.LogoBlobPath is { } logo ? await ReadAsync(logo, ct) : null, t.PrimaryColor),
            a.Number, fields.ReportDate ?? Today, fields.ValuationDate ?? Today, InspectedOn(l),
            row.Bank.Name, row.Branch?.Name, branchAddress, a.BankReference, a.Purpose, a.ApplicantName, p?.OwnerName,
            p?.Type, address, p?.SurveyNo, p?.CtsNo, p?.PlotNo, l.Inspection?.Latitude, l.Inspection?.Longitude,
            d, person,
            l.Documents.Select(x => new DocumentRow(x.Title ?? DocumentRules.Label(x.Type), x.Availability, x.Number, x.IssuedOn, x.IssuedBy, x.Notes)).ToList(),
            valuation.Input, ValuationCalculator.Calculate(valuation.Input),
            photos, fields.PhotosPerPage, fields.Opinion, fields.Declaration, fields.Place, signer, approvedBy,
            a.CaseType, previous?.Number, previous?.Percent, previous?.On);
    }

    private sealed record PreviousVisit(string Number, decimal? Percent, DateOnly? On);

    private async Task<PreviousVisit?> PreviousAsync(Guid prevId, CancellationToken ct)
    {
        var number = await db.Set<Assignment>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).Where(x => x.Id == prevId).Select(x => x.Number).FirstOrDefaultAsync(ct);
        if (number is null) return null;
        var pi = await db.Set<Inspection>().AsNoTracking().FirstOrDefaultAsync(x => x.AssignmentId == prevId, ct);
        decimal? percent = pi is null ? null : pi.Data.Construction.Stages.Count > 0 ? pi.Data.Construction.OverallPercent() : pi.Data.Building.CompletionPercent;
        DateOnly? on = pi is null ? null : DateOnly.FromDateTime((pi.SubmittedAt ?? pi.StartedAt).ToOffset(India).DateTime);
        return new PreviousVisit(number, percent, on);
    }

    private async Task<byte[]?> ReadAsync(string path, CancellationToken ct)
    {
        await using var s = await files.OpenReadAsync(path, ct);
        if (s is null) return null;
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task<Result<ReportPdf>> OpenAsync(Guid fileId, CancellationToken ct = default)
    {
        var f = await db.Set<ReportFile>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId, ct);
        if (f is null) return Error.NotFound("Report not found.");
        var stream = await files.OpenReadAsync(f.BlobPath, ct);
        return stream is null ? Error.NotFound("The report file is missing.") : new ReportPdf(stream, Path.GetFileName(f.BlobPath));
    }
}
