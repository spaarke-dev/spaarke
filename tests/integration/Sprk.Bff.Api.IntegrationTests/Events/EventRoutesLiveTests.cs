// spaarke-ontology-platform-r1 task 097 — LIVE route proof against a real Dataverse environment (review rounds 3 and 5).
//
// Opt-in only (skip-via-return, the repo's convention — see Membership/Phase2EndToEndTests LiveMode_* and
// tests/integration/seam/SpeAdmin/LiveIntegrationFixture): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_EVENTS_DATAVERSE_URL=https://<env>.crm.dynamics.com   (e.g. spaarkedev1)
//   SPAARKE_LIVE_EVENTS_MATTER_ID=<existing sprk_matter id>  SPAARKE_LIVE_EVENTS_PROJECT_ID=<existing sprk_project id>
//   SPAARKE_LIVE_EVENTS_DENIED_USER_ID=<systemuserid of a REAL low-privilege user>   (optional: the unauthorized leg)
// and an `az login` session for an identity that may read/write sprk_event in that environment.
//
// WHAT IS REAL: the BFF host (routing, binding, the EventAccessFilter chain, the EventEndpoints handlers) in-process;
// the PRODUCTION DataverseWebApiService (request building, HTTP, parsing, MSCRMCallerID impersonation) against the real
// environment; the production CallerRecordAccessProbe's own WhoAmI and RetrievePrincipalAccess calls — Dataverse makes
// every allow/deny decision below, for real systemusers; the production CoreAncestorResolver logic.
//
// WHAT IS SUBSTITUTED, AND WHY:
//   (1) inbound authentication — a fake scheme (DataverseIntegrationTestFixture), because an operator workstation cannot
//       mint a user token for the BFF audience;
//   (2) the OBO exchange inside the probe — replaced by the operator's own `az login` Dataverse token. The PRINCIPAL whose
//       rights are asked is then either the operator (WhoAmI, real) or the configured low-privilege user: Dataverse's
//       RetrievePrincipalAccess answers for THAT principal, so the deny is Dataverse's, not the test's;
//   (3) the caller → systemuser lookup for the list (oid → systemuserid) — answered with the same principal, and the list
//       query then really runs with MSCRMCallerID = that user;
//   (4) the BFF's outbound identity — AzureCliCredential instead of the managed identity (which does not exist off Azure).
//       No client secret is created or used.
// ExternalDataService is exercised by calling the production service directly (its routes need an external-caller
// principal and a project grant that a fake identity cannot hold).
//
// Every record is named "zz-097-test …" and deleted in `finally` (with its sprk_eventlog rows). Review M4: ids are
// registered BEFORE anything is asserted about them, every delete's status is checked, one failed delete does not stop
// the rest, and the final count is reported honestly — a leak fails the test.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Events;

public sealed class EventRoutesLiveTests
{
    private const string UrlVar = "SPAARKE_LIVE_EVENTS_DATAVERSE_URL";
    private readonly ITestOutputHelper _out;

    public EventRoutesLiveTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task LiveMode_EventRoutes_WriteAndReadBackAgainstRealDataverse()
    {
        var dataverseUrl = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(dataverseUrl))
            return; // not opted in

        var matterId = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_MATTER_ID")!);
        var projectId = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_PROJECT_ID")!);
        var deniedUser = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_DENIED_USER_ID"), out var d) ? d : (Guid?)null;
        var credential = new AzureCliCredential();

        using var dv = await DataverseClientAsync(dataverseUrl, credential);
        var operatorId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
        _out.WriteLine($"operator systemuser (WhoAmI) = {operatorId}; denied-leg user = {deniedUser?.ToString() ?? "(not configured)"}");

