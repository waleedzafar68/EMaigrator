using System.Collections.Generic;

namespace EMaigrator.Api.Contracts;

/// <summary>
/// A connector's wizard-facing capabilities, projected from its <c>IProviderPlugin</c> for the
/// <c>GET /providers</c> endpoint. <c>Id</c> is the <c>ProviderId</c> value ("imap"|"graph"|"gmail");
/// <c>SupportedAuth</c> carries the <c>AuthMethod</c> enum names. <c>CanBatch</c> reports whether the
/// provider supports multi-mailbox/admin batch migration (derived from the auth methods — see the
/// endpoint). Serialized camelCase by the default serializer.
/// </summary>
public sealed record ProviderCapabilityDto(
    string Id,
    bool CanBeSource,
    bool CanBeDestination,
    bool CanBatch,
    IReadOnlyList<string> SupportedAuth);
