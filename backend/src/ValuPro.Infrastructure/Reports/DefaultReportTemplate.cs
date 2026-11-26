using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ValuPro.Application.Reports;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Documents;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Valuations;

namespace ValuPro.Infrastructure.Reports;

/// <summary>
/// The default valuation report (bank style, A4). Bank-specific formats can be added as other <see cref="IReportRenderer"/>s.
/// QuestPDF is used under its Community licence (free below USD 1M annual revenue; see README).
/// </summary>
public sealed class DefaultReportTemplate : IReportRenderer
{
    private const string Ink = "#1F2937";
    private const string Muted = "#6B7280";
    private const string Line = "#E5E7EB";
    private const string Soft = "#F7F8FB";
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>The firm qualifies for QuestPDF's Community licence (see the README before revenue passes USD 1M).</summary>
    static DefaultReportTemplate() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(ReportModel m) => Build(m).GeneratePdf();

    /// <summary>The document itself (tests and tools can also render it to images).</summary>
    public IDocument Build(ReportModel m) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(1.6f, Unit.Centimetre);
                page.MarginVertical(1.2f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(9.3f).FontColor(Ink).LineHeight(1.25f));
                if (m.Draft)
                    page.Background().AlignCenter().AlignMiddle().Rotate(-35)
                        .Text("DRAFT").FontSize(120).Bold().FontColor("#EEF0F4");

