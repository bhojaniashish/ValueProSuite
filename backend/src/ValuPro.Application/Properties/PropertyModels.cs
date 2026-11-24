using ValuPro.Application.Assignments;
using ValuPro.Domain.Properties;

namespace ValuPro.Application.Properties;

public sealed record PropertyItem(
    Guid Id,
    string Code,
    PropertyType Type,
    string OwnerName,
    Guid? OwnerClientId,
    string Address,
    string? City,
    string? State,
    string? Pincode,
    string? SurveyNo,
    string? CtsNo,
    string? PlotNo,
    decimal? LandArea,
    decimal? BuiltUpArea,
    string? Zone,
    PropertyUsage? Usage,
    decimal? Latitude,
    decimal? Longitude,
    string? Remarks,
    DateTimeOffset CreatedAt);

public sealed record PropertyDetail(PropertyItem Property, IReadOnlyList<AssignmentListItem> Assignments);

public sealed record PropertyQuery(
    string? Search = null,
    PropertyType? Type = null,
    string? City = null,
    string? Zone = null,
    string? Sort = null,
    bool Desc = false,
    int Page = 1,
    int PageSize = 20);

public sealed record PropertyFilters(IReadOnlyList<string> Cities, IReadOnlyList<string> Zones, int Total);

public sealed record SavePropertyRequest(
    PropertyType Type,
    string OwnerName,
    Guid? OwnerClientId,
    string Address,
    string? City,
    string? State,
    string? Pincode,
    string? SurveyNo,
    string? CtsNo,
    string? PlotNo,
    decimal? LandArea,
    decimal? BuiltUpArea,
    string? Zone,
    PropertyUsage? Usage,
    decimal? Latitude,
    decimal? Longitude,
    string? Remarks);

public static class PropertyZones
{
    /// <summary>Suggested zones (from the prototype); a firm may type others.</summary>
    public static readonly IReadOnlyList<string> Suggested =
        ["Residential R1", "Residential R2", "Commercial C1", "Industrial", "Agricultural", "Mixed Use"];
}
