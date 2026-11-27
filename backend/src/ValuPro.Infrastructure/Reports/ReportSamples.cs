using QuestPDF.Helpers;
using ValuPro.Application.Reports;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Reports;
using ValuPro.Domain.Valuations;

namespace ValuPro.Infrastructure.Reports;

/// <summary>A made-up but realistic case, used to preview a report format before any real case uses it.</summary>
public static class ReportSamples
{
    public static ReportModel Model(FirmBlock firm, ReportFormatView format, string? declaration, bool stage, DateOnly today)
    {
        var data = new InspectionData();
        data.Visit.PersonMet = "Suresh Agarwal";
        data.Visit.PersonRelation = "Owner";
        data.Visit.AddressMatches = true;
        data.Location.LocalityType = "Urban";
        data.Location.LocalityClass = "Middle class";
        data.Location.RoadWidthFt = 40;
        data.Location.RoadType = "Tar road";
        data.Location.Landmark = "Near Vesu Char Rasta";
        data.Location.Amenities = ["School", "Hospital", "Market"];
        data.Location.Negatives = [];
        data.Boundaries.North = new Boundary { AsPerDocuments = "Road", AtSite = "40 ft road", Matches = true };
        data.Boundaries.South = new Boundary { AsPerDocuments = "Flat A-901", AtSite = "Flat A-901", Matches = true };
        data.Boundaries.East = new Boundary { AsPerDocuments = "Open space", AtSite = "Society garden", Matches = true };
        data.Boundaries.West = new Boundary { AsPerDocuments = "Flat A-903", AtSite = "Flat A-903", Matches = true };
        data.Occupancy.OccupiedBy = stage ? "UnderConstruction" : "Owner";
        data.Building.StructureType = "RCC Framed Structure";
        data.Building.AgeYears = stage ? null : 6;
        data.Building.TotalFloors = 12;
        data.Building.UnitFloor = "9th";
        data.Building.Quality = "Good";
        data.Building.Condition = stage ? null : "Good";
        data.Approvals.PlanSeen = "Yes";
        data.Approvals.PlanAuthority = "SMC";
        data.Measurements.CarpetAsPerDocuments = 1100;
        data.Measurements.CarpetMeasured = 1080;
        data.Measurements.BuiltUpAsPerDocuments = 1320;
        data.BankAnswers = [new BankAnswer { Question = "Is the property in a gated society?", Answer = "Yes" }];
        if (stage)
        {
            data.Construction.Stages = ConstructionSection.Defaults();
            for (var i = 0; i < 4; i++) data.Construction.Stages[i].DonePercent = 100;
            data.Construction.CurrentWork = "Plastering on the 9th floor";
            data.Construction.WorkersAtSite = 14;
            data.Construction.AsPerPlan = true;
        }

        var input = new ValuationInput
        {
            Method = ValuationMethod.Composite, AreaSqFt = 1080, CompositeRatePerSqFt = 7850, CircleRatePerSqM = 52000, GuidelineAreaSqFt = 1310,
            Progress = stage ? new ProgressInput { EstimatedCost = 60_00_000, CompletionPercent = 60, PreviousPercent = 35, DisbursedSoFar = 16_80_000 } : null,
        };
        var photos = Enumerable.Range(1, 2).Select(i => new PhotoBlock(i, i == 1 ? "Front elevation" : "Living room", i == 1 ? "From the main road" : null,
            new DateTimeOffset(today.ToDateTime(new TimeOnly(11, 0)), TimeSpan.FromHours(5.5)), 21.1702m, 72.7804m, Placeholders.Image(800, 600))).ToList();

        return new ReportModel(
            Draft: true, Version: 1, firm, "VAL/2026/0000", today, today, today.AddDays(-2),
            "Sample Bank", "Vesu", "Vesu Main Road, Surat", "HL-26-0412", "Home loan", "Suresh Agarwal", "Suresh Agarwal",
            PropertyType.ResidentialFlat, "A-902 Shanti Heights, Vesu, Surat 395007", null, "112", null, 21.1702m, 72.7804m,
            data, "Suresh Agarwal (Owner)",
            [
                new DocumentRow("Sale deed", DocumentAvailability.OriginalSeen, "SRT-4412/2019", new DateOnly(2019, 6, 14), "Sub-registrar, Surat-2", null),
                new DocumentRow("Approved plan", DocumentAvailability.CopyOnly, "SMC/BP/2017/221", new DateOnly(2017, 3, 2), "Surat Municipal Corporation", null),
            ],
            input, ValuationCalculator.Calculate(input),
            photos, 4,
            "The flat is well maintained and in a good residential locality. Rates are in line with recent sales in Vesu.",
            declaration ?? ReportText.DefaultDeclaration(today.AddDays(-2).ToString("dd MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))),
            "Surat", new SignerBlock("Valuer name", "RV/0000/00/0000", null, "Image"), null,
            stage ? CaseType.StageInspection : CaseType.Valuation, stage ? "VAL/2025/0999" : null, stage ? 35 : null, stage ? today.AddMonths(-3) : null,
            format, "PANEL/0000");
    }
}
