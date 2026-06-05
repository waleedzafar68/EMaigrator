using System;
using System.Collections.Generic;
using System.Linq;
using EMaigrator.Api.Contracts;
using EMaigrator.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// Read-only connector catalog: <c>GET /providers</c> projects every DI-discovered
/// <see cref="IProviderPlugin"/> into a <see cref="ProviderCapabilityDto"/> so the wizard can render the
/// endpoint pickers (which providers can be a source/destination, what auth methods they take, and
/// whether they support a multi-mailbox batch). Requires authentication (the fallback policy rejects
/// anonymous callers with 401) — it is not opted out with <c>.AllowAnonymous()</c>.
/// </summary>
public static class ProviderEndpoints
{
    /// <summary>
    /// Auth methods that authenticate across many mailboxes via an admin/service-account grant. A
    /// provider exposing one of these can run an admin-wide multi-mailbox batch migration; a provider
    /// limited to per-mailbox credentials (e.g. IMAP basic/XOAUTH2, delegated OAuth) cannot. This is the
    /// derivation behind <c>canBatch</c>: imap → false, graph → true (GraphAppOAuth),
    /// gmail → true (GmailServiceAccountDwd).
    /// </summary>
    private static readonly AuthMethod[] BatchCapableAuth =
        [AuthMethod.GraphAppOAuth, AuthMethod.GmailServiceAccountDwd];

    public static RouteGroupBuilder MapProviderEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/providers", List);

        return group;
    }

    private static IResult List([FromServices] IEnumerable<IProviderPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var capabilities = plugins
            .Select(p => new ProviderCapabilityDto(
                p.Id.Value,
                p.CanBeSource,
                p.CanBeDestination,
                CanBatch(p.SupportedAuth),
                p.SupportedAuth.Select(a => a.ToString()).ToArray()))
            .ToArray();

        return Results.Ok(capabilities);
    }

    // canBatch = the provider's SupportedAuth contains an admin/service-account method that
    // authenticates across many mailboxes (derived, not a hardcoded provider-name list).
    private static bool CanBatch(IReadOnlyCollection<AuthMethod> supportedAuth) =>
        supportedAuth.Any(BatchCapableAuth.Contains);
}
