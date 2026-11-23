using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Auth;
using ValuPro.Infrastructure.Identity;

namespace ValuPro.UnitTests;

public class SecurityTests
{
    [Fact]
    public void Admin_gets_every_permission() =>
        Assert.Equal(Permissions.All.Order(), Permissions.DefaultsFor(Roles.Admin).Order());

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void Role_defaults_only_use_known_permissions(string role) =>
        Assert.All(Permissions.DefaultsFor(role), p => Assert.Contains(p, Permissions.All));

    [Fact]
    public void Bank_user_cannot_manage_anything() =>
        Assert.DoesNotContain(Permissions.DefaultsFor(Roles.BankUser), p => p.EndsWith(".manage") || p.EndsWith(".approve"));

    [Fact]
    public void Only_admin_and_reviewer_can_approve_reports() =>
        Assert.Equal([Roles.Admin, Roles.Reviewer],
            Roles.All.Where(r => Permissions.DefaultsFor(r).Contains(Permissions.Reports.Approve)));

    [Fact]
    public void Access_token_carries_tenant_roles_and_permissions()
    {
        var service = new JwtTokenService(
            Options.Create(new JwtOptions { SigningKey = new string('k', 48) }), TimeProvider.System);
        var tenantId = Guid.NewGuid();
        var user = new AppUser { FullName = "Sneha Kulkarni", Email = "sneha@x.in", TenantId = tenantId };

        var (token, expires) = service.CreateAccessToken(user, [Roles.SeniorValuer], [Permissions.Clients.View]);
        var jwt = new JsonWebToken(token);

        Assert.Equal(tenantId.ToString(), jwt.GetClaim("tenant_id").Value);
        Assert.Contains(jwt.Claims, c => c.Type == Permissions.ClaimType && c.Value == Permissions.Clients.View);
        Assert.True(expires > DateTimeOffset.UtcNow);
    }

    public static TheoryData<string> AllRoles() => [.. Roles.All];
}
