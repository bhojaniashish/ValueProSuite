using ValuPro.Application.Common;

namespace ValuPro.Application.Signatures;

/// <summary>How a report was signed. Image now; Aadhaar eSign and DSC tokens are the planned additions.</summary>
public enum SignatureMethod
{
    Image = 0,
    ESign = 1,
    Dsc = 2,
}

public sealed record MySignature(bool HasSignature, DateTimeOffset? UpdatedAt, string FullName, string? RegistrationNo);

/// <summary>What a report needs to print the signer's block.</summary>
public sealed record Signer(Guid UserId, string FullName, string? RegistrationNo, byte[]? SignatureImage, SignatureMethod Method);

public sealed record SignatureImage(Stream Content, string ContentType);

public interface ISignatureService
{
    Task<MySignature> GetMineAsync(CancellationToken ct = default);
    Task<Result<SignatureImage>> OpenMineAsync(CancellationToken ct = default);
    Task<Result<MySignature>> SaveMineAsync(Stream image, string contentType, long size, CancellationToken ct = default);
    Task<Result<MySignature>> RemoveMineAsync(CancellationToken ct = default);
    /// <summary>The signer details for a user (null signature image when they have none).</summary>
    Task<Signer?> SignerAsync(Guid userId, CancellationToken ct = default);
}
