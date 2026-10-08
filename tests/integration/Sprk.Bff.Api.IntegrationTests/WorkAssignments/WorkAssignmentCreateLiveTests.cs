// spaarke-ontology-platform-r1 task 046 (D-21, D-59) — LIVE parity proof of the server-side work-assignment create against
// a real Dataverse environment, through the REAL route.
//
// Opt-in only (skip-via-return, the repo's convention — see Events/EventRoutesLiveTests): a plain `dotnet test` touches no
// network. Enable with
//   SPAARKE_LIVE_WA_DATAVERSE_URL=https://<env>.crm.dynamics.com          (spaarkedev1)
//   SPAARKE_LIVE_WA_MATTER_ID=<an ordinary sprk_matter the operator can append to>
//   SPAARKE_LIVE_WA_DENIED_USER_ID=<systemuserid of a real user WITHOUT AppendTo on DENIED_MATTER>   (optional leg)
//   SPAARKE_LIVE_WA_DENIED_MATTER_ID=<a sprk_matter that user cannot append to>                     (optional leg)
// and an `az login` session for an identity that may create sprk_workassignment and impersonate (System Administrator).
//
// WHAT IS REAL: the BFF host in-process (routing, binding, RequireAuthorization, WorkAssignmentEndpoints.CreateAsync), the
// PRODUCTION WorkAssignmentCreateService, OwnedChildWrite (G5), DataverseWriteItemMapper, CallerSystemUserResolver,
// RecordOwnershipResolver (I-6), SecureRootFilingGate / SecureRootInheritance's plan, the production SDK client
// (DataverseServiceClientImpl) for every app-only read, and the production DataverseWebApiService for the app-only create.
// Dataverse makes every allow/deny decision for real systemusers.
//
// WHAT IS SUBSTITUTED, AND WHY:
//   (1) the inbound TOKEN — a fake scheme whose identity carries the acting user's REAL Entra oid (an operator workstation
//       cannot mint a user token for the BFF audience);
//   (2) IDataverseUserClient's OBO exchange — the operator's own `az login` Dataverse token, with MSCRMCallerID naming the
//       acting user when it is not the operator, so Dataverse answers every as-the-caller question FOR THAT USER. The
//       response mapping is the production one (DataverseUserClient.MapErrorCode);
//   (3) the BFF's outbound identity — AzureCliCredential instead of the managed identity. No client secret is used.
//
// THE BROWSER LEG is the wizard's own write: the same payload POSTed to sprk_workassignments as the operator (what
// Xrm.WebApi.createRecord sends). The two rows are compared field by field (notes/046-work-assignment-parity.md).
//
// Every record is named "zz-046-test …" and deleted in `finally`; ids are registered BEFORE anything is asserted, every
// delete is checked, and a leak fails the test.
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

namespace Sprk.Bff.Api.IntegrationTests.WorkAssignments;

