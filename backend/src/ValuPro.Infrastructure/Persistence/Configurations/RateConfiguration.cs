using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ValuPro.Domain.Rates;

namespace ValuPro.Infrastructure.Persistence.Configurations;

public sealed class LocationRateConfiguration : IEntityTypeConfiguration<LocationRate>
{
    public void Configure(EntityTypeBuilder<LocationRate> b)
    {
        b.ToTable("LocationRates");
        b.Property(x => x.Locality).HasMaxLength(120).IsRequired();
        b.Property(x => x.City).HasMaxLength(100).IsRequired();
        b.Property(x => x.State).HasMaxLength(60);
        b.Property(x => x.Zone).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.MinRate).HasPrecision(14, 2);
        b.Property(x => x.MaxRate).HasPrecision(14, 2);
        b.Property(x => x.Source).HasMaxLength(200);
        b.HasIndex(x => new { x.TenantId, x.City, x.Locality, x.Zone, x.EffectiveFrom }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class CircleRateConfiguration : IEntityTypeConfiguration<CircleRate>
{
    public void Configure(EntityTypeBuilder<CircleRate> b)
    {
        b.ToTable("CircleRates");
        b.Property(x => x.State).HasMaxLength(60).IsRequired();
        b.Property(x => x.Scheme).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.City).HasMaxLength(100).IsRequired();
        b.Property(x => x.Locality).HasMaxLength(120).IsRequired();
        b.Property(x => x.Residential).HasPrecision(14, 2);
        b.Property(x => x.Commercial).HasPrecision(14, 2);
        b.Property(x => x.OpenLand).HasPrecision(14, 2);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.HasIndex(x => new { x.TenantId, x.State, x.Scheme, x.City, x.Locality, x.EffectiveFrom }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class ConstructionRateConfiguration : IEntityTypeConfiguration<ConstructionRate>
{
    public void Configure(EntityTypeBuilder<ConstructionRate> b)
    {
        b.ToTable("ConstructionRates");
        b.Property(x => x.ConstructionType).HasMaxLength(120).IsRequired();
        b.Property(x => x.Unit).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.CpwdRate).HasPrecision(14, 2);
        b.Property(x => x.PwdRate).HasPrecision(14, 2);
        b.Property(x => x.BankRate).HasPrecision(14, 2);
        b.Property(x => x.Remarks).HasMaxLength(300);
        b.HasIndex(x => new { x.TenantId, x.ConstructionType, x.Unit, x.EffectiveFrom }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class DepreciationRuleConfiguration : IEntityTypeConfiguration<DepreciationRule>
{
    public void Configure(EntityTypeBuilder<DepreciationRule> b)
    {
        b.ToTable("DepreciationRules");
        b.Property(x => x.StructureType).HasMaxLength(120).IsRequired();
        b.Property(x => x.SalvagePercent).HasPrecision(5, 2);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Remarks).HasMaxLength(300);
        b.HasIndex(x => new { x.TenantId, x.StructureType }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
