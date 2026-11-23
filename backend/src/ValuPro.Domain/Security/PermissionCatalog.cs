namespace ValuPro.Domain.Security;

public sealed record PermissionAction(string Key, string Label);

public sealed record PermissionModule(string Key, string Label, IReadOnlyList<PermissionAction> Actions);

/// <summary>Rows of the role permission matrix: each module with its actions, in sidebar order.</summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionModule> Modules =
    [
        new("dashboard", "Dashboard", [new(Permissions.Dashboard.View, "View")]),
        new("clients", "Clients", [new(Permissions.Clients.View, "View"), new(Permissions.Clients.Manage, "Manage")]),
        new("banks", "Banks", [new(Permissions.Banks.View, "View"), new(Permissions.Banks.Manage, "Manage")]),
        new("properties", "Properties", [new(Permissions.Properties.View, "View"), new(Permissions.Properties.Manage, "Manage")]),
        new("assignments", "Assignments", [new(Permissions.Assignments.View, "View"), new(Permissions.Assignments.Manage, "Manage")]),
        new("inspections", "Inspections", [new(Permissions.Inspections.View, "View"), new(Permissions.Inspections.Manage, "Manage")]),
        new("valuation", "Valuation Engine", [new(Permissions.Valuation.View, "View"), new(Permissions.Valuation.Manage, "Manage")]),
        new("reports", "Reports", [new(Permissions.Reports.View, "View"), new(Permissions.Reports.Manage, "Manage"), new(Permissions.Reports.Approve, "Approve")]),
        new("billing", "Billing", [new(Permissions.Billing.View, "View"), new(Permissions.Billing.Manage, "Manage")]),
        new("rates", "Rate Master", [new(Permissions.RateMaster.View, "View"), new(Permissions.RateMaster.Manage, "Manage")]),
        new("users", "User Management", [new(Permissions.Users.View, "View"), new(Permissions.Users.Manage, "Manage")]),
        new("settings", "Settings", [new(Permissions.Settings.View, "View"), new(Permissions.Settings.Manage, "Manage")]),
    ];
}
