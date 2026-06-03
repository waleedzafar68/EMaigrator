using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Tenancy;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Services;

/// <summary>Thrown when the requested job is not visible to the caller's tenant (→ 404).</summary>
public sealed class JobNotFoundException : Exception
{
    public JobNotFoundException()
        : base("Migration not found.")
    {
    }
}

/// <summary>Thrown when <c>side</c> is neither <c>from</c> nor <c>to</c> (→ 400).</summary>
public sealed class BadSideException : Exception
{
    public BadSideException()
        : base("side must be 'from' or 'to'.")
    {
    }
}

/// <summary>
/// Stores a side's connection — non-secret settings serialized onto the Job, the secret via
/// <see cref="ISecretStore"/> (returning a secretRef, never echoed) — and tests it by building the
/// connector via the discovered <see cref="IProviderPlugin"/>. A provider failure is mapped through
/// <see cref="IErrorCatalog"/> into a stable <c>errorCode</c>; the raw signature (which may embed a
/// credential) is never surfaced. The injected <see cref="EmaigratorDbContext"/> is tenant-filtered, so a
/// cross-tenant id is simply invisible (→ <see cref="JobNotFoundException"/>).
/// </summary>
public sealed class ConnectionService : IConnectionService
{
    private readonly EmaigratorDbContext _db;
    private readonly ISecretStore _secrets;
    private readonly ICurrentTenant _tenant;
    private readonly IEnumerable<IProviderPlugin> _plugins;
    private readonly IErrorCatalog _catalog;

    public ConnectionService(
        EmaigratorDbContext db,
        ISecretStore secrets,
        ICurrentTenant tenant,
        IEnumerable<IProviderPlugin> plugins,
        IErrorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(catalog);
        _db = db;
        _secrets = secrets;
        _tenant = tenant;
        _plugins = plugins;
        _catalog = catalog;
    }

    private static void ValidateSide(string side)
    {
        if (side is not ("from" or "to"))
        {
            throw new BadSideException();
        }
    }

    public async Task StoreConnectionAsync(Guid jobId, string side, ConnectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSide(side);

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new JobNotFoundException();

        string? secretRef = null;
        if (!string.IsNullOrEmpty(request.Secret))
        {
            secretRef = await _secrets.StoreAsync(
                _tenant.TenantId.ToString(), request.Secret, ct);
        }

        var providerValue = side == "from" ? job.SourceProvider.Value : job.DestProvider.Value;
        var descriptor = new ConnectionDescriptor
        {
            Provider = new ProviderId(providerValue),
            Auth = Enum.Parse<AuthMethod>(request.Auth, ignoreCase: true),
            Settings = request.Settings,
            SecretRef = secretRef,
        };
        var serialized = JsonSerializer.Serialize(descriptor);

        if (side == "from")
        {
            job.SourceConnectionRef = serialized;
        }
        else
        {
            job.DestConnectionRef = serialized;
        }

        job.WizardStep = Math.Max(job.WizardStep, 2);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(Guid jobId, string side, CancellationToken ct)
    {
        ValidateSide(side);

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new JobNotFoundException();

        var raw = side == "from" ? job.SourceConnectionRef : job.DestConnectionRef;
        if (string.IsNullOrEmpty(raw))
        {
            return new ConnectionTestResult(false, 0, 0, "NO_CONNECTION", "No connection configured for this side.");
        }

        var descriptor = JsonSerializer.Deserialize<ConnectionDescriptor>(raw)!;
        var plugin = _plugins.FirstOrDefault(p => p.Id.Value == descriptor.Provider.Value)
            ?? throw new InvalidOperationException(
                FormattableString.Invariant($"No plugin for provider {descriptor.Provider.Value}."));

        var secretValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(descriptor.SecretRef))
        {
            secretValues["secret"] = await _secrets.RetrieveAsync(descriptor.SecretRef, ct);
        }

        var bundle = new SecretBundle(secretValues);

        try
        {
            if (side == "from")
            {
                await using var src = plugin.CreateSource(descriptor, bundle);
                return await src.TestConnectionAsync(ct);
            }

            await using var dst = plugin.CreateDestination(descriptor, bundle);
            return await dst.TestConnectionAsync(ct);
        }
#pragma warning disable CA1031 // Catch-all is intentional: any provider failure must become a mapped, credential-free result.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The connector normalizes failures to a "<provider>:<condition>" signature; map it via the
            // catalog into a stable, credential-free code + diagnosis. Never echo the raw signature/creds.
            var signature = ex.Message;
            var resolution = _catalog.Match(descriptor.Provider, signature);
            var code = resolution is null ? "UNKNOWN_ERROR" : ToStableCode(descriptor.Provider, signature);
            return new ConnectionTestResult(false, 0, 0, code, resolution?.Diagnosis ?? "Connection failed.");
        }
    }

    private static string ToStableCode(ProviderId provider, string signature)
    {
        var condition = signature.Contains(':', StringComparison.Ordinal)
            ? signature[(signature.IndexOf(':', StringComparison.Ordinal) + 1)..]
            : signature;
        // e.g. ("imap","AUTHENTICATIONFAILED") -> "IMAP_AUTH_FAILED"
        var normalized = condition.Replace("AUTHENTICATIONFAILED", "AUTH_FAILED", StringComparison.Ordinal);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{provider.Value.ToUpperInvariant()}_{normalized}");
    }
}
