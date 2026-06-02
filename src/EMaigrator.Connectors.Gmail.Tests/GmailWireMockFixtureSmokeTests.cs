using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailWireMockFixtureSmokeTests
{
    [Fact]
    public async Task Harness_RoutesLabelsListToFixtureWithoutRealNetwork()
    {
        using var fx = new GmailWireMockFixture();
        fx.Server
          .Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json")
              .WithBody(GmailWireMockFixture.Fixture("labels.list.json")));

        using var service = fx.CreateService();
        var result = await service.Users.Labels.List("me").ExecuteAsync();

        result.Labels.Should().NotBeNull();
        result.Labels.Select(l => l.Name).Should().Contain("INBOX");
        result.Labels.Select(l => l.Name).Should().Contain("Work/Clients/Acme");
    }
}
