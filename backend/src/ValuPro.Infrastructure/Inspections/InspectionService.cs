using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Inspections;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Rates;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Inspections;

public sealed class InspectionService(AppDbContext db, ICurrentUser me, IFileStorage files, TimeProvider clock) : IInspectionService
{
    public const long MaxPhotoBytes = 8 * 1024 * 1024;
    public const long MaxThumbBytes = 512 * 1024;
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    // ---------------- case lookup ----------------

    private sealed class CaseRow
    {
        public required Assignment A { get; init; }
        public required Bank Bank { get; init; }
        public string? BranchName { get; init; }
        public required string ValuerName { get; init; }
        public Property? Property { get; init; }
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
               select new CaseRow { A = a, Bank = b, BranchName = br != null ? br.Name : null, ValuerName = u.FullName, Property = p };
    }

    // ---------------- list ----------------

    public async Task<IReadOnlyList<InspectionListItem>> ListAsync(InspectionQuery q, CancellationToken ct = default)
    {
        var rows = Cases();
        if (q.Mine && me.UserId is { } uid) rows = rows.Where(r => r.A.ValuerId == uid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(r => r.A.Number.Contains(s) || r.A.ApplicantName.Contains(s) || r.A.PropertySummary.Contains(s));
        }

        var inspections = db.Set<Inspection>();
        var joined = from r in rows
                     join i in inspections on r.A.Id equals i.AssignmentId into iss
                     from i in iss.DefaultIfEmpty()
                     select new { r.A, r.Bank.ShortCode, r.Bank.BrandColor, r.ValuerName, I = i };

        joined = q.Done
            ? joined.Where(x => x.I != null && x.I.Status == InspectionStatus.Submitted)
            : joined.Where(x => x.A.Status == AssignmentStatus.New || x.A.Status == AssignmentStatus.SiteVisitScheduled);

        var list = await (q.Done
                ? joined.OrderByDescending(x => x.I!.SubmittedAt)
                : joined.OrderBy(x => x.A.SiteVisitAt == null).ThenBy(x => x.A.SiteVisitAt).ThenBy(x => x.A.DueDate))
            .Take(200)
            .Select(x => new
            {
                x.A.Id, x.A.Number, x.A.Status, x.ShortCode, x.BrandColor, x.A.ApplicantName, x.A.PropertySummary,
                x.A.SiteVisitAt, x.A.DueDate, x.ValuerName,
                IStatus = x.I == null ? (InspectionStatus?)null : x.I.Status,
                Updated = x.I == null ? null : x.I.UpdatedAt ?? (DateTimeOffset?)x.I.CreatedAt,
            })
            .ToListAsync(ct);

        var ids = list.Select(x => x.Id).ToList();
        var photoCounts = await db.Set<AssignmentPhoto>().Where(p => ids.Contains(p.AssignmentId))
            .GroupBy(p => p.AssignmentId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return list.Select(x => new InspectionListItem(
            x.Id, x.Number, x.Status, x.ShortCode, x.BrandColor, x.ApplicantName, x.PropertySummary, x.SiteVisitAt, x.DueDate,
            x.ValuerName, x.IStatus, x.Updated, photoCounts.GetValueOrDefault(x.Id))).ToList();
    }

    // ---------------- detail ----------------

    public async Task<Result<InspectionDetail>> GetAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var row = await Cases().FirstOrDefaultAsync(r => r.A.Id == assignmentId, ct);
        if (row is null) return Error.NotFound("Assignment not found.");
        var inspection = await db.Set<Inspection>().AsNoTracking().FirstOrDefaultAsync(i => i.AssignmentId == assignmentId, ct);
        var photos = await PhotosAsync(assignmentId, ct);
        var structureTypes = await db.Set<DepreciationRule>().OrderBy(r => r.StructureType).Select(r => r.StructureType).ToListAsync(ct);
        return Detail(row, inspection, photos, structureTypes);
    }

