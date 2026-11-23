using ValuPro.Application.Common;

namespace ValuPro.Application.Users;

public interface IUserAdminService
{
    Task<PagedResult<UserListItem>> ListAsync(UserQuery query, CancellationToken ct = default);
    Task<UserSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<Result<UserListItem>> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates the user with a generated temporary password they must change at first sign-in.</summary>
    Task<Result<TemporaryPasswordResponse>> CreateAsync(SaveUserRequest request, CancellationToken ct = default);

    Task<Result<UserListItem>> UpdateAsync(Guid id, SaveUserRequest request, CancellationToken ct = default);

    /// <summary>Issues a new temporary password and signs the user out everywhere.</summary>
    Task<Result<TemporaryPasswordResponse>> ResetPasswordAsync(Guid id, CancellationToken ct = default);
}

public interface IRolePermissionService
{
    Task<PermissionMatrix> GetMatrixAsync(CancellationToken ct = default);
    Task<Result<PermissionMatrix>> UpdateRoleAsync(string role, IReadOnlyList<string> permissions, CancellationToken ct = default);
}
