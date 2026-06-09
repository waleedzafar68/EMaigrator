using System;

namespace EMaigrator.Api.Contracts;

/// <summary>
/// Aggregate progress across a migration's mailboxes: messages migrated of the total seen so far, the
/// derived percentage, the folder currently in flight (when known), and the recent throughput. Null on
/// a <see cref="MigrationDto"/> until at least one mailbox exists. (CONTRACTS.md §6)
/// </summary>
public sealed record MigrationProgressSummary(
    long Migrated,
    long Total,
    double Percent,
    string? CurrentFolder,
    double MsgPerMin);

/// <summary>
/// The migration projection returned by every migrations route. Serialized camelCase by the default Web
/// JSON options → keys <c>id, status, wizardStep, from, to, isBatch, scopeSummary, mailboxCount,
/// progress, createdAt, mode</c>. <c>mode</c> (<c>"migrate"</c>|<c>"reconcile"</c>, default
/// <c>"migrate"</c>) drives the mode-branched wizard. (CONTRACTS.md §6)
/// </summary>
public sealed record MigrationDto(
    Guid Id,
    string Status,
    int WizardStep,
    string? From,
    string? To,
    bool IsBatch,
    string? ScopeSummary,
    int MailboxCount,
    MigrationProgressSummary? Progress,
    DateTimeOffset CreatedAt,
    string Mode);
