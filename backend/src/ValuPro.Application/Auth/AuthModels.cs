namespace ValuPro.Application.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserProfile User);

public sealed record UserProfile(
    Guid Id,
    string Email,
    string FullName,
    string? RegistrationNo,
    Guid TenantId,
    string TenantName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);
