namespace EMaigrator.Api.Identity;

/// <summary>
/// JWT issuance/validation settings, bound from the <c>Jwt</c> configuration section. The
/// <see cref="SigningKey"/> is the HMAC-SHA256 symmetric key (≥ 32 bytes) and has no default — it
/// must be supplied per environment.
/// </summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "emaigrator";

    public string Audience { get; set; } = "emaigrator";

    public string SigningKey { get; set; } = "";

    public int LifetimeMinutes { get; set; } = 60;
}
