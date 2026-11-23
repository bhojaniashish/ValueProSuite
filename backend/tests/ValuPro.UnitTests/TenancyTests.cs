using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class TenancyTests
{
    private static readonly Guid FirmA = Guid.NewGuid();
    private static readonly Guid FirmB = Guid.NewGuid();

    private sealed class FixedTenant(Guid id) : ITenantContext { public Guid TenantId => id; }

    private sealed class NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Email => null;
        public bool HasPermission(string permission) => false;
    }

    private static AppDbContext Create(string dbName, Guid tenantId)
    {
        var tenant = new FixedTenant(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new AuditingInterceptor(new NoUser(), tenant, TimeProvider.System))
            .Options;
        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task Queries_only_return_rows_of_the_current_tenant()
    {
        var db = Guid.NewGuid().ToString();
        await using (var a = Create(db, FirmA))
        {
            a.Users.Add(new AppUser { FullName = "Valuer A", UserName = "a@x.in" });
            await a.SaveChangesAsync();
        }
        await using (var b = Create(db, FirmB))
        {
            b.Users.Add(new AppUser { FullName = "Valuer B", UserName = "b@x.in" });
            await b.SaveChangesAsync();
        }

        await using var readA = Create(db, FirmA);
        var names = await readA.Users.Select(u => u.FullName).ToListAsync();

        Assert.Equal(["Valuer A"], names);
        Assert.Equal(2, await readA.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_tenant()
    {
        await using var db = Create(Guid.NewGuid().ToString(), FirmA);
        var user = new AppUser { FullName = "New valuer", UserName = "n@x.in" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Equal(FirmA, user.TenantId);
    }

    [Fact]
    public async Task Moving_a_row_to_another_tenant_is_refused()
    {
        await using var db = Create(Guid.NewGuid().ToString(), FirmA);
        var user = new AppUser { FullName = "Valuer", UserName = "v@x.in" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.TenantId = FirmB;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Update_that_marks_all_columns_modified_is_allowed()
    {
        // ASP.NET Identity saves users with DbContext.Update(), which flags TenantId as modified.
        await using var db = Create(Guid.NewGuid().ToString(), FirmA);
        var user = new AppUser { FullName = "Valuer", UserName = "v@x.in" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.FullName = "Valuer renamed";
        db.Users.Update(user);
        await db.SaveChangesAsync();

        Assert.Equal(FirmA, user.TenantId);
    }
}
