// GitHub #2845 round 2 review, F1 — the process-wide Dataverse read gate and the throttle retry.
//
// Contracts pinned: concurrent sends through the handler never exceed the gate's permits; a throttled read (429) is retried
// after the server's wait and succeeds; a read still throttled after the retries is returned UNCHANGED (429), and the reader
// behind it fails closed as before; a caller cancelled while waiting for a permit gets OperationCanceledException and sends
// nothing; a non-idempotent verb is never resent; the filing walk's memoized reads hold a gate permit and their chunk fan-out
// is bounded. Only the HTTP exchange is substituted (a hand-written handler, never Mock<HttpMessageHandler>: testing.md B1).

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Dataverse;

public sealed class DataverseThrottlingHandlerTests
{
    private const string Url = "https://test.crm.dynamics.com/api/data/v9.2/sprk_matters";

    [Fact]
    public async Task ConcurrentSends_NeverExceedTheGatesPermits()
    {
        var gate = new DataverseConcurrencyGate(3);
        var wire = new Wire { Hold = TimeSpan.FromMilliseconds(25) };
        using var client = new HttpClient(Handler(gate, wire));

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync(Url)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        wire.Sends.Should().Be(20);
        wire.Peak.Should().BeLessOrEqualTo(3, "the process-wide gate bounds the reads in flight");
    }

    [Fact]
    public async Task OneTooManyRequests_ThenOk_SucceedsAfterOneRetry_HonoringRetryAfter()
    {
        var wire = new Wire();
        wire.Script.Enqueue(Throttled(retryAfterSeconds: 2));
        var delays = new List<TimeSpan>();
        using var client = new HttpClient(Handler(new DataverseConcurrencyGate(), wire, delays));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        wire.Sends.Should().Be(2, "one retry");
        delays.Should().Equal(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TheServersWait_IsCappedAtFiveSeconds_AndTheMillisecondHeaderWins()
    {
        var wire = new Wire();
        wire.Script.Enqueue(Throttled(retryAfterSeconds: 30));
        var precise = Throttled(retryAfterSeconds: 30);
        precise.Headers.Add("x-ms-retry-after-ms", "750");
        wire.Script.Enqueue(precise);
        var delays = new List<TimeSpan>();
        using var client = new HttpClient(Handler(new DataverseConcurrencyGate(), wire, delays));

        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);

        delays.Should().Equal(DataverseThrottlingHandler.MaxRetryDelay, TimeSpan.FromMilliseconds(750));
    }

    [Fact]
    public async Task ThreeTooManyRequests_ReturnTheLast429_AndTheDenyListFailsClosed()
    {
        var wire = new Wire { Always = HttpStatusCode.TooManyRequests };
        using var client = new HttpClient(Handler(new DataverseConcurrencyGate(), wire, new List<TimeSpan>()));

        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "a read still throttled after the retries is returned unchanged");
        wire.Sends.Should().Be(1 + DataverseThrottlingHandler.MaxRetries);

        // The caller's own fail-closed mapping still applies: the real deny-list reader denies every candidate.
        var record = Guid.NewGuid();
        var reader = new NoAccessListReader(
            new HttpClient(Handler(new DataverseConcurrencyGate(), new Wire { Always = HttpStatusCode.TooManyRequests }, new List<TimeSpan>())),
            Configuration(), new StaticTokenCredential(), NullLogger<NoAccessListReader>.Instance);

        var result = await reader.GetDeniedRecordsAsync(
            Guid.NewGuid(), Array.Empty<Guid>(), new[] { new NoAccessCandidateRecord("sprk_matter", record, Array.Empty<Guid>()) });

        result.FailedClosed.Should().BeTrue();
        result.DeniedRecordIds.Should().Contain(record);
    }

