using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValuPro.Application.Common;
using ValuPro.Application.Signatures;

namespace ValuPro.Api.Controllers;

/// <summary>Things every signed-in user manages for themselves.</summary>
[Route("api/account")]
[Authorize]
public sealed class AccountController(ISignatureService signatures) : ApiControllerBase
{
    /// <summary>Whether I have a signature saved, with my name and registration number as reports print them.</summary>
    [HttpGet("signature")]
    [ProducesResponseType<MySignature>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Signature(CancellationToken ct) => Ok(await signatures.GetMineAsync(ct));

    [HttpGet("signature/image")]
    public async Task<IActionResult> SignatureImage(CancellationToken ct)
    {
        var r = await signatures.OpenMineAsync(ct);
        if (!r.IsSuccess) return Problem(r.Error!);
        Response.Headers.CacheControl = "no-store";
        return File(r.Value!.Content, r.Value.ContentType);
    }

    /// <summary>Saves my signature: a PNG drawn on screen or an uploaded scan (PNG/JPEG, up to 1 MB).</summary>
    [HttpPut("signature")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType<MySignature>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSignature(IFormFile? file, CancellationToken ct)
    {
        if (file is null) return Problem(Error.Validation("Draw or choose your signature.", "file"));
        await using var stream = file.OpenReadStream();
        return From(await signatures.SaveMineAsync(stream, file.ContentType, file.Length, ct));
    }

    [HttpDelete("signature")]
    [ProducesResponseType<MySignature>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveSignature(CancellationToken ct) => From(await signatures.RemoveMineAsync(ct));
}
