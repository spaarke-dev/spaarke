using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Membership = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// #1830 (unified-access-control-r2 task 192) — the membership service's side of "a container id that does not resolve in
/// this tenant" (Graph <c>400 invalidRequest "Invalid hostname for this tenancy"</c>), and the marked-grant removal's guard
/// fault. Pinned:
/// <list type="bullet">
/// <item><see cref="Membership.IsUnresolvableContainer"/> recognises exactly the shared rule (400 + <c>invalidRequest</c>)
/// and nothing else: another 400 code, 404, 429, 5xx and non-Graph faults stay faults.</item>
/// <item>The READS do not swallow it: <see cref="Membership.ReadMarkersAsync"/> and <see cref="Membership.ReadAccessAsync"/>
/// still answer <see langword="null"/> for 404 only, and THROW for the 400 — <c>DemoExpirationJob</c> reads null as
/// "nothing to revoke", so a null here would fail open.</item>
/// <item><see cref="Membership.RemoveMarkedGrantAsync"/>: a guard that cannot answer fails that ONE grant (Failed, nothing
/// sent to Graph); the refusal (task 227d) still propagates.</item>
/// </list>
/// </summary>
/// <remarks>Mocking boundary (ADR-038 §4): the Graph SDK's <c>IRequestAdapter</c> (<see cref="SpeContainerGraphStub"/>) and
/// the guard's virtual ownership answer (<see cref="ScriptedOwnershipGuard"/>).</remarks>
public class SpeContainerMembershipServiceUnresolvableContainerTests
{
    private const string Container = "b!unresolvable-container";

    private readonly SpeContainerGraphStub _graph = new();

    private Membership Service(SpeContainerOwnershipGuard? guard = null)
        => new(guard ?? TestSpeOwnership.AllowAll(Moq.Mock.Of<IGraphClientFactory>(f => f.ForApp() == _graph.Client)),
            NullLogger<Membership>.Instance);

    // ── The classifier ─────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, Exception, bool> Faults() => new()
    {
        { "400 invalidRequest 'Invalid hostname for this tenancy' (dev, project 65a3fab2)", SpeContainerGraphStub.InvalidHostname(), true },
        { "400 invalidRequest 'drive ID incorrectly formatted' (same shared rule)",
            SpeContainerGraphStub.Error(400, "invalidRequest", "The drive ID is incorrectly formatted or was not recognized"), true },
        { "400 invalidRequest, code in another case", SpeContainerGraphStub.Error(400, "InvalidRequest", "Invalid hostname for this tenancy"), true },
        { "400 BadRequest (a malformed query — our fault)", SpeContainerGraphStub.Error(400, "BadRequest", "Invalid hostname for this tenancy"), false },
        { "400 with no error body", new ODataError { ResponseStatusCode = 400 }, false },
        { "404 itemNotFound", SpeContainerGraphStub.Error(404, "itemNotFound", "Item not found"), false },
        { "403 accessDenied", SpeContainerGraphStub.Error(403, "accessDenied", "Access denied"), false },
        { "429 throttled", SpeContainerGraphStub.Error(429, "TooManyRequests", "Throttled"), false },
        { "503 serviceNotAvailable", SpeContainerGraphStub.Error(503, "serviceNotAvailable", "Service unavailable"), false },
        { "SpaarkeStorageException carrying 400 invalidRequest", new SpaarkeStorageException("translated", 400, "invalidRequest"), false },
        { "HttpRequestException", new HttpRequestException("transport"), false },
    };

    [Theory(DisplayName = "#1830: only a Graph 400 invalidRequest is an unresolvable container; every other fault stays a fault")]
    [MemberData(nameof(Faults))]
    public void IsUnresolvableContainer_MatchesOnlyTheSharedRule(string because, Exception fault, bool expected)
        => Membership.IsUnresolvableContainer(fault).Should().Be(expected, because);

    // ── The reads ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1830: ReadMarkersAsync — 404 is still null (the container does not exist)")]
    public async Task ReadMarkers_404_IsStillNull()
    {
        _graph.ContainerReadError = SpeContainerGraphStub.Error(404, "itemNotFound", "Item not found");

        (await Service().ReadMarkersAsync(Container)).Should().BeNull();
    }

