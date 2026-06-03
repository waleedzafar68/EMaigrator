using System.Collections.Generic;

namespace EMaigrator.Api.Contracts;

/// <summary>
/// The approve-step payload: a per-issue-type map of <c>{ issueType → RemediationAction name }</c>. Each
/// value must parse to a <see cref="EMaigrator.Core.Diagnostics.RemediationAction"/> (else 400).
/// </summary>
public sealed record ApproveRequest(IReadOnlyDictionary<string, string> Resolutions);
