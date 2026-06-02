using System;
using System.Net;
using EMaigrator.Connectors.Gmail;
using FluentAssertions;
using Google;
using Google.Apis.Requests;
using Xunit;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailErrorNormalizerTests
{
    private static GoogleApiException MakeApiException(HttpStatusCode status, string reason, string message)
    {
        var err = new RequestError
        {
            Code = (int)status,
            Message = message,
            Errors = new System.Collections.Generic.List<SingleError>
            {
                new SingleError { Reason = reason, Message = message },
            },
        };
        return new GoogleApiException("gmail", message) { HttpStatusCode = status, Error = err };
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rateLimitExceeded", "gmail:429:rateLimitExceeded")]
    [InlineData(HttpStatusCode.TooManyRequests, "userRateLimitExceeded", "gmail:429:userRateLimitExceeded")]
    [InlineData(HttpStatusCode.Forbidden, "quotaExceeded", "gmail:403:quotaExceeded")]
    [InlineData(HttpStatusCode.Unauthorized, "authError", "gmail:401:authError")]
    [InlineData(HttpStatusCode.NotFound, "notFound", "gmail:404:notFound")]
    public void Normalize_MapsKnownGoogleErrors(HttpStatusCode status, string reason, string expected)
    {
        var ex = MakeApiException(status, reason, "boom");
        GmailErrorNormalizer.Normalize(ex).Should().Be(expected);
    }

    [Fact]
    public void Normalize_UnknownException_ReturnsGenericSignature()
    {
        GmailErrorNormalizer.Normalize(new InvalidOperationException("nope"))
            .Should().Be("gmail:unknown");
    }

    [Fact]
    public void Normalize_DoesNotLeakImpersonatedMailbox()
    {
        var ex = MakeApiException(
            HttpStatusCode.Forbidden, "quotaExceeded",
            "User rate limit exceeded for victim@example.com (project 12345)");
        var sig = GmailErrorNormalizer.Normalize(ex);

        sig.Should().Be("gmail:403:quotaExceeded");
        sig.Should().NotContain("@");
        sig.Should().NotContain("victim");
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("0", 0)]
    [InlineData("120", 120)]
    public void TryParseRetryAfter_ReturnsSeconds_ForNumericValue(string header, int expectedSeconds)
    {
        GmailErrorNormalizer.TryParseRetryAfter(header)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    public void TryParseRetryAfter_ReturnsNull_ForMissingOrInvalidValue(string? header)
    {
        GmailErrorNormalizer.TryParseRetryAfter(header).Should().BeNull();
    }
}
