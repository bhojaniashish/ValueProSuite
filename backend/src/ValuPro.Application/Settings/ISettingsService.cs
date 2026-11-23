using ValuPro.Application.Common;

namespace ValuPro.Application.Settings;

public enum BrandingImage
{
    Logo,
    Watermark,
}

public interface ISettingsService
{
    Task<SettingsResponse> GetAsync(CancellationToken ct = default);

    Task<Result<CompanySettings>> UpdateCompanyAsync(CompanySettings request, CancellationToken ct = default);
    Task<Result<BrandingSettings>> UpdateBrandingAsync(UpdateBrandingRequest request, CancellationToken ct = default);
    Task<Result<BrandingSettings>> UploadImageAsync(BrandingImage image, Stream content, string fileName, string contentType, long length, CancellationToken ct = default);
    Task<Result<BrandingSettings>> RemoveImageAsync(BrandingImage image, CancellationToken ct = default);
    Task<Result<CommunicationSettings>> UpdateCommunicationAsync(UpdateCommunicationRequest request, CancellationToken ct = default);
    Task<Result<GstSettings>> UpdateGstAsync(GstSettings request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<NotificationSetting>>> UpdateNotificationsAsync(UpdateNotificationsRequest request, CancellationToken ct = default);

    /// <summary>Sends a test email with the saved SMTP settings to <paramref name="to"/>.</summary>
    Task<Result<string>> SendTestEmailAsync(string to, CancellationToken ct = default);

    Task<PublicBranding> GetPublicBrandingAsync(CancellationToken ct = default);
    Task<FileContent?> OpenImageAsync(BrandingImage image, CancellationToken ct = default);
}

/// <summary>Sends email with the firm's SMTP settings.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
