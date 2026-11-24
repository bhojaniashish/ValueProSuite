using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Common;
using ValuPro.Application.Rates;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Rates;
using ValuPro.Infrastructure.Import;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Rates;

/// <summary>
/// Rate Master. Market, circle and construction rates are dated: a new row with a later
/// "effective from" date supersedes the older one for the same locality / type, and the
/// older row is kept as history. "Current" means the latest row in effect today (India date).
/// </summary>
public sealed class RateService(
    AppDbContext db,
    TimeProvider clock,
    IValidator<SaveLocationRate> locationValidator,
    IValidator<SaveCircleRate> circleValidator,
    IValidator<SaveConstructionRate> constructionValidator) : IRateService
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(5.5)).DateTime);

    // ---------------- filters ----------------

    public async Task<RateFilters> GetFiltersAsync(CancellationToken ct = default)
    {
        var cities = await db.Set<LocationRate>().Select(r => r.City)
            .Union(db.Set<CircleRate>().Select(r => r.City)).Distinct().OrderBy(c => c).ToListAsync(ct);
        var states = await db.Set<CircleRate>().Select(r => r.State)
            .Union(db.Set<LocationRate>().Where(r => r.State != null).Select(r => r.State!)).Distinct().OrderBy(s => s).ToListAsync(ct);
        return new RateFilters(cities, states,
            await db.Set<LocationRate>().CountAsync(ct), await db.Set<CircleRate>().CountAsync(ct),
            await db.Set<ConstructionRate>().CountAsync(ct), await db.Set<DepreciationRule>().CountAsync(ct));
    }

    // ---------------- location ----------------

    private IQueryable<LocationRate> CurrentLocation()
    {
        var today = Today;
        var all = db.Set<LocationRate>();
        return all.Where(r => r.EffectiveFrom <= today && !all.Any(o =>
            o.City == r.City && o.Locality == r.Locality && o.Zone == r.Zone && o.EffectiveFrom > r.EffectiveFrom && o.EffectiveFrom <= today));
    }

    public async Task<PagedResult<LocationRateItem>> ListLocationAsync(RateQuery q, CancellationToken ct = default)
    {
        var rows = q.CurrentOnly ? CurrentLocation() : db.Set<LocationRate>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Search)) { var s = q.Search.Trim(); rows = rows.Where(r => r.Locality.Contains(s) || r.City.Contains(s) || r.Source!.Contains(s)); }
        if (!string.IsNullOrWhiteSpace(q.City)) rows = rows.Where(r => r.City == q.City);
        if (!string.IsNullOrWhiteSpace(q.State)) rows = rows.Where(r => r.State == q.State);
        if (q.Zone is { } zone) rows = rows.Where(r => r.Zone == zone);

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(r => r.City).ThenBy(r => r.Locality).ThenBy(r => r.Zone).ThenByDescending(r => r.EffectiveFrom)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).AsNoTracking().ToListAsync(ct);

        var ids = page.Select(r => r.Id).ToList();
        var current = await CurrentLocation().Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);

        // Matching current circle rate for the same city and locality.
        var localities = page.Select(r => r.Locality).Distinct().ToList();
        var circles = await CurrentCircle().Where(c => localities.Contains(c.Locality)).AsNoTracking().ToListAsync(ct);

        return new PagedResult<LocationRateItem>([.. page.Select(r =>
        {
            var c = circles.FirstOrDefault(x => Same(x.City, r.City) && Same(x.Locality, r.Locality));
            var perSqM = c is null ? null : r.Zone is RateZone.Commercial ? c.Commercial ?? c.Residential : r.Zone is RateZone.Agricultural ? c.OpenLand ?? c.Residential : c.Residential ?? c.Commercial;
            return new LocationRateItem(r.Id, r.Locality, r.City, r.State, r.Zone, r.MinRate, r.MaxRate, r.EffectiveFrom, r.Source,
                current.Contains(r.Id), perSqM, c?.Scheme);
        })], total, q.Page, q.PageSize);
    }

    public async Task<Result<LocationRateItem>> SaveLocationAsync(Guid? id, SaveLocationRate r, CancellationToken ct = default)
    {
        var e = id is { } existingId ? await db.Set<LocationRate>().FirstOrDefaultAsync(x => x.Id == existingId, ct) : new LocationRate { Locality = "", City = "" };
        if (e is null) return Error.NotFound("Rate not found.");
        var locality = Clean(r.Locality)!; var city = Clean(r.City)!;
        if (await db.Set<LocationRate>().AnyAsync(x => x.Id != e.Id && x.City == city && x.Locality == locality && x.Zone == r.Zone && x.EffectiveFrom == r.EffectiveFrom, ct))
            return Error.Conflict($"{locality}, {city} ({r.Zone}) already has a rate from {r.EffectiveFrom:dd MMM yyyy}. Edit that one instead.", "effectiveFrom");

        (e.Locality, e.City, e.State, e.Zone, e.MinRate, e.MaxRate, e.EffectiveFrom, e.Source) =
            (locality, city, Clean(r.State), r.Zone, r.MinRate, r.MaxRate, r.EffectiveFrom, Clean(r.Source));
        if (id is null) db.Add(e);
        await db.SaveChangesAsync(ct);
        return (await ListLocationAsync(new RateQuery(CurrentOnly: false, PageSize: 500, Search: e.Locality, City: e.City), ct)).Items.First(x => x.Id == e.Id);
    }

    // ---------------- circle ----------------

    private IQueryable<CircleRate> CurrentCircle()
    {
        var today = Today;
        var all = db.Set<CircleRate>();
        return all.Where(r => r.EffectiveFrom <= today && !all.Any(o =>
            o.State == r.State && o.Scheme == r.Scheme && o.City == r.City && o.Locality == r.Locality &&
            o.EffectiveFrom > r.EffectiveFrom && o.EffectiveFrom <= today));
    }

    public async Task<PagedResult<CircleRateItem>> ListCircleAsync(RateQuery q, CancellationToken ct = default)
    {
        var rows = q.CurrentOnly ? CurrentCircle() : db.Set<CircleRate>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Search)) { var s = q.Search.Trim(); rows = rows.Where(r => r.Locality.Contains(s) || r.City.Contains(s) || r.Reference!.Contains(s)); }
        if (!string.IsNullOrWhiteSpace(q.City)) rows = rows.Where(r => r.City == q.City);
        if (!string.IsNullOrWhiteSpace(q.State)) rows = rows.Where(r => r.State == q.State);
        if (q.Scheme is { } scheme) rows = rows.Where(r => r.Scheme == scheme);

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(r => r.State).ThenBy(r => r.City).ThenBy(r => r.Locality).ThenByDescending(r => r.EffectiveFrom)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).AsNoTracking().ToListAsync(ct);
        var ids = page.Select(r => r.Id).ToList();
        var current = await CurrentCircle().Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        return new PagedResult<CircleRateItem>([.. page.Select(r => ToItem(r, current.Contains(r.Id)))], total, q.Page, q.PageSize);
    }

    private static CircleRateItem ToItem(CircleRate r, bool current) =>
        new(r.Id, r.State, r.Scheme, r.City, r.Locality, r.Residential, r.Commercial, r.OpenLand, r.EffectiveFrom, r.Reference, current);

    public async Task<Result<CircleRateItem>> SaveCircleAsync(Guid? id, SaveCircleRate r, CancellationToken ct = default)
    {
        var e = id is { } existingId ? await db.Set<CircleRate>().FirstOrDefaultAsync(x => x.Id == existingId, ct) : new CircleRate { State = "", City = "", Locality = "" };
        if (e is null) return Error.NotFound("Rate not found.");
        var (state, city, locality) = (Clean(r.State)!, Clean(r.City)!, Clean(r.Locality)!);
        if (await db.Set<CircleRate>().AnyAsync(x => x.Id != e.Id && x.State == state && x.Scheme == r.Scheme && x.City == city && x.Locality == locality && x.EffectiveFrom == r.EffectiveFrom, ct))
            return Error.Conflict($"{locality}, {city} already has a {r.Scheme} rate from {r.EffectiveFrom:dd MMM yyyy}. Edit that one instead.", "effectiveFrom");

        (e.State, e.Scheme, e.City, e.Locality, e.Residential, e.Commercial, e.OpenLand, e.EffectiveFrom, e.Reference) =
            (state, r.Scheme, city, locality, r.Residential, r.Commercial, r.OpenLand, r.EffectiveFrom, Clean(r.Reference));
        if (id is null) db.Add(e);
        await db.SaveChangesAsync(ct);
        return ToItem(e, await CurrentCircle().AnyAsync(x => x.Id == e.Id, ct));
    }

    // ---------------- construction ----------------

    private IQueryable<ConstructionRate> CurrentConstruction()
    {
        var today = Today;
        var all = db.Set<ConstructionRate>();
        return all.Where(r => r.EffectiveFrom <= today && !all.Any(o =>
            o.ConstructionType == r.ConstructionType && o.Unit == r.Unit && o.EffectiveFrom > r.EffectiveFrom && o.EffectiveFrom <= today));
    }

    public async Task<PagedResult<ConstructionRateItem>> ListConstructionAsync(RateQuery q, CancellationToken ct = default)
    {
        var rows = q.CurrentOnly ? CurrentConstruction() : db.Set<ConstructionRate>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Search)) { var s = q.Search.Trim(); rows = rows.Where(r => r.ConstructionType.Contains(s) || r.Remarks!.Contains(s)); }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(r => r.ConstructionType).ThenByDescending(r => r.EffectiveFrom)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).AsNoTracking().ToListAsync(ct);
        var ids = page.Select(r => r.Id).ToList();
        var current = await CurrentConstruction().Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        return new PagedResult<ConstructionRateItem>([.. page.Select(r => ToItem(r, current.Contains(r.Id)))], total, q.Page, q.PageSize);
    }

    private static ConstructionRateItem ToItem(ConstructionRate r, bool current) =>
        new(r.Id, r.ConstructionType, r.Unit, r.CpwdRate, r.PwdRate, r.BankRate, r.EffectiveFrom, r.Remarks, current);

    public async Task<Result<ConstructionRateItem>> SaveConstructionAsync(Guid? id, SaveConstructionRate r, CancellationToken ct = default)
    {
        var e = id is { } existingId ? await db.Set<ConstructionRate>().FirstOrDefaultAsync(x => x.Id == existingId, ct) : new ConstructionRate { ConstructionType = "" };
        if (e is null) return Error.NotFound("Rate not found.");
        var type = Clean(r.ConstructionType)!;
        if (await db.Set<ConstructionRate>().AnyAsync(x => x.Id != e.Id && x.ConstructionType == type && x.Unit == r.Unit && x.EffectiveFrom == r.EffectiveFrom, ct))
            return Error.Conflict($"{type} already has a rate from {r.EffectiveFrom:dd MMM yyyy}. Edit that one instead.", "effectiveFrom");

        (e.ConstructionType, e.Unit, e.CpwdRate, e.PwdRate, e.BankRate, e.EffectiveFrom, e.Remarks) =
            (type, r.Unit, r.CpwdRate, r.PwdRate, r.BankRate, r.EffectiveFrom, Clean(r.Remarks));
        if (id is null) db.Add(e);
        await db.SaveChangesAsync(ct);
        return ToItem(e, await CurrentConstruction().AnyAsync(x => x.Id == e.Id, ct));
    }

    // ---------------- depreciation ----------------

    /// <summary>Age bands shown in the prototype; the % in each is taken at the band's upper age.</summary>
    private static readonly (decimal From, decimal To)[] Bands = [(0, 5), (5, 10), (10, 20), (20, 30), (30, 40), (40, 50), (50, 60)];

    private static DepreciationRuleItem ToItem(DepreciationRule r)
    {
        var bands = Bands.Where(b => b.From < r.TotalLifeYears)
            .Select(b => new DepreciationBand($"{b.From:0} – {Math.Min(b.To, r.TotalLifeYears):0} years", b.From, Math.Min(b.To, r.TotalLifeYears),
                r.PercentAt(Math.Min(b.To, r.TotalLifeYears))))
            .Append(new DepreciationBand($"{r.TotalLifeYears}+ years", r.TotalLifeYears, r.TotalLifeYears, r.PercentAt(r.TotalLifeYears)))
            .ToList();
        return new DepreciationRuleItem(r.Id, r.StructureType, r.TotalLifeYears, r.SalvagePercent, r.Method, r.Remarks, bands);
    }

    public async Task<IReadOnlyList<DepreciationRuleItem>> ListDepreciationAsync(CancellationToken ct = default) =>
        [.. (await db.Set<DepreciationRule>().AsNoTracking().OrderByDescending(r => r.TotalLifeYears).ThenBy(r => r.StructureType).ToListAsync(ct)).Select(ToItem)];

    public async Task<Result<DepreciationRuleItem>> SaveDepreciationAsync(Guid? id, SaveDepreciationRule r, CancellationToken ct = default)
    {
        var e = id is { } existingId ? await db.Set<DepreciationRule>().FirstOrDefaultAsync(x => x.Id == existingId, ct) : new DepreciationRule { StructureType = "" };
        if (e is null) return Error.NotFound("Rule not found.");
        var type = Clean(r.StructureType)!;
        if (await db.Set<DepreciationRule>().AnyAsync(x => x.Id != e.Id && x.StructureType == type, ct))
            return Error.Conflict($"There is already a rule for {type}.", "structureType");

        (e.StructureType, e.TotalLifeYears, e.SalvagePercent, e.Method, e.Remarks) = (type, r.TotalLifeYears, r.SalvagePercent, r.Method, Clean(r.Remarks));
        if (id is null) db.Add(e);
        await db.SaveChangesAsync(ct);
        return ToItem(e);
    }

    /// <summary>Common starting points; firms adjust them to their banks' norms.</summary>
    public static readonly IReadOnlyList<SaveDepreciationRule> Standard =
    [
        new("RCC Framed Structure", 60, 10m, DepreciationMethod.StraightLine, "Typical bank norm for RCC framed buildings."),
        new("Load Bearing Structure", 50, 10m, DepreciationMethod.StraightLine, "Brick / stone masonry walls carrying the roof."),
        new("Steel / Industrial Shed", 40, 5m, DepreciationMethod.StraightLine, "Steel frame with sheet roofing."),
        new("Temporary / Tin Shed", 20, 0m, DepreciationMethod.StraightLine, null),
    ];

    public async Task<IReadOnlyList<DepreciationRuleItem>> AddStandardDepreciationAsync(CancellationToken ct = default)
    {
        var existing = await db.Set<DepreciationRule>().Select(r => r.StructureType).ToListAsync(ct);
        foreach (var s in Standard.Where(s => !existing.Contains(s.StructureType, StringComparer.OrdinalIgnoreCase)))
            db.Add(new DepreciationRule { StructureType = s.StructureType, TotalLifeYears = s.TotalLifeYears, SalvagePercent = s.SalvagePercent, Method = s.Method, Remarks = s.Remarks });
        await db.SaveChangesAsync(ct);
        return await ListDepreciationAsync(ct);
    }

    // ---------------- delete ----------------

    public async Task<Result<bool>> DeleteAsync(string kind, Guid id, CancellationToken ct = default)
    {
        object? e = kind.ToLowerInvariant() switch
        {
            "location" => await db.Set<LocationRate>().FirstOrDefaultAsync(x => x.Id == id, ct),
            "circle" => await db.Set<CircleRate>().FirstOrDefaultAsync(x => x.Id == id, ct),
            "construction" => await db.Set<ConstructionRate>().FirstOrDefaultAsync(x => x.Id == id, ct),
            "depreciation" => await db.Set<DepreciationRule>().FirstOrDefaultAsync(x => x.Id == id, ct),
            _ => null,
        };
        if (e is null) return Error.NotFound("Rate not found.");
        db.Remove(e); // soft delete
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------- import ----------------

    private static readonly Dictionary<string, string> LocationAliases = Aliases(
        ("Locality", ["locality", "area", "location", "village", "society"]), ("City", ["city", "town", "district"]), ("State", ["state"]),
        ("Zone", ["zone", "type", "usage"]), ("Min", ["minrate", "min", "ratefrom", "from", "lowest", "minimum"]),
        ("Max", ["maxrate", "max", "rateto", "to", "highest", "maximum"]), ("Rate", ["rate", "marketrate"]),
        ("EffectiveFrom", ["effectivefrom", "from date", "date", "wef", "validfrom"]), ("Source", ["source", "remarks", "notes"]));

    private static readonly Dictionary<string, string> CircleAliases = Aliases(
        ("State", ["state"]), ("Scheme", ["scheme", "type", "ratetype"]), ("City", ["city", "district", "taluka", "town"]),
        ("Locality", ["locality", "area", "village", "zone", "location"]), ("Residential", ["residential", "resi", "residentialrate"]),
        ("Commercial", ["commercial", "comm", "commercialrate"]), ("OpenLand", ["openland", "land", "plot", "openplot"]),
        ("EffectiveFrom", ["effectivefrom", "date", "wef", "validfrom", "year"]), ("Reference", ["reference", "notification", "gr", "remarks"]));

    private static readonly Dictionary<string, string> ConstructionAliases = Aliases(
        ("Type", ["constructiontype", "type", "construction", "specification", "description"]), ("Unit", ["unit", "per"]),
        ("CPWD", ["cpwd", "cpwdrate"]), ("PWD", ["pwd", "pwdrate", "statepwd"]), ("Bank", ["bank", "bankrate"]),
        ("EffectiveFrom", ["effectivefrom", "date", "wef", "validfrom"]), ("Remarks", ["remarks", "notes"]));

    private static Dictionary<string, string> Aliases(params (string Column, string[] Names)[] map)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, names) in map)
            foreach (var n in names.Append(column)) d[TabularFile.Normalize(n)] = column;
        return d;
    }

    public string Template(RateKind kind) => kind switch
    {
        RateKind.Location => "Locality,City,State,Zone,Min Rate,Max Rate,Effective From,Source\n" +
                             "Vesu,Surat,Gujarat,Residential,6500,9200,2026-04-01,Broker survey Apr 2026\n" +
                             "Bodakdev,Ahmedabad,Gujarat,Commercial,12000,18000,2026-04-01,\n",
        RateKind.Circle => "State,Scheme,City,Locality,Residential,Commercial,Open Land,Effective From,Reference\n" +
                           "Gujarat,Jantri,Surat,Vesu,52000,78000,,2026-04-01,Jantri 2026\n" +
                           "Maharashtra,Ready Reckoner,Mumbai,Kandivali,115000,168000,,2026-04-01,ASR 2026-27\n",
        _ => "Construction Type,Unit,CPWD,PWD,Bank,Effective From,Remarks\n" +
             "RCC Framed - Standard,sq.ft,1850,1750,1700,2026-04-01,\n" +
             "Compound Wall,r.ft,650,620,600,2026-04-01,\n",
    };

    public async Task<Result<RateImportPreview>> PreviewImportAsync(RateKind kind, Stream file, string fileName, CancellationToken ct = default)
    {
        var (rows, error) = await CheckAsync(kind, file, fileName, ct);
        if (error is not null) return error;
        var valid = rows.Count(r => r.IsValid);
        return new RateImportPreview(kind, rows, valid, rows.Count - valid);
    }

    public async Task<Result<RateImportResult>> ImportAsync(RateKind kind, Stream file, string fileName, CancellationToken ct = default)
    {
        var (rows, error) = await CheckAsync(kind, file, fileName, ct);
        if (error is not null) return error;
        var imported = 0;
        foreach (var row in rows.Where(r => r.IsValid))
        {
            var ok = row.Data switch
            {
                SaveLocationRate l => (await SaveLocationAsync(null, l, ct)).IsSuccess,
                SaveCircleRate c => (await SaveCircleAsync(null, c, ct)).IsSuccess,
                SaveConstructionRate c => (await SaveConstructionAsync(null, c, ct)).IsSuccess,
                _ => false,
            };
            if (ok) imported++;
        }
        return new RateImportResult(imported, rows.Count - imported);
    }

    private async Task<(List<RateImportRow> Rows, Error? Error)> CheckAsync(RateKind kind, Stream file, string fileName, CancellationToken ct)
    {
        var (aliases, required) = kind switch
        {
            RateKind.Location => (LocationAliases, new[] { "Locality", "City" }),
            RateKind.Circle => (CircleAliases, new[] { "State", "City", "Locality" }),
            _ => (ConstructionAliases, new[] { "Type" }),
        };
        var sheet = TabularFile.Read(file, fileName, aliases, required);
        if (sheet.Error is not null) return ([], Error.Validation(sheet.Error, "file"));
        if (sheet.Rows.Count == 0) return ([], Error.Validation("No rows found in the file.", "file"));

        var result = new List<RateImportRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rowNo, v) in sheet.Rows)
        {
            var errors = new List<string>();
            var date = TabularFile.Date(v.GetValueOrDefault("EffectiveFrom")) ?? (v.ContainsKey("EffectiveFrom") ? null : Today);
            if (date is null) errors.Add($"Could not read the date \"{v["EffectiveFrom"]}\". Use 2026-04-01 or 01/04/2026.");
            object? data = null;
            string key = "";

            switch (kind)
            {
                case RateKind.Location:
                {
                    var zone = ParseZone(v.GetValueOrDefault("Zone"), errors);
                    var min = TabularFile.Amount(v.GetValueOrDefault("Min")) ?? TabularFile.Amount(v.GetValueOrDefault("Rate"));
                    var max = TabularFile.Amount(v.GetValueOrDefault("Max")) ?? min;
                    var r = new SaveLocationRate(v.GetValueOrDefault("Locality") ?? "", v.GetValueOrDefault("City") ?? "", v.GetValueOrDefault("State"),
                        zone, min ?? 0, max ?? 0, date ?? Today, v.GetValueOrDefault("Source"));
                    errors.AddRange((await locationValidator.ValidateAsync(r, ct)).Errors.Select(e => e.ErrorMessage));
                    key = $"{r.City}|{r.Locality}|{r.Zone}|{r.EffectiveFrom}";
                    if (errors.Count == 0 && await db.Set<LocationRate>().AnyAsync(x => x.City == r.City.Trim() && x.Locality == r.Locality.Trim() && x.Zone == r.Zone && x.EffectiveFrom == r.EffectiveFrom, ct))
                        errors.Add($"{r.Locality}, {r.City} ({r.Zone}) already has a rate from {r.EffectiveFrom:dd MMM yyyy}.");
                    data = r;
                    break;
                }
                case RateKind.Circle:
                {
                    var scheme = ParseScheme(v.GetValueOrDefault("Scheme"), v.GetValueOrDefault("State"), errors);
                    var r = new SaveCircleRate(v.GetValueOrDefault("State") ?? "", scheme, v.GetValueOrDefault("City") ?? "", v.GetValueOrDefault("Locality") ?? "",
                        TabularFile.Amount(v.GetValueOrDefault("Residential")), TabularFile.Amount(v.GetValueOrDefault("Commercial")),
                        TabularFile.Amount(v.GetValueOrDefault("OpenLand")), date ?? Today, v.GetValueOrDefault("Reference"));
                    errors.AddRange((await circleValidator.ValidateAsync(r, ct)).Errors.Select(e => e.ErrorMessage));
                    key = $"{r.State}|{r.Scheme}|{r.City}|{r.Locality}|{r.EffectiveFrom}";
                    if (errors.Count == 0 && await db.Set<CircleRate>().AnyAsync(x => x.State == r.State.Trim() && x.Scheme == r.Scheme && x.City == r.City.Trim() && x.Locality == r.Locality.Trim() && x.EffectiveFrom == r.EffectiveFrom, ct))
                        errors.Add($"{r.Locality}, {r.City} already has a rate from {r.EffectiveFrom:dd MMM yyyy}.");
                    data = r;
                    break;
                }
                default:
                {
                    var unit = ParseUnit(v.GetValueOrDefault("Unit"), errors);
                    var r = new SaveConstructionRate(v.GetValueOrDefault("Type") ?? "", unit, TabularFile.Amount(v.GetValueOrDefault("CPWD")),
                        TabularFile.Amount(v.GetValueOrDefault("PWD")), TabularFile.Amount(v.GetValueOrDefault("Bank")), date ?? Today, v.GetValueOrDefault("Remarks"));
                    errors.AddRange((await constructionValidator.ValidateAsync(r, ct)).Errors.Select(e => e.ErrorMessage));
                    key = $"{r.ConstructionType}|{r.Unit}|{r.EffectiveFrom}";
                    if (errors.Count == 0 && await db.Set<ConstructionRate>().AnyAsync(x => x.ConstructionType == r.ConstructionType.Trim() && x.Unit == r.Unit && x.EffectiveFrom == r.EffectiveFrom, ct))
                        errors.Add($"{r.ConstructionType} already has a rate from {r.EffectiveFrom:dd MMM yyyy}.");
                    data = r;
                    break;
                }
            }

            if (errors.Count == 0 && !seen.Add(key)) errors.Add("This row repeats an earlier row in the file.");
            result.Add(new RateImportRow(rowNo, data, errors.Distinct().ToList()));
        }
        return (result, null);
    }

    private static RateZone ParseZone(string? s, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(s)) return RateZone.Residential;
        var n = TabularFile.Normalize(s);
        if (n.StartsWith("res")) return RateZone.Residential;
        if (n.StartsWith("com")) return RateZone.Commercial;
        if (n.StartsWith("mix")) return RateZone.MixedUse;
        if (n.StartsWith("ind")) return RateZone.Industrial;
        if (n.StartsWith("agr")) return RateZone.Agricultural;
        errors.Add($"Unknown zone \"{s}\". Use Residential, Commercial, Mixed Use, Industrial or Agricultural.");
        return RateZone.Residential;
    }

    private static CircleScheme ParseScheme(string? s, string? state, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(s))
            return TabularFile.Normalize(state ?? "") switch
            {
                "gujarat" => CircleScheme.Jantri,
                "maharashtra" => CircleScheme.ReadyReckoner,
                "rajasthan" => CircleScheme.DlcRate,
                "karnataka" or "tamilnadu" or "madhyapradesh" or "kerala" => CircleScheme.GuidelineValue,
                _ => CircleScheme.CircleRate,
            };
        var n = TabularFile.Normalize(s);
        if (n.Contains("jantri")) return CircleScheme.Jantri;
        if (n.Contains("ready") || n is "asr" or "rr") return CircleScheme.ReadyReckoner;
        if (n.Contains("guideline")) return CircleScheme.GuidelineValue;
        if (n.Contains("dlc")) return CircleScheme.DlcRate;
        if (n.Contains("circle")) return CircleScheme.CircleRate;
        errors.Add($"Unknown scheme \"{s}\". Use Jantri, Ready Reckoner, Guideline Value, Circle Rate or DLC Rate.");
        return CircleScheme.CircleRate;
    }

    private static RateUnit ParseUnit(string? s, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(s)) return RateUnit.SqFt;
        var n = TabularFile.Normalize(s);
        if (n is "sqft" or "sft" or "squarefeet" or "ft2") return RateUnit.SqFt;
        if (n is "sqm" or "sqmt" or "squaremetre" or "squaremeter" or "m2") return RateUnit.SqM;
        if (n is "rft" or "runningft" or "runningfeet" or "rmt") return RateUnit.RunningFt;
        errors.Add($"Unknown unit \"{s}\". Use sq.ft, sq.m or r.ft.");
        return RateUnit.SqFt;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
