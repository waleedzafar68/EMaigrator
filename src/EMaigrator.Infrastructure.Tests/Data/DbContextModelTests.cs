using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Tests.Data;

public class DbContextModelTests
{
    private static EmaigratorDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<EmaigratorDbContext>()
            .UseNpgsql("Host=localhost;Database=design_only;Username=u;Password=p")
            .Options;
        return new EmaigratorDbContext(options);
    }

    [Fact]
    public void Ledger_has_unique_index_on_migration_and_identity_key()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(LedgerEntryRow))!;

        var unique = entity.GetIndexes().FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).OrderBy(n => n)
                .SequenceEqual(new[] { nameof(LedgerEntryRow.IdentityKey), nameof(LedgerEntryRow.MailboxMigrationId) }.OrderBy(n => n)));

        unique.Should().NotBeNull("ledger upsert idempotency relies on UNIQUE(MailboxMigrationId, IdentityKey)");
    }

    [Theory]
    [InlineData(typeof(LedgerEntryRow))]
    [InlineData(typeof(MigrationLogRow))]
    public void Metadata_tables_store_no_message_content(Type entityType)
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(entityType)!;
        var forbidden = new[] { "body", "attachment", "content", "payload", "raw", "mime" };

        var offending = entity.GetProperties()
            .Select(p => p.Name)
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offending.Should().BeEmpty($"{entityType.Name} must never persist message content");
    }

    [Fact]
    public void MigrationLog_stores_no_sender_or_recipient()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(MigrationLogRow))!;
        var forbidden = new[] { "sender", "recipient", "from", "to", "cc", "bcc", "address" };

        var offending = entity.GetProperties()
            .Select(p => p.Name)
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offending.Should().BeEmpty("MigrationLogRow must not record correspondents (DESIGN.md §10)");
    }

    [Fact]
    public void All_core_entities_are_mapped()
    {
        using var ctx = NewContext();
        foreach (var t in new[]
        {
            typeof(Job), typeof(MailboxMigration), typeof(FolderTask),
            typeof(LedgerEntryRow), typeof(MigrationLogRow), typeof(CredentialRow), typeof(Tenant)
        })
        {
            ctx.Model.FindEntityType(t).Should().NotBeNull($"{t.Name} must be mapped");
        }
    }
}
