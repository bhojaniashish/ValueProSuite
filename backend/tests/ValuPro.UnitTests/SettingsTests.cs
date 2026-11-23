using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Settings;
using ValuPro.Domain.Settings;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Settings;

namespace ValuPro.UnitTests;

public class SettingsTests
{
    private sealed class FixedTenant(Guid id) : ITenantContext { public Guid TenantId => id; }

    private sealed class NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Email => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class MemoryFiles(Guid tenantId) : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public async Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"tenants/{tenantId}/{folder}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
            Files[path] = ms.ToArray();
            return path;
        }

        public Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(blobPath, out var b) ? new MemoryStream(b) : null);
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) { Files.Remove(blobPath); return Task.CompletedTask; }
    }

    private sealed class FailingEmail : IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
            throw new InvalidOperationException("Connection refused");
    }

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly AppDbContext _db;
    private readonly MemoryFiles _files;
    private readonly SettingsService _service;

    public SettingsTests()
    {
        var tenant = new FixedTenant(_tenantId);
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditingInterceptor(new NoUser(), tenant, TimeProvider.System))
            .Options, tenant);
        _db.Tenants.Add(new Tenant { Id = _tenantId, Name = "Om Engineers", Slug = "om" });
        _db.SaveChanges();
        _files = new MemoryFiles(_tenantId);
        _service = new SettingsService(_db, tenant, _files, new SecretProtector(new EphemeralDataProtectionProvider()),
            new FailingEmail(), NullLogger<SettingsService>.Instance);
    }

    private static UpdateCommunicationRequest Comm(string? password, string? apiKey = null) =>
        new("smtp.example.in", 587, true, "mailer", password, "noreply@example.in", "Om Engineers", SmsProvider.Msg91, "OMENGG", apiKey);

    [Fact]
    public async Task Secrets_are_encrypted_never_returned_and_kept_when_omitted()
    {
        var saved = (await _service.UpdateCommunicationAsync(Comm("s3cret-pass", "key-123"))).Value!;
        Assert.True(saved.HasSmtpPassword);
        Assert.True(saved.HasSmsApiKey);

        var row = await _db.Set<FirmSettings>().SingleAsync();
        Assert.DoesNotContain("s3cret-pass", row.SmtpPasswordEncrypted);

        // null keeps, "" clears
        var kept = (await _service.UpdateCommunicationAsync(Comm(null, ""))).Value!;
        Assert.True(kept.HasSmtpPassword);
        Assert.False(kept.HasSmsApiKey);
    }

    [Fact]
    public async Task Failed_test_email_is_reported_not_thrown()
    {
        var result = await _service.SendTestEmailAsync("admin@x.in");
        Assert.False(result.IsSuccess);
        Assert.Contains("Connection refused", result.Error!.Message);
    }

    [Fact]
    public async Task Notifications_keep_catalogue_order_and_unsent_events()
    {
        var result = await _service.UpdateNotificationsAsync(new UpdateNotificationsRequest(
            [new NotificationPreferenceInput("weekly.summary", Email: true, Sms: false, InApp: false)]));

        var list = result.Value!;
        Assert.Equal(NotificationEvents.All.Select(e => e.Key), list.Select(n => n.Event));
        Assert.True(list.Single(n => n.Event == "weekly.summary").Email);
        Assert.True(list.Single(n => n.Event == "assignment.received").Sms); // untouched default
    }

    [Fact]
    public async Task Logo_upload_replaces_old_file_and_bumps_version()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        var first = (await _service.UploadImageAsync(BrandingImage.Logo, new MemoryStream(png), "a.png", "image/png", png.Length)).Value!;
        var second = (await _service.UploadImageAsync(BrandingImage.Logo, new MemoryStream(png), "b.png", "image/png", png.Length)).Value!;

        Assert.Single(_files.Files);
        Assert.NotEqual(first.LogoUrl, second.LogoUrl);
        Assert.NotNull(await _service.OpenImageAsync(BrandingImage.Logo));
    }

    [Theory]
    [InlineData("image/png", "not really a png")]
    [InlineData("image/svg+xml", "<svg><script>alert(1)</script></svg>")]
    [InlineData("image/svg+xml", "<svg onload=\"alert(1)\"></svg>")]
    [InlineData("image/gif", "GIF89a")]
    public async Task Unsafe_or_mislabelled_images_are_refused(string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var result = await _service.UploadImageAsync(BrandingImage.Logo, new MemoryStream(bytes), "x", type, bytes.Length);
        Assert.False(result.IsSuccess);
        Assert.Empty(_files.Files);
    }

    [Fact]
    public async Task Clean_svg_is_accepted()
    {
        var bytes = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><circle r='4'/></svg>");
        Assert.True((await _service.UploadImageAsync(BrandingImage.Watermark, new MemoryStream(bytes), "w.svg", "image/svg+xml", bytes.Length)).IsSuccess);
    }

    [Theory]
    [InlineData("24ABCDE1234F1Z5", "24", true)]
    [InlineData("24ABCDE1234F1Z5", "27", false)] // state code must match the GSTIN
    [InlineData("24ABCDE1234F1X5", "24", false)] // 14th character must be Z
    [InlineData(null, null, true)]
    public void Gst_rules(string? gstin, string? state, bool valid)
    {
        var result = new GstSettingsValidator().Validate(new GstSettings(gstin, 18m, state, "Gujarat", "INV", "VAL"));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Sms_provider_needs_a_sender_id() =>
        Assert.False(new UpdateCommunicationRequestValidator().Validate(
            new UpdateCommunicationRequest(null, 587, true, null, null, null, null, SmsProvider.Twilio, null, null)).IsValid);
}
