using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Clients;
using ValuPro.Application.Common;
using ValuPro.Domain.Clients;
using ValuPro.Infrastructure.Clients;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class ClientTests
{
    private sealed class FixedTenant(Guid id) : ITenantContext { public Guid TenantId => id; }

    private sealed class NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Email => null;
        public bool HasPermission(string permission) => false;
    }

    private readonly AppDbContext _db;
    private readonly ClientService _clients;

    public ClientTests()
    {
        var tenant = new FixedTenant(Guid.NewGuid());
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditingInterceptor(new NoUser(), tenant, TimeProvider.System))
            .Options, tenant);
        _clients = new ClientService(_db, new NumberSequenceService(_db), new SaveClientRequestValidator());
    }

    private static SaveClientRequest Req(string name, string? pan = null, string? gstin = null, ClientType type = ClientType.Individual) =>
        new(type, name, "98250 41201", null, gstin, pan, null, "Surat", "Gujarat", "395007", null);

    [Fact]
    public async Task Codes_are_sequential_and_never_reused()
    {
        var a = (await _clients.CreateAsync(Req("Suresh Agarwal"))).Value!;
        var b = (await _clients.CreateAsync(Req("Kavita Reddy"))).Value!;
        await _clients.DeleteAsync(b.Id);
        var c = (await _clients.CreateAsync(Req("Mehul Trivedi"))).Value!;

        Assert.Equal(["CL0001", "CL0002", "CL0003"], new[] { a.Code, b.Code, c.Code });
    }

    [Fact]
    public async Task Pan_and_gstin_are_normalized_and_unique_among_live_clients()
    {
        var first = (await _clients.CreateAsync(Req("Balaji Developers", " aabcs1429n ", "24aabcs1429n1z5", ClientType.Builder))).Value!;
        Assert.Equal("AABCS1429N", first.Pan);
        Assert.Equal("24AABCS1429N1Z5", first.Gstin);

        var dup = await _clients.CreateAsync(Req("Someone Else", "AABCS1429N"));
        Assert.Equal(ErrorKind.Conflict, dup.Error!.Kind);
        Assert.Equal("pan", dup.Error.Field);

        // Once deleted, the PAN can be used again.
        await _clients.DeleteAsync(first.Id);
        Assert.True((await _clients.CreateAsync(Req("Balaji Developers LLP", "AABCS1429N"))).IsSuccess);
    }

    [Fact]
    public async Task Deleted_clients_are_hidden_but_kept()
    {
        var c = (await _clients.CreateAsync(Req("Temp Client"))).Value!;
        await _clients.DeleteAsync(c.Id);

        Assert.Equal(0, (await _clients.ListAsync(new ClientQuery())).Total);
        Assert.Equal(1, await _db.Set<Client>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Search_matches_mobile_without_spaces_and_summary_counts_types()
    {
        await _clients.CreateAsync(Req("Suresh Agarwal"));
        await _clients.CreateAsync(Req("HDFC Vesu", type: ClientType.BankBranch));

        Assert.Equal(2, (await _clients.ListAsync(new ClientQuery(Search: "9825041201"))).Total);
        Assert.Equal("HDFC Vesu", (await _clients.ListAsync(new ClientQuery(Type: ClientType.BankBranch))).Items.Single().Name);

        var s = await _clients.GetSummaryAsync();
        Assert.Equal((2, 1, 1), (s.Total, s.Individual, s.BankBranch));
        Assert.Equal(["Gujarat"], s.States);
    }

    [Theory]
    [InlineData("AGRPS4521K", null, true)]
    [InlineData("AGRPS4521", null, false)]                    // PAN too short
    [InlineData("AABCS1429N", "24AABCS1429N1Z5", true)]
    [InlineData("AGRPS4521K", "24AABCS1429N1Z5", false)]      // GSTIN doesn't contain the PAN
    public void Pan_and_gstin_rules(string? pan, string? gstin, bool valid) =>
        Assert.Equal(valid, new SaveClientRequestValidator().Validate(Req("X", pan, gstin)).IsValid);

    [Fact]
    public async Task Csv_preview_reads_aliases_quotes_and_flags_bad_rows()
    {
        var csv = "Client Name,Mobile No,Category,PAN,City\n" +
                  "\"Agarwal, Suresh\",98250 41201,Individual,AGRPS4521K,Ahmedabad\n" +
                  "Second,98250 41202,Bank,AGRPS4521K,Surat\n" +      // repeated PAN
                  "Third,123,Alien,,Pune\n" +                           // bad mobile + bad type
                  ",,,,\n";                                            // blank line ignored
        var preview = (await _clients.PreviewImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "clients.csv")).Value!;

        Assert.Equal(3, preview.Rows.Count);
        Assert.Equal("Agarwal, Suresh", preview.Rows[0].Client!.Name);
        Assert.True(preview.Rows[0].IsValid);
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("repeated (row 2)"));
        Assert.Equal(ClientType.BankBranch, preview.Rows[1].Client!.Type);
        Assert.Contains(preview.Rows[2].Errors, e => e.Contains("Unknown type"));
        Assert.True(Enum.IsDefined(preview.Rows[2].Client!.Type)); // never an out-of-range value in the response
        Assert.Contains(preview.Rows[2].Errors, e => e.Contains("10-digit"));
        Assert.Equal((1, 2), (preview.Valid, preview.Invalid));
    }

    [Fact]
    public async Task Excel_import_saves_valid_rows_and_returns_the_rest()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Clients");
        sheet.Cell(1, 1).Value = "Name"; sheet.Cell(1, 2).Value = "Mobile"; sheet.Cell(1, 3).Value = "Type";
        sheet.Cell(2, 1).Value = "Shree Balaji Developers"; sheet.Cell(2, 2).Value = "9099123456"; sheet.Cell(2, 3).Value = "Builder";
        sheet.Cell(3, 1).Value = "No Mobile";
        using var file = new MemoryStream();
        book.SaveAs(file);
        file.Position = 0;

        var preview = (await _clients.PreviewImportAsync(file, "clients.xlsx")).Value!;
        Assert.Equal((1, 1), (preview.Valid, preview.Invalid));

        var result = (await _clients.ImportAsync(new ImportRequest([.. preview.Rows.Where(r => r.IsValid).Select(r => r.Client!)]))).Value!;
        Assert.Equal(1, result.Imported);
        Assert.Equal("CL0001", (await _clients.ListAsync(new ClientQuery())).Items.Single().Code);
    }

    [Fact]
    public async Task File_without_name_and_mobile_columns_is_refused()
    {
        var result = await _clients.PreviewImportAsync(new MemoryStream("a,b\n1,2"u8.ToArray()), "x.csv");
        Assert.Equal("file", result.Error!.Field);
    }
}
