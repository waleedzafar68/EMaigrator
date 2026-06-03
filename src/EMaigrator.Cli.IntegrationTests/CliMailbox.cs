using System;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace EMaigrator.Cli.IntegrationTests;

/// <summary>
/// Shared GreenMail mailbox helpers for the cli-e2e tests: count an INBOX, and poll until it reaches a
/// target count (the destination is asserted by polling because a CLI run/resume returns on terminal status
/// a beat before the final IMAP appends land). Each E2E class uses a DEDICATED mailbox pair
/// (<see cref="GreenMailCliFixture.CreateMailboxPairAsync"/>) so the single shared GreenMail can't leak one
/// class's mail into another's counts.
/// </summary>
internal static class CliMailbox
{
    /// <summary>Counts the messages currently visible in an INBOX via a fresh IMAP session.</summary>
    public static async Task<int> CountAsync(int imapPort, string user, string password)
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", imapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(user, password);
        var inbox = client.Inbox!;
        await inbox.OpenAsync(FolderAccess.ReadOnly);
        var count = inbox.Count;
        await client.DisconnectAsync(true);
        return count;
    }

    /// <summary>Polls an INBOX (fresh session each time) until it reports at least <paramref name="expected"/>
    /// messages, so a newly-appended batch is provably visible before the worker lists the source — removing
    /// the GreenMail append-visibility race. Throws if it never reaches the count within the timeout.</summary>
    public static Task WaitUntilCountAtLeastAsync(
        int imapPort, string user, string password, int expected, TimeSpan timeout) =>
        WaitUntilCountAsync(imapPort, user, password, c => c >= expected, expected, timeout);

    /// <summary>Polls an INBOX until it reports EXACTLY <paramref name="expected"/> messages, then returns that
    /// count. Because a CLI <c>run</c>/<c>resume</c> returns once the migration status is terminal but the
    /// final IMAP appends at the destination can land a beat later (broker drain), the count is awaited rather
    /// than read once — a value that never settles at the expected count (e.g. duplicates → overshoot, or a
    /// missing copy → undershoot) still fails. Throws if it never settles within the timeout.</summary>
    public static Task<int> WaitUntilCountAsync(
        int imapPort, string user, string password, int expected, TimeSpan timeout) =>
        WaitUntilCountAsync(imapPort, user, password, c => c == expected, expected, timeout);

    private static async Task<int> WaitUntilCountAsync(
        int imapPort, string user, string password, Func<int, bool> predicate, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int last = -1;
        while (DateTime.UtcNow < deadline)
        {
            last = await CountAsync(imapPort, user, password);
            if (predicate(last)) return last;
            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Mailbox {user} never reached {expected} messages within {timeout.TotalSeconds:0}s (last saw {last}).");
    }
}