                page.Header().Element(c => Letterhead(c, m));
                page.Content().PaddingVertical(10).Column(col => Body(col, m));
                page.Footer().Element(c => Footer(c, m));
            });
        });

    // ---------------- page furniture ----------------

    private static void Letterhead(IContainer c, ReportModel m)
    {
        c.Column(col =>
        {
            col.Item().Row(row =>
            {
                if (m.Firm.Logo is { Length: > 0 } logo)
                    row.ConstantItem(54).Height(46).AlignMiddle().Image(logo).FitArea();
                row.RelativeItem().PaddingLeft(m.Firm.Logo is null ? 0 : 10).Column(f =>
                {
                    f.Item().Text(m.Firm.Name).FontSize(15).Bold().FontColor(m.Firm.Color);
                    if (m.Firm.Address is { } a) f.Item().Text(a).FontSize(8).FontColor(Muted);
                    var contact = string.Join("  ·  ", new[] { m.Firm.Phone, m.Firm.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (contact.Length > 0) f.Item().Text(contact).FontSize(8).FontColor(Muted);
                });
                row.ConstantItem(170).AlignRight().AlignMiddle().Column(r =>
                {
                    if (m.Firm.RegisteredValuerNo is { } rv) r.Item().AlignRight().Text($"Regd. Valuer: {rv}").FontSize(7.8f).FontColor(Muted);
                    if (m.Firm.IbbiRegistration is { } ibbi) r.Item().AlignRight().Text($"IBBI: {ibbi}").FontSize(7.8f).FontColor(Muted);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(m.Firm.Color);
        });
    }

    private static void Footer(IContainer c, ReportModel m)
    {
        c.BorderTop(0.5f).BorderColor(Line).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text($"Ref: {m.Number}{(m.Draft ? $"  ·  Draft v{m.Version}" : "")}").FontSize(7.5f).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    }

    // ---------------- body ----------------

    private void Body(ColumnDescriptor col, ReportModel m)
    {
        col.Spacing(10);
        var d = m.Inspection;
        var construction = m.CaseType is CaseType.StageInspection or CaseType.Reinspection;
        var progress = m.Valuation.Progress is { } pi ? (Input: pi, Result: m.Result.Progress ?? ValuationCalculator.Progress(pi)) : ((ProgressInput Input, ProgressResult Result)?)null;
        var no = 0;
        string Next() => (++no).ToString(CultureInfo.InvariantCulture);

        col.Item().AlignCenter().Text(m.CaseType switch
        {
            CaseType.StageInspection => "STAGE INSPECTION REPORT",
            CaseType.Reinspection => "RE-INSPECTION REPORT",
            _ => "VALUATION REPORT",
        }).FontSize(16).Bold().LetterSpacing(0.05f);
        col.Item().Row(row =>
        {
            row.RelativeItem().Column(to =>
            {
                to.Item().Text("To,").FontColor(Muted);
                to.Item().Text("The Branch Manager").Bold();
                to.Item().Text(m.BankName);
                if (m.BranchName is { } b) to.Item().Text($"{b} Branch");
                if (m.BranchAddress is { } ba) to.Item().Text(ba).FontColor(Muted);
            });
            row.ConstantItem(180).AlignRight().Column(r =>
            {
                r.Item().AlignRight().Text(t => { t.Span("Ref: ").FontColor(Muted); t.Span(m.Number).Bold(); });
                r.Item().AlignRight().Text(t => { t.Span("Date: ").FontColor(Muted); t.Span(Day(m.ReportDate)); });
            });
        });

        Section(col, Next(), "General", c => KeyValues(c,
        [
            ("Purpose of valuation", m.Purpose),
            ("Applicant", m.ApplicantName),
            ("Owner(s) as per documents", m.OwnerName),
            ("Bank reference", m.BankReference),
            ("Date of inspection", m.InspectionDate is { } i ? Day(i) : null),
            ("Date of valuation", Day(m.ValuationDate)),
            ("Person met at site", m.PersonMet),
            ("Previous inspection", m.PreviousNumber is null ? null
                : Join(" · ", m.PreviousNumber, m.PreviousDate is { } pd ? Day(pd) : null, m.PreviousPercent is { } pp ? $"{N(pp)}% complete" : null)),
        ]));

        Section(col, Next(), "Property and location", c => c.Column(p =>
        {
            p.Spacing(6);
            p.Item().Element(x => KeyValues(x,
            [
                ("Type of property", Type(m.PropertyType)),
                ("Address", m.PropertyAddress),
                ("Survey / CTS / Plot no.", Join(" · ", m.SurveyNo is null ? null : $"Survey {m.SurveyNo}", m.CtsNo is null ? null : $"CTS {m.CtsNo}", m.PlotNo is null ? null : $"Plot {m.PlotNo}")),
                ("Address at site matches", d is null ? null : YesNo(d.Visit.AddressMatches) + (d.Visit.AddressAtSite is { } at ? $" (at site: {at})" : "")),
                ("Locality", d is null ? null : Join(", ", d.Location.LocalityType, d.Location.LocalityClass, d.Location.Development)),
                ("Approach road", d is null ? null : Join(", ", d.Location.RoadWidthFt is { } w ? $"{N(w)} ft wide" : null, d.Location.RoadType)),
                ("Facing / corner plot", d is null ? null : Join(" · ", d.Location.Facing, d.Location.CornerPlot == true ? "Corner plot" : null)),
                ("Landmark", d?.Location.Landmark),
                ("Distance to station / bus / city centre", d is null ? null : Join(" / ", Km(d.Location.StationKm), Km(d.Location.BusStopKm), Km(d.Location.CityCentreKm))),
                ("Civic amenities", d is null ? null : Join(", ", [.. d.Location.Amenities])),
                ("Negatives in the vicinity", d is null ? null : d.Location.Negatives.Count == 0 ? "None noticed" : Join(", ", [.. d.Location.Negatives])),
                ("GPS location at site", m.Latitude is { } la && m.Longitude is { } lo ? $"{la.ToString("0.000000", CultureInfo.InvariantCulture)}, {lo.ToString("0.000000", CultureInfo.InvariantCulture)}" : null),
                ("Occupancy", d is null ? null : Join(" · ", Occupancy(d.Occupancy.OccupiedBy), d.Occupancy.TenantName, d.Occupancy.MonthlyRent is { } rent ? $"rent ₹{N(rent)}/month" : null)),
            ]));
            if (d is not null)
            {
                p.Item().PaddingTop(4).Text("Boundaries").Bold();
                p.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(60); cd.RelativeColumn(); cd.RelativeColumn(); cd.ConstantColumn(60); });
                    t.Header(h => { Head(h, "Side"); Head(h, "As per documents"); Head(h, "As found at site"); Head(h, "Matches"); });
                    foreach (var (side, b) in new[] { ("North", d.Boundaries.North), ("South", d.Boundaries.South), ("East", d.Boundaries.East), ("West", d.Boundaries.West) })
                    {
                        t.Cell().Element(Cell).Text(side).Bold();
                        t.Cell().Element(Cell).Text(b.AsPerDocuments ?? "—");
                        t.Cell().Element(Cell).Text(b.AtSite ?? "—");
                        t.Cell().Element(Cell).Text(YesNo(b.Matches) ?? "—");
                    }
                });
                if (d.Boundaries.Encroachment == true) p.Item().Text($"Encroachment: {d.Boundaries.Note}").FontColor("#B45309");
            }
        }));

        Section(col, Next(), "Documents perused", c =>
        {
            if (m.Documents.Count == 0)
            {
                c.Text("No documents were recorded for this case.").FontColor(Muted);
                return;
            }
            c.Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.ConstantColumn(20); cd.RelativeColumn(2.2f); cd.RelativeColumn(1.6f); cd.RelativeColumn(1.1f); cd.RelativeColumn(1.8f); cd.RelativeColumn(1.2f); });
                t.Header(h => { Head(h, "#"); Head(h, "Document"); Head(h, "Number"); Head(h, "Dated"); Head(h, "Issued / registered by"); Head(h, "Seen"); });
                var n = 0;
                foreach (var doc in m.Documents)
                {
                    n++;
                    t.Cell().Element(Cell).Text(n.ToString(CultureInfo.InvariantCulture)).FontColor(Muted);
                    t.Cell().Element(Cell).Text(doc.Name).Bold();
                    t.Cell().Element(Cell).Text(doc.Availability == DocumentAvailability.NotProvided ? "—" : doc.Number ?? "—");
                    t.Cell().Element(Cell).Text(doc.IssuedOn is { } on ? Day(on) : "—");
                    t.Cell().Element(Cell).Text(doc.Availability == DocumentAvailability.NotProvided ? doc.Notes ?? "—" : doc.IssuedBy ?? "—");
                    t.Cell().Element(Cell).Text(doc.Availability switch
                    {
                        DocumentAvailability.OriginalSeen => "Original",
                        DocumentAvailability.CopyOnly => "Copy",
                        _ => "Not provided",
                    }).FontColor(doc.Availability == DocumentAvailability.NotProvided ? "#B91C1C" : Ink);
                }
            });
        });

        if (d is not null)
        {
            if (construction) Section(col, Next(), "Construction progress", c => Construction(c, m, d));
            Section(col, Next(), "Building, construction and approvals", c => KeyValues(c,
            [
                ("Structure", d.Building.StructureType),
                ("Age of building", d.Building.AgeYears is { } age ? $"{age} years" : null),
                ("Floors (building / this unit)", Join(" / ", d.Building.TotalFloors?.ToString(CultureInfo.InvariantCulture), d.Building.UnitFloor)),
                ("Quality / condition", Join(" / ", d.Building.Quality, d.Building.Condition)),
                ("Construction complete", d.Building.CompletionPercent is { } pc ? $"{pc}%" : null),
                ("Foundation / walls / roof", Join(" · ", d.Building.Foundation, d.Building.Walls, d.Building.Roof)),
                ("Flooring / doors & windows", Join(" · ", d.Building.Flooring, d.Building.DoorsWindows)),
                ("Kitchen / toilets", Join(" · ", d.Building.Kitchen, d.Building.Toilets)),
                ("Electrical / plumbing", Join(" · ", d.Building.Electrical, d.Building.Plumbing)),
                ("Lift / parking", Join(" · ", d.Building.Lift is null ? null : d.Building.Lift.Value ? "Lift available" : "No lift", d.Building.Parking is null ? null : $"{d.Building.Parking} parking{(d.Building.ParkingSlots is { } ps ? $" ({ps})" : "")}")),
                ("Amenities", Join(", ", [.. d.Building.Amenities])),
                ("Defects noticed", d.Building.Defects.Count == 0 ? null : Join(", ", [.. d.Building.Defects])),
                ("Approved plan", Join(" · ", Plan(d.Approvals.PlanSeen), d.Approvals.PlanAuthority, d.Approvals.PlanNumber is null ? null : $"No. {d.Approvals.PlanNumber}")),
                ("Deviation from plan", Join(" — ", d.Approvals.Deviation, d.Approvals.DeviationNote)),
                ("Occupancy / BU certificate", Oc(d.Approvals.OccupancyCertificate)),
            ]));

            Section(col, Next(), "Measurements (sq.ft unless stated)", c => c.Table(t =>
            {
                var unit = d.Measurements.Unit == "SqM" ? " (sq.m)" : "";
                t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); cd.RelativeColumn(); });
                t.Header(h => { Head(h, "Area" + unit); Head(h, "As per documents", right: true); Head(h, "Measured at site", right: true); });
                void Row(string label, decimal? docs, decimal? measured)
                {
                    if (docs is null && measured is null) return;
                    t.Cell().Element(Cell).Text(label);
                    t.Cell().Element(Cell).AlignRight().Text(docs is { } a ? N(a) : "—");
                    t.Cell().Element(Cell).AlignRight().Text(measured is { } b ? N(b) : "—");
                }
                var mm = d.Measurements;
                Row("Land / plot", mm.LandAsPerDocuments, mm.LandMeasured);
                Row("Carpet", mm.CarpetAsPerDocuments, mm.CarpetMeasured);
                Row("Built-up", mm.BuiltUpAsPerDocuments, mm.BuiltUpMeasured);
                Row("Super built-up", mm.SuperBuiltUpAsPerDocuments, null);
                foreach (var f in mm.Floors) Row(f.Name ?? "Floor", f.AsPerDocuments, f.Measured);
            }));
        }

        if (progress is { } pr) Section(col, Next(), "Progress and disbursement", c => Disbursement(c, pr.Input, pr.Result));
        if (m.Result.FairMarketValue > 0) Section(col, Next(), construction ? "Market value as on date" : "Valuation", c => Valuation(c, m));

        var answers = d?.BankAnswers.Where(a => !string.IsNullOrWhiteSpace(a.Answer)).ToList() ?? [];
        if (answers.Count > 0)
            Section(col, Next(), $"{m.BankName}: additional information", c => c.Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(2); });
                foreach (var a in answers)
                {
                    t.Cell().Element(Cell).Text(a.Question);
                    t.Cell().Element(Cell).Text(a.Answer!).Bold();
                }
            }));

        Section(col, "", "Opinion and remarks", c => c.Column(o =>
        {
            o.Spacing(6);
            o.Item().Text(string.IsNullOrWhiteSpace(m.Opinion) ? "—" : m.Opinion);
            if (progress is { } pr)
                o.Item().Background(Soft).Padding(8).Text(t =>
                {
                    t.Span("In my opinion, the construction is ").FontColor(Muted);
                    t.Span($"{N(pr.Input.CompletionPercent)}% complete").Bold();
                    t.Span(" as on ").FontColor(Muted);
                    t.Span(Day(m.ValuationDate)).Bold();
                    t.Span(pr.Result.RecommendedRelease > 0 ? ", and a further release of " : ". No further release is recommended at this stage.").FontColor(Muted);
                    if (pr.Result.RecommendedRelease > 0)
                    {
                        t.Span($"₹ {N(pr.Result.RecommendedRelease)}").Bold();
                        t.Span($" ({IndianNumber.ToWords(pr.Result.RecommendedRelease)}) may be considered.").FontColor(Muted);
                    }
                });
            if (m.Result.FairMarketValue > 0)
                o.Item().Background(Soft).Padding(8).Text(t =>
                {
                    t.Span(construction ? "The fair market value of the property in its present state as on " : "In my opinion, the fair market value of the above property as on ").FontColor(Muted);
                    t.Span(Day(m.ValuationDate)).Bold();
                    t.Span(" is ").FontColor(Muted);
                    t.Span($"₹ {N(m.Result.FairMarketValue)}").Bold();
                    t.Span($" ({m.Result.FairMarketValueInWords}).").FontColor(Muted);
                });
        }));

        col.Item().EnsureSpace(230).Element(c => Declaration(c, m));

        if (m.Photos.Count > 0)
        {
            col.Item().PageBreak();
            col.Item().Text("Annexure: Photographs").FontSize(12).Bold().FontColor(m.Firm.Color);
            col.Item().Element(c => Photos(c, m));
        }
    }

    private static void Valuation(IContainer c, ReportModel m)
    {
        var v = m.Valuation;
        var r = m.Result;
        c.Column(col =>
        {
            col.Spacing(8);
            col.Item().Text(v.Method == ValuationMethod.Composite
                ? $"Method: composite rate (sales comparison), on the {ValuationCalculator.Basis(v.AreaBasis).ToLowerInvariant()} area."
                : "Method: land and building (land at market rate plus depreciated replacement cost of the building).").FontColor(Muted);

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(1.4f); cd.RelativeColumn(2.6f); cd.RelativeColumn(1.2f); });
                t.Header(h => { Head(h, "Item"); Head(h, "Working"); Head(h, "Amount (₹)", right: true); });
                foreach (var l in r.Working)
                {
                    t.Cell().Element(Cell).Text(l.Label).Bold();
                    t.Cell().Element(Cell).Text(l.Working ?? "").FontColor(Muted);
                    t.Cell().Element(Cell).AlignRight().Text(N(l.Amount));
                }
                t.Cell().ColumnSpan(2).Element(Cell).Text("Total").Bold();
                t.Cell().Element(Cell).AlignRight().Text(N(r.MarketValueBeforeRounding)).Bold();
            });

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(1); });
                void Row(string label, decimal value, bool strong = false)
                {
                    // Shade the whole cell (before its padding) so a highlighted row reads as one band.
                    var cell = t.Cell().Element(x => strong ? Cell(x.Background(Soft)) : Cell(x));
                    var txt = cell.Text(label);
                    if (strong) txt.Bold();
                    var val = t.Cell().Element(x => strong ? Cell(x.Background(Soft)) : Cell(x)).AlignRight().Text(value > 0 ? $"₹ {N(value)}" : "—");
                    if (strong) val.Bold().FontColor(m.Firm.Color);
                }
                Row("Fair market value (rounded)", r.FairMarketValue, strong: true);
                Row($"Realizable value ({N(v.RealizablePercent)}% of market value)", r.RealizableValue);
                Row($"Distress / forced sale value ({N(v.DistressPercent)}%)", r.DistressValue);
                Row($"Government (guideline) value{(v.CircleRatePerSqM > 0 ? $" at ₹{N(v.CircleRatePerSqM)}/sq.m" : "")}", r.GuidelineValue);
                Row("Insurance value (cost to rebuild, excluding land)", r.InsuranceValue);
            });
            col.Item().Text($"Fair market value in words: {r.FairMarketValueInWords}").Italic();
            if (!string.IsNullOrWhiteSpace(v.Remarks)) col.Item().Text(v.Remarks).FontColor(Muted);
        });
    }

    private static void Construction(IContainer c, ReportModel m, InspectionData d)
    {
        var con = d.Construction;
        c.Column(col =>
        {
            col.Spacing(8);
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(1); cd.RelativeColumn(1); cd.RelativeColumn(1.2f); });
                t.Header(h => { Head(h, "Stage of work"); Head(h, "Weight %", right: true); Head(h, "Done %", right: true); Head(h, "Contribution %", right: true); });
                foreach (var st in con.Stages)
                {
                    t.Cell().Element(Cell).Text(st.Name ?? "Stage");
                    t.Cell().Element(Cell).AlignRight().Text(N(st.Weight));
                    t.Cell().Element(Cell).AlignRight().Text(N(st.DonePercent));
                    t.Cell().Element(Cell).AlignRight().Text(N(Math.Round(st.Weight * st.DonePercent / 100, 2)));
                }
                t.Cell().ColumnSpan(3).Element(x => Cell(x.Background(Soft))).Text("Overall completion").Bold();
                t.Cell().Element(x => Cell(x.Background(Soft))).AlignRight().Text($"{N(con.OverallPercent())}%").Bold().FontColor(m.Firm.Color);
            });
            col.Item().Element(x => KeyValues(x,
            [
                ("Previous visit", m.PreviousPercent is { } pp
                    ? $"{N(pp)}% on {(m.PreviousDate is { } pd ? Day(pd) : "the last visit")} ({m.PreviousNumber}) · progress since then {N(con.OverallPercent() - pp)}%"
                    : null),
                ("Work in progress at site", con.CurrentWork),
                ("Workers at site", con.WorkersAtSite?.ToString(CultureInfo.InvariantCulture)),
                ("As per approved plan", YesNo(con.AsPerPlan)),
                ("Remarks", con.Note),
            ]));
        });
    }

    private static void Disbursement(IContainer c, ProgressInput p, ProgressResult r)
    {
        c.Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(1); });
            void Row(string label, string value, bool strong = false)
            {
                var a = t.Cell().Element(x => strong ? Cell(x.Background(Soft)) : Cell(x)).Text(label);
                var b = t.Cell().Element(x => strong ? Cell(x.Background(Soft)) : Cell(x)).AlignRight().Text(value);
                if (strong) { a.Bold(); b.Bold(); }
            }
            Row("Estimated cost of construction", $"₹ {N(p.EstimatedCost)}");
            Row("Completion", $"{N(p.CompletionPercent)}%");
            Row("Value of work done", $"₹ {N(r.ValueOfWorkDone)}");
            Row($"Eligible at {N(p.FinancePercent)}% finance{(p.SanctionedLoan is > 0 ? $" (capped at the sanctioned ₹{N(p.SanctionedLoan.Value)})" : "")}", $"₹ {N(r.EligibleAmount)}");
            Row("Already disbursed", $"₹ {N(p.DisbursedSoFar)}");
            Row("Release recommended now", r.RecommendedRelease > 0 ? $"₹ {N(r.RecommendedRelease)}" : "Nil", strong: true);
        });
    }

    private static void Declaration(IContainer c, ReportModel m)
    {
        c.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("Declaration").Bold().FontColor(m.Firm.Color);
            col.Item().Text(m.Declaration).FontSize(8.6f);
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().AlignBottom().Column(p =>
                {
                    p.Item().Text(t => { t.Span("Place: ").FontColor(Muted); t.Span(m.Place ?? "—"); });
                    p.Item().Text(t => { t.Span("Date: ").FontColor(Muted); t.Span(Day(m.ReportDate)); });
                });
                row.ConstantItem(210).Column(s =>
                {
                    if (m.Signer?.Signature is { Length: > 0 } sig) s.Item().Height(52).AlignRight().Image(sig).FitArea();
                    else s.Item().Height(52).AlignRight().AlignBottom().Text(m.Draft ? "(signature on the final report)" : "").FontSize(8).FontColor(Muted);
                    s.Item().AlignRight().Text(m.Signer?.Name ?? "Valuer").Bold();
                    if (m.Signer?.RegistrationNo is { } reg) s.Item().AlignRight().Text($"Reg. No. {reg}").FontSize(8.5f);
                    s.Item().AlignRight().Text($"For {m.Firm.Name}").FontSize(8.5f).FontColor(Muted);
                    if (!m.Draft && m.ApprovedBy is { } a) s.Item().AlignRight().Text($"Reviewed by {a}").FontSize(7.5f).FontColor(Muted);
                });
            });
        });
    }

    private static void Photos(IContainer c, ReportModel m)
    {
        var two = m.PhotosPerPage <= 2;
        var height = two ? 300f : 210f;
        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn();
                if (!two) cd.RelativeColumn();
            });
            foreach (var p in m.Photos)
            {
                t.Cell().Padding(5).ShowEntire().Column(ph =>
                {
                    ph.Item().Height(height).Background(Soft).AlignCenter().AlignMiddle().Image(p.Image).FitArea();
                    ph.Item().PaddingTop(3).Text(t2 =>
                    {
                        t2.Span($"{p.Number}. {p.Label}").Bold();
                        if (p.Caption is { } cap) t2.Span($": {cap}");
                    });
                    ph.Item().Text(Join(" · ",
                        p.TakenAt.ToOffset(TimeSpan.FromHours(5.5)).ToString("dd MMM yyyy, h:mm tt", India),
                        p.Latitude is { } la && p.Longitude is { } lo ? $"{la.ToString("0.00000", CultureInfo.InvariantCulture)}, {lo.ToString("0.00000", CultureInfo.InvariantCulture)}" : null) ?? "").FontSize(7.5f).FontColor(Muted);
                });
            }
        });
    }

    // ---------------- building blocks ----------------

    private static void Section(ColumnDescriptor col, string number, string title, Action<IContainer> body)
    {
        col.Item().EnsureSpace(90).Column(s =>
        {
            s.Item().PaddingTop(4).PaddingBottom(4).BorderBottom(1).BorderColor(Line)
                .Text(string.IsNullOrEmpty(number) ? title : $"{number}. {title}").FontSize(11).Bold();
            s.Item().PaddingTop(4).Element(body);
        });
    }

    private static void KeyValues(IContainer c, (string Label, string? Value)[] rows)
    {
        c.Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
            foreach (var (label, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
            {
                t.Cell().Element(Cell).Text(label).FontColor(Muted);
                t.Cell().Element(Cell).Text(value!);
            }
        });
    }

    private static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3.5f).PaddingHorizontal(4);

    private static void Head(TableCellDescriptor h, string text, bool right = false)
    {
        var cell = h.Cell().Background(Soft).BorderBottom(0.8f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(4);
        (right ? cell.AlignRight() : cell).Text(text).FontSize(8).Bold().FontColor(Muted);
    }

    // ---------------- formatting ----------------

    private static string N(decimal v) => IndianNumber.Format(v);
    private static string Day(DateOnly d) => d.ToString("dd MMM yyyy", India);
    private static string? Km(decimal? v) => v is { } k ? $"{N(k)} km" : null;
    private static string? YesNo(bool? b) => b switch { true => "Yes", false => "No", _ => null };

    private static string? Join(string sep, params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return kept.Length == 0 ? null : string.Join(sep, kept);
    }

    private static string? Type(PropertyType? t) => t switch
    {
        PropertyType.ResidentialFlat => "Residential flat",
        PropertyType.IndependentHouse => "Independent house",
        PropertyType.CommercialShop => "Commercial shop",
        PropertyType.OfficeSpace => "Office space",
        PropertyType.IndustrialShed => "Industrial shed",
        PropertyType.AgriculturalLand => "Agricultural land",
        PropertyType.OpenPlot => "Open plot",
        PropertyType.Bungalow => "Bungalow",
        PropertyType.RowHouse => "Row house",
        PropertyType.Warehouse => "Warehouse",
        _ => null,
    };

    private static string? Occupancy(string? o) => o switch
    {
        "Owner" => "Self-occupied by the owner",
        "Tenant" => "Let out to a tenant",
        "PartlyRented" => "Partly let out",
        "Vacant" => "Vacant",
        "UnderConstruction" => "Under construction",
        _ => o,
    };

    private static string? Plan(string? p) => p switch { "Yes" => "Seen", "No" => "Not shown", "NotAvailable" => "Not available", _ => p };
    private static string? Oc(string? o) => o switch { "Yes" => "Available", "No" => "Not obtained", "NotSeen" => "Not seen", "NotApplicable" => "Not applicable", _ => o };
}
