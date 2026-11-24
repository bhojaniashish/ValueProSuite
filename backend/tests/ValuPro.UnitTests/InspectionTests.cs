using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Inspections;
using ValuPro.Application.Photos;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Rates;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Inspections;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Photos;

namespace ValuPro.UnitTests;

public class InspectionTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? Name { get; set; } = "Sneha Kulkarni";
        public HashSet<string> Grants { get; set; } = [.. Permissions.All];
        public bool IsAuthenticated => true;
        public string? Email => null;
        public bool HasPermission(string permission) => Grants.Contains(permission);
    }

    private sealed class MemoryFiles : IFileStorage
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public async Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{folder}/{fileName}";
            Files[path] = ms.ToArray();
            return path;
        }
        public Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(blobPath, out var b) ? new MemoryStream(b) : null);
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => Task.CompletedTask;
    }

    // Property at Vesu, Surat; the "at site" fix is ~20 m away, the "far" one ~1.6 km.
    private const decimal PropLat = 21.140000m, PropLng = 72.770000m;

    private readonly Me _me = new();
    private readonly MemoryFiles _files = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _valuer, _other, _bank, _flat, _plot;

    private IInspectionService Svc => _scope.ServiceProvider.GetRequiredService<IInspectionService>();
    private IPhotoService Photos => _scope.ServiceProvider.GetRequiredService<IPhotoService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITenantContext, FixedTenant>();
        services.AddSingleton<ICurrentUser>(_me);
        services.AddSingleton<IFileStorage>(_files);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IInspectionService, InspectionService>();
        services.AddScoped<IPhotoService, PhotoService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var users = _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        async Task<Guid> User(string n)
        {
            var u = new AppUser { FullName = n, UserName = n.Replace(" ", "") + "@x.in" };
            await users.CreateAsync(u);
            return u.Id;
        }
        _valuer = await User("Sneha Kulkarni");
        _other = await User("Priya Sharma");
        _me.UserId = _valuer;

        var bank = new Bank
        {
            Name = "HDFC Bank", ShortCode = "HDFC",
            Questions = [new BankQuestion { Text = "Is the property in a gated society?", AnswerType = AnswerType.YesNo, Required = true }],
        };
        var flat = new Property
        {
            Code = "PR0001", Type = PropertyType.ResidentialFlat, OwnerName = "Suresh Agarwal", Address = "A-902 Shanti Heights", City = "Surat",
            BuiltUpArea = 1450, Usage = PropertyUsage.SelfOccupied, Latitude = PropLat, Longitude = PropLng,
        };
        var plot = new Property { Code = "PR0002", Type = PropertyType.OpenPlot, OwnerName = "Kavita Reddy", Address = "Plot 14, Pal", LandArea = 2400 };
        Db.AddRange(bank, flat, plot, new DepreciationRule { StructureType = "RCC Framed Structure", TotalLifeYears = 60, SalvagePercent = 10 });
        await Db.SaveChangesAsync();
        (_bank, _flat, _plot) = (bank.Id, flat.Id, plot.Id);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> CaseAsync(Guid? property = null, Guid? valuer = null, AssignmentStatus status = AssignmentStatus.SiteVisitScheduled)
    {
        var a = new Assignment
        {
            Number = $"VAL/2026/{Random.Shared.Next(1000, 9999)}", Status = status, BankId = _bank, ApplicantName = "Suresh Agarwal",
            PropertySummary = "Flat, Vesu", PropertyId = property ?? _flat, ValuerId = valuer ?? _valuer, DueDate = new DateOnly(2026, 12, 31),
        };
        Db.Add(a);
        await Db.SaveChangesAsync();
        return a.Id;
    }

    private static InspectionData Complete(InspectionData d)
    {
        d.Visit = new VisitSection { PersonMet = "Suresh Agarwal", PersonRelation = "Owner", IdentifiedBy = ["Name board / door number"], AddressMatches = true, Access = "Full" };
        d.Location = new LocationSection { LocalityType = "Urban", Development = "Fully developed", RoadWidthFt = 30 };
        d.Boundaries = new BoundariesSection
        {
            North = new() { AtSite = "Road" }, South = new() { AtSite = "Flat 901" }, East = new() { AtSite = "Lift" }, West = new() { AtSite = "Open" },
            Encroachment = false,
        };
        d.Occupancy = new OccupancySection { OccupiedBy = "Owner" };
        d.Building = new BuildingSection { StructureType = "RCC Framed Structure", AgeYears = 6, Condition = "Good" };
        d.Measurements.CarpetMeasured = 1080;
        d.Approvals = new ApprovalsSection { PlanSeen = "Yes", Deviation = "None" };
        d.BankAnswers = [new BankAnswer { Question = "Is the property in a gated society?", Answer = "Yes" }];
        d.Remarks = new RemarksSection { Marketability = "Good" };
        return d;
    }

    private Task<Result<PhotoItem>> PhotoAsync(Guid id, PhotoTag tag, Guid? key = null, string type = "image/jpeg", decimal? lat = PropLat, decimal? lng = PropLng, PhotoSource source = PhotoSource.Site) =>
        Photos.UploadAsync(id, new PhotoUpload(key ?? Guid.NewGuid(), tag, null, lat, lng, null, 1600, 1200, source, true),
            new MemoryStream([1, 2, 3]), new MemoryStream([4]), type, 3);

    [Fact]
    public async Task A_new_inspection_is_prefilled_from_the_property_and_bank()
    {
        var d = (await Svc.GetAsync(await CaseAsync())).Value!;

        Assert.False(d.Started);
        Assert.True(d.CanEdit);
        Assert.Equal(1450, d.Data.Measurements.BuiltUpAsPerDocuments);
        Assert.Equal("Owner", d.Data.Occupancy.OccupiedBy);
        Assert.Equal("Is the property in a gated society?", d.Data.BankAnswers.Single().Question);
        Assert.Equal(["RCC Framed Structure"], d.StructureTypes);
        Assert.Contains(d.Missing, m => m.Section == "building");
        Assert.Contains(d.Missing, m => m.Message == "Add a photo of the front elevation.");

        var plot = (await Svc.GetAsync(await CaseAsync(_plot))).Value!;
        Assert.True(plot.LandOnly);
        Assert.DoesNotContain(plot.Missing, m => m.Section == "building");
        Assert.Equal(2400, plot.Data.Measurements.LandAsPerDocuments);
    }

    [Fact]
    public async Task Saves_are_versioned_and_location_is_checked_against_the_property()
    {
        var id = await CaseAsync();
        var data = (await Svc.GetAsync(id)).Value!.Data;

        var first = (await Svc.SaveAsync(id, new SaveInspectionRequest(0, data, new GpsFix(21.140150m, 72.770100m, 8, null)))).Value!;
        Assert.Equal(1, first.Version);
        Assert.InRange(first.DistanceFromPropertyMetres!.Value, 10, 30);
        Assert.False(first.FarFromProperty);

        var far = (await Svc.SaveAsync(id, new SaveInspectionRequest(1, data, new GpsFix(21.150000m, 72.780000m, 8, null)))).Value!;
        Assert.Equal(2, far.Version);
        Assert.True(far.FarFromProperty);

        // An older copy (another phone still on version 1) is refused instead of overwriting.
        Assert.Equal(ErrorKind.Conflict, (await Svc.SaveAsync(id, new SaveInspectionRequest(1, data, null))).Error!.Kind);
        Assert.Equal(ErrorKind.Conflict, (await Svc.SaveAsync(await CaseAsync(), new SaveInspectionRequest(3, data, null))).Error!.Kind);
    }

    [Fact]
    public async Task Only_the_assigned_valuer_or_a_manager_can_record()
    {
        var id = await CaseAsync(valuer: _other);
        _me.Grants.Remove(Permissions.Assignments.Manage);

        var d = (await Svc.GetAsync(id)).Value!;
        Assert.False(d.CanEdit);
        Assert.Equal(ErrorKind.Forbidden, (await Svc.SaveAsync(id, new SaveInspectionRequest(0, d.Data, null))).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await PhotoAsync(id, PhotoTag.Front)).Error!.Kind);

        _me.Grants.Add(Permissions.Assignments.Manage);
        Assert.True((await Svc.SaveAsync(id, new SaveInspectionRequest(0, d.Data, null))).IsSuccess);
    }

    [Fact]
    public async Task Submitting_needs_everything_then_locks_and_moves_the_case()
    {
        var id = await CaseAsync(status: AssignmentStatus.New);
        var data = Complete((await Svc.GetAsync(id)).Value!.Data);
        var saved = (await Svc.SaveAsync(id, new SaveInspectionRequest(0, data, new GpsFix(PropLat, PropLng, 5, null)))).Value!;
        Assert.Equal(["Add a photo of the front elevation.", "Add a photo of the approach road."], saved.Missing.Select(m => m.Message));

        var early = await Svc.SubmitAsync(id, new SubmitInspectionRequest(saved.Version));
        Assert.Equal(ErrorKind.Validation, early.Error!.Kind);

        await PhotoAsync(id, PhotoTag.Front);
        await PhotoAsync(id, PhotoTag.Road);
        var done = (await Svc.SubmitAsync(id, new SubmitInspectionRequest(saved.Version))).Value!;

        Assert.Equal(InspectionStatus.Submitted, done.Status);
        Assert.Equal(AssignmentStatus.InspectionDone, done.Case.Status);
        Assert.NotNull(done.Case.SiteVisitAt); // visited without a schedule: the visit time is recorded
        Assert.False(done.CanEdit);
        var e = await Db.Set<AssignmentEvent>().SingleAsync(x => x.AssignmentId == id);
        Assert.Equal("Site inspection completed", e.Title);
        Assert.Equal("2 photos.", e.Note);

        Assert.Equal(ErrorKind.Forbidden, (await Svc.SaveAsync(id, new SaveInspectionRequest(done.Version, done.Data, null))).Error!.Kind);

        // Sent back by the office: editable again, and saving turns it back into a draft.
        var a = await Db.Set<Assignment>().SingleAsync(x => x.Id == id);
        a.Status = AssignmentStatus.SiteVisitScheduled;
        await Db.SaveChangesAsync();
        Assert.True((await Svc.SaveAsync(id, new SaveInspectionRequest(done.Version, done.Data, null))).IsSuccess);
        Assert.Equal(InspectionStatus.Draft, (await Svc.GetAsync(id)).Value!.Status);
    }

    [Fact]
    public async Task Photo_uploads_are_checked_and_a_retry_is_not_saved_twice()
    {
        var id = await CaseAsync();
        var key = Guid.NewGuid();

        var first = (await PhotoAsync(id, PhotoTag.Front, key)).Value!;
        var retry = (await PhotoAsync(id, PhotoTag.Front, key)).Value!;
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await Db.Set<AssignmentPhoto>().CountAsync());
        Assert.Equal(2, _files.Files.Count); // image + thumbnail
        Assert.Equal(0, first.DistanceFromPropertyMetres);
        Assert.True(first.Stamped);

        Assert.Equal("file", (await PhotoAsync(id, PhotoTag.Front, type: "application/pdf")).Error!.Field);

        var changed = (await Photos.UpdateAsync(first.Id, new UpdatePhotoRequest(PhotoTag.Rear, " Back side ", false))).Value!;
        Assert.Equal(PhotoTag.Rear, changed.Tag);
        Assert.Equal("Back side", changed.Caption);
        Assert.False(changed.IncludeInReport);

        Assert.Equal("image/jpeg", (await Photos.OpenAsync(first.Id, thumb: true)).Value!.ContentType);
        Assert.True((await Photos.DeleteAsync(first.Id)).IsSuccess);
        Assert.Empty((await Svc.GetAsync(id)).Value!.Photos);
    }

    [Fact]
    public async Task Photos_can_be_reordered_and_kept_after_the_inspection_is_submitted()
    {
        var id = await CaseAsync();
        var a = (await PhotoAsync(id, PhotoTag.Road)).Value!;
        var b = (await PhotoAsync(id, PhotoTag.Front)).Value!;

        var ordered = (await Photos.ReorderAsync(id, new ReorderPhotosRequest([b.Id, a.Id]))).Value!;
        Assert.Equal([b.Id, a.Id], ordered.Select(p => p.Id));
        Assert.Equal(ErrorKind.Conflict, (await Photos.ReorderAsync(id, new ReorderPhotosRequest([b.Id]))).Error!.Kind);

        Db.Add(new Inspection { AssignmentId = id, StartedByName = "Test", Status = InspectionStatus.Submitted });
        await Db.SaveChangesAsync();
        var view = (await Photos.GetCaseAsync(id)).Value!;
        Assert.True(view.CanEdit);
        Assert.False(view.CanDelete);
        Assert.Equal(ErrorKind.Conflict, (await Photos.DeleteAsync(a.Id)).Error!.Kind); // left out of the report instead
        Assert.True((await Photos.UpdateAsync(a.Id, new UpdatePhotoRequest(PhotoTag.Road, null, false))).IsSuccess);
        // office uploads are still possible after the visit (e.g. photos sent by the field team)
        Assert.True((await PhotoAsync(id, PhotoTag.Interior, source: PhotoSource.Upload, lat: null, lng: null)).IsSuccess);

        var case_ = await Db.Set<Assignment>().SingleAsync(x => x.Id == id);
        case_.Status = AssignmentStatus.Submitted;
        await Db.SaveChangesAsync();
        Assert.Equal(ErrorKind.Forbidden, (await PhotoAsync(id, PhotoTag.Side)).Error!.Kind);
    }

    [Fact]
    public async Task Gallery_filters_and_flags_photos_that_need_a_look()
    {
        var id = await CaseAsync();
        await PhotoAsync(id, PhotoTag.Front);                                        // at the property
        await PhotoAsync(id, PhotoTag.Road, lat: 21.155m, lng: 72.770m);             // ~1.7 km away
        await PhotoAsync(id, PhotoTag.Interior, lat: null, lng: null);               // site photo without location
        await PhotoAsync(id, PhotoTag.Document, lat: null, lng: null, source: PhotoSource.Upload); // office upload: fine

        var all = await Photos.GalleryAsync(new PhotoQuery());
        Assert.Equal(4, all.Summary.Total);
        Assert.Equal(2, all.Summary.Attention);
        Assert.Equal(1, all.Summary.ByTag[PhotoTag.Road]);

        var attention = await Photos.GalleryAsync(new PhotoQuery(Attention: true));
        Assert.Equal([PhotoTag.Interior, PhotoTag.Road], attention.Page.Items.Select(i => i.Photo.Tag).Order());
        Assert.Single((await Photos.GalleryAsync(new PhotoQuery(Tag: PhotoTag.Front))).Page.Items);
        Assert.Empty((await Photos.GalleryAsync(new PhotoQuery(Search: "nobody"))).Page.Items);
    }

    [Fact]
    public async Task Zip_names_photos_in_report_order()
    {
        var id = await CaseAsync();
        var road = (await PhotoAsync(id, PhotoTag.Road)).Value!;
        var front = (await PhotoAsync(id, PhotoTag.Front)).Value!;
        await Photos.ReorderAsync(id, new ReorderPhotosRequest([front.Id, road.Id]));
        await Photos.UpdateAsync(road.Id, new UpdatePhotoRequest(PhotoTag.Road, "40 ft road", false));

        var all = (await Photos.ZipAsync(id, reportOnly: false)).Value!;
        using var zip = new System.IO.Compression.ZipArchive(all.Content);
        Assert.Equal(["01 Front elevation.jpg", "02 Approach road - 40 ft road.jpg", "photos.csv"], zip.Entries.Select(e => e.FullName));

        var report = (await Photos.ZipAsync(id, reportOnly: true)).Value!;
        using var reportZip = new System.IO.Compression.ZipArchive(report.Content);
        Assert.Equal(["01 Front elevation.jpg", "photos.csv"], reportZip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task Bank_answers_follow_the_bank_questions_and_text_is_trimmed()
    {
        var id = await CaseAsync();
        var data = new InspectionData
        {
            Visit = new VisitSection { PersonMet = "  Ramesh  ", IdentifiedBy = [" Neighbours confirmed ", "", "Neighbours confirmed"] },
            BankAnswers = [new BankAnswer { Question = "is the property in a GATED society?", Answer = " No " }, new BankAnswer { Question = "Old question", Answer = null }],
        };
        await Svc.SaveAsync(id, new SaveInspectionRequest(0, data, null));
        var d = (await Svc.GetAsync(id)).Value!.Data;

        Assert.Equal("Ramesh", d.Visit.PersonMet);
        Assert.Equal(["Neighbours confirmed"], d.Visit.IdentifiedBy);
        var answer = Assert.Single(d.BankAnswers);
        Assert.Equal("Is the property in a gated society?", answer.Question);
        Assert.Equal("No", answer.Answer);
    }

    [Fact]
    public void Distance_is_in_metres()
    {
        // Surat railway station to Vesu is about 10 km in a straight line.
        var m = InspectionRules.DistanceMetres(21.2050m, 72.8400m, 21.1400m, 72.7700m);
        Assert.InRange(m, 9500, 10500);
        Assert.Equal(0, InspectionRules.DistanceMetres(PropLat, PropLng, PropLat, PropLng));
    }
}
