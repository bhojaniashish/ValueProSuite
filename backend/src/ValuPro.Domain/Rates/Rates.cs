using ValuPro.Domain.Banks;
using ValuPro.Domain.Common;

namespace ValuPro.Domain.Rates;

public enum RateZone
{
    Residential = 0,
    Commercial = 1,
    MixedUse = 2,
    Industrial = 3,
    Agricultural = 4,
}

/// <summary>Government guideline-value schemes, by their local names.</summary>
public enum CircleScheme
{
    Jantri = 0,          // Gujarat
    ReadyReckoner = 1,   // Maharashtra
    GuidelineValue = 2,  // Karnataka, Tamil Nadu, Madhya Pradesh…
    CircleRate = 3,      // Delhi, Uttar Pradesh, Haryana…
    DlcRate = 4,         // Rajasthan
}

public enum RateUnit
{
    SqFt = 0,
    SqM = 1,
    RunningFt = 2,
}

/// <summary>Market rate range (₹ per sq.ft) for a locality, as surveyed by the firm. Dated: rates are revised.</summary>
public class LocationRate : TenantEntity
{
    public required string Locality { get; set; }
    public required string City { get; set; }
    public string? State { get; set; }
    public RateZone Zone { get; set; }
    public decimal MinRate { get; set; }
    public decimal MaxRate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Source { get; set; }
}

/// <summary>Government circle / Jantri / ready-reckoner rate (₹ per sq.m) for a locality.</summary>
public class CircleRate : TenantEntity
{
    public required string State { get; set; }
    public CircleScheme Scheme { get; set; }
    public required string City { get; set; }
    public required string Locality { get; set; }
    public decimal? Residential { get; set; }
    public decimal? Commercial { get; set; }
    public decimal? OpenLand { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Cost of construction by type: CPWD, state PWD and bank rates.</summary>
public class ConstructionRate : TenantEntity
{
    public required string ConstructionType { get; set; }
    public RateUnit Unit { get; set; } = RateUnit.SqFt;
    public decimal? CpwdRate { get; set; }
    public decimal? PwdRate { get; set; }
    public decimal? BankRate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>
/// Depreciation rule for a structure type. The age-band table is derived from it, so the two can never disagree.
/// </summary>
public class DepreciationRule : TenantEntity
{
    public required string StructureType { get; set; }
    public int TotalLifeYears { get; set; } = 60;
    /// <summary>Value left at the end of the building's life, % of replacement cost.</summary>
    public decimal SalvagePercent { get; set; } = 10m;
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    public string? Remarks { get; set; }

    /// <summary>Depreciation (% of replacement cost) for a building of the given age.</summary>
    public decimal PercentAt(decimal ageYears) => DepreciationMath.Percent(Method, ageYears, TotalLifeYears, SalvagePercent);
}

public static class DepreciationMath
{
    /// <summary>
    /// Straight line: (age / life) × (100 − salvage), capped at 100 − salvage.
    /// Written-down value: the yearly rate r that leaves the salvage value after the full life, so
    /// 1 − (1 − r)^age = 1 − (salvage/100)^(age/life); capped the same way.
    /// </summary>
    public static decimal Percent(DepreciationMethod method, decimal ageYears, int lifeYears, decimal salvagePercent)
    {
        if (ageYears <= 0 || lifeYears <= 0) return 0m;
        var cap = 100m - salvagePercent;
        if (ageYears >= lifeYears) return Math.Round(cap, 2);

        var fraction = (double)(ageYears / lifeYears);
        var percent = method switch
        {
            DepreciationMethod.WrittenDownValue => 100.0 * (1 - Math.Pow(Math.Max((double)salvagePercent, 0.01) / 100.0, fraction)),
            _ => fraction * (double)cap,
        };
        return Math.Round(Math.Min((decimal)percent, cap), 2);
    }
}
