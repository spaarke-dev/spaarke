// CONTRACT — the MCP tool error result (spaarkeai-mcp-bff-api-r2 task 020; spec "Tools", FR-14; ADR-019). KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION McpToolResults.FromError, both overloads, with no host. The decision tools' error bytes are frozen here as
// literals (the bytes r1 shipped, before the general McpToolError existed), so the generalisation cannot move a byte of the decision
// contract. NOT HERE: the tools' own error paths over the wire (ShowDecisionsToolTests, PlanAndLookupToolTests, McpHostContractTests).

using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class McpToolResultsGeneralTests
{
    private static readonly Guid RecordId = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private static DecisionToolPartial Partial() => new(
        FailedStep: 3,
        Landed: [new DecisionCommitRef("sprk_budgetrevision", RecordId)],
        PossiblyLanded: [],
        Steps: [new DecisionToolStep("action:revise-budget", "revise-budget", 2, "failed", [], [new DecisionCommitRef("sprk_budget", RecordId)], "commit_failed")],
        RecordMayExist: false,
        SentButNotRecorded: false,
        Resumable: true,
        SendChoiceRequired: true,
        Replayed: false);

    // ---- the decision tools' bytes (frozen) ------------------------------------------------------------------------------------------

    [Fact]
    public void ADecisionError_KeepsItsBytes_ContentTextAndStructuredContent()
    {
        var result = McpToolResults.FromError(
            new DecisionToolError("ontology.decision.decided_in_invoice_review", "Decide this in Invoice Review.", 409), "0HN7ABC:00000001");

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().Be("Decide this in Invoice Review.");
        result.StructuredContent!.Value.GetRawText().Should().Be(
            "{\"reasonCode\":\"ontology.decision.decided_in_invoice_review\",\"message\":\"Decide this in Invoice Review.\",\"status\":409,"
            + "\"correlationId\":\"0HN7ABC:00000001\"}");
    }

    [Fact]
    public void ADecisionError_WithNoCorrelationId_KeepsItsBytes()
    {
        var result = McpToolResults.FromError(new DecisionToolError("ontology.signal.worklist_unavailable", "Try again shortly.", 503), null);

        result.StructuredContent!.Value.GetRawText().Should().Be(
            "{\"reasonCode\":\"ontology.signal.worklist_unavailable\",\"message\":\"Try again shortly.\",\"status\":503}");
    }

    [Fact]
    public void AFailedCommit_KeepsItsBytes_WithThePartialFacts()
    {
        var result = McpToolResults.FromError(new DecisionToolError("commit_failed", "Retry with the same review id.", 500, Partial()), "corr-9");

        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().Be("Retry with the same review id.");
        result.StructuredContent!.Value.GetRawText().Should().Be(
            "{\"reasonCode\":\"commit_failed\",\"message\":\"Retry with the same review id.\",\"status\":500,\"correlationId\":\"corr-9\","
            + "\"partial\":{\"failedStep\":3,\"landed\":[{\"entity\":\"sprk_budgetrevision\",\"id\":\"11111111-2222-4333-8444-555555555555\"}],"
            + "\"possiblyLanded\":[],\"steps\":[{\"key\":\"action:revise-budget\",\"code\":\"revise-budget\",\"phase\":2,\"status\":\"failed\","
            + "\"written\":[],\"possiblyWritten\":[{\"entity\":\"sprk_budget\",\"id\":\"11111111-2222-4333-8444-555555555555\"}],"
            + "\"reasonCode\":\"commit_failed\"}],\"recordMayExist\":false,\"sentButNotRecorded\":false,\"resumable\":true,"
            + "\"sendChoiceRequired\":true,\"replayed\":false}}");
    }

    // ---- the general error (any tool) ------------------------------------------------------------------------------------------------

    [Fact]
    public void AGeneralError_IsAnIsErrorResult_WithReasonCodeMessageStatusAndCorrelationId()
    {
        var result = McpToolResults.FromError(
            new McpToolError("finance.caller_not_internal", "Ask a firm user to read this matter's finance.", 403), "0HN7XYZ:00000002");

        result.IsError.Should().BeTrue("a domain failure is a tool result the model can read, never a transport error (ADR-019)");
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().Be("Ask a firm user to read this matter's finance.");
        var structured = result.StructuredContent!.Value;
        structured.EnumerateObject().Select(p => p.Name).Should().Equal("reasonCode", "message", "status", "correlationId");
        structured.GetProperty("reasonCode").GetString().Should().Be("finance.caller_not_internal");
        structured.GetProperty("message").GetString().Should().Be("Ask a firm user to read this matter's finance.");
        structured.GetProperty("status").GetInt32().Should().Be(403);
        structured.GetProperty("correlationId").GetString().Should().Be("0HN7XYZ:00000002");
    }

    [Fact]
    public void AGeneralError_WithNoCorrelationId_OmitsIt_AndWritesAPartialAsItsRuntimeType()
    {
        var bare = McpToolResults.FromError(new McpToolError("playbook.not_found", "Choose a playbook from the list.", 404), null);
        bare.StructuredContent!.Value.EnumerateObject().Select(p => p.Name).Should().Equal("reasonCode", "message", "status");

        var withPartial = McpToolResults.FromError(new McpToolError("task.create_failed", "Retry with the same key.", 500, new { created = 1, resumable = true }), "c-1");
        withPartial.StructuredContent!.Value.GetRawText().Should().Be(
            "{\"reasonCode\":\"task.create_failed\",\"message\":\"Retry with the same key.\",\"status\":500,\"correlationId\":\"c-1\","
            + "\"partial\":{\"created\":1,\"resumable\":true}}");
    }

    [Fact]
    public void ADecisionError_AndTheSameGeneralError_AreTheSameBytes()
    {
        var partial = Partial();
        var asDecision = McpToolResults.FromError(new DecisionToolError("commit_failed", "Retry with the same review id.", 500, partial), "corr-9");
        var asGeneral = McpToolResults.FromError(new McpToolError("commit_failed", "Retry with the same review id.", 500, partial), "corr-9");

        asGeneral.IsError.Should().Be(asDecision.IsError);
        asGeneral.StructuredContent!.Value.GetRawText().Should().Be(asDecision.StructuredContent!.Value.GetRawText());
        ((TextContentBlock)asGeneral.Content.Single()).Text.Should().Be(((TextContentBlock)asDecision.Content.Single()).Text);
    }

    [Fact]
    public void ANullError_IsAProgrammingError()
    {
        FluentActions.Invoking(() => McpToolResults.FromError((McpToolError)null!, null)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => McpToolResults.FromError((DecisionToolError)null!, null)).Should().Throw<ArgumentNullException>();
    }
}
