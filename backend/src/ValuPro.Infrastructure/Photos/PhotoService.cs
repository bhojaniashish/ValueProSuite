using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Photos;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Photos;

public sealed class PhotoService(AppDbContext db, ICurrentUser me, IFileStorage files, TimeProvider clock) : IPhotoService
{
    public const long MaxPhotoBytes = 8 * 1024 * 1024;
    public const long MaxThumbBytes = 512 * 1024;
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);

    public static PhotoItem Item(AssignmentPhoto p) => new(
        p.Id, p.AssignmentId, p.ClientKey, p.Tag, p.Caption, p.Width, p.Height, p.SizeBytes, p.Latitude, p.Longitude,
        p.DistanceFromPropertyMetres, p.TakenAt, p.TakenByName, p.Source, p.Stamped, p.IncludeInReport, p.SortOrder);

    /// <summary>A site photo with no location, or one taken far from the property, is worth a second look.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<AssignmentPhoto, bool>> NeedsAttention =
        p => (p.Source == PhotoSource.Site && p.Latitude == null) || p.DistanceFromPropertyMetres > InspectionRules.FarFromPropertyMetres;

    // ---------------- rules ----------------

    private bool CanWork(Assignment a) =>
        me.HasPermission(Permissions.Inspections.Manage)
        && (me.UserId == a.ValuerId || me.UserId == a.ReviewerId || me.HasPermission(Permissions.Assignments.Manage));

    /// <summary>Photos can be managed until the report goes to the bank.</summary>
    private string? ReadOnlyReason(Assignment a)
    {
        if (!CanWork(a)) return "Only the case's valuer, its reviewer or a manager can change its photos.";
        return a.Status switch
        {
            AssignmentStatus.Cancelled => "This assignment is cancelled.",
            AssignmentStatus.Submitted or AssignmentStatus.InvoiceGenerated => "The report has been submitted, so its photos are locked.",
            _ => null,
        };
    }

    private Task<bool> InspectionSubmittedAsync(Guid assignmentId, CancellationToken ct) =>
        db.Set<Inspection>().AnyAsync(i => i.AssignmentId == assignmentId && i.Status == InspectionStatus.Submitted, ct);

    // ---------------- gallery ----------------

    public async Task<GalleryPage> GalleryAsync(PhotoQuery q, CancellationToken ct = default)
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var rows = from p in db.Set<AssignmentPhoto>()
                   join a in db.Set<Assignment>() on p.AssignmentId equals a.Id
                   join b in banks on a.BankId equals b.Id
                   select new { P = p, A = a, b.ShortCode, b.BrandColor };

        if (q.AssignmentId is { } aid) rows = rows.Where(x => x.A.Id == aid);
        if (q.Mine && me.UserId is { } uid) rows = rows.Where(x => x.A.ValuerId == uid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(x => x.A.Number.Contains(s) || x.A.ApplicantName.Contains(s) || x.A.PropertySummary.Contains(s) || (x.P.Caption != null && x.P.Caption.Contains(s)));
        }
        if (q.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), India);
            rows = rows.Where(x => x.P.TakenAt >= start);
        }
        if (q.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), India);
            rows = rows.Where(x => x.P.TakenAt < end);
        }

        // Counts follow every filter except the tag and attention chips themselves, so the chips show what each would give.
        var byTag = await rows.GroupBy(x => x.P.Tag).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var attention = await rows.Select(x => x.P).CountAsync(NeedsAttention, ct);

        if (q.Tag is { } tag) rows = rows.Where(x => x.P.Tag == tag);
        if (q.Attention)
        {
            var ids = db.Set<AssignmentPhoto>().Where(NeedsAttention).Select(p => p.Id);
            rows = rows.Where(x => ids.Contains(x.P.Id));
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(x => x.P.TakenAt).ThenBy(x => x.P.SortOrder)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .ToListAsync(ct);

        var items = page.Select(x => new GalleryItem(Item(x.P), x.A.Number, x.ShortCode, x.BrandColor, x.A.ApplicantName)).ToList();
        return new GalleryPage(new PagedResult<GalleryItem>(items, total, q.Page, q.PageSize), new GallerySummary(byTag.Values.Sum(), attention, byTag));
    }

    // ---------------- one case ----------------

    public async Task<IReadOnlyList<PhotoItem>> ListAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var list = await db.Set<AssignmentPhoto>().Where(p => p.AssignmentId == assignmentId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.TakenAt).ToListAsync(ct);
        return list.Select(Item).ToList();
    }

    public async Task<Result<CasePhotos>> GetCaseAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var properties = db.Set<Property>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var row = await (from a in db.Set<Assignment>()
                         join b in banks on a.BankId equals b.Id
                         join p in properties on a.PropertyId equals (Guid?)p.Id into ps
                         from p in ps.DefaultIfEmpty()
                         where a.Id == assignmentId
                         select new { A = a, b.ShortCode, b.BrandColor, Lat = p != null ? p.Latitude : null, Lng = p != null ? p.Longitude : null })
            .FirstOrDefaultAsync(ct);
        if (row is null) return Error.NotFound("Assignment not found.");

        var submitted = await InspectionSubmittedAsync(assignmentId, ct);
        var reason = ReadOnlyReason(row.A);
        return new CasePhotos(
            row.A.Id, row.A.Number, row.A.Status, row.ShortCode, row.BrandColor, row.A.ApplicantName, row.A.PropertySummary, row.Lat, row.Lng,
            submitted, await ListAsync(assignmentId, ct), reason is null, reason is null && !submitted, reason);
    }

    // ---------------- changes ----------------

    public async Task<Result<PhotoItem>> UploadAsync(
        Guid assignmentId, PhotoUpload meta, Stream image, Stream thumb, string contentType, long size, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(a) is { } reason) return Error.Forbidden(reason);

        var existing = await db.Set<AssignmentPhoto>().FirstOrDefaultAsync(p => p.AssignmentId == assignmentId && p.ClientKey == meta.ClientKey, ct);
        if (existing is not null) return Item(existing); // the phone retried an upload that already arrived

        if (!ImageTypes.Contains(contentType)) return Error.Validation("Photos must be JPEG, PNG or WebP.", "file");
        if (size <= 0 || size > MaxPhotoBytes) return Error.Validation("Photos can be up to 8 MB.", "file");
        if (!Enum.IsDefined(meta.Tag)) return Error.Validation("Choose what the photo shows.", "tag");

        var ext = contentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var folder = $"assignments/{assignmentId:N}/photos";
        var blob = await files.UploadAsync(folder, $"{meta.ClientKey:N}{ext}", image, contentType, ct);
        var thumbPath = await files.UploadAsync(folder, $"{meta.ClientKey:N}-thumb.jpg", thumb, "image/jpeg", ct);

        var now = clock.GetUtcNow();
        var lat = meta.Latitude is { } la and >= -90 and <= 90 ? Math.Round(la, 6) : (decimal?)null;
        var lng = meta.Longitude is { } lo and >= -180 and <= 180 ? Math.Round(lo, 6) : (decimal?)null;
        int? distance = null;
        if (lat is not null && lng is not null && a.PropertyId is { } pid)
        {
            var prop = await db.Set<Property>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
                .Where(p => p.Id == pid).Select(p => new { p.Latitude, p.Longitude }).FirstOrDefaultAsync(ct);
            if (prop is { Latitude: { } plat, Longitude: { } plng }) distance = InspectionRules.DistanceMetres(lat.Value, lng.Value, plat, plng);
        }

        var order = await db.Set<AssignmentPhoto>().Where(p => p.AssignmentId == assignmentId).Select(p => (int?)p.SortOrder).MaxAsync(ct) ?? 0;
        var caption = string.IsNullOrWhiteSpace(meta.Caption) ? null : meta.Caption.Trim();
        var photo = new AssignmentPhoto
        {
            AssignmentId = assignmentId,
            ClientKey = meta.ClientKey,
            Tag = meta.Tag,
            Caption = caption is { Length: > 200 } ? caption[..200] : caption,
            BlobPath = blob,
            ThumbPath = thumbPath,
            ContentType = contentType,
            SizeBytes = size,
            Width = meta.Width,
            Height = meta.Height,
            Latitude = lat,
            Longitude = lng,
            DistanceFromPropertyMetres = distance,
            TakenAt = meta.TakenAt is { } t && t <= now.AddMinutes(5) ? t : now,
            TakenByName = me.Name ?? "Unknown",
            Source = meta.Source,
            Stamped = meta.Stamped && meta.Source == PhotoSource.Site,
            SortOrder = order + 1,
        };
        db.Add(photo);
        await db.SaveChangesAsync(ct);
        return Item(photo);
    }

    public async Task<Result<PhotoItem>> UpdateAsync(Guid photoId, UpdatePhotoRequest r, CancellationToken ct = default)
    {
        var (photo, error) = await EditableAsync(photoId, ct);
        if (error is not null) return error;
        photo!.Tag = r.Tag;
        photo.Caption = string.IsNullOrWhiteSpace(r.Caption) ? null : r.Caption.Trim();
        photo.IncludeInReport = r.IncludeInReport;
        await db.SaveChangesAsync(ct);
        return Item(photo);
    }

    public async Task<Result<IReadOnlyList<PhotoItem>>> ReorderAsync(Guid assignmentId, ReorderPhotosRequest r, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(a) is { } reason) return Error.Forbidden(reason);

        var photos = await db.Set<AssignmentPhoto>().Where(p => p.AssignmentId == assignmentId).ToListAsync(ct);
        if (photos.Count != r.PhotoIds.Count || photos.Any(p => !r.PhotoIds.Contains(p.Id)))
            return Error.Conflict("The photos changed while you were reordering. Reload and try again.");

        var position = r.PhotoIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        foreach (var p in photos) p.SortOrder = position[p.Id];
        await db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<PhotoItem>>.Ok(photos.OrderBy(p => p.SortOrder).Select(Item).ToList());
    }

    public async Task<Result<bool>> DeleteAsync(Guid photoId, CancellationToken ct = default)
    {
        var (photo, error) = await EditableAsync(photoId, ct);
        if (error is not null) return error;
        if (await InspectionSubmittedAsync(photo!.AssignmentId, ct))
            return Error.Conflict("Photos from a submitted inspection are kept as a record. Leave it out of the report instead.");
        db.Remove(photo); // soft delete; the file is kept with the case's history
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<(AssignmentPhoto?, Error?)> EditableAsync(Guid photoId, CancellationToken ct)
    {
        var photo = await db.Set<AssignmentPhoto>().FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return (null, Error.NotFound("Photo not found."));
        var a = await db.Set<Assignment>().FirstAsync(x => x.Id == photo.AssignmentId, ct);
        return ReadOnlyReason(a) is { } reason ? (null, Error.Forbidden(reason)) : (photo, null);
    }

    // ---------------- files ----------------

    public async Task<Result<PhotoFile>> OpenAsync(Guid photoId, bool thumb, CancellationToken ct = default)
    {
        var photo = await db.Set<AssignmentPhoto>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return Error.NotFound("Photo not found.");
        var stream = await files.OpenReadAsync(thumb ? photo.ThumbPath : photo.BlobPath, ct);
        return stream is null
            ? Error.NotFound("The photo file is missing.")
            : new PhotoFile(stream, thumb ? "image/jpeg" : photo.ContentType, Path.GetFileName(photo.BlobPath));
    }

    public async Task<Result<PhotoFile>> ZipAsync(Guid assignmentId, bool reportOnly, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        var photos = (await ListAsync(assignmentId, ct)).Where(p => !reportOnly || p.IncludeInReport).ToList();
        if (photos.Count == 0) return Error.Validation(reportOnly ? "No photos are marked for the report." : "This case has no photos yet.");

        var paths = await db.Set<AssignmentPhoto>().Where(p => p.AssignmentId == assignmentId)
            .ToDictionaryAsync(p => p.Id, p => (p.BlobPath, p.ContentType), ct);

        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            var csv = new StringBuilder("No,Photo,Caption,Taken (IST),Latitude,Longitude,Metres from property,Taken by,In report\r\n");
            var n = 0;
            foreach (var p in photos)
            {
                n++;
                var (blob, type) = paths[p.Id];
                var ext = type switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
                var name = $"{n:00} {InspectionRules.Label(p.Tag)}{(p.Caption is null ? "" : " - " + Safe(p.Caption))}{ext}";
                await using var src = await files.OpenReadAsync(blob, ct);
                if (src is null) continue;
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression); // JPEGs don't shrink further
                await using (var dst = entry.Open()) await src.CopyToAsync(dst, ct);

                csv.Append(CultureInfo.InvariantCulture, $"{n},{Csv(InspectionRules.Label(p.Tag))},{Csv(p.Caption)},{p.TakenAt.ToOffset(India):dd-MM-yyyy HH:mm},")
                   .Append(CultureInfo.InvariantCulture, $"{p.Latitude},{p.Longitude},{p.DistanceFromPropertyMetres},{Csv(p.TakenByName)},{(p.IncludeInReport ? "Yes" : "No")}\r\n");
            }
            var list = archive.CreateEntry("photos.csv");
            await using (var w = new StreamWriter(list.Open(), new UTF8Encoding(true))) await w.WriteAsync(csv.ToString());
        }
        zip.Position = 0;
        return new PhotoFile(zip, "application/zip", $"{a.Number.Replace('/', '-')} photos.zip");
    }

    private static string Safe(string s)
    {
        var clean = new string(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return clean.Length > 60 ? clean[..60] : clean;
    }

    private static string Csv(string? s) => s is null ? "" : s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
