using System.Text;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Rates;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Rates;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Rates;

namespace ValuPro.UnitTests;

public class RateTests
{
    private sealed class FixedTenant(Guid id) : ITenantContext { public Guid TenantId => id; }

    private sealed class NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Email => null;
        public bool HasPermission(string permission) => false;
    }

    /// <summary>15 Jun 2026, India.</summary>
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 6, 15, 6, 0, 0, TimeSpan.Zero);
    }

    private readonly RateService _rates;

    public RateTests()
    {
        var tenant = new FixedTenant(Guid.NewGuid());
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditingInterceptor(new NoUser(), tenant, TimeProvider.System))
            .Options, tenant);
        _rates = new RateService(db, new Clock(), new SaveLocationRateValidator(), new SaveCircleRateValidator(), new SaveConstructionRateValidator());
    }

    private static readonly DateOnly Apr25 = new(2025, 4, 1), Apr26 = new(2026, 4, 1), Apr27 = new(2027, 4, 1);

    [Theory]
    [InlineData(DepreciationMethod.StraightLine, 0, 0)]
    [InlineData(DepreciationMethod.StraightLine, 30, 45)]     // half the life of 90% depreciable
    [InlineData(DepreciationMethod.StraightLine, 60, 90)]     // capped at 100 - salvage
    [InlineData(DepreciationMethod.StraightLine, 80, 90)]
    [InlineData(DepreciationMethod.WrittenDownValue, 60, 90)] // salvage left at end of life
    [InlineData(DepreciationMethod.WrittenDownValue, 30, 68.38)] // 1 - 0.1^0.5
    public void Depreciation_formulas(DepreciationMethod method, int age, double expected) =>
        Assert.Equal((decimal)expected, DepreciationMath.Percent(method, age, 60, 10m));

    [Fact]
    public async Task Newer_rate_supersedes_older_and_future_rates_wait()
    {
        await _rates.SaveLocationAsync(null, new("Vesu", "Surat", "Gujarat", RateZone.Residential, 6000, 8500, Apr25, null));
        await _rates.SaveLocationAsync(null, new("Vesu", "Surat", "Gujarat", RateZone.Residential, 6500, 9200, Apr26, null));
        await _rates.SaveLocationAsync(null, new("Vesu", "Surat", "Gujarat", RateZone.Residential, 7000, 9900, Apr27, null));

        var current = (await _rates.ListLocationAsync(new RateQuery())).Items.Single();
        Assert.Equal(6500, current.MinRate);
        Assert.True(current.IsCurrent);

        var history = (await _rates.ListLocationAsync(new RateQuery(CurrentOnly: false))).Items;
        Assert.Equal(3, history.Count);
        Assert.Single(history, h => h.IsCurrent);
    }

    [Fact]
    public async Task Same_locality_and_date_is_a_conflict()
    {
        await _rates.SaveLocationAsync(null, new("Vesu", "Surat", null, RateZone.Residential, 6500, 9200, Apr26, null));
        var dup = await _rates.SaveLocationAsync(null, new("Vesu", "Surat", null, RateZone.Residential, 6600, 9300, Apr26, null));
        Assert.Equal(ErrorKind.Conflict, dup.Error!.Kind);

        // Different zone is fine.
        Assert.True((await _rates.SaveLocationAsync(null, new("Vesu", "Surat", null, RateZone.Commercial, 9000, 14000, Apr26, null))).IsSuccess);
    }

    [Fact]
    public async Task Location_rates_show_the_matching_circle_rate_for_their_zone()
    {
        await _rates.SaveCircleAsync(null, new("Gujarat", CircleScheme.Jantri, "Surat", "Vesu", 52000, 78000, null, Apr26, null));
        await _rates.SaveLocationAsync(null, new("Vesu", "Surat", null, RateZone.Residential, 6500, 9200, Apr26, null));
        await _rates.SaveLocationAsync(null, new("Vesu", "Surat", null, RateZone.Commercial, 9000, 14000, Apr26, null));

        var items = (await _rates.ListLocationAsync(new RateQuery())).Items;
        Assert.Equal(52000, items.Single(i => i.Zone == RateZone.Residential).CircleRatePerSqM);
        Assert.Equal(78000, items.Single(i => i.Zone == RateZone.Commercial).CircleRatePerSqM);
        Assert.Equal(CircleScheme.Jantri, items[0].CircleScheme);
    }

    [Fact]
    public async Task Csv_import_reads_rupee_amounts_indian_dates_and_flags_problems()
    {
        var csv = "Area,City,Zone,Rate From,Rate To,W.E.F.\n" +
                  "Adajan,Surat,Resi,\"₹5,800\",\"₹8,000\",01/04/2026\n" +
                  "Adajan,Surat,Resi,5800,8000,01/04/2026\n" +       // repeated
                  "Piplod,Surat,Hotel,6000,9000,2026-04-01\n" +       // unknown zone
                  "Rander,Surat,Residential,7000,6000,31/13/2026\n";  // bad date, max < min
        var preview = (await _rates.PreviewImportAsync(RateKind.Location, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "rates.csv")).Value!;

        var first = (SaveLocationRate)preview.Rows[0].Data!;
        Assert.Equal((5800m, 8000m, Apr26), (first.MinRate, first.MaxRate, first.EffectiveFrom));
        Assert.True(preview.Rows[0].IsValid);
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("repeats"));
        Assert.Contains(preview.Rows[2].Errors, e => e.Contains("Unknown zone"));
        Assert.Contains(preview.Rows[3].Errors, e => e.Contains("Could not read the date"));
        Assert.Contains(preview.Rows[3].Errors, e => e.Contains("cannot be below"));

        var result = (await _rates.ImportAsync(RateKind.Location, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "rates.csv")).Value!;
        Assert.Equal(1, result.Imported);
    }

    [Fact]
    public async Task Circle_import_infers_scheme_from_state()
    {
        var csv = "State,City,Locality,Residential,Commercial,Effective From\n" +
                  "Maharashtra,Mumbai,Kandivali,115000,168000,2026-04-01\n" +
                  "Gujarat,Surat,Vesu,52000,78000,2026-04-01\n";
        var preview = (await _rates.PreviewImportAsync(RateKind.Circle, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "c.csv")).Value!;
        Assert.Equal(CircleScheme.ReadyReckoner, ((SaveCircleRate)preview.Rows[0].Data!).Scheme);
        Assert.Equal(CircleScheme.Jantri, ((SaveCircleRate)preview.Rows[1].Data!).Scheme);
    }

    [Fact]
    public async Task Standard_depreciation_rules_are_added_once_with_bands()
    {
        await _rates.AddStandardDepreciationAsync();
        var list = await _rates.AddStandardDepreciationAsync(); // second call adds nothing

        Assert.Equal(4, list.Count);
        var rcc = list.Single(r => r.StructureType == "RCC Framed Structure");
        Assert.Equal("0 – 5 years", rcc.Bands[0].Label);
        Assert.Equal(7.5m, rcc.Bands[0].Percent);          // 5/60 × 90
        Assert.Equal(90m, rcc.Bands[^1].Percent);           // 60+ years
    }

    [Fact]
    public void Wdv_needs_a_salvage_value() =>
        Assert.False(new SaveDepreciationRuleValidator().Validate(new SaveDepreciationRule("RCC", 60, 0, DepreciationMethod.WrittenDownValue, null)).IsValid);
}
