using System;
using System.Globalization;
using EMaigrator.Api.Identity;
using Microsoft.AspNetCore.Http;

namespace EMaigrator.Api.Tenancy;

/// <summary>
/// Resolves the caller's tenant from the <see cref="JwtTokenIssuer.TenantClaim"/> (<c>tenant_id</c>)
/// on the current request's authenticated principal. <see cref="IsAuthenticated"/> is true only when
/// that claim is present and parses to a <see cref="Guid"/>.
/// </summary>
public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => TryGetTenantId(out _);

    public Guid TenantId =>
        TryGetTenantId(out var tenantId)
            ? tenantId
            : throw new UnauthorizedAccessException("No tenant context.");

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        var value = _httpContextAccessor.HttpContext?.User
            .FindFirst(JwtTokenIssuer.TenantClaim)?.Value;

        return value is not null && Guid.TryParse(value, CultureInfo.InvariantCulture, out tenantId);
    }
}
