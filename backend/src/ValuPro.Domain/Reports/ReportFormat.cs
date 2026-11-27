using ValuPro.Domain.Common;

namespace ValuPro.Domain.Reports;

/// <summary>
/// A bank's report format: what the report is called, who it is addressed to, which sections it has and in what
/// order, which values it states, and its declaration and limiting conditions. Banks pick a format; banks without
/// one use the firm's default (and with no default, <see cref="ReportLayout.Standard"/>).
/// </summary>
public class ReportFormat : TenantEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>Heading of a valuation report, e.g. "VALUATION REPORT". Stage visits keep their own headings.</summary>
    public string? Title { get; set; }
    /// <summary>Line under the heading, e.g. "As per IBA format".</summary>
    public string? Subtitle { get; set; }
    /// <summary>"To," line, e.g. "The Branch Manager" or "The Chief Manager (Credit)".</summary>
    public string? AddressTo { get; set; }

    public ReportLayout Layout { get; set; } = ReportLayout.Standard();

    /// <summary>Null = the standard declaration. Placeholders: {inspectionDate}, {bank}, {valuer}.</summary>
    public string? Declaration { get; set; }
    /// <summary>Caveats printed after the declaration, one per line.</summary>
    public string? LimitingConditions { get; set; }
    /// <summary>Null = the standard closing sentence. Placeholders: {date}, {value}, {valueWords}, {realizable}, {distress}, {guideline}.</summary>
    public string? OpinionWording { get; set; }
}

/// <summary>Section order and what the valuation section shows. Stored as JSON on the format.</summary>
public sealed class ReportLayout
{
    public List<ReportSectionSetting> Sections { get; set; } = [];
    public bool NumberSections { get; set; } = true;
    public bool ShowWorking { get; set; } = true;
    public bool ShowRealizable { get; set; } = true;
    public bool ShowDistress { get; set; } = true;
    public bool ShowGuideline { get; set; } = true;
    public bool ShowInsurance { get; set; } = true;
    public bool PhotoAnnexure { get; set; } = true;

    /// <summary>A copy with the sections <see cref="Normalized"/>.</summary>
    public ReportLayout Cleaned() => new()
    {
        Sections = Normalized(), NumberSections = NumberSections, ShowWorking = ShowWorking, ShowRealizable = ShowRealizable,
        ShowDistress = ShowDistress, ShowGuideline = ShowGuideline, ShowInsurance = ShowInsurance, PhotoAnnexure = PhotoAnnexure,
    };

    public static ReportLayout Standard() => new() { Sections = [.. ReportSections.All.Select(s => new ReportSectionSetting { Key = s.Key })] };

    /// <summary>
    /// The sections in order with every known section present exactly once: unknown keys are dropped and sections
    /// added in later versions are appended (switched on), so old formats keep working.
    /// </summary>
    public List<ReportSectionSetting> Normalized()
    {
        var seen = new HashSet<string>();
        var list = Sections.Where(s => ReportSections.Find(s.Key) is not null && seen.Add(s.Key))
            .Select(s => new ReportSectionSetting { Key = s.Key, Include = s.Include, Title = string.IsNullOrWhiteSpace(s.Title) ? null : s.Title.Trim() })
            .ToList();
        list.AddRange(ReportSections.All.Where(s => !seen.Contains(s.Key)).Select(s => new ReportSectionSetting { Key = s.Key }));
        return list;
    }
}

public sealed class ReportSectionSetting
{
    public required string Key { get; set; }
    public bool Include { get; set; } = true;
    /// <summary>Null = the standard heading.</summary>
    public string? Title { get; set; }
}

public sealed record ReportSectionInfo(string Key, string Title, string Description, bool StageOnly = false);

/// <summary>The sections the report engine can print (the opinion, declaration and signature always close the report).</summary>
public static class ReportSections
{
    public const string General = "general";
    public const string Property = "property";
    public const string Boundaries = "boundaries";
    public const string Documents = "documents";
    public const string Construction = "construction";
    public const string Building = "building";
    public const string Measurements = "measurements";
    public const string Progress = "progress";
    public const string Valuation = "valuation";
    public const string BankQuestions = "bankQuestions";

    public static readonly IReadOnlyList<ReportSectionInfo> All =
    [
        new(General, "General", "Purpose, applicant, owner, bank reference, dates, person met"),
        new(Property, "Property and location", "Address, survey numbers, locality, road, amenities, GPS, occupancy"),
        new(Boundaries, "Boundaries", "North / south / east / west as per documents and at site"),
        new(Documents, "Documents perused", "Each document with number, date, issuer and whether the original was seen"),
        new(Construction, "Construction progress", "Stage table and overall completion", StageOnly: true),
        new(Building, "Building, construction and approvals", "Structure, age, specifications, approvals and deviations"),
        new(Measurements, "Measurements (sq.ft unless stated)", "Areas as per documents and as measured"),
        new(Progress, "Progress and disbursement", "Value of work done and the release recommended", StageOnly: true),
        new(Valuation, "Valuation", "Method, working and the values"),
        new(BankQuestions, "Bank's additional information", "Answers to the bank's own questions"),
    ];

    public static ReportSectionInfo? Find(string? key) => All.FirstOrDefault(s => s.Key == key);
}
