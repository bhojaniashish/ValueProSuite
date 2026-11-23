using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Tenancy;
using ValuPro.Infrastructure.Identity;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.RegisteredValuerNo).HasMaxLength(60);
        b.Property(x => x.IbbiRegistration).HasMaxLength(60);
        b.Property(x => x.State).HasMaxLength(60);
        b.Property(x => x.ContactEmail).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.LogoBlobPath).HasMaxLength(400);
        b.Property(x => x.WatermarkBlobPath).HasMaxLength(400);
        b.Property(x => x.PrimaryColor).HasMaxLength(9);
        b.Property(x => x.AccentColor).HasMaxLength(9);
    }
}

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.RegistrationNo).HasMaxLength(60);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
