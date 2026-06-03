using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

/// <summary>
/// A request to store one side's connection: the non-secret <see cref="Settings"/> (host/port/email/...),
/// the <see cref="Auth"/> method (parses to <c>AuthMethod</c>), and the optional <see cref="Secret"/>
/// (password / client secret / service-account JSON). The secret is stored via <c>ISecretStore</c> and
/// NEVER echoed back. (CONTRACTS.md §2)
/// </summary>
public sealed record ConnectionRequest(
    [property: Required] string Auth,
    [property: Required] IReadOnlyDictionary<string, string> Settings,
    string? Secret);
