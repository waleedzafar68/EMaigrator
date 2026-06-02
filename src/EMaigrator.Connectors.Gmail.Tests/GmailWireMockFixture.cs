using System;
using System.IO;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using WireMock.Server;
using WireMock.Settings;

namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>
/// Stands up a local WireMock.Net server and produces a GmailService whose BaseUri points at
/// it, so all Gmail API calls are served by recorded fixtures with zero real network traffic.
/// </summary>
public sealed class GmailWireMockFixture : IDisposable
{
    public WireMockServer Server { get; }

    public GmailWireMockFixture()
    {
        Server = WireMockServer.Start(new WireMockServerSettings { StartAdminInterface = false });
    }

    public string BaseUrl => Server.Urls[0];

    public static string Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// A GmailService that talks to the mock server. No credential is attached — WireMock does
    /// not validate Authorization headers, so tests stay offline and credential-free.
    /// </summary>
    public GmailService CreateService()
    {
        return new GmailService(new BaseClientService.Initializer
        {
            // Host root only — the Gmail SDK's resource paths already include the "gmail/v1/"
            // segment, so a "/gmail/v1/" BaseUri would double-prefix and miss the fixtures.
            BaseUri = BaseUrl,
            ApplicationName = "EMaigrator.Tests",
            // No HttpClientInitializer: avoids any token fetch against real Google endpoints.
        });
    }

    public void Dispose() => Server.Dispose();
}
