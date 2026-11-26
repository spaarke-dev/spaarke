// CONTRACT — the FR-14 start-then-status result shapes every long-running MCP tool returns (spaarkeai-mcp-bff-api-r2 task 020; spec
// FR-14, owner Q3). KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION McpLongRunning builders, with the console link built by the PRODUCTION deep-link builder (HandoffUrlBuilder),
// with no host. NOT HERE: a tool's own start and status calls over the wire, and the run records behind them (the tool tasks 030, 032, 034).

using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class McpLongRunningTests
{
    private const string RunId = "6f0d5b8e-2c1a-4e7b-9a3d-0b1c2d3e4f50";
    private static readonly Guid AnalysisId = Guid.Parse("6f0d5b8e-2c1a-4e7b-9a3d-0b1c2d3e4f50");

    private static string BuiltLink() =>
        new HandoffUrlBuilder("https://contoso.crm.dynamics.com/").BuildAnalysisWorkspaceUrl(AnalysisId, Guid.Empty);

    private static string Text(CallToolResult result) =>
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>().Subject.Text;

    [Fact]
    public void TheBudget_IsThirtySeconds()
    {
        McpLongRunning.Budget.Should().Be(TimeSpan.FromSeconds(30));
        McpLongRunning.BudgetSeconds.Should().Be(30);
    }

    // ---- start ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(McpRunState.Queued, "queued")]
    [InlineData(McpRunState.Running, "running")]
    public void Started_IsRunIdStatusAndTheBuiltConsoleLink_WithTheOneLineSummary(McpRunState state, string wire)
    {
        var link = BuiltLink();

        var result = McpLongRunning.Started(RunId, state, link, "Started the contract review. Ask for its status to see the result.");

        result.IsError.Should().NotBe(true);
        Text(result).Should().Be("Started the contract review. Ask for its status to see the result.");
        var body = result.StructuredContent!.Value;
        body.EnumerateObject().Select(p => p.Name).Should().Equal("runId", "status", "consoleLink");
        body.GetProperty("runId").GetString().Should().Be(RunId);
        body.GetProperty("status").GetString().Should().Be(wire);
        body.GetProperty("consoleLink").GetString().Should().Be(link, "the link is the deep-link builder's, passed through unchanged")
            .And.StartWith("https://contoso.crm.dynamics.com/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai&data=");
    }

    [Theory]
    [InlineData("/main.aspx?pagetype=webresource")]
    [InlineData("http://contoso.crm.dynamics.com/main.aspx")]
    [InlineData("https://user:secret@contoso.crm.dynamics.com/main.aspx")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void Started_RefusesALinkThatIsNotAnAbsoluteHttpsUrlWithoutUserInfo(string link)
    {
        FluentActions.Invoking(() => McpLongRunning.Started(RunId, McpRunState.Queued, link, "Started."))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("consoleLink");
    }

    [Theory]
    [InlineData(McpRunState.Completed)]
    [InlineData(McpRunState.Failed)]
    public void Started_RefusesAFinishedState(McpRunState state)
    {
        FluentActions.Invoking(() => McpLongRunning.Started(RunId, state, BuiltLink(), "Started."))
            .Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("status");
    }

    [Fact]
    public void Started_RefusesABlankRunId_AndABlankOrMultiLineSummary()
    {
        FluentActions.Invoking(() => McpLongRunning.Started(" ", McpRunState.Queued, BuiltLink(), "Started.")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => McpLongRunning.Started(RunId, McpRunState.Queued, BuiltLink(), " ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => McpLongRunning.Started(RunId, McpRunState.Queued, BuiltLink(), "Started.\nSecond line."))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("summary");
    }

    // ---- status --------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(McpRunState.Queued, "queued", "The run is queued. Call the status tool again shortly.")]
    [InlineData(McpRunState.Running, "running", "The run is running. Call the status tool again shortly.")]
    [InlineData(McpRunState.Completed, "completed", "The run has completed.")]
    [InlineData(McpRunState.Failed, "failed", "The run has failed.")]
    public void Status_EachState_IsRunIdAndStatusOnly_WithAFixedLine_AndIsNotAnError(McpRunState state, string wire, string line)
    {
        var result = McpLongRunning.Status(RunId, state);

        result.IsError.Should().NotBe(true, "a failed run is a successful status call that reports the state");
        Text(result).Should().Be(line);
        result.StructuredContent!.Value.GetRawText().Should().Be($"{{\"runId\":\"{RunId}\",\"status\":\"{wire}\"}}");
    }

    [Fact]
    public void Status_Running_CarriesProgress()
    {
        var result = McpLongRunning.Status(RunId, McpRunState.Running, progress: 40);

        Text(result).Should().Be("The run is running (40% done). Call the status tool again shortly.");
        result.StructuredContent!.Value.GetRawText().Should().Be($"{{\"runId\":\"{RunId}\",\"status\":\"running\",\"progress\":40}}");
    }

    [Fact]
    public void Status_Completed_CarriesTheResultAsItsRuntimeType_AndTheGivenSummary()
    {
        var result = McpLongRunning.Status(
            RunId, McpRunState.Completed, progress: 100, result: new { summary = "Two findings.", findings = 2 }, summary: "The review found two issues.");

        Text(result).Should().Be("The review found two issues.");
        result.StructuredContent!.Value.GetRawText().Should().Be(
            $"{{\"runId\":\"{RunId}\",\"status\":\"completed\",\"progress\":100,\"result\":{{\"summary\":\"Two findings.\",\"findings\":2}}}}");
    }

    [Fact]
    public void Status_Failed_MayCarryAResult()
    {
        var result = McpLongRunning.Status(RunId, McpRunState.Failed, result: new { reasonCode = "playbook.node_failed" });

        result.IsError.Should().NotBe(true);
        result.StructuredContent!.Value.GetProperty("result").GetProperty("reasonCode").GetString().Should().Be("playbook.node_failed");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Status_RefusesProgressOutsideZeroToOneHundred(int progress)
    {
        FluentActions.Invoking(() => McpLongRunning.Status(RunId, McpRunState.Running, progress))
            .Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("progress");
    }

    [Fact]
    public void Status_RefusesABlankRunId_AndAMultiLineSummary()
    {
        FluentActions.Invoking(() => McpLongRunning.Status("", McpRunState.Queued)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => McpLongRunning.Status(RunId, McpRunState.Completed, summary: "a\r\nb"))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("summary");
    }
}
