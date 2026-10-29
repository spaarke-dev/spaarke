// -----------------------------------------------------------------------------
// H8GraphReplicationRetryTests.cs
//
// T186 audit of H8 (SharePoint Embedded container provisioning) — the same class of defect as the H3 L26/L27
// fixes (GraphReplicationRetryTests): a call that follows a create is refused because the platform has not
// caught up, and the handler treated that transient as fatal.
//   1. The business-unit stamp PATCH and its read-back on a container created seconds earlier can answer 404;
//      any error used to delete the container and quarantine.
//   2. /activate straight after the create can answer 404; a failure quarantined.
//   3. The verifier quarantined on ANY non-404 error, including 429 / 5xx / timeouts / dropped connections.
// Each is now retried (bounded, delays from SpeContainerOptions); a genuine failure keeps its old outcome.
//
// ADR-038 CATEGORY: contract test on the Graph wire shape — the REAL provisioner / verifier run against a REAL
// GraphServiceClient built by SpeConfidentialClientGraphFactory's test seam over a hand-written fake transport
// (never Mock<HttpMessageHandler>).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H8GraphReplicationRetryTests
{
    private const string ContainerId = "b!root-container";
    private const string OwningApp = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string StampProperty = Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName;
    private static readonly Guid Unit = Guid.Parse("0b0b0b0b-1111-2222-3333-444444444444");

    private const string NotFoundJson = """{"error":{"code":"itemNotFound","message":"The container was not found."}}""";
    private const string ForbiddenJson = """{"error":{"code":"accessDenied","message":"denied"}}""";
    private const string ServerErrorJson = """{"error":{"code":"generalException","message":"boom"}}""";

    // ── 1. Bind: the stamp PATCH and the read-back ─────────────────────────────

    [Fact]
    public async Task Bind_StampPatchGets404WhileTheContainerReplicates_IsRetried_AndTheContainerIsKept()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.NotFound, NotFoundJson), Resp(HttpStatusCode.NotFound, NotFoundJson), Resp(HttpStatusCode.OK, "{}"))
            .Queue("GET", Resp(HttpStatusCode.OK, ContainerJson(Unit.ToString("D"))));

        var outcome = await Bind(graph);

        outcome.Should().BeOfType<SpeContainerBindOutcome.Bound>("a 404 seconds after the create is replication lag (T186 audit)");
        graph.Count("PATCH").Should().Be(3);
        graph.Count("DELETE").Should().Be(0, "the container is never removed for a transient");
    }

    [Fact]
    public async Task Bind_ReadBackGets404WhileTheContainerReplicates_IsRetried_AndTheContainerIsKept()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.OK, "{}"))
            .Queue("GET", Resp(HttpStatusCode.NotFound, NotFoundJson), Resp(HttpStatusCode.NotFound, NotFoundJson),
                Resp(HttpStatusCode.OK, ContainerJson(Unit.ToString("D"))));

        var outcome = await Bind(graph);

        outcome.Should().BeOfType<SpeContainerBindOutcome.Bound>();
        graph.Count("GET").Should().Be(3);
        graph.Count("PATCH").Should().Be(1, "the stamp landed — only the read-back is repeated");
        graph.Count("DELETE").Should().Be(0);
    }

    private const string StaleReadBack = "{\"id\":\"" + ContainerId + "\",\"customProperties\":{}}";

    [Fact]
    public async Task Bind_ReadBack200WithoutTheStamp_IsStaleReplica_ReReadUntilVisible_AndTheContainerIsKept()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.OK, "{}"))
            .Queue("GET", Resp(HttpStatusCode.OK, StaleReadBack), Resp(HttpStatusCode.OK, StaleReadBack),
                Resp(HttpStatusCode.OK, ContainerJson(Unit.ToString("D"))));

        var outcome = await Bind(graph);

        outcome.Should().BeOfType<SpeContainerBindOutcome.Bound>("a missing stamp seconds after a successful PATCH is replication lag");
        graph.Count("GET").Should().Be(3);
        graph.Count("PATCH").Should().Be(1);
        graph.Count("DELETE").Should().Be(0);
    }

    [Fact]
    public async Task Bind_StampThatNeverBecomesVisible_IsBounded_ThenTakesTheExistingFailedBindPath()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.OK, "{}"))
            .Queue("GET", Resp(HttpStatusCode.OK, StaleReadBack))
            .Queue("DELETE", Resp(HttpStatusCode.NoContent, ""));

        var outcome = await Bind(graph, retryCount: 3);

        graph.Count("GET").Should().Be(3, "bounded by SpeContainerOptions.ReplicationRetryCount");
        var notBound = outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Subject;
        notBound.Removed.Should().BeTrue();
        notBound.Diagnostic.Should().Contain("did not read back").And.Contain("none");
        graph.Count("DELETE").Should().Be(1);
    }

    [Fact]
    public async Task Bind_ReadBackShowingADifferentStamp_IsARealConflict_NotRetried_AndKeepsItsOldOutcome()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.OK, "{}"))
            .Queue("GET", Resp(HttpStatusCode.OK, ContainerJson("1b1b1b1b-0000-0000-0000-000000000000")))
            .Queue("DELETE", Resp(HttpStatusCode.NoContent, ""));

        var outcome = await Bind(graph);

        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeTrue();
        graph.Count("GET").Should().Be(1, "another unit's stamp is visible — retrying cannot change that");
        graph.Count("DELETE").Should().Be(1);
    }

    [Fact]
    public async Task Bind_StampPatchThatIsRefused_IsNotRetried_AndKeepsItsOldOutcome()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.Forbidden, ForbiddenJson))
            .Queue("DELETE", Resp(HttpStatusCode.NoContent, ""));

        var outcome = await Bind(graph);

        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeTrue(
            "a genuine failure still removes the container H8 just created (unchanged behaviour)");
        graph.Count("PATCH").Should().Be(1, "only replication lag is waited out — a 403 fails at once");
        graph.Count("DELETE").Should().Be(1);
    }

    [Fact]
    public async Task Bind_ReadBackThatFailsWithAServerError_IsNotRetried_AndKeepsItsOldOutcome()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.OK, "{}"))
            .Queue("GET", Resp(HttpStatusCode.InternalServerError, ServerErrorJson))
            .Queue("DELETE", Resp(HttpStatusCode.NoContent, ""));

        var outcome = await Bind(graph);

        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeTrue();
        graph.Count("GET").Should().Be(1);
    }

    [Fact]
    public async Task Bind_ThatNeverReplicates_IsBounded_ThenTakesTheExistingFailurePath()
    {
        var graph = new SequencedGraph()
            .Queue("PATCH", Resp(HttpStatusCode.NotFound, NotFoundJson))
            .Queue("DELETE", Resp(HttpStatusCode.NotFound, NotFoundJson));

        var outcome = await Bind(graph, retryCount: 3);

        graph.Count("PATCH").Should().Be(3, "bounded by SpeContainerOptions.ReplicationRetryCount");
        graph.Count("DELETE").Should().Be(3, "the removal waits out the same lag, also bounded");
        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeFalse(
            "a container this run just created that still answers 404 stays on the record (T186 review) — a resume " +
            "binds or removes it instead of creating a second one");
    }

    // ── 2. Activate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_Gets404WhileTheNewContainerReplicates_IsRetriedUntilItSucceeds()
    {
        var graph = new SequencedGraph().Queue("POST",
            Resp(HttpStatusCode.NotFound, NotFoundJson), Resp(HttpStatusCode.NotFound, NotFoundJson), Resp(HttpStatusCode.OK, ""));

        var outcome = await Activate(graph);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.Success>().Which.Outputs.ContainerId.Should().Be(ContainerId);
        graph.Count("POST").Should().Be(3);
    }

    [Fact]
    public async Task Activate_ThatIsRefused_IsNotRetried_AndIsStillAnActivateFailure()
    {
        var graph = new SequencedGraph().Queue("POST", Resp(HttpStatusCode.Forbidden, ForbiddenJson));

        var outcome = await Activate(graph);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.ActivateFailure>().Which.ContainerId.Should().Be(ContainerId);
        graph.Count("POST").Should().Be(1);
    }

    [Fact]
    public async Task Activate_ThatNeverReplicates_IsBounded_ThenIsAnActivateFailure()
    {
        var graph = new SequencedGraph().Queue("POST", Resp(HttpStatusCode.NotFound, NotFoundJson));

        var outcome = await Activate(graph, retryCount: 4);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.ActivateFailure>().Which.ContainerId.Should().Be(ContainerId);
        graph.Count("POST").Should().Be(4);
    }

    [Fact]
    public async Task Provision_ActivationRightAfterCreateGets404_IsRetried_NoSecondContainerIsCreated()
    {
        var graph = new SequencedGraph().Queue("POST",
            Resp(HttpStatusCode.Created, "{\"id\":\"" + ContainerId + "\"}"),
            Resp(HttpStatusCode.NotFound, NotFoundJson),
            Resp(HttpStatusCode.OK, ""));

        var outcome = await Sut(graph).ProvisionAsync(
            new SpeContainerProvisionRequest("acme", TenantId, "cccccccc-dddd-eeee-ffff-000000000001", OwningApp, "Spaarke Container - acme", "d"),
            CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.Success>();
        graph.Paths("POST").Count(p => p.EndsWith("/containers", StringComparison.Ordinal)).Should().Be(1, "the create is never repeated");
    }

    // ── 3. Verifier ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Verify_TransientAnswerThenSuccess_IsRetried_AndVerified(HttpStatusCode transient)
    {
        var graph = new SequencedGraph().Queue("GET",
            Resp(transient, ServerErrorJson), Resp(transient, ServerErrorJson), Resp(HttpStatusCode.OK, "{\"id\":\"" + ContainerId + "\",\"status\":\"active\"}"));

        var result = await Verify(graph);

        result.Should().BeOfType<SpeContainerVerificationResult.Verified>();
        graph.Count("GET").Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Verify_TransientAnswerThatPersists_IsATransientFailure_NotAQuarantine(HttpStatusCode transient)
    {
        var graph = new SequencedGraph().Queue("GET", Resp(transient, ServerErrorJson));

        var result = await Verify(graph, retryCount: 3);

        result.Should().BeOfType<SpeContainerVerificationResult.TransientFailure>(
            "429/5xx says nothing about the container — the handler maps it to Resumable, never QuarantineRequired");
        graph.Count("GET").Should().Be(3, "bounded by SpeContainerOptions.VerifyTransientRetryCount");
    }

    [Fact]
    public async Task Verify_DroppedConnectionThatPersists_IsATransientFailure_NotAThrownException()
    {
        var graph = new SequencedGraph().Queue("GET", () => throw new HttpRequestException("connection reset"));

        var result = await Verify(graph, retryCount: 2);

        result.Should().BeOfType<SpeContainerVerificationResult.TransientFailure>();
        graph.Count("GET").Should().Be(2);
    }

    [Fact]
    public async Task Verify_ClientSideTimeout_IsRetried_ThenATransientFailure()
    {
        var graph = new SequencedGraph { Delay = TimeSpan.FromSeconds(5) }.Queue("GET", Resp(HttpStatusCode.OK, "{}"));

        var result = await Verify(graph, retryCount: 2, graphRequestTimeout: TimeSpan.FromMilliseconds(50));

        result.Should().BeOfType<SpeContainerVerificationResult.TransientFailure>(
            "a timeout the caller did not cause is transient (TaskCanceledException from the per-attempt timeout)");
        graph.Count("GET").Should().Be(2);
    }

    [Fact]
    public async Task Verify_Forbidden_IsNotRetried_AndStaysNotVerified()
    {
        var graph = new SequencedGraph().Queue("GET", Resp(HttpStatusCode.Forbidden, ForbiddenJson));

        var result = await Verify(graph);

        result.Should().BeOfType<SpeContainerVerificationResult.NotVerified>("a real authorization answer keeps its old outcome");
        graph.Count("GET").Should().Be(1);
    }

    [Fact]
    public async Task Verify_NotFound_IsNotRetried_AndStaysReplicationPending()
    {
        var graph = new SequencedGraph().Queue("GET", Resp(HttpStatusCode.NotFound, NotFoundJson));

        var result = await Verify(graph);

        result.Should().BeOfType<SpeContainerVerificationResult.ReplicationPending>();
        graph.Count("GET").Should().Be(1, "404 is the run-level replication gate (WaitingOnGate), not an in-process retry");
    }

    [Fact]
    public async Task Verify_TheCallersCancellation_IsNotRetried_AndIsNotATransientFailure()
    {
        using var cts = new CancellationTokenSource();
        var graph = new SequencedGraph().Queue("GET", () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var act = () => Verify(graph, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        graph.Count("GET").Should().Be(1);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static SpeContainerOptions FastOptions(int retryCount, TimeSpan? graphRequestTimeout = null)
    {
        var options = new SpeContainerOptions
        {
            ReplicationRetryCount = retryCount,
            ReplicationRetryDelay = TimeSpan.FromMilliseconds(1),
            VerifyTransientRetryCount = retryCount,
            VerifyTransientRetryDelay = TimeSpan.FromMilliseconds(1),
        };
        if (graphRequestTimeout is { } timeout)
        {
            options.GraphRequestTimeout = timeout;
        }

        return options;
    }

    private static SpeConfidentialClientGraphFactory Factory(SequencedGraph graph)
        => new((_, _) => new FixedCredential(), graph);

    private static GraphContainerProvisioner Sut(SequencedGraph graph, int retryCount = 4)
        => new(Factory(graph), Options.Create(FastOptions(retryCount)), NullLogger<GraphContainerProvisioner>.Instance);

    private static Task<SpeContainerBindOutcome> Bind(SequencedGraph graph, int retryCount = 4)
        => Sut(graph, retryCount).BindRootContainerAsync(
            new SpeContainerBindRequest("acme", TenantId, OwningApp, ContainerId, Unit), CancellationToken.None);

    private static Task<SpeContainerProvisionOutcome> Activate(SequencedGraph graph, int retryCount = 4)
        => Sut(graph, retryCount).ActivateAsync(
            new SpeContainerActivationRequest("acme", TenantId, OwningApp, ContainerId), CancellationToken.None);

    private static Task<SpeContainerVerificationResult> Verify(
        SequencedGraph graph, int retryCount = 4, TimeSpan? graphRequestTimeout = null, CancellationToken cancellationToken = default)
        => new GraphAppOnlyContainerVerifier(
                Factory(graph), Options.Create(FastOptions(retryCount, graphRequestTimeout)),
                NullLogger<GraphAppOnlyContainerVerifier>.Instance)
            .VerifyAsync(new SpeContainerVerificationRequest(ContainerId, OwningApp, TenantId), cancellationToken);

    private static string ContainerJson(string stamp) =>
        "{\"id\":\"" + ContainerId + "\",\"customProperties\":{\"" + StampProperty + "\":{\"value\":\"" + stamp +
        "\",\"isSearchable\":false}}}";

    private static Func<HttpResponseMessage> Resp(HttpStatusCode status, string body) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// A hand-written fake Graph transport: per verb, a queue of responders consumed in order (the last one repeats).
    /// Every request is recorded.
    /// </summary>
    private sealed class SequencedGraph : HttpMessageHandler
    {
        private readonly Dictionary<string, List<Func<HttpResponseMessage>>> _queues = new();
        private readonly Dictionary<string, int> _counts = new();
        private readonly List<(string Method, string Path)> _requests = new();

        /// <summary>Every request is delayed by this long (honouring cancellation) — a slow Graph.</summary>
        public TimeSpan Delay { get; init; }

        public SequencedGraph Queue(string method, params Func<HttpResponseMessage>[] responders)
        {
            _queues[method] = responders.ToList();
            return this;
        }

        public int Count(string method) => _requests.Count(r => r.Method == method);

        public IReadOnlyList<string> Paths(string method) => _requests.Where(r => r.Method == method).Select(r => r.Path).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.Method.Method;
            _requests.Add((method, request.RequestUri!.AbsolutePath));
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (!_queues.TryGetValue(method, out var queue))
            {
                throw new InvalidOperationException($"unexpected {method} {request.RequestUri}");
            }

            var index = _counts.GetValueOrDefault(method);
            _counts[method] = index + 1;
            return queue[Math.Min(index, queue.Count - 1)]();
        }
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("owner-app-graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
