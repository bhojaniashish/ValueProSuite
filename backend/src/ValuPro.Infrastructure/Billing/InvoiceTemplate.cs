using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ValuPro.Application.Billing;
using ValuPro.Domain.Billing;
using ValuPro.Domain.Valuations;

namespace ValuPro.Infrastructure.Billing;

/// <summary>A GST tax invoice (A4). QuestPDF Community licence, as for reports (see README).</summary>
public sealed class InvoiceTemplate : IInvoiceRenderer
{
    private const string Ink = "#1F2937";
    private const string Muted = "#6B7280";
    private const string Line = "#E5E7EB";
    private const string Soft = "#F7F8FB";
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    static InvoiceTemplate() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(InvoiceDetail d, InvoiceExtras x) => Build(d, x).GeneratePdf();

    public IDocument Build(InvoiceDetail d, InvoiceExtras x) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(1.6f, Unit.Centimetre);
            page.MarginVertical(1.3f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(9.3f).FontColor(Ink).LineHeight(1.25f));
            var mark = d.Status switch { InvoiceStatus.Draft => "DRAFT", InvoiceStatus.Cancelled => "CANCELLED", _ => null };
            if (mark is not null)
                page.Background().AlignCenter().AlignMiddle().Rotate(-35).Text(mark).FontSize(100).Bold().FontColor("#EEF0F4");
            page.Content().Column(col => Body(col, d, x));
            page.Footer().AlignCenter().Text("This is a computer-generated invoice.").FontSize(7.5f).FontColor(Muted);
        }));

    private static void Body(ColumnDescriptor col, InvoiceDetail d, InvoiceExtras x)
    {
        col.Spacing(12);

        // Seller
        col.Item().Row(row =>
        {
            if (x.Logo is { Length: > 0 } logo) row.ConstantItem(54).Height(46).AlignMiddle().Image(logo).FitArea();
            row.RelativeItem().PaddingLeft(x.Logo is null ? 0 : 10).Column(f =>
            {
                f.Item().Text(d.Firm.Name).FontSize(15).Bold().FontColor(x.Color);
                if (d.Firm.Address is { } a) f.Item().Text(a).FontSize(8.5f).FontColor(Muted);
                var contact = string.Join("  ·  ", new[] { x.Phone, x.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (contact.Length > 0) f.Item().Text(contact).FontSize(8.5f).FontColor(Muted);
                if (d.Firm.Gstin is { } g) f.Item().Text(t => { t.Span("GSTIN ").FontColor(Muted); t.Span(g).Bold(); if (d.Firm.Pan is { } p) { t.Span("   PAN ").FontColor(Muted); t.Span(p); } });
            });
            row.ConstantItem(190).AlignRight().Column(r =>
            {
                r.Item().AlignRight().Text("TAX INVOICE").FontSize(16).Bold().LetterSpacing(0.05f);
                r.Item().AlignRight().Text(t => { t.Span("Invoice no. ").FontColor(Muted); t.Span(d.Number ?? "Draft").Bold(); });
                r.Item().AlignRight().Text(t => { t.Span("Date ").FontColor(Muted); t.Span(Day(d.InvoiceDate)); });
                r.Item().AlignRight().Text(t => { t.Span("Due by ").FontColor(Muted); t.Span(Day(d.DueDate)); });
            });
        });
        col.Item().LineHorizontal(1.5f).LineColor(x.Color);

        // Buyer
        col.Item().Row(row =>
        {
            row.RelativeItem().Column(b =>
            {
                b.Item().Text("Bill to").FontSize(8).FontColor(Muted);
                b.Item().Text(d.BillToName).Bold();
                if (d.BillToAddress is { } a) b.Item().Text(a);
                if (d.BillToGstin is { } g) b.Item().Text(t => { t.Span("GSTIN ").FontColor(Muted); t.Span(g); });
            });
            row.ConstantItem(200).AlignRight().Column(r =>
            {
                if (d.PlaceOfSupply is { } pos) r.Item().AlignRight().Text(t => { t.Span("Place of supply ").FontColor(Muted); t.Span(pos); });
                r.Item().AlignRight().Text(d.InterState ? "Inter-state supply (IGST)" : "Intra-state supply (CGST + SGST)").FontSize(8).FontColor(Muted);
                r.Item().AlignRight().Text("Reverse charge: No").FontSize(8).FontColor(Muted);
            });
        });

        // Lines
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.ConstantColumn(22); c.RelativeColumn(); c.ConstantColumn(56); c.ConstantColumn(90); });
            t.Header(h =>
            {
                Head(h, "#");
                Head(h, "Description");
                Head(h, "SAC");
                Head(h, "Amount (₹)", right: true);
            });
            var n = 0;
            foreach (var l in d.Lines)
            {
                n++;
                t.Cell().Element(Cell).Text(n.ToString(CultureInfo.InvariantCulture)).FontColor(Muted);
                t.Cell().Element(Cell).Text(l.Description);
                t.Cell().Element(Cell).Text(l.Sac ?? "").FontColor(Muted);
                t.Cell().Element(Cell).AlignRight().Text(Money(l.Amount));
            }
        });

        // Totals
        col.Item().Row(row =>
        {
            row.RelativeItem().PaddingRight(20).Column(w =>
            {
                w.Item().Text("Amount in words").FontSize(8).FontColor(Muted);
                w.Item().Text(d.TotalInWords).Italic();
            });
            row.ConstantItem(230).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(90); });
                void Row(string label, decimal v, bool strong = false)
                {
                    var a = t.Cell().Element(c => strong ? Cell(c.Background(Soft)) : Cell(c)).Text(label);
                    var b = t.Cell().Element(c => strong ? Cell(c.Background(Soft)) : Cell(c)).AlignRight().Text(Money(v));
                    if (strong) { a.Bold(); b.Bold().FontColor(x.Color); }
                }
                Row("Taxable value", d.Taxable);
                if (d.InterState) Row($"IGST @ {Pct(d.GstRate)}%", d.Igst);
                else
                {
                    Row($"CGST @ {Pct(d.GstRate / 2)}%", d.Cgst);
                    Row($"SGST @ {Pct(d.GstRate / 2)}%", d.Sgst);
                }
                if (d.RoundOff != 0) Row("Round off", d.RoundOff);
                Row("Total", d.Total, strong: true);
                if (d.Status == InvoiceStatus.Issued && d.Received + d.Tds > 0)
                {
                    if (d.Received > 0) Row("Received", d.Received);
                    if (d.Tds > 0) Row("TDS deducted", d.Tds);
                    Row("Balance due", d.Outstanding, strong: true);
                }
            });
        });

        if (d.Status == InvoiceStatus.Cancelled)
            col.Item().Background("#FDECEC").Padding(8).Text($"Cancelled on {d.CancelledAt?.ToOffset(TimeSpan.FromHours(5.5)).ToString("dd MMM yyyy", India)}: {d.CancelReason}").FontColor("#B91C1C");

        if (d.Notes is { } notes) col.Item().Text(notes).FontColor(Muted);

        col.Item().Row(row =>
        {
            row.RelativeItem().Column(p =>
            {
                if (x.PaymentDetails is { } pay)
                {
                    p.Item().Text("Payment details").Bold().FontColor(x.Color);
                    p.Item().Text(pay);
                }
                if (x.Terms is { } terms)
                {
                    p.Item().PaddingTop(6).Text("Terms").Bold().FontColor(x.Color);
                    p.Item().Text(terms).FontSize(8.5f).FontColor(Muted);
                }
            });
            row.ConstantItem(200).AlignRight().AlignBottom().Column(s =>
            {
                s.Item().Height(40);
                s.Item().AlignRight().Text($"For {d.Firm.Name}").Bold();
                s.Item().AlignRight().Text("Authorised signatory").FontSize(8.5f).FontColor(Muted);
            });
        });
    }

    private static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(4);

    private static void Head(TableCellDescriptor h, string text, bool right = false)
    {
        var c = h.Cell().Background(Soft).BorderBottom(1).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(4);
        (right ? c.AlignRight() : c).Text(text).FontSize(8.3f).Bold().FontColor(Muted);
    }

    /// <summary>12,34,567.50 (Indian grouping, always paise).</summary>
    public static string Money(decimal v)
    {
        var r = Math.Round(Math.Abs(v), 2, MidpointRounding.AwayFromZero);
        var whole = Math.Truncate(r);
        var paise = (int)((r - whole) * 100);
        return (v < 0 ? "-" : "") + IndianNumber.Format(whole) + "." + paise.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string Pct(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Day(DateOnly d) => d.ToString("dd MMM yyyy", India);
}
