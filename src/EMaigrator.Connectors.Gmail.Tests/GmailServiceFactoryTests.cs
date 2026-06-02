using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailServiceFactoryTests
{
    // A syntactically valid (fake) service-account JSON with a real RSA test key so
    // GoogleCredential.FromJson succeeds. The key is a throwaway generated for tests only.
    private static string FakeServiceAccountJson() => TestServiceAccount.Json;

    private static ConnectionDescriptor Descriptor(string? email = "target@example.com") => new()
    {
        Provider = new ProviderId("gmail"),
        Auth = AuthMethod.GmailServiceAccountDwd,
        Settings = new Dictionary<string, string>
        {
            ["accountEmail"] = email ?? "",
        },
    };

    [Fact]
    public void RequiredScopes_IsSingleMailGoogleComScope()
    {
        GmailServiceFactory.RequiredScopes.Should().Equal(new[] { "https://mail.google.com/" });
    }

    [Fact]
    public void FromDescriptor_MissingEmail_ThrowsWithoutLeakingSecret()
    {
        var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = FakeServiceAccountJson() });
        var act = () => GmailConnectionConfig.FromDescriptor(Descriptor(email: ""), secrets);
        act.Should().Throw<ArgumentException>()
           .Which.Message.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public void FromDescriptor_MissingJson_ThrowsWithoutLeakingSecret()
    {
        var secrets = new SecretBundle(new Dictionary<string, string>());
        var act = () => GmailConnectionConfig.FromDescriptor(Descriptor(), secrets);
        act.Should().Throw<ArgumentException>()
           .Which.Message.Should().NotContain("BEGIN");
    }

    [Fact]
    public void Create_BuildsServiceWithoutWritingJsonToDisk()
    {
        var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = FakeServiceAccountJson() });
        var config = GmailConnectionConfig.FromDescriptor(Descriptor(), secrets);

        var tempBefore = Directory.GetFiles(Path.GetTempPath()).Length;
        using var service = GmailServiceFactory.Create(config);
        var tempAfter = Directory.GetFiles(Path.GetTempPath()).Length;

        service.Should().NotBeNull();
        service.HttpClientInitializer.Should().NotBeNull();
        tempAfter.Should().Be(tempBefore, "the SA JSON must be parsed in-memory, never spilled to a temp file");
    }

    [Fact]
    public void Config_DoesNotExposeRawJsonViaPublicProperty()
    {
        var json = FakeServiceAccountJson();
        var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = json });
        var config = GmailConnectionConfig.FromDescriptor(Descriptor(), secrets);

        var leaking = config.GetType()
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(config))
            .Any(v => v != null && v.Contains("PRIVATE KEY"));

        leaking.Should().BeFalse();
    }
}
