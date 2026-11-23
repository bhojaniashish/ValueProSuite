using ValuPro.Domain.Common;

namespace ValuPro.Domain.Settings;

public enum SmsProvider
{
    None = 0,
    Msg91 = 1,
    Twilio = 2,
    TextLocal = 3,
}

/// <summary>
/// A firm's operational configuration: email and SMS, GST and numbering, notification preferences.
/// One row per tenant. (Firm identity and branding live on <see cref="Tenancy.Tenant"/>.)
/// Secrets are stored encrypted and never returned by the API.
/// </summary>
public class FirmSettings : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }

    // Email (SMTP)
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPasswordEncrypted { get; set; }
    public string? FromEmail { get; set; }
    public string? FromName { get; set; }

    // SMS gateway
    public SmsProvider SmsProvider { get; set; } = SmsProvider.None;
    public string? SmsSenderId { get; set; }
    public string? SmsApiKeyEncrypted { get; set; }

    // GST and numbering
    public string? Gstin { get; set; }
    public decimal DefaultGstRate { get; set; } = 18m;
    public string? StateCode { get; set; }
    public string? PlaceOfSupply { get; set; }
    public string InvoicePrefix { get; set; } = "INV";
    public string AssignmentPrefix { get; set; } = "VAL";

    public List<NotificationPreference> Notifications { get; set; } = NotificationEvents.Defaults();
}

public class NotificationPreference
{
    public required string Event { get; set; }
    public bool Email { get; set; }
    public bool Sms { get; set; }
    public bool InApp { get; set; }
}

public sealed record NotificationEvent(string Key, string Label, string Description, bool Email, bool Sms, bool InApp);

/// <summary>Events a firm can be notified about, with the prototype's default channels.</summary>
public static class NotificationEvents
{
    public static readonly IReadOnlyList<NotificationEvent> All =
    [
        new("assignment.received", "New assignment received", "A bank or client sends a new valuation case.", true, true, true),
        new("visit.reminder", "Site visit reminders", "Before a scheduled site inspection.", false, false, true),
        new("report.due", "Report due deadline", "A report is close to or past its due date.", true, false, true),
        new("payment.received", "Payment received", "An invoice is marked as paid.", true, false, false),
        new("document.expiry", "Document expiry alerts", "A property document is about to expire.", true, true, false),
        new("weekly.summary", "Weekly summary report", "Every Monday: cases, revenue and pending work.", false, false, false),
        new("qc.review", "QC review requests", "A draft report is waiting for review.", false, false, true),
        new("bank.query", "Bank queries", "A bank raises a query on a submitted report.", true, true, true),
    ];

    public static List<NotificationPreference> Defaults() =>
        [.. All.Select(e => new NotificationPreference { Event = e.Key, Email = e.Email, Sms = e.Sms, InApp = e.InApp })];
}
