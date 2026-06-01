using System.Globalization;
using EMaigrator.Infrastructure.Observability;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.InMemory;

namespace EMaigrator.Infrastructure.Tests.Observability;

public class SecretScrubbingTests
{
    [Fact]
    public void Secret_named_properties_are_redacted()
    {
        const string plaintext = "Sup3rSecretPassw0rd!";
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("connecting with {Password} and {ClientSecret}", plaintext, "client-xyz");

        var evt = sink.LogEvents.Single();
        var rendered = evt.RenderMessage(CultureInfo.InvariantCulture);
        rendered.Should().NotContain(plaintext);
        rendered.Should().NotContain("client-xyz");
        ((ScalarValue)evt.Properties["Password"]).Value.Should().Be("***REDACTED***");
        ((ScalarValue)evt.Properties["ClientSecret"]).Value.Should().Be("***REDACTED***");
    }

    [Fact]
    public void Nonsecret_properties_pass_through()
    {
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("migrating folder {SourceFolder}", "INBOX");

        var evt = sink.LogEvents.Single();
        ((ScalarValue)evt.Properties["SourceFolder"]).Value.Should().Be("INBOX");
    }
}
