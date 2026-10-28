using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Signals;

/// <summary>
/// Ontology task 170 (D-196): the caller's <c>WhoAmI()</c> is asked once per request and per credential
/// (<see cref="CallerWhoAmIMemo"/>), and never answers for a different caller, a different request or the application's own
/// credential. A failure is never remembered.
/// </summary>
/// <remarks>
/// The SHIPPED <see cref="CallerRecordAccessProbe"/> runs with only its wire seams answered in memory (the OBO exchange,
/// the <c>WhoAmI()</c> GET and the <c>RetrievePrincipalAccess</c> GET, all <c>protected virtual</c>), so the memo under test is
/// the production one, reached the way the commit route reaches it. No transport mock (ADR-038 B1). The request context is set
/// through a separate <see cref="HttpContextAccessor"/> instance, as the host does, so these tests also pin that the memo
/// sees the request the host set.
/// </remarks>
[Trait("status", "new")]
public sealed class CallerWhoAmIMemoIsolationTests
{
    private static readonly Guid Alice = Guid.Parse("17000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Bob = Guid.Parse("17000000-0000-4000-8000-0000000000b0");
    private static readonly Guid AppUser = Guid.Parse("17000000-0000-4000-8000-0000000000ee");
    private static readonly Guid AliceUnit = Guid.Parse("17000000-0000-4000-8000-0000000000c1");

    [Fact]
    public async Task OneRequest_ManyCallerQuestions_AsksWhoAmIOnce_AndStillAsksEveryRightsQuestion()
    {
        var probe = new WireProbe();
        probe.Users["alice-dv"] = Alice;

        await InRequestAsync(NewRequest("alice"), async () =>
        {
            (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice);
            (await probe.GetCallerRightsAsync("alice", "sprk_matters", Guid.NewGuid())).Should().NotBe(AccessRights.None);
            (await probe.GetCallerRightsAsync("alice", "contacts", Guid.NewGuid())).Should().NotBe(AccessRights.None);
            (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice);
        });

        probe.WhoAmIsFor("alice-dv").Should().Be(1, "who the caller is is asked once per request");
        probe.RightsAsked.Should().Be(2, "access results are never remembered: every rights question still asks Dataverse");
        probe.PrincipalsAskedAbout.Should().OnlyContain(p => p == Alice);
    }

    [Fact]
    public async Task TwoCallersInParallel_EachGetsTheirOwnSystemUserId_AndNeitherSeesTheOthers()
    {
        var probe = new WireProbe { HoldWhoAmI = true };
        probe.Users["alice-dv"] = Alice;
        probe.Users["bob-dv"] = Bob;
        var seen = new ConcurrentDictionary<string, ConcurrentBag<Guid?>>();

        async Task Caller(string token)
        {
            var answers = seen.GetOrAdd(token, _ => new ConcurrentBag<Guid?>());
            await InRequestAsync(NewRequest(token), async () =>
            {
                answers.Add(await probe.GetCallerSystemUserIdAsync(token));
                answers.Add(await probe.GetCallerSystemUserIdAsync(token));
                await probe.GetCallerRightsAsync(token, "sprk_matters", Guid.NewGuid());
                answers.Add(await probe.GetCallerSystemUserIdAsync(token));
            });
        }

        var both = Task.WhenAll(Task.Run(() => Caller("alice")), Task.Run(() => Caller("bob")));
        // Both first WhoAmIs are in flight together before either answers, so the two requests really overlap.
        await probe.BothAsking.Task.WaitAsync(TimeSpan.FromSeconds(10));
        probe.ReleaseWhoAmI();
        await both;

        seen["alice"].Should().HaveCount(3).And.OnlyContain(id => id == Alice);
        seen["bob"].Should().HaveCount(3).And.OnlyContain(id => id == Bob);
        probe.WhoAmIsFor("alice-dv").Should().Be(1);
        probe.WhoAmIsFor("bob-dv").Should().Be(1);
        probe.PrincipalsAskedAbout.Should().BeEquivalentTo(new[] { Alice, Bob },
            "each caller's rights are asked about that caller only");
    }

    [Fact]
    public async Task TheSameCaller_InTwoRequests_IsAskedOncePerRequest_NeverSharedAcrossRequests()
    {
        var probe = new WireProbe();
        probe.Users["alice-dv"] = Alice;

        await InRequestAsync(NewRequest("alice"), async () => (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice));
        probe.Users["alice-dv"] = Bob; // what Dataverse answers for the same token in the next request
        await InRequestAsync(NewRequest("alice"), async () => (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Bob,
            "a new request asks again; the first request's answer died with it"));

        probe.WhoAmIsFor("alice-dv").Should().Be(2);
    }

    [Fact]
    public async Task AppOnlyAndOboCredentials_InOneRequest_AreNeverMixed()
    {
        var probe = new WireProbe();
        probe.Users["alice-dv"] = Alice;
        probe.Users["app-only-dv"] = AppUser;

        await InRequestAsync(NewRequest("alice"), async () =>
        {
            (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice);
            (await probe.WhoAmIUnder("app-only-dv")).Should().Be(AppUser,
                "the application's own credential is a different token: it is asked about itself, never handed the caller");
            (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice,
                "and the caller is never handed the application's answer");
            (await probe.WhoAmIUnder("app-only-dv")).Should().Be(AppUser);
        });

        probe.WhoAmIsFor("alice-dv").Should().Be(1);
        probe.WhoAmIsFor("app-only-dv").Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "{\"UserId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task AFailedWhoAmI_IsNotRemembered_TheNextQuestionAsksAgain(HttpStatusCode failure, string failureBody)
    {
        var probe = new WireProbe();
        probe.Users["alice-dv"] = Alice;
        probe.FailNextWhoAmI = (failure, failureBody);

        await InRequestAsync(NewRequest("alice"), async () =>
        {
            (await probe.GetCallerSystemUserIdAsync("alice")).Should().BeNull("the failure denies, as before");
            (await probe.GetCallerRightsAsync("alice", "sprk_matters", Guid.NewGuid())).Should().NotBe(AccessRights.None,
                "the next question asks again and gets the real answer");
            (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice);
        });

        probe.WhoAmIsFor("alice-dv").Should().Be(2, "one failed ask, one successful ask, then the remembered success");
    }

    [Fact]
    public async Task OutsideARequest_NothingIsRemembered_EveryQuestionAsks()
    {
        var probe = new WireProbe();
        probe.Users["alice-dv"] = Alice;

        (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice);
        (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice);

        probe.WhoAmIsFor("alice-dv").Should().Be(2, "background work has no request to remember anything in");
    }

    [Fact]
    public async Task TheUserClientsWhoAmI_IsAnsweredWithTheFullBody_TheProbeObtained()
    {
        // The probe and DataverseUserClient exchange the same assertion through one MSAL cache, so they share the token and
        // the answer. A reader of WhoAmI's business unit (the reporting routes) must get it from a remembered answer too.
        var probe = new WireProbe { WhoAmIBusinessUnit = AliceUnit };
        probe.Users["alice-dv"] = Alice;
        var request = NewRequest("alice");

        await InRequestAsync(request, async () => (await probe.GetCallerSystemUserIdAsync("alice")).Should().Be(Alice));

        CallerWhoAmIMemo.TryGetBody(request, "alice-dv", out var body).Should().BeTrue();
        body.GetProperty("UserId").GetString().Should().Be(Alice.ToString("D"));
        body.GetProperty("BusinessUnitId").GetString().Should().Be(AliceUnit.ToString("D"));
        CallerWhoAmIMemo.TryGetBody(request, "bob-dv", out _).Should().BeFalse("another credential has no answer here");
        CallerWhoAmIMemo.TryGetBody(NewRequest("alice"), "alice-dv", out _).Should().BeFalse("another request has no answer here");
    }

    [Theory]
    [InlineData("GET", "/api/data/v9.2/WhoAmI()", true)]
    [InlineData("GET", "/api/data/v9.2/WhoAmI", true)]
    [InlineData("POST", "/api/data/v9.2/WhoAmI()", false)]
    [InlineData("GET", "/api/data/v9.2/WhoAmI()?$select=UserId", false)]
    [InlineData("GET", "/api/data/v9.2/systemusers(17000000-0000-4000-8000-0000000000a1)", false)]
    [InlineData("GET", "/api/data/v9.2/whoami()", false)]
    public void OnlyTheCallersWhoAmIGet_IsAnsweredFromTheMemo(string method, string apiPath, bool answered) =>
        CallerWhoAmIMemo.IsWhoAmI(new HttpMethod(method), apiPath).Should().Be(answered);

    // ── The rig ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext NewRequest(string bearer)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {bearer}";
        return http;
    }

    /// <summary>Runs <paramref name="work"/> as part of <paramref name="request"/>, set the way the host sets it. The setting is
    /// local to this async flow and is undone when it returns.</summary>
    private static async Task InRequestAsync(HttpContext request, Func<Task> work)
    {
        new HttpContextAccessor().HttpContext = request;
        await work();
    }

    /// <summary>The shipped probe with its three Dataverse-facing wire steps answered in memory and counted. The OBO exchange
    /// maps a caller's bearer token <c>x</c> to the Dataverse token <c>x-dv</c>, as MSAL would give one caller one token.</summary>
    private sealed class WireProbe : CallerRecordAccessProbe
    {
        private readonly ConcurrentDictionary<string, int> _whoAmIs = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _rightsAsked;

        public WireProbe()
            : base(
                new HttpClient(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-0000000000aa",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-0000000000bb",
                    ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                }).Build(),
                NullLogger<CallerRecordAccessProbe>.Instance,
                // Never asked for a client (the exchange is answered below); it only has to exist, or the probe refuses OBO.
                new OrderedCredentialClientProvider(
                    Options.Create(new CredentialSelectionOptions { Order = new List<string> { "ManagedIdentityFederated" } }),
                    new ConfigurationBuilder().Build(),
                    NullLogger<OrderedCredentialClientProvider>.Instance))
        {
        }

        /// <summary>Who Dataverse says each Dataverse token belongs to.</summary>
        public ConcurrentDictionary<string, Guid> Users { get; } = new();

        public Guid? WhoAmIBusinessUnit { get; init; }

        /// <summary>The next WhoAmI answers this instead (once).</summary>
        public (HttpStatusCode Status, string Body)? FailNextWhoAmI { get; set; }

        /// <summary>When set, WhoAmIs wait until two are in flight and the test releases them.</summary>
        public bool HoldWhoAmI { get; init; }

        public TaskCompletionSource BothAsking { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<Guid> PrincipalsAskedAbout { get; } = new();

        public int RightsAsked => Volatile.Read(ref _rightsAsked);

        public int WhoAmIsFor(string dataverseToken) => _whoAmIs.GetValueOrDefault(dataverseToken);

        public void ReleaseWhoAmI() => _release.TrySetResult();

        /// <summary>The production WhoAmI step under a token of the test's choosing (the application's own, say).</summary>
        public Task<Guid?> WhoAmIUnder(string dataverseToken) => ResolveCallerSystemUserIdAsync(dataverseToken, CancellationToken.None);

        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct) =>
            Task.FromResult<string?>($"{callerBearerToken}-dv");

        protected override async Task<(HttpStatusCode Status, string Body)> SendWhoAmIRequestAsync(string dataverseToken, CancellationToken ct)
        {
            _whoAmIs.AddOrUpdate(dataverseToken, 1, (_, n) => n + 1);
            if (HoldWhoAmI)
            {
                if (Interlocked.Increment(ref _inFlight) == 2)
                    BothAsking.TrySetResult();
                await _release.Task.WaitAsync(ct);
            }

            if (FailNextWhoAmI is { } failure)
            {
                FailNextWhoAmI = null;
                return failure;
            }

            var body = new Dictionary<string, object?> { ["UserId"] = Users[dataverseToken].ToString("D") };
            if (WhoAmIBusinessUnit is { } unit)
                body["BusinessUnitId"] = unit.ToString("D");
            return (HttpStatusCode.OK, JsonSerializer.Serialize(body));
        }

        protected override Task<(HttpStatusCode Status, string Body)> SendPrincipalAccessRequestAsync(
            string url, string dataverseToken, CancellationToken ct)
        {
            Interlocked.Increment(ref _rightsAsked);
            var principal = Guid.Parse(url[(url.IndexOf("systemusers(", StringComparison.Ordinal) + "systemusers(".Length)..][..36]);
            PrincipalsAskedAbout.Add(principal);
            return Task.FromResult((HttpStatusCode.OK, "{\"AccessRights\":\"ReadAccess, WriteAccess, AppendToAccess\"}"));
        }
    }
}
