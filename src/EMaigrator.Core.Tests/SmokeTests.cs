using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Harness_Runs()
    {
        var marker = typeof(EMaigrator.Core.AssemblyMarker).Assembly.GetName().Name;
        marker.Should().Be("EMaigrator.Core");
    }

    [Fact]
    public void ProviderId_ToString_ReturnsValue()
    {
        var id = new ProviderId("imap");
        id.ToString().Should().Be("imap");
    }
}