        await using var factory = new LiveEventsFactory(dataverseUrl, credential, operatorId, _out);
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var createdEvents = new List<Guid>();
        var createdAnalyses = new List<Guid>();
        var leaks = new List<string>();
        try
        {
            // ── AUTHORIZED leg: the operator, a real systemuser with rights ─────────────────────────────────────

            // 1. POST — create with live priority and a Matter regarding (parent probed for Read first).
            var post = await bff.PostAsJsonAsync("/api/v1/events", new
            {
                subject = "zz-097-test route create",
                priority = EventPriority.High,
                dueDate = "2026-10-20",
                regardingRecordType = RegardingRecordType.Matter,
                regardingRecordId = matterId,
            });
            var id = await RegisterCreatedAsync(post, createdEvents);
            Log("POST /api/v1/events", post);
            post.StatusCode.Should().Be(HttpStatusCode.Created);

            var row = await ReadEventAsync(dv, id);
            Log("  read-back", row);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Open);
            row.GetProperty("statecode").GetInt32().Should().Be(0);
            row.GetProperty("sprk_priority").GetInt32().Should().Be(EventPriority.High);
            row.GetProperty("_sprk_regardingmatter_value").GetGuid().Should().Be(matterId);
            row.GetProperty("_sprk_regardingrecordtype_value").ValueKind.Should().Be(JsonValueKind.String);
            row.GetProperty("sprk_regardingrecordid").GetString().Should().Be(matterId.ToString("D"));
            row.GetProperty("sprk_regardingrecordtypelogicalname").GetString().Should().Be("sprk_matter");
            row.GetProperty("sprk_regardingrecordurl").GetString().Should().Contain(matterId.ToString("D"));
            var matter = await dv.GetFromJsonAsync<JsonElement>($"sprk_matters({matterId})?$select=sprk_mattername,sprk_matternumber");
            row.GetProperty("sprk_regardingrecordname").GetString().Should().Be(matter.GetProperty("sprk_mattername").GetString(),
                "M2: the server-read name is written");
            row.GetProperty("sprk_regardingrecordnumber").GetString().Should().Be(matter.GetProperty("sprk_matternumber").GetString(),
                "M2: the number column comes from sprk_recordtype_ref.sprk_regardingrecordnumberfield");

            // 2. GET /{id}
            var get = await bff.GetAsync($"/api/v1/events/{id}");
            Log($"GET /api/v1/events/{id}", get);
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await get.Content.ReadFromJsonAsync<JsonElement>();
            dto.GetProperty("statusCode").GetInt32().Should().Be(EventStatusCode.Open);
            dto.GetProperty("priorityName").GetString().Should().Be("High");
            dto.GetProperty("regardingRecordType").GetInt32().Should().Be(RegardingRecordType.Matter,
                "H3: the type comes from sprk_regardingrecordtypelogicalname");

            // 3. GET list, filtered — runs IMPERSONATED as the operator (MSCRMCallerID).
            var list = await bff.GetAsync(
                $"/api/v1/events?regardingRecordType=1&regardingRecordId={matterId}&status=open&pageSize=50");
            Log("GET /api/v1/events?… (as operator)", list);
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            (await list.Content.ReadAsStringAsync()).Should().Contain(id.ToString());

