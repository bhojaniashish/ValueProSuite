using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using ValuPro.Application.Settings;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Settings;

/// <summary>Sends email through the firm's own SMTP server (Settings → Email &amp; SMS).</summary>
public sealed class SmtpEmailSender(AppDbContext db, SecretProtector secrets) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var s = await db.Set<Domain.Settings.FirmSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (s is null || string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.FromEmail))
            throw new InvalidOperationException("Email is not set up. Add the SMTP host and From email first.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(s.FromName ?? "", s.FromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15_000 };
        // Port 465 = implicit TLS; otherwise upgrade with STARTTLS when SSL is on.
        var security = !s.SmtpUseSsl ? SecureSocketOptions.None
            : s.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(s.SmtpHost, s.SmtpPort, security, ct);
        var password = secrets.Unprotect(s.SmtpPasswordEncrypted);
        if (!string.IsNullOrWhiteSpace(s.SmtpUsername) && password is not null)
            await client.AuthenticateAsync(s.SmtpUsername, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
