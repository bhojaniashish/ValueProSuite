using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ValuPro.Application.Abstractions;

namespace ValuPro.Infrastructure.Tenancy;

/// <summary>
/// Signed-in requests use the tenant in the user's token; everything else (login, seeding,
/// background jobs) uses the default tenant. For SaaS, resolve from the subdomain here.
/// </summary>
public sealed class TenantContext(IHttpContextAccessor http, IOptions<TenancyOptions> options) : ITenantContext
{
    public const string ClaimType = "tenant_id";

    public Guid TenantId
    {
        get
        {
            var claim = http.HttpContext?.User.FindFirst(ClaimType)?.Value;
            return Guid.TryParse(claim, out var id) ? id : options.Value.DefaultTenantId;
        }
    }
}
