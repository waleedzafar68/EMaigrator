using System.Net;
using System.Text;
using EMaigrator.Connectors.Graph.Reconcile;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests.Reconcile;

public class GraphAttachmentUploaderTests
{
    [Fact]
    public async Task Small_attachment_is_added_via_single_post()
    {
        using var srv = WireMockServer.Start();
        srv.Given(Request.Create()
                .WithPath("/v1.0/users/u@x.com/messages/MID/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Created)
                .WithHeader("Content-Type", "application/json").WithBody("{\"id\":\"att1\"}"));

        var client = GraphTestClientFactory.Create(srv.Urls[0]);
        var att = new GraphAttachmentContent("a.pdf", "application/pdf", false, null, 5,
            _ => new MemoryStream(Encoding.ASCII.GetBytes("hello")));

        var ok = await GraphAttachmentUploader.AddAsync(client, "u@x.com", "MID", att, CancellationToken.None);

        ok.Should().BeTrue();
        srv.LogEntries.Should().Contain(e => e.RequestMessage!.Path.EndsWith("/attachments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Large_attachment_uses_upload_session()
    {
        using var srv = WireMockServer.Start();
        var uploadUrl = srv.Urls[0] + "/uploadsession/abc";

        // createUploadSession → 201 with an uploadUrl pointing back at WireMock.
        srv.Given(Request.Create()
                .WithPath("/v1.0/users/u@x.com/messages/MID/attachments/createUploadSession").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Created)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"uploadUrl\":\"{uploadUrl}\",\"expirationDateTime\":\"2030-01-01T00:00:00Z\",\"nextExpectedRanges\":[\"0-\"]}}"));

        // The (single) chunk PUT → 201 Created (final chunk → upload succeeded).
        srv.Given(Request.Create().WithPath("/uploadsession/abc").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Created)
                .WithHeader("Content-Type", "application/json").WithBody("{\"id\":\"att-large\"}"));

        var client = GraphTestClientFactory.Create(srv.Urls[0]);
        var bytes = new byte[4 * 1024 * 1024]; // 4 MB ≥ 3 MB threshold → upload session
        var att = new GraphAttachmentContent("big.bin", "application/octet-stream", false, null, bytes.Length,
            _ => new MemoryStream(bytes, writable: false));

        var ok = await GraphAttachmentUploader.AddAsync(client, "u@x.com", "MID", att, CancellationToken.None);

        ok.Should().BeTrue();
        srv.LogEntries.Should().Contain(e => e.RequestMessage!.Path.EndsWith("/createUploadSession", StringComparison.Ordinal));
        srv.LogEntries.Should().Contain(e => e.RequestMessage!.Path.EndsWith("/uploadsession/abc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inline_attachment_carries_contentId_and_isInline()
    {
        using var srv = WireMockServer.Start();
        srv.Given(Request.Create()
                .WithPath("/v1.0/users/u@x.com/messages/MID/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Created)
                .WithHeader("Content-Type", "application/json").WithBody("{\"id\":\"att1\"}"));

        var client = GraphTestClientFactory.Create(srv.Urls[0]);
        var att = new GraphAttachmentContent("logo.png", "image/png", IsInline: true, ContentId: "cid123", 5,
            _ => new MemoryStream(Encoding.ASCII.GetBytes("hello")));

        var ok = await GraphAttachmentUploader.AddAsync(client, "u@x.com", "MID", att, CancellationToken.None);

        ok.Should().BeTrue();
        var post = srv.LogEntries.Single(e => e.RequestMessage!.Method == "POST");
        var body = post.RequestMessage!.Body ?? "";
        body.Should().Contain("\"isInline\":true").And.Contain("\"contentId\":\"cid123\"");
    }

    [Fact]
    public async Task Failed_post_returns_false_without_leaking()
    {
        using var srv = WireMockServer.Start();
        srv.Given(Request.Create()
                .WithPath("/v1.0/users/u@x.com/messages/MID/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.BadRequest)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"error\":{\"code\":\"ErrorInvalid\",\"message\":\"bad\"}}"));

        var client = GraphTestClientFactory.Create(srv.Urls[0]);
        var att = new GraphAttachmentContent("a.pdf", "application/pdf", false, null, 5,
            _ => new MemoryStream(Encoding.ASCII.GetBytes("hello")));

        var ok = await GraphAttachmentUploader.AddAsync(client, "u@x.com", "MID", att, CancellationToken.None);

        ok.Should().BeFalse();
    }
}
