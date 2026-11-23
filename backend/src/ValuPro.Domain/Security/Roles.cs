namespace ValuPro.Domain.Security;

/// <summary>The seven roles from the prototype's User Management screen.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string SeniorValuer = "Senior Valuer";
    public const string JuniorValuer = "Junior Valuer";
    public const string Architect = "Architect";
    public const string OfficeStaff = "Office Staff";
    public const string Reviewer = "Reviewer";
    public const string BankUser = "Bank User";

    public static readonly IReadOnlyList<string> All =
        [Admin, SeniorValuer, JuniorValuer, Architect, OfficeStaff, Reviewer, BankUser];
}
