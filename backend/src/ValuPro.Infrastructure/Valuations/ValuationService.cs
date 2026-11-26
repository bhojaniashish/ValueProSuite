using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Valuations;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Rates;
using ValuPro.Domain.Security;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Valuations;

public sealed class ValuationService(AppDbContext db, ICurrentUser me, TimeProvider clock) : IValuationService
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);

    private sealed class CaseRow
    {
        public required Assignment A { get; init; }
        public required Bank Bank { get; init; }
        public required string ValuerName { get; init; }
        public Property? Property { get; init; }
    }

    private IQueryable<CaseRow> Cases()
    {
        var banks = db.Set<Bank>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        var properties = db.Set<Property>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]);
        return from a in db.Set<Assignment>()
               join b in banks on a.BankId equals b.Id
               join u in db.Users on a.ValuerId equals u.Id
               join p in properties on a.PropertyId equals (Guid?)p.Id into ps
               from p in ps.DefaultIfEmpty()
               select new CaseRow { A = a, Bank = b, ValuerName = u.FullName, Property = p };
    }

    // ---------------- rules ----------------

    private bool CanWork(Assignment a) =>
        me.HasPermission(Permissions.Valuation.Manage)
        && (me.UserId == a.ValuerId || me.UserId == a.ReviewerId || me.HasPermission(Permissions.Assignments.Manage));

    private string? ReadOnlyReason(Assignment a)
    {
        if (!CanWork(a)) return "Only the case's valuer, its reviewer or a manager can change the valuation.";
        return a.Status switch
        {
            AssignmentStatus.Cancelled => "This assignment is cancelled.",
            AssignmentStatus.Submitted or AssignmentStatus.InvoiceGenerated => "The report has been submitted, so the valuation is locked.",
            _ => null,
        };
    }

    // ---------------- list ----------------

    public async Task<IReadOnlyList<ValuationListItem>> ListAsync(ValuationQuery q, CancellationToken ct = default)
    {
        var rows = Cases();
        if (q.Mine && me.UserId is { } uid) rows = rows.Where(r => r.A.ValuerId == uid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            rows = rows.Where(r => r.A.Number.Contains(s) || r.A.ApplicantName.Contains(s) || r.A.PropertySummary.Contains(s));
        }
        var joined = from r in rows
                     join v in db.Set<Valuation>() on r.A.Id equals v.AssignmentId into vs
                     from v in vs.DefaultIfEmpty()
                     select new { r.A, r.Bank.ShortCode, r.Bank.BrandColor, r.ValuerName, V = v };

        // To value: the visit is done (or later) and there is no valuation yet. Valued: has one.
        joined = q.Done
            ? joined.Where(x => x.V != null)
            : joined.Where(x => x.V == null && x.A.Status != AssignmentStatus.New && x.A.Status != AssignmentStatus.SiteVisitScheduled
                && x.A.Status != AssignmentStatus.Cancelled);

        var list = await (q.Done ? joined.OrderByDescending(x => x.V!.ValuedAt) : joined.OrderBy(x => x.A.DueDate))
            .Take(200)
            .Select(x => new ValuationListItem(x.A.Id, x.A.Number, x.A.Status, x.ShortCode, x.BrandColor, x.A.ApplicantName, x.A.PropertySummary,
                x.ValuerName, x.A.DueDate, x.V == null ? null : x.V.FairMarketValue, x.V == null ? null : x.V.ValuedAt))
            .ToListAsync(ct);
        return list;
    }

    // ---------------- detail ----------------

    public ValuationResult Calculate(ValuationInput input) => ValuationCalculator.Calculate(input);

    public async Task<Result<ValuationDetail>> GetAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var row = await Cases().FirstOrDefaultAsync(r => r.A.Id == assignmentId, ct);
        if (row is null) return Error.NotFound("Assignment not found.");
        var saved = await db.Set<Valuation>().AsNoTracking().FirstOrDefaultAsync(v => v.AssignmentId == assignmentId, ct);
        var inspection = await db.Set<Inspection>().AsNoTracking().FirstOrDefaultAsync(i => i.AssignmentId == assignmentId, ct);
        var options = await OptionsAsync(row, inspection, ct);

        var suggestions = new List<string>();
        var previous = row.A.PreviousAssignmentId is { } prevId ? await PreviousAsync(prevId, ct) : null;
        var input = saved?.Input ?? Suggest(row, inspection, options, suggestions);
        if (saved is null && IsConstruction(row.A)) input.Progress = SuggestProgress(inspection, previous, suggestions);
        var reason = ReadOnlyReason(row.A);
        var p = row.Property;
        var d = inspection?.Data;
        return new ValuationDetail(
            new ValuationCase(row.A.Id, row.A.Number, row.A.Status, row.Bank.Name, row.Bank.ShortCode, row.Bank.BrandColor, row.A.ApplicantName,
                row.A.PropertySummary, p?.Type, p?.City, row.ValuerName, row.A.CaseType, previous?.Number),
            saved is not null,
            saved?.Version ?? 0,
            saved?.ValuedAt,
            saved?.ValuedByName,
            input,
            ValuationCalculator.Calculate(input),
            new BankFactors(row.Bank.RateBasis, row.Bank.Depreciation, row.Bank.RealizablePercent, row.Bank.DistressPercent),
            new InspectionFacts(
                inspection?.Status == InspectionStatus.Submitted,
                SqFt(d, d?.Measurements.LandAsPerDocuments) ?? p?.LandArea, SqFt(d, d?.Measurements.LandMeasured),
                SqFt(d, d?.Measurements.CarpetAsPerDocuments), SqFt(d, d?.Measurements.CarpetMeasured),
                SqFt(d, d?.Measurements.BuiltUpAsPerDocuments) ?? p?.BuiltUpArea, SqFt(d, d?.Measurements.BuiltUpMeasured),
                SqFt(d, d?.Measurements.SuperBuiltUpAsPerDocuments),
                d?.Building.StructureType, d?.Building.AgeYears, d?.Building.Condition, d?.Remarks.Marketability),
            options,
            saved is null ? suggestions : [],
            reason is null,
            reason);
    }

    private static bool IsConstruction(Assignment a) => a.CaseType is CaseType.StageInspection or CaseType.Reinspection;

    private sealed record Previous(string Number, decimal? Percent, ProgressInput? Progress, ProgressResult? Result);

    /// <summary>The previous visit's completion and progress figures, to carry the cost and disbursements forward.</summary>
    private async Task<Previous?> PreviousAsync(Guid prevId, CancellationToken ct)
    {
        var number = await db.Set<Assignment>().IgnoreQueryFilters([AppDbContext.SoftDeleteFilter]).Where(a => a.Id == prevId).Select(a => a.Number).FirstOrDefaultAsync(ct);
        if (number is null) return null;
        var pi = await db.Set<Inspection>().AsNoTracking().FirstOrDefaultAsync(i => i.AssignmentId == prevId, ct);
        var pv = await db.Set<Valuation>().AsNoTracking().FirstOrDefaultAsync(v => v.AssignmentId == prevId, ct);
        decimal? percent = pi is null ? null : pi.Data.Construction.Stages.Count > 0 ? pi.Data.Construction.OverallPercent() : pi.Data.Building.CompletionPercent;
        var progress = pv?.Input.Progress;
        return new Previous(number, percent, progress, progress is null ? null : ValuationCalculator.Progress(progress));
    }

    /// <summary>
    /// Completion from this visit's stage table; cost, finance share and disbursements carried from the previous visit
    /// (assuming its recommended release was paid).
    /// </summary>
    private static ProgressInput SuggestProgress(Inspection? inspection, Previous? previous, List<string> notes)
    {
        var c = inspection?.Data.Construction;
        var p = new ProgressInput
        {
            CompletionPercent = c is { Stages.Count: > 0 } ? c.OverallPercent() : inspection?.Data.Building.CompletionPercent ?? 0,
            PreviousPercent = previous?.Percent,
            EstimatedCost = previous?.Progress?.EstimatedCost ?? 0,
            FinancePercent = previous?.Progress?.FinancePercent ?? 80,
            DisbursedSoFar = previous?.Progress is { } pp ? pp.DisbursedSoFar + (previous.Result?.RecommendedRelease ?? 0) : 0,
            SanctionedLoan = previous?.Progress?.SanctionedLoan,
        };
        notes.Add($"Completion {IndianNumber.Format(p.CompletionPercent)}% from this visit's stage table.");
        if (previous?.Progress is not null)
            notes.Add($"Estimated cost, finance share and disbursements carried from {previous.Number}, assuming its recommended release of ₹{IndianNumber.Format(previous.Result!.RecommendedRelease)} was paid. Correct the amount disbursed if not.");
        else
            notes.Add("Enter the estimated cost of construction and the amount disbursed so far (from the bank's sanction letter).");
        return p;
    }

    /// <summary>Inspection areas are in the unit the valuer chose; valuations work in sq.ft.</summary>
    private static decimal? SqFt(InspectionData? d, decimal? v) =>
        v is null ? null : d?.Measurements.Unit == "SqM" ? Math.Round(v.Value * ValuationCalculator.SqFtPerSqM, 2) : v;

    private async Task<ValuationOptions> OptionsAsync(CaseRow row, Inspection? inspection, CancellationToken ct)
    {
        var today = Today;
        var city = row.Property?.City;
        var haystack = $"{row.Property?.Address} {row.A.PropertySummary}".ToLowerInvariant();

        // Current rate = the latest one already in force for each locality / type.
        var locations = (await db.Set<LocationRate>().Where(r => r.EffectiveFrom <= today && (city == null || r.City == city)).ToListAsync(ct))
            .GroupBy(r => (r.City, r.Locality, r.Zone)).Select(g => g.OrderByDescending(r => r.EffectiveFrom).First()).ToList();
        var circles = (await db.Set<CircleRate>().Where(r => r.EffectiveFrom <= today && (city == null || r.City == city)).ToListAsync(ct))
            .GroupBy(r => (r.City, r.Locality, r.Scheme)).Select(g => g.OrderByDescending(r => r.EffectiveFrom).First()).ToList();
        var construction = (await db.Set<ConstructionRate>().Where(r => r.EffectiveFrom <= today).ToListAsync(ct))
            .GroupBy(r => r.ConstructionType).Select(g => g.OrderByDescending(r => r.EffectiveFrom).First()).OrderBy(r => r.ConstructionType).ToList();
        var rules = await db.Set<DepreciationRule>().OrderBy(r => r.StructureType).ToListAsync(ct);

        var zone = row.Property?.Type is PropertyType.CommercialShop or PropertyType.OfficeSpace ? RateZone.Commercial
            : row.Property?.Type is PropertyType.IndustrialShed or PropertyType.Warehouse ? RateZone.Industrial
            : row.Property?.Type is PropertyType.AgriculturalLand ? RateZone.Agricultural : RateZone.Residential;
        var match = locations.Where(l => haystack.Contains(l.Locality.ToLowerInvariant()))
            .OrderByDescending(l => l.Zone == zone).ThenByDescending(l => l.Locality.Length).FirstOrDefault();

        var structure = inspection?.Data.Building.StructureType?.ToLowerInvariant() ?? "";
        var key = structure.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "rcc";
        var pick = construction.FirstOrDefault(c => c.ConstructionType.ToLowerInvariant().Contains(key)) ?? construction.FirstOrDefault();

        decimal? PerSqFt(ConstructionRate c, decimal? v) => v is null ? null : c.Unit == RateUnit.SqM ? Math.Round(v.Value / ValuationCalculator.SqFtPerSqM, 0) : v;

        return new ValuationOptions(
            locations.OrderBy(l => l.Locality).Select(l =>
            {
                var c = circles.FirstOrDefault(x => x.Locality.Equals(l.Locality, StringComparison.OrdinalIgnoreCase));
                return new LocationOption(l.Id, l.Locality, l.City, l.Zone, l.MinRate, l.MaxRate, c?.Residential, c?.Commercial, c?.OpenLand, c?.Scheme,
                    l.EffectiveFrom, l.Id == match?.Id);
            }).ToList(),
            construction.Select(c => new ConstructionOption(c.Id, c.ConstructionType, PerSqFt(c, c.CpwdRate), PerSqFt(c, c.PwdRate), PerSqFt(c, c.BankRate), c.Id == pick?.Id)).ToList(),
            rules.Select(r => new DepreciationOption(r.StructureType, r.TotalLifeYears, r.SalvagePercent)).ToList());
    }

    /// <summary>A starting point from the inspection, the Rate Master and the bank's rules; the valuer changes what they need.</summary>
    private static ValuationInput Suggest(CaseRow row, Inspection? inspection, ValuationOptions o, List<string> notes)
    {
        var p = row.Property;
        var d = inspection?.Data;
        var bank = row.Bank;
        var type = p?.Type;
        var i = new ValuationInput
        {
            Method = type is PropertyType.ResidentialFlat or PropertyType.OfficeSpace or PropertyType.CommercialShop || type is null && d?.Measurements.LandMeasured is null
                ? ValuationMethod.Composite : ValuationMethod.LandAndBuilding,
            RealizablePercent = bank.RealizablePercent,
            DistressPercent = bank.DistressPercent,
        };
        notes.Add($"Realizable {IndianNumber.Format(bank.RealizablePercent)}% and distress {IndianNumber.Format(bank.DistressPercent)}% from {bank.ShortCode}'s rules.");

        // The lower of measured and document area, as most banks require.
        decimal? Lower(decimal? docs, decimal? measured) =>
            docs is > 0 && measured is > 0 ? Math.Min(docs.Value, measured.Value) : measured is > 0 ? measured : docs is > 0 ? docs : null;
        var m = d?.Measurements;
        var land = Lower(SqFt(d, m?.LandAsPerDocuments) ?? p?.LandArea, SqFt(d, m?.LandMeasured));
        var carpet = Lower(SqFt(d, m?.CarpetAsPerDocuments), SqFt(d, m?.CarpetMeasured));
        var builtUp = Lower(SqFt(d, m?.BuiltUpAsPerDocuments) ?? p?.BuiltUpArea, SqFt(d, m?.BuiltUpMeasured));

        var loc = o.Locations.FirstOrDefault(l => l.Suggested);
        var midpoint = loc is null ? 0 : Math.Round((loc.MinRate + loc.MaxRate) / 2 / 50, MidpointRounding.AwayFromZero) * 50;
        if (loc is not null)
        {
            i.MarketRateMin = loc.MinRate;
            i.MarketRateMax = loc.MaxRate;
            notes.Add($"Market rate ₹{IndianNumber.Format(midpoint)}/sq.ft: middle of the {loc.Locality} band (₹{IndianNumber.Format(loc.MinRate)}–₹{IndianNumber.Format(loc.MaxRate)}) in the Rate Master.");
        }
        else
        {
            notes.Add("No Rate Master entry matched the property's locality; choose one or type the rate.");
        }

        var con = o.Construction.FirstOrDefault(c => c.Suggested);
        // Bank rates for banks that ask for them, otherwise PWD (state schedule), then CPWD.
        var conRate = con is null ? 0 : bank.RateBasis == RateBasis.BankRates
            ? con.BankPerSqFt ?? con.PwdPerSqFt ?? con.CpwdPerSqFt ?? 0
            : con.PwdPerSqFt ?? con.CpwdPerSqFt ?? con.BankPerSqFt ?? 0;
        if (con is not null) notes.Add($"Construction ₹{IndianNumber.Format(conRate)}/sq.ft: {con.ConstructionType} ({(bank.RateBasis == RateBasis.BankRates ? "bank" : "PWD")} rate).");

        var rule = o.Depreciation.FirstOrDefault(r => string.Equals(r.StructureType, d?.Building.StructureType, StringComparison.OrdinalIgnoreCase));
        var age = d?.Building.AgeYears ?? 0;

        if (i.Method == ValuationMethod.Composite)
        {
            (i.AreaBasis, i.AreaSqFt) = carpet is > 0 ? (AreaBasis.Carpet, carpet.Value)
                : builtUp is > 0 ? (AreaBasis.BuiltUp, builtUp.Value)
                : (AreaBasis.SuperBuiltUp, SqFt(d, m?.SuperBuiltUpAsPerDocuments) ?? 0);
            i.CompositeRatePerSqFt = midpoint;
            i.BuiltUpSqFt = builtUp ?? 0;
            i.ConstructionRatePerSqFt = conRate;
            i.GuidelineAreaSqFt = builtUp ?? i.AreaSqFt;
            (i.DocumentAreaSqFt, i.MeasuredAreaSqFt) = i.AreaBasis == AreaBasis.Carpet
                ? (SqFt(d, m?.CarpetAsPerDocuments), SqFt(d, m?.CarpetMeasured))
                : (SqFt(d, m?.BuiltUpAsPerDocuments) ?? p?.BuiltUpArea, SqFt(d, m?.BuiltUpMeasured));
            if (i.AreaSqFt > 0) notes.Add($"{ValuationCalculator.Basis(i.AreaBasis)} area {IndianNumber.Format(i.AreaSqFt)} sq.ft: the lower of documents and measured.");
        }
        else
        {
            i.LandAreaSqFt = land ?? 0;
            i.LandRatePerSqFt = midpoint;
            i.GuidelineAreaSqFt = land ?? 0;
            (i.DocumentAreaSqFt, i.MeasuredAreaSqFt) = (SqFt(d, m?.LandAsPerDocuments) ?? p?.LandArea, SqFt(d, m?.LandMeasured));
            if (land > 0) notes.Add($"Land {IndianNumber.Format(land.Value)} sq.ft: the lower of documents and measured.");

            if (type is not (PropertyType.OpenPlot or PropertyType.AgriculturalLand))
            {
                BuildingLine Line(string label, decimal area) => new()
                {
                    Label = label, AreaSqFt = area, RatePerSqFt = conRate, AgeYears = age,
                    LifeYears = rule?.LifeYears ?? 60, SalvagePercent = rule?.SalvagePercent ?? 10, Method = bank.Depreciation,
                };
                var floors = m?.Floors.Where(f => (f.Measured ?? f.AsPerDocuments) is > 0).ToList() ?? [];
                i.Building = floors.Count > 0
                    ? floors.Select(f => Line(f.Name ?? "Floor", SqFt(d, Lower(f.AsPerDocuments, f.Measured)) ?? 0)).ToList()
                    : builtUp is > 0 ? [Line("Building", builtUp.Value)] : [];
                if (i.Building.Count > 0)
                    notes.Add(rule is null
                        ? $"Age {age} years from the inspection; life 60 years and salvage 10% (no depreciation rule matched the structure)."
                        : $"Age {age} years from the inspection; {rule.StructureType}: {rule.LifeYears} years life, {IndianNumber.Format(rule.SalvagePercent)}% salvage, {(bank.Depreciation == DepreciationMethod.WrittenDownValue ? "written-down value" : "straight line")} as {bank.ShortCode} uses.");
            }
        }

        // Government value: residential, commercial or open-land circle rate for the locality.
        if (loc is not null)
        {
            i.CircleRatePerSqM = type is PropertyType.OpenPlot or PropertyType.AgriculturalLand ? loc.CircleOpenLand ?? 0
                : type is PropertyType.CommercialShop or PropertyType.OfficeSpace ? loc.CircleCommercial ?? loc.CircleResidential ?? 0
                : loc.CircleResidential ?? 0;
            if (i.CircleRatePerSqM > 0) notes.Add($"Guideline value at the {loc.Locality} circle rate of ₹{IndianNumber.Format(i.CircleRatePerSqM)}/sq.m.");
        }
        return i;
    }

    // ---------------- save ----------------

    public async Task<Result<ValuationDetail>> SaveAsync(Guid assignmentId, SaveValuationRequest r, CancellationToken ct = default)
    {
        var a = await db.Set<Assignment>().FirstOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (a is null) return Error.NotFound("Assignment not found.");
        if (ReadOnlyReason(a) is { } reason) return Error.Forbidden(reason);

        var result = ValuationCalculator.Calculate(r.Input);
        var hasProgress = result.Progress is not null && r.Input.Progress!.EstimatedCost > 0;
        if (result.FairMarketValue <= 0 && !hasProgress)
            return Error.Validation(IsConstruction(a) ? "Enter the estimated cost (and completion) so there is a release to save." : "Enter the area and rate so there is a value to save.");

        var v = await db.Set<Valuation>().FirstOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        if (v is null)
        {
            if (r.Version != 0) return Error.Conflict("This valuation is no longer on the server. Reload to start again.");
            v = new Valuation { AssignmentId = assignmentId, ValuedByName = me.Name ?? "Unknown", Version = 0 };
            db.Add(v);
        }
        else if (v.Version != r.Version)
        {
            return Error.Conflict("Someone else saved this valuation since you opened it. Reload to see their figures.");
        }

        v.Input = r.Input;
        v.Apply(result);
        v.ValuedAt = clock.GetUtcNow();
        v.ValuedByName = me.Name ?? "Unknown";
        v.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("Someone else saved this valuation since you opened it. Reload to see their figures.");
        }
        catch (DbUpdateException) when (v.Version == 1)
        {
            return Error.Conflict("Someone else started this valuation at the same time. Reload to see it.");
        }
        return await GetAsync(assignmentId, ct);
    }
}
