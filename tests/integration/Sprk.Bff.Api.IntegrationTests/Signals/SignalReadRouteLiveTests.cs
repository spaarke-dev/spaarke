// spaarke-ontology-platform-r1 task 038 — LIVE, READ-ONLY proof of GET /api/v1/signals/worklist as REAL low-privilege users
// against a real Dataverse environment (ADR-038: authorization cases run as real users, the 097/046 pattern).
//
// Opt-in only (skip-via-attribute, the repo's convention): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_SIGNALS_DATAVERSE_URL=https://<env>.crm.dynamics.com     (spaarkedev1)
//   SPAARKE_LIVE_SIGNALS_READER_ID=<systemuserid of a real non-admin user>  (e.g. a Spaarke Console User + Core User)
//   SPAARKE_LIVE_SIGNALS_OTHER_BU_ID=<systemuserid of a real non-admin user in ANOTHER business unit>
// and an `az login` session for an identity that may impersonate (System Administrator).
//
// NOTHING IS WRITTEN. The route only reads; the harness only reads. Signal rows on the environment are not required:
//   Leg 1 runs the route over the environment's REAL Signals and the REAL worklist row (id ...5010) for every set, and checks
//         that every core record returned is one the acting user can read (asked separately, as that user).
//   Leg 2 replaces ONLY the Signal query's answer with synthetic Signal rows that point at REAL records chosen live (a matter
//         the reader can read and the other-BU user cannot; an event that is the reader's own work and one the reader can read
//         but is not theirs). Every other question — WhoAmI aside — goes to Dataverse AS THE ACTING USER, so Dataverse decides
//         every core-record and subject read for that real user.
//
// WHAT IS SUBSTITUTED, AND WHY (as in WorkAssignments/WorkAssignmentCreateLiveTests):
//   (1) the inbound TOKEN — a fake scheme carrying the acting user's REAL Entra oid (a workstation cannot mint a user token for
//       the BFF audience);
//   (2) IDataverseUserClient's OBO exchange — the operator's `az login` Dataverse token with MSCRMCallerID = the acting user; the
//       error mapping is the production one (DataverseUserClient.MapErrorCode). WhoAmI ignores MSCRMCallerID, so it is answered
//       with the acting user (what the user's own token answers);
//   (3) the BFF's app-only identity — AzureCliCredential instead of the managed identity (catalog, configuration, metadata).
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed class SignalReadRouteLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_SIGNALS_DATAVERSE_URL";
    private const string Route = "/api/v1/signals/worklist";

    private sealed class LiveSignalsFactAttribute : FactAttribute
    {
        public LiveSignalsFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVar)))
                Skip = $"Live leg: set {UrlVar}, SPAARKE_LIVE_SIGNALS_READER_ID and SPAARKE_LIVE_SIGNALS_OTHER_BU_ID to run it (read-only).";
        }
    }

    private readonly ITestOutputHelper _out;

    public SignalReadRouteLiveTests(ITestOutputHelper output) => _out = output;

    [LiveSignalsFact]
    public async Task LiveMode_RealSignals_RealWorklistRow_EveryReturnedCoreIsReadableByTheCaller()
    {
        var (factory, reader, _) = await StartAsync();
        await using var _f = factory;
        await factory.ActAsAsync(reader);
        using var bff = Client(factory);

        foreach (var set in new[] { "lanes", "closedToday", "suppressed", "closedEarlier" })
        {
            var response = await bff.GetAsync($"{Route}?set={set}");
            var text = await response.Content.ReadAsStringAsync();
            _out.WriteLine($"GET {Route}?set={set} as {reader} -> {(int)response.StatusCode} {text[..Math.Min(text.Length, 300)]}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, text);

            using var doc = JsonDocument.Parse(text);
            foreach (var group in doc.RootElement.GetProperty("lanes").EnumerateArray().SelectMany(l => l.GetProperty("groups").EnumerateArray()))
            {
                if (group.GetProperty("core") is { ValueKind: JsonValueKind.Object } core)
                {
                    var table = core.GetProperty("recordType").GetString()!;
                    var id = core.GetProperty("recordId").GetGuid();
                    (await factory.CanReadAsync(reader, $"{table}s", id)).Should().BeTrue($"{table} {id} was returned to {reader}");
                }
            }
        }
    }

    [LiveSignalsFact]
    public async Task LiveMode_SyntheticSignalsOverRealRecords_RealUsersSeeOnlyWhatTheyMayRead()
    {
        var (factory, reader, otherBu) = await StartAsync();
        await using var _f = factory;

        // Real records, chosen live as the real users (read-only).
        var readerMatters = await factory.ReadableIdsAsync(reader, "sprk_matters", "sprk_matterid", 200);
        var otherMatters = await factory.ReadableIdsAsync(otherBu, "sprk_matters", "sprk_matterid", 5000);
        var shared = readerMatters.FirstOrDefault(otherMatters.Contains);
        var readerOnly = readerMatters.FirstOrDefault(m => !otherMatters.Contains(m));
        readerOnly.Should().NotBeEmpty("the environment needs a matter the reader can read and the other-BU user cannot");

        var contact = await factory.ContactOfAsync(reader);
        var mineFilter = $"_ownerid_value eq {reader:D} or _sprk_createdbyperson_value eq {reader:D}"
            + (contact is { } c ? $" or _sprk_assignedto_value eq {c:D}" : string.Empty);
        var myEvent = (await factory.ReadableIdsAsync(reader, "sprk_events", "sprk_eventid", 1, $"&$filter={mineFilter}")).FirstOrDefault();
        var notMineEvent = (await factory.ReadableIdsAsync(reader, "sprk_events", "sprk_eventid", 1, $"&$filter=not ({mineFilter})")).FirstOrDefault();
        _out.WriteLine($"reader-only matter {readerOnly}; shared matter {shared}; my event {myEvent}; readable-not-mine event {notMineEvent}");

        var matterType = await factory.CatalogIdAsync("sprk_matter");
        var eventType = await factory.CatalogIdAsync("sprk_event");
        var onReaderOnly = Guid.NewGuid();
        var onShared = Guid.NewGuid();
        var doMine = Guid.NewGuid();
        var doNotMine = Guid.NewGuid();
        factory.SyntheticSignals = new List<object>
        {
            Row(onReaderOnly, 100000000, matterType, readerOnly, matterType, readerOnly),
            Row(onShared, 100000000, matterType, shared, matterType, shared),
            Row(doMine, 100000001, matterType, readerOnly, eventType, myEvent),
            Row(doNotMine, 100000001, matterType, readerOnly, eventType, notMineEvent),
        };

        // The reader: both Decide rows they can read; in the Do lane only their own event.
        await factory.ActAsAsync(reader);
        using (var bff = Client(factory))
        {
            var ids = await IdsAsync(bff);
            ids.Should().Contain(onReaderOnly);
            if (shared != Guid.Empty) ids.Should().Contain(onShared);
            if (myEvent != Guid.Empty) ids.Should().Contain(doMine, "the reader's own event (owner, assignee contact or creator)");
            ids.Should().NotContain(doNotMine, "an event the reader can read but that is not their work stays out of their Do lane");
        }

        // The user in another business unit: never the Signal on the matter they cannot read, never someone else's Do row.
        await factory.ActAsAsync(otherBu);
        using (var bff = Client(factory))
        {
            var response = await bff.GetAsync(Route);
            var raw = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
            raw.Should().NotContain(onReaderOnly.ToString("D")).And.NotContain(readerOnly.ToString("D"))
                .And.NotContain(doMine.ToString("D")).And.NotContain(doNotMine.ToString("D"));
            // The oracle, asked separately as that user. The worklist query links sprk_policy (outer), and Dataverse refuses the WHOLE
            // query (0x80040220) for a user without Read on sprk_policy even when they read sprk_signal (verified live 2026-10-14:
            // Spaarke Basic + Core User, no Console User). Such a user gets an empty list (FR-54), never an error and never a row.
            using var signalProbe = await factory.SendAsync(HttpMethod.Get, "sprk_signals?$select=sprk_signalid&$top=1", otherBu);
            using var policyProbe = await factory.SendAsync(HttpMethod.Get, "sprk_policies?$select=sprk_policyid&$top=1", otherBu);
            var runsWorklist = signalProbe.IsSuccessStatusCode && policyProbe.IsSuccessStatusCode;
            _out.WriteLine($"other-BU user reads sprk_signal {(int)signalProbe.StatusCode}, sprk_policy {(int)policyProbe.StatusCode}");
            if (runsWorklist && shared != Guid.Empty) raw.Should().Contain(onShared.ToString("D"));
            if (!runsWorklist) raw.Should().NotContain("\"items\":[{", "a user who cannot run the worklist query gets an empty list");
        }

        // A caller with no Dataverse identity: the single 403.
        factory.ActAsUnresolved();
        using (var bff = Client(factory))
        {
            var response = await bff.GetAsync(Route);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        }
    }

    private static object Row(Guid id, int lane, Guid coreType, Guid core, Guid subjectType, Guid subject) => new Dictionary<string, object?>
    {
        ["sprk_signalid"] = id.ToString("D"),
        ["sprk_name"] = "zz-038 synthetic (never written)",
        ["sprk_lane"] = lane,
        ["sprk_signalstatus"] = 100000000,
        ["sprk_firstdetected"] = "2026-10-01T00:00:00Z",
        ["_sprk_corerecordtype_value"] = coreType.ToString("D"),
        ["sprk_corerecordid"] = core.ToString("D"),
        ["_sprk_regardingrecordtype_value"] = subjectType.ToString("D"),
        ["sprk_regardingrecordid"] = subject.ToString("D"),
        ["_sprk_policyversion_value"] = Guid.NewGuid().ToString("D"),
        ["policy.sprk_severity"] = 100000001,
    };

    private static async Task<List<Guid>> IdsAsync(HttpClient bff)
    {
        var response = await bff.GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("lanes").EnumerateArray().SelectMany(l => l.GetProperty("groups").EnumerateArray())
            .SelectMany(g => g.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("signalId").GetGuid()).ToList();
    }

    private static HttpClient Client(LiveSignalsFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");
        return client;
    }

    private async Task<(LiveSignalsFactory Factory, Guid Reader, Guid OtherBu)> StartAsync()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar)!;
        var reader = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_SIGNALS_READER_ID")!);
        var otherBu = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_SIGNALS_OTHER_BU_ID")!);
        var factory = new LiveSignalsFactory(url, new AzureCliCredential(), _out);
        await factory.InitAsync();
        return (factory, reader, otherBu);
    }

    private sealed class LiveSignalsFactory : DataverseIntegrationTestFixture
    {
        private readonly TokenCredential _credential;
        private readonly ITestOutputHelper _out;
        private static readonly HttpClient Http = new();

        public LiveSignalsFactory(string dataverseUrl, TokenCredential credential, ITestOutputHelper output)
        {
            _credential = credential;
            _out = output;
            DataverseUrl = dataverseUrl.TrimEnd('/');
            LiveConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = DataverseUrl,
                ["Graph:ManagedIdentity:Enabled"] = "true",
            }).Build();
        }

        public string DataverseUrl { get; }
        public IConfiguration LiveConfiguration { get; }
        public Guid? CallerSystemUserId { get; private set; }
        public Guid? OperatorSystemUserId { get; private set; }
        public string? CallerObjectId { get; private set; }
        public bool Unresolved { get; private set; }

        /// <summary>When set, the answer to the Signal query (only); everything else is Dataverse's, as the acting user.</summary>
        public List<object>? SyntheticSignals { get; set; }

        public async Task InitAsync()
        {
            using var who = await SendAsync(HttpMethod.Get, "WhoAmI()", null);
            OperatorSystemUserId = JsonDocument.Parse(await who.Content.ReadAsStringAsync()).RootElement.GetProperty("UserId").GetGuid();
        }

        public async Task ActAsAsync(Guid systemUserId)
        {
            using var user = await SendAsync(HttpMethod.Get, $"systemusers({systemUserId})?$select=azureactivedirectoryobjectid", null);
            CallerObjectId = JsonDocument.Parse(await user.Content.ReadAsStringAsync()).RootElement.GetProperty("azureactivedirectoryobjectid").GetString();
            CallerSystemUserId = systemUserId;
            Unresolved = false;
            _out.WriteLine($"acting as systemuser {systemUserId} (oid {CallerObjectId})");
        }

        public void ActAsUnresolved()
        {
            CallerObjectId = Guid.NewGuid().ToString("D");
            CallerSystemUserId = null;
            Unresolved = true;
        }

        public async Task<bool> CanReadAsync(Guid user, string entitySet, Guid id)
        {
            using var response = await SendAsync(HttpMethod.Get, $"{entitySet}({id})?$select=createdon", user);
            return response.IsSuccessStatusCode;
        }

        public async Task<List<Guid>> ReadableIdsAsync(Guid user, string entitySet, string pk, int top, string extra = "")
        {
            using var response = await SendAsync(HttpMethod.Get, $"{entitySet}?$select={pk}&$top={top}{extra}", user);
            var text = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(text);
            return JsonDocument.Parse(text).RootElement.GetProperty("value").EnumerateArray().Select(r => r.GetProperty(pk).GetGuid()).ToList();
        }

        public async Task<Guid?> ContactOfAsync(Guid user)
        {
            using var response = await SendAsync(HttpMethod.Get, $"systemusers({user})?$select=_sprk_primarycontact_value", null);
            var row = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return row.TryGetProperty("_sprk_primarycontact_value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetGuid() : null;
        }

        public async Task<Guid> CatalogIdAsync(string logicalName)
        {
            using var response = await SendAsync(HttpMethod.Get,
                $"sprk_recordtype_refs?$select=sprk_recordtype_refid&$filter=sprk_recordlogicalname eq '{logicalName}' and statecode eq 0", null);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value")
                .EnumerateArray().Single().GetProperty("sprk_recordtype_refid").GetGuid();
        }

        internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, Guid? asUser)
        {
            if (method != HttpMethod.Get)
                throw new InvalidOperationException("This live leg is read-only.");
            var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { $"{DataverseUrl}/.default" }), default);
            var request = new HttpRequestMessage(method, $"{DataverseUrl}/api/data/v9.2/{relativePath.TrimStart('/')}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (asUser is { } u && u != OperatorSystemUserId)
                request.Headers.Add("MSCRMCallerID", u.ToString("D"));
            return await Http.SendAsync(request);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataverseService>();
                services.AddSingleton<IDataverseService>(sp => new DataverseServiceClientImpl(
                    LiveConfiguration, sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>(),
                    confidentialClients: null, managedIdentityCredential: _credential));
                services.RemoveAll<TokenCredential>();
                services.AddSingleton<TokenCredential>(_credential);
                services.RemoveAll<IDataverseUserClient>();
                services.AddScoped<IDataverseUserClient>(_ => new LiveUserClient(this));
                services.AddSingleton(this);
                services.AddAuthentication()
                    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, LiveUserAuthHandler>(LiveUserAuthHandler.SchemeName, _ => { });
                services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = LiveUserAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = LiveUserAuthHandler.SchemeName;
                });
            });
        }

        internal void Trace(string line) => _out.WriteLine(line);
    }

    /// <summary>The caller's Dataverse with the OBO exchange substituted (MSCRMCallerID); GET only — the route never writes.</summary>
    private sealed class LiveUserClient(LiveSignalsFactory factory) : IDataverseUserClient
    {
        public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            var path = relativePath.TrimStart('/');
            if (path == "WhoAmI()")
            {
                return factory.Unresolved || factory.CallerSystemUserId is not { } caller
                    ? DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "No Dataverse user for this token.")
                    : DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { UserId = caller.ToString("D") }));
            }

            using var response = await factory.SendAsync(HttpMethod.Get, path, factory.CallerSystemUserId);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            factory.Trace($"    as-caller GET {path.Split('?')[0]} -> {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                return DataverseUserResponse.Fail((int)response.StatusCode, DataverseUserClient.MapErrorCode((int)response.StatusCode), text);
            }

            if (path.StartsWith("sprk_signals?fetchXml=", StringComparison.Ordinal) && factory.SyntheticSignals is { } synthetic)
            {
                // The REAL query ran as the real user (status checked above); its answer is replaced by the synthetic rows.
                return DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = synthetic }));
            }

            using var doc = JsonDocument.Parse(text);
            return DataverseUserResponse.Ok((int)response.StatusCode, doc.RootElement.Clone());
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("read-only");
    }

    private sealed class LiveUserAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        LiveSignalsFactory factory)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "LiveUser";

        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(factory.CallerObjectId is { } oid
                ? Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
                    new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim("oid", oid), new System.Security.Claims.Claim("tid", "live") }, SchemeName)),
                    SchemeName))
                : Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("no acting user"));
    }
}
