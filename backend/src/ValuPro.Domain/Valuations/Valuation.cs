using ValuPro.Domain.Common;

namespace ValuPro.Domain.Valuations;

/// <summary>
/// The valuation of one assignment: the valuer's inputs (stored as JSON) and the resulting values, kept as columns so lists,
/// reports and billing can read them without recalculating.
/// </summary>
public class Valuation : TenantEntity
{
    public Guid AssignmentId { get; set; }
    public ValuationInput Input { get; set; } = new();

    public decimal FairMarketValue { get; set; }
    public decimal RealizableValue { get; set; }
    public decimal DistressValue { get; set; }
    public decimal GuidelineValue { get; set; }
    public decimal InsuranceValue { get; set; }

    public DateTimeOffset ValuedAt { get; set; }
    public required string ValuedByName { get; set; }

    /// <summary>Goes up on every save; a save from an older copy is refused.</summary>
    public int Version { get; set; } = 1;

    public void Apply(ValuationResult r)
    {
        FairMarketValue = r.FairMarketValue;
        RealizableValue = r.RealizableValue;
        DistressValue = r.DistressValue;
        GuidelineValue = r.GuidelineValue;
        InsuranceValue = r.InsuranceValue;
    }
}
