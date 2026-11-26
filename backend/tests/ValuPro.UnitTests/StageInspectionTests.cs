using ValuPro.Domain.Assignments;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Reports;

namespace ValuPro.UnitTests;

public class StageInspectionTests
{
    [Fact]
    public void Overall_completion_is_the_weighted_sum_of_the_stages()
    {
        var c = new ConstructionSection { Stages = ConstructionSection.Defaults() };
        Assert.Equal(100, c.Stages.Sum(s => s.Weight));
        c.Stages[0].DonePercent = 100; // foundation 10
        c.Stages[1].DonePercent = 100; // plinth 5
        c.Stages[2].DonePercent = 50;  // frame and slabs 30 → 15
        Assert.Equal(30, c.OverallPercent());
    }

    [Fact]
    public void Release_is_the_finance_share_of_work_done_less_what_was_paid()
    {
        var r = ValuationCalculator.Progress(new ProgressInput
        {
            EstimatedCost = 60_00_000, CompletionPercent = 45, FinancePercent = 80, DisbursedSoFar = 12_00_000,
        });
        Assert.Equal(27_00_000, r.ValueOfWorkDone);   // 45% of 60 lakh
        Assert.Equal(21_60_000, r.EligibleAmount);    // 80% of that
        Assert.Equal(9_60_000, r.RecommendedRelease); // less 12 lakh already paid
    }

    [Fact]
    public void Release_is_capped_and_never_negative_and_warns()
    {
        var warnings = new List<string>();
        var capped = ValuationCalculator.Progress(new ProgressInput { EstimatedCost = 1_00_00_000, CompletionPercent = 90, FinancePercent = 80, SanctionedLoan = 50_00_000 }, warnings);
        Assert.Equal(50_00_000, capped.EligibleAmount);

        var over = ValuationCalculator.Progress(new ProgressInput { EstimatedCost = 60_00_000, CompletionPercent = 20, PreviousPercent = 25, DisbursedSoFar = 15_00_000 }, warnings);
        Assert.Equal(0, over.RecommendedRelease);
        Assert.Equal(-5, over.ProgressSincePrevious);
        Assert.Contains(warnings, w => w.Contains("lower than at the previous visit"));
        Assert.Contains(warnings, w => w.Contains("already been disbursed"));
    }

    [Fact]
    public void A_stage_valuation_needs_no_market_value()
    {
        var r = ValuationCalculator.Calculate(new ValuationInput { Progress = new ProgressInput { EstimatedCost = 60_00_000, CompletionPercent = 45 } });
        Assert.Equal(0, r.FairMarketValue);
        Assert.NotNull(r.Progress);
        Assert.DoesNotContain(r.Warnings, w => w.StartsWith("No value yet"));
    }

    [Fact]
    public void Stage_visits_ask_for_progress_instead_of_age_and_condition()
    {
        var i = new Inspection { StartedByName = "S", Latitude = 1 };
        i.Data.Building.StructureType = "RCC Framed Structure";
        i.Data.Construction.Stages = ConstructionSection.Defaults();
        i.Data.Construction.Stages[0].Weight = 20; // now adds up to 110
        var missing = InspectionRules.Missing(i, [], landOnly: false, [], construction: true);
        Assert.Contains(missing, m => m.Message == "Stage weights should add up to 100%.");
        Assert.Contains(missing, m => m.Message == "What work was going on at site?");
        Assert.DoesNotContain(missing, m => m.Message == "Enter the age of the building.");
        Assert.DoesNotContain(missing, m => m.Section == "measurements");
    }

    [Fact]
    public void Renders_a_stage_inspection_report()
    {
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
        var m = ReportTemplateTests.Sample(draft: false);
        m.Inspection!.Construction.Stages = ConstructionSection.Defaults();
        m.Inspection.Construction.Stages.ForEach(s => s.DonePercent = 40);
        m.Inspection.Construction.CurrentWork = "Fourth slab shuttering";
        m.Inspection.Construction.AsPerPlan = true;
        var input = new ValuationInput { Progress = new ProgressInput { EstimatedCost = 60_00_000, CompletionPercent = 40, PreviousPercent = 25, DisbursedSoFar = 12_00_000 } };
        var stage = m with
        {
            CaseType = CaseType.StageInspection, PreviousNumber = "VAL/2026/0007", PreviousPercent = 25, PreviousDate = new DateOnly(2026, 9, 1),
            Valuation = input, Result = ValuationCalculator.Calculate(input),
        };
        var pdf = new DefaultReportTemplate().Render(stage);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
