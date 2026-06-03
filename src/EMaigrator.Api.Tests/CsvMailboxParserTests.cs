using System.IO;
using System.Text;
using EMaigrator.Api.Services;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Pure unit coverage for <see cref="CsvMailboxParser"/>: a well-formed CSV parses to its pairs, while a
/// missing header, a blank field, or a duplicate source each surface a row-numbered
/// <see cref="CsvValidationException"/> (no containers / harness required).
/// </summary>
public class CsvMailboxParserTests
{
    private static MemoryStream S(string csv) => new(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public void Parses_valid_csv()
    {
        var pairs = CsvMailboxParser.Parse(S("source_mailbox,destination_mailbox\na@old.com,a@new.com\nb@old.com,b@new.com\n"));
        pairs.Should().HaveCount(2);
        pairs[0].SourceMailbox.Should().Be("a@old.com");
        pairs[1].DestMailbox.Should().Be("b@new.com");
    }

    [Fact]
    public void Rejects_missing_header()
    {
        var act = () => CsvMailboxParser.Parse(S("a@old.com,a@new.com\n"));
        act.Should().Throw<CsvValidationException>().WithMessage("*header*");
    }

    [Fact]
    public void Rejects_blank_field()
    {
        var act = () => CsvMailboxParser.Parse(S("source_mailbox,destination_mailbox\na@old.com,\n"));
        act.Should().Throw<CsvValidationException>().WithMessage("*row 2*");
    }

    [Fact]
    public void Rejects_duplicate_source()
    {
        var act = () => CsvMailboxParser.Parse(S("source_mailbox,destination_mailbox\na@old.com,a@new.com\na@old.com,c@new.com\n"));
        act.Should().Throw<CsvValidationException>().WithMessage("*duplicate*row 3*");
    }
}
