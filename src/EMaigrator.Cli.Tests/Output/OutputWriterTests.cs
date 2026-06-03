using System.Reflection;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Output;

public class OutputWriterTests
{
    [Fact]
    public void Json_connect_test_emits_camelCase_and_no_secret_keys()
    {
        var sw = new StringWriter();
        var writer = new JsonOutputWriter(sw);

        writer.WriteConnectTest(new ConnectTestOutput(Ok: true, FolderCount: 12, MessageCount: 3400, ErrorCode: null));

        string json = sw.ToString();
        json.Should().Contain("\"ok\": true").And.Contain("\"folderCount\": 12").And.Contain("\"messageCount\": 3400");
        json.ToLowerInvariant().Should().NotContain("password").And.NotContain("secret").And.NotContain("token");
    }

    [Fact]
    public void Json_preflight_emits_issues_and_estimate()
    {
        var sw = new StringWriter();
        var writer = new JsonOutputWriter(sw);
        var output = new PreflightOutput(
            Issues:
            [
                new PreflightIssueOutput("FolderTooDeep", Severity.Warning, RemediationAction.FlattenFolder,
                    ["/A/B/C/D/E"], "Folder exceeds destination max depth.")
            ],
            Estimate: new EstimateOutput(MailboxCount: 1, FolderCount: 12, MessageCount: 3400, TotalBytes: 1_000_000));

        writer.WritePreflight(output);

        string json = sw.ToString();
        json.Should().Contain("\"issueType\": \"FolderTooDeep\"")
            .And.Contain("\"recommendedAction\": \"FlattenFolder\"")
            .And.Contain("\"mailboxCount\": 1")
            .And.Contain("\"messageCount\": 3400");
    }

    [Fact]
    public void Human_writer_produces_non_empty_output()
    {
        var sw = new StringWriter();
        var writer = new HumanOutputWriter(sw);

        writer.WriteConnectTest(new ConnectTestOutput(true, 12, 3400, null));

        sw.ToString().Should().Contain("12").And.Contain("3400");
    }

    [Fact]
    public void No_result_dto_property_is_named_like_a_secret()
    {
        Type[] dtoTypes =
        [
            typeof(ConnectTestOutput), typeof(PreflightOutput), typeof(PreflightIssueOutput),
            typeof(EstimateOutput), typeof(RunOutput), typeof(StatusOutput),
        ];
        string[] forbidden = ["secret", "password", "token", "credential"];

        foreach (Type t in dtoTypes)
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string lower = p.Name.ToLowerInvariant();
            forbidden.Should().NotContain(f => lower.Contains(f),
                because: $"{t.Name}.{p.Name} must not look like a secret");
        }
    }
}
