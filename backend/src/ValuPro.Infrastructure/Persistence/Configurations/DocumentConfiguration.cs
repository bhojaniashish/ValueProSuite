using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Documents;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class CaseDocumentConfiguration : IEntityTypeConfiguration<CaseDocument>
{
    public void Configure(EntityTypeBuilder<CaseDocument> b)
    {
        b.ToTable("CaseDocuments");
        b.HasIndex(x => x.AssignmentId);
        b.HasIndex(x => x.PropertyId);
        b.HasIndex(x => x.ValidUntil);
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Availability).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Number).HasMaxLength(80);
        b.Property(x => x.IssuedBy).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DocumentFileConfiguration : IEntityTypeConfiguration<DocumentFile>
{
    public void Configure(EntityTypeBuilder<DocumentFile> b)
    {
        b.ToTable("DocumentFiles");
        b.HasIndex(x => new { x.DocumentId, x.Version });
        b.Property(x => x.BlobPath).HasMaxLength(400).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(80).IsRequired();
        b.Property(x => x.UploadedByName).HasMaxLength(120).IsRequired();
    }
}
