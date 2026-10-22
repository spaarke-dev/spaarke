using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// <see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync"/> (ebilling-r1 task 081, uac-r2 review F1, 2026-10-22):
/// the batched rights question that keeps "Dataverse said no" (a 403: <see cref="AccessRights.None"/>) apart from "Dataverse said
/// nothing" (any other non-200, a failed exchange or WhoAmI: <see langword="null"/>), so a count of what the caller can see is never
/// lowered by throttling.
/// </summary>
/// <remarks>
/// The PRODUCTION probe runs, with only its wire steps answered from memory (the OBO exchange, WhoAmI and the one RetrievePrincipalAccess
/// GET) and its waits recorded instead of slept, as <c>AuthorizationGateTimingTests</c> does (ADR-038: no transport double, no real sleep).
/// The existing <see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsAsync"/> keeps answering <see cref="AccessRights.None"/> for
/// every unanswered question (pinned below).
/// </remarks>
public class CallerRecordAccessProbeRecordsOrUnknownTests
{
    private const string Set = "sprk_invoices";

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ARecordAnsweredOtherThan200Or403_IsUnknown_AndTheOthersStillAnswer(HttpStatusCode status)
    {
        var failing = Guid.NewGuid();
        var visible = Guid.NewGuid();
        var probe = new WireProbe();
        probe.StatusByRecord[failing] = status;

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync("caller-token", new[] { (Set, failing), (Set, visible) });

        rights[0].Should().BeNull($"a {(int)status} is no answer about the caller's rights");
        rights[1].Should().Be(AccessRights.Read, "one unanswered record does not take the others with it");
    }

    [Fact]
    public async Task ARecordWhoseLookupThrows_IsUnknown()
    {
        var failing = Guid.NewGuid();
        var probe = new WireProbe();
        probe.ThrowingRecords.Add(failing);

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync("caller-token", new[] { (Set, failing) });

        rights.Should().Equal(new AccessRights?[] { null });
    }

    [Fact]
    public async Task WhenTheExchangeFails_EveryRecordIsUnknown_WithoutAskingAboutAny()
    {
        var probe = new WireProbe { FailExchange = true };

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync("caller-token", new[] { (Set, Guid.NewGuid()), (Set, Guid.NewGuid()) });

        rights.Should().HaveCount(2).And.OnlyContain(r => r == null);
        probe.Asks.Should().Be(0);
    }

    [Fact]
    public async Task WhenWhoAmIFails_EveryRecordIsUnknown_WithoutAskingAboutAny()
    {
        var probe = new WireProbe { FailWhoAmI = true };

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync("caller-token", new[] { (Set, Guid.NewGuid()) });

        rights.Should().Equal(new AccessRights?[] { null });
        probe.Asks.Should().Be(0);
    }

    [Fact]
    public async Task WhenTheCallerCancelsDuringWhoAmI_EveryRecordIsUnknown_NotRethrown()
    {
        // The per-record step rethrows the caller's cancellation; the per-set steps (the OBO exchange, WhoAmI) catch everything and
        // return "no caller", so a cancellation there reads as "every entry unknown" (uac-r2 re-check round 2, doc correction).
        using var cts = new CancellationTokenSource();
        var probe = new WireProbe(new HttpClient(new CancellingHandler(cts))) { UseRealWhoAmI = true };

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync(
            "caller-token", new[] { (Set, Guid.NewGuid()), (Set, Guid.NewGuid()) }, cts.Token);

        rights.Should().HaveCount(2).And.OnlyContain(r => r == null);
        probe.Asks.Should().Be(0, "no record was asked about once the caller could not be established");
    }

    [Fact]
    public async Task ARecordWhoseOkBodyCannotBeRead_IsUnknown_AndTheOthersStillAnswer()
    {
        var unreadable = Guid.NewGuid();
        var visible = Guid.NewGuid();
        var probe = new WireProbe();
        probe.UnreadableBodyRecords.Add(unreadable);

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync("caller-token", new[] { (Set, unreadable), (Set, visible) });

        rights[0].Should().BeNull("a 200 whose body cannot be parsed is no answer");
        rights[1].Should().Be(AccessRights.Read);
    }

    [Fact]
    public async Task WithNoCallerToken_EveryRecordIsUnknown()
    {
        var probe = new WireProbe();

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync(null, new[] { (Set, Guid.NewGuid()) });

        rights.Should().Equal(new AccessRights?[] { null });
    }

