using System.Text.RegularExpressions;
using FluentValidation;
using ValuPro.Application.Common;
using ValuPro.Domain.Reports;

namespace ValuPro.Application.Reports;

public sealed record ReportFormatItem(Guid Id, string Name, string? Description, bool IsDefault, IReadOnlyList<string> Banks, DateTimeOffset UpdatedAt);

public sealed record ReportFormatDetail(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    string? Title,
    string? Subtitle,
    string? AddressTo,
    ReportLayout Layout,
    string? Declaration,
    string? LimitingConditions,
    string? OpinionWording,
    IReadOnlyList<string> Banks);

public sealed record SaveReportFormatRequest(
    string Name,
    string? Description,
    string? Title,
    string? Subtitle,
    string? AddressTo,
    ReportLayout Layout,
    string? Declaration,
    string? LimitingConditions,
    string? OpinionWording);

public sealed record Placeholder(string Token, string Meaning);

/// <summary>What the format editor offers: the sections, the placeholders and the standard wording.</summary>
public sealed record ReportFormatCatalog(
    IReadOnlyList<ReportSectionInfo> Sections,
    IReadOnlyList<Placeholder> DeclarationPlaceholders,
    IReadOnlyList<Placeholder> OpinionPlaceholders,
    string StandardDeclaration,
    string StandardOpinion,
    string StandardTitle,
    string StandardAddressTo);

