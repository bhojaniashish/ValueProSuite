using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Reports;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class ReportFormatConfiguration : IEntityTypeConfiguration<ReportFormat>
{
    private static readonly JsonSerializerOptions Json = InspectionConfiguration.Json;

    public void Configure(EntityTypeBuilder<ReportFormat> b)
    {
        b.ToTable("ReportFormats");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Title).HasMaxLength(100);
        b.Property(x => x.Subtitle).HasMaxLength(150);
        b.Property(x => x.AddressTo).HasMaxLength(100);
        b.Property(x => x.Declaration).HasMaxLength(4000);
        b.Property(x => x.LimitingConditions).HasMaxLength(4000);
        b.Property(x => x.OpinionWording).HasMaxLength(600);
        // Blank (e.g. rows created by the migration from the old free-text format names) = the standard layout.
        b.Property(x => x.Layout)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                s => string.IsNullOrWhiteSpace(s) ? ReportLayout.Standard() : JsonSerializer.Deserialize<ReportLayout>(s, Json) ?? ReportLayout.Standard(),
                new ValueComparer<ReportLayout>(
                    (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => JsonSerializer.Deserialize<ReportLayout>(JsonSerializer.Serialize(v, Json), Json)!));
    }
}
