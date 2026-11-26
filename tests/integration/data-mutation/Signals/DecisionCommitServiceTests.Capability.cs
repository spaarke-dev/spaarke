using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 014 (design §8 B-1; spec FR-08, FR-10 as amended 2026-11-07, option C; ontology condition 2): the
/// decision capability's commit, the one entry REST and MCP share, over the REAL <see cref="DecisionCommitService"/> in this file's rig.
/// With no signed-in caller it answers <c>UserContextRequired</c> before the service runs: nothing read, locked or written and the
/// review ledger untouched. With a caller it is the service's own outcome: a billing Signal is the D-207 refusal before the rights
/// probe and any write (no second billing check), a missing plan read is the service's existing refusal, and WhoAmI is asked once.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    public static TheoryData<string> NoSignedInCaller => new() { "no-context", "anonymous", "claims-without-authentication" };

    private static HttpContext? CallerlessRequest(string caller) => caller switch
    {
        "no-context" => null,
        "anonymous" => new DefaultHttpContext(),
        // The claims the service reads (tid, oid) and a bearer token, but no authenticated identity: still no caller.
        _ => WithoutAuthentication(),
    };

    private static HttpContext WithoutAuthentication()
    {
        var http = Http();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", Tenant), new Claim("oid", CallerOid.ToString("D"))]));
        return http;
    }

    /// <summary>The capability as the routes and tools get it, over this rig's real commit service.</summary>
    private static IDecisionCapability CapabilityOver(Rig rig) => DecisionCapabilityFixtures.Capability(commits: rig.Service());

    [Theory]
    [MemberData(nameof(NoSignedInCaller))]
    public async Task Capability_CommitAsync_WithNoSignedInCaller_ReturnsUserContextRequired_WritesNothing_AndLeavesTheLedgerUntouched(string caller)
    {
        var rig = new Rig();
        var ledger = new TrackingTenantCache();
        rig.LedgerStore = ledger;
        var lease = new Mock<IScheduledJobLease>(MockBehavior.Strict);
        rig.Lease = lease.Object;

        var result = await CapabilityOver(rig).CommitAsync(CallerlessRequest(caller), Primary, PathBReview(), CancellationToken.None);

        result.Should().BeSameAs(DecisionCapability.UserContextRequiredCommitResult);
        var error = DecisionResultMapper.ToToolError(result)!;
        (error.ReasonCode, error.Status).Should().Be((DataverseUserClientErrorCodes.UserContextRequired, StatusCodes.Status401Unauthorized));
        rig.User.Invocations.Should().BeEmpty("nothing is read as the caller");
        rig.Org.Invocations.Should().BeEmpty("nothing is read or written app-only");
        rig.Probe.Invocations.Should().BeEmpty("the caller-rights probe is never asked");
        lease.Invocations.Should().BeEmpty("no lock is taken");
        (ledger.GetCount, ledger.SetCount, ledger.RemoveCount).Should().Be((0, 0, 0), "the review ledger is not touched");
        rig.Log.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
    }

    [Theory]
    [InlineData("sprk_billingevent")]
    [InlineData("sprk_invoice")]
    public async Task Capability_CommitAsync_OnABillingSignal_Is409DecidedInInvoiceReview_BeforeTheRightsProbeAndAnyWrite(string subject)
    {
        // Ontology condition 2: the shared entry reaches the service's own (1c-billing) step; the capability adds no billing check.
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: subject, subjectId: BillingSubjectId);

        var result = await CapabilityOver(rig).CommitAsync(Http(), Primary, PathBReview(inquiry: false, todo: false), CancellationToken.None);

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "an invoice or line is decided in Invoice Review");
        var error = DecisionResultMapper.ToToolError(result)!;
        (error.ReasonCode, error.Status, error.Partial).Should().Be(
            (DecisionCommitReasons.DecidedInInvoiceReview, StatusCodes.Status409Conflict, (DecisionToolPartial?)null));
    }

    [Fact]
    public async Task Capability_CommitAsync_WithNoPriorPlanRead_OnASignalTheSystemClosed_IsTheServicesSignalNotOpenRefusal()
    {
        // The service's existing answer to a missing plan read: with no stamp the review starts now, so a closure the system made
        // before it is a conflict and nothing is written. (A stamp from before the closure re-closes instead: next test.)
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));

        var result = await CapabilityOver(rig).CommitAsync(Http(), Primary, PathBReview(), CancellationToken.None);

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalNotOpen);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Capability_CommitAsync_AfterAPlanReadThatPrecededTheSystemClosure_IsRecorded()
    {
        // The positive control for the test above: the same Signal, read through the plan before the system closed it.
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));
        await rig.StampPlanRead(Primary, Now.AddMinutes(-10));

        var result = await CapabilityOver(rig).CommitAsync(Http(), Primary, PathBReview(), CancellationToken.None);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task Capability_CommitAsync_AsksWhoAmIOnce()
    {
        // T170 through the shared entry (FR-10: WhoAmI stays at one per commit). The capability passes the request through, so the
        // service's one-question memo is unchanged.
        var rig = new Rig();
        var probe = new WireCountingProbe(Caller);
        rig.ProbeOverride = probe;
        var http = Http();
        new HttpContextAccessor().HttpContext = http;

        var result = await CapabilityOver(rig).CommitAsync(http, Primary, PathBReview(), CancellationToken.None);

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        probe.WhoAmIs.Should().Be(1);
        probe.RightsAsked.Should().BeGreaterThan(0).And.Be(probe.RightsQuestions);
    }
}
