using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Auth;
using ValuPro.Application.Common;
using ValuPro.Application.Users;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Auth;

public sealed class AuthService(
    UserManager<AppUser> users,
    AppDbContext db,
    UserAccessCache accessCache,
    JwtTokenService tokens,
    TimeProvider clock) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.CanSignIn) return null;
        if (await users.IsLockedOutAsync(user)) return null;

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);

        var profile = await BuildProfileAsync(user, ct);
        var (token, expires) = tokens.CreateAccessToken(user, profile.Roles, profile.Permissions);
        return new LoginResponse(token, expires, profile);
    }

    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null || !user.CanSignIn ? null : await BuildProfileAsync(user, ct);
    }

    public async Task<Result<LoginResponse>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.CanSignIn) return Error.NotFound("User not found.");

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var mismatch = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
            return mismatch
                ? Error.Validation("Current password is incorrect.", "currentPassword")
                : Error.Validation(string.Join(" ", result.Errors.Select(e => e.Description)), "newPassword");
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user); // ChangePasswordAsync already rotated the security stamp

        var profile = await BuildProfileAsync(user, ct);
        var (token, expires) = tokens.CreateAccessToken(user, profile.Roles, profile.Permissions);
        return new LoginResponse(token, expires, profile);
    }

    private async Task<UserProfile> BuildProfileAsync(AppUser user, CancellationToken ct)
    {
        accessCache.Invalidate(user.Id);
        var access = await accessCache.GetAsync(user.Id, db, ct);

        var tenantName = await db.Tenants
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? "";

        return new UserProfile(user.Id, user.Email ?? "", user.FullName, user.RegistrationNo,
            user.TenantId, tenantName, access?.Roles ?? [], access?.Permissions ?? [], user.MustChangePassword);
    }
}
