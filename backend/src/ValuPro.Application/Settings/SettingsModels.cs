using ValuPro.Domain.Settings;

namespace ValuPro.Application.Settings;

public sealed record CompanySettings(
    string Name,
    string? RegisteredValuerNo,
    string? IbbiRegistration,
    string? ContactEmail,
    string? Phone,
    string? Address,
    string? State);

/// <summary>Branding as stored; logo/watermark URLs are versioned so browsers refresh after a change.</summary>
public sealed record BrandingSettings(string PrimaryColor, string AccentColor, string? LogoUrl, string? WatermarkUrl);

public sealed record UpdateBrandingRequest(string PrimaryColor, string AccentColor);

/// <summary>Read model: secrets are never returned, only whether one is set.</summary>
public sealed record CommunicationSettings(
    string? SmtpHost,
    int SmtpPort,
    bool SmtpUseSsl,
    string? SmtpUsername,
    bool HasSmtpPassword,
    string? FromEmail,
    string? FromName,
    SmsProvider SmsProvider,
    string? SmsSenderId,
    bool HasSmsApiKey);

/// <summary>
/// Write model. For each secret: null keeps the stored value, "" clears it, anything else replaces it.
/// </summary>
public sealed record UpdateCommunicationRequest(
    string? SmtpHost,
    int SmtpPort,
    bool SmtpUseSsl,
    string? SmtpUsername,
    string? SmtpPassword,
    string? FromEmail,
    string? FromName,
    SmsProvider SmsProvider,
    string? SmsSenderId,
    string? SmsApiKey);

public sealed record GstSettings(
    string? Gstin,
    decimal DefaultGstRate,
    string? StateCode,
    string? PlaceOfSupply,
    string InvoicePrefix,
    string AssignmentPrefix,
    string SacCode = "997222",
    int PaymentDueDays = 30,
    string? PaymentDetails = null,
    string? InvoiceTerms = null);

public sealed record NotificationSetting(string Event, string Label, string Description, bool Email, bool Sms, bool InApp);

public sealed record UpdateNotificationsRequest(IReadOnlyList<NotificationPreferenceInput> Preferences);

public sealed record NotificationPreferenceInput(string Event, bool Email, bool Sms, bool InApp);

public sealed record SettingsResponse(
    CompanySettings Company,
    BrandingSettings Branding,
    CommunicationSettings Communication,
    GstSettings Gst,
    IReadOnlyList<NotificationSetting> Notifications);

/// <summary>What the app needs before sign-in to look like the firm (login page, sidebar).</summary>
public sealed record PublicBranding(string FirmName, string PrimaryColor, string AccentColor, string? LogoUrl);

public sealed record FileContent(Stream Content, string ContentType);
