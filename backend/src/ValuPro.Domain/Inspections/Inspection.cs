using ValuPro.Domain.Common;

namespace ValuPro.Domain.Inspections;

public enum InspectionStatus
{
    Draft = 0,
    Submitted = 1,
}

/// <summary>
/// The site visit for one assignment: where and when the valuer was, and what they recorded.
/// The recorded details live in <see cref="Data"/> (stored as JSON) so fields can grow as banks' formats are collected.
/// </summary>
public class Inspection : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public InspectionStatus Status { get; set; } = InspectionStatus.Draft;

    public DateTimeOffset StartedAt { get; set; }
    public Guid? StartedByUserId { get; set; }
    public required string StartedByName { get; set; }

    /// <summary>Where the valuer's phone was when the location was captured at site.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? AccuracyMetres { get; set; }
    public DateTimeOffset? LocatedAt { get; set; }
    /// <summary>Distance from the location saved on the property record, when both are known.</summary>
    public int? DistanceFromPropertyMetres { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SubmittedByName { get; set; }

    /// <summary>Goes up by one on every save; a save carrying an older number is refused, so two devices cannot overwrite each other.</summary>
    public int Version { get; set; } = 1;

    public InspectionData Data { get; set; } = new();
}

public enum PhotoTag
{
    Front = 0,
    Rear = 1,
    Side = 2,
    Interior = 3,
    Kitchen = 4,
    Bathroom = 5,
    Road = 6,
    Neighbourhood = 7,
    NameBoard = 8,
    Selfie = 9,
    Deviation = 10,
    Document = 11,
    Other = 12,
}

/// <summary>Where a photo came from: the phone camera at site, or added later (office upload, phone gallery).</summary>
public enum PhotoSource
{
    Site = 0,
    Upload = 1,
}

/// <summary>A photo taken for an assignment (at site or later). The Photos module and reports use the same records.</summary>
public class AssignmentPhoto : TenantEntity
{
    public Guid AssignmentId { get; set; }
    /// <summary>Id made on the phone before upload, so a retried upload is not saved twice.</summary>
    public Guid ClientKey { get; set; }
    public PhotoTag Tag { get; set; }
    public string? Caption { get; set; }