    private InspectionDetail Detail(CaseRow row, Inspection? i, IReadOnlyList<PhotoItem> photos, IReadOnlyList<string> structureTypes)
    {
        var a = row.A;
        var p = row.Property;
        var landOnly = IsLandOnly(p);
        var questions = row.Bank.Questions.Select(q => new BankQuestionItem(q.Text, q.AnswerType, q.Options, q.Required)).ToList();
        var readOnly = ReadOnlyReason(a);
        var data = i?.Data ?? Prefill(row);
        var shell = i ?? new Inspection { AssignmentId = a.Id, StartedByName = "", Data = data };

        return new InspectionDetail(
            new InspectionCase(
                a.Id, a.Number, a.Status, row.Bank.Name, row.Bank.ShortCode, row.Bank.BrandColor, row.BranchName,
                a.ApplicantName, a.ApplicantPhone, a.PropertySummary, a.PropertyId, p?.Type,
                p is null ? null : string.Join(", ", new[] { p.Address, p.City, p.State, p.Pincode }.Where(s => !string.IsNullOrWhiteSpace(s))),
                p?.Latitude, p?.Longitude, p?.LandArea, p?.BuiltUpArea, a.SiteVisitAt, a.DueDate, row.ValuerName),
            i is not null,
            i?.Status ?? InspectionStatus.Draft,
            i?.StartedAt,
            i?.StartedByName,
            i?.Latitude,
            i?.Longitude,
            i?.AccuracyMetres,
            i?.LocatedAt,
            i?.DistanceFromPropertyMetres,
            i?.DistanceFromPropertyMetres > InspectionRules.FarFromPropertyMetres,
            i?.SubmittedAt,
            i?.SubmittedByName,
            i?.Version ?? 0,
            data,
            photos,
            questions,
            structureTypes,
            landOnly,
            Missing(shell, photos, landOnly, row.Bank),
            readOnly is null,
            readOnly);
    }

    /// <summary>A new inspection starts with what the office already knows: areas from the property record and the bank's questions.</summary>
    private static InspectionData Prefill(CaseRow row)
    {
        var d = new InspectionData();
        if (row.Property is { } p)
        {
            d.Measurements.LandAsPerDocuments = p.LandArea;
            d.Measurements.BuiltUpAsPerDocuments = p.BuiltUpArea;
            d.Occupancy.OccupiedBy = p.Usage switch
            {
                PropertyUsage.SelfOccupied => "Owner",
                PropertyUsage.Rented => "Tenant",
                PropertyUsage.Vacant => "Vacant",
                PropertyUsage.UnderConstruction => "UnderConstruction",
                _ => null,
            };
        }
        d.BankAnswers = row.Bank.Questions.Select(q => new BankAnswer { Question = q.Text }).ToList();
        return d;
    }

    private static bool IsLandOnly(Property? p) => p?.Type is PropertyType.OpenPlot or PropertyType.AgriculturalLand;

    private static IReadOnlyList<MissingItem> Missing(Inspection i, IReadOnlyList<PhotoItem> photos, bool landOnly, Bank bank) =>
        InspectionRules.Missing(i, photos.Select(p => p.Tag).ToHashSet(), landOnly, bank.Questions.Where(q => q.Required).Select(q => q.Text).ToList());

    // ---------------- rules ----------------

    /// <summary>Inspection managers who are the case's valuer, or who manage all assignments.</summary>
    private bool CanWork(Assignment a) =>
        me.HasPermission(Permissions.Inspections.Manage) && (me.UserId == a.ValuerId || me.HasPermission(Permissions.Assignments.Manage));

    private string? ReadOnlyReason(Assignment a)
    {
        if (!CanWork(a)) return "Only the assigned valuer or a manager can record this inspection.";
        return a.Status switch
        {
            AssignmentStatus.New or AssignmentStatus.SiteVisitScheduled => null,
            AssignmentStatus.Cancelled => "This assignment is cancelled.",
            _ => "The inspection has been submitted. To change it, send the case back to \"Site Visit Scheduled\".",
        };
    }

    // ---------------- save / submit ----------------

    public async Task<Result<SaveInspectionResult>> SaveAsync(Guid assignmentId, SaveInspectionRequest r, CancellationToken ct = default)
    {
        var row = await Cases().FirstOrDefaultAsync(x => x.A.Id == assignmentId, ct);
        if (row is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(row.A) is { } reason) return Error.Forbidden(reason);

        var i = await db.Set<Inspection>().FirstOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        var now = clock.GetUtcNow();
        if (i is null)
        {
            if (r.Version != 0) return Error.Conflict("This inspection is no longer on the server. Reload to start again.");
            i = new Inspection
            {
                AssignmentId = assignmentId,
                StartedAt = now,
                StartedByUserId = me.UserId,
                StartedByName = me.Name ?? "Unknown",
                Version = 0,
            };
            db.Add(i);
        }
        else if (i.Version != r.Version)
        {
            return Error.Conflict("This inspection was changed on another device. Reload to see the latest copy.");
        }

        i.Data = Clean(r.Data, row.Bank);
        i.Status = InspectionStatus.Draft;
        if (r.Location is { } fix)
        {
            i.Latitude = Math.Round(fix.Latitude, 6);
            i.Longitude = Math.Round(fix.Longitude, 6);
            i.AccuracyMetres = fix.AccuracyMetres;
            i.LocatedAt = fix.At is { } at && at <= now.AddMinutes(5) ? at : now;
        }
        i.DistanceFromPropertyMetres = i.Latitude is { } lat && i.Longitude is { } lng && row.Property is { Latitude: { } plat, Longitude: { } plng }
            ? InspectionRules.DistanceMetres(lat, lng, plat, plng)
            : null;
        i.Version++;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("This inspection was changed on another device. Reload to see the latest copy.");
        }
        catch (DbUpdateException) when (i.Version == 1)
        {
            // Two devices started the same inspection at once; the unique index kept only one.
            return Error.Conflict("This inspection was started on another device. Reload to continue from there.");
        }

