using System;

namespace EMaigrator.Api.Identity;

/// <summary>Issues a signed access token for an authenticated <see cref="ApplicationUser"/>.</summary>
public interface IJwtTokenIssuer
{
    /// <summary>
    /// Mints an HMAC-SHA256 JWT carrying the user's id, email, and tenant; returns the compact token
    /// and its absolute expiry.
    /// </summary>
    (string Token, DateTimeOffset ExpiresAt) Issue(ApplicationUser user);
}
