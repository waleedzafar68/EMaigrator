using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>Builds a GraphServiceClient pointed at a WireMock base URL with no real token.</summary>
public static class GraphTestClientFactory
{
    public static GraphServiceClient Create(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = baseUrl.TrimEnd('/') + "/v1.0",
        };
        return new GraphServiceClient(adapter);
    }
}
