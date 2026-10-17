using System.Collections.Concurrent;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Issue #1576 (c) and the disabled-user half of (a) — <see cref="DataverseAccessDataSource"/> sends every Dataverse
/// read with the token of the call that issued it, never through the shared
/// <c>HttpClient.DefaultRequestHeaders</c>, and an oid that maps only to a DISABLED systemuser resolves to no rights.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>GetUserAccessAsync</c> wrote the caller's OBO token onto
/// <c>DefaultRequestHeaders.Authorization</c>, and <c>EnsureAuthenticatedAsync</c> wrote the app token there only when
/// it refreshed. So on one instance an app-only call after an OBO call ran its lookup, team and role reads under the
/// previous caller's identity, and two concurrent calls on one instance could read under each other's tokens. The
/// systemuser lookup also had no <c>isdisabled</c> filter, so a disabled account still got its rights computed.</para>
/// <para><b>Instruments (ADR-038).</b> The same shape as <c>AccessCacheFaultCachingTests</c>: the production data
/// source over an in-memory ASP.NET Core server standing in for the Dataverse Web API, with the ONE override being the
/// MSAL OBO exchange (<c>GetDataverseTokenViaOBOAsync</c>, an internal virtual seam). The fake records each request's
/// Authorization header and attributes the request to a user by the oid (lookup) or systemuserid (every other read) in
/// its URL. No <c>Mock&lt;HttpMessageHandler&gt;</c>, no reflection, no sleeps.</para>
/// </remarks>
public sealed class AccessDataSourcePerRequestTokenTests
{
    private const string FakeServiceUrl = "https://fake-dataverse.crm.dynamics.com";
    private const string AppToken = "app-only-dataverse-token";

    private const string OidA = "1576a000-0000-0000-0000-0000000000a1";
    private const string OidB = "1576b000-0000-0000-0000-0000000000b1";
    private const string DisabledOid = "1576d000-0000-0000-0000-0000000000d1";

    private static readonly Guid DocumentId = Guid.Parse("15760000-0000-0000-0000-0000000000c1");

    [Fact]
    public async Task Obo_call_sends_every_read_with_the_obo_token_and_never_writes_the_default_header()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var client = dataverse.CreateClient();
        var source = new SeamAccessDataSource(client);

        var snapshot = await source.GetUserAccessAsync(OidA, DocumentId.ToString(), userAccessToken: "user-a");

