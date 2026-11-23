using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Settings;
using ValuPro.Domain.Tenancy;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class FirmSettingsConfiguration : IEntityTypeConfiguration<FirmSettings>
{
    public void Configure(EntityTypeBuilder<FirmSettings> b)
    {
        b.ToTable("FirmSettings");
        b.HasIndex(x => x.TenantId).IsUnique(); // one row per firm
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);

        b.Property(x => x.SmtpHost).HasMaxLength(200);
        b.Property(x => x.SmtpUsername).HasMaxLength(256);
        b.Property(x => x.SmtpPasswordEncrypted).HasMaxLength(1000);
        b.Property(x => x.FromEmail).HasMaxLength(256);
        b.Property(x => x.FromName).HasMaxLength(100);
        b.Property(x => x.SmsProvider).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SmsSenderId).HasMaxLength(11);
        b.Property(x => x.SmsApiKeyEncrypted).HasMaxLength(1000);
        b.Property(x => x.Gstin).HasMaxLength(15);
        b.Property(x => x.DefaultGstRate).HasPrecision(5, 2);
        b.Property(x => x.StateCode).HasMaxLength(2);
        b.Property(x => x.PlaceOfSupply).HasMaxLength(60);
        b.Property(x => x.InvoicePrefix).HasMaxLength(10);
        b.Property(x => x.AssignmentPrefix).HasMaxLength(10);

        // Small, always read together: stored as one JSON column.
        b.OwnsMany(x => x.Notifications, n => n.ToJson());
    }
}
