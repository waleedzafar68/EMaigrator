using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

/// <summary>
/// Sets a draft migration's source/destination providers (e.g. <c>imap</c> → <c>graph</c>) and advances
/// the wizard. Both sides are required and non-empty. (CONTRACTS.md §6)
/// </summary>
public sealed record SetEndpointsRequest(
    [property: Required, MinLength(1)] string From,
    [property: Required, MinLength(1)] string To);