            // 4. PUT — re-parent to a Project, On Hold, Urgent: matter cleared, five fields rewritten.
            var put = await bff.PutAsJsonAsync($"/api/v1/events/{id}", new
            {
                statusCode = EventStatusCode.OnHold,
                priority = EventPriority.Urgent,
                regardingRecordType = RegardingRecordType.Project,
                regardingRecordId = projectId,
            });
            Log($"PUT /api/v1/events/{id} (→ project)", put);
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, id);
            Log("  read-back", row);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.OnHold);
            row.GetProperty("sprk_priority").GetInt32().Should().Be(EventPriority.Urgent);
            row.GetProperty("_sprk_regardingproject_value").GetGuid().Should().Be(projectId);
            row.GetProperty("_sprk_regardingmatter_value").ValueKind.Should().Be(JsonValueKind.Null, "re-parent clears the old lookup");
            row.GetProperty("sprk_regardingrecordtypelogicalname").GetString().Should().Be("sprk_project");

            // 5. H2 — re-parent to a CHILD record (a zz analysis under the matter): the derived matter stamp must
            //    survive the clear-all-14 loop, and the project lookup must be cleared.
            var analysisId = await CreateAnalysisAsync(dv, matterId, createdAnalyses);
            var toChild = await bff.PutAsJsonAsync($"/api/v1/events/{id}", new
            {
                regardingRecordType = RegardingRecordType.Analysis,
                regardingRecordId = analysisId,
            });
            Log($"PUT /api/v1/events/{id} (→ analysis {analysisId})", toChild);
            toChild.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, id);
            Log("  read-back", row);
            row.GetProperty("_sprk_regardinganalysis_value").GetGuid().Should().Be(analysisId);
            row.GetProperty("_sprk_regardingmatter_value").GetGuid().Should().Be(matterId,
                "H2: the core-ancestor stamp is written AFTER the clear, so it survives");
            row.GetProperty("_sprk_regardingproject_value").ValueKind.Should().Be(JsonValueKind.Null);
            row.GetProperty("sprk_regardingrecordtypelogicalname").GetString().Should().Be("sprk_analysis");
            var childDto = await (await bff.GetAsync($"/api/v1/events/{id}")).Content.ReadFromJsonAsync<JsonElement>();
            childDto.GetProperty("regardingRecordType").GetInt32().Should().Be(RegardingRecordType.Analysis,
                "H3: a stamped child row reads as its own type, not as the matter its stamp points at");

            // 6. PUT with a type but no id ⇒ 400, nothing written.
            var half = await bff.PutAsJsonAsync($"/api/v1/events/{id}", new { regardingRecordType = RegardingRecordType.Matter });
            Log("PUT (type without id)", half);
            half.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 7. POST /complete
            var complete = await bff.PostAsync($"/api/v1/events/{id}/complete", null);
            Log("POST /complete", complete);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, id);
            Log("  read-back", row);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Completed);
            row.GetProperty("statecode").GetInt32().Should().Be(0);
            row.GetProperty("sprk_completeddate").ValueKind.Should().Be(JsonValueKind.String);

            // 8. GET /logs — the audit rows really exist (create, updates, complete).
            var logs = await bff.GetAsync($"/api/v1/events/{id}/logs");
            Log("GET /logs", logs);
            logs.StatusCode.Should().Be(HttpStatusCode.OK);
            (await logs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);

            // 9. cancel + DELETE on two more events.
            var second = await CreateAsync(bff, "zz-097-test route cancel", createdEvents);
            var cancel = await bff.PostAsync($"/api/v1/events/{second}/cancel", null);
            Log("POST /cancel", cancel);
            cancel.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, second);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Cancelled);
            row.GetProperty("statecode").GetInt32().Should().Be(1);

            var third = await CreateAsync(bff, "zz-097-test route delete", createdEvents);
            var delete = await bff.DeleteAsync($"/api/v1/events/{third}");
            Log("DELETE", delete);
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await ReadEventAsync(dv, third)).GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Cancelled);

            // 10. A page beyond Dataverse's $top ceiling ⇒ explicit 400.
            var deep = await bff.GetAsync("/api/v1/events?pageNumber=200&pageSize=50");
            Log("GET page 200", deep);
            deep.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 11. External events — the production ExternalDataService against the same environment.
            var external = new ExternalDataService(new HttpClient(), factory.LiveConfiguration, credential,
                factory.Services.GetRequiredService<ILogger<ExternalDataService>>());
            var ext = await external.CreateEventAsync(projectId,
                new CreateExternalEventRequest { SprkName = "zz-097-test external create", SprkDuedate = "2026-10-21" });
            createdEvents.Add(Guid.Parse(ext.SprkEventid));
            _out.WriteLine($"ExternalDataService.CreateEventAsync -> id={ext.SprkEventid} sprk_status={ext.SprkStatus}");
            ext.SprkStatus.Should().Be(EventStatusCode.Open);
            var mine = (await external.GetEventsAsync(projectId)).Single(e => e.SprkEventid == ext.SprkEventid);
            mine.SprkName.Should().Be("zz-097-test external create");
            mine.SprkStatus.Should().Be(EventStatusCode.Open);

            // ── UNAUTHORIZED leg: a REAL low-privilege systemuser ────────────────────────────────────────────────
            if (deniedUser is { } denied)
            {
                var direct = await RetrievePrincipalAccessAsync(dv, denied, "sprk_events", second);
                _out.WriteLine($"RetrievePrincipalAccess({denied}, sprk_events({second})) asked directly = {direct}");
                var directParent = await RetrievePrincipalAccessAsync(dv, denied, "sprk_matters", matterId);
                _out.WriteLine($"RetrievePrincipalAccess({denied}, sprk_matters({matterId})) asked directly = {directParent}");

                factory.CallerSystemUserId = denied;
                var before = await ReadEventAsync(dv, second);

                var deniedCalls = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
                {
                    ("GET /{id}", () => bff.GetAsync($"/api/v1/events/{second}")),
                    ("GET /{id}/logs", () => bff.GetAsync($"/api/v1/events/{second}/logs")),
                    ("PUT /{id}", () => bff.PutAsJsonAsync($"/api/v1/events/{second}", new { subject = "zz-097-test hijacked" })),
                    ("POST /{id}/complete", () => bff.PostAsync($"/api/v1/events/{second}/complete", null)),
                    ("POST /{id}/cancel", () => bff.PostAsync($"/api/v1/events/{second}/cancel", null)),
                    ("DELETE /{id}", () => bff.DeleteAsync($"/api/v1/events/{second}")),
                };
                foreach (var (name, call) in deniedCalls)
                {
                    var r = await call();
                    Log($"[as {denied}] {name}", r);
                    r.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{name} for a user Dataverse grants nothing on");
                }

                // The parent leg needs a parent this user cannot read: the matter if Dataverse denies it, else the zz
                // analysis this run created (owned by the operator).
                var directAnalysis = await RetrievePrincipalAccessAsync(dv, denied, "sprk_analysises", analysisId);
                _out.WriteLine($"RetrievePrincipalAccess({denied}, sprk_analysises({analysisId})) asked directly = {directAnalysis}");
                (int Type, Guid Id)? unreadableParent =
                    !directParent.Contains("ReadAccess") ? (RegardingRecordType.Matter, matterId)
                    : !directAnalysis.Contains("ReadAccess") ? (RegardingRecordType.Analysis, analysisId)
                    : null;
                if (unreadableParent is { } p)
                {
                    var deniedCreate = await bff.PostAsJsonAsync("/api/v1/events", new
                    {
                        subject = "zz-097-test denied create",
                        regardingRecordType = p.Type,
                        regardingRecordId = p.Id,
                    });
                    await RegisterCreatedAsync(deniedCreate, createdEvents);
                    Log($"[as {denied}] POST (parent {p.Id} it cannot read)", deniedCreate);
                    deniedCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                }
                else
                {
                    _out.WriteLine($"[as {denied}] POST-with-parent leg not run: Dataverse grants this user Read on every candidate parent.");
                }

                var deniedList = await bff.GetAsync("/api/v1/events?pageSize=100");
                Log($"[as {denied}] GET /api/v1/events (impersonated)", deniedList);
                deniedList.StatusCode.Should().Be(HttpStatusCode.OK);
                var deniedBody = await deniedList.Content.ReadAsStringAsync();
                foreach (var ours in createdEvents)
                    deniedBody.Should().NotContain(ours.ToString(), "Dataverse trims rows the impersonated user cannot read");

                var after = await ReadEventAsync(dv, second);
                after.GetProperty("statuscode").GetInt32().Should().Be(before.GetProperty("statuscode").GetInt32(),
                    "no denied write reached Dataverse");
                factory.CallerSystemUserId = null;
            }
        }
        finally
        {
            foreach (var eventId in createdEvents)
                leaks.AddRange(await DeleteEventWithLogsAsync(dv, eventId));
            foreach (var analysisId in createdAnalyses)
                leaks.AddRange(await DeleteAsync(dv, $"sprk_analysises({analysisId})"));
            _out.WriteLine($"cleanup: {createdEvents.Count} zz-097-test events (+ their sprk_eventlog rows) and "
                + $"{createdAnalyses.Count} zz analyses attempted; {leaks.Count} NOT deleted"
                + (leaks.Count > 0 ? ":\n  " + string.Join("\n  ", leaks) : "."));
        }

        leaks.Should().BeEmpty("every zz-097-test record this run created must be gone");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────

    private void Log(string what, HttpResponseMessage r) =>
        _out.WriteLine($"{what} -> HTTP {(int)r.StatusCode} {r.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

    private void Log(string what, JsonElement row) => _out.WriteLine($"{what}: {row}");

    /// <summary>M4: registers a created id for cleanup BEFORE any assertion about the response.</summary>
    private static async Task<Guid> RegisterCreatedAsync(HttpResponseMessage response, List<Guid> created)
    {
        if (response.StatusCode != HttpStatusCode.Created)
            return Guid.Empty;
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Add(id);
        return id;
    }

    private static async Task<Guid> CreateAsync(HttpClient bff, string subject, List<Guid> created)
    {
        var r = await bff.PostAsJsonAsync("/api/v1/events", new { subject });
        var id = await RegisterCreatedAsync(r, created);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return id;
    }

    private static async Task<Guid> CreateAnalysisAsync(HttpClient dv, Guid matterId, List<Guid> created)
    {
        var r = await dv.PostAsJsonAsync("sprk_analysises", new Dictionary<string, object>
        {
            ["sprk_name"] = "zz-097-test analysis (H2 re-parent target)",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matterId})",
        });
        if (r.Headers.TryGetValues("OData-EntityId", out var values))
            created.Add(Guid.Parse(values.First().Split('(', ')')[1]));
        r.EnsureSuccessStatusCode();
        return created[^1];
    }

    private static async Task<string> RetrievePrincipalAccessAsync(HttpClient dv, Guid user, string set, Guid id)
    {
        var target = Uri.EscapeDataString($"{{\"@odata.id\":\"{set}({id})\"}}");
        var r = await dv.GetAsync($"systemusers({user})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)?@t={target}");
        return r.IsSuccessStatusCode
            ? (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("AccessRights").GetString() ?? "None"
            : $"ERROR {(int)r.StatusCode}";
    }

    private static async Task<HttpClient> DataverseClientAsync(string url, TokenCredential credential)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { $"{url.TrimEnd('/')}/.default" }), default);
        var client = new HttpClient { BaseAddress = new Uri($"{url.TrimEnd('/')}/api/data/v9.2/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        return client;
    }

    private static async Task<JsonElement> ReadEventAsync(HttpClient dv, Guid id) =>
        await dv.GetFromJsonAsync<JsonElement>(
            $"sprk_events({id})?$select=statuscode,statecode,sprk_priority,sprk_completeddate,_sprk_regardingmatter_value," +
            "_sprk_regardingproject_value,_sprk_regardinganalysis_value,_sprk_regardingrecordtype_value,sprk_regardingrecordid," +
            "sprk_regardingrecordname,sprk_regardingrecordnumber,sprk_regardingrecordurl,sprk_regardingrecordtypelogicalname");

    /// <summary>M4: deletes an event and its log rows; returns what could NOT be deleted (never throws).</summary>
    private static async Task<List<string>> DeleteEventWithLogsAsync(HttpClient dv, Guid id)
    {
        var leaks = new List<string>();
        try
        {
            var logs = await dv.GetFromJsonAsync<JsonElement>($"sprk_eventlogs?$select=sprk_eventlogid&$filter=_sprk_event_value eq {id}");
            foreach (var log in logs.GetProperty("value").EnumerateArray())
                leaks.AddRange(await DeleteAsync(dv, $"sprk_eventlogs({log.GetProperty("sprk_eventlogid").GetString()})"));
        }
        catch (Exception ex)
        {
            leaks.Add($"sprk_eventlogs of {id}: could not be listed ({ex.Message})");
        }

        leaks.AddRange(await DeleteAsync(dv, $"sprk_events({id})"));
        return leaks;
    }

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

    /// <summary>The BFF host with the production event service, probe and resolver bound to the real environment.</summary>
    private sealed class LiveEventsFactory : DataverseIntegrationTestFixture
    {
        private readonly TokenCredential _credential;
        private readonly Guid _operatorId;
        private readonly ITestOutputHelper _out;
        private readonly HttpClient _metadata;

        public LiveEventsFactory(string dataverseUrl, TokenCredential credential, Guid operatorId, ITestOutputHelper output)
        {
            _credential = credential;
            _operatorId = operatorId;
            _out = output;
            LiveConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = dataverseUrl,
            }).Build();
            _metadata = DataverseClientAsync(dataverseUrl, credential).GetAwaiter().GetResult();
        }

        /// <summary>Only the live services read this; the rest of the host keeps the fixture's fake endpoints.</summary>
        public IConfiguration LiveConfiguration { get; }

        /// <summary>The principal the probe asks Dataverse about, and the list impersonates. Null → the operator.</summary>
        public Guid? CallerSystemUserId { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // The list's oid → systemuser lookup (substitution 3) and the generic row read the core-ancestor
            // resolver uses — both answered from the real environment / the configured principal.
            DataverseServiceMock.Setup(s => s.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => CallerSystemUserId ?? _operatorId);
            DataverseServiceMock.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string logicalName, Guid id, string[] columns, CancellationToken ct) => LiveRetrieveAsync(logicalName, id, columns, ct));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DataverseWebApiService>();
                services.AddSingleton<DataverseWebApiService>(sp => new LiveDataverseWebApiService(
                    new HttpClient(), LiveConfiguration,
                    sp.GetRequiredService<ILogger<DataverseWebApiService>>(), _credential));
                services.RemoveAll<IEventDataverseService>();
                services.AddSingleton<IEventDataverseService>(sp => sp.GetRequiredService<DataverseWebApiService>());

                services.RemoveAll<CallerRecordAccessProbe>();
                services.AddSingleton<CallerRecordAccessProbe>(sp => new LiveProbe(this, LiveConfiguration,
                    sp.GetRequiredService<ILogger<CallerRecordAccessProbe>>()));

                services.RemoveAll<CoreAncestorResolver>();
                services.AddSingleton(sp => new CoreAncestorResolver(
                    sp.GetRequiredService<IGenericEntityService>(), LiveColumnsAsync,
                    sp.GetRequiredService<ILogger<CoreAncestorResolver>>()));

                var identity = new Mock<IIdentityNormalizationService>();
                identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(id, ContactId: null));
                services.RemoveAll<IIdentityNormalizationService>();
                services.AddSingleton(identity.Object);
            });
        }

        internal Task<string> DataverseTokenAsync(CancellationToken ct) =>
            _credential.GetTokenAsync(new TokenRequestContext(new[] { $"{LiveConfiguration["Dataverse:ServiceUrl"]!.TrimEnd('/')}/.default" }), ct)
                .AsTask().ContinueWith(t => t.Result.Token, ct);

        internal void Trace(string line) => _out.WriteLine(line);

        private async Task<IReadOnlySet<string>> LiveColumnsAsync(string logicalName, CancellationToken ct)
        {
            var body = await _metadata.GetFromJsonAsync<JsonElement>(
                $"EntityDefinitions(LogicalName='{logicalName}')/Attributes?$select=LogicalName", ct);
            return body.GetProperty("value").EnumerateArray().Select(a => a.GetProperty("LogicalName").GetString()!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<Entity> LiveRetrieveAsync(string logicalName, Guid id, string[] columns, CancellationToken ct)
        {
            var set = (await _metadata.GetFromJsonAsync<JsonElement>(
                $"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName", ct)).GetProperty("EntitySetName").GetString();
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{set}({id})?$select={string.Join(',', columns.Select(c => $"_{c}_value"))}");
            request.Headers.Add("Prefer", "odata.include-annotations=\"Microsoft.Dynamics.CRM.lookuplogicalname\"");
            using var response = await _metadata.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var row = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var entity = new Entity(logicalName, id);
            foreach (var column in columns)
            {
                if (row.TryGetProperty($"_{column}_value", out var v) && v.ValueKind == JsonValueKind.String
                    && row.TryGetProperty($"_{column}_value@Microsoft.Dynamics.CRM.lookuplogicalname", out var target))
                    entity[column] = new EntityReference(target.GetString(), v.GetGuid());
            }

            return entity;
        }
    }

    /// <summary>
    /// The production probe with ONE step substituted: the OBO exchange (substitution 2). WhoAmI and
    /// RetrievePrincipalAccess are the production implementations, called against the real environment.
    /// </summary>
    private sealed class LiveProbe(LiveEventsFactory factory, IConfiguration configuration, ILogger<CallerRecordAccessProbe> logger)
        : CallerRecordAccessProbe(new HttpClient(), configuration, logger)
    {
        public override async Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            var token = await factory.DataverseTokenAsync(ct);
            var principal = factory.CallerSystemUserId ?? await ResolveCallerSystemUserIdAsync(token, ct);
            if (principal is null)
                return AccessRights.None;
            var rights = await RetrievePrincipalAccessAsync(token, principal.Value, entitySet, recordId, ct);
            factory.Trace($"    probe: RetrievePrincipalAccess(principal {principal}, {entitySet}({recordId})) = {rights}");
            return rights;
        }
    }

    /// <summary>Reaches the production service's credential seam (protected constructor).</summary>
    private sealed class LiveDataverseWebApiService : DataverseWebApiService
    {
        public LiveDataverseWebApiService(HttpClient http, IConfiguration config, ILogger<DataverseWebApiService> logger,
            TokenCredential credential)
            : base(http, config, logger, confidentialClients: null, credential: credential)
        {
        }
    }
}
