namespace ValuPro.Application.Abstractions;

/// <summary>Stores photos, documents and generated PDFs. Paths are always prefixed with the tenant.</summary>
public interface IFileStorage
{
    /// <returns>The stored blob path (tenants/{tenantId}/{folder}/{file}).</returns>
    Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>A short-lived, read-only link the browser can open directly.</summary>
    Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>Opens a stored file for streaming through the API; null when it no longer exists.</summary>
    Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default);

    Task DeleteAsync(string blobPath, CancellationToken ct = default);
}
