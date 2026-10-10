// spaarke-ontology-platform-r1 task 046 (D-21; uac-r2's answer D-113) — LIVE parity proof of the ONE server-side
// work-assignment create, POST /api/v1/child-records/sprk_workassignment, against a real Dataverse environment through the
// REAL route.
//
// Opt-in only (skip-via-return, the repo's convention — see Events/EventRoutesLiveTests): a plain `dotnet test` touches no
// network. Enable with
//   SPAARKE_LIVE_WA_DATAVERSE_URL=https://<env>.crm.dynamics.com          (spaarkedev1)
//   SPAARKE_LIVE_WA_MATTER_ID=<an ORDINARY sprk_matter the acting users can append to>
//   SPAARKE_LIVE_WA_SECOND_USER_ID=<systemuserid of an ordinary user in the matter's business unit>   (optional parity leg)
//   SPAARKE_LIVE_WA_DENIED_USER_ID=<systemuserid of a real user WITHOUT AppendTo on DENIED_MATTER>     (optional leg)
//   SPAARKE_LIVE_WA_DENIED_MATTER_ID=<a sprk_matter that user cannot append to>                       (optional leg)
// and an `az login` session for an identity that may create sprk_workassignment and impersonate (System Administrator).
//
// WHAT IS REAL: the BFF host in-process (routing, binding, RequireAuthorization, rate limiting, ChildRecordEndpoints.CreateAsync),
// OwnedChildWrite (G5), DataverseWriteItemMapper, RecordOwnershipResolver (I-6), SecureRootFilingGate / SecureRootInheritance's
// plan, CoreAncestorRestamper, the production SDK client (DataverseServiceClientImpl) for every app-only read, and the
// production DataverseWebApiService for the app-only create. Dataverse makes every allow/deny decision for real systemusers.
//
// WHAT IS SUBSTITUTED, AND WHY:
//   (1) the inbound TOKEN — a fake scheme whose identity carries the acting user's REAL Entra oid (an operator workstation
//       cannot mint a user token for the BFF audience);
//   (2) IDataverseUserClient's OBO exchange — the operator's own `az login` Dataverse token, with MSCRMCallerID naming the
//       acting user when it is not the operator, so Dataverse answers every as-the-caller question FOR THAT USER. The
//       response mapping is the production one (DataverseUserClient.MapErrorCode). WhoAmI is the one exception: Dataverse
//       ignores MSCRMCallerID there, so the harness answers it with the acting user (what the user's own token answers);
//   (3) the BFF's outbound identity — AzureCliCredential instead of the managed identity. No client secret is used.
//
// THE BROWSER LEG is the wizard's own write, replayed exactly: the payload workAssignmentService.ts builds (name, priority,
// description, due date; the acting user's business-unit search defaults; the regarding matter with the ADR-024 resolver
// fields; the matter's type and practice area; the field-mapping profile's rule) POSTed to sprk_workassignments AS THE
// ACTING USER (MSCRMCallerID) — what Xrm.WebApi.createRecord sends. Both rows are then read IN FULL and compared column by
// column; only the parity table's intended differences may differ (notes/046-work-assignment-parity.md).
//
// Every record is named "zz-046-test …" and deleted in `finally`; ids are registered BEFORE anything is asserted, every
// delete is checked, and the zz-046 count is confirmed zero by query.
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
    private const string Route = "/api/v1/child-records/sprk_workassignment";
    private readonly ITestOutputHelper _out;

    public WorkAssignmentCreateLiveTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Columns that MAY differ between the two rows — the parity table's intended differences and per-row identity. Every
    /// other column of the full row must be equal.
    /// </summary>
    private static readonly HashSet<string> IntendedDifferences = new(StringComparer.OrdinalIgnoreCase)
    {
        // identity, timestamps, row version
        "sprk_workassignmentid", "createdon", "modifiedon", "versionnumber", "@odata.etag",
        // the owner decision (parity rows 13-15): team-owned by the resolver's team, created by the application, the
        // person recorded in sprk_createdbyperson
        "_ownerid_value", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value",
        "_createdby_value", "_modifiedby_value", "_createdonbehalfby_value", "_modifiedonbehalfby_value",
        "_sprk_createdbyperson_value",
        // Dataverse's internal time-zone rule version of the CREATING principal (an impersonated user with a time zone: 4;
        // the application: 0). It follows createdby (the application on the server path), like the rows above.
        "timezoneruleversionnumber",
    };

    [Fact]
    public async Task LiveMode_WorkAssignmentCreate_ThroughTheChildRecordsRoute_MatchesTheWizardField_ByField()
    {
        var dataverseUrl = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(dataverseUrl))
            return; // not opted in

        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_MATTER_ID"), out var matterId))
        {
            _out.WriteLine("SPAARKE_LIVE_WA_MATTER_ID is not set to a matter id; skipped");
            return;
        }

        var secondUser = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_SECOND_USER_ID"), out var su) ? su : (Guid?)null;
        var deniedUser = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_DENIED_USER_ID"), out var du) ? du : (Guid?)null;
        var deniedMatter = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_WA_DENIED_MATTER_ID"), out var dm) ? dm : (Guid?)null;
        var credential = new AzureCliCredential();

        using var dv = await DataverseClientAsync(dataverseUrl, credential);
        var operatorId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
        _out.WriteLine($"operator systemuser (WhoAmI) = {operatorId}");

        await using var factory = new LiveWorkAssignmentFactory(dataverseUrl, credential, _out);
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var created = new List<Guid>();
        var leaks = new List<string>();
        var before = await CountZzAsync(dv);
        _out.WriteLine($"zz-046 work assignments before = {before}");
        try
        {
            // ── Parity leg(s): the same payload through the browser path and the route, as the same user ──────────────
            await ParityLegAsync(dv, bff, factory, operatorId, operatorId, matterId, "operator", created);
            if (secondUser is { } user)
            {
                // An ordinary user (Spaarke Basic User + Spaarke Core User) holds Read but NOT AppendTo on sprk_aisearchindex
                // (live, 2026-10-10), so the wizard's own create fails whenever their business unit names an AI search index.
                // Refusal parity first: the browser path is refused by Dataverse, and the route refuses too, creating nothing.
                await RefusalParityLegAsync(dv, bff, factory, user, matterId, created);

                // Then full field-by-field parity for that user with a business unit that names no AI search index (the
                // wizard then leaves the lookup unset, INV-5) — a real configuration, and the one this user can create in.
                await ParityLegAsync(dv, bff, factory, operatorId, user, matterId, "second user", created, includeAiSearchIndex: false);
            }
            else
                _out.WriteLine("second-user parity leg not configured (SPAARKE_LIVE_WA_SECOND_USER_ID)");

            // ── A caller WITHOUT AppendTo on the regarding matter is refused, and nothing is created ──────────────────
            if (deniedUser is { } denied && deniedMatter is { } deniedOn)
            {
                await factory.ActAsAsync(denied);
                var count = await CountZzAsync(dv);
                // Without the AI search index (the user holds no AppendTo on it — see the refusal-parity leg), so the ONLY
                // record this caller cannot append to is the regarding matter.
                var deniedPayload = await WizardPayloadAsync(dv, deniedOn, denied, "zz-046-test denied", includeAiSearchIndex: false);
                var refused = await bff.PostAsJsonAsync(Route, deniedPayload);
                await RegisterCreatedAsync(refused, created); // registered in case it was (wrongly) created
                _out.WriteLine($"POST {Route} as {denied} -> HTTP {(int)refused.StatusCode} {await refused.Content.ReadAsStringAsync()}");
                refused.StatusCode.Should().Be(HttpStatusCode.NotFound, "no AppendTo on the regarding matter is the uniform 404");
                (await CountZzAsync(dv)).Should().Be(count, "a refused create writes nothing");
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
            _out.WriteLine($"cleanup: deleted {created.Count - leaks.Count} of {created.Count} ({string.Join(", ", created)}); " +
                           $"zz-046 rows remaining = {remaining}");
            leaks.Should().BeEmpty("every zz-046 row is removed");
            remaining.Should().Be(0, "no zz-046 row is left behind");
        }
    }

    private async Task RefusalParityLegAsync(
        HttpClient dv, HttpClient bff, LiveWorkAssignmentFactory factory, Guid actingUser, Guid matterId, List<Guid> created)
    {
        await factory.ActAsAsync(actingUser);
        var payload = await WizardPayloadAsync(dv, matterId, actingUser, "zz-046-test refusal parity");
        if (!payload.Keys.Any(k => k.StartsWith("sprk_AI_Search_Index", StringComparison.OrdinalIgnoreCase)))
        {
            _out.WriteLine("[refusal] the user's business unit names no AI search index; refusal-parity leg not applicable");
            return;
        }

        var count = await CountZzAsync(dv);
        using var browser = new HttpRequestMessage(HttpMethod.Post, "sprk_workassignments")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        browser.Headers.Add("MSCRMCallerID", actingUser.ToString("D"));
        using var browserResponse = await dv.SendAsync(browser);
        if (browserResponse.Headers.TryGetValues("OData-EntityId", out var values))
            created.Add(Guid.Parse(values.First().Split('(', ')')[1]));
        _out.WriteLine($"[refusal] browser path -> HTTP {(int)browserResponse.StatusCode} {await browserResponse.Content.ReadAsStringAsync()}");

        var post = await bff.PostAsJsonAsync(Route, payload);
        await RegisterCreatedAsync(post, created);
        _out.WriteLine($"[refusal] POST {Route} -> HTTP {(int)post.StatusCode} {await post.Content.ReadAsStringAsync()}");

        browserResponse.IsSuccessStatusCode.Should().BeFalse("the user holds no AppendTo on the AI search index the payload binds");
        ((int)post.StatusCode).Should().BeInRange(400, 499, "the route asks the same question as the caller and refuses too");
        (await CountZzAsync(dv)).Should().Be(count, "neither path created anything");
    }

    private async Task ParityLegAsync(
        HttpClient dv, HttpClient bff, LiveWorkAssignmentFactory factory, Guid operatorId, Guid actingUser, Guid matterId,
        string label, List<Guid> created, bool includeAiSearchIndex = true)
    {
        await factory.ActAsAsync(actingUser);
        var payload = await WizardPayloadAsync(dv, matterId, actingUser, $"zz-046-test parity {label} {DateTime.UtcNow:yyyyMMddHHmmss}",
            includeAiSearchIndex);
        _out.WriteLine($"[{label}] wizard payload: {JsonSerializer.Serialize(payload)}");

        // 1. The browser path: the wizard's createRecord, AS the acting user.
        var browserId = await CreateAsUserAsync(dv, payload, actingUser == operatorId ? null : actingUser, created);
        _out.WriteLine($"[{label}] browser path created {browserId}");

        // 2. The route: the same payload, as the same user.
        var post = await bff.PostAsJsonAsync(Route, payload);
        var bffId = await RegisterCreatedAsync(post, created);
        _out.WriteLine($"[{label}] POST {Route} -> HTTP {(int)post.StatusCode} {await post.Content.ReadAsStringAsync()}");
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. Field by field, over the FULL rows — once the environment's own secure-root inheritance job (the deployed BFF's,
        //    ≤ 5 minutes; task 175) has written the access record it owns on BOTH rows, whichever path created them. That
        //    column is the job's on either path; comparing before it ran on one of them compares timing, not the create.
        var (browser, server) = await SettledAsync(dv, browserId, bffId, label);
        var columns = browser.EnumerateObject().Select(p => p.Name)
            .Union(server.EnumerateObject().Select(p => p.Name))
            .Where(c => !c.Contains('@')) // annotations
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        var unexpected = new List<string>();
        var compared = 0;
        foreach (var column in columns)
        {
            var (b, s) = (Value(browser, column), Value(server, column));
            if (b == s)
            {
                if (b is not null)
                    _out.WriteLine($"[{label}]   =  {column}: {b}");
                compared++;
                continue;
            }

            var intended = IntendedDifferences.Contains(column);
            _out.WriteLine($"[{label}]   {(intended ? "~ " : "!!")} {column}: browser={b ?? "null"} server={s ?? "null"}");
            if (!intended)
                unexpected.Add($"{column}: browser={b ?? "null"} server={s ?? "null"}");
        }

        _out.WriteLine($"[{label}] {columns.Count} columns read, {compared} equal, {unexpected.Count} unexpected difference(s)");
        unexpected.Should().BeEmpty("every column outside the parity table's intended differences matches the wizard's");

        // The intended differences are what the parity table says they are.
        var matterTeam = await DefaultOwnerTeamOfRecordAsync(dv, "sprk_matters", matterId);
        Value(browser, "_ownerid_value").Should().Be(actingUser.ToString("D"), "the wizard's create is owned by the user");
        Value(server, "_ownerid_value").Should().Be(matterTeam.ToString("D"), "I-6 record-first: the matter's business-unit team");
        Value(server, "_owningbusinessunit_value").Should().Be(Value(await ReadMatterAsync(dv, matterId), "_owningbusinessunit_value"));
        Value(browser, "_sprk_createdbyperson_value").Should().BeNull("the wizard never stamps the creator person");
        Value(server, "_sprk_createdbyperson_value").Should().Be(actingUser.ToString("D"), "the app-only create records the person");
        Value(server, "sprk_issecure").Should().NotBe("True", "filed under an ordinary matter: not secured");
        Value(server, "sprk_containerid").Should().BeNull("an ordinary work assignment gets no container of its own");
    }

    /// <summary>The job-owned access record (task 175) on a work assignment.</summary>
    private const string AccessRecordColumn = "sprk_accessinheritance";

    /// <summary>
    /// Both rows, read in full once the secure-root inheritance job has written <see cref="AccessRecordColumn"/> on both
    /// (polled every 15 s for up to 6 minutes). If it never reaches one of them, the rows are compared as they are and the
    /// difference fails the comparison — a create path the job does not complete is a finding, not timing.
    /// </summary>
    private async Task<(JsonElement Browser, JsonElement Server)> SettledAsync(HttpClient dv, Guid browserId, Guid serverId, string label)
    {
        var deadline = DateTime.UtcNow.AddMinutes(6);
        while (true)
        {
            var browser = await dv.GetFromJsonAsync<JsonElement>($"sprk_workassignments({browserId})");
            var server = await dv.GetFromJsonAsync<JsonElement>($"sprk_workassignments({serverId})");
            var (b, s) = (Value(browser, AccessRecordColumn) is not null, Value(server, AccessRecordColumn) is not null);
            if ((b && s) || DateTime.UtcNow > deadline)
            {
                _out.WriteLine($"[{label}] access record written by the job: browser={b} server={s} " +
                               $"(after {(DateTime.UtcNow - deadline.AddMinutes(-6)).TotalSeconds:F0} s)");
                return (browser, server);
            }

            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// The payload the wizard builds (workAssignmentService.ts createWorkAssignment): name, priority, description, due
    /// date; the acting user's business-unit search defaults; the regarding matter with the ADR-024 resolver fields
    /// (applyResolverFields); the field-mapping profile's rules for {sprk_matter → sprk_workassignment} (applyFieldMappings:
    /// a lookup Copy writes `{navProp}@odata.bind`); the matter's type and practice area. Navigation properties are read
    /// from metadata, as the wizard's discoverNavProps does.
    /// </summary>
    private static async Task<Dictionary<string, object?>> WizardPayloadAsync(
        HttpClient dv, Guid matterId, Guid userId, string name, bool includeAiSearchIndex = true)
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

        // Field mapping (task 021 / FR-12): every active rule of the {sprk_matter → sprk_workassignment} profile. Live in
        // spaarkedev1: one lookup Copy, sprk_assignedattorney1 → sprk_assignedattorney1 (a contact).
        var profiles = (await dv.GetFromJsonAsync<JsonElement>(
                "sprk_fieldmappingprofiles?$select=sprk_fieldmappingprofileid" +
                "&$filter=statecode eq 0 and sprk_name eq 'Matter to Work Assignment (Attorney Matrix)'"))
            .GetProperty("value").EnumerateArray().Select(p => p.GetProperty("sprk_fieldmappingprofileid").GetString()).ToList();
        var rules = profiles.Count == 0
            ? Enumerable.Empty<JsonElement>()
            : (await dv.GetFromJsonAsync<JsonElement>(
                    "sprk_fieldmappingrules?$select=sprk_sourcefield,sprk_targetfield" +
                    $"&$filter=sprk_isactive eq true and _sprk_fieldmappingprofile_value eq {profiles[0]}"))
                .GetProperty("value").EnumerateArray().ToList();
        foreach (var rule in rules)
        {
            var source = rule.GetProperty("sprk_sourcefield").GetString()!;
            var target = rule.GetProperty("sprk_targetfield").GetString()!;
            var row = await dv.GetFromJsonAsync<JsonElement>($"sprk_matters({matterId})?$select=_{source}_value");
            if (Value(row, $"_{source}_value") is { } contact)
                payload[$"{nav[target]}@odata.bind"] = $"/contacts({contact})";
        }

        if (Value(matter, "_sprk_mattertype_value") is { } type)
            payload[$"{nav["sprk_mattertype"]}@odata.bind"] = $"/sprk_mattertype_refs({type})";
        if (Value(matter, "_sprk_practicearea_value") is { } area)
            payload[$"{nav["sprk_practicearea"]}@odata.bind"] = $"/sprk_practicearea_refs({area})";

        // The acting user's business-unit search defaults (EntityCreationService.resolveUserBuDefaults / applyUserBuDefaults).
        var bu = Value(await dv.GetFromJsonAsync<JsonElement>($"systemusers({userId})?$select=_businessunitid_value"), "_businessunitid_value");
        var unit = await dv.GetFromJsonAsync<JsonElement>($"businessunits({bu})?$select=sprk_searchindexname,_sprk_ai_search_index_value");
        if (Value(unit, "sprk_searchindexname") is { Length: > 0 } index)
            payload["sprk_searchindexname"] = index;
        if (includeAiSearchIndex && Value(unit, "_sprk_ai_search_index_value") is { } indexId)
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

    /// <summary>The wizard's Xrm.WebApi.createRecord, as <paramref name="impersonate"/> (MSCRMCallerID) when not the operator.</summary>
    private static async Task<Guid> CreateAsUserAsync(HttpClient dv, Dictionary<string, object?> payload, Guid? impersonate, List<Guid> created)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "sprk_workassignments")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        if (impersonate is { } user)
            request.Headers.Add("MSCRMCallerID", user.ToString("D"));
        using var response = await dv.SendAsync(request);
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

    /// <summary>The BFF host with the production child-records route bound to the real environment.</summary>
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

                // Every other app-only outbound call that takes the host's TokenCredential (the secure-flag reads of the
                // Assigned-To step, ExternalParticipationService): the operator's credential (substitution 3).
                services.RemoveAll<TokenCredential>();
                services.AddSingleton<TokenCredential>(_credential);

                // The app-only Web API client the Assigned-To store reads and writes through (the inline I-12 step after the
                // create): the production client, given the operator's credential (substitution 3).
                services.RemoveAll<DataverseWebApiClient>();
                services.AddSingleton(sp => new DataverseWebApiClient(
                    LiveConfiguration, sp.GetRequiredService<ILogger<DataverseWebApiClient>>(), _credential));

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

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            // Under the caller's OWN token (production OBO) WhoAmI names the caller. Dataverse's WhoAmI ignores MSCRMCallerID
            // (live, 2026-10-10: it answered the operator for an impersonated user), so the impersonated identity is
            // answered here — every other as-the-caller question still goes to Dataverse under MSCRMCallerID.
            if (relativePath.TrimStart('/') == "WhoAmI()"
                && factory.CallerSystemUserId is { } caller && caller != factory.OperatorSystemUserId)
            {
                factory.Trace($"    as-caller GET WhoAmI() -> {caller} (the acting user; substitution 2)");
                return Task.FromResult(DataverseUserResponse.Ok(200,
                    JsonSerializer.SerializeToElement(new { UserId = caller.ToString("D") })));
            }

            return SendAsync(HttpMethod.Get, $"/api/data/v9.2/{relativePath.TrimStart('/')}", null, cancellationToken);
        }

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
