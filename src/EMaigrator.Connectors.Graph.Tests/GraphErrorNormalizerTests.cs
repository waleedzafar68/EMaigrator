using System.Globalization;
using EMaigrator.Connectors.Graph;
using FluentAssertions;
using Microsoft.Graph.Models.ODataErrors;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphErrorNormalizerTests
{
    private static ODataError ODataError(string code, int status, int? retryAfterSeconds = null)
    {
        var err = new ODataError
        {
            ResponseStatusCode = status,
            Error = new MainError { Code = code, Message = "human readable message" }
        };
        if (retryAfterSeconds is { } s)
            err.ResponseHeaders = new Microsoft.Kiota.Abstractions.RequestHeaders
            {
                { "Retry-After", s.ToString(CultureInfo.InvariantCulture) }
            };
        return err;
    }

    [Fact]
    public void Throttled_429_is_transient_with_retry_after()
    {
        var n = GraphErrorNormalizer.Normalize(ODataError("errorThrottledRequest", 429, retryAfterSeconds: 17));

        n.Signature.Should().Be("graph:429:throttled");
        n.IsTransient.Should().BeTrue();
        n.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public void Item_not_found_is_non_transient()
    {
        var n = GraphErrorNormalizer.Normalize(ODataError("ErrorItemNotFound", 404));

        n.Signature.Should().Be("graph:404:ErrorItemNotFound");
        n.IsTransient.Should().BeFalse();
        n.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Access_denied_403_maps_signature()
    {
        GraphErrorNormalizer.Normalize(ODataError("ErrorAccessDenied", 403))
            .Signature.Should().Be("graph:403:ErrorAccessDenied");
    }

    [Fact]
    public void Invalid_token_401_maps_signature()
    {
        GraphErrorNormalizer.Normalize(ODataError("InvalidAuthenticationToken", 401))
            .Signature.Should().Be("graph:401:InvalidAuthenticationToken");
    }

    [Fact]
    public void Service_unavailable_503_is_transient_with_retry_after()
    {
        var n = GraphErrorNormalizer.Normalize(ODataError("serviceUnavailable", 503, retryAfterSeconds: 30));

        n.Signature.Should().Be("graph:503:serviceUnavailable");
        n.IsTransient.Should().BeTrue();
        n.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Signature_never_leaks_tenant_or_secret()
    {
        var leaky = ODataError("ErrorAccessDenied", 403);
        leaky.Error!.Message =
            "Access denied for tenant 11111111-1111-1111-1111-111111111111 " +
            "secret super-secret-client-value account user@contoso.onmicrosoft.com";

        var n = GraphErrorNormalizer.Normalize(leaky);

        n.Signature.Should().NotContain("11111111-1111-1111-1111-111111111111");
        n.Signature.Should().NotContain("super-secret-client-value");
        n.Signature.Should().NotContain("user@contoso.onmicrosoft.com");
    }

    [Fact]
    public void Unknown_exception_maps_to_unknown()
    {
        var n = GraphErrorNormalizer.Normalize(new InvalidOperationException("boom"));

        n.Signature.Should().Be("graph:unknown");
        n.IsTransient.Should().BeFalse();
    }
}
