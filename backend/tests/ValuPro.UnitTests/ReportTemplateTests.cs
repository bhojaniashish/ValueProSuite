using QuestPDF.Helpers;
using ValuPro.Application.Reports;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Reports;

namespace ValuPro.UnitTests;

public class ReportTemplateTests
{
    public static ReportModel Sample(bool draft = true, int photos = 3)
    {
        var data = new InspectionData();
        data.Visit.PersonMet = "Suresh Agarwal";
        data.Location.LocalityType = "Urban";
        data.Location.RoadWidthFt = 40;
        data.Location.Negatives = ["High-tension line"];
        data.Boundaries.North = new Boundary { AsPerDocuments = "Road", AtSite = "40 ft road", Matches = true };
        data.Building.StructureType = "RCC Framed Structure";
        data.Building.AgeYears = 6;
        data.Measurements.CarpetAsPerDocuments = 1100;
        data.Measurements.CarpetMeasured = 1080;
        data.BankAnswers = [new BankAnswer { Question = "Is the property in a gated society?", Answer = "Yes" }];

        var input = new ValuationInput { Method = ValuationMethod.Composite, AreaSqFt = 1080, CompositeRatePerSqFt = 7850, CircleRatePerSqM = 52000, GuidelineAreaSqFt = 1310 };
        return new ReportModel(
            draft, 2,
            new FirmBlock("Om Engineers", "201 Sahjanand Complex, Surat 395007", "98250 41201", "office@example.in", "RV/2018/GJ/0412", null, null, "#C8104F"),
            "VAL/2026/0001", new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24),
            "HDFC Bank", "Vesu", "Vesu Main Road, Surat", "HL-26-0412", "Home loan", "Suresh Agarwal", "Suresh Agarwal",
            PropertyType.ResidentialFlat, "A-902 Shanti Heights, Vesu, Surat", null, "CTS 112", null, 21.14m, 72.77m,
            data, "Suresh Agarwal",
            [
                new DocumentRow("Sale deed", DocumentAvailability.OriginalSeen, "SRT-4412/2019", new DateOnly(2019, 6, 14), "Sub-registrar, Surat-2", null),
                new DocumentRow("Approved plan", DocumentAvailability.NotProvided, null, null, null, "With the society"),
            ],
            input, ValuationCalculator.Calculate(input),
            Enumerable.Range(1, photos).Select(i => new PhotoBlock(i, "Front elevation", i == 1 ? "From the main road" : null, DateTimeOffset.UtcNow, 21.14m, 72.77m, Placeholders.Image(800, 600))).ToList(),
            4, "The flat is well maintained. ₹ rates are in line with recent sales in Vesu.", "I hereby declare that…", "Surat",
            new SignerBlock("Sneha Kulkarni", "RV/2018/GJ/0412", draft ? null : Placeholders.Image(300, 100), "Image"),
            draft ? null : "Neha Joshi");
    }

    [Fact]
    public void Renders_a_complete_report_including_the_rupee_sign()
    {
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true; // fail if any character (e.g. ₹) has no glyph
        QuestPDF.Settings.UseEnvironmentFonts = false;               // like a Linux server: only bundled fonts
        var pdf = new DefaultReportTemplate().Render(Sample(draft: false));
        Assert.True(pdf.Length > 10_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Renders_a_draft_without_photos_or_inspection()
    {
        var m = Sample(photos: 0) with { Inspection = null, Documents = [] };
        var pdf = new DefaultReportTemplate().Render(m);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
