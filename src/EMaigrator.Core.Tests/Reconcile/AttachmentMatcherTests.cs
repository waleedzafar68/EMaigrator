using EMaigrator.Core.Model;
using EMaigrator.Core.Reconcile;

namespace EMaigrator.Core.Tests.Reconcile;

public class AttachmentMatcherTests
{
    private static CanonicalAttachmentInfo A(string name, string type = "application/pdf", long size = 10)
        => new(name, type, size);

    [Fact]
    public void Missing_returns_source_attachments_absent_at_dest()
    {
        var source = new[] { A("a.pdf"), A("b.png", "image/png") };
        var dest = new[] { A("a.pdf") };
        AttachmentMatcher.Missing(source, dest).Should().ContainSingle().Which.FileName.Should().Be("b.png");
    }

    [Fact]
    public void Missing_is_multiset_and_size_insensitive()
    {
        var source = new[] { A("img.png", "image/png", 100), A("img.png", "image/png", 999) };
        var dest = new[] { A("IMG.PNG", "IMAGE/PNG", 5) }; // case-insensitive name+type match, size ignored
        AttachmentMatcher.Missing(source, dest).Should().HaveCount(1); // one of the two still missing
    }

    [Fact]
    public void Missing_empty_when_all_present_or_no_source()
    {
        AttachmentMatcher.Missing(new[] { A("a.pdf") }, new[] { A("a.pdf") }).Should().BeEmpty();
        AttachmentMatcher.Missing(System.Array.Empty<CanonicalAttachmentInfo>(), new[] { A("a.pdf") }).Should().BeEmpty();
    }
}
