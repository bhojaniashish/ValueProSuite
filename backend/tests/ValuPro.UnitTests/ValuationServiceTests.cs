using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Valuations;
using ValuPro.Domain.Assignments;
using ValuPro.Domain.Banks;
using ValuPro.Domain.Inspections;
using ValuPro.Domain.Properties;
using ValuPro.Domain.Rates;
using ValuPro.Domain.Security;
using ValuPro.Domain.Valuations;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Valuations;

namespace ValuPro.UnitTests;

public class ValuationServiceTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 12, 1, 4, 30, 0, TimeSpan.Zero); }

    private sealed class Me : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? Name { get; set; } = "Sneha Kulkarni";
        public HashSet<string> Grants { get; set; } = [.. Permissions.All];
        public bool IsAuthenticated => true;
        public string? Email => null;
        public bool HasPermission(string permission) => Grants.Contains(permission);
    }

    private readonly Me _me = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;
    private Guid _valuer, _bank, _flat, _house;

    private IValuationService Svc => _scope.ServiceProvider.GetRequiredService<IValuationService>();
    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async Task InitializeAsync()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<TimeProvider, FixedClock>();
        services.AddSingleton<ITenantContext, FixedTenant>();
        services.AddSingleton<ICurrentUser>(_me);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(name).AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IValuationService, ValuationService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var u = new AppUser { FullName = "Sneha Kulkarni", UserName = "sneha@x.in" };
        await _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(u);
        _valuer = u.Id;
        _me.UserId = _valuer;

        var bank = new Bank { Name = "HDFC Bank", ShortCode = "HDFC", RealizablePercent = 90, DistressPercent = 75, Depreciation = DepreciationMethod.StraightLine, RateBasis = RateBasis.BankRates };
        var flat = new Property { Code = "PR1", Type = PropertyType.ResidentialFlat, OwnerName = "S", Address = "A-902 Shanti Heights, Vesu", City = "Surat", BuiltUpArea = 1450 };
        var house = new Property { Code = "PR2", Type = PropertyType.Bungalow, OwnerName = "K", Address = "12 Green Park, Adajan", City = "Surat", LandArea = 2400, BuiltUpArea = 1800 };
        Db.AddRange(bank, flat, house,
            new LocationRate { Locality = "Vesu", City = "Surat", Zone = RateZone.Residential, MinRate = 6500, MaxRate = 9200, EffectiveFrom = new DateOnly(2026, 4, 1) },
            new LocationRate { Locality = "Vesu", City = "Surat", Zone = RateZone.Residential, MinRate = 6000, MaxRate = 8500, EffectiveFrom = new DateOnly(2025, 4, 1) }, // older
            new LocationRate { Locality = "Adajan", City = "Surat", Zone = RateZone.Residential, MinRate = 5800, MaxRate = 8000, EffectiveFrom = new DateOnly(2026, 4, 1) },
            new CircleRate { State = "Gujarat", City = "Surat", Locality = "Vesu", Residential = 52000, EffectiveFrom = new DateOnly(2026, 4, 1) },
            new ConstructionRate { ConstructionType = "RCC Framed - Standard", Unit = RateUnit.SqFt, CpwdRate = 2100, PwdRate = 2000, BankRate = 1900, EffectiveFrom = new DateOnly(2026, 4, 1) },
            new DepreciationRule { StructureType = "RCC Framed Structure", TotalLifeYears = 60, SalvagePercent = 10 });
        await Db.SaveChangesAsync();
        (_bank, _flat, _house) = (bank.Id, flat.Id, house.Id);
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private async Task<Guid> CaseAsync(Guid property, AssignmentStatus status = AssignmentStatus.InspectionDone, Action<InspectionData>? data = null)
    {
        var a = new Assignment
        {
            Number = $"VAL/2026/{Random.Shared.Next(1000, 9999)}", Status = status, BankId = _bank, ApplicantName = "S",
            PropertySummary = "Property", PropertyId = property, ValuerId = _valuer, DueDate = new DateOnly(2026, 12, 9),
        };
        Db.Add(a);
        if (data is not null)
        {
            var d = new InspectionData();
            data(d);
            Db.Add(new Inspection { AssignmentId = a.Id, StartedByName = "S", Status = InspectionStatus.Submitted, Data = d });
        }
        await Db.SaveChangesAsync();
        return a.Id;
    }

    [Fact]
    public async Task A_flat_starts_as_composite_from_the_lower_area_and_the_locality_band()
    {
        var id = await CaseAsync(_flat, data: d =>
        {
            d.Measurements.CarpetAsPerDocuments = 1100;
            d.Measurements.CarpetMeasured = 1080;
            d.Measurements.BuiltUpMeasured = 1310;
            d.Building.StructureType = "RCC Framed Structure";
            d.Building.AgeYears = 6;
        });
        var v = (await Svc.GetAsync(id)).Value!;

        Assert.False(v.Saved);
        Assert.Equal(ValuationMethod.Composite, v.Input.Method);
        Assert.Equal(AreaBasis.Carpet, v.Input.AreaBasis);
        Assert.Equal(1080, v.Input.AreaSqFt);                 // lower of 1,100 and 1,080
        Assert.Equal(7850, v.Input.CompositeRatePerSqFt);     // middle of the current Vesu band 6,500–9,200
        Assert.Equal(6500, v.Input.MarketRateMin);
        Assert.Equal(1900, v.Input.ConstructionRatePerSqFt);  // HDFC uses bank rates
        Assert.Equal(52000, v.Input.CircleRatePerSqM);
        Assert.Equal(1310, v.Input.BuiltUpSqFt);
        Assert.Equal(90, v.Input.RealizablePercent);
        Assert.True(v.Result.FairMarketValue > 0);
        Assert.Contains(v.Options.Locations, l => l.Locality == "Vesu" && l.Suggested && l.MinRate == 6500);
        Assert.NotEmpty(v.Suggestions);
    }

    [Fact]
    public async Task A_bungalow_starts_as_land_and_building_with_the_depreciation_rule()
    {
        var id = await CaseAsync(_house, data: d =>
        {
            d.Measurements.LandMeasured = 2450;            // more than documents: documents win
            d.Measurements.Floors = [new FloorArea { Name = "Ground floor", Measured = 1000 }, new FloorArea { Name = "First floor", Measured = 800 }];
            d.Building.StructureType = "RCC Framed Structure";
            d.Building.AgeYears = 12;
        });
        var i = (await Svc.GetAsync(id)).Value!.Input;

        Assert.Equal(ValuationMethod.LandAndBuilding, i.Method);
        Assert.Equal(2400, i.LandAreaSqFt);
        Assert.Equal(6900, i.LandRatePerSqFt);              // middle of Adajan 5,800–8,000
        Assert.Equal(["Ground floor", "First floor"], i.Building.Select(b => b.Label));
        Assert.All(i.Building, b => Assert.Equal((12m, 60, 10m, 1900m), (b.AgeYears, b.LifeYears, b.SalvagePercent, b.RatePerSqFt)));
    }

    [Fact]
    public async Task Saving_stores_the_values_and_refuses_an_older_copy()
    {
        var id = await CaseAsync(_flat, data: d => d.Measurements.CarpetMeasured = 1000);
        var start = (await Svc.GetAsync(id)).Value!;
        start.Input.CompositeRatePerSqFt = 8000;

        var saved = (await Svc.SaveAsync(id, new SaveValuationRequest(0, start.Input))).Value!;
        Assert.True(saved.Saved);
        Assert.Equal(1, saved.Version);
        Assert.Equal(80_00_000, saved.Result.FairMarketValue);
        var row = await Db.Set<Valuation>().AsNoTracking().SingleAsync(v => v.AssignmentId == id);
        Assert.Equal(72_00_000, row.RealizableValue);
        Assert.Equal(60_00_000, row.DistressValue);
        Assert.Empty(saved.Suggestions);

        Assert.Equal(ErrorKind.Conflict, (await Svc.SaveAsync(id, new SaveValuationRequest(0, start.Input))).Error!.Kind);
        start.Input.CompositeRatePerSqFt = 0;
        Assert.Equal(ErrorKind.Validation, (await Svc.SaveAsync(id, new SaveValuationRequest(1, start.Input))).Error!.Kind);
    }

    [Fact]
    public async Task Only_the_valuer_reviewer_or_a_manager_can_save_until_submission()
    {
        var id = await CaseAsync(_flat, status: AssignmentStatus.Submitted, data: d => d.Measurements.CarpetMeasured = 1000);
        var v = (await Svc.GetAsync(id)).Value!;
        Assert.False(v.CanEdit);
        Assert.Equal(ErrorKind.Forbidden, (await Svc.SaveAsync(id, new SaveValuationRequest(0, v.Input))).Error!.Kind);

        var open = await CaseAsync(_flat, data: d => d.Measurements.CarpetMeasured = 1000);
        _me.UserId = Guid.NewGuid();
        _me.Grants.Remove(Permissions.Assignments.Manage);
        Assert.False((await Svc.GetAsync(open)).Value!.CanEdit);
    }

    [Fact]
    public async Task The_list_shows_cases_waiting_for_a_valuation_then_valued()
    {
        var waiting = await CaseAsync(_flat, data: d => d.Measurements.CarpetMeasured = 1000);
        await CaseAsync(_flat, status: AssignmentStatus.SiteVisitScheduled); // not visited yet

        var todo = await Svc.ListAsync(new ValuationQuery());
        Assert.Equal([waiting], todo.Select(x => x.AssignmentId));

        var input = (await Svc.GetAsync(waiting)).Value!.Input;
        await Svc.SaveAsync(waiting, new SaveValuationRequest(0, input));
        Assert.Empty(await Svc.ListAsync(new ValuationQuery()));
        Assert.True((await Svc.ListAsync(new ValuationQuery(Done: true))).Single().FairMarketValue > 0);
    }
}
