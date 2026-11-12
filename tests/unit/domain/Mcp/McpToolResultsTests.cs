using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Mcp;

public sealed class McpToolResultsTests
{
    [Fact]
    public void FromError_ADecisionToolError_IsAnIsErrorResultWithTheReasonCodeAndTheMessage()
    {
        var error = new DecisionToolError("ontology.decision.decided_in_invoice_review", "Decide this in Invoice Review.", 409);

        var result = McpToolResults.FromError(error);

        result.IsError.Should().BeTrue("a domain failure is a tool result the model can read, never a transport error");
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().Be("Decide this in Invoice Review.");
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("reasonCode").GetString().Should().Be("ontology.decision.decided_in_invoice_review");
        structured.GetProperty("message").GetString().Should().Be("Decide this in Invoice Review.");
        structured.TryGetProperty("partial", out _).Should().BeFalse("only a failed commit carries the D-17 facts");
        structured.TryGetProperty("status", out _).Should().BeFalse("the HTTP status is information for the host, not part of the result");
    }

    [Fact]
    public void FromError_AFailedCommit_CarriesThePartialFactsInStructuredContent()
    {
        var recordId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var partial = new DecisionToolPartial(
            FailedStep: 3,
            Landed: [new DecisionCommitRef("sprk_budgetrevision", recordId)],
            PossiblyLanded: [],
            Steps: [],
            RecordMayExist: false,
            SentButNotRecorded: false,
            Resumable: true,
            Replayed: false);

        var result = McpToolResults.FromError(new DecisionToolError("commit_failed", "Retry with the same review id.", 500, partial));

        result.IsError.Should().BeTrue();
        var p = result.StructuredContent!.Value.GetProperty("partial");
        p.GetProperty("failedStep").GetInt32().Should().Be(3);
        p.GetProperty("resumable").GetBoolean().Should().BeTrue();
        p.GetProperty("landed")[0].GetProperty("entity").GetString().Should().Be("sprk_budgetrevision");
        p.GetProperty("landed")[0].GetProperty("id").GetGuid().Should().Be(recordId);
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>()
            .Which.Text.Should().NotContain(recordId.ToString(), "the text part carries the fix, never record ids");
    }
}
