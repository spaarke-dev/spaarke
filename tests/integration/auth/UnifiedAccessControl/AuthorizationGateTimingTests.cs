using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 064, verifier finding F3: an authorization gate whose denial says "an unknown id and a
/// denied one cannot be told apart" must not tell them apart by TIME either. Dataverse answers an unknown record 404
/// (re-asked for replication lag, ~1.6 s) and a record the caller cannot see 403 <c>0x80048306</c> (at once), so before
/// this fix the gate's uniform denial came back ~1.6 s later for an unknown id.
/// </summary>
/// <remarks>
/// The PRODUCTION probe runs, with only its three wire steps answered from memory (the OBO exchange, WhoAmI, and the one
/// RetrievePrincipalAccess GET) and its wait recorded instead of slept (ADR-038: no transport double, no real sleep).
/// "Same time" is asserted as the same work: the same number of Dataverse asks and the same waits, in the same order.
/// </remarks>
public class AuthorizationGateTimingTests
{
    private const string Set = "sprk_projects";
    private static readonly Guid Unknown = Guid.Parse("06406406-1111-4000-8000-000000000001");
    private static readonly Guid Denied = Guid.Parse("06406406-1111-4000-8000-000000000002");
    private static readonly Guid Visible = Guid.Parse("06406406-1111-4000-8000-000000000003");
    private static readonly Guid PrivilegeDenied = Guid.Parse("06406406-1111-4000-8000-000000000004");

