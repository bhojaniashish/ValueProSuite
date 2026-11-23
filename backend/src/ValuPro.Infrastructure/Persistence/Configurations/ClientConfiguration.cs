using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Clients;
using ValuPro.Domain.Common;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Clients");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Mobile).HasMaxLength(20).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Gstin).HasMaxLength(15);
        b.Property(x => x.Pan).HasMaxLength(10);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(60);
        b.Property(x => x.Pincode).HasMaxLength(6);
        b.Property(x => x.Remarks).HasMaxLength(1000);

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        // One live client per PAN / GSTIN within a firm (deleted clients don't block re-adding).
        b.HasIndex(x => new { x.TenantId, x.Pan }).IsUnique().HasFilter("[Pan] IS NOT NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.TenantId, x.Gstin }).IsUnique().HasFilter("[Gstin] IS NOT NULL AND [IsDeleted] = 0");
        b.HasIndex(x => new { x.TenantId, x.Name });
    }
}

public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("NumberSequences");
        b.Property(x => x.Key).HasMaxLength(50).IsRequired();
        b.Property(x => x.Last).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
    }
}
