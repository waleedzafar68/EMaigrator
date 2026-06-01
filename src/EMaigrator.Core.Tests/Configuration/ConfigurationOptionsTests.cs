using EMaigrator.Core.Configuration;

namespace EMaigrator.Core.Tests.Configuration;

public class ConfigurationOptionsTests
{
    [Fact]
    public void OrchestrationOptions_Defaults()
    {
        var o = new OrchestrationOptions();
        o.GlobalMaxConcurrentMigrations.Should().Be(16);
        o.PerTenantConcurrencyCap.Should().Be(8);
        o.PerMailboxFolderConcurrency.Should().Be(4);
        o.BatchSize.Should().Be(100);
        o.ConsumerPrefetch.Should().Be(16);
        o.DlqRetryCount.Should().Be(5);
    }

    [Fact]
    public void RateLimitOptions_StartsEmptyAndAcceptsBuckets()
    {
        var o = new RateLimitOptions();
        o.Buckets.Should().BeEmpty();
        o.Buckets["graph:dest-tenant"] = new BucketSpec { RefillPerSecond = 10.0, Burst = 50 };
        o.Buckets["graph:dest-tenant"].RefillPerSecond.Should().Be(10.0);
        o.Buckets["graph:dest-tenant"].Burst.Should().Be(50);
    }

    [Fact]
    public void RetentionOptions_Default()
        => new RetentionOptions().LogRetentionDays.Should().Be(30);

    [Fact]
    public void SecretStoreOptions_Defaults()
    {
        var o = new SecretStoreOptions();
        o.Mode.Should().Be("LocalKey");
        o.KeyRef.Should().BeNull();
    }
}