    private static readonly TimeSpan[] NotFoundSchedule = { TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200) };

    [Fact]
    public async Task OnTheGate_AnUnknownRecordAndADeniedOne_TakeTheSameWork_AndBothAreNone()
    {
        var unknown = new TimingProbe();
        var denied = new TimingProbe();

        var a = await unknown.GetCallerRightsForAuthorizationGateAsync("caller", Set, Unknown);
        var b = await denied.GetCallerRightsForAuthorizationGateAsync("caller", Set, Denied);

        a.Should().Be(AccessRights.None);
        b.Should().Be(AccessRights.None);
        denied.Asks.Should().Be(unknown.Asks).And.Be(3);
        denied.Waits.Should().Equal(unknown.Waits);
        denied.Waits.Should().Equal(NotFoundSchedule);
    }

    [Fact]
    public async Task OnTheGate_AnyOther403_IsEqualisedToo()
    {
        var probe = new TimingProbe();

        (await probe.GetCallerRightsForAuthorizationGateAsync("caller", Set, PrivilegeDenied)).Should().Be(AccessRights.None);

        probe.Waits.Should().Equal(NotFoundSchedule, "a 403 for a missing privilege is as fast as a no-access one");
    }

    [Fact]
    public async Task OnTheGate_ARecordTheCallerCanSee_IsAnsweredAtOnce()
    {
        var probe = new TimingProbe();

        (await probe.GetCallerRightsForAuthorizationGateAsync("caller", Set, Visible)).Should().Be(AccessRights.Read);

        probe.Asks.Should().Be(1);
        probe.Waits.Should().BeEmpty("an allowed caller pays nothing");
    }

    [Fact]
    public async Task OffTheGate_TheNotFoundRetryIsKept_AndANoAccessDenialIsAnsweredAtOnce()
    {
        var unknown = new TimingProbe();
        var denied = new TimingProbe();

        await unknown.GetCallerRightsAsync("caller", Set, Unknown);
        await denied.GetCallerRightsAsync("caller", Set, Denied);

        unknown.Waits.Should().Equal(NotFoundSchedule, "the replication-lag retry after a create stays");
        denied.Asks.Should().Be(1);
        denied.Waits.Should().BeEmpty("only an authorization gate equalises; every other probe use is unchanged");
    }

    [Fact]
    public async Task TheGateSchedule_DoesNotLeakIntoTheNextQuestionOnTheSameProbe()
    {
        var probe = new TimingProbe();

        await probe.GetCallerRightsForAuthorizationGateAsync("caller", Set, Denied);
        probe.Reset();
        await probe.GetCallerRightsAsync("caller", Set, Denied);

        probe.Waits.Should().BeEmpty();
    }

    [Fact]
    public async Task OnTheGate_ACancellationDuringTheWait_IsACancellation_NeverAnAnswer()
    {
        using var cts = new CancellationTokenSource();
        var probe = new TimingProbe { OnWait = cts.Cancel };

        var act = () => probe.GetCallerRightsForAuthorizationGateAsync("caller", Set, Denied, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a cancelled request is not a denial or an allow");
        probe.Asks.Should().Be(1, "nothing is asked again after the request is cancelled");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, false, true)]
    [InlineData(HttpStatusCode.NotFound, true, true)]
    [InlineData(HttpStatusCode.Forbidden, true, true)]
    [InlineData(HttpStatusCode.Forbidden, false, false)]
    [InlineData(HttpStatusCode.InternalServerError, true, false)]
    [InlineData(HttpStatusCode.Unauthorized, true, false)]
    public void TheScheduleRule(HttpStatusCode status, bool onTheGate, bool reasked)
        => CallerRecordAccessProbe.FollowsNotFoundSchedule(status, onTheGate).Should().Be(reasked);

    // ── Through the filter: every route on the fixed-entity-set and declared-target gates gets it ───────────────

    [Fact]
    public async Task TheFixedEntitySetGate_AnUnknownAndADeniedRecord_AreTheSameUniform404_AfterTheSameWork()
    {
        // The form task 159's events /{id} routes and task 064's /no-access routes use.
        var unknown = new TimingProbe();
        var denied = new TimingProbe();

        var a = await RouteGate(unknown, Unknown);
        var b = await RouteGate(denied, Denied);

        StatusOf(a).Should().Be(StatusCodes.Status404NotFound);
        StatusOf(b).Should().Be(StatusCodes.Status404NotFound);
        denied.Waits.Should().Equal(unknown.Waits).And.Equal(NotFoundSchedule);
        denied.Asks.Should().Be(unknown.Asks);
    }

    [Fact]
    public async Task TheDeclaredTargetGate_AnUnknownAndADeniedRecord_AreTheSame403_AfterTheSameWork()
    {
        // The form task 159's events create uses.
        var unknown = new TimingProbe();
        var denied = new TimingProbe();

        var a = await DeclaredGate(unknown, Unknown);
        var b = await DeclaredGate(denied, Denied);

        StatusOf(a).Should().Be(StatusCodes.Status403Forbidden);
        StatusOf(b).Should().Be(StatusCodes.Status403Forbidden);
        denied.Waits.Should().Equal(unknown.Waits).And.Equal(NotFoundSchedule);
    }

    private static DefaultHttpContext Context(Guid recordId)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer caller";
        http.Request.RouteValues["id"] = recordId.ToString();
        return http;
    }

    private static async Task<object?> RouteGate(TimingProbe probe, Guid recordId)
    {
        var context = new DefaultEndpointFilterInvocationContext(Context(recordId));
        return await RecordRouteAccessAuthorizationFilter.AuthorizeRouteRecordAsync(
            context, _ => ValueTask.FromResult<object?>(Results.Ok()), probe, "read", Set, "id", NullLogger.Instance);
    }

    private static async Task<object?> DeclaredGate(TimingProbe probe, Guid recordId)
    {
        var context = new DefaultEndpointFilterInvocationContext(Context(recordId));
        return await RecordRouteAccessAuthorizationFilter.AuthorizeDeclaredTargetAsync(
            context, _ => ValueTask.FromResult<object?>(Results.Ok()), probe, "read",
            _ => ValueTask.FromResult<(string, Guid)?>((Set, recordId)), requiredPrivilege: null, NullLogger.Instance);
    }

    private static int? StatusOf(object? result) => (result as IStatusCodeHttpResult)?.StatusCode;

    /// <summary>The production probe with its wire steps answered from memory and its waits recorded, never slept.</summary>
    private sealed class TimingProbe : CallerRecordAccessProbe
    {
        private int _asks;

        public TimingProbe()
            : base(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000aa",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000bb",
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
                }).Build(),
                NullLogger<CallerRecordAccessProbe>.Instance,
                // Never asked for a client (the exchange is substituted); the probe only refuses OBO without one.
                new OrderedCredentialClientProvider(
                    Options.Create(new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentityFederated" } }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
        }

        public int Asks => Volatile.Read(ref _asks);

        public ConcurrentQueue<TimeSpan> WaitQueue { get; } = new();

        public IReadOnlyList<TimeSpan> Waits => WaitQueue.ToList();

        public void Reset()
        {
            Interlocked.Exchange(ref _asks, 0);
            WaitQueue.Clear();
        }

        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct)
            => Task.FromResult<string?>("dataverse-token");

        protected override Task<Guid?> ResolveCallerSystemUserIdAsync(string dataverseToken, CancellationToken ct)
            => Task.FromResult<Guid?>(Guid.Parse("06406406-1111-4000-8000-0000000000ff"));

        protected override Task<(HttpStatusCode Status, string Body)> SendPrincipalAccessRequestAsync(
            string url, string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _asks);
            (HttpStatusCode, string) answer =
                url.Contains(Unknown.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? (HttpStatusCode.NotFound, """{"error":{"code":"0x80040217","message":"Does Not Exist"}}""")
                : url.Contains(Denied.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? (HttpStatusCode.Forbidden, """{"error":{"code":"0x80048306","message":"Principal user is missing access"}}""")
                : url.Contains(PrivilegeDenied.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? (HttpStatusCode.Forbidden, """{"error":{"code":"0x80040220","message":"missing prvReadsprk_project"}}""")
                : (HttpStatusCode.OK, """{"AccessRights":"ReadAccess"}""");
            return Task.FromResult(answer);
        }

        /// <summary>Runs inside each wait (e.g. to cancel the request mid-wait).</summary>
        public Action? OnWait { get; init; }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            WaitQueue.Enqueue(delay);
            OnWait?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