        var photos = await PhotosAsync(assignmentId, ct);
        return new SaveInspectionResult(i.Version, Missing(i, photos, IsLandOnly(row.Property), row.Bank),
            i.DistanceFromPropertyMetres, i.DistanceFromPropertyMetres > InspectionRules.FarFromPropertyMetres, now);
    }

    public async Task<Result<InspectionDetail>> SubmitAsync(Guid assignmentId, SubmitInspectionRequest r, CancellationToken ct = default)
    {
        var row = await Cases().FirstOrDefaultAsync(x => x.A.Id == assignmentId, ct);
        if (row is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(row.A) is { } reason) return Error.Forbidden(reason);

        var i = await db.Set<Inspection>().FirstOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        if (i is null) return Error.Validation("Nothing has been recorded yet.");
        if (i.Version != r.Version) return Error.Conflict("This inspection was changed on another device. Reload to see the latest copy.");

        var photos = await PhotosAsync(assignmentId, ct);
        var missing = Missing(i, photos, IsLandOnly(row.Property), row.Bank);
        if (missing.Count > 0)
            return Error.Validation($"{missing.Count} item{(missing.Count == 1 ? " is" : "s are")} still needed: {missing[0].Message}");

        var now = clock.GetUtcNow();
        i.Status = InspectionStatus.Submitted;
        i.SubmittedAt = now;
        i.SubmittedByName = me.Name ?? "Unknown";
        i.Version++;

        // The case moves on to "Inspection Done". A visit made without scheduling records the visit time as well.
        var a = row.A;
        var from = a.Status;
        a.SiteVisitAt ??= i.StartedAt;
        a.Status = AssignmentStatus.InspectionDone;
        var where = i.DistanceFromPropertyMetres is { } m
            ? m > InspectionRules.FarFromPropertyMetres ? $" Location was {Distance(m)} from the saved property location." : null
            : null;
        db.Add(new AssignmentEvent
        {
            AssignmentId = a.Id,
            At = now,
            ByUserId = me.UserId,
            ByName = me.Name ?? "System",
            FromStatus = from,
            ToStatus = AssignmentStatus.InspectionDone,
            Title = "Site inspection completed",
            Note = $"{photos.Count} photo{(photos.Count == 1 ? "" : "s")}.{where}",
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("This inspection was changed on another device. Reload to see the latest copy.");
        }
        return await GetAsync(assignmentId, ct);
    }

    private static string Distance(int metres) => metres >= 1000 ? $"{metres / 1000.0:0.#} km" : $"{metres} m";

    /// <summary>Trims text, drops empty list entries, and keeps bank answers in the bank's question order.</summary>
    private static InspectionData Clean(InspectionData d, Bank bank)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static List<string> L(List<string>? xs) => (xs ?? []).Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(40).ToList()!;

        d.Visit.PersonMet = T(d.Visit.PersonMet);
        d.Visit.PersonPhone = T(d.Visit.PersonPhone);
        d.Visit.AddressAtSite = T(d.Visit.AddressAtSite);
        d.Visit.AccessNote = T(d.Visit.AccessNote);
        d.Visit.IdentifiedBy = L(d.Visit.IdentifiedBy);
        d.Location.Amenities = L(d.Location.Amenities);
        d.Location.Negatives = L(d.Location.Negatives);
        d.Location.Landmark = T(d.Location.Landmark);
        foreach (var b in new[] { d.Boundaries.North, d.Boundaries.South, d.Boundaries.East, d.Boundaries.West })
        {
            b.AsPerDocuments = T(b.AsPerDocuments);
            b.AtSite = T(b.AtSite);
        }
        d.Building.Amenities = L(d.Building.Amenities);
        d.Building.Defects = L(d.Building.Defects);
        d.Measurements.Floors = d.Measurements.Floors
            .Where(f => !string.IsNullOrWhiteSpace(f.Name) || f.AsPerDocuments is not null || f.Measured is not null).ToList();
        d.Measurements.Unit = d.Measurements.Unit == "SqM" ? "SqM" : "SqFt";

        var answers = d.BankAnswers.Where(x => !string.IsNullOrWhiteSpace(x.Question))
            .GroupBy(x => x.Question.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => T(g.Last().Answer), StringComparer.OrdinalIgnoreCase);
        d.BankAnswers = bank.Questions.Select(q => new BankAnswer { Question = q.Text, Answer = answers.GetValueOrDefault(q.Text) })
            .Concat(answers.Where(kv => kv.Value is not null && !bank.Questions.Any(q => string.Equals(q.Text, kv.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(kv => new BankAnswer { Question = kv.Key, Answer = kv.Value }))
            .ToList();
        d.Remarks.Text = T(d.Remarks.Text);
        return d;
    }

    // ---------------- photos ----------------

    private async Task<IReadOnlyList<PhotoItem>> PhotosAsync(Guid assignmentId, CancellationToken ct) =>
        await db.Set<AssignmentPhoto>().Where(p => p.AssignmentId == assignmentId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.TakenAt)
            .Select(p => new PhotoItem(p.Id, p.ClientKey, p.Tag, p.Caption, p.Width, p.Height, p.SizeBytes, p.Latitude, p.Longitude, p.TakenAt, p.TakenByName))
            .ToListAsync(ct);

    private static PhotoItem Item(AssignmentPhoto p) =>
        new(p.Id, p.ClientKey, p.Tag, p.Caption, p.Width, p.Height, p.SizeBytes, p.Latitude, p.Longitude, p.TakenAt, p.TakenByName);

    public async Task<Result<PhotoItem>> UploadPhotoAsync(
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
        var order = await db.Set<AssignmentPhoto>().Where(p => p.AssignmentId == assignmentId).Select(p => (int?)p.SortOrder).MaxAsync(ct) ?? 0;
        var photo = new AssignmentPhoto
        {
            AssignmentId = assignmentId,
            ClientKey = meta.ClientKey,
            Tag = meta.Tag,
            Caption = string.IsNullOrWhiteSpace(meta.Caption) ? null : meta.Caption.Trim()[..Math.Min(200, meta.Caption.Trim().Length)],
            BlobPath = blob,
            ThumbPath = thumbPath,
            ContentType = contentType,
            SizeBytes = size,
            Width = meta.Width,
            Height = meta.Height,
            Latitude = meta.Latitude is { } lat and >= -90 and <= 90 ? Math.Round(lat, 6) : null,
            Longitude = meta.Longitude is { } lng and >= -180 and <= 180 ? Math.Round(lng, 6) : null,
            TakenAt = meta.TakenAt is { } t && t <= now.AddMinutes(5) ? t : now,
            TakenByName = me.Name ?? "Unknown",
            SortOrder = order + 1,
        };
        db.Add(photo);
        await db.SaveChangesAsync(ct);
        return Item(photo);
    }

    public async Task<Result<PhotoItem>> UpdatePhotoAsync(Guid photoId, UpdatePhotoRequest r, CancellationToken ct = default)
    {
        var (photo, error) = await EditablePhotoAsync(photoId, ct);
        if (error is not null) return error;
        photo!.Tag = r.Tag;
        photo.Caption = string.IsNullOrWhiteSpace(r.Caption) ? null : r.Caption.Trim();
        await db.SaveChangesAsync(ct);
        return Item(photo);
    }

    public async Task<Result<bool>> DeletePhotoAsync(Guid photoId, CancellationToken ct = default)
    {
        var (photo, error) = await EditablePhotoAsync(photoId, ct);
        if (error is not null) return error;
        db.Remove(photo!); // soft delete; the file is kept with the case's history
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<(AssignmentPhoto?, Error?)> EditablePhotoAsync(Guid photoId, CancellationToken ct)
    {
        var photo = await db.Set<AssignmentPhoto>().FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return (null, Error.NotFound("Photo not found."));
        var a = await db.Set<Assignment>().FirstAsync(x => x.Id == photo.AssignmentId, ct);
        return ReadOnlyReason(a) is { } reason ? (null, Error.Forbidden(reason)) : (photo, null);
    }

    public async Task<Result<PhotoFile>> OpenPhotoAsync(Guid photoId, bool thumb, CancellationToken ct = default)
    {
        var photo = await db.Set<AssignmentPhoto>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return Error.NotFound("Photo not found.");
        var stream = await files.OpenReadAsync(thumb ? photo.ThumbPath : photo.BlobPath, ct);
        return stream is null
            ? Error.NotFound("The photo file is missing.")
            : new PhotoFile(stream, thumb ? "image/jpeg" : photo.ContentType);
    }
}
