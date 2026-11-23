using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Users;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Auth;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;
using ValuPro.Infrastructure.Users;

namespace ValuPro.UnitTests;

public class UserAdminTests : IAsyncLifetime
{
    private sealed class FixedTenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public bool IsAuthenticated => UserId is not null;
        public string? Email => null;
        public bool HasPermission(string permission) => true;
    }

    private readonly FakeUser _me = new();
    private ServiceProvider _sp = null!;
    private IServiceScope _scope = null!;

    private IUserAdminService Users => _scope.ServiceProvider.GetRequiredService<IUserAdminService>();
    private IRolePermissionService Matrix => _scope.ServiceProvider.GetRequiredService<IRolePermissionService>();

    public async Task InitializeAsync()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITenantContext, FixedTenant>();
        services.AddSingleton<ICurrentUser>(_me);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase(dbName)
            .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddIdentityCore<AppUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 10; o.Password.RequireNonAlphanumeric = false; })
            .AddRoles<AppRole>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddMemoryCache();
        services.AddSingleton<UserAccessCache>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IRolePermissionService, RolePermissionService>();
        _sp = services.BuildServiceProvider();
        _scope = _sp.CreateScope();

        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        foreach (var name in Roles.All)
        {
            var role = new AppRole(name);
            await roles.CreateAsync(role);
            foreach (var p in Permissions.DefaultsFor(name))
                await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, p));
        }

        var admin = await Users.CreateAsync(Request("Rajesh Patel", "admin@x.in", Roles.Admin));
        _me.UserId = admin.Value!.User.Id;
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _sp.DisposeAsync();
    }

    private static SaveUserRequest Request(string name, string email, string role, UserStatus status = UserStatus.Active) =>
        new(name, email, "98250 41201", null, role, status);

    [Fact]
    public async Task New_user_gets_a_temporary_password_and_must_change_it()
    {
        var result = await Users.CreateAsync(Request("Sneha Kulkarni", "sneha@x.in", Roles.SeniorValuer));

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value!.TemporaryPassword.Length);
        var user = await _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync("sneha@x.in");
        Assert.True(user!.MustChangePassword);
        Assert.Equal(Roles.SeniorValuer, result.Value.User.Role);
    }

    [Fact]
    public async Task Duplicate_email_is_a_conflict()
    {
        var result = await Users.CreateAsync(Request("Someone", "ADMIN@x.in", Roles.Reviewer));
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal("email", result.Error.Field);
    }

    [Fact]
    public async Task Cannot_change_own_role_or_deactivate_self()
    {
        var self = _me.UserId!.Value;
        Assert.Equal(ErrorKind.Forbidden, (await Users.UpdateAsync(self, Request("Rajesh Patel", "admin@x.in", Roles.Reviewer))).Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, (await Users.UpdateAsync(self, Request("Rajesh Patel", "admin@x.in", Roles.Admin, UserStatus.Inactive))).Error!.Kind);
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_demoted()
    {
        var second = (await Users.CreateAsync(Request("Second Admin", "a2@x.in", Roles.Admin))).Value!.User.Id;
        _me.UserId = second; // the second admin edits the first

        var first = (await Users.ListAsync(new UserQuery(Search: "admin@x.in"))).Items.Single().Id;
        Assert.True((await Users.UpdateAsync(first, Request("Rajesh Patel", "admin@x.in", Roles.SeniorValuer))).IsSuccess);

        // Now `second` is the only admin; demoting them via another account must fail.
        _me.UserId = first;
        var result = await Users.UpdateAsync(second, Request("Second Admin", "a2@x.in", Roles.Reviewer));
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
    }

    [Fact]
    public async Task List_filters_by_role_status_and_search()
    {
        await Users.CreateAsync(Request("Priya Sharma", "priya@x.in", Roles.JuniorValuer));
        await Users.CreateAsync(Request("Karan Mehta", "karan@x.in", Roles.JuniorValuer, UserStatus.OnLeave));
        await Users.CreateAsync(Request("Neha Joshi", "neha@x.in", Roles.Reviewer));

        Assert.Equal(2, (await Users.ListAsync(new UserQuery(Role: Roles.JuniorValuer))).Total);
        Assert.Equal("Karan Mehta", (await Users.ListAsync(new UserQuery(Status: UserStatus.OnLeave))).Items.Single().FullName);
        Assert.Equal("Neha Joshi", (await Users.ListAsync(new UserQuery(Search: "neha"))).Items.Single().FullName);

        var summary = await Users.GetSummaryAsync();
        Assert.Equal(new UserSummary(Total: 4, Active: 3, Valuers: 2, Reviewers: 1), summary);
    }

    [Fact]
    public async Task Reset_password_rotates_security_stamp()
    {
        var created = (await Users.CreateAsync(Request("Amit Deshmukh", "amit@x.in", Roles.SeniorValuer))).Value!;
        var manager = _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var before = (await manager.FindByIdAsync(created.User.Id.ToString()))!.SecurityStamp;

        var reset = await Users.ResetPasswordAsync(created.User.Id);

        Assert.True(reset.IsSuccess);
        Assert.NotEqual(created.TemporaryPassword, reset.Value!.TemporaryPassword);
        Assert.NotEqual(before, (await manager.FindByIdAsync(created.User.Id.ToString()))!.SecurityStamp);
    }

    [Fact]
    public async Task Admin_role_is_locked_in_the_matrix()
    {
        var result = await Matrix.UpdateRoleAsync(Roles.Admin, []);
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task Granting_manage_also_grants_view()
    {
        var result = await Matrix.UpdateRoleAsync(Roles.Architect, [Permissions.Billing.Manage]);

        Assert.True(result.IsSuccess);
        Assert.Equal([Permissions.Billing.Manage, Permissions.Billing.View], result.Value!.Grants[Roles.Architect]);
    }

    [Fact]
    public void Temporary_passwords_meet_the_password_policy()
    {
        for (var i = 0; i < 200; i++)
        {
            var p = TemporaryPassword.Generate();
            Assert.True(p.Length >= 10 && p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit), p);
        }
    }
}
