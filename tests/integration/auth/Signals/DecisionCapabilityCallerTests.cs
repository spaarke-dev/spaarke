using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.AccessControl.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 013 (spec FR-10 as amended 2026-11-07, option C; ADR-028; ADR-002 WP-6): the decision capability takes
/// the caller from the request and fails closed without one. With no request context or an unauthenticated principal, the list, plan and
/// suppression reads answer <c>UserContextRequired</c> before any Dataverse call, as the caller or app-only. For a Signal outside the
/// caller's core-record access the plan read answers the REST route's own reason code and writes no plan-read stamp.
/// </summary>
public sealed class DecisionCapabilityCallerTests
{
    public static TheoryData<string> NoCaller => new() { "no-context", "anonymous", "claims-without-authentication" };

    private static HttpContext? Request(string caller) => caller switch
    {
        "no-context" => null,
        "anonymous" => new DefaultHttpContext(),
        _ => new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", TenantId), new Claim("oid", CallerOid.ToString("D"))])) },
    };

    [Theory]
    [MemberData(nameof(NoCaller))]
    public async Task ListDecideAsync_WithNoSignedInCaller_ReturnsUserContextRequired_AndMakesNoDataverseCall(string caller)
    {
        var users = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var signalRead = new SignalReadService(
            users.Object, entities.Object, new Mock<IIdentityNormalizationService>(MockBehavior.Strict).Object,
            Unused<SignalSuppressionService>(), Unused<DecisionRecordWriter>(), new EarlierInvoiceReadability(NullLogger<EarlierInvoiceReadability>.Instance),
            new Mock<ICallerSystemUserResolver>(MockBehavior.Strict).Object, new Mock<ISystemUserIdentityResolver>(MockBehavior.Strict).Object,
            TimeProvider.System, NullLogger<SignalReadService>.Instance);

        var result = await Capability(signalRead: signalRead, caller: users.Object).ListDecideAsync(Request(caller)?.User, CancellationToken.None);

        AssertUserContextRequired(DecisionResultMapper.ToToolError(result), DecisionResultMapper.ToHttp(result, SignedIn()));
        users.VerifyNoOtherCalls();
        entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(NoCaller))]
    public async Task GetPlanAsync_WithNoSignedInCaller_ReturnsUserContextRequired_MakesNoDataverseCall_AndWritesNoStamp(string caller)
    {
        var (access, plans, users, entities) = DecideSignalOnAMatter();
        var cache = new Mock<ITenantCache>();
        var stamp = new DecisionPlanReadStamp(cache.Object, TimeProvider.System, NullLogger<DecisionPlanReadStamp>.Instance);

        var result = await Capability(access, plans, Describer(), caller: users.Object, planReadStamp: stamp)
            .GetPlanAsync(Request(caller), SignalId, CancellationToken.None);

        result.Outcome.Should().Be(DecisionPlanReadOutcome.UserContextRequired);
        AssertUserContextRequired(DecisionResultMapper.ToToolError(result), DecisionResultMapper.ToHttp(result, SignedIn()));
        users.VerifyNoOtherCalls();
        entities.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(NoCaller))]
    public async Task GetSuppressionAsync_WithNoSignedInCaller_ReturnsUserContextRequired_AndMakesNoDataverseCall(string caller)
    {
        // The suppression read is the second half of FR-06's plan tool (plan + suppression), so it carries the same guard.
        var (access, _, users, entities) = DecideSignalOnAMatter();

        var result = await Capability(access, suppression: Unused<SignalSuppressionService>(), caller: users.Object)
            .GetSuppressionAsync(Request(caller), SignalId, CancellationToken.None);

        result.Outcome.Should().Be(SuppressionReadOutcome.UserContextRequired);
        AssertUserContextRequired(DecisionResultMapper.ToToolError(result), DecisionResultMapper.ToHttp(result, SignedIn()));
        users.VerifyNoOtherCalls();
        entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPlanAsync_ForASignalOutsideTheCallersCoreRecordAccess_ReturnsTheRestReasonCode_AndWritesNoStamp()
    {
        var (access, plans, _, entities) = DecideSignalOnAMatter(matterReadable: false);
        var cache = new Mock<ITenantCache>();
        var stamp = new DecisionPlanReadStamp(cache.Object, TimeProvider.System, NullLogger<DecisionPlanReadStamp>.Instance);
        var capability = Capability(access, plans, Describer(), planReadStamp: stamp);

        var result = await capability.GetPlanAsync(SignedIn(), SignalId, CancellationToken.None);
        var rest = (await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, SignedIn(), capability, CancellationToken.None))
            .Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;

        result.Should().Be(DecisionPlanReadResult.NotAllowed(SignalAccessOutcome.NotFound));
        rest.Status.Should().Be(StatusCodes.Status404NotFound);
        rest.Extensions["reasonCode"].Should().Be(ProblemDetailsHelper.RecordUnavailableReasonCode);
        var tool = DecisionResultMapper.ToToolError(result);
        (tool!.ReasonCode, tool.Status).Should().Be((ProblemDetailsHelper.RecordUnavailableReasonCode, StatusCodes.Status404NotFound),
            "the MCP tool and the REST route answer one vocabulary");
        cache.VerifyNoOtherCalls();
        entities.Verify(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "the plan is never read for a Signal the caller cannot read");
    }

    private static void AssertUserContextRequired(DecisionToolError? tool, IResult rest)
    {
        tool.Should().NotBeNull();
        (tool!.ReasonCode, tool.Status).Should().Be((DataverseUserClientErrorCodes.UserContextRequired, StatusCodes.Status401Unauthorized));
        tool.Message.Should().NotBeNullOrWhiteSpace();
        var problem = rest.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        (problem.Status, problem.Extensions["reasonCode"]).Should().Be(
            (StatusCodes.Status401Unauthorized, (object?)DataverseUserClientErrorCodes.UserContextRequired), "REST and MCP share the answer");
    }
}
