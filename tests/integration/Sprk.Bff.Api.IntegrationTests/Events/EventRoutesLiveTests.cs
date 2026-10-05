// spaarke-ontology-platform-r1 task 097, review round 3 — LIVE route proof against a real Dataverse environment.
//
// Opt-in only (skip-via-return, the repo's convention — see Membership/Phase2EndToEndTests LiveMode_* and
// tests/integration/seam/SpeAdmin/LiveIntegrationFixture): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_EVENTS_DATAVERSE_URL=https://<env>.crm.dynamics.com   (e.g. spaarkedev1)
//   SPAARKE_LIVE_EVENTS_MATTER_ID=<existing sprk_matter id>  SPAARKE_LIVE_EVENTS_PROJECT_ID=<existing sprk_project id>
// and an `az login` session for an identity that may read/write sprk_event in that environment.
//
// What is real here: the BFF host (routing, binding, filters, the EventEndpoints handlers) running in-process, and
// the PRODUCTION DataverseWebApiService (request building, HTTP, response parsing) talking to the real environment.
// What is substituted, and why: (1) inbound authentication — a fake scheme (as in DataverseIntegrationTestFixture),
// because an operator workstation cannot mint a user token for the BFF audience; (2) the OUTBOUND Dataverse identity
// — the operator's own `az login` (AzureCliCredential) instead of the BFF's managed identity, which does not exist off
// Azure. No client secret is created or used. ExternalDataService is exercised by calling the production service
// (resolved from the host's container with the same credential) — its routes need an external-caller principal and
// project grant that a fake identity cannot hold.
//
// Every record is named "zz-097-test …" and is deleted in `finally`, together with its sprk_eventlog rows.

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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
        var credential = new AzureCliCredential();

        await using var factory = new LiveEventsFactory(dataverseUrl, credential);
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");
        using var dv = await DataverseClientAsync(dataverseUrl, credential);

        var created = new List<Guid>();
        try
        {
            // 1. POST /api/v1/events — create with live priority and a Matter regarding.
            var post = await bff.PostAsJsonAsync("/api/v1/events", new
            {
                subject = "zz-097-test route create",
                priority = EventPriority.High,
                dueDate = "2026-10-20",
                regardingRecordType = RegardingRecordType.Matter,
                regardingRecordId = matterId,
            });
            Log("POST /api/v1/events", post);
            post.StatusCode.Should().Be(HttpStatusCode.Created);
            var id = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            created.Add(id);

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

            // 2. GET /api/v1/events/{id}
            var get = await bff.GetAsync($"/api/v1/events/{id}");
            Log($"GET /api/v1/events/{id}", get);
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await get.Content.ReadFromJsonAsync<JsonElement>();
            dto.GetProperty("statusCode").GetInt32().Should().Be(EventStatusCode.Open);
            dto.GetProperty("priorityName").GetString().Should().Be("High");
            dto.GetProperty("regardingRecordType").GetInt32().Should().Be(RegardingRecordType.Matter);

            // 3. GET /api/v1/events (list, filtered) — the read URL that used to 400.
            var list = await bff.GetAsync(
                $"/api/v1/events?regardingRecordType=1&regardingRecordId={matterId}&status=open&pageSize=50");
            Log("GET /api/v1/events?…", list);
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            (await list.Content.ReadAsStringAsync()).Should().Contain(id.ToString());

            // 4. PUT — re-parent to a Project, On Hold, Urgent: matter must be cleared, five fields rewritten.
            var put = await bff.PutAsJsonAsync($"/api/v1/events/{id}", new
            {
                statusCode = EventStatusCode.OnHold,
                priority = EventPriority.Urgent,
                regardingRecordType = RegardingRecordType.Project,
                regardingRecordId = projectId,
            });
            Log($"PUT /api/v1/events/{id}", put);
            put.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, id);
            Log("  read-back", row);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.OnHold);
            row.GetProperty("sprk_priority").GetInt32().Should().Be(EventPriority.Urgent);
            row.GetProperty("_sprk_regardingproject_value").GetGuid().Should().Be(projectId);
            row.GetProperty("_sprk_regardingmatter_value").ValueKind.Should().Be(JsonValueKind.Null, "re-parent clears the old lookup");
            row.GetProperty("sprk_regardingrecordtypelogicalname").GetString().Should().Be("sprk_project");

            // 5. PUT with a type but no id ⇒ 400, nothing written.
            var half = await bff.PutAsJsonAsync($"/api/v1/events/{id}", new { regardingRecordType = RegardingRecordType.Matter });
            Log("PUT (type without id)", half);
            half.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 6. POST /complete
            var complete = await bff.PostAsync($"/api/v1/events/{id}/complete", null);
            Log("POST /complete", complete);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, id);
            Log("  read-back", row);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Completed);
            row.GetProperty("statecode").GetInt32().Should().Be(0);
            row.GetProperty("sprk_completeddate").ValueKind.Should().Be(JsonValueKind.String);

            // 7. GET /logs — the audit rows really exist now (create, status update, complete).
            var logs = await bff.GetAsync($"/api/v1/events/{id}/logs");
            Log("GET /logs", logs);
            logs.StatusCode.Should().Be(HttpStatusCode.OK);
            var logBody = await logs.Content.ReadFromJsonAsync<JsonElement>();
            logBody.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);

            // 8. cancel + DELETE on two more events.
            var second = await CreateAsync(bff, "zz-097-test route cancel");
            created.Add(second);
            var cancel = await bff.PostAsync($"/api/v1/events/{second}/cancel", null);
            Log("POST /cancel", cancel);
            cancel.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, second);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Cancelled);
            row.GetProperty("statecode").GetInt32().Should().Be(1);

            var third = await CreateAsync(bff, "zz-097-test route delete");
            created.Add(third);
            var delete = await bff.DeleteAsync($"/api/v1/events/{third}");
            Log("DELETE", delete);
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
            row = await ReadEventAsync(dv, third);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Cancelled);

            // 9. A page beyond Dataverse's $top ceiling ⇒ explicit 400, not an empty page.
            var deep = await bff.GetAsync("/api/v1/events?pageNumber=200&pageSize=50");
            Log("GET page 200", deep);
            deep.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 10. External events — the production ExternalDataService against the same environment.
            var external = new ExternalDataService(new HttpClient(), factory.LiveConfiguration, credential,
                factory.Services.GetRequiredService<ILogger<ExternalDataService>>());
            var ext = await external.CreateEventAsync(projectId,
                new CreateExternalEventRequest { SprkName = "zz-097-test external create", SprkDuedate = "2026-10-21" });
            created.Add(Guid.Parse(ext.SprkEventid));
            _out.WriteLine($"ExternalDataService.CreateEventAsync -> id={ext.SprkEventid} sprk_status={ext.SprkStatus}");
            ext.SprkStatus.Should().Be(EventStatusCode.Open);
            var extList = await external.GetEventsAsync(projectId);
            var mine = extList.Single(e => e.SprkEventid == ext.SprkEventid);
            _out.WriteLine($"ExternalDataService.GetEventsAsync -> {extList.Count} rows; ours name='{mine.SprkName}' status={mine.SprkStatus}");
            mine.SprkName.Should().Be("zz-097-test external create");
            mine.SprkStatus.Should().Be(EventStatusCode.Open);
        }
        finally
        {
            foreach (var eventId in created)
                await DeleteEventWithLogsAsync(dv, eventId);
            _out.WriteLine($"cleanup: deleted {created.Count} zz-097-test events and their sprk_eventlog rows");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────

    private void Log(string what, HttpResponseMessage r) =>
        _out.WriteLine($"{what} -> HTTP {(int)r.StatusCode} {r.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

    private void Log(string what, JsonElement row) => _out.WriteLine($"{what}: {row}");

    private static async Task<Guid> CreateAsync(HttpClient bff, string subject)
    {
        var r = await bff.PostAsJsonAsync("/api/v1/events", new { subject });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
            "_sprk_regardingproject_value,_sprk_regardingrecordtype_value,sprk_regardingrecordid,sprk_regardingrecordname," +
            "sprk_regardingrecordnumber,sprk_regardingrecordurl,sprk_regardingrecordtypelogicalname");

    private static async Task DeleteEventWithLogsAsync(HttpClient dv, Guid id)
    {
        var logs = await dv.GetFromJsonAsync<JsonElement>($"sprk_eventlogs?$select=sprk_eventlogid&$filter=_sprk_event_value eq {id}");
        foreach (var log in logs.GetProperty("value").EnumerateArray())
            await dv.DeleteAsync($"sprk_eventlogs({log.GetProperty("sprk_eventlogid").GetString()})");
        await dv.DeleteAsync($"sprk_events({id})");
    }

    /// <summary>The BFF host with the production event service bound to the real environment.</summary>
    private sealed class LiveEventsFactory : DataverseIntegrationTestFixture
    {
        private readonly string _dataverseUrl;
        private readonly TokenCredential _credential;

        public LiveEventsFactory(string dataverseUrl, TokenCredential credential)
        {
            _dataverseUrl = dataverseUrl;
            _credential = credential;
            LiveConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = dataverseUrl,
            }).Build();
        }

        /// <summary>Only the live services read this; the rest of the host keeps the fixture's fake endpoints.</summary>
        public IConfiguration LiveConfiguration { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DataverseWebApiService>();
                services.AddSingleton<DataverseWebApiService>(sp => new LiveDataverseWebApiService(
                    new HttpClient(), LiveConfiguration,
                    sp.GetRequiredService<ILogger<DataverseWebApiService>>(), _credential));
                services.RemoveAll<IEventDataverseService>();
                services.AddSingleton<IEventDataverseService>(sp => sp.GetRequiredService<DataverseWebApiService>());
            });
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
