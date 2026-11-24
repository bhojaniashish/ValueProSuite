using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Properties;
using ValuPro.Infrastructure.Identity;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("Properties");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500).IsRequired();
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(60);
        b.Property(x => x.Pincode).HasMaxLength(6);
        b.Property(x => x.SurveyNo).HasMaxLength(60);
        b.Property(x => x.CtsNo).HasMaxLength(60);
        b.Property(x => x.PlotNo).HasMaxLength(60);
        b.Property(x => x.LandArea).HasPrecision(14, 2);
        b.Property(x => x.BuiltUpArea).HasPrecision(14, 2);
        b.Property(x => x.Zone).HasMaxLength(60);
        b.Property(x => x.Usage).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.Remarks).HasMaxLength(1000);

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.City });
        b.HasOne<Client>().WithMany().HasForeignKey(x => x.OwnerClientId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> b)
    {
        b.ToTable("Assignments");
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.BankReference).HasMaxLength(60);
        b.Property(x => x.Purpose).HasMaxLength(100);
        b.Property(x => x.ApplicantName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ApplicantPhone).HasMaxLength(20);
        b.Property(x => x.PropertySummary).HasMaxLength(300).IsRequired();
        b.Property(x => x.Fee).HasPrecision(12, 2);
        b.Property(x => x.Remarks).HasMaxLength(1000);

        // Links are kept even when the linked record is (soft-)deleted later.
        b.HasOne<Bank>().WithMany().HasForeignKey(x => x.BankId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankBranch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ValuerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
        b.HasIndex(x => x.ValuerId);
    }
}

public sealed class AssignmentEventConfiguration : IEntityTypeConfiguration<AssignmentEvent>
{
    public void Configure(EntityTypeBuilder<AssignmentEvent> b)
    {
        b.ToTable("AssignmentEvents");
        b.Property(x => x.ByName).HasMaxLength(150).IsRequired();
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.AssignmentId, x.At });
    }
}
