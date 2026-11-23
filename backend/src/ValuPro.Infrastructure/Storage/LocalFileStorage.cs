using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ValuPro.Application.Abstractions;

namespace ValuPro.Infrastructure.Storage;

/// <summary>
/// Development stand-in for Blob Storage: files go under Storage:LocalPath with the same
/// tenants/{tenantId}/… layout. Not for production (single machine, no SAS links).
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment env, ITenantContext tenant) : IFileStorage
{
    private string Root => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.LocalPath!));

    public async Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType,
        CancellationToken ct = default)
    {
        var path = $"tenants/{tenant.TenantId}/{folder.Trim('/')}/{Guid.CreateVersion7():N}{Path.GetExtension(fileName)}";
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return path;
    }

    public Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default) =>
        throw new NotSupportedException("Local storage has no direct links; stream files through the API.");

    public Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default)
    {
        var full = Resolve(blobPath);
        return Task.FromResult<Stream?>(File.Exists(full) ? File.OpenRead(full) : null);
    }

    public Task DeleteAsync(string blobPath, CancellationToken ct = default)
    {
        var full = Resolve(blobPath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    private string Resolve(string blobPath)
    {
        if (!blobPath.StartsWith($"tenants/{tenant.TenantId}/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("File belongs to another tenant.");
        var full = Path.GetFullPath(Path.Combine(Root, blobPath));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid file path.");
        return full;
    }
}