    [Fact]
    public async Task MustNotFire_A200IsTheRights_AndA403IsNone_InTargetOrder()
    {
        var granted = Guid.NewGuid();
        var denied = Guid.NewGuid();
        var probe = new WireProbe();
        probe.StatusByRecord[denied] = HttpStatusCode.Forbidden;

        var rights = await probe.GetCallerRightsForRecordsOrUnknownAsync("caller-token", new[] { (Set, denied), (Set, granted) });

        rights.Should().Equal(new AccessRights?[] { AccessRights.None, AccessRights.Read }, "a 403 is Dataverse's answer: no access");
        probe.Waits.Should().BeEmpty("a 403 outside an authorization gate is answered at once, as before");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TheExistingBatchedMethod_IsUnchanged_EveryUnansweredOrDeniedRecordIsNone(HttpStatusCode status)
    {
        var record = Guid.NewGuid();
        var granted = Guid.NewGuid();
        var probe = new WireProbe();
        probe.StatusByRecord[record] = status;

        var rights = await probe.GetCallerRightsForRecordsAsync("caller-token", new[] { (Set, record), (Set, granted) });

        rights.Should().Equal(AccessRights.None, AccessRights.Read);
        (await new WireProbe { FailExchange = true }.GetCallerRightsForRecordsAsync("caller-token", new[] { (Set, record) }))
            .Should().Equal(AccessRights.None);
    }

    /// <summary>Cancels the caller's token as the request is sent, then fails the way a cancelled HttpClient call does.</summary>
    private sealed class CancellingHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>The shipped probe, with its wire steps answered in memory and its waits recorded.</summary>
    private sealed class WireProbe : CallerRecordAccessProbe
    {
        private int _asks;

        public WireProbe(HttpClient? http = null)
            : base(
                http ?? new HttpClient(),
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

        public ConcurrentDictionary<Guid, HttpStatusCode> StatusByRecord { get; } = new();
        public ConcurrentBag<Guid> ThrowingRecords { get; } = new();
        public bool FailExchange { get; init; }
        public bool FailWhoAmI { get; init; }

        /// <summary>Run the PRODUCTION WhoAmI step (over the HttpClient given) instead of the in-memory answer.</summary>
        public bool UseRealWhoAmI { get; init; }

        public ConcurrentBag<Guid> UnreadableBodyRecords { get; } = new();
        public int Asks => Volatile.Read(ref _asks);
        public ConcurrentQueue<TimeSpan> WaitQueue { get; } = new();
        public IReadOnlyList<TimeSpan> Waits => WaitQueue.ToList();

        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct)
            => Task.FromResult<string?>(FailExchange ? null : "dataverse-token");

        protected override Task<Guid?> ResolveCallerSystemUserIdAsync(string dataverseToken, CancellationToken ct)
            => UseRealWhoAmI
                ? base.ResolveCallerSystemUserIdAsync(dataverseToken, ct)
                : Task.FromResult<Guid?>(FailWhoAmI ? null : Guid.Parse("08108108-1111-4000-8000-0000000000ff"));

        protected override Task<(HttpStatusCode Status, string Body)> SendPrincipalAccessRequestAsync(
            string url, string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _asks);
            if (ThrowingRecords.Any(id => url.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)))
                throw new HttpRequestException("simulated transport fault");

            if (UnreadableBodyRecords.Any(id => url.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult((HttpStatusCode.OK, "this is not json {"));

            var match = StatusByRecord.FirstOrDefault(kv => url.Contains(kv.Key.ToString(), StringComparison.OrdinalIgnoreCase));
            (HttpStatusCode, string) answer = match.Key == Guid.Empty
                ? (HttpStatusCode.OK, """{"AccessRights":"ReadAccess"}""")
                : match.Value switch
                {
                    HttpStatusCode.Forbidden => (HttpStatusCode.Forbidden, """{"error":{"code":"0x80048306","message":"Principal user is missing access"}}"""),
                    HttpStatusCode.NotFound => (HttpStatusCode.NotFound, """{"error":{"code":"0x80040217","message":"Does Not Exist"}}"""),
                    var other => (other, """{"error":{"code":"0x80072321","message":"simulated"}}"""),
                };
            return Task.FromResult(answer);
        }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            WaitQueue.Enqueue(delay);
            return Task.CompletedTask;
        }
    }
}