/// <summary>What the template needs from the bank's format (already resolved to the bank's or the firm's default).</summary>
public sealed record ReportFormatView(
    string? Title,
    string? Subtitle,
    string AddressTo,
    IReadOnlyList<ReportSectionSetting> Sections,
    ReportLayout Layout,
    IReadOnlyList<string> LimitingConditions,
    string? OpinionWording)
{
    public static ReportFormatView Standard { get; } = From(null);

    public static ReportFormatView From(ReportFormat? f)
    {
        var layout = f?.Layout ?? ReportLayout.Standard();
        return new ReportFormatView(
            Blank(f?.Title), Blank(f?.Subtitle), Blank(f?.AddressTo) ?? ReportFormatText.StandardAddressTo, layout.Normalized(), layout,
            (f?.LimitingConditions ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
            Blank(f?.OpinionWording));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class ReportFormatText
{
    public const string StandardTitle = "VALUATION REPORT";
    public const string StandardAddressTo = "The Branch Manager";
    public const string StandardOpinion = "In my opinion, the fair market value of the above property as on {date} is {value} ({valueWords}).";

    public static readonly IReadOnlyList<Placeholder> DeclarationPlaceholders =
    [
        new("{inspectionDate}", "Date of inspection"),
        new("{bank}", "Bank name"),
        new("{valuer}", "Valuer's name"),
    ];

    public static readonly IReadOnlyList<Placeholder> OpinionPlaceholders =
    [
        new("{date}", "Date of valuation"),
        new("{value}", "Fair market value, e.g. ₹ 84,78,000"),
        new("{valueWords}", "Fair market value in words"),
        new("{realizable}", "Realizable value"),
        new("{distress}", "Distress / forced sale value"),
        new("{guideline}", "Government (guideline) value"),
    ];

    private static readonly Regex Token = new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>Placeholders in the text that aren't in the allowed list (typos like {vaule}).</summary>
    public static IReadOnlyList<string> Unknown(string? text, IReadOnlyList<Placeholder> allowed) =>
        text is null ? [] : Token.Matches(text).Select(m => m.Value).Where(t => allowed.All(a => a.Token != t)).Distinct().ToList();

    /// <summary>Fills placeholders; the parts are returned so the template can print the values in bold.</summary>
    public static IReadOnlyList<(string Text, bool Value)> Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        var parts = new List<(string, bool)>();
        var at = 0;
        foreach (Match m in Token.Matches(text))
        {
            if (m.Index > at) parts.Add((text[at..m.Index], false));
            parts.Add(values.TryGetValue(m.Value, out var v) ? (v, true) : (m.Value, false));
            at = m.Index + m.Length;
        }
        if (at < text.Length) parts.Add((text[at..], false));
        return parts;
    }

    public static string FillPlain(string text, IReadOnlyDictionary<string, string> values) =>
        string.Concat(Fill(text, values).Select(p => p.Text));

    /// <summary>The format's declaration with its placeholders filled, or the standard one.</summary>
    public static string Declaration(string? formatDeclaration, string inspectedOn, string bank, string valuer) =>
        string.IsNullOrWhiteSpace(formatDeclaration)
            ? ReportText.DefaultDeclaration(inspectedOn)
            : FillPlain(formatDeclaration.Trim(), new Dictionary<string, string> { ["{inspectionDate}"] = inspectedOn, ["{bank}"] = bank, ["{valuer}"] = valuer });
}

public sealed class SaveReportFormatRequestValidator : AbstractValidator<SaveReportFormatRequest>
{
    public SaveReportFormatRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name the format, e.g. SBI Format 3.2.").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Title).MaximumLength(100);
        RuleFor(x => x.Subtitle).MaximumLength(150);
        RuleFor(x => x.AddressTo).MaximumLength(100);
        RuleFor(x => x.Declaration).MaximumLength(4000);
        RuleFor(x => x.LimitingConditions).MaximumLength(4000);
        RuleFor(x => x.OpinionWording).MaximumLength(600);
        RuleFor(x => x.Layout).NotNull();
        RuleFor(x => x.Layout.Sections).Must(s => s.Count <= 30).When(x => x.Layout is not null);
        RuleForEach(x => x.Layout.Sections).ChildRules(s =>
        {
            s.RuleFor(x => x.Key).Must(k => ReportSections.Find(k) is not null).WithMessage("Unknown section.");
            s.RuleFor(x => x.Title).MaximumLength(100);
        }).When(x => x.Layout is not null);
        RuleFor(x => x.Layout).Must(l => l.Normalized().Any(s => s.Include)).WithMessage("Keep at least one section in the report.")
            .When(x => x.Layout is not null);
        RuleFor(x => x.Declaration).Must(d => ReportFormatText.Unknown(d, ReportFormatText.DeclarationPlaceholders).Count == 0)
            .WithMessage(x => $"Unknown placeholder {string.Join(", ", ReportFormatText.Unknown(x.Declaration, ReportFormatText.DeclarationPlaceholders))}. Use {string.Join(", ", ReportFormatText.DeclarationPlaceholders.Select(p => p.Token))}.");
        RuleFor(x => x.OpinionWording).Must(d => ReportFormatText.Unknown(d, ReportFormatText.OpinionPlaceholders).Count == 0)
            .WithMessage(x => $"Unknown placeholder {string.Join(", ", ReportFormatText.Unknown(x.OpinionWording, ReportFormatText.OpinionPlaceholders))}. Use {string.Join(", ", ReportFormatText.OpinionPlaceholders.Select(p => p.Token))}.");
    }
}

public interface IReportFormatService
{
    ReportFormatCatalog Catalog();
    Task<IReadOnlyList<ReportFormatItem>> ListAsync(CancellationToken ct = default);
    Task<Result<ReportFormatDetail>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<ReportFormatDetail>> CreateAsync(SaveReportFormatRequest request, CancellationToken ct = default);
    Task<Result<ReportFormatDetail>> UpdateAsync(Guid id, SaveReportFormatRequest request, CancellationToken ct = default);
    /// <summary>A copy to start a new bank's format from.</summary>
    Task<Result<ReportFormatDetail>> DuplicateAsync(Guid id, CancellationToken ct = default);
    /// <summary>The format banks without their own use.</summary>
    Task<Result<ReportFormatDetail>> MakeDefaultAsync(Guid id, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>A sample report (draft) in the given format, so the editor can show what it looks like before saving.</summary>
    Task<byte[]> PreviewAsync(SaveReportFormatRequest request, bool stage, CancellationToken ct = default);
}
