using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ISS-010 (#3184) part C: the decision commit's access window (<see cref="DecisionAccessWindow"/>) on the four-action decision,
/// through the REAL commit path (<see cref="BuildFourActionWorld"/>: the real executors, the real route core over the REAL transient
/// <see cref="DataverseUserClient"/>, the shipped probe with only its wire answered). Each test runs the commit twice: as shipped,
/// and with the window removed (the request's <c>Items</c> refuse it), which is the commit exactly as it was before part C.
/// </summary>
/// <remarks>
/// <para><b>Profile and must-fire.</b> The caller-access calls the window saves, counted at the wire, and the same counts with the
/// window removed: the saving disappears.</para>
/// <para><b>Equivalence.</b> The same writes in the same order with the same payloads, and the same response, with and without the
/// window, for every scenario the part 1 equivalence tests pin.</para>
/// <para><b>Must-not-fire.</b> A record-access answer is not carried into a later step (each step re-asks its record-access
/// questions on the wire), and the window is gone when the commit returns.</para>
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The caller-access calls of one commit, split by who asked (the probe or the route core's caller client).</summary>
    private sealed record AccessCalls(
        int ProbePrincipalAccess, int RoutePrincipalAccess, int ProbePrivilegeSets, int RoutePrivilegeSets, int RecordTypeRefs,
        int MetadataReads, int Total)
    {
        public int PrincipalAccess => ProbePrincipalAccess + RoutePrincipalAccess;

        public int PrivilegeSets => ProbePrivilegeSets + RoutePrivilegeSets;

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"RetrievePrincipalAccess {PrincipalAccess} (probe {ProbePrincipalAccess}, route {RoutePrincipalAccess}); privilege sets {PrivilegeSets} "
            + $"(probe {ProbePrivilegeSets}, route {RoutePrivilegeSets}); sprk_recordtype_refs {RecordTypeRefs}; metadata {MetadataReads}; all calls {Total}");
    }

    private const string ProbeSeam = "caller access (probe)";
    private const string RouteSeam = "caller (route core, IDataverseUserClient)";

    private static AccessCalls CountAccess(DecisionCommitCallProfile profile)
    {
        var calls = profile.Calls;
        int Count(string seam, string marker) =>
            calls.Count(c => c.Seam == seam && c.Target.Contains(marker, StringComparison.Ordinal));
        return new AccessCalls(
            Count(ProbeSeam, "RetrievePrincipalAccess"),
            Count(RouteSeam, "RetrievePrincipalAccess"),
            Count(ProbeSeam, "RetrieveUserSetOfPrivilegesByNames"),
            Count(RouteSeam, "RetrieveUserSetOfPrivilegesByNames"),
            calls.Count(c => c.Seam == RouteSeam && c.Target.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal)),
            profile.MetadataReads.Count,
            calls.Count);
    }

    /// <summary>The four-action commit's caller-access calls WITHOUT the window: part 1's profile (design §1: 32 / 18 / 8).</summary>
    private static readonly AccessCalls WithoutWindow = new(17, 15, 8, 10, 8, 25, TotalWithout);

    /// <summary>The same commit WITH the window (design §5, "C as recommended": 23 / 10 / 2).</summary>
    private static readonly AccessCalls WithWindow = new(10, 13, 4, 6, 2, 25, TotalWith);

    private const int TotalWithout = 137;
    private const int TotalWith = 114;

    /// <summary>
    /// The profile: with the window, the four-action decision asks each record-access question once per step (window 0 holds
    /// FR-51, the four preflights and step 1's execute) and each privilege set and catalog row once per commit.
    /// </summary>
    [Fact]
    public async Task Iss010C_Profile_WithTheAccessWindow_EachAccessQuestionIsAskedOncePerStep()
    {
        var w = BuildFourActionWorld();

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var counted = CountAccess(w.Profile);
        WriteProfile("four-action commit WITH the access window", w.Profile);
        if (Environment.GetEnvironmentVariable("ISS010_PROFILE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, $"## access calls with the window\n{counted}\n\n");
        counted.Should().Be(WithWindow, $"measured: {counted}");
        (WithoutWindow.Total - counted.Total).Should().Be(23, "the window saves 23 calls on this decision");
    }

    /// <summary>
    /// Must-fire: remove the window (the request refuses it) and the saving disappears: the same commit asks every caller-access
    /// question on the wire, as part 1 measured.
    /// </summary>
    [Fact]
    public async Task Iss010C_MustFire_WithoutTheAccessWindow_TheSavingDisappears()
    {
        var w = BuildFourActionWorld(withoutAccessWindow: true);

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var counted = CountAccess(w.Profile);
        WriteProfile("four-action commit WITHOUT the access window", w.Profile);
        if (Environment.GetEnvironmentVariable("ISS010_PROFILE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, $"## access calls without the window\n{counted}\n\n");
        counted.Should().Be(WithoutWindow, $"measured: {counted}");
    }

    /// <summary>
    /// K4 off switch, must-fire: <c>Signals:DecisionAccessWindow:Enabled=false</c> makes the commit ask the wire every time (the profile
    /// is back at the no-window counts: 32 record-access, 18 privilege-set, 8 catalog, 137 calls), with the same writes and the same
    /// response as the window-on commit. Fails if the switch is not read (the window then saves its 23 calls).
    /// </summary>
    [Fact]
    public async Task Iss010C_OffSwitch_False_TheCommitAsksTheWireEveryTime()
    {
        var on = BuildFourActionWorld();
        var onResult = await on.CommitAsync();
        var onWrites = on.Profile.WriteTranscript();
        _appCreates.Clear();

        var w = BuildFourActionWorld(windowSwitch: "false");
        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var counted = CountAccess(w.Profile);
        counted.Should().Be(WithoutWindow, $"measured: {counted}");
        (counted.PrincipalAccess, counted.PrivilegeSets, counted.RecordTypeRefs, counted.Total).Should().Be((32, 18, 8, 137));
        w.Profile.WriteTranscript().Should().Be(onWrites, "the switch changes how often a question is asked, never what is written");
        Normalized(result).Should().Be(Normalized(onResult));
    }

    /// <summary>K4 off switch, must-not-fire: the switch set to <c>true</c> (and the other tests, where it is unset) keeps the window's saving.</summary>
    [Fact]
    public async Task Iss010C_OffSwitch_True_TheWindowServes()
    {
        var w = BuildFourActionWorld(windowSwitch: "true");

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var counted = CountAccess(w.Profile);
        counted.Should().Be(WithWindow, $"measured: {counted}");
        (WithoutWindow.Total - counted.Total).Should().Be(23);
    }

    public static TheoryData<string> EquivalenceScenarios => new()
    {
        "recorded", "replay", "refused-before", "refused-after", "fault-before", "fault-after", "metadata-fault",
    };

    /// <summary>
    /// Equivalence: with the window REMOVED, every part 1 scenario gives exactly the values the part 1 tests pin (recorded before
    /// part C); those tests (<c>Iss010_Equivalence_*</c>) now run WITH the window against the same pins. So each scenario's writes
    /// (order and payloads) and response are the same with and without the window.
    /// </summary>
    [Theory]
    [MemberData(nameof(EquivalenceScenarios))]
    public async Task Iss010C_Equivalence_WithoutTheAccessWindow_GivesThePinnedWritesAndResponse(string scenario)
    {
        var (writes, response) = await RunScenarioAsync(scenario, withoutAccessWindow: true);

        var (pinnedWrites, pinnedResponse) = scenario switch
        {
            "recorded" => (PinRecordedWrites, PinRecordedResponse),
            "replay" => (EmptyWrites, PinReplayResponse),
            "refused-before" => (EmptyWrites, PinRefusedBeforeResponse),
            "refused-after" => (PinRefusedAfterWrites, PinRefusedAfterResponse),
            "fault-before" => (EmptyWrites, PinFaultBeforeResponse),
            "fault-after" => (PinFaultAfterWrites, PinFaultAfterResponse),
            "metadata-fault" => (EmptyWrites, PinMetadataFaultResponse),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        Pinned($"{scenario} writes (no window)", writes, pinnedWrites);
        Pinned($"{scenario} response (no window)", response, pinnedResponse);
    }

    private async Task<(string Writes, string Response)> RunScenarioAsync(string scenario, bool withoutAccessWindow)
    {
        var w = BuildFourActionWorld(withoutAccessWindow: withoutAccessWindow);
        switch (scenario)
        {
            case "recorded":
                w.Rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new[] { "assignee_email_not_sent" });
                break;
            case "replay":
                (await w.CommitAsync()).Status.Should().Be(DecisionCommitStatus.Recorded);
                w.Profile.Clear();
                break;
            case "refused-before":
                w.Probe.NotHeld.Add("prvCreatesprk_WorkAssignment");
                break;
            case "refused-after":
                w.User.Held.Remove("prvCreatesprk_workassignment");
                break;
            case "fault-before":
                w.Probe.Faults.Add("prvCreatesprk_Todo");
                break;
            case "fault-after":
                w.User.PrivilegesFault = ("prvCreatesprk_workassignment", 503);
                break;
            case "metadata-fault":
                var faulted = 0;
                w.User.GetOverride = path =>
                    path.StartsWith("EntityDefinitions(LogicalName='sprk_budget')", StringComparison.Ordinal) && Interlocked.Increment(ref faulted) == 1
                        ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "Scripted fault.")
                        : null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        var result = await w.CommitAsync();
        return (w.Profile.WriteTranscript(), Normalized(result));
    }

    /// <summary>
    /// Must-not-fire: no record-access answer is carried into a later step. With the window, every step after window 0 asks its
    /// record-access questions on the wire again: the matter's AppendTo is asked by the probe once in window 0 and once in each of
    /// the three later steps, and by the route core once in each of the four creates (the work assignment's second pass, inside its
    /// own step, reuses its first).
    /// </summary>
    [Fact]
    public async Task Iss010C_MustNotFire_ARecordAccessAnswer_IsNotCarriedIntoALaterStep()
    {
        var w = BuildFourActionWorld();

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        var calls = w.Profile.Calls;
        calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrievePrincipalAccess sprk_matters")
            .Should().Be(4, "window 0 asks once, then each of the three later steps asks again after the step before it wrote");
        calls.Count(c => c.Seam == ProbeSeam && c.Target == "RetrievePrincipalAccess contacts")
            .Should().Be(4, "the assignee's AppendTo is re-asked in each later step");
        calls.Count(c => c.Seam == RouteSeam && c.Target.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                && c.Key.Contains("sprk_matters", StringComparison.Ordinal))
            .Should().Be(4, "each create's route core asks the matter's AppendTo in its own step");
    }

    /// <summary>Must-not-fire: the window ends with the commit; the request carries no window afterwards (nothing kept past the commit).</summary>
    [Fact]
    public async Task Iss010C_MustNotFire_TheWindowIsGone_WhenTheCommitReturns()
    {
        var w = BuildFourActionWorld();

        var result = await w.CommitAsync();

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        DecisionAccessWindow.Of(w.Http).Should().BeNull("the commit closed its window");
        w.Http.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty("the slot is removed on every return path");
    }
}
