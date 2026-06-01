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
}
