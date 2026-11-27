using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Banks;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class BankConfiguration : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> b)
    {
        b.ToTable("Banks");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.ShortCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.BrandColor).HasMaxLength(7);
        b.Property(x => x.IfscPrefix).HasMaxLength(4);
        b.Property(x => x.PanelCode).HasMaxLength(60);
        b.HasOne<Domain.Reports.ReportFormat>().WithMany().HasForeignKey(x => x.ReportFormatId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.RateBasis).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Depreciation).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DistressPercent).HasPrecision(5, 2);
        b.Property(x => x.RealizablePercent).HasPrecision(5, 2);

        // The questionnaire is edited and read as a whole: one JSON column.
        b.OwnsMany(x => x.Questions, q =>
        {
            q.ToJson();
            q.Property(x => x.AnswerType).HasConversion<string>();
        });

        b.HasMany(x => x.Branches).WithOne(x => x.Bank).HasForeignKey(x => x.BankId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.ShortCode }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class BankBranchConfiguration : IEntityTypeConfiguration<BankBranch>
{
    public void Configure(EntityTypeBuilder<BankBranch> b)
    {
        b.ToTable("BankBranches");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Ifsc).HasMaxLength(11);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(60);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.ContactPerson).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Ifsc }).IsUnique().HasFilter("[Ifsc] IS NOT NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.BankId, x.Name });
    }
}
