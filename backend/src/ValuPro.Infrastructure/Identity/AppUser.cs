using Microsoft.AspNetCore.Identity;
using ValuPro.Domain.Common;
using ValuPro.Domain.Security;

namespace ValuPro.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>, ITenantScoped
{
    public AppUser() => Id = Guid.CreateVersion7();

    public Guid TenantId { get; set; }
    public required string FullName { get; set; }

    /// <summary>Valuer registration (e.g. RV/2018/MH/0412), architect licence, or blank.</summary>
    public string? RegistrationNo { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>Set when an admin creates the user or resets the password; cleared once they choose their own.</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public bool CanSignIn => Status != UserStatus.Inactive;
}
