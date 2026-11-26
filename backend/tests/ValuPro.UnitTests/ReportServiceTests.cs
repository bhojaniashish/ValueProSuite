using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Helpers;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Reports;
using ValuPro.Application.Settings;
using ValuPro.Application.Signatures;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Reports;
using ValuPro.Domain.Security;
using ValuPro.Domain.Tenancy;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Reports;
using ValuPro.Infrastructure.Signatures;

namespace ValuPro.UnitTests;

public class ReportServiceTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 26, 6, 0, 0, TimeSpan.Zero); }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? Name { get; set; } = "Neha Joshi";
        public HashSet<string> Grants { get; set; } = [.. Permissions.All];
        public bool IsAuthenticated => true;
        public string? Email { get; set; } = "neha@om.in";
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

    private sealed class Outbox : IEmailSender
    {
        public readonly List<EmailMessage> Sent = [];
        public bool Ready { get; set; } = true;
        public string? Fail { get; set; }
        public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(Ready);
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Fail is not null) throw new InvalidOperationException(Fail);
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private readonly Outbox _outbox = new();
    private readonly Me _me = new();
    private readonly MemoryFiles _files = new();
    private readonly FixedTenant _tenant = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _valuer, _bank, _flat;

    private IReportService Svc => _scope.ServiceProvider.GetRequiredService<IReportService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<TimeProvider, FixedClock>();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(_me);
        services.AddSingleton<IFileStorage>(_files);
        services.AddSingleton<IEmailSender>(_outbox);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<ISignatureService, SignatureService>();
        services.AddSingleton<IReportRenderer, DefaultReportTemplate>();
        services.AddScoped<IReportService, ReportService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var u = new AppUser { FullName = "Sneha Kulkarni", UserName = "sneha@x.in", RegistrationNo = "RV/2018/GJ/0412" };
        await _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(u);
        _valuer = u.Id;
        _me.UserId = Guid.NewGuid(); // reviewer / manager

        var bank = new Bank { Name = "HDFC Bank", ShortCode = "HDFC" };
        var flat = new Property { Code = "PR1", Type = PropertyType.ResidentialFlat, OwnerName = "Suresh", Address = "A-902 Shanti Heights", City = "Surat" };
        Db.AddRange(bank, flat, new Tenant { Id = _tenant.TenantId, Name = "Om Engineers", Slug = "om", Address = "Surat" });
        await Db.SaveChangesAsync();
        (_bank, _flat) = (bank.Id, flat.Id);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> CaseAsync(AssignmentStatus status = AssignmentStatus.InspectionDone, bool inspected = true, bool valued = true)
    {
        var a = new Assignment
        {
            Number = $"VAL/2026/{Random.Shared.Next(1000, 9999)}", Status = status, BankId = _bank, ApplicantName = "Suresh", Purpose = "Home loan",
            PropertySummary = "Flat, Vesu", PropertyId = _flat, ValuerId = _valuer, DueDate = new DateOnly(2026, 12, 1),
        };
        Db.Add(a);
        if (inspected)
        {
            var data = new InspectionData();
            data.Remarks.Text = "Well maintained flat.";
            Db.Add(new Inspection { AssignmentId = a.Id, StartedByName = "S", Status = InspectionStatus.Submitted, StartedAt = new DateTimeOffset(2026, 11, 24, 5, 0, 0, TimeSpan.Zero), Data = data });
        }
        if (valued)
        {
            var input = new ValuationInput { Method = ValuationMethod.Composite, AreaSqFt = 1080, CompositeRatePerSqFt = 7850 };
            var v = new Valuation { AssignmentId = a.Id, Input = input, ValuedByName = "S" };
            v.Apply(ValuationCalculator.Calculate(input));
            Db.Add(v);
        }
        await Db.SaveChangesAsync();
        return a.Id;
    }

    private async Task GiveValuerASignatureAsync()
    {
        var u = await Db.Users.SingleAsync(x => x.Id == _valuer);
        _files.Files["users/sig.png"] = Placeholders.Image(300, 100);
        u.SignatureBlobPath = "users/sig.png";
        await Db.SaveChangesAsync();
    }

    private async Task<AssignmentStatus> StatusAsync(Guid id) => (await Db.Set<Assignment>().AsNoTracking().SingleAsync(a => a.Id == id)).Status;

    [Fact]
    public async Task A_report_needs_the_inspection_and_the_valuation()
    {
        var id = await CaseAsync(valued: false);
        var d = (await Svc.GetAsync(id)).Value!;
        Assert.False(d.CanGenerate);
        Assert.False(d.Checks.Single(c => c.Key == "valuation").Ok);
        Assert.Equal(ErrorKind.Validation, (await Svc.GenerateDraftAsync(id)).Error!.Kind);

        var ready = (await Svc.GetAsync(await CaseAsync())).Value!;
        Assert.True(ready.CanGenerate);
        Assert.Equal("Well maintained flat.", ready.Fields.Opinion);               // starts from what the valuer wrote
        Assert.Equal(new DateOnly(2026, 11, 24), ready.Fields.ValuationDate);      // the inspection day
        Assert.Contains("24 Nov 2026", ready.Fields.Declaration);
        Assert.False(ready.Checks.Single(c => c.Key == "documents").Ok);           // reminders don't block
    }

    [Fact]
    public async Task The_first_draft_moves_the_case_and_every_draft_is_kept()
    {
        var id = await CaseAsync();
        var d1 = (await Svc.GenerateDraftAsync(id)).Value!;
        Assert.Equal(AssignmentStatus.DraftPrepared, await StatusAsync(id));
        var file = Assert.Single(d1.Files);
        Assert.Equal(ReportKind.Draft, file.Kind);
        Assert.Equal(84_78_000, file.FairMarketValue);

        var d2 = (await Svc.GenerateDraftAsync(id)).Value!;
        Assert.Equal([2, 1], d2.Files.Select(f => f.Number));
        Assert.Equal(AssignmentStatus.DraftPrepared, await StatusAsync(id));

        var pdf = (await Svc.OpenAsync(d2.Files[0].Id)).Value!;
        using var ms = new MemoryStream();
        await pdf.Content.CopyToAsync(ms);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(ms.ToArray(), 0, 4));
        Assert.EndsWith("-v2-draft.pdf", pdf.FileName);
        var events = await Db.Set<AssignmentEvent>().Where(e => e.AssignmentId == id).Select(e => e.Title).ToListAsync();
        Assert.Contains("Draft report prepared", events);
        Assert.Contains("Draft report version 2 generated", events);
    }

    [Fact]
    public async Task Report_details_are_saved_and_the_standard_declaration_follows_the_case()
    {
        var id = await CaseAsync();
        var std = (await Svc.GetAsync(id)).Value!.Fields.Declaration;

        var saved = (await Svc.SaveAsync(id, new SaveReportRequest(new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 24), "Rate as per recent sales.", std, "Surat", 2))).Value!;
        Assert.True(saved.Fields.DeclarationIsStandard);
        Assert.Equal(2, saved.Fields.PhotosPerPage);
        Assert.Equal("Rate as per recent sales.", saved.Fields.Opinion);

        var custom = (await Svc.SaveAsync(id, new SaveReportRequest(null, null, null, "I declare as per HDFC's format.", null, 4))).Value!;
        Assert.False(custom.Fields.DeclarationIsStandard);
        Assert.Equal("I declare as per HDFC's format.", custom.Fields.Declaration);

        Assert.Equal("photosPerPage", (await Svc.SaveAsync(id, new SaveReportRequest(null, null, null, null, null, 3))).Error!.Field);
        Assert.Equal("reportDate", (await Svc.SaveAsync(id, new SaveReportRequest(new DateOnly(2026, 12, 25), null, null, null, null, 4))).Error!.Field);
    }

    [Fact]
    public async Task Approving_signs_with_the_valuers_signature_and_submits()
    {
        var id = await CaseAsync(AssignmentStatus.DraftPrepared);
        Assert.Equal(ErrorKind.Forbidden, (await Svc.ApproveAsync(id)).Error!.Kind); // not in review yet

        var a = await Db.Set<Assignment>().SingleAsync(x => x.Id == id);
        a.Status = AssignmentStatus.QualityReview;
        await Db.SaveChangesAsync();
        var noSig = (await Svc.GetAsync(id)).Value!;
        Assert.False(noSig.CanApprove);
        Assert.Contains("Sneha Kulkarni needs to save a signature", noSig.ApproveBlockedReason);

        await GiveValuerASignatureAsync();
        _me.Grants.Remove(Permissions.Reports.Approve);
        Assert.False((await Svc.GetAsync(id)).Value!.CanApprove);
        _me.Grants.Add(Permissions.Reports.Approve);

        var done = (await Svc.ApproveAsync(id)).Value!;
        var final = done.Files.First();
        Assert.Equal(ReportKind.Final, final.Kind);
        Assert.Equal("Sneha Kulkarni", final.SignedByName);
        Assert.Equal("Neha Joshi", final.ApprovedByName);
        Assert.Equal(AssignmentStatus.Submitted, await StatusAsync(id));
        Assert.False(done.CanEdit);
        Assert.Equal(ErrorKind.Forbidden, (await Svc.SaveAsync(id, new SaveReportRequest(null, null, "late", null, null, 4))).Error!.Kind);
    }

    private async Task<Guid> SignedCaseAsync()
    {
        var id = await CaseAsync(AssignmentStatus.QualityReview);
        var branch = new BankBranch { BankId = _bank, Name = "Vesu", Email = "vesu.branch@hdfc.example", ContactPerson = "Mr. Shah" };
        Db.Add(branch);
        var a = await Db.Set<Assignment>().SingleAsync(x => x.Id == id);
        a.BranchId = branch.Id;
        a.BankReference = "HL-77";
        await Db.SaveChangesAsync();
        await GiveValuerASignatureAsync();
        Assert.True((await Svc.ApproveAsync(id)).IsSuccess);
        return id;
    }

    [Fact]
    public async Task Only_a_signed_report_can_be_emailed()
    {
        var id = await CaseAsync(AssignmentStatus.DraftPrepared);
        var d = (await Svc.GetAsync(id)).Value!;
        Assert.False(d.CanEmail);
        Assert.Contains("approve & sign", d.EmailBlockedReason);
        Assert.Equal(ErrorKind.Forbidden, (await Svc.EmailDraftAsync(id)).Error!.Kind);

        var signed = await SignedCaseAsync();
        _outbox.Ready = false;
        Assert.Contains("Email is not set up", (await Svc.GetAsync(signed)).Value!.EmailBlockedReason);
        _outbox.Ready = true;
        _me.Grants.Remove(Permissions.Reports.Manage);
        Assert.False((await Svc.GetAsync(signed)).Value!.CanEmail);
    }

    [Fact]
    public async Task Emailing_sends_the_signed_pdf_to_the_branch_and_logs_it()
    {
        var id = await SignedCaseAsync();
        var draft = (await Svc.EmailDraftAsync(id)).Value!;
        Assert.Equal(["vesu.branch@hdfc.example"], draft.To);
        Assert.Equal(["neha@om.in"], draft.Cc);
        Assert.Contains("Valuation report", draft.Subject);
        Assert.Contains("HL-77", draft.Subject);
        Assert.StartsWith("Dear Mr. Shah,", draft.Message);
        Assert.Contains("Fair market value: ₹84,78,000", draft.Message);
        Assert.False(draft.SentBefore);

        var done = (await Svc.EmailAsync(id, new SendReportEmailRequest(draft.FileId, draft.To, [.. draft.Cc, "VESU.branch@hdfc.example"], draft.Subject, "Dear Sir,\n\nReport attached <b>.\nThanks"))).Value!;
        var m = Assert.Single(_outbox.Sent);
        Assert.Equal("neha@om.in", m.ReplyTo);
        Assert.Equal(["neha@om.in"], m.Cc); // the To address is not copied again
        var pdf = Assert.Single(m.Attachments!);
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.EndsWith("-valuation-report-v1.pdf", pdf.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content, 0, 4));
        Assert.Contains("<p>Report attached &lt;b&gt;.<br>Thanks</p>", m.HtmlBody);

        var log = Assert.Single(done.Emails!);
        Assert.Equal(EmailStatus.Sent, log.Status);
        Assert.Equal("vesu.branch@hdfc.example", log.To);
        var ev = await Db.Set<AssignmentEvent>().Where(e => e.AssignmentId == id).OrderByDescending(e => e.At).ThenByDescending(e => e.Id).FirstAsync();
        Assert.Equal("Report emailed to the bank", ev.Title);
        Assert.Equal(AssignmentStatus.Submitted, ev.ToStatus);
        Assert.True((await Svc.EmailDraftAsync(id)).Value!.SentBefore);
    }

    [Fact]
    public async Task A_failed_email_is_logged_and_reported()
    {
        var id = await SignedCaseAsync();
        var draft = (await Svc.EmailDraftAsync(id)).Value!;
        _outbox.Fail = "Mailbox unavailable";
        var r = await Svc.EmailAsync(id, new SendReportEmailRequest(draft.FileId, draft.To, null, draft.Subject, draft.Message));
        Assert.Contains("Mailbox unavailable", r.Error!.Message);
        var log = Assert.Single((await Svc.GetAsync(id)).Value!.Emails!);
        Assert.Equal(EmailStatus.Failed, log.Status);
        Assert.False(await Db.Set<AssignmentEvent>().AnyAsync(e => e.AssignmentId == id && e.Title == "Report emailed to the bank"));

        var draftFile = new SendReportEmailRequest(Guid.NewGuid(), draft.To, null, "s", "m");
        Assert.Equal("fileId", (await Svc.EmailAsync(id, draftFile)).Error!.Field);
    }

    [Fact]
    public void The_email_request_is_validated()
    {
        var v = new SendReportEmailRequestValidator();
        Assert.False(v.Validate(new SendReportEmailRequest(Guid.NewGuid(), [], null, "s", "m")).IsValid);
        Assert.False(v.Validate(new SendReportEmailRequest(Guid.NewGuid(), ["not-an-email"], null, "s", "m")).IsValid);
        Assert.False(v.Validate(new SendReportEmailRequest(Guid.NewGuid(), ["a@b.in"], ["bad"], "s", "m")).IsValid);
        Assert.True(v.Validate(new SendReportEmailRequest(Guid.NewGuid(), ["a@b.in"], ["c@d.in"], "s", "m")).IsValid);
    }
}
