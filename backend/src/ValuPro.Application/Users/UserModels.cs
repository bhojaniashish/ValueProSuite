using ValuPro.Domain.Security;

namespace ValuPro.Application.Users;

public sealed record UserListItem(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string? RegistrationNo,
    string Role,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserQuery(
    string? Search = null,
    string? Role = null,
    UserStatus? Status = null,
    string? Sort = null,
    bool Desc = false,
    int Page = 1,
    int PageSize = 20);

public sealed record UserSummary(int Total, int Active, int Valuers, int Reviewers);

public sealed record SaveUserRequest(
    string FullName,
    string Email,
    string? Phone,
    string? RegistrationNo,
    string Role,
    UserStatus Status);

/// <summary>Returned once when a user is created or their password is reset. Never stored in plain text.</summary>
public sealed record TemporaryPasswordResponse(UserListItem User, string TemporaryPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record PermissionMatrix(
    IReadOnlyList<string> Roles,
    IReadOnlyList<PermissionModule> Modules,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Grants,
    IReadOnlyList<string> LockedRoles);

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
