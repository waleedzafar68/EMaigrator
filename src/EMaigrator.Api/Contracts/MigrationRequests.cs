using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

/// <summary>
/// Sets a draft migration's source/destination providers (e.g. <c>imap</c> → <c>graph</c>) and advances
/// the wizard. Both sides are required and non-empty. (CONTRACTS.md §6)
/// </summary>
public sealed record SetEndpointsRequest(
    [property: Required, MinLength(1)] string From,
    [property: Required, MinLength(1)] string To);

/// <summary>
/// Sets a draft migration's <c>mode</c> at the wizard's Step 1 chooser. The value must be exactly
/// <c>migrate</c> or <c>reconcile</c> (the regex is whole-string anchored by the validator). (CONTRACTS.md §6)
/// </summary>
public sealed record SetModeRequest(
    [property: Required, RegularExpression("migrate|reconcile")] string Mode);
