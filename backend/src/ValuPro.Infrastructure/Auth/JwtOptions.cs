namespace ValuPro.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "valupro";
    public string Audience { get; set; } = "valupro-web";

    /// <summary>At least 32 characters. From user-secrets locally and Key Vault in Azure.</summary>
    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 60;
}
