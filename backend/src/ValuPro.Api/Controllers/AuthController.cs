using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Auth;
using ValuPro.Application.Users;

namespace ValuPro.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser) : ApiControllerBase
{
    /// <summary>Sign in with email and password; returns a bearer token and the user's profile.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;

        var result = await auth.LoginAsync(request, ct);
        return result is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.")
            : Ok(result);
    }

    /// <summary>The signed-in user's profile, roles and current permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserProfile>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (currentUser.UserId is not { } id) return Unauthorized();
        var profile = await auth.GetProfileAsync(id, ct);
        return profile is null ? Unauthorized() : Ok(profile);
    }

    /// <summary>Change your own password. Returns a fresh token; other sessions are signed out.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, ct) is { } invalid) return invalid;
        if (currentUser.UserId is not { } id) return Unauthorized();
        return From(await auth.ChangePasswordAsync(id, request, ct));
    }
}
