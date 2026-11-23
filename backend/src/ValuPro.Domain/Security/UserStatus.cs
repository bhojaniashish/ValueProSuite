namespace ValuPro.Domain.Security;

/// <summary>Team member status, as in the prototype. Only Inactive users are blocked from signing in.</summary>
public enum UserStatus
{
    Active = 0,
    OnLeave = 1,
    Inactive = 2,
}