        snapshot.AccessRights.Should().HaveFlag(AccessRights.Read);
        var requests = dataverse.Requests.ToList();
        requests.Select(r => r.Kind).Should().BeEquivalentTo(new[] { "lookup", "rights", "teams", "roles" });
        requests.Should().OnlyContain(r => r.Authorization == "Bearer " + SeamAccessDataSource.OboTokenFor("user-a"));
        client.DefaultRequestHeaders.Authorization.Should().BeNull(
            "the shared default header is per-instance state that a later or concurrent call would inherit");
    }

    [Fact]
    public async Task App_only_calls_after_an_obo_call_on_the_same_instance_send_the_app_token()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var client = dataverse.CreateClient();
        var source = new SeamAccessDataSource(client);

        await source.GetUserAccessAsync(OidA, DocumentId.ToString(), userAccessToken: null);
        var firstAppOnly = dataverse.Drain();
        await source.GetUserAccessAsync(OidA, DocumentId.ToString(), userAccessToken: "user-a");
        var obo = dataverse.Drain();
        await source.GetUserAccessAsync(OidA, DocumentId.ToString(), userAccessToken: null);
        var secondAppOnly = dataverse.Drain();

        firstAppOnly.Should().HaveCount(4).And.OnlyContain(r => r.Authorization == "Bearer " + AppToken);
        obo.Should().HaveCount(4).And.OnlyContain(r => r.Authorization == "Bearer " + SeamAccessDataSource.OboTokenFor("user-a"));
        secondAppOnly.Should().HaveCount(4).And.OnlyContain(r => r.Authorization == "Bearer " + AppToken,
            "an app-only call must not run its reads under the previous caller's OBO identity");
        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_calls_on_one_instance_each_send_their_own_token()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        // Hold both oid lookups until BOTH have arrived, so the two calls are provably in flight together: each has
        // already obtained its token before either proceeds to its rights, team and role reads.
        dataverse.HoldLookupsUntil(2);
        var source = new SeamAccessDataSource(dataverse.CreateClient());

        var callA = source.GetUserAccessAsync(OidA, DocumentId.ToString(), userAccessToken: "user-a");
        var callB = source.GetUserAccessAsync(OidB, DocumentId.ToString(), userAccessToken: "user-b");
        await Task.WhenAll(callA, callB).WaitAsync(TimeSpan.FromSeconds(20));

        var requests = dataverse.Requests.ToList();
        requests.Should().HaveCount(8);
        requests.Where(r => r.Oid == OidA).Should().HaveCount(4)
            .And.OnlyContain(r => r.Authorization == "Bearer " + SeamAccessDataSource.OboTokenFor("user-a"));
        requests.Where(r => r.Oid == OidB).Should().HaveCount(4)
            .And.OnlyContain(r => r.Authorization == "Bearer " + SeamAccessDataSource.OboTokenFor("user-b"),
                "a read issued for user B must carry user B's token, whatever user A's call wrote meanwhile");
    }

    [Theory]
    [InlineData("user-d")]
    [InlineData(null)]
    public async Task A_disabled_user_resolves_to_no_rights(string? userAccessToken)
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var source = new SeamAccessDataSource(dataverse.CreateClient());

        var snapshot = await source.GetUserAccessAsync(DisabledOid, DocumentId.ToString(), userAccessToken);

        snapshot.AccessRights.Should().Be(AccessRights.None);
        snapshot.Faulted.Should().BeFalse("\"no active systemuser for this oid\" is an answer, not a failed read");
        dataverse.Requests.Select(r => r.Kind).Should().Equal(new[] { "lookup" },
            "no rights may be computed for a disabled account");
    }

    [Fact]
    public async Task A_disabled_user_resolves_to_no_record_rights()
    {
        await using var dataverse = await FakeDataverse.StartAsync();
        var source = new SeamAccessDataSource(dataverse.CreateClient());

        var snapshot = await source.GetRecordAccessAsync(DisabledOid, "sprk_matters", DocumentId, "user-d");

        snapshot.AccessRights.Should().Be(AccessRights.None);
        dataverse.Requests.Select(r => r.Kind).Should().Equal("lookup");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Instruments
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The PRODUCTION data source with only the MSAL OBO exchange replaced. The Dataverse token it hands back is
    /// derived from the caller's token, so each request can be traced to the call that issued it.
    /// </summary>
    private sealed class SeamAccessDataSource : DataverseAccessDataSource
    {
        public SeamAccessDataSource(HttpClient client)
            : base(
                Mock.Of<IDataverseService>(),
                client,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dataverse:ServiceUrl"] = FakeServiceUrl,
                    ["Graph:ManagedIdentity:Enabled"] = "true",
                }).Build(),
                NullLogger<DataverseAccessDataSource>.Instance,
                credential: new StaticTokenCredential())
        {
        }

        public static string OboTokenFor(string userAccessToken) => "obo-for-" + userAccessToken;

        internal override Task<string> GetDataverseTokenViaOBOAsync(string userAccessToken, CancellationToken ct = default)
            => Task.FromResult(OboTokenFor(userAccessToken));
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new(AppToken, DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }

    /// <summary>One request as the fake Dataverse saw it.</summary>
    private sealed record SeenRequest(string Kind, string Oid, string? Authorization);

    /// <summary>
    /// The Web API surface <c>GetUserAccessAsync</c> and <c>GetRecordAccessAsync</c> read, over an in-memory server.
    /// Users A and B are active; the disabled user's systemuser row has <c>isdisabled = true</c>, so a lookup that
    /// filters <c>isdisabled eq false</c> does not return it — as Dataverse would.
    /// </summary>
    private sealed class FakeDataverse : IAsyncDisposable
    {
        private static readonly IReadOnlyDictionary<string, (string SystemUserId, bool Disabled)> Users =
            new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                [OidA] = ("1576a000-0000-0000-0000-00000000f0a1", false),
                [OidB] = ("1576b000-0000-0000-0000-00000000f0b1", false),
                [DisabledOid] = ("1576d000-0000-0000-0000-00000000f0d1", true),
            };

        private readonly WebApplication _app;
        private ConcurrentQueue<SeenRequest> _requests = new();
        private int _heldLookupTarget;
        private int _lookupsArrived;
        private readonly TaskCompletionSource _lookupsReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private FakeDataverse(WebApplication app) => _app = app;

        public IReadOnlyCollection<SeenRequest> Requests => _requests.ToArray();

        public static async Task<FakeDataverse> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new FakeDataverse(app);
            app.Map("/api/data/v9.2/{**rest}", (HttpContext context) => fake.HandleAsync(context));
            await app.StartAsync();
            return fake;
        }

        public HttpClient CreateClient()
        {
            var client = _app.GetTestClient();
            // DataverseAccessDataSource addresses relative URLs, as its DI registration's BaseAddress does.
            client.BaseAddress = new Uri("http://localhost/api/data/v9.2/");
            client.Timeout = TimeSpan.FromSeconds(20);
            return client;
        }

        public void HoldLookupsUntil(int count) => _heldLookupTarget = count;

        /// <summary>Returns the requests seen so far and starts a fresh log.</summary>
        public IReadOnlyList<SeenRequest> Drain() => Interlocked.Exchange(ref _requests, new()).ToArray();

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var path = Uri.UnescapeDataString(context.Request.Path.Value ?? string.Empty)["/api/data/v9.2/".Length..];

            if (path == "systemusers")
            {
                var filter = context.Request.Query["$filter"].ToString();
                var oid = Users.Keys.Single(k => filter.Contains(k, StringComparison.OrdinalIgnoreCase));
                _requests.Enqueue(new SeenRequest("lookup", oid, authorization));

                if (_heldLookupTarget > 0)
                {
                    if (Interlocked.Increment(ref _lookupsArrived) >= _heldLookupTarget)
                    {
                        _lookupsReleased.TrySetResult();
                    }

                    await _lookupsReleased.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }

                var (systemUserId, disabled) = Users[oid];
                var filtersDisabled = filter.Contains("isdisabled eq false", StringComparison.OrdinalIgnoreCase);
                await WriteAsync(context, disabled && filtersDisabled
                    ? "{\"value\":[]}"
                    : $"{{\"value\":[{{\"systemuserid\":\"{systemUserId}\"}}]}}");
                return;
            }

            var owner = Users.Single(u => path.Contains(u.Value.SystemUserId, StringComparison.OrdinalIgnoreCase)).Key;
            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
            {
                _requests.Enqueue(new SeenRequest("rights", owner, authorization));
                await WriteAsync(context, "{\"AccessRights\":\"ReadAccess,WriteAccess\"}");
            }
            else if (path.EndsWith("/teammembership_association", StringComparison.Ordinal))
            {
                _requests.Enqueue(new SeenRequest("teams", owner, authorization));
                await WriteAsync(context, "{\"value\":[{\"teamid\":\"t-1\",\"name\":\"Team\"}]}");
            }
            else if (path.EndsWith("/systemuserroles_association", StringComparison.Ordinal))
            {
                _requests.Enqueue(new SeenRequest("roles", owner, authorization));
                await WriteAsync(context, "{\"value\":[{\"roleid\":\"r-1\",\"name\":\"Role\"}]}");
            }
            else
            {
                _requests.Enqueue(new SeenRequest("other:" + path, owner, authorization));
                context.Response.StatusCode = 404;
            }
        }

        private static async Task WriteAsync(HttpContext context, string json)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json, Encoding.UTF8);
        }
    }
}
