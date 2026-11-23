using ValuPro.Application.Common;

namespace ValuPro.Application.Clients;

public interface IClientService
{
    Task<PagedResult<ClientItem>> ListAsync(ClientQuery query, CancellationToken ct = default);
    Task<ClientSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<Result<ClientItem>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<ClientItem>> CreateAsync(SaveClientRequest request, CancellationToken ct = default);
    Task<Result<ClientItem>> UpdateAsync(Guid id, SaveClientRequest request, CancellationToken ct = default);

    /// <summary>Hides the client; its history stays for past assignments and invoices.</summary>
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Reads a CSV or Excel file and checks every row, without saving.</summary>
    Task<Result<ImportPreview>> PreviewImportAsync(Stream file, string fileName, CancellationToken ct = default);

    /// <summary>Saves the given rows; any that fail checks now (e.g. duplicates added meanwhile) are returned.</summary>
    Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct = default);
}

public interface INumberSequence
{
    /// <summary>Takes the next number for <paramref name="key"/> in the current firm. Safe under concurrent use.</summary>
    Task<long> NextAsync(string key, CancellationToken ct = default);
}
