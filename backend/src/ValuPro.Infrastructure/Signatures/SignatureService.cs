using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Common;
using ValuPro.Application.Signatures;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.Infrastructure.Signatures;

/// <summary>Each user's own signature image (drawn on screen or uploaded), used on the reports they sign.</summary>
public sealed class SignatureService(AppDbContext db, ICurrentUser me, IFileStorage files, TimeProvider clock) : ISignatureService
{
    public const long MaxBytes = 1024 * 1024;
    private static readonly string[] Types = ["image/png", "image/jpeg"];

    private async Task<Identity.AppUser?> MeAsync(CancellationToken ct) =>
        me.UserId is { } id ? await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) : null;

    public async Task<MySignature> GetMineAsync(CancellationToken ct = default)
    {
        var u = await MeAsync(ct);
        return new MySignature(u?.SignatureBlobPath is not null, u?.SignatureUpdatedAt, u?.FullName ?? "", u?.RegistrationNo);
    }

    public async Task<Result<SignatureImage>> OpenMineAsync(CancellationToken ct = default)
    {
        var u = await MeAsync(ct);
        if (u?.SignatureBlobPath is not { } path) return Error.NotFound("No signature saved yet.");
        var stream = await files.OpenReadAsync(path, ct);
        return stream is null ? Error.NotFound("The signature file is missing.") : new SignatureImage(stream, path.EndsWith(".png") ? "image/png" : "image/jpeg");
    }

    public async Task<Result<MySignature>> SaveMineAsync(Stream image, string contentType, long size, CancellationToken ct = default)
    {
        var u = await MeAsync(ct);
        if (u is null) return Error.Forbidden("Sign in again.");
        if (!Types.Contains(contentType)) return Error.Validation("Use a PNG or JPEG image.", "file");
        if (size <= 0 || size > MaxBytes) return Error.Validation("The signature image can be up to 1 MB.", "file");

        var ext = contentType == "image/png" ? ".png" : ".jpg";
        // A new name each time, so a report generated earlier keeps pointing at the signature it used.
        u.SignatureBlobPath = await files.UploadAsync($"users/{u.Id:N}", $"signature-{clock.GetUtcNow():yyyyMMddHHmmss}{ext}", image, contentType, ct);
        u.SignatureUpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetMineAsync(ct);
    }

    public async Task<Result<MySignature>> RemoveMineAsync(CancellationToken ct = default)
    {
        var u = await MeAsync(ct);
        if (u is null) return Error.Forbidden("Sign in again.");
        u.SignatureBlobPath = null;
        u.SignatureUpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetMineAsync(ct);
    }

    public async Task<Signer?> SignerAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return null;
        byte[]? image = null;
        if (u.SignatureBlobPath is { } path && await files.OpenReadAsync(path, ct) is { } stream)
        {
            await using (stream)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                image = ms.ToArray();
            }
        }
        return new Signer(u.Id, u.FullName, u.RegistrationNo, image, SignatureMethod.Image);
    }
}
