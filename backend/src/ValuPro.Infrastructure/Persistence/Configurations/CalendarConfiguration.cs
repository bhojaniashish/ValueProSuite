using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Calendar;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class CalendarEntryConfiguration : IEntityTypeConfiguration<CalendarEntry>
{
    public void Configure(EntityTypeBuilder<CalendarEntry> b)
    {
        b.ToTable("CalendarEntries");
        b.HasIndex(x => new { x.TenantId, x.Date });
        b.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Title).HasMaxLength(160).IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CreatedByName).HasMaxLength(120).IsRequired();
    }
}
