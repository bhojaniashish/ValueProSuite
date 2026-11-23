using Microsoft.AspNetCore.Authorization;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Security;

/// <summary>
/// Protects an endpoint with a permission from the role matrix, e.g.
/// <c>[HasPermission(Permissions.Clients.Manage)]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(Permission)];
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
