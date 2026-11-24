using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Assignments;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Application.Properties;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Properties;
using ValuPro.Infrastructure.Assignments;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Properties;

public sealed class PropertyService(
    AppDbContext db,
    INumberSequence numbers,
    IAssignmentUsage usage,
    IAssignmentService assignments) : IPropertyService
{
    public const string SequenceKey = "property";

    private static PropertyItem ToItem(Property p) => new(p.Id, p.Code, p.Type, p.OwnerName, p.OwnerClientId, p.Address, p.City,
        p.State, p.Pincode, p.SurveyNo, p.CtsNo, p.PlotNo, p.LandArea, p.BuiltUpArea, p.Zone, p.Usage, p.Latitude, p.Longitude,
        p.Remarks, p.CreatedAt);

    public async Task<PagedResult<PropertyItem>> ListAsync(PropertyQuery query, CancellationToken ct = default)
    {
        var q = db.Set<Property>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(p => p.Code.Contains(s) || p.OwnerName.Contains(s) || p.Address.Contains(s) || p.City!.Contains(s) ||
                             p.SurveyNo!.Contains(s) || p.CtsNo!.Contains(s) || p.PlotNo!.Contains(s) || p.Pincode!.Contains(s));
        }
        if (query.Type is { } type) q = q.Where(p => p.Type == type);
        if (!string.IsNullOrWhiteSpace(query.City)) q = q.Where(p => p.City == query.City);
        if (!string.IsNullOrWhiteSpace(query.Zone)) q = q.Where(p => p.Zone == query.Zone);

        q = (query.Sort?.ToLowerInvariant(), query.Desc) switch
        {
            ("owner", false) => q.OrderBy(p => p.OwnerName),
            ("owner", true) => q.OrderByDescending(p => p.OwnerName),
            ("type", false) => q.OrderBy(p => p.Type).ThenBy(p => p.Code),
            ("type", true) => q.OrderByDescending(p => p.Type).ThenBy(p => p.Code),
            ("city", false) => q.OrderBy(p => p.City).ThenBy(p => p.Code),
            ("city", true) => q.OrderByDescending(p => p.City).ThenBy(p => p.Code),
            ("land", false) => q.OrderBy(p => p.LandArea),
            ("land", true) => q.OrderByDescending(p => p.LandArea),
            ("code", false) => q.OrderBy(p => p.Code),
            _ => q.OrderByDescending(p => p.Code), // newest first by default
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<PropertyItem>([.. items.Select(ToItem)], total, query.Page, query.PageSize);
    }

    public async Task<PropertyFilters> GetFiltersAsync(CancellationToken ct = default)
    {
        var all = db.Set<Property>();
        var cities = await all.Where(p => p.City != null).Select(p => p.City!).Distinct().OrderBy(c => c).ToListAsync(ct);
        var used = await all.Where(p => p.Zone != null).Select(p => p.Zone!).Distinct().ToListAsync(ct);
        var zones = PropertyZones.Suggested.Concat(used.Except(PropertyZones.Suggested)).ToList();
        return new PropertyFilters(cities, zones, await all.CountAsync(ct));
    }

    public async Task<Result<PropertyDetail>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Set<Property>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("Property not found.");
        var history = await assignments.ListAsync(new AssignmentQuery(PropertyId: id, Sort: "created", Desc: true, PageSize: 50), ct);
        return new PropertyDetail(ToItem(p), history.Items);
    }

    public async Task<Result<PropertyItem>> CreateAsync(SavePropertyRequest r, CancellationToken ct = default)
    {
        if (await CheckOwnerAsync(r.OwnerClientId, ct) is { } error) return error;
        var number = await numbers.NextAsync(SequenceKey, ct);
        var p = new Property { Code = $"PR{number:0000}", OwnerName = "", Address = "" };
        Apply(p, r);
        db.Add(p);
        await db.SaveChangesAsync(ct);
        return ToItem(p);
    }

    public async Task<Result<PropertyItem>> UpdateAsync(Guid id, SavePropertyRequest r, CancellationToken ct = default)
    {
        var p = await db.Set<Property>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("Property not found.");
        if (await CheckOwnerAsync(r.OwnerClientId, ct) is { } error) return error;
        Apply(p, r);
        await db.SaveChangesAsync(ct);
        return ToItem(p);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Set<Property>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("Property not found.");
        if (await usage.OpenForPropertyAsync(id, ct) is > 0 and var open)
            return Error.Conflict(AssignmentUsage.Message($"Property {p.Code}", open));
        db.Remove(p);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<Error?> CheckOwnerAsync(Guid? clientId, CancellationToken ct) =>
        clientId is { } id && !await db.Set<Client>().AnyAsync(c => c.Id == id, ct)
            ? Error.Validation("The selected owner client no longer exists.", "ownerClientId")
            : null;

    private static void Apply(Property p, SavePropertyRequest r)
    {
        p.Type = r.Type;
        p.OwnerName = r.OwnerName.Trim();
        p.OwnerClientId = r.OwnerClientId;
        p.Address = r.Address.Trim();
        p.City = Clean(r.City);
        p.State = Clean(r.State);
        p.Pincode = Clean(r.Pincode);
        p.SurveyNo = Clean(r.SurveyNo);
        p.CtsNo = Clean(r.CtsNo);
        p.PlotNo = Clean(r.PlotNo);
        p.LandArea = r.LandArea;
        p.BuiltUpArea = r.BuiltUpArea;
        p.Zone = Clean(r.Zone);
        p.Usage = r.Usage;
        p.Latitude = r.Latitude is { } lat ? Math.Round(lat, 6) : null;
        p.Longitude = r.Longitude is { } lng ? Math.Round(lng, 6) : null;
        p.Remarks = Clean(r.Remarks);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
