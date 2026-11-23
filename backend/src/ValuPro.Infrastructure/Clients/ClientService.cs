using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Domain.Clients;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Clients;

public sealed partial class ClientService(
    AppDbContext db,
    INumberSequence numbers,
    IValidator<SaveClientRequest> validator) : IClientService
{
    public const string SequenceKey = "client";

    private static ClientItem ToItem(Client c) => new(c.Id, c.Code, c.Type, c.Name, c.Mobile, c.Email, c.Gstin, c.Pan,
        c.Address, c.City, c.State, c.Pincode, c.Remarks, c.CreatedAt);

    // ---------------- read ----------------

    public async Task<PagedResult<ClientItem>> ListAsync(ClientQuery query, CancellationToken ct = default)
    {
        var q = db.Set<Client>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            var digits = DigitsOnly(s);
            q = q.Where(c => c.Name.Contains(s) || c.Code.Contains(s) || c.Email!.Contains(s) || c.City!.Contains(s) ||
                             c.Pan!.Contains(s) || c.Gstin!.Contains(s) || c.Mobile.Contains(s) ||
                             (digits.Length >= 4 && c.Mobile.Replace(" ", "").Contains(digits)));
        }
        if (query.Type is { } type) q = q.Where(c => c.Type == type);
        if (!string.IsNullOrWhiteSpace(query.State)) q = q.Where(c => c.State == query.State);

        q = (query.Sort?.ToLowerInvariant(), query.Desc) switch
        {
            ("code", false) => q.OrderBy(c => c.Code),
            ("code", true) => q.OrderByDescending(c => c.Code),
            ("type", false) => q.OrderBy(c => c.Type).ThenBy(c => c.Name),
            ("type", true) => q.OrderByDescending(c => c.Type).ThenBy(c => c.Name),
            ("city", false) => q.OrderBy(c => c.City).ThenBy(c => c.Name),
            ("city", true) => q.OrderByDescending(c => c.City).ThenBy(c => c.Name),
            ("created", false) => q.OrderBy(c => c.CreatedAt),
            ("created", true) => q.OrderByDescending(c => c.CreatedAt),
            (_, true) => q.OrderByDescending(c => c.Name),
            _ => q.OrderBy(c => c.Name),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<ClientItem>([.. items.Select(ToItem)], total, query.Page, query.PageSize);
    }

    public async Task<ClientSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var byType = await db.Set<Client>().GroupBy(c => c.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(ClientType t) => byType.FirstOrDefault(x => x.Key == t)?.Count ?? 0;
        var states = await db.Set<Client>().Where(c => c.State != null).Select(c => c.State!).Distinct().OrderBy(s => s).ToListAsync(ct);
        return new ClientSummary(byType.Sum(x => x.Count), Count(ClientType.Individual), Count(ClientType.Builder),
            Count(ClientType.BankBranch), Count(ClientType.Advocate), states);
    }

    public async Task<Result<ClientItem>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? Error.NotFound("Client not found.") : ToItem(c);
    }

    // ---------------- write ----------------

    public async Task<Result<ClientItem>> CreateAsync(SaveClientRequest request, CancellationToken ct = default)
    {
        var r = Normalize(request);
        if (await DuplicateAsync(r, null, ct) is { } duplicate) return duplicate;

        var number = await numbers.NextAsync(SequenceKey, ct); // saves on its own, before the client is tracked
        var client = new Client { Code = $"CL{number:0000}", Name = r.Name, Mobile = r.Mobile };
        Apply(client, r);
        db.Add(client);
        await db.SaveChangesAsync(ct);
        return ToItem(client);
    }

    public async Task<Result<ClientItem>> UpdateAsync(Guid id, SaveClientRequest request, CancellationToken ct = default)
    {
        var client = await db.Set<Client>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (client is null) return Error.NotFound("Client not found.");

        var r = Normalize(request);
        if (await DuplicateAsync(r, id, ct) is { } duplicate) return duplicate;

        Apply(client, r);
        await db.SaveChangesAsync(ct);
        return ToItem(client);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var client = await db.Set<Client>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (client is null) return Error.NotFound("Client not found.");
        // Phase 1 (Assignments): refuse while the client has open assignments.
        db.Remove(client); // soft delete via AuditingInterceptor
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------- import ----------------

    public async Task<Result<ImportPreview>> PreviewImportAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        var (rows, error) = ClientFileReader.Read(file, fileName);
        if (error is not null) return Error.Validation(error, "file");
        if (rows.Count == 0) return Error.Validation("No clients found in the file.", "file");

        var checkedRows = await CheckRowsAsync(rows.Select(r =>
        {
            var (client, typeError) = ClientFileReader.ToRequest(r.Values);
            return (r.Row, client, typeError);
        }).ToList(), ct);

        var valid = checkedRows.Count(r => r.IsValid);
        return new ImportPreview(checkedRows, valid, checkedRows.Count - valid);
    }

    public async Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct = default)
    {
        if (request.Clients is not { Count: > 0 }) return Error.Validation("Nothing to import.");
        if (request.Clients.Count > ClientFileReader.MaxRows) return Error.Validation($"Import at most {ClientFileReader.MaxRows} clients at a time.");

        // Check again: data may have changed since the preview, and the request could be edited.
        var checkedRows = await CheckRowsAsync(request.Clients.Select((c, i) => (Row: i + 1, (SaveClientRequest?)c, (string?)null)).ToList(), ct);

        var imported = 0;
        foreach (var row in checkedRows.Where(r => r.IsValid))
        {
            var number = await numbers.NextAsync(SequenceKey, ct);
            var client = new Client { Code = $"CL{number:0000}", Name = row.Client!.Name, Mobile = row.Client.Mobile };
            Apply(client, row.Client);
            db.Add(client);
            await db.SaveChangesAsync(ct);
            imported++;
        }
        return new ImportResult(imported, [.. checkedRows.Where(r => !r.IsValid)]);
    }

    /// <summary>Validates each row, then checks PAN/GSTIN against existing clients and earlier rows of the same file.</summary>
    private async Task<List<ImportRow>> CheckRowsAsync(List<(int Row, SaveClientRequest? Client, string? TypeError)> rows, CancellationToken ct)
    {
        var normalized = rows.Select(r => (r.Row, Client: r.Client is null ? null : Normalize(r.Client), r.TypeError)).ToList();
        var pans = normalized.Select(r => r.Client?.Pan).OfType<string>().ToList();
        var gstins = normalized.Select(r => r.Client?.Gstin).OfType<string>().ToList();
        var existingPans = (await db.Set<Client>().Where(c => pans.Contains(c.Pan!)).Select(c => new { c.Pan, c.Code }).ToListAsync(ct))
            .ToDictionary(x => x.Pan!, x => x.Code);
        var existingGstins = (await db.Set<Client>().Where(c => gstins.Contains(c.Gstin!)).Select(c => new { c.Gstin, c.Code }).ToListAsync(ct))
            .ToDictionary(x => x.Gstin!, x => x.Code);

        var seenPan = new Dictionary<string, int>();
        var seenGstin = new Dictionary<string, int>();
        var result = new List<ImportRow>();
        foreach (var (row, client, typeError) in normalized)
        {
            var errors = new List<string>();
            if (typeError is not null) errors.Add(typeError);
            if (client is not null)
            {
                var validation = await validator.ValidateAsync(client, ct);
                errors.AddRange(validation.Errors.Where(e => e.PropertyName != nameof(SaveClientRequest.Type) || typeError is null).Select(e => e.ErrorMessage));

                if (client.Pan is { } pan)
                {
                    if (existingPans.TryGetValue(pan, out var code)) errors.Add($"PAN {pan} already belongs to client {code}.");
                    else if (seenPan.TryGetValue(pan, out var first)) errors.Add($"PAN {pan} is repeated (row {first}).");
                    else seenPan[pan] = row;
                }
                if (client.Gstin is { } gstin)
                {
                    if (existingGstins.TryGetValue(gstin, out var code)) errors.Add($"GSTIN {gstin} already belongs to client {code}.");
                    else if (seenGstin.TryGetValue(gstin, out var first)) errors.Add($"GSTIN {gstin} is repeated (row {first}).");
                    else seenGstin[gstin] = row;
                }
            }
            result.Add(new ImportRow(row, client, errors.Distinct().ToList()));
        }
        return result;
    }

    // ---------------- helpers ----------------

    private async Task<Error?> DuplicateAsync(SaveClientRequest r, Guid? exceptId, CancellationToken ct)
    {
        var others = db.Set<Client>().Where(c => c.Id != exceptId);
        if (r.Pan is not null && await others.FirstOrDefaultAsync(c => c.Pan == r.Pan, ct) is { } byPan)
            return Error.Conflict($"This PAN already belongs to {byPan.Name} ({byPan.Code}).", "pan");
        if (r.Gstin is not null && await others.FirstOrDefaultAsync(c => c.Gstin == r.Gstin, ct) is { } byGst)
            return Error.Conflict($"This GSTIN already belongs to {byGst.Name} ({byGst.Code}).", "gstin");
        return null;
    }

    private static void Apply(Client c, SaveClientRequest r)
    {
        c.Type = r.Type;
        c.Name = r.Name;
        c.Mobile = r.Mobile;
        c.Email = r.Email;
        c.Gstin = r.Gstin;
        c.Pan = r.Pan;
        c.Address = r.Address;
        c.City = r.City;
        c.State = r.State;
        c.Pincode = r.Pincode;
        c.Remarks = r.Remarks;
    }

    /// <summary>Trim, collapse spaces, upper-case tax ids, lower-case email; blanks become null.</summary>
    public static SaveClientRequest Normalize(SaveClientRequest r) => new(
        r.Type,
        Collapse(r.Name) ?? "",
        Collapse(r.Mobile) ?? "",
        Clean(r.Email)?.ToLowerInvariant(),
        Clean(r.Gstin)?.Replace(" ", "").ToUpperInvariant(),
        Clean(r.Pan)?.Replace(" ", "").ToUpperInvariant(),
        Clean(r.Address),
        Collapse(r.City),
        Collapse(r.State),
        Clean(r.Pincode)?.Replace(" ", ""),
        Clean(r.Remarks));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Collapse(string? s) => Clean(s) is { } t ? Spaces().Replace(t, " ") : null;
    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
