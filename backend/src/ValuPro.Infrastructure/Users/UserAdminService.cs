using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Users;
using ValuPro.Domain.Security;
using ValuPro.Infrastructure.Auth;
using ValuPro.Infrastructure.Identity;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Users;

public sealed class UserAdminService(
    AppDbContext db,
    UserManager<AppUser> users,
    ICurrentUser currentUser,
    UserAccessCache accessCache,
    TimeProvider clock) : IUserAdminService
{
    private static readonly string[] ValuerRoles = [Roles.SeniorValuer, Roles.JuniorValuer];

    private sealed class Row
    {
        public required AppUser User { get; init; }
        public string? Role { get; init; }
    }

    // One role per user (as in the prototype); the role name is read alongside each user.
    private IQueryable<Row> Rows() =>
        db.Users.Select(u => new Row
        {
            User = u,
            Role =
            db.UserRoles.Where(ur => ur.UserId == u.Id)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .FirstOrDefault(),
        });

    private static UserListItem ToItem(Row r) => new(r.User.Id, r.User.FullName, r.User.Email ?? "",
        r.User.PhoneNumber, r.User.RegistrationNo, r.Role ?? "", r.User.Status, r.User.CreatedAt, r.User.LastLoginAt);

    public async Task<PagedResult<UserListItem>> ListAsync(UserQuery query, CancellationToken ct = default)
    {
        var rows = Rows();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            rows = rows.Where(r => r.User.FullName.Contains(s) || r.User.Email!.Contains(s) ||
                                   r.User.RegistrationNo!.Contains(s) || r.User.PhoneNumber!.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(query.Role)) rows = rows.Where(r => r.Role == query.Role);
        if (query.Status is { } status) rows = rows.Where(r => r.User.Status == status);

        rows = (query.Sort?.ToLowerInvariant(), query.Desc) switch
        {
            ("email", false) => rows.OrderBy(r => r.User.Email),
            ("email", true) => rows.OrderByDescending(r => r.User.Email),
            ("role", false) => rows.OrderBy(r => r.Role).ThenBy(r => r.User.FullName),
            ("role", true) => rows.OrderByDescending(r => r.Role).ThenBy(r => r.User.FullName),
            ("status", false) => rows.OrderBy(r => r.User.Status).ThenBy(r => r.User.FullName),
            ("status", true) => rows.OrderByDescending(r => r.User.Status).ThenBy(r => r.User.FullName),
            ("lastlogin", false) => rows.OrderBy(r => r.User.LastLoginAt),
            ("lastlogin", true) => rows.OrderByDescending(r => r.User.LastLoginAt),
            ("created", false) => rows.OrderBy(r => r.User.CreatedAt),
            ("created", true) => rows.OrderByDescending(r => r.User.CreatedAt),
            (_, true) => rows.OrderByDescending(r => r.User.FullName),
            _ => rows.OrderBy(r => r.User.FullName),
        };

        var total = await rows.CountAsync(ct);
        var page = await rows.AsNoTracking().Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<UserListItem>([.. page.Select(ToItem)], total, query.Page, query.PageSize);
    }

    public async Task<UserSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var rows = Rows();
        return new UserSummary(
            await rows.CountAsync(ct),
            await rows.CountAsync(r => r.User.Status == UserStatus.Active, ct),
            await rows.CountAsync(r => ValuerRoles.Contains(r.Role!) && r.User.Status != UserStatus.Inactive, ct),
            await rows.CountAsync(r => r.Role == Roles.Reviewer && r.User.Status != UserStatus.Inactive, ct));
    }

    public async Task<Result<UserListItem>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await Rows().AsNoTracking().FirstOrDefaultAsync(r => r.User.Id == id, ct);
        return row is null ? Error.NotFound("User not found.") : ToItem(row);
    }

    public async Task<Result<TemporaryPasswordResponse>> CreateAsync(SaveUserRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
            return Error.Conflict("A user with this email already exists.", "email");

        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            PhoneNumber = Clean(request.Phone),
            RegistrationNo = Clean(request.RegistrationNo),
            Status = request.Status,
            MustChangePassword = true,
            CreatedAt = clock.GetUtcNow(),
        };

        var password = TemporaryPassword.Generate();
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) return IdentityError(created);

        var roleAdded = await users.AddToRoleAsync(user, request.Role);
        if (!roleAdded.Succeeded)
        {
            await users.DeleteAsync(user); // keep create all-or-nothing
            return IdentityError(roleAdded);
        }

        return new TemporaryPasswordResponse(ToItem(new Row { User = user, Role = request.Role }), password);
    }

    public async Task<Result<UserListItem>> UpdateAsync(Guid id, SaveUserRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return Error.NotFound("User not found.");

        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();
        var isSelf = currentUser.UserId == id;

        if (isSelf && currentRole != request.Role)
            return Error.Forbidden("You cannot change your own role. Ask another admin.");
        if (isSelf && request.Status == UserStatus.Inactive)
            return Error.Forbidden("You cannot deactivate your own account.");

        var losesAdmin = currentRole == Roles.Admin && user.Status != UserStatus.Inactive &&
                         (request.Role != Roles.Admin || request.Status == UserStatus.Inactive);
        if (losesAdmin && await CountOtherActiveAdminsAsync(id, ct) == 0)
            return Error.Conflict("At least one active Admin is required.");

        var email = request.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var other = await users.FindByEmailAsync(email);
            if (other is not null && other.Id != id) return Error.Conflict("A user with this email already exists.", "email");
            user.Email = email;
            user.UserName = email;
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = Clean(request.Phone);
        user.RegistrationNo = Clean(request.RegistrationNo);
        user.Status = request.Status;

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) return IdentityError(updated);

        if (currentRole != request.Role)
        {
            if (currentRole is not null) await users.RemoveFromRoleAsync(user, currentRole);
            var added = await users.AddToRoleAsync(user, request.Role);
            if (!added.Succeeded) return IdentityError(added);
        }

        accessCache.Invalidate(id);
        return ToItem(new Row { User = user, Role = request.Role });
    }

    public async Task<Result<TemporaryPasswordResponse>> ResetPasswordAsync(Guid id, CancellationToken ct = default)
    {
        if (currentUser.UserId == id)
            return Error.Forbidden("Use Change password for your own account.");

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return Error.NotFound("User not found.");

        var password = TemporaryPassword.Generate();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password); // also rotates the security stamp
        if (!reset.Succeeded) return IdentityError(reset);

        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        accessCache.Invalidate(id);

        var role = (await users.GetRolesAsync(user)).FirstOrDefault();
        return new TemporaryPasswordResponse(ToItem(new Row { User = user, Role = role }), password);
    }

    private Task<int> CountOtherActiveAdminsAsync(Guid exceptUserId, CancellationToken ct) =>
        Rows().CountAsync(r => r.Role == Roles.Admin && r.User.Id != exceptUserId && r.User.Status != UserStatus.Inactive, ct);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error IdentityError(IdentityResult result)
    {
        var duplicate = result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName));
        return duplicate
            ? Error.Conflict("A user with this email already exists.", "email")
            : Error.Validation(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
