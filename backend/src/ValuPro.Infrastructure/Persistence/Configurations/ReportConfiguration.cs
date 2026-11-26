using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Reports;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class CaseReportConfiguration : IEntityTypeConfiguration<CaseReport>
{
    public void Configure(EntityTypeBuilder<CaseReport> b)
    {
        b.ToTable("CaseReports");
        b.HasIndex(x => x.AssignmentId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Opinion).HasMaxLength(4000);
        b.Property(x => x.Declaration).HasMaxLength(4000);
        b.Property(x => x.Place).HasMaxLength(100);
    }
}

public sealed class ReportEmailConfiguration : IEntityTypeConfiguration<ReportEmail>
{
    public void Configure(EntityTypeBuilder<ReportEmail> b)
    {
        b.ToTable("ReportEmails");
        b.HasIndex(x => x.AssignmentId);
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ReportFile>().WithMany().HasForeignKey(x => x.ReportFileId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.To).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Cc).HasMaxLength(1000);
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.SentByName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Error).HasMaxLength(500);
    }
}

public sealed class ReportFileConfiguration : IEntityTypeConfiguration<ReportFile>
{
    public void Configure(EntityTypeBuilder<ReportFile> b)
    {
        b.ToTable("ReportFiles");
        b.HasIndex(x => new { x.AssignmentId, x.Number });
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.BlobPath).HasMaxLength(400).IsRequired();
        b.Property(x => x.GeneratedByName).HasMaxLength(120).IsRequired();
        b.Property(x => x.SignedByName).HasMaxLength(120);
        b.Property(x => x.SignatureMethod).HasMaxLength(20);
        b.Property(x => x.ApprovedByName).HasMaxLength(120);
        b.Property(x => x.FairMarketValue).HasPrecision(16, 2);
    }
}
