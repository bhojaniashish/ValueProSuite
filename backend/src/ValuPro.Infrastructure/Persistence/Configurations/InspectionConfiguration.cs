using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Inspections;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    /// <summary>The recorded details are one JSON document, so new fields need no schema change.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Configure(EntityTypeBuilder<Inspection> b)
    {
        b.ToTable("Inspections");
        b.HasIndex(x => x.AssignmentId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.StartedByName).HasMaxLength(120).IsRequired();
        b.Property(x => x.SubmittedByName).HasMaxLength(120);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Data)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                s => JsonSerializer.Deserialize<InspectionData>(s, Json) ?? new InspectionData(),
                new ValueComparer<InspectionData>(
                    (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => JsonSerializer.Deserialize<InspectionData>(JsonSerializer.Serialize(v, Json), Json)!));
    }
}

public sealed class AssignmentPhotoConfiguration : IEntityTypeConfiguration<AssignmentPhoto>
{
    public void Configure(EntityTypeBuilder<AssignmentPhoto> b)
    {
        b.ToTable("AssignmentPhotos");
        b.HasIndex(x => new { x.AssignmentId, x.SortOrder });
        b.HasIndex(x => new { x.AssignmentId, x.ClientKey }).IsUnique();
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Tag).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Caption).HasMaxLength(200);
        b.Property(x => x.BlobPath).HasMaxLength(400).IsRequired();
        b.Property(x => x.ThumbPath).HasMaxLength(400).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
        b.Property(x => x.TakenByName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
    }
}
