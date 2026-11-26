using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Events;
using CreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Events;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 006 (spec FR-15, FR-07): the must-not-leak proofs for the events read path. Two callers, A and B,
/// and an event private to A: B's list never contains it - on any page, and not in the count - and B's get on it is the uniform 404,
/// byte-identical to an id that does not exist.
/// </summary>
/// <remarks>
/// <para><b>What makes this a proof and not a restatement.</b> <see cref="IEventDataverseService"/> authenticates as the BFF's own
/// identity, so the boundary is whatever the BFF sends. Here it is the REAL <see cref="DataverseWebApiService"/> over
/// <see cref="FakeDataverse"/>, a hand-written Web API double that applies row security the way Dataverse does: a request
/// impersonating a user (<c>MSCRMCallerID</c>) sees only the rows that user may read, and its <c>@odata.count</c> counts only
/// those; an app-only request sees every row. A list that went out app-only, or as the wrong user, returns A's event and fails.
/// The record-rights question the get route asks is answered from the same table (<see cref="RowSecurityProbe"/>, the
/// <c>RetrievePrincipalAccess</c> seam), as the caller's own token. Everything else is the real route: <c>MapEventEndpoints</c>,
/// its filters and handlers. No <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1).</para>
/// <para>Controls: A sees A's event through the same list and get (must-not-fire), so the fake is not simply hiding it.</para>
/// </remarks>
public sealed class EventCallerScopeTests
{
    // Two signed-in callers: Entra object ids, Dataverse systemuserids and bearer tokens.
    internal static readonly Guid OidA = Guid.Parse("a0060000-0000-4000-8000-00000000000a");
    internal static readonly Guid OidB = Guid.Parse("a0060000-0000-4000-8000-00000000000b");
    internal static readonly Guid UserA = Guid.Parse("50060000-0000-4000-8000-00000000000a");
    internal static readonly Guid UserB = Guid.Parse("50060000-0000-4000-8000-00000000000b");

    internal static readonly Guid PrivateToA = Guid.Parse("e0060000-0000-4000-8000-0000000000aa");
    internal static readonly Guid SharedWithB = Guid.Parse("e0060000-0000-4000-8000-0000000000ab");
    internal static readonly Guid[] OwnedByB =
    [
        Guid.Parse("e0060000-0000-4000-8000-0000000000b1"),
        Guid.Parse("e0060000-0000-4000-8000-0000000000b2"),
        Guid.Parse("e0060000-0000-4000-8000-0000000000b3"),
    ];

    private static readonly Guid Absent = Guid.Parse("e0060000-0000-4000-8000-0000000000ff");

