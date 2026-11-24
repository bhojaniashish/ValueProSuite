using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ValuPro.Application.Abstractions;
using ValuPro.Domain.Security;

namespace ValuPro.Infrastructure.Auth;

public sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Principal?.FindFirst("sub")?.Value, out var id)
            ? id : null;

    public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value ?? Principal?.FindFirst("email")?.Value;

    public string? Name => Principal?.FindFirst("name")?.Value;

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(Permissions.ClaimType, permission) == true;
}
