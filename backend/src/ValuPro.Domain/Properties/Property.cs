using ValuPro.Domain.Common;

namespace ValuPro.Domain.Properties;

/// <summary>The ten property types from the prototype.</summary>
public enum PropertyType
{
    ResidentialFlat = 0,
    IndependentHouse = 1,
    CommercialShop = 2,
    OfficeSpace = 3,
    IndustrialShed = 4,
    AgriculturalLand = 5,
    OpenPlot = 6,
    Bungalow = 7,
    RowHouse = 8,
    Warehouse = 9,
}

public enum PropertyUsage
{
    SelfOccupied = 0,
    Rented = 1,
    Vacant = 2,
    UnderConstruction = 3,
}

/// <summary>A property the firm has valued or will value. One property can have many assignments over the years.</summary>
public class Property : TenantEntity
{
    /// <summary>Readable code, e.g. PR0012. Unique per firm, never reused.</summary>
    public required string Code { get; set; }

    public PropertyType Type { get; set; }
    public required string OwnerName { get; set; }
    /// <summary>Optional link to the owner's client record.</summary>
    public Guid? OwnerClientId { get; set; }

    public required string Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }

    public string? SurveyNo { get; set; }
    public string? CtsNo { get; set; }
    public string? PlotNo { get; set; }

    /// <summary>Square feet.</summary>
    public decimal? LandArea { get; set; }
    /// <summary>Square feet.</summary>
    public decimal? BuiltUpArea { get; set; }

    public string? Zone { get; set; }
    public PropertyUsage? Usage { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public string? Remarks { get; set; }
}
