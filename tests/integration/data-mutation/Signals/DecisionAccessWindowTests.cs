using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ISS-010 (#3184) part C: <see cref="DecisionAccessWindow"/> at its boundaries: the probe's questions (a Moq probe, counted) and
/// the route core's caller client (the REAL <see cref="DataverseUserClient"/> over a counting wire, <see cref="RealCallerClientRig"/>).
/// </summary>
/// <remarks>
/// <para><b>Kept.</b> A record-access answer (a mask or a 403) within one step; a Held/NotHeld privilege and a successful privilege-set or
/// catalog read for the whole window. <b>Never kept.</b> An unknown rights answer, a Faulted privilege, a failed GET, a row read, an
/// answer across requests or callers, a record-access answer across steps, anything after the window closes, and anything for a
/// client that is not the <see cref="DataverseUserClient"/>.</para>
/// </remarks>
public sealed class DecisionAccessWindowTests
{
    private static readonly Guid CallerA = Guid.Parse("c0430000-0000-4000-8000-00000000a001");
    private static readonly Guid CallerB = Guid.Parse("c0430000-0000-4000-8000-00000000b002");
    private static readonly Guid Matter = Guid.Parse("c0430000-0000-4000-8000-0000000000e1");
    private const string Privilege = "prvCreatesprk_Todo";

    private static readonly string RightsPath =
        $"systemusers({CallerA:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1=%7B%22%40odata.id%22%3A%22sprk_matters({Matter:D})%22%7D";

    private static readonly string PrivilegesPath =
        $"systemusers({CallerA:D})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1=%5B%22prvCreatesprk_todo%22%5D";

    private const string CatalogPath = "sprk_recordtype_refs(c0430000-0000-4000-8000-0000000000f1)?$select=sprk_recordlogicalname";
    private const string RowPath = "sprk_matters(c0430000-0000-4000-8000-0000000000e1)?$select=sprk_matterid";

    private readonly RealCallerClientRig _rig = RealCallerClientRig.Create();
    private readonly CountingWire _wire = new();
    private readonly Mock<CallerRecordAccessProbe> _probe = new(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!)
    { CallBase = false };

    private AccessRights? _rights = AccessRights.AppendTo;
    private CallerPrivilegeAnswer _privilege = CallerPrivilegeAnswer.Held;

    public DecisionAccessWindowTests()
    {
        _probe.Setup(p => p.GetCallerRightsForRecordsOrUnknownAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyList<(string EntitySet, Guid RecordId)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<AccessRights?>)new[] { _rights });
        _probe.Setup(p => p.CallerPrivilegeAnswerAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _privilege);
    }

    /// <summary>The caller's Dataverse wire, answered in memory and counted per Web API path.</summary>
    private sealed class CountingWire : HttpMessageHandler
    {
        public ConcurrentDictionary<string, int> Gets { get; } = new(StringComparer.Ordinal);

        public int Writes;

        public Func<string, HttpStatusCode?>? Fault { get; set; }

        /// <summary>Run 3: a GET of a path this answers with an exception THROWS it (a transport fault or a timeout).</summary>
        public Func<string, Exception?>? Throw { get; set; }

        public int Count(string path) => Gets.GetValueOrDefault(path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get)
            {
                Interlocked.Increment(ref Writes);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            var original = request.RequestUri!.OriginalString;
            var path = original[(original.IndexOf("/api/data/v9.2/", StringComparison.Ordinal) + "/api/data/v9.2/".Length)..];
            Gets.AddOrUpdate(path, 1, (_, n) => n + 1);
            await Task.Yield();
            if (Throw?.Invoke(path) is { } thrown)
                throw thrown;
            return Fault?.Invoke(path) is { } status
                ? new HttpResponseMessage(status)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { AccessRights = "AppendToAccess" })) };
        }
    }

    /// <summary>A request with the rig's bearer token and the caller's object id, made the ambient request.</summary>
    private static DefaultHttpContext Request(Guid oid)
    {
        var http = RealCallerClientRig.Request();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid.ToString("D"))], "test"));
        new HttpContextAccessor().HttpContext = http;
        return http;
    }

    private IDecisionActionExecutor ChildCreate() =>
        new AddTodoExecutor(_probe.Object, new DecisionRouteCores(), Mock.Of<IGenericEntityService>());

    private int RightsAsks() => _probe.Invocations.Count(i => i.Method.Name == nameof(CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync));

    private int PrivilegeAsks() => _probe.Invocations.Count(i => i.Method.Name == nameof(CallerRecordAccessProbe.CallerPrivilegeAnswerAsync));

    // ── The slot ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Of_IsTheOpenWindowOfItsOwnRequestOnly_AndNothingOnceClosed()
    {
        var http = Request(CallerA);
        var other = Request(CallerA);
        DecisionAccessWindow.Of(http).Should().BeNull("no commit has opened a window");

        var window = DecisionAccessWindow.Open(http, CallerA);

        DecisionAccessWindow.Of(http).Should().BeSameAs(window);
        DecisionAccessWindow.Of(other).Should().BeNull("another request shares nothing");
        window.Dispose();
        DecisionAccessWindow.Of(http).Should().BeNull();
        http.Items.Values.OfType<DecisionAccessWindow>().Should().BeEmpty("closing removes the slot");
        window.Dispose(); // idempotent
    }

    [Fact]
    public void Open_WithNoProvenCaller_KeepsNothing()
    {
        var http = Request(CallerA);

        using var window = DecisionAccessWindow.Open(http, Guid.Empty);

        DecisionAccessWindow.Of(http).Should().BeNull();
    }

    [Fact]
    public void BeginStep_SuspendsTheWindow_ForAnyExecutorButAChildCreate()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        window.BeginStep(Mock.Of<IDecisionActionExecutor>());
        DecisionAccessWindow.Of(http).Should().BeNull("a non-child executor's execute runs with the window suspended");
        window.EndStep();
        DecisionAccessWindow.Of(http).Should().BeSameAs(window);

        window.BeginStep(ChildCreate());
        DecisionAccessWindow.Of(http).Should().BeSameAs(window, "a child create asks everything before its one write");
    }

    // ── The probe's questions ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARightsAnswer_IsKeptWithinAStep_AndAskedAgainAfterIt()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        (await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default)).Should().Be(AccessRights.AppendTo);
        (await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default)).Should().Be(AccessRights.AppendTo);
        RightsAsks().Should().Be(1);

        window.EndStep();
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);
        RightsAsks().Should().Be(2, "a record-access answer is never carried into a later step");
    }

    [Fact]
    public async Task A403_IsKeptOnlyWithinItsStep()
    {
        _rights = AccessRights.None;
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        (await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default)).Should().Be(AccessRights.None);
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);
        RightsAsks().Should().Be(1, "Dataverse's own denial is an answer");

        window.EndStep();
        _rights = AccessRights.AppendTo; // granted before the next step
        (await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default)).Should().Be(AccessRights.AppendTo);
        RightsAsks().Should().Be(2);
    }

    [Fact]
    public async Task AnUnknownRightsAnswer_IsNeverKept()
    {
        _rights = null;
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        (await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default)).Should().BeNull();
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);

        RightsAsks().Should().Be(2, "a fault (unknown) is asked again, never kept");
    }

    [Fact]
    public async Task ARightsAnswer_IsNotSharedAcrossCallers()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);

        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", CallerB.ToString("D"))], "test"));
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);

        RightsAsks().Should().Be(2, "each answer is keyed on the caller who asked");
    }

    [Fact]
    public async Task APrivilegeAnswer_IsKeptForTheWholeWindow_ButAFaultIsNot()
    {
        var http = Request(CallerA);
        var window = DecisionAccessWindow.Open(http, CallerA);

        (await window.CallerPrivilegeAsync(_probe.Object, http, Privilege, default)).Should().Be(CallerPrivilegeAnswer.Held);
        window.EndStep();
        (await window.CallerPrivilegeAsync(_probe.Object, http, Privilege, default)).Should().Be(CallerPrivilegeAnswer.Held);
        PrivilegeAsks().Should().Be(1, "privileges are kept for the commit (no role or team edit on its path, A2)");

        _privilege = CallerPrivilegeAnswer.Faulted;
        (await window.CallerPrivilegeAsync(_probe.Object, http, "prvCreatesprk_Budget", default)).Should().Be(CallerPrivilegeAnswer.Faulted);
        await window.CallerPrivilegeAsync(_probe.Object, http, "prvCreatesprk_Budget", default);
        PrivilegeAsks().Should().Be(3, "a Faulted answer is asked again");

        window.Close();
        _privilege = CallerPrivilegeAnswer.NotHeld;
        (await window.CallerPrivilegeAsync(_probe.Object, http, Privilege, default)).Should().Be(CallerPrivilegeAnswer.NotHeld);
        PrivilegeAsks().Should().Be(4, "nothing is kept once the window closes");
    }

    // ── The route core's client ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CallerClient_WrapsOnlyTheDataverseUserClient()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        var other = Mock.Of<IDataverseUserClient>();

        window.CallerClient(other).Should().BeSameAs(other, "any other client gets no kept answer");
        window.CallerClient(_rig.NewClient(_wire)).Should().BeOfType<DecisionAccessWindowCallerClient>();
    }

    [Fact]
    public async Task TheRouteCoresRightsRead_IsKeptAcrossClientsWithinAStep_AndAskedAgainAfterIt()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        (await window.CallerClient(_rig.NewClient(_wire)).GetAsync(RightsPath, default)).IsSuccess.Should().BeTrue();
        var kept = await window.CallerClient(_rig.NewClient(_wire)).GetAsync(RightsPath, default); // production's next transient client
        _wire.Count(RightsPath).Should().Be(1, "the window is per request, not per client instance");
        kept.Body!.Value.GetProperty("AccessRights").GetString().Should().Be("AppendToAccess");

        window.EndStep();
        await window.CallerClient(_rig.NewClient(_wire)).GetAsync(RightsPath, default);
        _wire.Count(RightsPath).Should().Be(2, "a record-access answer is never carried into a later step");
    }

    [Fact]
    public async Task ThePrivilegeAndCatalogReads_AreKeptAcrossSteps_AndARowReadIsNeverKept()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        var client = window.CallerClient(_rig.NewClient(_wire));

        foreach (var path in new[] { PrivilegesPath, CatalogPath, RowPath })
            await client.GetAsync(path, default);
        window.EndStep();
        foreach (var path in new[] { PrivilegesPath, CatalogPath, RowPath })
            await window.CallerClient(_rig.NewClient(_wire)).GetAsync(path, default);

        _wire.Count(PrivilegesPath).Should().Be(1);
        _wire.Count(CatalogPath).Should().Be(1);
        _wire.Count(RowPath).Should().Be(2, "a record read is not an access answer the window keeps");
    }

    [Fact]
    public async Task AFailedRead_IsNeverKept()
    {
        var faults = 1;
        _wire.Fault = path => faults-- > 0 ? HttpStatusCode.ServiceUnavailable : null;
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        var client = window.CallerClient(_rig.NewClient(_wire));

        (await client.GetAsync(PrivilegesPath, default)).IsSuccess.Should().BeFalse();
        (await client.GetAsync(PrivilegesPath, default)).IsSuccess.Should().BeTrue();

        _wire.Count(PrivilegesPath).Should().Be(2, "a failure is asked again");
    }

    [Fact]
    public async Task AWriteThroughTheClient_ForgetsTheRecordAccessAnswers()
    {
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        var client = window.CallerClient(_rig.NewClient(_wire));
        await client.GetAsync(RightsPath, default);
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);

        await client.PatchAsync("sprk_todos(c0430000-0000-4000-8000-0000000000d1)", "{}", default);
        await client.GetAsync(RightsPath, default);
        await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);

        _wire.Count(RightsPath).Should().Be(2);
        RightsAsks().Should().Be(2);
    }

    [Fact]
    public async Task TwoRequests_ShareNothing()
    {
        var first = Request(CallerA);
        using (var window = DecisionAccessWindow.Open(first, CallerA))
            await window.CallerClient(_rig.NewClient(_wire)).GetAsync(PrivilegesPath, default);

        var second = Request(CallerA);
        using (var window = DecisionAccessWindow.Open(second, CallerA))
            await window.CallerClient(_rig.NewClient(_wire)).GetAsync(PrivilegesPath, default);

        _wire.Count(PrivilegesPath).Should().Be(2);
    }

    [Fact]
    public async Task ASuspendedOrClosedWindow_ServesAndKeepsNothing()
    {
        var http = Request(CallerA);
        var window = DecisionAccessWindow.Open(http, CallerA);
        var client = window.CallerClient(_rig.NewClient(_wire));

        window.BeginStep(Mock.Of<IDecisionActionExecutor>());
        await client.GetAsync(PrivilegesPath, default);
        window.EndStep();
        await client.GetAsync(PrivilegesPath, default);
        _wire.Count(PrivilegesPath).Should().Be(2, "nothing was kept while suspended");

        window.Close();
        await client.GetAsync(PrivilegesPath, default);
        _wire.Count(PrivilegesPath).Should().Be(3, "nothing is served once closed");
    }

    /// <summary>Must-fire for the gate in <c>CallerMetadataReadMemo.For</c>: the window's view still shares the request's metadata answers.</summary>
    [Fact]
    public async Task TheRequestMetadataStore_StillSharesThroughTheWindowsView()
    {
        const string metadata = "EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName";
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        var scope = CallerMetadataReadMemo.RequestScope.Of(http);

        await CallerMetadataReadMemo.For(window.CallerClient(_rig.NewClient(_wire)), scope).GetAsync(metadata, default);
        await CallerMetadataReadMemo.For(window.CallerClient(_rig.NewClient(_wire)), scope).GetAsync(metadata, default);

        _wire.Count(metadata).Should().Be(1);
    }

    // ── Run 3: faults that throw are never kept ───────────────────────────────────────────────────────────────────────

    /// <summary>Must-fire: a probe rights question that THROWS keeps nothing; the next ask goes to the probe (fails if a fault is
    /// stored, e.g. as a 403, before or instead of an answer).</summary>
    [Fact]
    public async Task ARightsQuestionThatThrows_IsNeverKept()
    {
        var throws = 1;
        _probe.Setup(p => p.GetCallerRightsForRecordsOrUnknownAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyList<(string EntitySet, Guid RecordId)>>(), It.IsAny<CancellationToken>()))
            .Returns(() => throws-- > 0
                ? Task.FromException<IReadOnlyList<AccessRights?>>(new TimeoutException("no answer"))
                : Task.FromResult<IReadOnlyList<AccessRights?>>(new AccessRights?[] { AccessRights.AppendTo }));
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        var first = () => window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);
        await first.Should().ThrowAsync<TimeoutException>();
        (await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default)).Should().Be(AccessRights.AppendTo);

        RightsAsks().Should().Be(2, "a thrown question is asked again");
    }

    /// <summary>Must-fire: a privilege question that THROWS keeps nothing; the next ask goes to the probe.</summary>
    [Fact]
    public async Task APrivilegeQuestionThatThrows_IsNeverKept()
    {
        var throws = 1;
        _probe.Setup(p => p.CallerPrivilegeAnswerAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => throws-- > 0
                ? Task.FromException<CallerPrivilegeAnswer>(new TimeoutException("no answer"))
                : Task.FromResult(CallerPrivilegeAnswer.NotHeld));
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);

        var first = () => window.CallerPrivilegeAsync(_probe.Object, http, Privilege, default);
        await first.Should().ThrowAsync<TimeoutException>();
        (await window.CallerPrivilegeAsync(_probe.Object, http, Privilege, default)).Should().Be(CallerPrivilegeAnswer.NotHeld);

        PrivilegeAsks().Should().Be(2, "a thrown question is asked again");
    }

    public static TheoryData<string> RouteKeptPaths => new() { RightsPath, PrivilegesPath, CatalogPath };

    /// <summary>Must-fire, every shape the route core's view keeps: a GET whose wire THROWS (a timeout) keeps nothing, and neither does
    /// a transport failure (the client's 0 status); the next ask goes to the wire.</summary>
    [Theory]
    [MemberData(nameof(RouteKeptPaths))]
    public async Task ARouteReadThatThrowsOrFailsInTransport_IsNeverKept(string path)
    {
        var faults = 0;
        _wire.Throw = asked => asked == path ? (faults++) switch
        {
            0 => new TimeoutException("no answer"),
            1 => new HttpRequestException("connection reset"),
            _ => null,
        } : null;
        var http = Request(CallerA);
        using var window = DecisionAccessWindow.Open(http, CallerA);
        var client = window.CallerClient(_rig.NewClient(_wire));

        var thrown = () => client.GetAsync(path, default);
        await thrown.Should().ThrowAsync<TimeoutException>();
        (await client.GetAsync(path, default)).IsSuccess.Should().BeFalse("the client answers a transport failure as a failure");
        (await client.GetAsync(path, default)).IsSuccess.Should().BeTrue();
        (await client.GetAsync(path, default)).IsSuccess.Should().BeTrue();

        _wire.Count(path).Should().Be(3, "the throw and the transport failure are asked again; only the success is kept");
    }

    // ── Run 3: scope ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two requests with windows open AT THE SAME TIME share nothing: both ask the same caller's same questions, interleaved on one
    /// wire; each request's first ask reaches the wire and its repeat is answered from its own window; one request's step end does
    /// not touch the other's answers. Fails if any store is shared across windows (e.g. made static).
    /// </summary>
    [Fact]
    public async Task TwoConcurrentRequests_ShareNothing()
    {
        var first = Request(CallerA);
        var second = Request(CallerA);
        using var one = DecisionAccessWindow.Open(first, CallerA);
        using var two = DecisionAccessWindow.Open(second, CallerA);

        // Both windows open: the second request's first asks are not answered by the first request's.
        foreach (var window in new[] { one, two })
        {
            await window.CallerClient(_rig.NewClient(_wire)).GetAsync(RightsPath, default);
            await window.CallerClient(_rig.NewClient(_wire)).GetAsync(PrivilegesPath, default);
        }

        await one.CallerRightsAsync(_probe.Object, first, "sprk_matters", Matter, default);
        await two.CallerRightsAsync(_probe.Object, second, "sprk_matters", Matter, default);
        _wire.Count(RightsPath).Should().Be(2, "each request asked its own record-access question on the wire");
        _wire.Count(PrivilegesPath).Should().Be(2, "each request asked its own privilege question on the wire");
        RightsAsks().Should().Be(2, "each request asked the probe itself");

        // Then both repeat, at the same time: each is answered from its own window.
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            var (window, http) = i % 2 == 0 ? (one, first) : (two, second);
            await window.CallerClient(_rig.NewClient(_wire)).GetAsync(i % 4 < 2 ? RightsPath : PrivilegesPath, default);
            await window.CallerRightsAsync(_probe.Object, http, "sprk_matters", Matter, default);
        })));
        _wire.Count(RightsPath).Should().Be(2);
        _wire.Count(PrivilegesPath).Should().Be(2);
        RightsAsks().Should().Be(2);

        // One request's step ends: only its own record-access answers go.
        one.EndStep();
        await two.CallerClient(_rig.NewClient(_wire)).GetAsync(RightsPath, default);
        await two.CallerRightsAsync(_probe.Object, second, "sprk_matters", Matter, default);
        _wire.Count(RightsPath).Should().Be(2, "the second request still holds its own answer");
        RightsAsks().Should().Be(2);
        await one.CallerClient(_rig.NewClient(_wire)).GetAsync(RightsPath, default);
        await one.CallerRightsAsync(_probe.Object, first, "sprk_matters", Matter, default);
        _wire.Count(RightsPath).Should().Be(3, "the first request's step ended, so it asks again");
        RightsAsks().Should().Be(3);
    }

    /// <summary>
    /// A request with no window (a REST create, the chat tool, a job or the reconciler with no request at all, a follow-up after the
    /// commit closed its window) asks exactly as before: <see cref="DecisionExec"/> calls the probe's plain questions every time, and the
    /// window's forms are never asked. The same two questions on a request WITH a window go through the window (one ask), so the test
    /// fails if the executors stop consulting the window, and if they consult one where there is none.
    /// </summary>
    [Fact]
    public async Task ARequestWithNoWindow_AsksThePlainProbeQuestions_EveryTime()
    {
        _probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.AppendTo);
        _probe.Setup(p => p.CallerHoldsPrivilegeAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        DecisionAccessWindow.Of(null).Should().BeNull("a job or the reconciler has no request, so no window");

        var plain = Request(CallerA);
        var closed = Request(CallerA);
        DecisionAccessWindow.Open(closed, CallerA).Close(); // a follow-up after the commit closed its window
        foreach (var http in new[] { plain, closed })
        {
            for (var i = 0; i < 2; i++)
            {
                (await DecisionExec.CallerHoldsAsync(_probe.Object, http, "sprk_matters", Matter, DecisionExec.AppendToOperation, default)).Should().BeTrue();
                (await DecisionExec.CallerHoldsPrivilegeAsync(_probe.Object, http, Privilege, default)).Should().BeTrue();
            }
        }

        _probe.Invocations.Count(i => i.Method.Name == nameof(CallerRecordAccessProbe.GetCallerRightsAsync)).Should().Be(4);
        _probe.Invocations.Count(i => i.Method.Name == nameof(CallerRecordAccessProbe.CallerHoldsPrivilegeAsync)).Should().Be(4);
        RightsAsks().Should().Be(0, "the window's forms are asked only inside a window");
        PrivilegeAsks().Should().Be(0);

        var windowed = Request(CallerA);
        using var window = DecisionAccessWindow.Open(windowed, CallerA);
        for (var i = 0; i < 2; i++)
        {
            (await DecisionExec.CallerHoldsAsync(_probe.Object, windowed, "sprk_matters", Matter, DecisionExec.AppendToOperation, default)).Should().BeTrue();
            (await DecisionExec.CallerHoldsPrivilegeAsync(_probe.Object, windowed, Privilege, default)).Should().BeTrue();
        }

        RightsAsks().Should().Be(1, "inside a window the repeat is answered from it");
        PrivilegeAsks().Should().Be(1);
        _probe.Invocations.Count(i => i.Method.Name == nameof(CallerRecordAccessProbe.GetCallerRightsAsync)).Should().Be(4, "unchanged");
    }
}
