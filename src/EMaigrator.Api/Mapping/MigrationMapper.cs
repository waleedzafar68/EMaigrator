using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EMaigrator.Api.Contracts;
using EMaigrator.Infrastructure.Data;

namespace EMaigrator.Api.Mapping;

/// <summary>
/// Projects an engine <see cref="Job"/> plus its <see cref="MailboxMigration"/> rows into the
/// camelCase <see cref="MigrationDto"/> the API exposes. Empty provider ids map to <c>null</c>, the
/// progress summary is null until a mailbox exists, and the scope summary collapses a single mailbox to
/// its source→dest pair (or a count when batched).
/// </summary>
public static class MigrationMapper
{
    public static MigrationDto ToDto(Job job, IReadOnlyCollection<MailboxMigration> mailboxes)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(mailboxes);

        var from = job.SourceProvider.Value.Length == 0 ? null : job.SourceProvider.Value;
        var to = job.DestProvider.Value.Length == 0 ? null : job.DestProvider.Value;

        var migrated = mailboxes.Sum(m => m.MigratedCount);
        var total = mailboxes.Sum(m => m.MigratedCount + m.SkippedCount + m.FailedCount);

        var progress = mailboxes.Count == 0
            ? null
            : new MigrationProgressSummary(
                migrated,
                total,
                total == 0 ? 0 : Math.Round(100.0 * migrated / total, 1),
                CurrentFolder: null,
                MsgPerMin: 0);

        var scopeSummary = ScopeSummary(job, mailboxes);

        var mode = job.Mode == JobMode.Reconcile ? "reconcile" : "migrate";

        return new MigrationDto(
            job.Id,
            job.Status.ToString(),
            job.WizardStep,
            from,
            to,
            job.IsBatch,
            scopeSummary,
            mailboxes.Count,
            progress,
            job.CreatedAt,
            mode);
    }

    private static string? ScopeSummary(Job job, IReadOnlyCollection<MailboxMigration> mailboxes)
    {
        if (mailboxes.Count == 0)
        {
            return null;
        }

        if (job.IsBatch)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{mailboxes.Count} mailboxes");
        }

        // A non-batch job normally has exactly one mailbox; collapse it to its source→dest pair.
        if (mailboxes.Count == 1)
        {
            var first = mailboxes.First();
            return $"{first.SourceMailbox} → {first.DestMailbox}";
        }

        // Defensive fallback for the (unexpected) non-batch, count≠1 case — report the honest count.
        return string.Create(CultureInfo.InvariantCulture, $"{mailboxes.Count} mailboxes");
    }
}
