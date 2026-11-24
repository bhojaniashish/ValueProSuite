using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Valuations;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class ValuationConfiguration : IEntityTypeConfiguration<Valuation>
{
    public void Configure(EntityTypeBuilder<Valuation> b)
    {
        var json = InspectionConfiguration.Json;
        b.ToTable("Valuations");
        b.HasIndex(x => x.AssignmentId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.FairMarketValue).HasPrecision(16, 2);
        b.Property(x => x.RealizableValue).HasPrecision(16, 2);
        b.Property(x => x.DistressValue).HasPrecision(16, 2);
        b.Property(x => x.GuidelineValue).HasPrecision(16, 2);
        b.Property(x => x.InsuranceValue).HasPrecision(16, 2);
        b.Property(x => x.ValuedByName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Input)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, json),
                s => JsonSerializer.Deserialize<ValuationInput>(s, json) ?? new ValuationInput(),
                new ValueComparer<ValuationInput>(
                    (a, c) => JsonSerializer.Serialize(a, json) == JsonSerializer.Serialize(c, json),
                    v => JsonSerializer.Serialize(v, json).GetHashCode(),
                    v => JsonSerializer.Deserialize<ValuationInput>(JsonSerializer.Serialize(v, json), json)!));
    }
}