    /// <summary>MUTATION: mapping the 400 to null inside ReadMarkersCoreAsync (every caller would read "does not exist").</summary>
    [Fact(DisplayName = "#1830: ReadMarkersAsync and ReadAccessAsync THROW the unresolvable 400 — never null, which a caller reads as 'nothing to revoke'")]
    public async Task Reads_InvalidHostname_ThrowTheClassifiedFault()
    {
        _graph.ContainerReadError = SpeContainerGraphStub.InvalidHostname();
        var service = Service();

        var markers = await FluentActions.Awaiting(() => service.ReadMarkersAsync(Container)).Should().ThrowAsync<ODataError>();
        Membership.IsUnresolvableContainer(markers.Which).Should().BeTrue();
        var access = await FluentActions.Awaiting(() => service.ReadAccessAsync(Container)).Should().ThrowAsync<ODataError>();
        Membership.IsUnresolvableContainer(access.Which).Should().BeTrue();
    }

    [Fact(DisplayName = "#1830: ReadMarkersAsync — a 5xx propagates and is not classified unresolvable")]
    public async Task ReadMarkers_5xx_Propagates()
    {
        _graph.ContainerReadError = SpeContainerGraphStub.Error(503, "serviceNotAvailable", "Service unavailable");

        var thrown = await FluentActions.Awaiting(() => Service().ReadMarkersAsync(Container)).Should().ThrowAsync<ODataError>();
        Membership.IsUnresolvableContainer(thrown.Which).Should().BeFalse();
    }

    // ── RemoveMarkedGrantAsync: the guard inside the try ───────────────────────────────────────────────────────────

    private static Membership.ContainerAccess Access(params string[] writerPermissionIds)
        => new(
            writerPermissionIds.Select(p => new Membership.ContainerUserRole(p, ["writer"], $"{p}@contoso.example", null)).ToList(),
            RolesComplete: true,
            new Dictionary<string, string>());

    /// <summary>MUTATION: the ownership check back outside the try (it threw out of the caller's loop).</summary>
    [Fact(DisplayName = "#1830: a guard that cannot answer fails that ONE removal closed (Failed, nothing sent); the next removal proceeds")]
    public async Task RemoveMarkedGrant_GuardFault_FailsThatGrantOnly()
    {
        var guard = new ScriptedOwnershipGuard(_graph.Client, (call, _) => call == 1
            ? throw new SpaarkeStorageException("ownership read of container throttled", 429)
            : true);
        var service = Service(guard);
        var access = Access("perm-a", "perm-b");

        var first = await service.RemoveMarkedGrantAsync(Container, "SprkJitA", "perm-a", access);
        _graph.Requests.Should().BeEmpty("a grant whose ownership could not be confirmed is neither deleted nor un-marked");
        var second = await service.RemoveMarkedGrantAsync(Container, "SprkJitB", "perm-b", access);

        first.Should().Be(Membership.MarkedRemovalOutcome.Failed);
        second.Should().Be(Membership.MarkedRemovalOutcome.Removed);
        _graph.Requests.Should().HaveCount(2)
            .And.Contain(r => r.StartsWith("DELETE ") && r.EndsWith("/permissions/perm-b"))
            .And.Contain(r => r.StartsWith("PATCH ") && r.EndsWith("/customProperties"));
    }

    [Fact(DisplayName = "Task 227d (kept): a container this stamp does not own is still REFUSED by RemoveMarkedGrantAsync, not reported as Failed")]
    public async Task RemoveMarkedGrant_NotOwned_StillPropagatesTheRefusal()
    {
        var service = Service(new ScriptedOwnershipGuard(_graph.Client, (_, _) => false));

        var refusal = await FluentActions.Awaiting(() => service.RemoveMarkedGrantAsync(Container, "SprkJitA", "perm-a", Access("perm-a")))
            .Should().ThrowAsync<SdapProblemException>();
        refusal.Which.Code.Should().Be(SpeContainerOwnershipGuard.NotOwnedErrorCode);
        _graph.Requests.Should().BeEmpty();
    }
}
