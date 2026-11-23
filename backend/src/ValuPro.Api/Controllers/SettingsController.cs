using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ValuPro.Api.Security;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Settings;
using ValuPro.Domain.Security;

namespace ValuPro.Api.Controllers;

[Route("api/settings")]
[HasPermission(Permissions.Settings.View)]
public sealed class SettingsController(ISettingsService settings, ICurrentUser currentUser) : ApiControllerBase
{
    /// <summary>All settings sections. Secrets are never returned, only whether they are set.</summary>
    [HttpGet]
    [ProducesResponseType<SettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await settings.GetAsync(ct));

    [HttpPut("company")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<CompanySettings>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompany(CompanySettings request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await settings.UpdateCompanyAsync(request, ct));

    [HttpPut("branding")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<BrandingSettings>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBranding(UpdateBrandingRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await settings.UpdateBrandingAsync(request, ct));

    /// <summary>Uploads the logo or report watermark (PNG, JPG, WebP or SVG, up to 2 MB).</summary>
    [HttpPost("branding/{image}")]
    [HasPermission(Permissions.Settings.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [ProducesResponseType<BrandingSettings>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadImage(BrandingImage image, IFormFile? file, CancellationToken ct)
    {
        if (file is null) return From<BrandingSettings>(Application.Common.Error.Validation("Choose an image file.", "file"));
        await using var stream = file.OpenReadStream();
        return From(await settings.UploadImageAsync(image, stream, file.FileName, file.ContentType, file.Length, ct));
    }

    [HttpDelete("branding/{image}")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<BrandingSettings>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveImage(BrandingImage image, CancellationToken ct) =>
        From(await settings.RemoveImageAsync(image, ct));

    [HttpPut("communication")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<CommunicationSettings>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCommunication(UpdateCommunicationRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await settings.UpdateCommunicationAsync(request, ct));

    /// <summary>Sends a test email to the signed-in user using the saved SMTP settings.</summary>
    [HttpPost("communication/test-email")]
    [HasPermission(Permissions.Settings.Manage)]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> TestEmail(CancellationToken ct)
    {
        if (currentUser.Email is not { Length: > 0 } to) return Unauthorized();
        var result = await settings.SendTestEmailAsync(to, ct);
        return result.IsSuccess ? Ok(new { message = result.Value }) : Problem(result.Error!);
    }

    [HttpPut("gst")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<GstSettings>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGst(GstSettings request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await settings.UpdateGstAsync(request, ct));

    [HttpPut("notifications")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<IReadOnlyList<NotificationSetting>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateNotifications(UpdateNotificationsRequest request, CancellationToken ct) =>
        await ValidateAsync(request, ct) ?? From(await settings.UpdateNotificationsAsync(request, ct));
}

/// <summary>
/// Firm name, colours and logo, readable without signing in so the login page and the app shell
/// look like the firm. Nothing here is confidential.
/// </summary>
[Route("api/branding")]
[AllowAnonymous]
public sealed class BrandingController(ISettingsService settings) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PublicBranding>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await settings.GetPublicBrandingAsync(ct));

    [HttpGet("{image}")]
    public async Task<IActionResult> Image(BrandingImage image, CancellationToken ct)
    {
        var file = await settings.OpenImageAsync(image, ct);
        if (file is null) return NotFound();

        // URLs carry ?v=<version>, so the image can be cached; the CSP stops an SVG from running anything.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType);
    }
}
