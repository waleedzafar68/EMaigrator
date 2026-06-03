using System;
using System.Globalization;
using EMaigrator.Api.Identity;
using EMaigrator.Api.Tenancy;
using Microsoft.AspNetCore.Http;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Per-scope test <see cref="ICurrentTenant"/>. Registered scoped so each request/seeding scope gets
/// its own instance, overriding <c>HttpContextCurrentTenant</c> for tests.
/// <para>
/// Direct-DbContext seeding tests set <see cref="Current"/> explicitly (there is no HTTP request, so
/// there is no JWT claim to read). Real HTTP requests in later endpoint/security/functional tests do
/// not touch <see cref="Current"/>, so this still resolves the per-request tenant from the
/// <see cref="JwtTokenIssuer.TenantClaim"/> on the injected <see cref="IHttpContextAccessor"/>.
/// </para>
/// </summary>
public sealed class TestCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TestCurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Explicit tenant override for direct-DbContext seeding tests. <see cref="Guid.Empty"/> (the
    /// default) defers to the request's <c>tenant_id</c> claim instead.
    /// </summary>
    public Guid Current { get; set; } = Guid.Empty;

    public bool IsAuthenticated => Current != Guid.Empty || TryGetClaimTenantId(out _);

    public Guid TenantId =>
        Current != Guid.Empty
            ? Current
            : TryGetClaimTenantId(out var claimTenantId)
                ? claimTenantId
                : throw new UnauthorizedAccessException("No tenant context.");

    private bool TryGetClaimTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        var value = _httpContextAccessor.HttpContext?.User
            .FindFirst(JwtTokenIssuer.TenantClaim)?.Value;

        return value is not null && Guid.TryParse(value, CultureInfo.InvariantCulture, out tenantId);
    }
}
