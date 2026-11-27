// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 020/023; spike/specialist-agents-phase0, never merged; ADR-038: branch-only tests).
//
// WHAT THIS PROVES (spec FR-06, G5), over the production MCP route (SpikeToolsTestHost):
//   - spike_review_agreement and spike_review_legal_hold are listed read-only, model-visible, with one required closed argument;
//   - a resolved caller gets the fixed stub acknowledgement naming the function reached, and NO review runs (no Action, no model);
//   - fail closed like the other spike tools: an unresolved caller, a failed lookup or AI switched off is an error, never a result;
//   - a wrong argument is INVALID_ARGUMENT; each call writes the header-name line and one telemetry line with the tool's own name
//     (the G5 routing evidence, BR-7).

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.Spike;

public sealed class SpikeRoutingStubToolsTests
{
    private static readonly string OwnerToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.OwnerOid);
    private static readonly string NoProfileToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.NoProfileOid);
    private static readonly string UnresolvedToken = SpikeToolsTestHost.MintToken(SpikeToolsTestHost.UnresolvedOid);

    private static readonly Type[] StubTools = [typeof(SpikeReviewAgreementStubTool), typeof(SpikeReviewLegalHoldStubTool)];

    private const string Document = "Services Agreement between Northwind Fabrication and Contoso Logistics. Liability: uncapped. Payment: net 90.";

    [Fact]
    public async Task ToolsList_BothStubs_AreReadOnly_ModelVisible_WithOneRequiredClosedArgument()
    {
        await using var host = await SpikeToolsTestHost.StartAsync(tools: StubTools);

        var tools = await host.ListToolsAsync(OwnerToken);

        tools.Keys.Should().BeEquivalentTo(SpikeReviewAgreementStubTool.Name, SpikeReviewLegalHoldStubTool.Name);
        foreach (var tool in tools.Values)
        {
            tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
            tool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
            tool.TryGetProperty("_meta", out _).Should().BeFalse("a G5 function tool is model-visible");
            tool.GetProperty("description").GetString()!.Length.Should().BeLessThanOrEqualTo(1024);
            var schema = tool.GetProperty("inputSchema");
            schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("documentText");
            schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Equal("documentText");
        }

        tools[SpikeReviewAgreementStubTool.Name].GetProperty("description").GetString().Should().Contain("Not for: NDAs");
        tools[SpikeReviewLegalHoldStubTool.Name].GetProperty("description").GetString().Should().Contain("Not for: NDAs or other agreements");
    }

    [Theory]
    [InlineData(SpikeReviewAgreementStubTool.Name, "agreement-review")]
    [InlineData(SpikeReviewLegalHoldStubTool.Name, "legal-hold-review")]
    public async Task AResolvedCaller_GetsTheFixedStubAcknowledgement_AndNoReviewRuns(string tool, string function)
    {
        await using var host = await SpikeToolsTestHost.StartAsync(tools: StubTools);

        // A resolved user WITHOUT a test profile still reaches the stub: the stubs serve no profile.
        foreach (var token in new[] { OwnerToken, NoProfileToken })
        {
            var result = await host.CallAsync(tool, new { documentText = Document }, token);

            IsError(result).Should().BeFalse(result.GetRawText());
            var structured = result.GetProperty("structuredContent");
            structured.GetProperty("stub").GetString().Should().Be(SpikeRoutingStub.StubMarker);
            structured.GetProperty("function").GetString().Should().Be(function);
            structured.GetProperty("tool").GetString().Should().Be(tool);
            structured.GetProperty("documentChars").GetInt32().Should().Be(Document.Length);
            Text(result).Should().Contain($"the {function} function was reached").And.Contain("did not review the document");
            result.GetRawText().Should().NotContain("profileVersion").And.NotContain("g1-a-v1").And.NotContain("findings\":[");
        }

        host.Spike.Reviews.Should().BeEmpty("a stub runs no Action and calls no model");
        host.Spike.Resolutions.Should().Be(2, "each call resolves its caller, fail closed");
    }

    [Theory]
    [InlineData(SpikeReviewAgreementStubTool.Name, "no-matching-systemuser", "CALLER_UNRESOLVED")]
    [InlineData(SpikeReviewLegalHoldStubTool.Name, "no-matching-systemuser", "CALLER_UNRESOLVED")]
    [InlineData(SpikeReviewAgreementStubTool.Name, "lookup-failed", "CALLER_LOOKUP_FAILED")]
    [InlineData(SpikeReviewLegalHoldStubTool.Name, "lookup-failed", "CALLER_LOOKUP_FAILED")]
    public async Task AnUnresolvedCaller_FailsClosed_WithNoAcknowledgement(string tool, string resolverReason, string reasonCode)
    {
        var spike = new SpikeAiDouble { UnresolvedReason = resolverReason };
        await using var host = await SpikeToolsTestHost.StartAsync(spike, tools: StubTools);

        var result = await host.CallAsync(tool, new { documentText = Document }, UnresolvedToken);

        IsError(result).Should().BeTrue(result.GetRawText());
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(reasonCode);
        result.GetRawText().Should().NotContain(SpikeRoutingStub.StubMarker).And.NotContain("function was reached");
        CompletedLine(host, tool).State["Reason"].Should().Be(resolverReason);
        CompletedLine(host, tool).State["Outcome"].Should().Be(reasonCode);
    }

    [Theory]
    [InlineData(SpikeReviewAgreementStubTool.Name)]
    [InlineData(SpikeReviewLegalHoldStubTool.Name)]
    public async Task WithAiSwitchedOff_TheNullFacade_IsAiDisabled(string tool)
    {
        await using var host = await SpikeToolsTestHost.StartAsync(facade: new NullSpecialistProfileSpikeAi(), tools: StubTools);

        var result = await host.CallAsync(tool, new { documentText = Document }, OwnerToken);

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("AI_DISABLED");
        result.GetRawText().Should().NotContain(SpikeRoutingStub.StubMarker);
    }

    [Theory]
    [InlineData(SpikeReviewAgreementStubTool.Name, "{}")]
    [InlineData(SpikeReviewAgreementStubTool.Name, "{\"documentText\":\"  \"}")]
    [InlineData(SpikeReviewLegalHoldStubTool.Name, "{\"documentText\":\"x\",\"function\":\"nda\"}")]
    [InlineData(SpikeReviewLegalHoldStubTool.Name, "{\"documentText\":7}")]
    public async Task AWrongArgument_IsInvalidArgument_BeforeTheCallerIsLookedUp(string tool, string argumentsJson)
    {
        await using var host = await SpikeToolsTestHost.StartAsync(tools: StubTools);

        var result = await host.CallAsync(tool, JsonSerializer.Deserialize<JsonElement>(argumentsJson), OwnerToken);

        IsError(result).Should().BeTrue(result.GetRawText());
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("INVALID_ARGUMENT");
        host.Spike.Resolutions.Should().Be(0);
    }

    [Fact]
    public async Task ADocumentOverTheLimit_IsInvalidArgument()
    {
        await using var host = await SpikeToolsTestHost.StartAsync(tools: StubTools);

        var result = await host.CallAsync(
            SpikeReviewAgreementStubTool.Name, new { documentText = new string('a', SpecialistProfileSpike.MaxDocumentTextChars + 1) }, OwnerToken);

        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("INVALID_ARGUMENT");
    }

    [Theory]
    [InlineData(SpikeReviewAgreementStubTool.Name)]
    [InlineData(SpikeReviewLegalHoldStubTool.Name)]
    public async Task EachCall_WritesTheHeaderNameLine_AndOneTelemetryLine_UnderTheToolsOwnName_WithNoHeaderValue(string tool)
    {
        await using var host = await SpikeToolsTestHost.StartAsync(tools: StubTools);

        await host.CallAsync(tool, new { documentText = Document }, OwnerToken,
            new Dictionary<string, string> { ["x-spike-probe"] = "probe-value-must-not-be-logged" });

        var completed = CompletedLine(host, tool);
        completed.State["Outcome"].Should().Be("ok");
        completed.State["DocumentChars"].Should().Be(Document.Length);
        var headerLine = host.Logs.Entries.Single(e => e.Message.StartsWith($"MCP spike tool {tool} request header names", StringComparison.Ordinal));
        headerLine.Message.Should().Contain("x-spike-probe");
        foreach (var entry in host.Logs.Entries)
        {
            entry.Message.Should().NotContain("probe-value-must-not-be-logged").And.NotContain(OwnerToken);
        }
    }

    private static global::Sprk.Bff.Api.Tests.Api.Mcp.DecisionToolsTestHost.CapturingLoggerProvider.Entry CompletedLine(SpikeToolsTestHost host, string tool) =>
        host.Logs.Entries.Single(e => e.Message.StartsWith($"MCP spike tool {tool} completed:", StringComparison.Ordinal));

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;
}
