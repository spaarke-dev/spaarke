using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 014 (ontology's review of task 017, binding; D-17; ADR-015): a failed commit's tool error carries the
/// same partial-write facts the REST route's Failed extensions carry (<c>failedStep</c>, <c>landed</c>, <c>possiblyLanded</c>, the steps,
/// <c>recordMayExist</c>, <c>sentButNotRecorded</c>, <c>resumable</c>, <c>replayed</c>), with codes, ids and flags only, never record
/// text. And the capability's no-caller commit answer is <c>UserContextRequired</c> on both hosts while the service's own unresolved
/// caller stays the single 403.
/// </summary>
public sealed class DecisionToolErrorPartialTests
{
    private const string RecordText = "Acme Holdings v. Beta Matter 2026-0042";

    private static readonly DecisionCommitRef Revision = new("sprk_budgetrevision", Guid.Parse("01400000-0000-4000-8000-000000000001"));
    private static readonly DecisionCommitRef Budget = new("sprk_budget", Guid.Parse("01400000-0000-4000-8000-000000000002"));
    private static readonly DecisionCommitRef Message = new("sprk_communication", Guid.Parse("01400000-0000-4000-8000-000000000003"));

    /// <summary>A step-4 failure after the revision landed and a message was sent without a clear answer. Every text field holds record text.</summary>
    private static DecisionCommitResult FailedAfterWrites(bool resumable = true) => new()
    {
        Status = DecisionCommitStatus.Failed,
        ReviewId = Guid.Parse("01400000-0000-4000-8000-0000000000aa"),
        ReasonCode = DecisionCommitReasons.StepFailed,
        ActionCode = "send-budget-inquiry",
        Detail = $"The inquiry to {RecordText} could not be sent.",
        FailedStep = 4,
        Landed = [Revision, Budget],
        PossiblyLanded = [Message],
        Steps =
        [
            new("action:revise-budget", "revise-budget", 2, nameof(DecisionActionStatus.Done), [Revision, Budget], [], null, $"Revised for {RecordText}"),
            new("action:send-budget-inquiry", "send-budget-inquiry", 4, nameof(DecisionActionStatus.Failed), [], [Message],
                DecisionActionReasons.WriteFailed, $"Mail to {RecordText} timed out"),
        ],
        RecordMayExist = true,
        SentButNotRecorded = true,
        Resumable = resumable,
        Replayed = true,
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToToolError_FailedCommit_CarriesTheD17FactsWithTheValuesRestReturns(bool resumable)
    {
        var result = FailedAfterWrites(resumable);

        var partial = DecisionResultMapper.ToToolError(result)!.Partial;
        var rest = RestExtensions(DecisionResultMapper.ToHttp(result, Http()));

        partial.Should().NotBeNull("a failed commit tells the host which writes landed (D-17)");
        partial!.FailedStep.Should().Be(4).And.Be((int?)rest["failedStep"]);
        partial.Landed.Should().Equal(Revision, Budget).And.Equal((IReadOnlyList<DecisionCommitRef>)rest["landed"]!);
        partial.PossiblyLanded.Should().Equal(Message).And.Equal((IReadOnlyList<DecisionCommitRef>)rest["possiblyLanded"]!);
        partial.Resumable.Should().Be(resumable).And.Be((bool)rest["resumable"]!);
        partial.RecordMayExist.Should().BeTrue().And.Be((bool)rest["recordMayExist"]!);
        partial.SentButNotRecorded.Should().BeTrue().And.Be((bool)rest["sentButNotRecorded"]!);
        partial.Replayed.Should().BeTrue().And.Be((bool)rest["replayed"]!);

        var restSteps = (IReadOnlyList<DecisionCommitStep>)rest["steps"]!;
        partial.Steps.Select(s => (s.Key, s.Code, s.Phase, s.Status, s.ReasonCode))
            .Should().Equal(restSteps.Select(s => (s.Key, s.Code, s.Phase, s.Status, s.ReasonCode)));
        partial.Steps.Select(s => s.Written).Should().Equal(restSteps.Select(s => s.Written));
        partial.Steps.Select(s => s.PossiblyWritten).Should().Equal(restSteps.Select(s => s.PossiblyWritten));
    }

    [Fact]
    public void ToToolError_FailedCommit_PartialHoldsNoRecordText()
    {
        // ADR-015: the result's Detail and each step's Detail can carry record text; the tool error carries codes, ids and flags only.
        var error = DecisionResultMapper.ToToolError(FailedAfterWrites())!;

        var json = JsonSerializer.Serialize(error, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContain(RecordText);
        json.Should().NotContainEquivalentOf("\"detail\"", "no Detail member is carried, at the top or on a step");
        json.Should().Contain("\"landed\"").And.Contain(Revision.Id.ToString("D"), "the ids of what landed are the point");
    }

    [Theory]
    [InlineData(DecisionCommitStatus.Refused, DecisionCommitReasons.NotConfirmed)]
    [InlineData(DecisionCommitStatus.Conflict, DecisionCommitReasons.DecidedInInvoiceReview)]
    [InlineData(DecisionCommitStatus.Forbidden, DecisionCommitReasons.NotAuthorized)]
    [InlineData(DecisionCommitStatus.Unavailable, DecisionCommitReasons.IdempotencyUnavailable)]
    public void ToToolError_CommitThatWroteNothing_HasNoPartial(DecisionCommitStatus status, string reasonCode)
    {
        // REST adds the D-17 extensions only to a Failed result; a refusal wrote nothing, so the tool error has nothing to report either.
        var result = new DecisionCommitResult { Status = status, ReviewId = Guid.NewGuid(), ReasonCode = reasonCode, Landed = [Revision] };

        DecisionResultMapper.ToToolError(result)!.Partial.Should().BeNull();
        RestExtensions(DecisionResultMapper.ToHttp(result, Http())).Should().NotContainKey("landed");
    }

    [Fact]
    public void ToToolError_CapabilityCommitWithNoSignedInCaller_IsUserContextRequired_NotTheSingle403()
    {
        const string code = DataverseUserClientErrorCodes.UserContextRequired;

        var error = DecisionResultMapper.ToToolError(DecisionCapability.UserContextRequiredCommitResult)!;
        var rest = DecisionResultMapper.ToHttp(DecisionCapability.UserContextRequiredCommitResult, Http());

        (error.ReasonCode, error.Status, error.Partial).Should().Be((code, 401, (DecisionToolPartial?)null));
        Status(rest).Should().Be((401, code));
    }

    [Fact]
    public void ToToolError_ServiceCallerUnresolvedWithTheUserContextRequiredReason_StaysTheSingle403()
    {
        // Only the capability's own instance is 401: a CallerUnresolved the service builds stays the 403 it always was, whatever its reason.
        var result = DecisionCapability.UserContextRequiredCommitResult with { ReviewId = Guid.NewGuid() };

        DecisionResultMapper.ToToolError(result)!.ReasonCode.Should().Be(DecisionPlanEndpoints.CallerUnresolvedReasonCode);
        Status(DecisionResultMapper.ToHttp(result, Http())).Should().Be((403, DecisionPlanEndpoints.CallerUnresolvedReasonCode));
    }

    private static IDictionary<string, object?> RestExtensions(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Extensions;

    private static (int Status, string? ReasonCode) Status(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        return (problem.Status!.Value, problem.Extensions["reasonCode"]?.ToString());
    }

    private static HttpContext Http() => new DefaultHttpContext { TraceIdentifier = "trace-014" };
}
