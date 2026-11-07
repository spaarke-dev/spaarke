using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 017 (design §8 B-2, spec FR-09): the decision slice's one result mapper. Every reason code the REST
/// routes return (task 011's inventory, <c>notes/contract-baseline.md</c>) maps to a tool error with the identical reasonCode, the
/// REST status and a message that says how to fix the call; the REST answer for the same result carries the same code and status, so
/// the two hosts share one vocabulary. The REST bytes themselves are pinned by 011's golden baselines, not here.
/// </summary>
public sealed class DecisionResultMapperTests
{
    // ── The decision route: one case per inventory row ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DecisionCommitStatus.Forbidden, 403, DecisionActionReasons.NotAuthorized)]
    [InlineData(DecisionCommitStatus.Forbidden, 403, DecisionCommitReasons.NotAuthorized)]
    [InlineData(DecisionCommitStatus.Forbidden, 403, DecisionCommitReasons.SignalSubjectMissing)]
    [InlineData(DecisionCommitStatus.Conflict, 409, DecisionCommitReasons.InquiryDispositionRequired)]
    [InlineData(DecisionCommitStatus.Conflict, 409, DecisionCommitReasons.DecidedInInvoiceReview)]
    [InlineData(DecisionCommitStatus.InProgress, 409, DecisionCommitReasons.ReviewInProgress)]
    [InlineData(DecisionCommitStatus.Conflict, 409, DecisionCommitReasons.SignalNotOpen)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.ActionsDoNotMatchPlan)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.ActionsExclude)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.AlsoResolveNotAllowed)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.CoreChanged)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.CoreUnsupported)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.DismissalDetailRequired)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.DismissalReasonRequired)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.DismissalReasonUnknown)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.DismissalWithActions)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.InvalidParameter)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.ItemOwnerUnsupported)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.NotConfirmed)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.RecordRefused)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.ReviewChanged)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.ReviewInvalid)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.UnresolvedAction)]
    [InlineData(DecisionCommitStatus.Refused, 422, DecisionCommitReasons.PlanRefused)]
    [InlineData(DecisionCommitStatus.Failed, 500, DecisionActionReasons.WriteFailed)]
    [InlineData(DecisionCommitStatus.Failed, 500, DecisionCommitReasons.NotResumable)]
    [InlineData(DecisionCommitStatus.Failed, 500, DecisionCommitReasons.RecordWriteFailed)]
    [InlineData(DecisionCommitStatus.Unavailable, 503, DecisionCommitReasons.CoreUnsupported)]
    [InlineData(DecisionCommitStatus.Unavailable, 503, DecisionCommitReasons.IdempotencyUnavailable)]
    [InlineData(DecisionCommitStatus.Unavailable, 503, DecisionCommitReasons.ItemOwnerUnsupported)]
    [InlineData(DecisionCommitStatus.Unavailable, 503, DecisionCommitReasons.RecordRefused)]
    [InlineData(DecisionCommitStatus.Unavailable, 503, DecisionCommitReasons.SecureFamilyUnavailable)]
    [InlineData(DecisionCommitStatus.Unavailable, 503, DecisionCommitReasons.SignalUnreadable)]
    public void ToToolError_CommitFailure_CarriesTheRestReasonCodeAndStatusWithAFixMessage(DecisionCommitStatus status, int httpStatus, string reasonCode)
    {
        var result = new DecisionCommitResult { Status = status, ReviewId = Guid.NewGuid(), ReasonCode = reasonCode, Detail = "detail" };

        var error = DecisionResultMapper.ToToolError(result);

        AssertKnownToolError(error, reasonCode, httpStatus);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((httpStatus, reasonCode), "REST and MCP share one vocabulary");
    }

    [Fact]
    public void ToToolError_CommitNotFound_IsTheUniformRecordUnavailable404()
    {
        var result = new DecisionCommitResult { Status = DecisionCommitStatus.NotFound, ReviewId = Guid.NewGuid() };

        AssertKnownToolError(DecisionResultMapper.ToToolError(result), ProblemDetailsHelper.RecordUnavailableReasonCode, 404);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((404, ProblemDetailsHelper.RecordUnavailableReasonCode));
    }

    [Fact]
    public void ToToolError_CommitCallerUnresolved_IsTheSingle403()
    {
        var result = new DecisionCommitResult { Status = DecisionCommitStatus.CallerUnresolved, ReviewId = Guid.NewGuid() };

        AssertKnownToolError(DecisionResultMapper.ToToolError(result), DecisionPlanEndpoints.CallerUnresolvedReasonCode, 403);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((403, DecisionPlanEndpoints.CallerUnresolvedReasonCode));
    }

    [Fact]
    public void ToToolError_DecidedInInvoiceReview_TellsTheCallerToDecideInInvoiceReview()
    {
        var result = new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Conflict,
            ReviewId = Guid.NewGuid(),
            ReasonCode = DecisionCommitReasons.DecidedInInvoiceReview,
        };

        var error = DecisionResultMapper.ToToolError(result);

        error!.Message.Should().Contain("decide it").And.Contain("Invoice Review");
    }

    [Fact]
    public void ToToolError_CommitDetailHoldsRecordText_MessageNeverRepeatsIt()
    {
        // ADR-015: a result's Detail can carry text from the records (an executor's refusal, a writer's reason); the tool message is
        // fixed text that names the reason and the fix only.
        const string recordText = "Acme Holdings v. Beta Matter 2026-0042";
        var result = new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Refused,
            ReviewId = Guid.NewGuid(),
            ReasonCode = DecisionCommitReasons.RecordRefused,
            Detail = $"This decision cannot be recorded ({recordText}); nothing was done.",
        };

        DecisionResultMapper.ToToolError(result)!.Message.Should().NotContain(recordText);
    }

    // ── The worklist route ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToToolError_WorklistCallerUnresolved_IsTheSingle403()
    {
        var result = SignalReadResult.CallerUnresolved;

        AssertKnownToolError(DecisionResultMapper.ToToolError(result), DecisionPlanEndpoints.CallerUnresolvedReasonCode, 403);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((403, DecisionPlanEndpoints.CallerUnresolvedReasonCode));
    }

    [Theory]
    [InlineData("too_many_signals")]
    [InlineData("dataverse_fault")]
    public void ToToolError_WorklistUnavailable_IsWorklistUnavailableAndHidesTheInternalReason(string internalReason)
    {
        var result = SignalReadResult.Unavailable(internalReason);

        var error = DecisionResultMapper.ToToolError(result);

        AssertKnownToolError(error, SignalReadEndpoints.WorklistUnavailableReasonCode, 503);
        error!.Message.Should().NotContain(internalReason);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((503, SignalReadEndpoints.WorklistUnavailableReasonCode));
    }

    // ── The decision-plan and suppression routes ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SignalAccessOutcome.CallerUnresolved, 403, DecisionPlanEndpoints.CallerUnresolvedReasonCode)]
    [InlineData(SignalAccessOutcome.NotFound, 404, ProblemDetailsHelper.RecordUnavailableReasonCode)]
    [InlineData(SignalAccessOutcome.Unavailable, 503, DecisionPlanEndpoints.AccessUnavailableReasonCode)]
    public void ToToolError_AccessCheckStopsTheRoute_MatchesTheRestAnswer(SignalAccessOutcome outcome, int httpStatus, string reasonCode)
    {
        AssertKnownToolError(DecisionResultMapper.ToToolError(outcome), reasonCode, httpStatus);
        Rest(DecisionResultMapper.ToHttp(outcome, Http())).Should().Be((httpStatus, reasonCode));
    }

    [Fact]
    public void ToToolError_PlanRefused_MatchesTheRestAnswer()
    {
        var (status, reasonCode) = Rest(DecisionResultMapper.PlanRefused("unknown_action_code", Http()));

        (status, reasonCode).Should().Be((422, DecisionPlanEndpoints.PlanRefusedReasonCode));
        AssertKnownToolError(DecisionResultMapper.ToToolError(reasonCode!, status), DecisionPlanEndpoints.PlanRefusedReasonCode, 422);
    }

    [Fact]
    public void ToToolError_SuppressionUnavailable_MatchesTheRestAnswer()
    {
        var (status, reasonCode) = Rest(DecisionResultMapper.SuppressionUnavailable(Http()));

        (status, reasonCode).Should().Be((503, DecisionPlanEndpoints.SuppressionUnavailableReasonCode));
        AssertKnownToolError(DecisionResultMapper.ToToolError(reasonCode!, status), DecisionPlanEndpoints.SuppressionUnavailableReasonCode, 503);
    }

    [Fact]
    public void ToToolError_SuppressionPartitionUndetermined_MatchesTheRestAnswer()
    {
        // Not in 011's baselines (needs the suppression store), but a code the suppression route returns, so it has an MCP mapping.
        var (status, reasonCode) = Rest(DecisionResultMapper.SuppressionPartitionUndetermined(Http()));

        (status, reasonCode).Should().Be((503, DecisionPlanEndpoints.PartitionUndeterminedReasonCode));
        AssertKnownToolError(DecisionResultMapper.ToToolError(reasonCode!, status), DecisionPlanEndpoints.PartitionUndeterminedReasonCode, 503);
    }

    // ── Task 013: the capability's plan and suppression results ────────────────────────────────────────────────────

    public static TheoryData<string, int, string> PlanResults => new()
    {
        { "caller-unresolved", 403, DecisionPlanEndpoints.CallerUnresolvedReasonCode },
        { "not-found", 404, ProblemDetailsHelper.RecordUnavailableReasonCode },
        { "allowed-without-lane", 404, ProblemDetailsHelper.RecordUnavailableReasonCode },
        { "access-unavailable", 503, DecisionPlanEndpoints.AccessUnavailableReasonCode },
        { "refused", 422, DecisionPlanEndpoints.PlanRefusedReasonCode },
        { "inquiry-check-unavailable", 503, DecisionPlanEndpoints.AccessUnavailableReasonCode },
        { "user-context-required", 401, Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserClientErrorCodes.UserContextRequired },
    };

    [Theory]
    [MemberData(nameof(PlanResults))]
    public void ToToolError_PlanReadFailure_MatchesTheRestAnswer(string name, int httpStatus, string reasonCode)
    {
        var result = name switch
        {
            "caller-unresolved" => DecisionPlanReadResult.NotAllowed(SignalAccessOutcome.CallerUnresolved),
            "not-found" => DecisionPlanReadResult.NotAllowed(SignalAccessOutcome.NotFound),
            "allowed-without-lane" => DecisionPlanReadResult.NotAllowed(SignalAccessOutcome.Allowed),
            "access-unavailable" => DecisionPlanReadResult.NotAllowed(SignalAccessOutcome.Unavailable),
            "refused" => DecisionPlanReadResult.Refused("unknown_action_code"),
            "inquiry-check-unavailable" => DecisionPlanReadResult.AccessUnavailable,
            _ => DecisionPlanReadResult.UserContextRequired,
        };

        AssertKnownToolError(DecisionResultMapper.ToToolError(result), reasonCode, httpStatus);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((httpStatus, reasonCode));
    }

    public static TheoryData<string, int, string> SuppressionResults => new()
    {
        { "caller-unresolved", 403, DecisionPlanEndpoints.CallerUnresolvedReasonCode },
        { "not-allowed", 404, ProblemDetailsHelper.RecordUnavailableReasonCode },
        { "access-unavailable", 503, DecisionPlanEndpoints.AccessUnavailableReasonCode },
        { "count-not-found", 404, ProblemDetailsHelper.RecordUnavailableReasonCode },
        { "partition-undetermined", 503, DecisionPlanEndpoints.PartitionUndeterminedReasonCode },
        { "unavailable", 503, DecisionPlanEndpoints.SuppressionUnavailableReasonCode },
        { "user-context-required", 401, Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserClientErrorCodes.UserContextRequired },
    };

    [Theory]
    [MemberData(nameof(SuppressionResults))]
    public void ToToolError_SuppressionReadFailure_MatchesTheRestAnswer(string name, int httpStatus, string reasonCode)
    {
        var result = name switch
        {
            "caller-unresolved" => SuppressionReadResult.NotAllowed(SignalAccessOutcome.CallerUnresolved),
            "not-allowed" => SuppressionReadResult.NotAllowed(SignalAccessOutcome.NotFound),
            "access-unavailable" => SuppressionReadResult.NotAllowed(SignalAccessOutcome.Unavailable),
            "count-not-found" => SuppressionReadResult.NotFound,
            "partition-undetermined" => SuppressionReadResult.PartitionUndetermined,
            "unavailable" => SuppressionReadResult.Unavailable,
            _ => SuppressionReadResult.UserContextRequired,
        };

        AssertKnownToolError(DecisionResultMapper.ToToolError(result), reasonCode, httpStatus);
        Rest(DecisionResultMapper.ToHttp(result, Http())).Should().Be((httpStatus, reasonCode));
    }

    [Fact]
    public void ToToolError_WorklistWithNoSignedInCaller_IsUserContextRequired_NotTheSingle403()
    {
        const string code = Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserClientErrorCodes.UserContextRequired;

        AssertKnownToolError(DecisionResultMapper.ToToolError(DecisionCapability.UserContextRequiredReadResult), code, 401);
        Rest(DecisionResultMapper.ToHttp(DecisionCapability.UserContextRequiredReadResult, Http())).Should().Be((401, code));
        Rest(DecisionResultMapper.ToHttp(SignalReadResult.CallerUnresolved, Http()))
            .Should().Be((403, DecisionPlanEndpoints.CallerUnresolvedReasonCode), "the service's own unresolved caller keeps the single 403");
    }

    [Fact]
    public void ToToolError_PlanOrSuppressionReadWithABody_IsNoError()
    {
        var plan = new DecisionPlanResponse(Guid.NewGuid(), "Decide", Guid.NewGuid(), [], [], [], null);
        var suppression = new SuppressionResponse(Guid.NewGuid(), 0, 0, 3, 30, null, true);

        DecisionResultMapper.ToToolError(DecisionPlanReadResult.Ok(plan)).Should().BeNull();
        DecisionResultMapper.ToToolError(SuppressionReadResult.Ok(suppression)).Should().BeNull();
        DecisionResultMapper.ToHttp(DecisionPlanReadResult.Ok(plan), Http()).Should().BeOfType<Ok<DecisionPlanResponse>>().Which.Value.Should().BeSameAs(plan);
        DecisionResultMapper.ToHttp(SuppressionReadResult.Ok(suppression), Http()).Should().BeOfType<Ok<SuppressionResponse>>().Which.Value.Should().BeSameAs(suppression);
    }

    // ── Negative cases ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DecisionCommitStatus.Refused, 422)]
    [InlineData(DecisionCommitStatus.Failed, 500)]
    public void ToToolError_UnknownReasonCode_PassesTheCodeThroughAsAnError(DecisionCommitStatus status, int httpStatus)
    {
        const string unknown = "ontology.decision.not_a_code_yet";
        var result = new DecisionCommitResult { Status = status, ReviewId = Guid.NewGuid(), ReasonCode = unknown };

        var error = DecisionResultMapper.ToToolError(result);

        error.Should().NotBeNull();
        error!.ReasonCode.Should().Be(unknown);
        error.Status.Should().Be(httpStatus);
        error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToToolError_CommitFailureWithoutAReasonCode_IsAnErrorNotAnException(string? missing)
    {
        // A domain failure never becomes an exception (FR-09): a failed step with no code gets the service's own step_failed.
        var result = new DecisionCommitResult { Status = DecisionCommitStatus.Failed, ReviewId = Guid.NewGuid(), ReasonCode = missing };

        AssertKnownToolError(DecisionResultMapper.ToToolError(result), DecisionCommitReasons.StepFailed, 500);
    }

    [Fact]
    public void ToToolError_RecordedOrReplayedCommit_IsNoError()
    {
        DecisionResultMapper.ToToolError(new DecisionCommitResult { Status = DecisionCommitStatus.Recorded, ReviewId = Guid.NewGuid() })
            .Should().BeNull();
        DecisionResultMapper.ToToolError(new DecisionCommitResult { Status = DecisionCommitStatus.Recorded, ReviewId = Guid.NewGuid(), Replayed = true })
            .Should().BeNull();
    }

    [Fact]
    public void ToToolError_WorklistReadWithABody_IsNoError()
    {
        var result = new SignalReadResult(SignalReadOutcome.Ok, new WorklistResponse("lanes", []));

        DecisionResultMapper.ToToolError(result).Should().BeNull();
    }

    // ── The vocabulary covers every code a route can return ───────────────────────────────────────────────────────

    [Fact]
    public void ToToolError_EveryDeclaredReasonCode_HasItsOwnFixMessage()
    {
        // Beyond 011's inventory: codes a route can return that are not baselined (gate_undeclared, step_failed and the executors'
        // own codes, which the commit route passes through unchanged). A code added to either constants class without a message fails here.
        var codes = ReasonCodesOf(typeof(DecisionCommitReasons)).Concat(ReasonCodesOf(typeof(DecisionActionReasons)))
            .Where(c => c != DecisionCommitReasons.ReplaySubjectMissingWarning) // a replay warning, never a failure code
            .ToList();

        codes.Should().NotBeEmpty();
        foreach (var code in codes)
        {
            AssertKnownToolError(DecisionResultMapper.ToToolError(code, 422), code, 422);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static void AssertKnownToolError(DecisionToolError? error, string reasonCode, int status)
    {
        error.Should().NotBeNull($"{reasonCode} is a failure");
        error!.ReasonCode.Should().Be(reasonCode);
        error.Status.Should().Be(status);
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Message.Should().NotBe(UnknownMessage(status), $"{reasonCode} needs its own fix message, not the unknown-code one");
    }

    private static string UnknownMessage(int status) => DecisionResultMapper.ToToolError("test.unknown.code", status).Message;

    private static IEnumerable<string> ReasonCodesOf(Type constants) =>
        constants.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static (int Status, string? ReasonCode) Rest(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        return (problem.Status!.Value, problem.Extensions["reasonCode"]?.ToString());
    }

    private static HttpContext Http() => new DefaultHttpContext { TraceIdentifier = "trace-017" };
}
