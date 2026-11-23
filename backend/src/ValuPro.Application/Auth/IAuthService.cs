using ValuPro.Application.Common;
using ValuPro.Application.Users;

namespace ValuPro.Application.Auth;

public interface IAuthService
{
    /// <returns>Null when the email/password is wrong, the user is inactive or locked out.</returns>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Changes the signed-in user's own password. Other sessions end; a fresh token is
    /// returned so the current one keeps working.
    /// </summary>
    Task<Result<LoginResponse>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}
