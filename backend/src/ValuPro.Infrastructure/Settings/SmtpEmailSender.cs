using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MimeKit;
using ValuPro.Application.Settings;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Settings;

/// <summary>
/// Sends email through the firm's own SMTP server (Settings → Email &amp; SMS).
/// In development, <c>Email:PickupDirectory</c> writes each message as an .eml file instead, so nothing leaves the machine.
/// </summary>
public sealed class SmtpEmailSender(AppDbContext db, SecretProtector secrets, IConfiguration config, IHostEnvironment env) : IEmailSender
{
    private string? Pickup => config["Email:PickupDirectory"] is { Length: > 0 } p ? Path.GetFullPath(Path.Combine(env.ContentRootPath, p)) : null;

    private Task<Domain.Settings.FirmSettings?> SettingsAsync(CancellationToken ct) =>
        db.Set<Domain.Settings.FirmSettings>().AsNoTracking().FirstOrDefaultAsync(ct);

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        if (Pickup is not null) return true;
        var s = await SettingsAsync(ct);
        return s is not null && !string.IsNullOrWhiteSpace(s.SmtpHost) && !string.IsNullOrWhiteSpace(s.FromEmail);
    }

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var pickup = Pickup;
        if (pickup is null && (s is null || string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.FromEmail)))
            throw new InvalidOperationException("Email is not set up. Add the SMTP host and From email first.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(s?.FromName ?? "", s?.FromEmail is { Length: > 0 } from ? from : "noreply@valupro.local"));
        foreach (var to in m.To) message.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in m.Cc) message.Cc.Add(MailboxAddress.Parse(cc));
        if (!string.IsNullOrWhiteSpace(m.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(m.ReplyTo));
        message.Subject = m.Subject;
        var body = new BodyBuilder { HtmlBody = m.HtmlBody };
        foreach (var a in m.Attachments ?? []) body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        message.Body = body.ToMessageBody();

        if (pickup is not null)
        {
            Directory.CreateDirectory(pickup);
            await message.WriteToAsync(Path.Combine(pickup, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml"), ct);
            return;
        }

        using var client = new SmtpClient { Timeout = 30_000 };
        // Port 465 = implicit TLS; otherwise upgrade with STARTTLS when SSL is on.
        var security = !s!.SmtpUseSsl ? SecureSocketOptions.None
            : s.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(s.SmtpHost!, s.SmtpPort, security, ct);
        var password = secrets.Unprotect(s.SmtpPasswordEncrypted);
        if (!string.IsNullOrWhiteSpace(s.SmtpUsername) && password is not null)
            await client.AuthenticateAsync(s.SmtpUsername, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