    [Fact]
    public async Task ACallerCancelledWhileWaitingForAPermit_GetsOperationCanceled_AndSendsNothing()
    {
        var gate = new DataverseConcurrencyGate(1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new Wire { Until = release.Task };
        using var invoker = new HttpMessageInvoker(Handler(gate, wire));

        var holder = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url), CancellationToken.None);
        await wire.FirstSend.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        var waiter = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Url), cts.Token);
        cts.Cancel();

        await FluentActions.Awaiting(() => waiter).Should().ThrowAsync<OperationCanceledException>();
        wire.Sends.Should().Be(1, "the cancelled caller never got a permit, so it sent nothing");

        release.SetResult();
        (await holder).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ANonIdempotentVerb_IsNeverResent()
    {
        var wire = new Wire { Always = HttpStatusCode.TooManyRequests };
        using var client = new HttpClient(Handler(new DataverseConcurrencyGate(), wire, new List<TimeSpan>()));

        var response = await client.PostAsync(Url, new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        wire.Sends.Should().Be(1);
    }

    [Fact]
    public void ThePermitCount_DefaultsTo24_AndTheOptionalKeyOverridesIt()
    {
        DataverseConcurrencyGate.FromConfiguration(new ConfigurationBuilder().Build())
            .MaxConcurrentRequests.Should().Be(24);
        DataverseConcurrencyGate.FromConfiguration(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [DataverseConcurrencyGate.ConfigurationKey] = "10" }).Build())
            .MaxConcurrentRequests.Should().Be(10);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(24, SecureRootInheritance.MaxConcurrentChunkReads)]
    public async Task TheFilingWalksMemoizedReads_HoldAGatePermit_AndBoundTheirChunkFanOut(int permits, int expectedBound)
    {
        // 1,400 work assignments = 7 chunks of 200 for ONE walk step (each started at once before F1).
        var ids = Enumerable.Range(0, 1400).Select(_ => Guid.NewGuid()).ToList();
        var inFlight = 0;
        var peak = 0;
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns(async (QueryExpression _, CancellationToken ct) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, now);
                await Task.Delay(25, ct);
                Interlocked.Decrement(ref inFlight);
                return new EntityCollection();
            });

        var answers = await SecureRootInheritance.ReadSecureParentsOfManyAsync(
            entities.Object, NullLogger.Instance, "sprk_workassignment", ids, CancellationToken.None,
            requestMemo: new RequestAccessMemo(new DataverseConcurrencyGate(permits)));

        answers.Should().HaveCount(ids.Count);
        peak.Should().BeLessOrEqualTo(expectedBound);
    }

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────

    internal static DataverseThrottlingHandler Handler(DataverseConcurrencyGate gate, HttpMessageHandler inner, List<TimeSpan>? delays = null)
    {
        var handler = new DataverseThrottlingHandler(gate, TimeProvider.System) { InnerHandler = inner };
        if (delays is not null)
        {
            handler.Delay = (delay, _) =>
            {
                lock (delays) delays.Add(delay);
                return Task.CompletedTask;
            };
        }

        return handler;
    }

    internal static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
        .Build();

    private static HttpResponseMessage Throttled(int retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }

    internal static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    /// <summary>The Dataverse end of the wire: scripted answers first, then <see cref="Always"/> (an empty page by default);
    /// counts sends and the most in flight at once.</summary>
    private sealed class Wire : HttpMessageHandler
    {
        private int _inFlight, _peak, _sends;

        public Queue<HttpResponseMessage> Script { get; } = new();

        public HttpStatusCode Always { get; init; } = HttpStatusCode.OK;

        public TimeSpan Hold { get; init; }

        public Task? Until { get; init; }

        public TaskCompletionSource FirstSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Peak => Volatile.Read(ref _peak);

        public int Sends => Volatile.Read(ref _sends);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _sends);
            InterlockedMax(ref _peak, Interlocked.Increment(ref _inFlight));
            FirstSend.TrySetResult();
            try
            {
                if (Until is not null) await Until;
                if (Hold > TimeSpan.Zero) await Task.Delay(Hold, ct);
                lock (Script)
                {
                    if (Script.Count > 0) return Script.Dequeue();
                }

                return new HttpResponseMessage(Always) { Content = new StringContent("{\"value\":[]}") };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    internal sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
