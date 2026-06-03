using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EMaigrator.Api.Identity;

/// <summary>
/// Mints HMAC-SHA256 access tokens for authenticated users. Claims: <c>sub</c> + NameIdentifier =
/// user id, <c>email</c> = user email, and <see cref="TenantClaim"/> (<c>tenant_id</c>) = the user's
/// TenantId — the value every tenant-scoped request is later filtered on (Task 2).
/// </summary>
public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    /// <summary>The claim type carrying the user's tenant id.</summary>
    public const string TenantClaim = "tenant_id";

    // JwtSecurityTokenHandler is documented stateless/thread-safe, so a single instance is reused.
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    private readonly JwtOptions _options;

    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(TenantClaim, user.TenantId.ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var compact = TokenHandler.WriteToken(token);
        return (compact, expiresAt);
    }
}
