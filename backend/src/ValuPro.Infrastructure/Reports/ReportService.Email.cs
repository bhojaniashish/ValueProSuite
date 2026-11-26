using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Common;
using ValuPro.Application.Reports;
using ValuPro.Application.Settings;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Reports;
using ValuPro.Domain.Security;
using ValuPro.Domain.Tenancy;
using ValuPro.Domain.Valuations;

namespace ValuPro.Infrastructure.Reports;

/// <summary>Emailing the signed report to the bank.</summary>
public sealed partial class ReportService
{
    /// <summary>Mail servers commonly refuse messages above ~25 MB once encoded; keep the PDF well under that.</summary>
    public const long MaxAttachmentBytes = 18 * 1024 * 1024;

    private string? EmailBlockedReason(Loaded l) =>
        !me.HasPermission(Permissions.Reports.Manage) ? "Emailing the report needs the \"Reports: Manage\" permission."
        : !l.Files.Any(f => f.Kind == ReportKind.Final) ? "Only a signed report goes to the bank: approve & sign it first."
        : !l.EmailReady ? "Email is not set up yet (Settings → Email & SMS)."
        : null;

    private static string AttachmentName(Assignment a, ReportFile f) =>
        $"{a.Number.Replace('/', '-')}-{(a.CaseType == CaseType.Valuation ? "valuation" : "stage")}-report-v{f.Number}.pdf";

    private static string KindText(Assignment a) => a.CaseType switch
    {
        CaseType.StageInspection => "stage inspection report",
        CaseType.Reinspection => "re-inspection report",
        _ => "valuation report",
    };

    public async Task<Result<ReportEmailDraft>> EmailDraftAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var l = await LoadAsync(assignmentId, ct);
        if (l is null) return Error.NotFound("Assignment not found.");
        if (EmailBlockedReason(l) is { } blocked) return Error.Forbidden(blocked);

        var a = l.Row.A;
        var file = l.Files.Where(f => f.Kind == ReportKind.Final).MaxBy(f => f.Number)!;
        var t = await db.Set<Tenant>().AsNoTracking().FirstAsync(x => x.Id == tenant.TenantId, ct);
        var last = l.Emails.FirstOrDefault(e => e.Status == EmailStatus.Sent);

        // The branch's address first; else whoever it went to last time.
        var to = !string.IsNullOrWhiteSpace(l.Row.Branch?.Email) ? [l.Row.Branch!.Email!.Trim()]
            : last is not null ? Split(last.To) : [];
        var cc = last?.Cc is { } lastCc ? Split(lastCc) : [];
        // A copy to the sender, so the firm's own mailbox has what went out.
        if (me.Email is { Length: > 0 } mine && !to.Contains(mine, StringComparer.OrdinalIgnoreCase) && !cc.Contains(mine, StringComparer.OrdinalIgnoreCase))
            cc.Add(mine);

        var subject = $"{Cap(KindText(a))}: {a.Number} · {a.ApplicantName}" + (a.BankReference is { Length: > 0 } r ? $" · Ref {r}" : "");
        if (subject.Length > 200) subject = subject[..200];

        var sb = new StringBuilder();
        sb.AppendLine($"Dear {(l.Row.Branch?.ContactPerson is { Length: > 0 } cp ? cp : "Sir / Madam")},").AppendLine();
        sb.AppendLine($"Please find attached our {KindText(a)} for the property below.").AppendLine();
        sb.AppendLine($"Applicant: {a.ApplicantName}");
        sb.AppendLine($"Property: {a.PropertySummary}");
        sb.AppendLine($"Our reference: {a.Number}" + (a.BankReference is { Length: > 0 } br ? $" (your reference {br})" : ""));
        if (l.Valuation is { } v)
        {
            if (a.CaseType != CaseType.Valuation && v.Input.Progress is { } pr)
            {
                var p = ValuationCalculator.Progress(pr);
                sb.AppendLine($"Completion: {IndianNumber.Format(pr.CompletionPercent)}%, release recommended now: ₹{IndianNumber.Format(p.RecommendedRelease)}");
            }
            if (v.FairMarketValue > 0) sb.AppendLine($"Fair market value: ₹{IndianNumber.Format(v.FairMarketValue)}");
        }
        sb.AppendLine().AppendLine("Please let us know if you need anything further.").AppendLine();
        sb.AppendLine("Regards,");
        if (me.Name is { Length: > 0 } name) sb.AppendLine(name);
        sb.Append(t.Name);
        if (t.Phone is { Length: > 0 } phone) sb.AppendLine().Append(phone);