public sealed class WorkAssignmentCreateLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_WA_DATAVERSE_URL";
    private const string Route = "/api/v1/record-creation/workassignment";
    private readonly ITestOutputHelper _out;

    public WorkAssignmentCreateLiveTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task LiveMode_WorkAssignmentCreate_MatchesTheWizardField_ByField_AndRefusesACallerWithoutAppendTo()
    {
        var dataverseUrl = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(dataverseUrl))
            return; // not opted in

        var matterId = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_MATTER_ID")!);
        var deniedUser = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_DENIED_USER_ID"), out var du) ? du : (Guid?)null;
        var deniedMatter = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_DENIED_MATTER_ID"), out var dm) ? dm : (Guid?)null;
        var credential = new AzureCliCredential();

        using var dv = await DataverseClientAsync(dataverseUrl, credential);
        var operatorId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
        _out.WriteLine($"operator systemuser (WhoAmI) = {operatorId}");

        await using var factory = new LiveWorkAssignmentFactory(dataverseUrl, credential, _out);
        await factory.ActAsAsync(operatorId);
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var created = new List<Guid>();
        var leaks = new List<string>();
        try
        {
            var payload = await WizardPayloadAsync(dv, matterId, operatorId, $"zz-046-test parity {DateTime.UtcNow:yyyyMMddHHmmss}");
            _out.WriteLine($"wizard payload: {JsonSerializer.Serialize(payload)}");

            // ── 1. The browser path: the wizard's createRecord, as the operator ─────────────────────────────────────
            var browserId = await CreateAsUserAsync(dv, payload, created);
            _out.WriteLine($"browser path created {browserId}");

            // ── 2. The BFF path: the same payload through the real route, as the same operator ────────────────────
            var post = await bff.PostAsJsonAsync(Route, payload);
            var bffId = await RegisterCreatedAsync(post, created);
            _out.WriteLine($"POST {Route} -> HTTP {(int)post.StatusCode} {await post.Content.ReadAsStringAsync()}");
            post.StatusCode.Should().Be(HttpStatusCode.Created);

            // ── 3. Field by field ───────────────────────────────────────────────────────────────────────────────────
            var browser = await ReadAsync(dv, browserId);
            var server = await ReadAsync(dv, bffId);
            _out.WriteLine($"browser row: {browser}");
            _out.WriteLine($"server row:  {server}");

            foreach (var column in ParityColumns)
            {
                Value(server, column).Should().Be(Value(browser, column), $"parity table: {column} matches the wizard's");
            }

            // Intended differences (rows 13-16, accepted by D-59).
            var matterTeam = await DefaultOwnerTeamOfRecordAsync(dv, "sprk_matters", matterId);
            Value(browser, "_ownerid_value").Should().Be(operatorId.ToString("D"), "the wizard's create is owned by the user");
            Value(server, "_ownerid_value").Should().Be(matterTeam.ToString("D"), "I-6 record-first: the matter's business-unit team");
            Value(server, "_owningbusinessunit_value").Should().Be(Value(await ReadMatterAsync(dv, matterId), "_owningbusinessunit_value"));
            Value(browser, "_sprk_createdbyperson_value").Should().BeNull("the wizard never stamps the creator person");
            Value(server, "_sprk_createdbyperson_value").Should().Be(operatorId.ToString("D"), "the app-only create records the person");
            Value(server, "sprk_issecure").Should().NotBe("True", "filed under an ordinary matter: not secured");

            // ── 4. A caller WITHOUT AppendTo on the parent is refused, and nothing is created (D-59) ──────────────────
            if (deniedUser is { } user && deniedMatter is { } denied)
            {
                await factory.ActAsAsync(user);
                var before = await CountZzAsync(dv);
                var deniedPayload = await WizardPayloadAsync(dv, denied, operatorId, "zz-046-test denied");
                var refused = await bff.PostAsJsonAsync(Route, deniedPayload);
                await RegisterCreatedAsync(refused, created); // registered in case it was (wrongly) created
                _out.WriteLine($"POST {Route} as {user} -> HTTP {(int)refused.StatusCode} {await refused.Content.ReadAsStringAsync()}");
                refused.StatusCode.Should().Be(HttpStatusCode.NotFound, "no AppendTo on the regarding matter is the uniform 404");
                (await CountZzAsync(dv)).Should().Be(before, "a refused create writes nothing");
                await factory.ActAsAsync(operatorId);
            }
            else
            {
                _out.WriteLine("denied-caller leg not configured (SPAARKE_LIVE_WA_DENIED_USER_ID / _MATTER_ID)");
            }
        }
        finally
        {
            foreach (var id in created)
                leaks.AddRange(await DeleteAsync(dv, $"sprk_workassignments({id})"));
            var remaining = await CountZzAsync(dv);
            _out.WriteLine($"cleanup: deleted {created.Count - leaks.Count} of {created.Count}; zz-046 rows remaining = {remaining}");
            leaks.Should().BeEmpty("every zz-046 row is removed");
            remaining.Should().Be(0, "no zz-046 row is left behind");
        }
    }

    /// <summary>Every column the wizard writes and both paths must agree on (parity table rows 1-12).</summary>
    private static readonly string[] ParityColumns =
    {
        "sprk_name", "sprk_priority", "sprk_description", "sprk_responseduedate", "sprk_searchindexname",
        "_sprk_ai_search_index_value", "_sprk_regardingmatter_value", "_sprk_regardingrecordtype_value",
        "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl",
        "_sprk_mattertype_value", "_sprk_practicearea_value", "sprk_containerid", "statecode", "statuscode",
    };

    /// <summary>
    /// The payload the wizard builds (workAssignmentService.ts createWorkAssignment): name, priority, description, due
    /// date; the user's business-unit search defaults; the regarding matter with the ADR-024 resolver fields
    /// (applyResolverFields); the matter's type and practice area. Navigation properties are read from metadata, as the
    /// wizard's discoverNavProps does.
    /// </summary>
    private static async Task<Dictionary<string, object?>> WizardPayloadAsync(HttpClient dv, Guid matterId, Guid userId, string name)
    {
        var nav = await NavPropsAsync(dv, "sprk_workassignment");
        var matter = await ReadMatterAsync(dv, matterId);
        var recordType = (await dv.GetFromJsonAsync<JsonElement>(
                "sprk_recordtype_refs?$select=sprk_recordtype_refid&$filter=sprk_recordlogicalname eq 'sprk_matter' and statecode eq 0"))
            .GetProperty("value").EnumerateArray().First().GetProperty("sprk_recordtype_refid").GetString();

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_priority"] = 100000001,
            ["sprk_description"] = "Live parity check (task 046)",
            ["sprk_responseduedate"] = "2026-10-21",
            [$"{nav["sprk_regardingmatter"]}@odata.bind"] = $"/sprk_matters({matterId:D})",
            [$"{nav["sprk_regardingrecordtype"]}@odata.bind"] = $"/sprk_recordtype_refs({recordType})",
            ["sprk_regardingrecordid"] = matterId.ToString("D"),
            ["sprk_regardingrecordname"] = Value(matter, "sprk_mattername"),
            ["sprk_regardingrecordnumber"] = Value(matter, "sprk_matternumber"),
            ["sprk_regardingrecordurl"] = $"main.aspx?etn=sprk_matter&id={matterId:D}&pagetype=entityrecord",
        };
        if (Value(matter, "_sprk_mattertype_value") is { } type)
            payload[$"{nav["sprk_mattertype"]}@odata.bind"] = $"/sprk_mattertype_refs({type})";
        if (Value(matter, "_sprk_practicearea_value") is { } area)
            payload[$"{nav["sprk_practicearea"]}@odata.bind"] = $"/sprk_practicearea_refs({area})";

        // The user's business-unit search defaults (EntityCreationService.resolveUserBuDefaults / applyUserBuDefaults).
        var bu = Value(await dv.GetFromJsonAsync<JsonElement>($"systemusers({userId})?$select=_businessunitid_value"), "_businessunitid_value");
        var unit = await dv.GetFromJsonAsync<JsonElement>($"businessunits({bu})?$select=sprk_searchindexname,_sprk_ai_search_index_value");
        if (Value(unit, "sprk_searchindexname") is { Length: > 0 } index)
            payload["sprk_searchindexname"] = index;
        if (Value(unit, "_sprk_ai_search_index_value") is { } indexId)
            payload[$"{nav["sprk_ai_search_index"]}@odata.bind"] = $"/sprk_aisearchindexes({indexId})";
        return payload;
    }

    private static async Task<Dictionary<string, string>> NavPropsAsync(HttpClient dv, string table)
    {
        var body = await dv.GetFromJsonAsync<JsonElement>(
            $"EntityDefinitions(LogicalName='{table}')/ManyToOneRelationships?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName");
        return body.GetProperty("value").EnumerateArray()
            .GroupBy(r => r.GetProperty("ReferencingAttribute").GetString()!)
            .ToDictionary(g => g.Key, g => g.First().GetProperty("ReferencingEntityNavigationPropertyName").GetString()!);
    }

    private static async Task<Guid> CreateAsUserAsync(HttpClient dv, Dictionary<string, object?> payload, List<Guid> created)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await dv.PostAsync("sprk_workassignments", content);
        if (response.Headers.TryGetValues("OData-EntityId", out var values))
            created.Add(Guid.Parse(values.First().Split('(', ')')[1]));
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return created[^1];
    }

    /// <summary>Registers a created id for cleanup BEFORE any assertion about the response.</summary>
    private static async Task<Guid> RegisterCreatedAsync(HttpResponseMessage response, List<Guid> created)
    {
        if (response.StatusCode != HttpStatusCode.Created)
            return Guid.Empty;
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Add(id);
        return id;
    }

    private static async Task<JsonElement> ReadAsync(HttpClient dv, Guid id) =>
        await dv.GetFromJsonAsync<JsonElement>(
            $"sprk_workassignments({id})?$select={string.Join(',', ParityColumns)},_ownerid_value,_owningbusinessunit_value," +
            "_sprk_createdbyperson_value,sprk_issecure");

    private static async Task<JsonElement> ReadMatterAsync(HttpClient dv, Guid id) =>
        await dv.GetFromJsonAsync<JsonElement>(
            $"sprk_matters({id})?$select=sprk_mattername,sprk_matternumber,_sprk_mattertype_value,_sprk_practicearea_value,_owningbusinessunit_value");

    private static string? Value(JsonElement row, string column) =>
        row.TryGetProperty(column, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    private static async Task<Guid> DefaultOwnerTeamOfRecordAsync(HttpClient dv, string set, Guid id)
    {
        var bu = (await dv.GetFromJsonAsync<JsonElement>($"{set}({id})?$select=_owningbusinessunit_value"))
            .GetProperty("_owningbusinessunit_value").GetGuid();
        return (await dv.GetFromJsonAsync<JsonElement>(
                $"teams?$select=teamid&$filter=_businessunitid_value eq {bu} and isdefault eq true and teamtype eq 0"))
            .GetProperty("value").EnumerateArray().Single().GetProperty("teamid").GetGuid();
    }

    private static async Task<int> CountZzAsync(HttpClient dv) =>
        (await dv.GetFromJsonAsync<JsonElement>("sprk_workassignments?$select=sprk_workassignmentid&$filter=startswith(sprk_name,'zz-046')"))
        .GetProperty("value").GetArrayLength();

    private static async Task<List<string>> DeleteAsync(HttpClient dv, string path)
    {
        try
        {
            var r = await dv.DeleteAsync(path);
            return r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound
                ? new List<string>()
                : new List<string> { $"{path}: HTTP {(int)r.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new List<string> { $"{path}: {ex.Message}" };
        }
    }

    private static async Task<HttpClient> DataverseClientAsync(string url, TokenCredential credential)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { $"{url.TrimEnd('/')}/.default" }), default);
        var client = new HttpClient { BaseAddress = new Uri($"{url.TrimEnd('/')}/api/data/v9.2/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        return client;
    }

    /// <summary>The BFF host with the production work-assignment path bound to the real environment.</summary>
    private sealed class LiveWorkAssignmentFactory : DataverseIntegrationTestFixture
    {
        private readonly TokenCredential _credential;
        private readonly ITestOutputHelper _out;
        private readonly HttpClient _metadata;

        public LiveWorkAssignmentFactory(string dataverseUrl, TokenCredential credential, ITestOutputHelper output)
        {
            _credential = credential;
            _out = output;
            DataverseUrl = dataverseUrl.TrimEnd('/');
            LiveConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = DataverseUrl,
                // DataverseServiceClientImpl's managed-identity branch, fed the operator's credential (substitution 3).
                ["Graph:ManagedIdentity:Enabled"] = "true",
            }).Build();
            _metadata = DataverseClientAsync(dataverseUrl, credential).GetAwaiter().GetResult();
        }

        public string DataverseUrl { get; }

        public IConfiguration LiveConfiguration { get; }

        /// <summary>The real systemuser the requests act as.</summary>
        public Guid? CallerSystemUserId { get; private set; }

        /// <summary>The operator (the identity behind the az login token) — impersonation is sent only for someone else.</summary>
        public Guid? OperatorSystemUserId { get; private set; }

        /// <summary>That user's real Entra object id — the inbound <c>oid</c> claim.</summary>
        public string? CallerObjectId { get; private set; }

        public async Task ActAsAsync(Guid systemUserId)
        {
            OperatorSystemUserId ??= (await _metadata.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
            var user = await _metadata.GetFromJsonAsync<JsonElement>($"systemusers({systemUserId})?$select=azureactivedirectoryobjectid");
            CallerSystemUserId = systemUserId;
            CallerObjectId = user.GetProperty("azureactivedirectoryobjectid").GetString();
            _out.WriteLine($"acting as systemuser {systemUserId} (oid {CallerObjectId})");
        }

        internal Task<string> DataverseTokenAsync(CancellationToken ct) =>
            _credential.GetTokenAsync(new TokenRequestContext(new[] { $"{DataverseUrl}/.default" }), ct)
                .AsTask().ContinueWith(t => t.Result.Token, ct);

        internal void Trace(string line) => _out.WriteLine(line);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                // Every app-only SDK read and the resolver's queries: the PRODUCTION client against the real environment.
                services.RemoveAll<IDataverseService>();
                services.AddSingleton<IDataverseService>(sp => new DataverseServiceClientImpl(
                    LiveConfiguration, sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>(),
                    confidentialClients: null, managedIdentityCredential: _credential));

                // The app-only create (IFieldMappingDataverseService → DataverseWebApiService): the production service.
                services.RemoveAll<DataverseWebApiService>();
                services.AddSingleton<DataverseWebApiService>(sp => new LiveDataverseWebApiService(
                    new HttpClient(), LiveConfiguration, sp.GetRequiredService<ILogger<DataverseWebApiService>>(),
                    sp.GetRequiredService<IRecordShareWriteObserver>(), _credential));

                // The caller's own Dataverse (substitution 2).
                services.RemoveAll<IDataverseUserClient>();
                services.AddScoped<IDataverseUserClient>(_ => new LiveUserClient(this));

                // The inbound identity carries the acting user's REAL oid (substitution 1).
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
    }

    /// <summary>
    /// The caller's Dataverse with ONE step substituted: the OBO exchange (substitution 2). Every question is sent with the
    /// operator's token and MSCRMCallerID = the acting user, so Dataverse authorizes it AS that user; the response shape is
    /// the production client's (status, <see cref="DataverseUserClient.MapErrorCode"/>, the Web API error message).
    /// </summary>
    private sealed class LiveUserClient(LiveWorkAssignmentFactory factory) : IDataverseUserClient
    {
        private static readonly HttpClient Http = new();

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Get, $"/api/data/v9.2/{relativePath.TrimStart('/')}", null, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Post, absoluteApiPath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Post, absoluteApiPath, jsonBody, cancellationToken, preferRepresentation);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Patch, $"/api/data/v9.2/{relativePath.TrimStart('/')}", jsonBody, cancellationToken, ifMatch: true);

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            SendAsync(HttpMethod.Delete, $"/api/data/v9.2/{relativePath.TrimStart('/')}", null, cancellationToken);

        private async Task<DataverseUserResponse> SendAsync(
            HttpMethod method, string apiPath, string? jsonBody, CancellationToken ct, bool preferRepresentation = false, bool ifMatch = false)
        {
            using var request = new HttpRequestMessage(method, $"{factory.DataverseUrl}{apiPath}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await factory.DataverseTokenAsync(ct));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            if (factory.CallerSystemUserId is { } caller && caller != factory.OperatorSystemUserId)
                request.Headers.Add("MSCRMCallerID", caller.ToString("D"));
            if (preferRepresentation)
                request.Headers.Add("Prefer", "return=representation");
            if (ifMatch)
                request.Headers.TryAddWithoutValidation("If-Match", "*");
            if (jsonBody is not null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            factory.Trace($"    as-caller {method} {apiPath.Split('?')[0]} -> {(int)response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return DataverseUserResponse.Ok((int)response.StatusCode, body: null);
                using var doc = JsonDocument.Parse(text);
                return DataverseUserResponse.Ok((int)response.StatusCode, doc.RootElement.Clone());
            }

            string? message = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                message = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
            }
            catch (Exception)
            {
                // not a Web API error body
            }

            return DataverseUserResponse.Fail((int)response.StatusCode, DataverseUserClient.MapErrorCode((int)response.StatusCode),
                message ?? $"Dataverse returned HTTP {(int)response.StatusCode}.");
        }
    }

    /// <summary>Reaches the production service's credential seam (protected constructor).</summary>
    private sealed class LiveDataverseWebApiService : DataverseWebApiService
    {
        public LiveDataverseWebApiService(HttpClient http, IConfiguration config, ILogger<DataverseWebApiService> logger,
            IRecordShareWriteObserver observer, TokenCredential credential)
            : base(http, config, logger, observer, confidentialClients: null, credential: credential)
        {
        }
    }

    /// <summary>Authenticates every request as the acting user's REAL Entra object id.</summary>
    private sealed class LiveUserAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        LiveWorkAssignmentFactory factory)
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