    public required string BlobPath { get; set; }
    public required string ThumbPath { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public required string TakenByName { get; set; }
    /// <summary>Position in the report; the Photos page lets the office reorder.</summary>
    public int SortOrder { get; set; }

    public PhotoSource Source { get; set; }
    /// <summary>Date, time and location printed on the photo by the phone (camera photos only).</summary>
    public bool Stamped { get; set; }
    /// <summary>Banks want a handful of photos; the rest stay on file.</summary>
    public bool IncludeInReport { get; set; } = true;
    /// <summary>From the property's saved location, when both are known (worked out on upload).</summary>
    public int? DistanceFromPropertyMetres { get; set; }
}

// ---------------- recorded details ----------------

public sealed class InspectionData
{
    public VisitSection Visit { get; set; } = new();
    public LocationSection Location { get; set; } = new();
    public BoundariesSection Boundaries { get; set; } = new();
    public OccupancySection Occupancy { get; set; } = new();
    public BuildingSection Building { get; set; } = new();
    public MeasurementsSection Measurements { get; set; } = new();
    public ApprovalsSection Approvals { get; set; } = new();
    /// <summary>The bank's own questions, copied with the answer so later edits to the questionnaire don't change past inspections.</summary>
    public List<BankAnswer> BankAnswers { get; set; } = [];
    public RemarksSection Remarks { get; set; } = new();
}

public sealed class VisitSection
{
    public string? PersonMet { get; set; }
    /// <summary>Owner, Applicant, Tenant, Family member, Builder / Developer, Caretaker, Neighbour, Other.</summary>
    public string? PersonRelation { get; set; }
    public string? PersonPhone { get; set; }
    /// <summary>How the valuer confirmed this is the right property.</summary>
    public List<string> IdentifiedBy { get; set; } = [];
    public bool? AddressMatches { get; set; }
    public string? AddressAtSite { get; set; }
    /// <summary>Full, OutsideOnly, NoEntry.</summary>
    public string? Access { get; set; }
    public string? AccessNote { get; set; }
}

public sealed class LocationSection
{
    /// <summary>Urban, Semi-urban, Rural.</summary>
    public string? LocalityType { get; set; }
    /// <summary>High, Middle, Low income; Commercial; Industrial.</summary>
    public string? LocalityClass { get; set; }
    /// <summary>Fully developed, Developing, Undeveloped.</summary>
    public string? Development { get; set; }
    public decimal? RoadWidthFt { get; set; }
    public string? RoadType { get; set; }
    public string? Facing { get; set; }
    public bool? CornerPlot { get; set; }
    public string? Landmark { get; set; }
    public decimal? StationKm { get; set; }
    public decimal? BusStopKm { get; set; }
    public decimal? CityCentreKm { get; set; }
    public List<string> Amenities { get; set; } = [];
    /// <summary>Things that lower value or lending appetite: high-tension line, nala, cemetery, flood-prone…</summary>
    public List<string> Negatives { get; set; } = [];
    public string? Note { get; set; }
}

public sealed class Boundary
{
    public string? AsPerDocuments { get; set; }
    public string? AtSite { get; set; }
    public bool? Matches { get; set; }
}

public sealed class BoundariesSection
{
    public Boundary North { get; set; } = new();
    public Boundary South { get; set; } = new();
    public Boundary East { get; set; } = new();
    public Boundary West { get; set; } = new();
    public bool? Encroachment { get; set; }
    public string? Note { get; set; }
}

public sealed class OccupancySection
{
    /// <summary>Owner, Tenant, PartlyRented, Vacant, UnderConstruction.</summary>
    public string? OccupiedBy { get; set; }
    public string? TenantName { get; set; }
    public decimal? MonthlyRent { get; set; }
    public decimal? OccupiedSinceYears { get; set; }
    public string? Note { get; set; }
}

public sealed class BuildingSection
{
    /// <summary>Matches a depreciation rule's structure type in the Rate Master.</summary>
    public string? StructureType { get; set; }
    public int? AgeYears { get; set; }
    public int? TotalFloors { get; set; }
    public string? UnitFloor { get; set; }
    /// <summary>Good, Average, Poor.</summary>
    public string? Quality { get; set; }
    /// <summary>Excellent, Good, Average, Poor, Dilapidated.</summary>
    public string? Condition { get; set; }
    /// <summary>For property under construction.</summary>
    public int? CompletionPercent { get; set; }
    public string? Foundation { get; set; }
    public string? Walls { get; set; }
    public string? Roof { get; set; }
    public string? Flooring { get; set; }
    public string? DoorsWindows { get; set; }
    public string? Kitchen { get; set; }
    public string? Toilets { get; set; }
    public string? Electrical { get; set; }
    public string? Plumbing { get; set; }
    public bool? Lift { get; set; }
    /// <summary>Covered, Open, Stilt, None.</summary>
    public string? Parking { get; set; }
    public int? ParkingSlots { get; set; }
    public List<string> Amenities { get; set; } = [];
    public List<string> Defects { get; set; } = [];
    public string? Note { get; set; }
}

public sealed class FloorArea
{
    public string? Name { get; set; }
    public decimal? AsPerDocuments { get; set; }
    public decimal? Measured { get; set; }
}

public sealed class MeasurementsSection
{
    /// <summary>SqFt or SqM.</summary>
    public string Unit { get; set; } = "SqFt";
    public decimal? PlotLengthFt { get; set; }
    public decimal? PlotBreadthFt { get; set; }
    public decimal? LandAsPerDocuments { get; set; }
    public decimal? LandMeasured { get; set; }
    public decimal? CarpetAsPerDocuments { get; set; }
    public decimal? CarpetMeasured { get; set; }
    public decimal? BuiltUpAsPerDocuments { get; set; }
    public decimal? BuiltUpMeasured { get; set; }
    public decimal? SuperBuiltUpAsPerDocuments { get; set; }
    public List<FloorArea> Floors { get; set; } = [];
    public string? Note { get; set; }
}

public sealed class ApprovalsSection
{
    /// <summary>Yes, No, NotAvailable.</summary>
    public string? PlanSeen { get; set; }
    public string? PlanAuthority { get; set; }
    public string? PlanNumber { get; set; }
    /// <summary>None, Minor, Major.</summary>
    public string? Deviation { get; set; }
    public string? DeviationNote { get; set; }
    /// <summary>Yes, No, NotApplicable, NotSeen.</summary>
    public string? OccupancyCertificate { get; set; }
    public string? Note { get; set; }
}

public sealed class BankAnswer
{
    public required string Question { get; set; }
    public string? Answer { get; set; }
}

public sealed class RemarksSection
{
    /// <summary>Good, Average, Poor.</summary>
    public string? Marketability { get; set; }
    public string? Text { get; set; }
}