        return new ReportEmailDraft(file.Id, file.Number, AttachmentName(a, file), file.SizeBytes, to, cc, subject, sb.ToString(), me.Email,
            l.Row.Bank.Name, l.Row.Branch?.Name, last is not null);
    }

    public async Task<Result<ReportDetail>> EmailAsync(Guid assignmentId, SendReportEmailRequest r, CancellationToken ct = default)
    {
        var l = await LoadAsync(assignmentId, ct);
        if (l is null) return Error.NotFound("Assignment not found.");
        if (EmailBlockedReason(l) is { } blocked) return Error.Forbidden(blocked);
        var file = l.Files.FirstOrDefault(f => f.Id == r.FileId);
        if (file is null) return Error.Validation("Choose a report of this case.", "fileId");
        if (file.Kind != ReportKind.Final) return Error.Validation("Only a signed report can be emailed to the bank.", "fileId");
        if (file.SizeBytes > MaxAttachmentBytes)
            return Error.Validation($"The report is {file.SizeBytes / 1024 / 1024} MB, too large for email. Choose fewer report photos and approve again.", "fileId");
        var pdf = await ReadAsync(file.BlobPath, ct);
        if (pdf is null) return Error.NotFound("The report file is missing.");

        var a = l.Row.A;
        var to = Clean(r.To);
        var cc = Clean(r.Cc ?? []).Where(x => !to.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        var log = new ReportEmail
        {
            AssignmentId = a.Id, ReportFileId = file.Id, ReportNumber = file.Number, To = string.Join(", ", to),
            Cc = cc.Count > 0 ? string.Join(", ", cc) : null, Subject = r.Subject.Trim(), SentByUserId = me.UserId, SentByName = me.Name ?? "Unknown",
        };
        try
        {
            await email.SendAsync(new EmailMessage(to, cc, log.Subject, Html(r.Message), me.Email,
                [new EmailAttachment(AttachmentName(a, file), "application/pdf", pdf)]), ct);
            log.Status = EmailStatus.Sent;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Status = EmailStatus.Failed;
            log.Error = e.Message.Length > 500 ? e.Message[..500] : e.Message;
        }

        db.Add(log);
        if (log.Status == EmailStatus.Sent)
            db.Add(new AssignmentEvent
            {
                AssignmentId = a.Id, At = clock.GetUtcNow(), ByUserId = me.UserId, ByName = me.Name ?? "System", FromStatus = a.Status, ToStatus = a.Status,
                Title = "Report emailed to the bank", Note = $"Version {file.Number} to {log.To}" + (log.Cc is null ? "." : $", copy to {log.Cc}."),
            });
        await db.SaveChangesAsync(ct);

        return log.Status == EmailStatus.Sent
            ? await GetAsync(assignmentId, ct)
            : Error.Validation($"Could not send the email: {log.Error}");
    }

    private static List<string> Split(string list) => [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static List<string> Clean(IEnumerable<string> list) =>
        [.. list.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The valuer's plain-text message as simple HTML (encoded, line breaks kept).</summary>
    private static string Html(string text) =>
        "<div style=\"font-family:Arial,sans-serif;font-size:14px;line-height:1.5;color:#222\">" +
        string.Concat(text.Replace("\r\n", "\n").Split("\n\n").Select(p => $"<p>{WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</p>")) +
        "</div>";
}
