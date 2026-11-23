namespace ValuPro.Domain.Security;

/// <summary>
/// Permission keys, "module.action". Stored as role claims so the role × module matrix
/// in User Management can be edited at runtime (Phase 1).
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Dashboard { public const string View = "dashboard.view"; }
    public static class Clients { public const string View = "clients.view"; public const string Manage = "clients.manage"; }
    public static class Banks { public const string View = "banks.view"; public const string Manage = "banks.manage"; }
    public static class Properties { public const string View = "properties.view"; public const string Manage = "properties.manage"; }
    public static class Assignments { public const string View = "assignments.view"; public const string Manage = "assignments.manage"; }
    public static class Inspections { public const string View = "inspections.view"; public const string Manage = "inspections.manage"; }
    public static class Valuation { public const string View = "valuation.view"; public const string Manage = "valuation.manage"; }
    public static class Reports { public const string View = "reports.view"; public const string Manage = "reports.manage"; public const string Approve = "reports.approve"; }
    public static class Billing { public const string View = "billing.view"; public const string Manage = "billing.manage"; }
    public static class RateMaster { public const string View = "rates.view"; public const string Manage = "rates.manage"; }
    public static class Users { public const string View = "users.view"; public const string Manage = "users.manage"; }
    public static class Settings { public const string View = "settings.view"; public const string Manage = "settings.manage"; }

    public static readonly IReadOnlyList<string> All =
    [
        Dashboard.View,
        Clients.View, Clients.Manage,
        Banks.View, Banks.Manage,
        Properties.View, Properties.Manage,
        Assignments.View, Assignments.Manage,
        Inspections.View, Inspections.Manage,
        Valuation.View, Valuation.Manage,
        Reports.View, Reports.Manage, Reports.Approve,
        Billing.View, Billing.Manage,
        RateMaster.View, RateMaster.Manage,
        Users.View, Users.Manage,
        Settings.View, Settings.Manage,
    ];

    /// <summary>Starting permissions per role, seeded once. Admins can change them later.</summary>
    public static IReadOnlyList<string> DefaultsFor(string role) => role switch
    {
        Roles.Admin => All,
        Roles.SeniorValuer =>
        [
            Dashboard.View, Clients.View, Clients.Manage, Banks.View, Properties.View, Properties.Manage,
            Assignments.View, Assignments.Manage, Inspections.View, Inspections.Manage,
            Valuation.View, Valuation.Manage, Reports.View, Reports.Manage, RateMaster.View, Billing.View,
        ],
        Roles.JuniorValuer =>
        [
            Dashboard.View, Clients.View, Properties.View, Assignments.View,
            Inspections.View, Inspections.Manage, Valuation.View, Valuation.Manage, Reports.View, RateMaster.View,
        ],
        Roles.Architect =>
        [
            Dashboard.View, Properties.View, Assignments.View, Inspections.View, Inspections.Manage,
            Valuation.View, Reports.View, RateMaster.View,
        ],
        Roles.OfficeStaff =>
        [
            Dashboard.View, Clients.View, Clients.Manage, Banks.View, Banks.Manage, Properties.View,
            Properties.Manage, Assignments.View, Assignments.Manage, Billing.View, Billing.Manage,
        ],
        Roles.Reviewer =>
        [
            Dashboard.View, Assignments.View, Inspections.View, Valuation.View,
            Reports.View, Reports.Manage, Reports.Approve, RateMaster.View,
        ],
        Roles.BankUser => [Assignments.View, Reports.View],
        _ => [],
    };
}
