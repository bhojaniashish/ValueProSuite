using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Documents;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Documents;

public sealed class DocumentService(AppDbContext db, ICurrentUser me, IFileStorage files, TimeProvider clock) : IDocumentService
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    private static readonly Dictionary<string, string> FileTypes = new()
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);

    // ---------------- rules ----------------

    /// <summary>The office (assignment managers), or the case's valuer / reviewer with inspection rights.</summary>
    private bool CanWork(Assignment a) =>
        me.HasPermission(Permissions.Assignments.Manage)
        || (me.HasPermission(Permissions.Inspections.Manage) && (me.UserId == a.ValuerId || me.UserId == a.ReviewerId));

    private string? ReadOnlyReason(Assignment a)
    {
        if (!CanWork(a)) return "Only the office, the case's valuer or its reviewer can change its documents.";
        return a.Status switch
        {
            AssignmentStatus.Cancelled => "This assignment is cancelled.",
            AssignmentStatus.Submitted or AssignmentStatus.InvoiceGenerated => "The report has been submitted, so its documents are locked.",
            _ => null,
        };
    }

    // ---------------- mapping ----------------

    private DocumentItem Item(CaseDocument d, string? copiedFromCase = null) => new(
        d.Id, d.AssignmentId, d.Type, DocumentRules.Label(d.Type), d.Title, d.Availability, d.Number, d.IssuedOn, d.IssuedBy, d.ValidUntil,
        DocumentRules.ExpiryState(d.ValidUntil, Today), d.Notes,
        d.Files.OrderByDescending(f => f.Version).Select(f => new DocumentFileItem(f.Id, f.Version, f.FileName, f.ContentType, f.SizeBytes, f.CreatedAt, f.UploadedByName)).ToList(),
        copiedFromCase, d.CreatedAt);

    /// <summary>Case numbers for documents copied from earlier cases.</summary>
    private async Task<Dictionary<Guid, string>> SourceCasesAsync(IEnumerable<CaseDocument> docs, CancellationToken ct)
    {
        var sources = docs.Where(d => d.CopiedFromId is not null).Select(d => d.CopiedFromId!.Value).Distinct().ToList();
        if (sources.Count == 0) return [];
        return await (from d in db.Set<CaseDocument>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                      join a in db.Set<Assignment>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]) on d.AssignmentId equals a.Id
                      where sources.Contains(d.Id)
                      select new { d.Id, a.Number }).ToDictionaryAsync(x => x.Id, x => x.Number, ct);
    }

    // ---------------- firm-wide list ----------------

    public async Task<DocumentPage> ListAsync(DocumentQuery q, CancellationToken ct = default)
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var rows = from d in db.Set<CaseDocument>()
                   join a in db.Set<Assignment>() on d.AssignmentId equals a.Id
                   join b in banks on a.BankId equals b.Id
                   select new { D = d, A = a, b.ShortCode, b.BrandColor };

        if (q.Mine && me.UserId is { } uid) rows = rows.Where(x => x.A.ValuerId == uid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(x => x.A.Number.Contains(s) || x.A.ApplicantName.Contains(s) || x.A.PropertySummary.Contains(s)
                || (x.D.Number != null && x.D.Number.Contains(s)) || (x.D.Title != null && x.D.Title.Contains(s)));
        }
        if (q.Type is { } type) rows = rows.Where(x => x.D.Type == type);

        var today = Today;
        var soon = today.AddDays(DocumentRules.ExpiringDays);
        var summary = new DocumentSummary(
            await rows.CountAsync(ct),
            await rows.CountAsync(x => x.D.Files.Any(), ct),
            await rows.CountAsync(x => x.D.Availability == DocumentAvailability.NotProvided, ct),
            await rows.CountAsync(x => x.D.ValidUntil >= today && x.D.ValidUntil <= soon, ct),
            await rows.CountAsync(x => x.D.ValidUntil < today, ct));

        if (q.Expiry == "Expiring") rows = rows.Where(x => x.D.ValidUntil >= today && x.D.ValidUntil <= soon);
        if (q.Expiry == "Expired") rows = rows.Where(x => x.D.ValidUntil < today);

        var total = await rows.CountAsync(ct);
        var ordered = q.Expiry is null
            ? rows.OrderByDescending(x => x.D.CreatedAt)
            : rows.OrderBy(x => x.D.ValidUntil);
        var page = await ordered.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(x => new { x.D, x.A.Number, x.ShortCode, x.BrandColor, x.A.ApplicantName })
            .ToListAsync(ct);
        var ids = page.Select(x => x.D.Id).ToList();
        var fileList = await db.Set<DocumentFile>().Where(f => ids.Contains(f.DocumentId)).ToListAsync(ct);
        foreach (var p in page) p.D.Files = fileList.Where(f => f.DocumentId == p.D.Id).ToList();
        var sources = await SourceCasesAsync(page.Select(x => x.D), ct);

        var items = page.Select(x => new DocumentListItem(Item(x.D, x.D.CopiedFromId is { } s ? sources.GetValueOrDefault(s) : null), x.Number, x.ShortCode, x.BrandColor, x.ApplicantName)).ToList();
        return new DocumentPage(new PagedResult<DocumentListItem>(items, total, q.Page, q.PageSize), summary);
    }

    // ---------------- one case ----------------

    public async Task<Result<CaseDocuments>> GetCaseAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var properties = db.Set<Property>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var row = await (from a in db.Set<Assignment>()
                         join b in banks on a.BankId equals b.Id
                         join p in properties on a.PropertyId equals (Guid?)p.Id into ps
                         from p in ps.DefaultIfEmpty()
                         where a.Id == assignmentId
                         select new { A = a, b.ShortCode, b.BrandColor, Type = p != null ? (PropertyType?)p.Type : null })
            .FirstOrDefaultAsync(ct);
        if (row is null) return Error.NotFound("Assignment not found.");

        var expected = DocumentRules.Expected(row.Type);
        var docs = Ordered(await db.Set<CaseDocument>().Include(d => d.Files).Where(d => d.AssignmentId == assignmentId).ToListAsync(ct), expected);

        // Documents recorded on earlier cases of the same property, not already reused here.
        var earlier = new List<EarlierDocument>();
        if (row.A.PropertyId is { } pid)
        {
            var reused = docs.Where(d => d.CopiedFromId is not null).Select(d => d.CopiedFromId!.Value).ToHashSet();
            var others = await (from d in db.Set<CaseDocument>().Include(d => d.Files)
                                join a in db.Set<Assignment>() on d.AssignmentId equals a.Id
                                where d.PropertyId == pid && d.AssignmentId != assignmentId && d.Availability != DocumentAvailability.NotProvided
                                orderby d.CreatedAt descending
                                select new { D = d, a.Number }).Take(50).ToListAsync(ct);
            earlier = others.Where(o => !reused.Contains(o.D.Id) && !reused.Contains(o.D.CopiedFromId ?? Guid.Empty))
                .Select(o => new EarlierDocument(Item(o.D), o.Number)).ToList();
        }

        var sources = await SourceCasesAsync(docs, ct);
        var items = docs.Select(d => Item(d, d.CopiedFromId is { } s ? sources.GetValueOrDefault(s) : null)).ToList();
        var checklist = expected.Select(t => new ChecklistItem(
            t, DocumentRules.Label(t),
            docs.Any(d => d.Type == t && d.Availability != DocumentAvailability.NotProvided),
            docs.Any(d => d.Type == t) && docs.Where(d => d.Type == t).All(d => d.Availability == DocumentAvailability.NotProvided))).ToList();

        var reason = ReadOnlyReason(row.A);
        return new CaseDocuments(row.A.Id, row.A.Number, row.A.Status, row.ShortCode, row.BrandColor, row.A.ApplicantName, row.A.PropertySummary,
            row.Type, items, checklist, earlier, reason is null, reason);
    }

    // ---------------- changes ----------------

    public async Task<Result<DocumentItem>> CreateAsync(Guid assignmentId, SaveDocumentRequest r, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(a) is { } reason) return Error.Forbidden(reason);
        var d = new CaseDocument { AssignmentId = a.Id, PropertyId = a.PropertyId };
        Apply(d, r);
        db.Add(d);
        await db.SaveChangesAsync(ct);
        return Item(d);
    }

    public async Task<Result<DocumentItem>> UpdateAsync(Guid documentId, SaveDocumentRequest r, CancellationToken ct = default)
    {
        var (d, error) = await EditableAsync(documentId, ct);
        if (error is not null) return error;
        Apply(d!, r);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(d!, ct);
    }

    public async Task<Result<bool>> DeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        var (d, error) = await EditableAsync(documentId, ct);
        if (error is not null) return error;
        db.Remove(d!); // soft delete; files stay with the case's history
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Result<DocumentItem>> UploadAsync(Guid documentId, Stream content, string fileName, string contentType, long size, CancellationToken ct = default)
    {
        var (d, error) = await EditableAsync(documentId, ct);
        if (error is not null) return error;
        if (!FileTypes.TryGetValue(contentType, out var ext)) return Error.Validation("Upload a PDF or a photo (JPEG, PNG, WebP).", "file");
        if (size <= 0 || size > MaxFileBytes) return Error.Validation("Files can be up to 20 MB.", "file");

        var version = d!.Files.Count == 0 ? 1 : d.Files.Max(f => f.Version) + 1;
        var path = await files.UploadAsync($"assignments/{d.AssignmentId:N}/documents", $"{d.Id:N}-v{version}{ext}", content, contentType, ct);
        var name = Path.GetFileName(fileName);
        var file = new DocumentFile
        {
            DocumentId = d.Id,
            Version = version,
            BlobPath = path,
            FileName = string.IsNullOrWhiteSpace(name) ? $"{DocumentRules.Label(d.Type)}{ext}" : name.Length > 200 ? name[^200..] : name,
            ContentType = contentType,
            SizeBytes = size,
            UploadedByName = me.Name ?? "Unknown",
        };
        db.Add(file); // added explicitly: the id is set up front, so through the navigation EF would treat it as existing
        // A copy was evidently obtained; if it was marked as not provided, it is now a copy.
        if (d.Availability == DocumentAvailability.NotProvided) d.Availability = DocumentAvailability.CopyOnly;
        await db.SaveChangesAsync(ct);
        return await ItemAsync(d, ct);
    }

    public async Task<Result<DocumentItem>> ReuseAsync(Guid assignmentId, Guid sourceDocumentId, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(a) is { } reason) return Error.Forbidden(reason);
        var src = await db.Set<CaseDocument>().Include(d => d.Files).FirstOrDefaultAsync(d => d.Id == sourceDocumentId, ct);
        if (src is null || src.PropertyId is null || src.PropertyId != a.PropertyId)
            return Error.Validation("Only documents from earlier cases of the same property can be reused.");
        var original = src.CopiedFromId ?? src.Id;
        if (await db.Set<CaseDocument>().AnyAsync(d => d.AssignmentId == assignmentId && (d.CopiedFromId == original || d.Id == original), ct))
            return Error.Conflict("This document is already on the case.");

        var copy = new CaseDocument
        {
            AssignmentId = a.Id, PropertyId = a.PropertyId, Type = src.Type, Title = src.Title, Availability = src.Availability,
            Number = src.Number, IssuedOn = src.IssuedOn, IssuedBy = src.IssuedBy, ValidUntil = src.ValidUntil, Notes = src.Notes,
            CopiedFromId = original,
            // The same stored files, listed again: nothing is copied in storage.
            Files = src.Files.Select(f => new DocumentFile
            {
                Version = f.Version, BlobPath = f.BlobPath, FileName = f.FileName, ContentType = f.ContentType, SizeBytes = f.SizeBytes,
                UploadedByName = f.UploadedByName,
            }).ToList(),
        };
        db.Add(copy);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(copy, ct);
    }

    private async Task<DocumentItem> ItemAsync(CaseDocument d, CancellationToken ct) =>
        Item(d, d.CopiedFromId is { } s ? (await SourceCasesAsync([d], ct)).GetValueOrDefault(s) : null);

    private async Task<(CaseDocument?, Error?)> EditableAsync(Guid documentId, CancellationToken ct)
    {
        var d = await db.Set<CaseDocument>().Include(x => x.Files).FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (d is null) return (null, Error.NotFound("Document not found."));
        var a = await db.Set<Assignment>().FirstAsync(x => x.Id == d.AssignmentId, ct);
        return ReadOnlyReason(a) is { } reason ? (null, Error.Forbidden(reason)) : (d, null);
    }

    private static void Apply(CaseDocument d, SaveDocumentRequest r)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        d.Type = r.Type;
        d.Title = T(r.Title);
        d.Availability = r.Availability;
        d.Number = T(r.Number);
        d.IssuedOn = r.IssuedOn;
        d.IssuedBy = T(r.IssuedBy);
        d.ValidUntil = r.ValidUntil;
        d.Notes = T(r.Notes);
    }

    // ---------------- files ----------------

    public async Task<Result<DownloadFile>> OpenFileAsync(Guid fileId, CancellationToken ct = default)
    {
        var f = await db.Set<DocumentFile>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId, ct);
        if (f is null) return Error.NotFound("File not found.");
        var stream = await files.OpenReadAsync(f.BlobPath, ct);
        return stream is null ? Error.NotFound("The file is missing.") : new DownloadFile(stream, f.ContentType, f.FileName);
    }

    public async Task<Result<DownloadFile>> ZipAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        var propertyType = a.PropertyId is { } pid
            ? await db.Set<Property>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).Where(p => p.Id == pid).Select(p => (PropertyType?)p.Type).FirstOrDefaultAsync(ct)
            : null;
        var docs = Ordered(await db.Set<CaseDocument>().AsNoTracking().Include(d => d.Files).Where(d => d.AssignmentId == assignmentId).ToListAsync(ct),
            DocumentRules.Expected(propertyType));
        var current = docs.Where(d => d.Files.Count > 0).Select(d => (Doc: d, File: d.Files.OrderByDescending(f => f.Version).First())).ToList();
        if (current.Count == 0) return Error.Validation("No document files have been uploaded for this case.");

        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var n = 0;
            foreach (var (doc, file) in current)
            {
                await using var src = await files.OpenReadAsync(file.BlobPath, ct);
                if (src is null) continue;
                n++;
                var label = doc.Title ?? DocumentRules.Label(doc.Type);
                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(ext)) ext = FileTypes.GetValueOrDefault(file.ContentType, "");
                var name = $"{n:00} {Safe(label)}{(doc.Number is null ? "" : " - " + Safe(doc.Number))}{ext}";
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                await using var dst = entry.Open();
                await src.CopyToAsync(dst, ct);
            }
        }
        zip.Position = 0;
        return new DownloadFile(zip, "application/zip", $"{a.Number.Replace('/', '-')} documents.zip");
    }

    /// <summary>Checklist documents first, in checklist order (as the report lists them), then the rest by type; oldest first within a type.</summary>
    private static List<CaseDocument> Ordered(List<CaseDocument> docs, IReadOnlyList<DocumentType> expected)
    {
        int Rank(DocumentType t) => expected.ToList().IndexOf(t) is var i and >= 0 ? i : 1000 + (int)t;
        return docs.OrderBy(d => Rank(d.Type)).ThenBy(d => d.CreatedAt).ToList();
    }

    private static string Safe(string s)
    {
        var clean = new string(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return clean.Length > 60 ? clean[..60] : clean;
    }
}
