using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using ValuPro.Application.Abstractions;

namespace ValuPro.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Local dev: "UseDevelopmentStorage=true" (Azurite). Azure: leave empty and set ServiceUri.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Azure: https://{account}.blob.core.windows.net, used with the app's managed identity.</summary>
    public string? ServiceUri { get; set; }

    public string Container { get; set; } = "valupro";

    /// <summary>Local development only: store files in this folder instead of Blob Storage (no Azurite needed).</summary>
    public string? LocalPath { get; set; }
}

/// <summary>Azure Blob Storage. Every path starts with tenants/{tenantId}/ so firms never share files.</summary>
public sealed class BlobFileStorage(BlobServiceClient service, IOptions<StorageOptions> options, ITenantContext tenant)
    : IFileStorage
{
    private BlobContainerClient Container => service.GetBlobContainerClient(options.Value.Container);

    public async Task<string> UploadAsync(string folder, string fileName, Stream content, string contentType,
        CancellationToken ct = default)
    {
        var safeName = $"{Guid.CreateVersion7():N}{Path.GetExtension(fileName)}";
        var path = $"tenants/{tenant.TenantId}/{folder.Trim('/')}/{safeName}";

        await Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await Container.GetBlobClient(path).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
        return path;
    }

    public async Task<Uri> GetReadUrlAsync(string blobPath, TimeSpan lifetime, CancellationToken ct = default)
    {
        EnsureOwnTenant(blobPath);
        var blob = Container.GetBlobClient(blobPath);
        var sas = new BlobSasBuilder
        {
            BlobContainerName = Container.Name,
            BlobName = blobPath,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(lifetime),
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        if (blob.CanGenerateSasUri) return blob.GenerateSasUri(sas); // account key (Azurite / connection string)

        // Managed identity: sign with a user delegation key.
        var key = await service.GetUserDelegationKeyAsync(DateTimeOffset.UtcNow.AddMinutes(-5), sas.ExpiresOn, ct);
        return new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(key.Value, service.AccountName),
        }.ToUri();
    }

    public async Task<Stream?> OpenReadAsync(string blobPath, CancellationToken ct = default)
    {
        EnsureOwnTenant(blobPath);
        var blob = Container.GetBlobClient(blobPath);
        try
        {
            return await blob.OpenReadAsync(cancellationToken: ct);
        }
        catch (Azure.RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string blobPath, CancellationToken ct = default)
    {
        EnsureOwnTenant(blobPath);
        await Container.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);
    }

    private void EnsureOwnTenant(string blobPath)
    {
        if (!blobPath.StartsWith($"tenants/{tenant.TenantId}/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("File belongs to another tenant.");
    }
}
