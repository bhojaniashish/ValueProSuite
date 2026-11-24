using ValuPro.Domain.Banks;
using ValuPro.Domain.Rates;

namespace ValuPro.Domain.Valuations;

public enum ValuationMethod
{
    /// <summary>Land value plus depreciated building value (houses, bungalows, plots, sheds).</summary>
    LandAndBuilding = 0,
    /// <summary>Area × composite market rate with ± adjustments (flats, offices, shops).</summary>
    Composite = 1,
}

/// <summary>Which area the composite rate is applied to.</summary>
public enum AreaBasis
{
    Carpet = 0,
    BuiltUp = 1,
    SuperBuiltUp = 2,
}

/// <summary>One floor or structure of the building (land &amp; building method). Areas in sq.ft, rates per sq.ft.</summary>
public sealed class BuildingLine
{
    public string? Label { get; set; }
    public decimal AreaSqFt { get; set; }
    /// <summary>Replacement (construction) cost per sq.ft.</summary>
    public decimal RatePerSqFt { get; set; }
    public decimal AgeYears { get; set; }
    public int LifeYears { get; set; } = 60;
    public decimal SalvagePercent { get; set; } = 10;
    public DepreciationMethod Method { get; set; }
}

/// <summary>A ± percentage on the market value (floor rise, view, poor access…).</summary>
public sealed class Adjustment
{
    public string? Label { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>An amount added on top (compound wall, borewell, solar plant, car parking sold separately…).</summary>
public sealed class Extra
{
    public string? Label { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Everything the valuer decides. Stored as JSON with the valuation; the result is always recalculated from it.</summary>
public sealed class ValuationInput
{
    public ValuationMethod Method { get; set; }

    // land & building
    public decimal LandAreaSqFt { get; set; }
    public decimal LandRatePerSqFt { get; set; }
    public List<BuildingLine> Building { get; set; } = [];

    // composite
    public AreaBasis AreaBasis { get; set; } = AreaBasis.Carpet;
    public decimal AreaSqFt { get; set; }
    public decimal CompositeRatePerSqFt { get; set; }
    /// <summary>For the insurance value of a flat: rebuild cost per sq.ft of built-up area.</summary>
    public decimal ConstructionRatePerSqFt { get; set; }
    public decimal BuiltUpSqFt { get; set; }

    public List<Adjustment> Adjustments { get; set; } = [];
    public List<Extra> Extras { get; set; } = [];

    // government (guideline / circle / Jantri) value
    public decimal CircleRatePerSqM { get; set; }
    /// <summary>Area the circle rate applies to, sq.ft (land for plots and houses, built-up for flats).</summary>
    public decimal GuidelineAreaSqFt { get; set; }

    // bank's factors
    public decimal RealizablePercent { get; set; } = 90;
    public decimal DistressPercent { get; set; } = 80;

    // checks against the Rate Master and the documents
    public decimal? MarketRateMin { get; set; }
    public decimal? MarketRateMax { get; set; }
    public decimal? DocumentAreaSqFt { get; set; }
    public decimal? MeasuredAreaSqFt { get; set; }

    public string? Remarks { get; set; }
}

public sealed record Line(string Label, string? Working, decimal Amount);

public sealed record ValuationResult(
    decimal LandValue,
    decimal ReplacementCost,
    decimal Depreciation,
    decimal BuildingValue,
    decimal CompositeValue,
    decimal AdjustmentAmount,
    decimal ExtrasAmount,
    decimal MarketValueBeforeRounding,
    decimal FairMarketValue,
    decimal RealizableValue,
    decimal DistressValue,
    decimal GuidelineValue,
    decimal InsuranceValue,
    decimal RatePerSqFt,
    string FairMarketValueInWords,
    IReadOnlyList<Line> Working,
    IReadOnlyList<string> Warnings);

public static class ValuationCalculator
{
    public const decimal SqFtPerSqM = 10.7639m;
    /// <summary>Market value is rounded to the nearest ₹1,000, as reports state it.</summary>
    public const decimal RoundTo = 1000m;
    /// <summary>Flag when the market value is this far below the government (guideline) value.</summary>
    public const decimal BelowGuidelinePercent = 10m;
    /// <summary>Flag when measured and document areas differ by more than this.</summary>
    public const decimal AreaDifferencePercent = 10m;

    public static ValuationResult Calculate(ValuationInput i)
    {
        var lines = new List<Line>();
        var warnings = new List<string>();

        decimal land = 0, replacement = 0, depreciation = 0, building = 0, composite = 0, area;
        if (i.Method == ValuationMethod.LandAndBuilding)
        {
            land = R(i.LandAreaSqFt * i.LandRatePerSqFt);
            if (land > 0) lines.Add(new("Land", $"{N(i.LandAreaSqFt)} sq.ft × ₹{N(i.LandRatePerSqFt)}", land));
            foreach (var b in i.Building.Where(b => b.AreaSqFt > 0))
            {
                var cost = R(b.AreaSqFt * b.RatePerSqFt);
                var pct = DepreciationMath.Percent(b.Method, b.AgeYears, b.LifeYears, b.SalvagePercent);
                var dep = R(cost * pct / 100);
                replacement += cost;
                depreciation += dep;
                var name = string.IsNullOrWhiteSpace(b.Label) ? "Building" : b.Label.Trim();
                lines.Add(new(name, $"{N(b.AreaSqFt)} sq.ft × ₹{N(b.RatePerSqFt)} = ₹{N(cost)}, less {N(pct)}% for {N(b.AgeYears)} of {b.LifeYears} years", cost - dep));
            }
            building = replacement - depreciation;
            area = i.Building.Sum(b => b.AreaSqFt) is > 0 and var built ? built : i.LandAreaSqFt;
        }
        else
        {
            composite = R(i.AreaSqFt * i.CompositeRatePerSqFt);
            lines.Add(new($"{Basis(i.AreaBasis)} area", $"{N(i.AreaSqFt)} sq.ft × ₹{N(i.CompositeRatePerSqFt)}", composite));
            area = i.AreaSqFt;
        }

        var baseValue = land + building + composite;
        decimal adjust = 0;
        foreach (var a in i.Adjustments.Where(a => a.Percent != 0))
        {
            var amt = R(baseValue * a.Percent / 100);
            adjust += amt;
            lines.Add(new(string.IsNullOrWhiteSpace(a.Label) ? "Adjustment" : a.Label.Trim(), $"{(a.Percent > 0 ? "+" : "")}{N(a.Percent)}%", amt));
        }
        var extras = 0m;
        foreach (var e in i.Extras.Where(e => e.Amount != 0))
        {
            extras += R(e.Amount);
            lines.Add(new(string.IsNullOrWhiteSpace(e.Label) ? "Other" : e.Label.Trim(), null, R(e.Amount)));
        }

        var market = Math.Max(0, baseValue + adjust + extras);
        var fmv = Math.Round(market / RoundTo, MidpointRounding.AwayFromZero) * RoundTo;
        var realizable = Math.Round(fmv * i.RealizablePercent / 100 / RoundTo, MidpointRounding.AwayFromZero) * RoundTo;
        var distress = Math.Round(fmv * i.DistressPercent / 100 / RoundTo, MidpointRounding.AwayFromZero) * RoundTo;
        var guideline = R(i.GuidelineAreaSqFt / SqFtPerSqM * i.CircleRatePerSqM);
        var insurance = i.Method == ValuationMethod.LandAndBuilding ? replacement : R(i.BuiltUpSqFt * i.ConstructionRatePerSqFt);
        var ratePerSqFt = area > 0 ? Math.Round(fmv / area, 0) : 0;

        // Checks reviewers and banks look for.
        if (guideline > 0 && fmv > 0 && fmv < guideline * (1 - BelowGuidelinePercent / 100))
            warnings.Add($"Market value is {N(Math.Round((1 - fmv / guideline) * 100, 1))}% below the government (guideline) value of ₹{IndianNumber.Format(guideline)}. Banks usually ask for a reason.");
        var checkedRate = i.Method == ValuationMethod.Composite ? i.CompositeRatePerSqFt : i.LandRatePerSqFt;
        if (i.MarketRateMin is { } min && i.MarketRateMax is { } max && checkedRate > 0 && (checkedRate < min || checkedRate > max))
            warnings.Add($"The rate of ₹{N(checkedRate)}/sq.ft is outside the Rate Master range for the locality (₹{N(min)}–₹{N(max)}).");
        if (i.DocumentAreaSqFt is > 0 and var docs && i.MeasuredAreaSqFt is > 0 and var measured
            && Math.Abs(measured - docs) / docs * 100 > AreaDifferencePercent)
            warnings.Add($"Measured area ({N(measured)} sq.ft) differs from the documents ({N(docs)} sq.ft) by {N(Math.Round(Math.Abs(measured - docs) / docs * 100, 1))}%.");
        if (i.Method == ValuationMethod.LandAndBuilding && i.Building.Any(b => b.AreaSqFt > 0 && b.RatePerSqFt <= 0))
            warnings.Add("A building line has no construction rate.");
        if (fmv <= 0) warnings.Add("No value yet: enter the area and rate.");

        return new ValuationResult(land, replacement, depreciation, building, composite, adjust, extras, market, fmv, realizable, distress,
            guideline, insurance, ratePerSqFt, fmv > 0 ? IndianNumber.ToWords(fmv) : "", lines, warnings);
    }

    public static string Basis(AreaBasis b) => b switch
    {
        AreaBasis.BuiltUp => "Built-up",
        AreaBasis.SuperBuiltUp => "Super built-up",
        _ => "Carpet",
    };

    private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
    private static string N(decimal v) => IndianNumber.Format(v);
}

/// <summary>Indian digit grouping (1,41,38,708) and amounts in words (Rupees One Crore Forty One Lakh … Only).</summary>
public static class IndianNumber
{
    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen",
        "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];
    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Format(decimal v)
    {
        var neg = v < 0;
        v = Math.Abs(v);
        var whole = Math.Truncate(v);
        var frac = v - whole;
        var s = ((long)whole).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (s.Length > 3)
        {
            var last3 = s[^3..];
            var rest = s[..^3];
            var groups = new List<string>();
            while (rest.Length > 2)
            {
                groups.Insert(0, rest[^2..]);
                rest = rest[..^2];
            }
            if (rest.Length > 0) groups.Insert(0, rest);
            s = string.Join(",", groups) + "," + last3;
        }
        if (frac > 0) s += "." + Math.Round(frac * 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
        return (neg ? "-" : "") + s;
    }

    /// <summary>"Rupees One Crore Forty One Lakh Thirty Eight Thousand Seven Hundred Eight Only" (whole rupees).</summary>
    public static string ToWords(decimal amount)
    {
        var n = (long)Math.Round(Math.Abs(amount), 0);
        if (n == 0) return "Rupees Zero Only";
        var parts = new List<string>();
        void Add(long value, string unit)
        {
            if (value > 0) parts.Add(Below100((int)value) + (unit.Length > 0 ? " " + unit : ""));
        }
        var crore = n / 10_000_000;
        n %= 10_000_000;
        if (crore > 0) parts.Add((crore >= 100 ? ToWords(crore)[7..^5] : Below100((int)crore)) + " Crore");
        Add(n / 100_000, "Lakh");
        n %= 100_000;
        Add(n / 1000, "Thousand");
        n %= 1000;
        Add(n / 100, "Hundred");
        Add(n % 100, "");
        return "Rupees " + string.Join(" ", parts) + " Only";
    }

    private static string Below100(int n) => n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
}
