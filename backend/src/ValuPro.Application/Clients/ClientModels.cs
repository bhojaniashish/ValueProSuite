using ValuPro.Domain.Clients;

namespace ValuPro.Application.Clients;

public sealed record ClientItem(
    Guid Id,
    string Code,
    ClientType Type,
    string Name,
    string Mobile,
    string? Email,
    string? Gstin,
    string? Pan,
    string? Address,
    string? City,
    string? State,
    string? Pincode,
    string? Remarks,
    DateTimeOffset CreatedAt);

public sealed record ClientQuery(
    string? Search = null,
    ClientType? Type = null,
    string? State = null,
    string? Sort = null,
    bool Desc = false,
    int Page = 1,
    int PageSize = 20);

public sealed record ClientSummary(int Total, int Individual, int Builder, int BankBranch, int Advocate, IReadOnlyList<string> States);

public sealed record SaveClientRequest(
    ClientType Type,
    string Name,
    string Mobile,
    string? Email,
    string? Gstin,
    string? Pan,
    string? Address,
    string? City,
    string? State,
    string? Pincode,
    string? Remarks);

/// <summary>One row of an uploaded file, as read and checked. Nothing is saved yet.</summary>
public sealed record ImportRow(int Row, SaveClientRequest? Client, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Client is not null;
}

public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows, int Valid, int Invalid);

public sealed record ImportRequest(IReadOnlyList<SaveClientRequest> Clients);

public sealed record ImportResult(int Imported, IReadOnlyList<ImportRow> Rejected);
