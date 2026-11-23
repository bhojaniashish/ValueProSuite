using Microsoft.AspNetCore.DataProtection;

namespace ValuPro.Infrastructure.Settings;

/// <summary>
/// Encrypts integration secrets (SMTP password, SMS API key) before they reach the database.
/// Uses ASP.NET Data Protection; on Azure App Service the key ring is persisted and shared by the platform.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("ValuPro.FirmSettings.Secrets.v1");

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // key ring lost or rotated out: treat as not set, admin re-enters it
        }
    }
}
