using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1, merge of ontology task 191 (D-222, D-223): <c>spaarke_complete_decision</c> over the REAL
/// <see cref="DecisionCommitService"/> for the resumable inquiry failure. The tool adds nothing: a stored failure returned again is the
/// REST route's 409 with <c>send_choice_required</c> and the same D-17 facts, a send choice on the same call finishes the review, and a
/// choice where none is needed is the REST route's 422. Each case compares the tool's answer with the REST route's for the same input.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static DecisionActionOutcome MaybeSentInquiry() =>
        DecisionActionOutcome.Failed("send-budget-inquiry", DecisionActionReasons.WriteFailed, "The service request was created; the email was not sent.",
            written: [new DecisionRecordRef("sprk_servicerequest", ServiceRequestId)]) with
        { SendFailure = DecisionSendFailure.Unknown };

    private static int Status(CallToolResultLike result) => result.Structured.GetProperty("status").GetInt32();

    [Fact]
    public async Task CompleteTool_AMaybeSentInquiry_RetryWithoutAChoice_Is409SendChoiceRequired_AsRestIs_AndDoesNothing()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = MaybeSentInquiry();
        var review = PathBReview();

        var first = new CallToolResultLike(await CompleteTool(rig, review, Http()));
        first.Structured.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError, "the first failure is a server fault");
        first.Structured.GetProperty("partial").GetProperty("sendChoiceRequired").GetBoolean().Should().BeTrue();
        first.Structured.GetProperty("partial").GetProperty("replayed").GetBoolean().Should().BeFalse();
        var log = rig.Log.Count;

        var again = new CallToolResultLike(await CompleteTool(rig, review with { Retry = true }, Http()));

        again.Result.IsError.Should().BeTrue();
        ErrorReason(again.Result).Should().Be(DecisionCommitReasons.SendChoiceRequired);
        Status(again).Should().Be(StatusCodes.Status409Conflict, "a stored failure returned again is never a 500");
        var partial = again.Structured.GetProperty("partial");
        partial.GetProperty("sendChoiceRequired").GetBoolean().Should().BeTrue();
        partial.GetProperty("replayed").GetBoolean().Should().BeTrue();
        partial.GetProperty("resumable").GetBoolean().Should().BeTrue();
        rig.Log.Should().HaveCount(log, "no preflight, no write, no record");
        rig.Records.Should().BeEmpty();

        // The REST route's answer for the same input: same status, reasonCode and facts.
        var restRig = new Rig();
        restRig.Executors["send-budget-inquiry"].Result = MaybeSentInquiry();
        await DecisionCommitEndpoints.CommitDecisionAsync(Primary, review, Http(), CapabilityOver(restRig), CancellationToken.None);
        var rest = await DecisionCommitEndpoints.CommitDecisionAsync(Primary, review with { Retry = true }, Http(), CapabilityOver(restRig), CancellationToken.None);
        var problem = rest.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        problem.Status.Should().Be(Status(again));
        problem.Extensions["reasonCode"].Should().Be(ErrorReason(again.Result), "one reasonCode vocabulary across both hosts");
        problem.Extensions["sendChoiceRequired"].Should().Be(partial.GetProperty("sendChoiceRequired").GetBoolean());
        problem.Extensions["replayed"].Should().Be(partial.GetProperty("replayed").GetBoolean());
        problem.Title.Should().Be("The decision was not recorded");
    }

    [Theory]
    [InlineData(DecisionReview.SendAgain, 2)]
    [InlineData(DecisionReview.RecordWithoutSending, 1)]
    public async Task CompleteTool_AMaybeSentInquiry_WithTheSendChoice_Records_AndSendsOnlyAsChosen(string choice, int sends)
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = MaybeSentInquiry();
        var review = PathBReview();
        await CompleteTool(rig, review, Http());
        rig.Executors["send-budget-inquiry"].Result = null;

        var finished = await CompleteTool(rig, review with { Retry = true, SendChoice = choice }, Http());

        finished.IsError.Should().NotBe(true);
        finished.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be((int)DecisionCommitStatus.Recorded);
        rig.Log.Count(l => l == "execute:send-budget-inquiry").Should().Be(sends);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1, "the revision that landed is never written again");
        rig.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task CompleteTool_ASendChoiceWhereNoneIsNeeded_Is422ReviewInvalid_AsRestIs_AndWritesNothing()
    {
        var rig = new Rig();

        var result = new CallToolResultLike(await CompleteTool(rig, PathBReview() with { SendChoice = DecisionReview.SendAgain }, Http()));

        ErrorReason(result.Result).Should().Be(DecisionCommitReasons.ReviewInvalid);
        Status(result).Should().Be(StatusCodes.Status422UnprocessableEntity);
        result.Structured.TryGetProperty("partial", out _).Should().BeFalse("a refusal carries no D-17 facts");
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();

        var rest = await DecisionCommitEndpoints.CommitDecisionAsync(
            Primary, PathBReview() with { SendChoice = DecisionReview.SendAgain }, Http(), CapabilityOver(new Rig()), CancellationToken.None);
        var problem = rest.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        problem.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problem.Extensions["reasonCode"].Should().Be(DecisionCommitReasons.ReviewInvalid);
    }

    /// <summary>A tool result with its structured content at hand.</summary>
    private sealed record CallToolResultLike(ModelContextProtocol.Protocol.CallToolResult Result)
    {
        public System.Text.Json.JsonElement Structured => Result.StructuredContent!.Value;
    }
}
