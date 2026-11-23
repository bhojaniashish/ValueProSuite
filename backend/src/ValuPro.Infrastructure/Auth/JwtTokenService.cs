using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Tenancy;

namespace ValuPro.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        AppUser user, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(TenantContext.ClaimType, user.TenantId.ToString()),
            new(TokenValidation.SecurityStampClaim, user.SecurityStamp ?? ""),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(Permissions.ClaimType, p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
