using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Reports;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Reports;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Reports;

public sealed class ReportFormatService(
    AppDbContext db,
    ITenantContext tenant,
    IFileStorage files,
    IReportRenderer renderer,
    TimeProvider clock) : IReportFormatService
{
    public ReportFormatCatalog Catalog() => new(
        ReportSections.All, ReportFormatText.DeclarationPlaceholders, ReportFormatText.OpinionPlaceholders,
        ReportText.DefaultDeclaration("{inspectionDate}"), ReportFormatText.StandardOpinion, ReportFormatText.StandardTitle, ReportFormatText.StandardAddressTo);

    public async Task<IReadOnlyList<ReportFormatItem>> ListAsync(CancellationToken ct = default)
    {
        var formats = await db.Set<ReportFormat>().AsNoTracking().OrderByDescending(f => f.IsDefault).ThenBy(f => f.Name).ToListAsync(ct);
        var banks = await BanksByFormatAsync(ct);
        return [.. formats.Select(f => new ReportFormatItem(f.Id, f.Name, f.Description, f.IsDefault, banks.GetValueOrDefault(f.Id) ?? [], f.UpdatedAt ?? f.CreatedAt))];
    }

    public async Task<Result<ReportFormatDetail>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.Set<ReportFormat>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return f is null ? Error.NotFound("Report format not found.") : Detail(f, (await BanksByFormatAsync(ct)).GetValueOrDefault(f.Id) ?? []);
    }

    public async Task<Result<ReportFormatDetail>> CreateAsync(SaveReportFormatRequest r, CancellationToken ct = default)
    {
        if (await NameTakenAsync(r.Name, null, ct)) return Error.Conflict($"A format called \"{r.Name.Trim()}\" already exists.", "name");
        var f = new ReportFormat { Name = r.Name.Trim() };
        Apply(f, r);
        // The first format becomes the default, so every bank has one.
        f.IsDefault = !await db.Set<ReportFormat>().AnyAsync(ct);
        db.Add(f);
        await db.SaveChangesAsync(ct);
        return await GetAsync(f.Id, ct);
    }

    public async Task<Result<ReportFormatDetail>> UpdateAsync(Guid id, SaveReportFormatRequest r, CancellationToken ct = default)
    {
        var f = await db.Set<ReportFormat>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Error.NotFound("Report format not found.");
        if (await NameTakenAsync(r.Name, id, ct)) return Error.Conflict($"A format called \"{r.Name.Trim()}\" already exists.", "name");
        Apply(f, r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<ReportFormatDetail>> DuplicateAsync(Guid id, CancellationToken ct = default)
    {
        var src = await db.Set<ReportFormat>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (src is null) return Error.NotFound("Report format not found.");
        var name = $"{src.Name} (copy)";
        for (var n = 2; await NameTakenAsync(name, null, ct); n++) name = $"{src.Name} (copy {n})";
        var f = new ReportFormat
        {
            Name = name.Length > 100 ? name[..100] : name, Description = src.Description, Title = src.Title, Subtitle = src.Subtitle, AddressTo = src.AddressTo,
            Layout = src.Layout.Cleaned(),
            Declaration = src.Declaration, LimitingConditions = src.LimitingConditions, OpinionWording = src.OpinionWording,
        };
        db.Add(f);
        await db.SaveChangesAsync(ct);
        return await GetAsync(f.Id, ct);
    }

    public async Task<Result<ReportFormatDetail>> MakeDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var all = await db.Set<ReportFormat>().ToListAsync(ct);
        if (all.All(f => f.Id != id)) return Error.NotFound("Report format not found.");
        foreach (var f in all) f.IsDefault = f.Id == id;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.Set<ReportFormat>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Error.NotFound("Report format not found.");
        var banks = (await BanksByFormatAsync(ct)).GetValueOrDefault(id) ?? [];
        if (banks.Count > 0) return Error.Conflict($"Used by {string.Join(", ", banks)}. Give those banks another format first.");
        if (f.IsDefault && await db.Set<ReportFormat>().AnyAsync(x => x.Id != id, ct))
            return Error.Conflict("This is the default format. Make another format the default first.");
        db.Remove(f);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<byte[]> PreviewAsync(SaveReportFormatRequest r, bool stage, CancellationToken ct = default)
    {
        var f = new ReportFormat { Name = r.Name };
        Apply(f, r);
        var t = await db.Set<Tenant>().AsNoTracking().FirstAsync(x => x.Id == tenant.TenantId, ct);
        byte[]? logo = null;
        if (t.LogoBlobPath is { } path)
        {
            await using var s = await files.OpenReadAsync(path, ct);
            if (s is not null)
            {
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                logo = ms.ToArray();
            }
        }
        var firm = new FirmBlock(t.Name, string.Join(", ", new[] { t.Address, t.State }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } a ? a : null,
            t.Phone, t.ContactEmail, t.RegisteredValuerNo, t.IbbiRegistration, logo, t.PrimaryColor);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var declaration = ReportFormatText.Declaration(f.Declaration, today.AddDays(-2).ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-IN")), "Sample Bank", "Valuer name");
        return renderer.Render(ReportSamples.Model(firm, ReportFormatView.From(f), declaration, stage, today));
    }

    // ---------------- helpers ----------------

    private async Task<Dictionary<Guid, List<string>>> BanksByFormatAsync(CancellationToken ct)
    {
        var rows = await db.Set<Bank>().AsNoTracking().Where(b => b.ReportFormatId != null)
            .OrderBy(b => b.Name).Select(b => new { b.ReportFormatId, b.ShortCode }).ToListAsync(ct);
        return rows.GroupBy(x => x.ReportFormatId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.ShortCode).ToList());
    }

    private Task<bool> NameTakenAsync(string name, Guid? except, CancellationToken ct)
    {
        var n = name.Trim();
        return db.Set<ReportFormat>().AnyAsync(f => f.Name == n && f.Id != except, ct);
    }

    private static ReportFormatDetail Detail(ReportFormat f, IReadOnlyList<string> banks) =>
        new(f.Id, f.Name, f.Description, f.IsDefault, f.Title, f.Subtitle, f.AddressTo, f.Layout.Cleaned(),
            f.Declaration, f.LimitingConditions, f.OpinionWording, banks);

    private static void Apply(ReportFormat f, SaveReportFormatRequest r)
    {
        f.Name = r.Name.Trim();
        f.Description = Clean(r.Description);
        f.Title = Clean(r.Title);
        f.Subtitle = Clean(r.Subtitle);
        f.AddressTo = Clean(r.AddressTo);
        f.Layout = (r.Layout ?? ReportLayout.Standard()).Cleaned();
        f.Declaration = Clean(r.Declaration);
        f.LimitingConditions = Clean(r.LimitingConditions);
        f.OpinionWording = Clean(r.OpinionWording);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Replace("\r\n", "\n");
}
