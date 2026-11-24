namespace ValuPro.Domain.Inspections;

/// <summary>Something still needed before the inspection can be submitted.</summary>
public sealed record MissingItem(string Section, string Message);

public static class InspectionRules
{
    public static readonly string[] Sections = ["visit", "location", "boundaries", "occupancy", "building", "measurements", "approvals", "bank", "photos", "remarks"];

    /// <summary>A location further than this from the property's saved location is flagged for the reviewer.</summary>
    public const int FarFromPropertyMetres = 300;

    /// <summary>Photos every inspection needs.</summary>
    public static readonly PhotoTag[] RequiredPhotos = [PhotoTag.Front, PhotoTag.Road];

    /// <summary>
    /// What is still missing. <paramref name="landOnly"/> (open plot, agricultural land) skips the building details.
    /// Required bank questions are those whose text is in <paramref name="requiredQuestions"/>.
    /// </summary>
    public static IReadOnlyList<MissingItem> Missing(
        Inspection i, IReadOnlyCollection<PhotoTag> photos, bool landOnly, IReadOnlyCollection<string> requiredQuestions)
    {
        var d = i.Data;
        var m = new List<MissingItem>();
        void Need(bool ok, string section, string message)
        {
            if (!ok) m.Add(new MissingItem(section, message));
        }

        Need(i.Latitude is not null, "visit", "Capture the location at site.");
        Need(Has(d.Visit.PersonMet), "visit", "Who did you meet at the property?");
        Need(Has(d.Visit.PersonRelation), "visit", "How is the person met related to the property?");
        Need(d.Visit.IdentifiedBy.Count > 0, "visit", "How was the property identified?");
        Need(d.Visit.AddressMatches is not null, "visit", "Does the address at site match the documents?");
        Need(d.Visit.AddressMatches != false || Has(d.Visit.AddressAtSite), "visit", "Write the address as found at site.");
        Need(Has(d.Visit.Access), "visit", "Could you inspect inside the property?");
        Need(d.Visit.Access is null or "Full" || Has(d.Visit.AccessNote), "visit", "Say why the inside could not be fully inspected.");

        Need(Has(d.Location.LocalityType), "location", "Choose the locality type.");
        Need(Has(d.Location.Development), "location", "How developed is the area?");
        Need(d.Location.RoadWidthFt is > 0, "location", "Enter the approach road width.");

        var b = d.Boundaries;
        Need(Has(b.North.AtSite) && Has(b.South.AtSite) && Has(b.East.AtSite) && Has(b.West.AtSite), "boundaries", "Record all four boundaries as found at site.");
        Need(b.Encroachment is not null, "boundaries", "Is there any encroachment?");
        Need(b.Encroachment != true || Has(b.Note), "boundaries", "Describe the encroachment.");

        Need(Has(d.Occupancy.OccupiedBy), "occupancy", "Who occupies the property?");
        Need(d.Occupancy.OccupiedBy is not ("Tenant" or "PartlyRented") || Has(d.Occupancy.TenantName), "occupancy", "Enter the tenant's name.");

        if (!landOnly)
        {
            Need(Has(d.Building.StructureType), "building", "Choose the structure type.");
            Need(d.Building.AgeYears is not null || d.Occupancy.OccupiedBy == "UnderConstruction", "building", "Enter the age of the building.");
            Need(Has(d.Building.Condition), "building", "Rate the condition of the building.");
            Need(d.Occupancy.OccupiedBy != "UnderConstruction" || d.Building.CompletionPercent is not null, "building", "How much of the construction is complete?");
        }

        var a = d.Measurements;
        var measured = a.LandMeasured is > 0 || a.CarpetMeasured is > 0 || a.BuiltUpMeasured is > 0 || a.Floors.Any(f => f.Measured is > 0);
        Need(measured, "measurements", landOnly ? "Enter the land area measured at site." : "Enter at least one area measured at site.");

        Need(Has(d.Approvals.PlanSeen), "approvals", "Was the approved plan seen?");
        Need(landOnly || Has(d.Approvals.Deviation), "approvals", "Any deviation from the approved plan?");
        Need(d.Approvals.Deviation is null or "None" || Has(d.Approvals.DeviationNote), "approvals", "Describe the deviation.");

        foreach (var q in requiredQuestions)
        {
            var answer = d.BankAnswers.FirstOrDefault(x => string.Equals(x.Question, q, StringComparison.OrdinalIgnoreCase));
            Need(Has(answer?.Answer), "bank", $"Answer: {q}");
        }

        foreach (var tag in RequiredPhotos)
            Need(photos.Contains(tag), "photos", $"Add a photo of the {Label(tag).ToLowerInvariant()}.");

        Need(Has(d.Remarks.Marketability), "remarks", "Rate the marketability.");
        return m;
    }

    public static string Label(PhotoTag t) => t switch
    {
        PhotoTag.Front => "Front elevation",
        PhotoTag.Rear => "Rear elevation",
        PhotoTag.Side => "Side elevation",
        PhotoTag.Interior => "Interior",
        PhotoTag.Kitchen => "Kitchen",
        PhotoTag.Bathroom => "Bathroom",
        PhotoTag.Road => "Approach road",
        PhotoTag.Neighbourhood => "Neighbourhood",
        PhotoTag.NameBoard => "Name board / meter",
        PhotoTag.Selfie => "Valuer at site",
        PhotoTag.Deviation => "Deviation",
        PhotoTag.Document => "Document",
        _ => "Other",
    };

    /// <summary>Great-circle distance in metres (haversine).</summary>
    public static int DistanceMetres(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        const double R = 6_371_000;
        static double Rad(decimal deg) => (double)deg * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return (int)Math.Round(2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h))));
    }

    private static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);
}
