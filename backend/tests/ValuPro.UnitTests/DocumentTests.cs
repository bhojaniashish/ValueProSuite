using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Documents;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Documents;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class DocumentTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    /// <summary>1 Dec 2026, 10:00 IST.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 1, 4, 30, 0, TimeSpan.Zero);
    }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? Name { get; set; } = "Office";
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
            Files[$"{folder}/{fileName}"] = ms.ToArray();
            return $"{folder}/{fileName}";
        }
        public Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(blobPath, out var b) ? new MemoryStream(b) : null);
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly DateOnly Today = new(2026, 12, 1);
    private readonly Me _me = new();
    private readonly MemoryFiles _files = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _valuer, _bank, _flat, _plot;

    private IDocumentService Svc => _scope.ServiceProvider.GetRequiredService<IDocumentService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<TimeProvider, FixedClock>();
        services.AddSingleton<ITenantContext, FixedTenant>();
        services.AddSingleton<ICurrentUser>(_me);
        services.AddSingleton<IFileStorage>(_files);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IDocumentService, DocumentService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var u = new AppUser { FullName = "Sneha Kulkarni", UserName = "sneha@x.in" };
        await _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(u);
        _valuer = u.Id;
        _me.UserId = Guid.NewGuid(); // office user, not the valuer

        var bank = new Bank { Name = "HDFC Bank", ShortCode = "HDFC" };
        var flat = new Property { Code = "PR0001", Type = PropertyType.ResidentialFlat, OwnerName = "Suresh", Address = "A-902" };
        var plot = new Property { Code = "PR0002", Type = PropertyType.OpenPlot, OwnerName = "Kavita", Address = "Plot 14" };
        Db.AddRange(bank, flat, plot);
        await Db.SaveChangesAsync();
        (_bank, _flat, _plot) = (bank.Id, flat.Id, plot.Id);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> CaseAsync(Guid? property = null, AssignmentStatus status = AssignmentStatus.SiteVisitScheduled)
    {
        var a = new Assignment
        {
            Number = $"VAL/2026/{Random.Shared.Next(1000, 9999)}", Status = status, BankId = _bank, ApplicantName = "Suresh",
            PropertySummary = "Flat", PropertyId = property ?? _flat, ValuerId = _valuer, DueDate = Today.AddDays(5),
        };
        Db.Add(a);
        await Db.SaveChangesAsync();
        return a.Id;
    }

    private static SaveDocumentRequest Req(DocumentType type, DocumentAvailability availability = DocumentAvailability.OriginalSeen,
        string? number = "SRT-4412/2019", DateOnly? validUntil = null, string? notes = null) =>
        new(type, null, availability, number, new DateOnly(2019, 6, 14), "Sub-registrar, Surat", validUntil, notes);

    private Task<Result<DocumentItem>> UploadAsync(Guid doc, string type = "application/pdf") =>
        Svc.UploadAsync(doc, new MemoryStream([1, 2, 3]), "deed.pdf", type, 3);

    [Fact]
    public async Task The_checklist_follows_the_property_type()
    {
        var flat = (await Svc.GetCaseAsync(await CaseAsync())).Value!;
        Assert.Contains(flat.Checklist, c => c.Type == DocumentType.ShareCertificate);
        Assert.DoesNotContain(flat.Checklist, c => c.Type == DocumentType.SevenTwelve);

        var plot = (await Svc.GetCaseAsync(await CaseAsync(_plot))).Value!;
        Assert.Equal([DocumentType.SaleDeed, DocumentType.IndexII, DocumentType.SevenTwelve, DocumentType.NaOrder], plot.Checklist.Select(c => c.Type));
    }

    [Fact]
    public async Task Recorded_and_not_provided_documents_tick_the_checklist()
    {
        var id = await CaseAsync();
        await Svc.CreateAsync(id, Req(DocumentType.Noc));
        await Svc.CreateAsync(id, Req(DocumentType.OccupancyCertificate, DocumentAvailability.NotProvided, number: null, notes: "Not yet issued by SMC"));
        await Svc.CreateAsync(id, Req(DocumentType.SaleDeed));

        var c = (await Svc.GetCaseAsync(id)).Value!;
        // listed in checklist order (as the report will), then the rest
        Assert.Equal([DocumentType.SaleDeed, DocumentType.OccupancyCertificate, DocumentType.Noc], c.Documents.Select(d => d.Type));
        Assert.True(c.Checklist.Single(x => x.Type == DocumentType.SaleDeed).Recorded);
        var oc = c.Checklist.Single(x => x.Type == DocumentType.OccupancyCertificate);
        Assert.False(oc.Recorded);
        Assert.True(oc.NotProvided);
    }

    [Fact]
    public void Not_provided_needs_a_reason_and_dates_must_make_sense()
    {
        var v = new SaveDocumentRequestValidator(new FixedClock());
        Assert.Contains(v.Validate(Req(DocumentType.TaxBill, DocumentAvailability.NotProvided)).Errors, e => e.PropertyName == "Notes");
        Assert.Contains(v.Validate(Req(DocumentType.Other)).Errors, e => e.PropertyName == "Title");
        Assert.Contains(v.Validate(Req(DocumentType.Noc) with { IssuedOn = Today.AddDays(1) }).Errors, e => e.PropertyName == "IssuedOn");
        Assert.Contains(v.Validate(Req(DocumentType.Noc, validUntil: new DateOnly(2018, 1, 1))).Errors, e => e.PropertyName == "ValidUntil");
        Assert.True(v.Validate(Req(DocumentType.SaleDeed)).IsValid);
    }

    [Fact]
    public async Task Uploads_add_versions_and_keep_the_old_ones()
    {
        var id = await CaseAsync();
        var doc = (await Svc.CreateAsync(id, Req(DocumentType.ApprovedPlan, DocumentAvailability.NotProvided, notes: "Owner bringing tomorrow"))).Value!;

        Assert.Equal("file", (await UploadAsync(doc.Id, "application/zip")).Error!.Field);
        var v1 = (await UploadAsync(doc.Id)).Value!;
        Assert.Equal(DocumentAvailability.CopyOnly, v1.Availability); // a copy now exists
        var v2 = (await UploadAsync(doc.Id, "image/jpeg")).Value!;

        Assert.Equal([2, 1], v2.Files.Select(f => f.Version));
        Assert.Equal(2, _files.Files.Count);
        var opened = (await Svc.OpenFileAsync(v2.Files[1].Id)).Value!;
        Assert.Equal("application/pdf", opened.ContentType);
    }

    [Fact]
    public async Task Documents_from_an_earlier_case_of_the_same_property_can_be_reused()
    {
        var first = await CaseAsync(status: AssignmentStatus.Submitted);
        var deed = (await Svc.CreateAsync(await CaseAsync(), Req(DocumentType.SaleDeed))).Value!;
        // the earlier case moved on; its documents are still there for the next case
        var deedCase = await Db.Set<CaseDocument>().SingleAsync(d => d.Id == deed.Id);
        deedCase.AssignmentId = first;
        await Db.SaveChangesAsync();
        await UploadOnSubmittedAsync(deed.Id);

        var next = await CaseAsync();
        var view = (await Svc.GetCaseAsync(next)).Value!;
        var offered = Assert.Single(view.Earlier);
        Assert.Equal(deed.Id, offered.Document.Id);

        var reused = (await Svc.ReuseAsync(next, deed.Id)).Value!;
        Assert.Equal("SRT-4412/2019", reused.Number);
        Assert.Single(reused.Files);
        Assert.NotNull(reused.CopiedFromCase);
        Assert.Empty((await Svc.GetCaseAsync(next)).Value!.Earlier);
        Assert.Equal(ErrorKind.Conflict, (await Svc.ReuseAsync(next, deed.Id)).Error!.Kind);

        // not across properties
        Assert.Equal(ErrorKind.Validation, (await Svc.ReuseAsync(await CaseAsync(_plot), deed.Id)).Error!.Kind);
    }

    /// <summary>Adds a file directly (the earlier case is locked for edits).</summary>
    private async Task UploadOnSubmittedAsync(Guid docId)
    {
        Db.Add(new DocumentFile { DocumentId = docId, Version = 1, BlobPath = "x/deed.pdf", FileName = "deed.pdf", ContentType = "application/pdf", SizeBytes = 3, UploadedByName = "Office" });
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Expiry_is_flagged_and_listed()
    {
        var id = await CaseAsync();
        await Svc.CreateAsync(id, Req(DocumentType.Noc, validUntil: Today.AddDays(10)));
        await Svc.CreateAsync(id, Req(DocumentType.EncumbranceCertificate, validUntil: Today.AddDays(-1)));
        await Svc.CreateAsync(id, Req(DocumentType.TaxBill, validUntil: Today.AddDays(200)));

        var all = await Svc.ListAsync(new DocumentQuery());
        Assert.Equal(3, all.Summary.Total);
        Assert.Equal(1, all.Summary.Expiring);
        Assert.Equal(1, all.Summary.Expired);
        Assert.Equal("Expiring", (await Svc.ListAsync(new DocumentQuery(Expiry: "Expiring"))).Page.Items.Single().Document.ExpiryState);
        Assert.Equal(DocumentType.EncumbranceCertificate, (await Svc.ListAsync(new DocumentQuery(Expiry: "Expired"))).Page.Items.Single().Document.Type);
        Assert.Single((await Svc.ListAsync(new DocumentQuery(Search: "SRT-4412", Type: DocumentType.TaxBill))).Page.Items);
    }

    [Fact]
    public async Task The_office_or_the_case_valuer_can_edit_until_the_report_is_submitted()
    {
        var id = await CaseAsync();
        _me.Grants = [Permissions.Assignments.View, Permissions.Inspections.Manage]; // a valuer, but not this case's
        Assert.Equal(ErrorKind.Forbidden, (await Svc.CreateAsync(id, Req(DocumentType.SaleDeed))).Error!.Kind);

        _me.UserId = _valuer; // the case's valuer
        var doc = (await Svc.CreateAsync(id, Req(DocumentType.SaleDeed))).Value!;

        var a = await Db.Set<Assignment>().SingleAsync(x => x.Id == id);
        a.Status = AssignmentStatus.Submitted;
        await Db.SaveChangesAsync();
        Assert.Equal(ErrorKind.Forbidden, (await Svc.DeleteAsync(doc.Id)).Error!.Kind);
        Assert.False((await Svc.GetCaseAsync(id)).Value!.CanEdit);
    }

    [Fact]
    public async Task Zip_has_the_current_file_of_each_document()
    {
        var id = await CaseAsync();
        Assert.Equal(ErrorKind.Validation, (await Svc.ZipAsync(id)).Error!.Kind);
        var deed = (await Svc.CreateAsync(id, Req(DocumentType.SaleDeed))).Value!;
        await UploadAsync(deed.Id);
        await UploadAsync(deed.Id, "image/jpeg");
        await Svc.CreateAsync(id, Req(DocumentType.TaxBill)); // no file: not in the ZIP

        using var zip = new System.IO.Compression.ZipArchive((await Svc.ZipAsync(id)).Value!.Content);
        Assert.Equal(["01 Sale deed - SRT-44122019.pdf"], zip.Entries.Select(e => e.FullName));
    }
}