    /// <summary>A's private event, one A shares with B, and three of B's own.</summary>
    internal static FakeDataverse World()
    {
        var dataverse = new FakeDataverse();
        dataverse.Add(PrivateToA, "A's private hearing", readers: [UserA]);
        dataverse.Add(SharedWithB, "Shared filing", readers: [UserA, UserB]);
        foreach (var id in OwnedByB)
        {
            dataverse.Add(id, "B's work " + id.ToString("N")[^2..], readers: [UserB]);
        }

        return dataverse;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // REST: GET /api/v1/events
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_CallerB_NeverSeesAPrivateEventOfCallerA_OnAnyPageOrInTheCount()
    {
        await using var host = await CallerScopeHost.StartAsync(World());

        var seen = new List<Guid>();
        int? totalCount = null;
        for (var page = 1; ; page++)
        {
            var json = await host.GetJsonAsync("B", $"/api/v1/events?pageSize=1&pageNumber={page}");
            totalCount ??= json["totalCount"]!.GetValue<int>();
            json["totalCount"]!.GetValue<int>().Should().Be(totalCount, "every page reports the same trimmed count");
            seen.AddRange(json["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()));
            if (!json["hasNextPage"]!.GetValue<bool>())
            {
                break;
            }
        }

        seen.Should().NotContain(PrivateToA);
        seen.Should().BeEquivalentTo(OwnedByB.Append(SharedWithB), "B sees exactly what B may read");
        totalCount.Should().Be(4, "the count is B's trimmed count, not the table's 5 rows");
        host.Dataverse.ListQueries.Should().OnlyContain(q => q.ImpersonatedUser == UserB, "every page is queried AS B");
    }

    [Fact]
    public async Task List_CallerA_SeesTheirOwnPrivateEvent()
    {
        await using var host = await CallerScopeHost.StartAsync(World());

        var json = await host.GetJsonAsync("A", "/api/v1/events?pageSize=100");

        json["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).Should().BeEquivalentTo(new[] { PrivateToA, SharedWithB });
        json["totalCount"]!.GetValue<int>().Should().Be(2);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // REST: GET /api/v1/events/{id}
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_CallerBOnCallerAsPrivateEvent_IsTheUniform404_ByteIdenticalToAnAbsentId()
    {
        await using var host = await CallerScopeHost.StartAsync(World());

        using var forPrivate = await host.SendAsync("B", HttpMethod.Get, $"/api/v1/events/{PrivateToA}");
        using var forAbsent = await host.SendAsync("B", HttpMethod.Get, $"/api/v1/events/{Absent}");

        forPrivate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var privateBody = await forPrivate.Content.ReadAsStringAsync();
        Normalize(privateBody).Should().Be(Normalize(await forAbsent.Content.ReadAsStringAsync()));
        privateBody.Should().NotContain(PrivateToA.ToString("D")).And.NotContain("A's private hearing");
        JsonNode.Parse(privateBody)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.Dataverse.ReadsOf(PrivateToA).Should().Be(0, "the event is never read for a caller who may not read it");
    }

    [Fact]
    public async Task Get_CallerAOnTheirOwnPrivateEvent_Is200()
    {
        await using var host = await CallerScopeHost.StartAsync(World());

        var json = await host.GetJsonAsync("A", $"/api/v1/events/{PrivateToA}");

        json["id"]!.GetValue<Guid>().Should().Be(PrivateToA);
        json["subject"]!.GetValue<string>().Should().Be("A's private hearing");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The capability (IEventsCapability): what an MCP tool calls, with no endpoint filter in front of it
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Capability_List_CallerB_NeverSeesAPrivateEventOfCallerA_OnAnyPageOrInTheCount()
    {
        var world = new CapabilityWorld();

        var seen = new List<Guid>();
        int? totalCount = null;
        for (var page = 1; ; page++)
        {
            var result = await world.Capability.ListAsync(
                world.Request("B"), new EventListQuery { PageSize = 1, PageNumber = page }, CancellationToken.None);

            result.Status.Should().Be(EventCapabilityStatus.Ok);
            totalCount ??= result.Value!.TotalCount;
            result.Value!.TotalCount.Should().Be(totalCount, "every page reports the same trimmed count");
            seen.AddRange(result.Value.Items.Select(i => i.Id));
            if (!result.Value.HasNextPage)
            {
                break;
            }
        }

        seen.Should().NotContain(PrivateToA);
        seen.Should().BeEquivalentTo(OwnedByB.Append(SharedWithB), "B sees exactly what B may read");
        totalCount.Should().Be(4, "the count is B's trimmed count, not the table's 5 rows");
        world.Dataverse.ListQueries.Should().OnlyContain(q => q.ImpersonatedUser == UserB, "every page is queried AS B");
    }

    [Fact]
    public async Task Capability_Get_CallerBOnCallerAsPrivateEvent_IsNotFound_AsForAnAbsentId_AndNeverReadsIt()
    {
        var world = new CapabilityWorld();

        var forPrivate = await world.Capability.GetAsync(world.Request("B"), PrivateToA, CancellationToken.None);
        var forAbsent = await world.Capability.GetAsync(world.Request("B"), Absent, CancellationToken.None);

        forPrivate.Should().BeEquivalentTo(forAbsent, "a private event and an absent one get one answer");
        forPrivate.Status.Should().Be(EventCapabilityStatus.NotFound);
        forPrivate.Value.Should().BeNull();
        forPrivate.ReasonCode.Should().Be("sdap.access.deny.record_unavailable", "the uniform 404's reason code");
        world.Dataverse.ReadsOf(PrivateToA).Should().Be(0, "the event is never read for a caller who may not read it");
    }

    [Fact]
    public async Task Capability_Get_CallerAOnTheirOwnPrivateEvent_IsOk()
    {
        var world = new CapabilityWorld();

        var result = await world.Capability.GetAsync(world.Request("A"), PrivateToA, CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.Ok);
        result.Value!.Id.Should().Be(PrivateToA);
    }

    public static TheoryData<string> NoSignedInCaller => new() { "null context", "no principal claims", "unauthenticated identity" };

    [Theory]
    [MemberData(nameof(NoSignedInCaller))]
    public async Task Capability_WithoutASignedInCaller_IsUserContextRequired_ForEveryMethod_AndAsksDataverseNothing(string shape)
    {
        var world = new CapabilityWorld();
        world.Probe.HeldPrivileges.Add(EventEndpoints.CreateEventPrivilege);
        HttpContext? context = shape switch
        {
            "null context" => null,
            "no principal claims" => new DefaultHttpContext { User = new ClaimsPrincipal() },
            // Claims, and the bearer token, but no authentication type: not signed in.
            _ => world.Request("A", new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", OidA.ToString("D"))]))),
        };

        var list = await world.Capability.ListAsync(context, new EventListQuery(), CancellationToken.None);
        var get = await world.Capability.GetAsync(context, PrivateToA, CancellationToken.None);
        var create = await world.Capability.CreateAsync(context, new CreateEventRequest("Subject"), CancellationToken.None);
        var complete = await world.Capability.CompleteAsync(context, PrivateToA, CancellationToken.None);

        foreach (var (status, reasonCode) in new[]
        {
            (list.Status, list.ReasonCode), (get.Status, get.ReasonCode),
            (create.Status, create.ReasonCode), (complete.Status, complete.ReasonCode),
        })
        {
            status.Should().Be(EventCapabilityStatus.UserContextRequired);
            reasonCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        }

        world.Dataverse.Requests.Should().Be(0, "no Dataverse call without a signed-in caller");
        world.Probe.Calls.Should().Be(0, "not even an as-caller access question");
        world.CallerResolver.Invocations.Should().BeEmpty();
        world.UserClient.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Capability_Create_WithoutTheCreatePrivilege_IsRefusedWithRestsReason_AndWritesNothing()
    {
        var world = new CapabilityWorld(); // B holds no privilege

        var result = await world.Capability.CreateAsync(
            world.Request("B"), new CreateEventRequest("Subject"), CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.InsufficientPrivilege);
        result.ReasonCode.Should().Be(RestReasonCode("create.403.privilege"), "the same reason as POST /api/v1/events");
        world.AssertNothingWritten();
    }

    [Fact]
    public async Task Capability_Create_WithoutAppendToOnTheRegardingRecord_IsRefusedWithRestsReason_AndWritesNothing()
    {
        var world = new CapabilityWorld();
        world.Probe.HeldPrivileges.Add(EventEndpoints.CreateEventPrivilege); // Create held; no rights on the matter
        var matter = Guid.Parse("a7060000-0000-4000-8000-000000000001");

        var result = await world.Capability.CreateAsync(
            world.Request("B"),
            new CreateEventRequest("Subject", RegardingRecordId: matter, RegardingRecordType: Spaarke.Dataverse.RegardingRecordType.Matter),
            CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.Forbidden);
        result.ReasonCode.Should().Be(RestReasonCode("create.403.rights"), "the same reason as POST /api/v1/events");
        world.Probe.Calls.Should().Be(2, "the privilege, then the rights on the regarding record, both as the caller");
        world.AssertNothingWritten();
    }

    [Fact]
    public async Task Capability_Create_WithAnInvalidShape_IsInvalid_BeforeAnyAccessQuestion()
    {
        var world = new CapabilityWorld();

        var result = await world.Capability.CreateAsync(world.Request("B"), new CreateEventRequest("  "), CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.Invalid);
        result.Field.Should().Be("Subject");
        world.Probe.Calls.Should().Be(0, "a 400 is never turned into a 403");
        world.AssertNothingWritten();
    }

    [Fact]
    public async Task Capability_Complete_CallerBOnCallerAsPrivateEvent_IsNotFound_AndNeverReadsIt()
    {
        var world = new CapabilityWorld();

        var result = await world.Capability.CompleteAsync(world.Request("B"), PrivateToA, CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.NotFound);
        result.ReasonCode.Should().Be("sdap.access.deny.record_unavailable");
        world.Dataverse.ReadsOf(PrivateToA).Should().Be(0);
        world.AssertNothingWritten();
    }

    [Fact]
    public async Task Capability_Complete_WithReadButNotWrite_IsForbiddenWithRestsReason_AndWritesNothing()
    {
        var world = new CapabilityWorld();
        world.Probe.Grants[(UserB, EventEndpoints.EventEntitySet, SharedWithB)] = AccessRights.Read; // Read only

        var result = await world.Capability.CompleteAsync(world.Request("B"), SharedWithB, CancellationToken.None);

        result.Status.Should().Be(EventCapabilityStatus.Forbidden, "complete asks Write, not Read");
        result.ReasonCode.Should().Be(RestReasonCode("complete.403.read-only"), "the same reason as POST /api/v1/events/{id}/complete");
        world.Dataverse.ReadsOf(SharedWithB).Should().Be(0, "the event is not read before Write is established");
        world.AssertNothingWritten();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    internal static string Normalize(string body) =>
        Regex.Replace(body, "\"(traceId|correlationId)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"<volatile>\"");

    /// <summary>The <c>reasonCode</c> the REST route answered for a case, as its frozen golden file records it.</summary>
    private static string RestReasonCode(string caseName)
    {
        var path = Path.Combine(
            Sprk.Bff.Api.Tests.Integration.Contract.Signals.GoldenContract.BaselineDirectoryOf("Events"), caseName + ".json");
        using var golden = JsonDocument.Parse(File.ReadAllText(path));
        using var body = JsonDocument.Parse(golden.RootElement.GetProperty("body").GetString()!);
        return body.RootElement.GetProperty("reasonCode").GetString()!;
    }

    /// <summary>
    /// The real <see cref="EventsCapability"/> over the row-secured world. Everything a write would use is a strict double with no
    /// setup (or absent), so a refusal that reached a write would fault and fail the test rather than pass it.
    /// </summary>
    private sealed class CapabilityWorld
    {
        public CapabilityWorld()
        {
            Dataverse = World();
            Probe = new RowSecurityProbe(Dataverse);
            Entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string logicalName, CancellationToken _) => logicalName + "s");
            Capability = new EventsCapability(
                new OfflineDataverseService(Dataverse),
                new Mock<ICommunicationDataverseService>(MockBehavior.Strict).Object,
                CallerResolver.Object,
                NoLinkedContacts().Object,
                Entities.Object,
                recordCreation: null!, // never reached by these tests: a refusal that got this far would fault
                UserClient.Object,
                Ownership.Object,
                Probe,
                NullLogger<EventsCapability>.Instance);
        }

        public FakeDataverse Dataverse { get; }

        public RowSecurityProbe Probe { get; }

        public Mock<ICallerSystemUserResolver> CallerResolver { get; } = EventCallerScopeTests.CallerResolver();

        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);

        public Mock<IDataverseUserClient> UserClient { get; } = new(MockBehavior.Strict);

        public Mock<IRecordOwnershipResolver> Ownership { get; } = new(MockBehavior.Strict);

        public EventsCapability Capability { get; }

        /// <summary>A request from caller A or B: the signed-in principal and the caller's own bearer token.</summary>
        public HttpContext Request(string who, ClaimsPrincipal? principal = null)
        {
            var context = new DefaultHttpContext { User = principal ?? Principal(who) };
            context.Request.Headers.Authorization = "Bearer token-" + who;
            return context;
        }

        public void AssertNothingWritten()
        {
            UserClient.Invocations.Should().BeEmpty("no write as the caller");
            Ownership.Invocations.Should().BeEmpty("no owner decided for a write");
            Dataverse.ListQueries.Should().BeEmpty();
            Dataverse.Requests.Should().Be(0, "no app-only Dataverse call either");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The world: Dataverse with row security, the caller-rights probe over the same rows, two signed-in callers
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A Dataverse Web API double with row security: each <c>sprk_event</c> row has the systemusers who may read it. A request
    /// carrying <c>MSCRMCallerID</c> sees only that user's rows and counts only them; an app-only request sees every row (the
    /// BFF identity's view). Hand-written; it answers the two event reads the BFF makes and refuses anything else.
    /// </summary>
    internal sealed class FakeDataverse : HttpMessageHandler
    {
        private readonly SortedDictionary<Guid, (string Name, HashSet<Guid> Readers)> _rows = new();
        private readonly Dictionary<Guid, int> _reads = new();

        public List<(Guid? ImpersonatedUser, int Top)> ListQueries { get; } = new();

        /// <summary>Every request that reached this double.</summary>
        public int Requests { get; private set; }

        public void Add(Guid id, string name, Guid[] readers) => _rows[id] = (name, new HashSet<Guid>(readers));

        public bool CanRead(Guid user, Guid id) => _rows.TryGetValue(id, out var row) && row.Readers.Contains(user);

        public int ReadsOf(Guid id) => _reads.GetValueOrDefault(id);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Guid? caller = request.Headers.TryGetValues(DataverseImpersonation.CallerIdHeader, out var values)
                ? Guid.Parse(values.Single())
                : null;

            if (request.Method == HttpMethod.Get && path.EndsWith("/sprk_events", StringComparison.Ordinal))
            {
                var top = int.Parse(Regex.Match(query, @"[?&]\$top=(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                ListQueries.Add((caller, top));
                var visible = _rows.Where(r => caller is not { } user || r.Value.Readers.Contains(user)).ToList();
                var body = new JsonObject
                {
                    ["@odata.count"] = visible.Count,
                    ["value"] = new JsonArray(visible.Take(top).Select(r => (JsonNode)Row(r.Key, r.Value.Name)).ToArray()),
                };
                return Task.FromResult(Json(HttpStatusCode.OK, body));
            }

            var single = Regex.Match(path, @"/sprk_events\((?<id>[0-9a-fA-F-]{36})\)$");
            if (request.Method == HttpMethod.Get && single.Success)
            {
                var id = Guid.Parse(single.Groups["id"].Value);
                _reads[id] = _reads.GetValueOrDefault(id) + 1;
                var found = _rows.TryGetValue(id, out var row) && (caller is not { } user || row.Readers.Contains(user));
                return Task.FromResult(found ? Json(HttpStatusCode.OK, Row(id, row.Name)) : new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            throw new InvalidOperationException($"FakeDataverse does not answer {request.Method} {path}{query}.");
        }

        private static JsonObject Row(Guid id, string name) => new()
        {
            ["sprk_eventid"] = id.ToString("D"),
            ["sprk_eventname"] = name,
            ["statecode"] = 0,
            ["statuscode"] = EventStatusCode.Open,
            ["sprk_duedate"] = "2026-12-01",
            ["createdon"] = "2026-11-01T09:30:00Z",
            ["modifiedon"] = "2026-11-01T09:30:00Z",
        };

        private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    /// <summary>The real <see cref="DataverseWebApiService"/> over <see cref="FakeDataverse"/>, through its test constructor.</summary>
    internal sealed class OfflineDataverseService(FakeDataverse dataverse) : DataverseWebApiService(
        new HttpClient(dataverse),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        new NullMembershipCacheInvalidator(NullLogger<NullMembershipCacheInvalidator>.Instance),
        confidentialClients: null,
        credential: new StaticTokenCredential());

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("app-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token);
    }

    /// <summary>
    /// The caller-rights probe at its <c>RetrievePrincipalAccess</c> seam, answered from <see cref="FakeDataverse"/>'s row security for
    /// the user the caller's OWN token names: a reader holds Read and Write, anyone else nothing. Records every question.
    /// </summary>
    internal sealed class RowSecurityProbe(FakeDataverse dataverse) : CallerRecordAccessProbe(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public int Calls { get; private set; }

        public HashSet<string> HeldPrivileges { get; } = new(StringComparer.Ordinal);

        public Dictionary<(Guid User, string Set, Guid Id), AccessRights> Grants { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Calls++;
            if (UserOf(callerBearerToken) is not { } user)
            {
                return Task.FromResult(AccessRights.None);
            }

            if (Grants.TryGetValue((user, entitySet, recordId), out var granted))
            {
                return Task.FromResult(granted);
            }

            return Task.FromResult(entitySet == EventEndpoints.EventEntitySet && dataverse.CanRead(user, recordId)
                ? AccessRights.Read | AccessRights.Write
                : AccessRights.None);
        }

        public override Task<bool> CallerHoldsPrivilegeAsync(string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(UserOf(callerBearerToken) is not null && HeldPrivileges.Contains(privilegeName));
        }

        private static Guid? UserOf(string? token) => token switch
        {
            "token-A" => UserA,
            "token-B" => UserB,
            _ => null,
        };
    }

    /// <summary>Signs in caller A or B (header <see cref="CallerHeader"/>): an authenticated principal with that caller's oid.</summary>
    internal sealed class TwoCallerAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "EventCallerScope";
        public const string CallerHeader = "X-Test-Caller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(CallerHeader, out var who))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = Principal(who.ToString());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    /// <summary>Caller A or B as an authenticated principal (the oid claim the resolver keys on).</summary>
    internal static ClaimsPrincipal Principal(string who) =>
        new(new ClaimsIdentity([new Claim("oid", (who == "A" ? OidA : OidB).ToString("D"))], TwoCallerAuthHandler.SchemeName));

    /// <summary>Resolves the request's oid to that caller's systemuserid, as the real resolver does from the token's claims.</summary>
    internal static Mock<ICallerSystemUserResolver> CallerResolver()
    {
        var resolver = new Mock<ICallerSystemUserResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClaimsPrincipal? user, CancellationToken _) => user?.FindFirst("oid")?.Value switch
            {
                var oid when oid == OidA.ToString("D") => CallerSystemUserResolution.Resolved(UserA.ToString("D")),
                var oid when oid == OidB.ToString("D") => CallerSystemUserResolution.Resolved(UserB.ToString("D")),
                _ => CallerSystemUserResolution.Unresolved("no-oid-claim"),
            });
        return resolver;
    }

    /// <summary>The identity service: neither caller has a linked contact (the "mine" scope is owner or creator).</summary>
    internal static Mock<IIdentityNormalizationService> NoLinkedContacts()
    {
        var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Strict);
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(id, ContactId: null));
        return identity;
    }

    /// <summary>The REAL <c>MapEventEndpoints</c> over the row-secured world. Services the read routes never reach are registered
    /// (so the route set builds) behind factories that throw if a test ever reaches them.</summary>
    internal sealed class CallerScopeHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        private CallerScopeHost(FakeDataverse dataverse)
        {
            Dataverse = dataverse;
            Probe = new RowSecurityProbe(dataverse);
        }

        public FakeDataverse Dataverse { get; }

        public RowSecurityProbe Probe { get; }

        public static async Task<CallerScopeHost> StartAsync(FakeDataverse dataverse)
        {
            var host = new CallerScopeHost(dataverse);
            await host.InitializeAsync();
            return host;
        }

        private async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TwoCallerAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TwoCallerAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TwoCallerAuthHandler>(TwoCallerAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

            builder.Services.AddSingleton<IEventDataverseService>(new OfflineDataverseService(Dataverse));
            builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
            builder.Services.AddSingleton(CallerResolver().Object);
            builder.Services.AddSingleton(NoLinkedContacts().Object);
            builder.Services.AddSingleton(new Mock<ICommunicationDataverseService>(MockBehavior.Strict).Object);
            builder.Services.AddSingleton(new Mock<IGenericEntityService>(MockBehavior.Strict).Object);
            builder.Services.AddSingleton(new Mock<IDataverseUserClient>(MockBehavior.Strict).Object);
            builder.Services.AddSingleton(new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object);
            builder.Services.AddSingleton<RecordCreationService>(_ => throw new InvalidOperationException("not on the read path"));
            builder.Services.AddSingleton<CoreAncestorRestamper>(_ => throw new InvalidOperationException("not on the read path"));
            builder.Services.AddSingleton<Sprk.Bff.Api.Services.Access.SecureChildReconciler>(_ => throw new InvalidOperationException("not on the read path"));

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapEventEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(string who, HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add(TwoCallerAuthHandler.CallerHeader, who);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-" + who);
            return _client!.SendAsync(request);
        }

        public async Task<JsonObject> GetJsonAsync(string who, string url)
        {
            using var response = await SendAsync(who, HttpMethod.Get, url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
