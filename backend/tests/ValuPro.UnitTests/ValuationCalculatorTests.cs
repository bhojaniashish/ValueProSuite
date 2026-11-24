using ValuPro.Domain.Banks;
using ValuPro.Domain.Valuations;

namespace ValuPro.UnitTests;

public class ValuationCalculatorTests
{
    [Fact]
    public void Land_and_building_adds_land_to_the_depreciated_building()
    {
        var r = ValuationCalculator.Calculate(new ValuationInput
        {
            Method = ValuationMethod.LandAndBuilding,
            LandAreaSqFt = 2000, LandRatePerSqFt = 4500,                                   // 90,00,000
            Building =
            [
                new BuildingLine { Label = "Ground floor", AreaSqFt = 1000, RatePerSqFt = 2000, AgeYears = 12, LifeYears = 60, SalvagePercent = 10 }, // 20,00,000 less 18% = 16,40,000
                new BuildingLine { Label = "First floor", AreaSqFt = 900, RatePerSqFt = 2000, AgeYears = 0, LifeYears = 60, SalvagePercent = 10 },   // 18,00,000 new
            ],
            Extras = [new Extra { Label = "Compound wall", Amount = 1_25_000 }],
            RealizablePercent = 90, DistressPercent = 75,
        });

        Assert.Equal(90_00_000, r.LandValue);
        Assert.Equal(38_00_000, r.ReplacementCost);
        Assert.Equal(3_60_000, r.Depreciation);
        Assert.Equal(34_40_000, r.BuildingValue);
        Assert.Equal(1_25_65_000, r.FairMarketValue);
        Assert.Equal(1_13_09_000, r.RealizableValue);  // 90%, to the nearest thousand
        Assert.Equal(94_24_000, r.DistressValue);       // 75%
        Assert.Equal(38_00_000, r.InsuranceValue);      // rebuild cost, no land
        Assert.Equal("Rupees One Crore Twenty Five Lakh Sixty Five Thousand Only", r.FairMarketValueInWords);
        Assert.Equal(["Land", "Ground floor", "First floor", "Compound wall"], r.Working.Select(l => l.Label));
    }

    [Fact]
    public void Composite_applies_adjustments_and_rounds_to_the_nearest_thousand()
    {
        var r = ValuationCalculator.Calculate(new ValuationInput
        {
            Method = ValuationMethod.Composite,
            AreaBasis = AreaBasis.Carpet, AreaSqFt = 1080, CompositeRatePerSqFt = 7850,     // 84,78,000
            Adjustments = [new Adjustment { Label = "Higher floor", Percent = 2 }, new Adjustment { Label = "Old lift", Percent = -1.5m }],
            BuiltUpSqFt = 1310, ConstructionRatePerSqFt = 2100,
        });

        Assert.Equal(84_78_000, r.CompositeValue);
        Assert.Equal(42_390, r.AdjustmentAmount);       // +1,69,560 − 1,27,170
        Assert.Equal(85_20_000, r.FairMarketValue);     // 85,20,390 → nearest thousand
        Assert.Equal(27_51_000, r.InsuranceValue);
        Assert.Equal(7889, r.RatePerSqFt);
    }

    [Fact]
    public void Written_down_value_depreciates_faster_early()
    {
        var line = new BuildingLine { AreaSqFt = 1000, RatePerSqFt = 1000, AgeYears = 10, LifeYears = 60, SalvagePercent = 10 };
        var sl = ValuationCalculator.Calculate(new ValuationInput { Method = ValuationMethod.LandAndBuilding, Building = [line] });
        line.Method = DepreciationMethod.WrittenDownValue;
        var wdv = ValuationCalculator.Calculate(new ValuationInput { Method = ValuationMethod.LandAndBuilding, Building = [line] });
        Assert.Equal(1_50_000, sl.Depreciation);         // 15%
        Assert.True(wdv.Depreciation > sl.Depreciation); // 31.87%
    }

    [Fact]
    public void Checks_flag_what_banks_question()
    {
        var r = ValuationCalculator.Calculate(new ValuationInput
        {
            Method = ValuationMethod.Composite, AreaSqFt = 1000, CompositeRatePerSqFt = 5000,      // 50,00,000
            CircleRatePerSqM = 70_000, GuidelineAreaSqFt = 1000,                                    // 65,03,219
            MarketRateMin = 5800, MarketRateMax = 8000,
            DocumentAreaSqFt = 1000, MeasuredAreaSqFt = 870,
        });

        Assert.Equal(65_03_219, r.GuidelineValue);
        Assert.Equal(3, r.Warnings.Count);
        Assert.Contains(r.Warnings, w => w.Contains("below the government"));
        Assert.Contains(r.Warnings, w => w.Contains("outside the Rate Master range"));
        Assert.Contains(r.Warnings, w => w.Contains("13%"));
    }

    [Theory]
    [InlineData(0, "Rupees Zero Only")]
    [InlineData(5_000, "Rupees Five Thousand Only")]
    [InlineData(1_15_000, "Rupees One Lakh Fifteen Thousand Only")]
    [InlineData(1_41_38_708, "Rupees One Crore Forty One Lakh Thirty Eight Thousand Seven Hundred Eight Only")]
    [InlineData(250_00_00_000, "Rupees Two Hundred Fifty Crore Only")]
    public void Amounts_in_words_use_lakh_and_crore(long amount, string words) =>
        Assert.Equal(words, IndianNumber.ToWords(amount));

    [Theory]
    [InlineData(999, "999")]
    [InlineData(115000, "1,15,000")]
    [InlineData(14138708, "1,41,38,708")]
    [InlineData(-2500, "-2,500")]
    public void Numbers_use_indian_grouping(long v, string text) => Assert.Equal(text, IndianNumber.Format(v));
}
