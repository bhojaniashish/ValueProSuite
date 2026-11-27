using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Settings;
using ValuPro.Domain.Settings;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Settings;

public sealed class SettingsService(
    AppDbContext db,
    ITenantContext tenantContext,
    IFileStorage files,
    SecretProtector secrets,
    IEmailSender email,
    ILogger<SettingsService> log) : ISettingsService
{
    public const long MaxImageBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
    };

    // ---------------- read ----------------

    public async Task<SettingsResponse> GetAsync(CancellationToken ct = default)
    {
        var tenant = await TenantAsync(ct);
        var s = await FirmSettingsAsync(ct);
        return new SettingsResponse(Company(tenant), Branding(tenant), Communication(s), Gst(s), Notifications(s));
    }

    public async Task<PublicBranding> GetPublicBrandingAsync(CancellationToken ct = default)
    {
        var t = await TenantAsync(ct);
        return new PublicBranding(t.Name, t.PrimaryColor, t.AccentColor, ImageUrl(t, BrandingImage.Logo));
    }

    public async Task<FileContent?> OpenImageAsync(BrandingImage image, CancellationToken ct = default)
    {
        var t = await TenantAsync(ct);
        var path = image == BrandingImage.Logo ? t.LogoBlobPath : t.WatermarkBlobPath;
        if (path is null) return null;
        var stream = await files.OpenReadAsync(path, ct);
        if (stream is null) return null;
        var type = ImageTypes.FirstOrDefault(kv => path.EndsWith(kv.Value, StringComparison.OrdinalIgnoreCase)).Key ?? "application/octet-stream";
        return new FileContent(stream, type);
    }

    // ---------------- company & branding ----------------

    public async Task<Result<CompanySettings>> UpdateCompanyAsync(CompanySettings r, CancellationToken ct = default)
    {
        var t = await TenantAsync(ct);
        t.Name = r.Name.Trim();
        t.RegisteredValuerNo = Clean(r.RegisteredValuerNo);
        t.IbbiRegistration = Clean(r.IbbiRegistration);
        t.ContactEmail = Clean(r.ContactEmail);
        t.Phone = Clean(r.Phone);
        t.Address = Clean(r.Address);
        t.State = Clean(r.State);
        await db.SaveChangesAsync(ct);
        return Company(t);
    }

    public async Task<Result<BrandingSettings>> UpdateBrandingAsync(UpdateBrandingRequest r, CancellationToken ct = default)
    {
        var t = await TenantAsync(ct);
        t.PrimaryColor = r.PrimaryColor.ToUpperInvariant();
        t.AccentColor = r.AccentColor.ToUpperInvariant();
        t.BrandingVersion++;
        await db.SaveChangesAsync(ct);
        return Branding(t);
    }

    public async Task<Result<BrandingSettings>> UploadImageAsync(BrandingImage image, Stream content, string fileName,
        string contentType, long length, CancellationToken ct = default)
    {
        if (length <= 0) return Error.Validation("Choose an image file.", "file");
        if (length > MaxImageBytes) return Error.Validation("The image must be 2 MB or smaller.", "file");
        if (!ImageTypes.TryGetValue(contentType, out var extension))
            return Error.Validation("Use a PNG, JPG, WebP or SVG image.", "file");

        // Check the bytes, not just the declared type.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (!LooksLike(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), extension))
            return Error.Validation("This file is not a valid image of the chosen type.", "file");
        buffer.Position = 0;

        var t = await TenantAsync(ct);
        var path = await files.UploadAsync("branding", $"{image.ToString().ToLowerInvariant()}{extension}", buffer, contentType, ct);
        var old = image == BrandingImage.Logo ? t.LogoBlobPath : t.WatermarkBlobPath;
        if (image == BrandingImage.Logo) t.LogoBlobPath = path; else t.WatermarkBlobPath = path;
        t.BrandingVersion++;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return Branding(t);
    }

    public async Task<Result<BrandingSettings>> RemoveImageAsync(BrandingImage image, CancellationToken ct = default)
    {
        var t = await TenantAsync(ct);
        var old = image == BrandingImage.Logo ? t.LogoBlobPath : t.WatermarkBlobPath;
        if (image == BrandingImage.Logo) t.LogoBlobPath = null; else t.WatermarkBlobPath = null;
        t.BrandingVersion++;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return Branding(t);
    }

    // ---------------- communication ----------------

    public async Task<Result<CommunicationSettings>> UpdateCommunicationAsync(UpdateCommunicationRequest r, CancellationToken ct = default)
    {
        var s = await FirmSettingsAsync(ct);
        s.SmtpHost = Clean(r.SmtpHost);
        s.SmtpPort = r.SmtpPort;
        s.SmtpUseSsl = r.SmtpUseSsl;
        s.SmtpUsername = Clean(r.SmtpUsername);
        s.FromEmail = Clean(r.FromEmail);
        s.FromName = Clean(r.FromName);
        s.SmsProvider = r.SmsProvider;
        s.SmsSenderId = Clean(r.SmsSenderId)?.ToUpperInvariant();

        // null = keep, "" = clear, value = replace
        if (r.SmtpPassword is not null)
            s.SmtpPasswordEncrypted = r.SmtpPassword.Length == 0 ? null : secrets.Protect(r.SmtpPassword);
        if (r.SmsApiKey is not null)
            s.SmsApiKeyEncrypted = r.SmsApiKey.Length == 0 ? null : secrets.Protect(r.SmsApiKey);

        await db.SaveChangesAsync(ct);
        return Communication(s);
    }

    public async Task<Result<string>> SendTestEmailAsync(string to, CancellationToken ct = default)
    {
        var tenant = await TenantAsync(ct);
        try
        {
            await email.SendAsync(to, $"{tenant.Name}: test email from ValuPro",
                $"<p>This is a test email from <b>{WebUtility.HtmlEncode(tenant.Name)}</b>'s ValuPro account.</p>" +
                "<p>If you received it, email notifications are set up correctly.</p>", ct);
            return $"Test email sent to {to}.";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Test email failed");
            return Error.Validation($"Could not send the test email: {e.Message}");
        }
    }

    // ---------------- GST & notifications ----------------

    public async Task<Result<GstSettings>> UpdateGstAsync(GstSettings r, CancellationToken ct = default)
    {
        var s = await FirmSettingsAsync(ct);
        s.Gstin = Clean(r.Gstin)?.ToUpperInvariant();
        s.DefaultGstRate = r.DefaultGstRate;
        s.StateCode = Clean(r.StateCode) ?? s.Gstin?[..2];
        s.PlaceOfSupply = Clean(r.PlaceOfSupply);
        s.InvoicePrefix = r.InvoicePrefix.Trim().ToUpperInvariant();
        s.AssignmentPrefix = r.AssignmentPrefix.Trim().ToUpperInvariant();
        s.SacCode = r.SacCode.Trim();
        s.PaymentDueDays = r.PaymentDueDays;
        s.PaymentDetails = Clean(r.PaymentDetails);
        s.InvoiceTerms = Clean(r.InvoiceTerms);
        await db.SaveChangesAsync(ct);
        return Gst(s);
    }

    public async Task<Result<IReadOnlyList<NotificationSetting>>> UpdateNotificationsAsync(UpdateNotificationsRequest r,
        CancellationToken ct = default)
    {
        var s = await FirmSettingsAsync(ct);
        var input = r.Preferences.ToDictionary(p => p.Event);
        // Keep every known event, in catalogue order; events not sent keep their current values.
        s.Notifications =
        [
            .. NotificationEvents.All.Select(e =>
            {
                var current = s.Notifications.FirstOrDefault(n => n.Event == e.Key);
                return input.TryGetValue(e.Key, out var p)
                    ? new NotificationPreference { Event = e.Key, Email = p.Email, Sms = p.Sms, InApp = p.InApp }
                    : current ?? new NotificationPreference { Event = e.Key, Email = e.Email, Sms = e.Sms, InApp = e.InApp };
            }),
        ];
        await db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<NotificationSetting>>.Ok(Notifications(s));
    }

    // ---------------- helpers ----------------

    private async Task<Tenant> TenantAsync(CancellationToken ct) =>
        await db.Tenants.FirstAsync(t => t.Id == tenantContext.TenantId, ct);

    /// <summary>The firm's settings row, created with defaults on first use.</summary>
    private async Task<FirmSettings> FirmSettingsAsync(CancellationToken ct)
    {
        var s = await db.Set<FirmSettings>().FirstOrDefaultAsync(ct);
        if (s is not null) return s;
        s = new FirmSettings();
        db.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    private static CompanySettings Company(Tenant t) =>
        new(t.Name, t.RegisteredValuerNo, t.IbbiRegistration, t.ContactEmail, t.Phone, t.Address, t.State);

    private static BrandingSettings Branding(Tenant t) =>
        new(t.PrimaryColor, t.AccentColor, ImageUrl(t, BrandingImage.Logo), ImageUrl(t, BrandingImage.Watermark));

    private static string? ImageUrl(Tenant t, BrandingImage image)
    {
        var path = image == BrandingImage.Logo ? t.LogoBlobPath : t.WatermarkBlobPath;
        return path is null ? null : $"/api/branding/{image.ToString().ToLowerInvariant()}?v={t.BrandingVersion}";
    }

    private static CommunicationSettings Communication(FirmSettings s) =>
        new(s.SmtpHost, s.SmtpPort, s.SmtpUseSsl, s.SmtpUsername, s.SmtpPasswordEncrypted is not null,
            s.FromEmail, s.FromName, s.SmsProvider, s.SmsSenderId, s.SmsApiKeyEncrypted is not null);

    private static GstSettings Gst(FirmSettings s) =>
        new(s.Gstin, s.DefaultGstRate, s.StateCode, s.PlaceOfSupply, s.InvoicePrefix, s.AssignmentPrefix, s.SacCode, s.PaymentDueDays,
            s.PaymentDetails, s.InvoiceTerms);

    private static IReadOnlyList<NotificationSetting> Notifications(FirmSettings s) =>
    [
        .. NotificationEvents.All.Select(e =>
        {
            var p = s.Notifications.FirstOrDefault(n => n.Event == e.Key);
            return new NotificationSetting(e.Key, e.Label, e.Description, p?.Email ?? e.Email, p?.Sms ?? e.Sms, p?.InApp ?? e.InApp);
        }),
    ];

    private async Task DeleteQuietlyAsync(string? path, CancellationToken ct)
    {
        if (path is null) return;
        try { await files.DeleteAsync(path, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Could not delete old file {Path}", path); }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Checks the file's first bytes match its declared image type.</summary>
    public static bool LooksLike(ReadOnlySpan<byte> b, string extension) => extension switch
    {
        ".png" => b.Length > 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        ".webp" => b.Length > 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8),
        ".svg" => IsSafeSvg(b),
        _ => false,
    };

    private static bool IsSafeSvg(ReadOnlySpan<byte> b)
    {
        var text = System.Text.Encoding.UTF8.GetString(b).ToLowerInvariant();
        // SVGs can carry script; refuse anything active. Files are also served with a sandboxing CSP.
        return text.Contains("<svg") && !text.Contains("<script") && !text.Contains("javascript:") &&
               !text.Contains("<foreignobject") && !System.Text.RegularExpressions.Regex.IsMatch(text, @"\son[a-z]+\s*=");
    }
}
