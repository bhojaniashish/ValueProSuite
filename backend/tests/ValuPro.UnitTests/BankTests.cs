using Microsoft.EntityFrameworkCore;
using ValuPro.Application.Abstractions;
using ValuPro.Application.Banks;
using ValuPro.Application.Common;
using ValuPro.Domain.Banks;
using ValuPro.Infrastructure.Banks;
using ValuPro.Infrastructure.Persistence;

namespace ValuPro.UnitTests;

public class BankTests
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
    private readonly BankService _banks;

    public BankTests()
    {
        var tenant = new FixedTenant(Guid.NewGuid());
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditingInterceptor(new NoUser(), tenant, TimeProvider.System))
            .Options, tenant);
        _banks = new BankService(_db, new ValuPro.Infrastructure.Assignments.AssignmentUsage(_db));
    }

    private static readonly BankRules Rules = new(RateBasis.BankRates, 70m, 90m, DepreciationMethod.StraightLine);

    private static SaveBankRequest Bank(string name, string code, string? ifsc = null) =>
        new(name, code, "#2453A6", ifsc, "SBI VR-Format 3.2", true, Rules);

    private static SaveBranchRequest Branch(string name, string? ifsc = null, bool active = true) =>
        new(name, ifsc, "Surat", "Gujarat", null, "Branch Manager", "0261 245 6789", null, active);

    [Fact]
    public async Task Short_code_is_upper_cased_and_unique()
    {
        var sbi = (await _banks.CreateAsync(Bank("State Bank of India", "sbi"))).Value!;
        Assert.Equal("SBI", sbi.ShortCode);

        var dup = await _banks.CreateAsync(Bank("Some Bank", "SBI"));
        Assert.Equal(ErrorKind.Conflict, dup.Error!.Kind);
        Assert.Equal("shortCode", dup.Error.Field);
    }

    [Fact]
    public async Task Branch_ifsc_must_match_bank_prefix_and_be_unique()
    {
        var sbi = (await _banks.CreateAsync(Bank("State Bank of India", "SBI", "SBIN"))).Value!;

        var wrong = await _banks.CreateBranchAsync(sbi.Id, Branch("Vesu", "HDFC0001234"));
        Assert.Equal("ifsc", wrong.Error!.Field);

        Assert.True((await _banks.CreateBranchAsync(sbi.Id, Branch("Vesu", "sbin0001234"))).IsSuccess);
        var dup = await _banks.CreateBranchAsync(sbi.Id, Branch("Adajan", "SBIN0001234"));
        Assert.Equal(ErrorKind.Conflict, dup.Error!.Kind);
    }

    [Fact]
    public async Task List_counts_branches_and_search_matches_branch_city()
    {
        var hdfc = (await _banks.CreateAsync(Bank("HDFC Bank", "HDFC"))).Value!;
        await _banks.CreateAsync(Bank("ICICI Bank", "ICICI"));
        await _banks.CreateBranchAsync(hdfc.Id, Branch("Vesu"));
        await _banks.CreateBranchAsync(hdfc.Id, Branch("Adajan", active: false));

        var list = await _banks.ListAsync(null);
        var h = list.Single(b => b.ShortCode == "HDFC");
        Assert.Equal((2, 1), (h.Branches, h.ActiveBranches));

        // (The in-memory provider compares case-sensitively; SQL Server's collation does not.)
        Assert.Equal(["HDFC"], (await _banks.ListAsync("Adajan")).Select(b => b.ShortCode));
    }

    [Fact]
    public async Task Deleting_a_bank_hides_its_branches_and_frees_the_code()
    {
        var axis = (await _banks.CreateAsync(Bank("Axis Bank", "AXIS"))).Value!;
        await _banks.CreateBranchAsync(axis.Id, Branch("Ring Road", "UTIB0000123"));
        await _banks.DeleteAsync(axis.Id);

        Assert.Empty(await _banks.ListAsync(null));
        Assert.Equal(0, await _db.Set<BankBranch>().CountAsync());
        Assert.True((await _banks.CreateAsync(Bank("Axis Bank", "AXIS"))).IsSuccess);
    }

    [Fact]
    public async Task Questionnaire_keeps_order_and_cleans_choice_options()
    {
        var bob = (await _banks.CreateAsync(Bank("Bank of Baroda", "BOB"))).Value!;
        var result = await _banks.UpdateQuestionnaireAsync(bob.Id, new UpdateQuestionnaireRequest(
        [
            new QuestionItem("Is the property clearly demarcated?", AnswerType.YesNo, ["ignored"], true),
            new QuestionItem("Marketability of the property?", AnswerType.Choice, [" Good ", "Average", "", "good"], false),
        ]));

        var q = result.Value!;
        Assert.Equal("Is the property clearly demarcated?", q[0].Text);
        Assert.Empty(q[0].Options);
        Assert.Equal(["Good", "Average"], q[1].Options);
        Assert.Equal(2, (await _banks.GetAsync(bob.Id)).Value!.Bank.Questions);
    }

    [Fact]
    public async Task Rules_table_updates_several_banks()
    {
        var a = (await _banks.CreateAsync(Bank("SBI", "SBI"))).Value!;
        var b = (await _banks.CreateAsync(Bank("PNB", "PNB"))).Value!;
        var result = await _banks.UpdateRulesAsync(new UpdateRulesRequest(
        [
            new RulesRow(a.Id, new BankRules(RateBasis.CircleRates, 75m, 85m, DepreciationMethod.WrittenDownValue)),
            new RulesRow(b.Id, new BankRules(RateBasis.MarketRates, 72m, 88m, DepreciationMethod.StraightLine)),
        ]));

        var sbi = result.Value!.Single(x => x.ShortCode == "SBI");
        Assert.Equal(new BankRules(RateBasis.CircleRates, 75m, 85m, DepreciationMethod.WrittenDownValue), sbi.Rules);
    }

    [Theory]
    [InlineData(70, 90, true)]
    [InlineData(95, 90, false)] // distress above realizable
    [InlineData(0, 90, false)]
    public void Rules_validation(decimal distress, decimal realizable, bool valid) =>
        Assert.Equal(valid, new BankRulesValidator().Validate(new BankRules(RateBasis.MarketRates, distress, realizable, DepreciationMethod.StraightLine)).IsValid);

    [Fact]
    public void Choice_question_needs_two_options() =>
        Assert.False(new UpdateQuestionnaireRequestValidator().Validate(new UpdateQuestionnaireRequest(
            [new QuestionItem("Marketability?", AnswerType.Choice, ["Good"], false)])).IsValid);
}
