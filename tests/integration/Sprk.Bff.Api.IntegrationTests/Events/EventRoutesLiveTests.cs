// spaarke-ontology-platform-r1 task 097 — LIVE route proof against a real Dataverse environment (review rounds 3, 5, 6
// and the round-8 merge with unified-access-control-r2 #1312).
//
// Opt-in only (skip-via-return, the repo's convention — see Membership/Phase2EndToEndTests LiveMode_* and
// tests/integration/seam/SpeAdmin/LiveIntegrationFixture): a plain `dotnet test` touches no network. Enable with
//   SPAARKE_LIVE_EVENTS_DATAVERSE_URL=https://<env>.crm.dynamics.com   (e.g. spaarkedev1)
//   SPAARKE_LIVE_EVENTS_MATTER_ID=<existing sprk_matter id>  SPAARKE_LIVE_EVENTS_PROJECT_ID=<existing sprk_project id>
//   SPAARKE_LIVE_EVENTS_DENIED_USER_ID=<systemuserid of a REAL non-root, non-admin user>   (optional: the child-BU leg)
//   SPAARKE_LIVE_EVENTS_INVOICE_ID=<existing sprk_invoice id with a number>   (optional: the regarding-number leg)
// and an `az login` session for an identity that may read/write sprk_event in that environment.
//
// WHAT IS REAL: the BFF host (routing, binding, RecordRouteAccessAuthorizationFilter, the EventEndpoints handlers)
// in-process; the PRODUCTION DataverseWebApiService (request building, HTTP, parsing, MSCRMCallerID impersonation), the
// production CallerSystemUserResolver, RecordOwnershipResolver (I-6, task 146) and CoreAncestorResolver, and the probe's
// RetrievePrincipalAccess / RetrieveUserSetOfPrivilegesByNames calls — Dataverse makes every allow/deny decision, for
// real systemusers.
//
// WHAT IS SUBSTITUTED, AND WHY:
//   (1) the inbound TOKEN — a fake scheme whose identity carries the acting user's REAL Entra oid (an operator
//       workstation cannot mint a user token for the BFF audience);
//   (2) the OBO exchange inside the probe — the operator's own `az login` Dataverse token; the PRINCIPAL asked about is
//       the acting user, so Dataverse answers for that user;
//   (3) the fixture's SDK client (a mock): its generic reads, entity-set lookups and record-type catalog reads are
//       answered from the real environment through the Web API (equality QueryExpressions only — anything else throws);
//   (4) the BFF's outbound identity — AzureCliCredential instead of the managed identity. No client secret is used.
// ExternalDataService is exercised by calling the production service directly.
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
        var otherUser = Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_DENIED_USER_ID"), out var d) ? d : (Guid?)null;
        var credential = new AzureCliCredential();

        using var dv = await DataverseClientAsync(dataverseUrl, credential);
        var operatorId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();
        _out.WriteLine($"operator systemuser (WhoAmI) = {operatorId}; child-BU user = {otherUser?.ToString() ?? "(not configured)"}");

        await using var factory = new LiveEventsFactory(dataverseUrl, credential, _out);
        await factory.ActAsAsync(operatorId);
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var createdEvents = new List<Guid>();
        var createdAnalyses = new List<Guid>();
        var leaks = new List<string>();
        try
        {
            // ── The operator: create / get / list / complete ─────────────────────────────────────────────────

            // 1. POST — live priority, a Matter regarding (Create privilege + AppendTo on the matter asked first).
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
            row.GetProperty("sprk_priority").GetInt32().Should().Be(EventPriority.High, "task 097: the LIVE priority value");
            row.GetProperty("_sprk_regardingmatter_value").GetGuid().Should().Be(matterId);
            row.GetProperty("sprk_regardingrecordid").GetString().Should().Be(matterId.ToString("D"));
            var matterTeam = await DefaultOwnerTeamOfRecordAsync(dv, "sprk_matters", matterId);
            row.GetProperty("_ownerid_value").GetGuid().Should().Be(matterTeam, "I-6 record-first: the parent's BU team");
            row.GetProperty("_sprk_createdbyperson_value").GetGuid().Should().Be(operatorId, "task 146: the person who asked");

            // 2. GET /{id}
            var get = await bff.GetAsync($"/api/v1/events/{id}");
            Log($"GET /api/v1/events/{id}", get);
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await get.Content.ReadFromJsonAsync<JsonElement>();
            dto.GetProperty("statusCode").GetInt32().Should().Be(EventStatusCode.Open);
            dto.GetProperty("priorityName").GetString().Should().Be("High");
            dto.GetProperty("regardingRecordType").GetInt32().Should().Be(RegardingRecordType.Matter);

            // 3. GET list, filtered — runs AS the operator; "my events" (decision B) includes the team-owned event the
            //    operator created (sprk_createdbyperson).
            var list = await bff.GetAsync($"/api/v1/events?regardingRecordType=1&regardingRecordId={matterId}&status=open&pageSize=50");
            Log("GET /api/v1/events?… (as operator)", list);
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            (await list.Content.ReadAsStringAsync()).Should().Contain(id.ToString());

            // 4. POST /complete — and the audit row is really written (task 097 F1: no sprk_description column).
            var complete = await bff.PostAsync($"/api/v1/events/{id}/complete", null);
            Log("POST /complete", complete);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);
            row = await ReadEventAsync(dv, id);
            row.GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Completed);
            row.GetProperty("statecode").GetInt32().Should().Be(0);
            var logs = await dv.GetFromJsonAsync<JsonElement>(
                $"sprk_eventlogs?$select=sprk_eventlogname,sprk_action&$filter=_sprk_event_value eq {id}");
            _out.WriteLine($"  sprk_eventlog rows: {logs.GetProperty("value")}");
            logs.GetProperty("value").EnumerateArray().Select(l => l.GetProperty("sprk_action").GetInt32())
                .Should().Contain(new[] { EventLogAction.Created, EventLogAction.Completed });

            // 5. OWNER DECISION A: a Reassigned event is completable. (Status set on this run's own zz event directly —
            //    no API route sets Reassigned since uac-r2 deleted PUT.)
            var reassigned = await CreateAsync(bff, "zz-097-test reassigned", createdEvents);
            (await dv.PatchAsJsonAsync($"sprk_events({reassigned})", new { statuscode = EventStatusCode.Reassigned, statecode = 0 }))
                .EnsureSuccessStatusCode();
            var completeReassigned = await bff.PostAsync($"/api/v1/events/{reassigned}/complete", null);
            Log("POST /complete (from Reassigned)", completeReassigned);
            completeReassigned.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadEventAsync(dv, reassigned)).GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Completed);

            // 6. An event regarding a CHILD (a zz analysis under the matter) carries the matter stamp (FR-26) on create.
            var analysisId = await CreateAnalysisAsync(dv, matterId, createdAnalyses);
            var childPost = await bff.PostAsJsonAsync("/api/v1/events", new
            {
                subject = "zz-097-test regarding analysis",
                regardingRecordType = RegardingRecordType.Analysis,
                regardingRecordId = analysisId,
            });
            var childEvent = await RegisterCreatedAsync(childPost, createdEvents);
            Log("POST (regarding analysis)", childPost);
            childPost.StatusCode.Should().Be(HttpStatusCode.Created);
            row = await ReadEventAsync(dv, childEvent);
            row.GetProperty("_sprk_regardinganalysis_value").GetGuid().Should().Be(analysisId);
            row.GetProperty("_sprk_regardingmatter_value").GetGuid().Should().Be(matterId, "the FR-26 core-ancestor stamp");
            row.GetProperty("sprk_regardingrecordnumber").GetString().Should().Be("ZZ-097-AN-001",
                "round 9: the number column the analysis catalog row names (sprk_analysis_number)");

            // 6b. Round 9: an event under an INVOICE carries the invoice's number (sprk_invoicenumber, from the catalog row).
            if (Guid.TryParse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_INVOICE_ID"), out var invoiceId))
            {
                var invoiceNumber = (await dv.GetFromJsonAsync<JsonElement>($"sprk_invoices({invoiceId})?$select=sprk_invoicenumber"))
                    .GetProperty("sprk_invoicenumber").GetString();
                var invPost = await bff.PostAsJsonAsync("/api/v1/events", new
                {
                    subject = "zz-097-test regarding invoice",
                    regardingRecordType = RegardingRecordType.Invoice,
                    regardingRecordId = invoiceId,
                });
                var invEvent = await RegisterCreatedAsync(invPost, createdEvents);
                Log("POST (regarding invoice)", invPost);
                invPost.StatusCode.Should().Be(HttpStatusCode.Created);
                var invRow = await ReadEventAsync(dv, invEvent);
                Log("  read-back", invRow);
                invRow.GetProperty("sprk_regardingrecordnumber").GetString().Should().Be(invoiceNumber);
            }

            // 7. An unfiled event → the operator's own business-unit team (I-6 fallback).
            var unfiled = await CreateAsync(bff, "zz-097-test operator unfiled", createdEvents);
            (await ReadEventAsync(dv, unfiled)).GetProperty("_ownerid_value").GetGuid()
                .Should().Be(await DefaultOwnerTeamOfUserAsync(dv, operatorId));

            // 8. A page beyond Dataverse's $top ceiling ⇒ explicit 400.
            var deep = await bff.GetAsync("/api/v1/events?pageNumber=200&pageSize=50");
            Log("GET page 200", deep);
            deep.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 9. External events — the production ExternalDataService (sprk_eventname / statuscode, project team owner).
            var external = new ExternalDataService(new HttpClient(), factory.LiveConfiguration, credential,
                factory.Services.GetRequiredService<ILogger<ExternalDataService>>());
            var projectTeam = await DefaultOwnerTeamOfRecordAsync(dv, "sprk_projects", projectId);
            var ext = await external.CreateEventAsync(projectId,
                new CreateExternalEventRequest { SprkName = "zz-097-test external create", SprkDuedate = "2026-10-21" }, projectTeam);
            createdEvents.Add(Guid.Parse(ext.SprkEventid));
            _out.WriteLine($"ExternalDataService.CreateEventAsync -> id={ext.SprkEventid} sprk_status={ext.SprkStatus}");
            ext.SprkStatus.Should().Be(EventStatusCode.Open);
            var listedExternal = (await external.GetEventsAsync(projectId)).Single(e => e.SprkEventid == ext.SprkEventid);
            listedExternal.SprkName.Should().Be("zz-097-test external create");

            // ── A REAL child-BU user: denied on the operator's root-BU event, allowed on its own ─────────────────
            if (otherUser is { } child)
            {
                await factory.ActAsAsync(child);
                var operatorsEvents = createdEvents.ToList();

                // Denied: the operator's UNFILED event is owned by the root BU team. No Read → the uniform 404 (uac-r2's
                // mechanism: a record the caller cannot read answers exactly like one that does not exist).
                _out.WriteLine($"RetrievePrincipalAccess({child}, sprk_events({unfiled})) = {await RetrievePrincipalAccessAsync(dv, child, "sprk_events", unfiled)}");
                var deniedGet = await bff.GetAsync($"/api/v1/events/{unfiled}");
                Log($"[as {child}] GET operator's event", deniedGet);
                deniedGet.StatusCode.Should().Be(HttpStatusCode.NotFound);
                var deniedComplete = await bff.PostAsync($"/api/v1/events/{unfiled}/complete", null);
                Log($"[as {child}] POST operator's event /complete", deniedComplete);
                deniedComplete.StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await ReadEventAsync(dv, unfiled)).GetProperty("statuscode").GetInt32().Should().Be(EventStatusCode.Open,
                    "no denied write reached Dataverse");

                // Denied: a parent it may not attach to (the operator-owned zz analysis).
                _out.WriteLine($"RetrievePrincipalAccess({child}, sprk_analysises({analysisId})) = {await RetrievePrincipalAccessAsync(dv, child, "sprk_analysises", analysisId)}");
                var deniedCreate = await bff.PostAsJsonAsync("/api/v1/events", new
                {
                    subject = "zz-097-test denied create",
                    regardingRecordType = RegardingRecordType.Analysis,
                    regardingRecordId = analysisId,
                });
                await RegisterCreatedAsync(deniedCreate, createdEvents);
                Log($"[as {child}] POST (parent it may not attach to)", deniedCreate);
                ((int)deniedCreate.StatusCode).Should().BeOneOf(403, 404);

                // Allowed: its OWN events.
                var childContact = await LinkedContactAsync(dv, child);
                var childTeam = await DefaultOwnerTeamOfUserAsync(dv, child);
                var ownPost = await bff.PostAsJsonAsync("/api/v1/events", new
                {
                    subject = "zz-097-test child-BU own event",
                    regardingRecordType = RegardingRecordType.Matter,
                    regardingRecordId = matterId,
                });
                var own = await RegisterCreatedAsync(ownPost, createdEvents);
                Log($"[as {child}] POST (own, regarding matter)", ownPost);
                ownPost.StatusCode.Should().Be(HttpStatusCode.Created);
                var ownRow = await ReadEventAsync(dv, own);
                Log("  read-back", ownRow);
                ownRow.GetProperty("_ownerid_value").GetGuid().Should().Be(matterTeam, "TEAM-owned (I-6), not the user");
                ownRow.GetProperty("_sprk_createdbyperson_value").GetGuid().Should().Be(child);

                (await bff.GetAsync($"/api/v1/events/{own}")).StatusCode.Should().Be(HttpStatusCode.OK);
                var ownComplete = await bff.PostAsync($"/api/v1/events/{own}/complete", null);
                Log($"[as {child}] POST own /complete", ownComplete);
                ownComplete.StatusCode.Should().Be(HttpStatusCode.OK);

                var own2 = await CreateAsync(bff, "zz-097-test child-BU own unfiled event", createdEvents);
                (await ReadEventAsync(dv, own2)).GetProperty("_ownerid_value").GetGuid().Should().Be(childTeam);

                // OWNER DECISION B: the list shows the team-owned events this user created (sprk_createdbyperson) —
                // and only its own (it owned / was assigned / created none before this run).
                var preExisting = await CountMineAsync(dv, child, childContact, exclude: new[] { own, own2 });
                _out.WriteLine($"[as {child}] events that were already its own before this run: {preExisting}; linked contact {childContact}");
                var childList = await bff.GetAsync("/api/v1/events?pageSize=100");
                Log($"[as {child}] GET /api/v1/events (as the caller)", childList);
                childList.StatusCode.Should().Be(HttpStatusCode.OK);
                var listed = (await childList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
                    .Select(i => i.GetProperty("id").GetGuid()).ToList();
                listed.Should().Contain(new[] { own, own2 }, "a team-owned event the BFF created for this user is in its list");
                listed.Should().NotIntersectWith(operatorsEvents, "the operator's events are not this user's");
                if (preExisting == 0)
                    listed.Should().BeEquivalentTo(new[] { own, own2 }, "it sees ONLY its own events");

                await factory.ActAsAsync(operatorId);
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

    /// <summary>
    /// Task 098 — the six sprk_event date columns are Behavior DateOnly in the target environment (spaarkedev1 since
    /// 2026-10-05; docs/data-model/sprk_event-date-columns.md). Proves, through the real routes and the production
    /// services: dates are stored and returned as "yyyy-MM-dd"; a timestamp is refused before Dataverse; /complete
    /// stores the operator's LOCAL date from their Dataverse time zone (run it between 20:00 and 24:00 Eastern and the
    /// UTC date differs); the external create stores the date an earlier SPA build's toISOString() meant.
    /// Same opt-in, same cleanup discipline as the 097 test; records are named "zz-098-test …".
    /// </summary>
    [Fact]
    public async Task LiveMode_EventDates_AreCalendarDates_AndCompletionIsTheCallersLocalDate()
    {
        var dataverseUrl = Environment.GetEnvironmentVariable(UrlVar);
        if (string.IsNullOrWhiteSpace(dataverseUrl))
            return; // not opted in

        var projectId = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_PROJECT_ID")!);
        var matterId = Guid.Parse(Environment.GetEnvironmentVariable("SPAARKE_LIVE_EVENTS_MATTER_ID")!);
        var credential = new AzureCliCredential();
        using var dv = await DataverseClientAsync(dataverseUrl, credential);
        var operatorId = (await dv.GetFromJsonAsync<JsonElement>("WhoAmI()")).GetProperty("UserId").GetGuid();

        // The operator's own Dataverse time zone, read independently of the BFF.
        var code = (await dv.GetFromJsonAsync<JsonElement>($"usersettingscollection({operatorId})?$select=timezonecode"))
            .GetProperty("timezonecode").GetInt32();
        var zoneName = (await dv.GetFromJsonAsync<JsonElement>($"timezonedefinitions?$select=standardname&$filter=timezonecode eq {code}"))
            .GetProperty("value")[0].GetProperty("standardname").GetString()!;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        _out.WriteLine($"operator {operatorId}: Dataverse time zone {code} = {zoneName}");

        await using var factory = new LiveEventsFactory(dataverseUrl, credential, _out);
        await factory.ActAsAsync(operatorId); // as the 097 leg: the routes resolve the caller (master)
        using var bff = factory.CreateClient();
        bff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "live-test");

        var createdEvents = new List<Guid>();
        var leaks = new List<string>();
        try
        {
            // a. POST: due and base date are stored as Edm.Date, exactly the day sent.
            var post = await bff.PostAsJsonAsync("/api/v1/events", new
            {
                subject = "zz-098-test dates",
                dueDate = "2026-10-20",
                scheduledStart = "2026-10-01",
                // Task 146 (master): an event is owned by its regarding record's team, never app-owned.
                regardingRecordType = RegardingRecordType.Matter,
                regardingRecordId = matterId,
            });
            var id = await RegisterCreatedAsync(post, createdEvents);
            Log("POST /api/v1/events (dueDate 2026-10-20, scheduledStart 2026-10-01)", post);
            post.StatusCode.Should().Be(HttpStatusCode.Created);
            var row = await ReadDatesAsync(dv, id);
            Log("  Dataverse read-back", row);
            row.GetProperty("sprk_duedate").GetString().Should().Be("2026-10-20");
            row.GetProperty("sprk_basedate").GetString().Should().Be("2026-10-01");

            // b. GET: all six as calendar dates (absent ones null).
            var dto = await (await bff.GetAsync($"/api/v1/events/{id}")).Content.ReadFromJsonAsync<JsonElement>();
            _out.WriteLine($"GET /api/v1/events/{id} -> {dto}");
            dto.GetProperty("dueDate").GetString().Should().Be("2026-10-20");
            dto.GetProperty("baseDate").GetString().Should().Be("2026-10-01");
            foreach (var p in new[] { "finalDueDate", "completedDate", "approvedDate", "meetingDate" })
                dto.GetProperty(p).ValueKind.Should().Be(JsonValueKind.Null);

            // d. A timestamp is refused at the API — it never reaches Dataverse (which answers 400 for an Edm.Date).
            var stamped = await bff.PostAsJsonAsync("/api/v1/events", new { subject = "zz-098-test timestamp", dueDate = "2026-10-20T00:00:00Z" });
            await RegisterCreatedAsync(stamped, createdEvents);
            Log("POST /api/v1/events (dueDate as a timestamp)", stamped);
            stamped.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // e. /complete: the operator's local date, from their Dataverse time zone — not the UTC date.
            var before = DateTimeOffset.UtcNow;
            var complete = await bff.PostAsync($"/api/v1/events/{id}/complete", null);
            var after = DateTimeOffset.UtcNow;
            Log("POST /complete", complete);
            complete.StatusCode.Should().Be(HttpStatusCode.OK);
            var stored = (await ReadDatesAsync(dv, id)).GetProperty("sprk_completeddate").GetString();
            var expected = new[] { before, after }
                .Select(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zone).DateTime).ToString("yyyy-MM-dd")).Distinct().ToList();
            _out.WriteLine($"  completed at {before:O} (UTC date {before.UtcDateTime:yyyy-MM-dd}); local date in {zoneName} = "
                + $"{string.Join(" / ", expected)}; Dataverse stored sprk_completeddate = {stored}");
            expected.Should().Contain(stored, "sprk_completeddate is the completing user's local calendar date");
            (await (await bff.GetAsync($"/api/v1/events/{id}")).Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("completedDate").GetString().Should().Be(stored);

            // f. External create with the shape earlier SPA builds sent (toISOString of the picked day's UTC midnight).
            var external = new ExternalDataService(new HttpClient(), factory.LiveConfiguration, credential,
                factory.Services.GetRequiredService<ILogger<ExternalDataService>>());
            var ext = await external.CreateEventAsync(projectId,
                new CreateExternalEventRequest { SprkName = "zz-098-test external legacy date", SprkDuedate = "2026-10-21T00:00:00.000Z" },
                await DefaultOwnerTeamOfRecordAsync(dv, "sprk_projects", projectId));
            createdEvents.Add(Guid.Parse(ext.SprkEventid));
            _out.WriteLine($"ExternalDataService.CreateEventAsync(sprk_duedate 2026-10-21T00:00:00.000Z) -> id={ext.SprkEventid} sprk_duedate={ext.SprkDuedate}");
            ext.SprkDuedate.Should().Be("2026-10-21");
            (await ReadDatesAsync(dv, Guid.Parse(ext.SprkEventid))).GetProperty("sprk_duedate").GetString().Should().Be("2026-10-21");
            (await external.GetEventsAsync(projectId)).Single(e => e.SprkEventid == ext.SprkEventid)
                .SprkDuedate.Should().Be("2026-10-21");
        }
        finally
        {
            foreach (var eventId in createdEvents.Where(e => e != Guid.Empty))
                leaks.AddRange(await DeleteEventWithLogsAsync(dv, eventId));
            _out.WriteLine($"cleanup: {createdEvents.Count(e => e != Guid.Empty)} zz-098-test events (+ their sprk_eventlog rows) "
                + $"attempted; {leaks.Count} NOT deleted" + (leaks.Count > 0 ? ":" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", leaks) : "."));
        }

        leaks.Should().BeEmpty("every zz-098-test record this run created must be gone");
    }

    private static async Task<JsonElement> ReadDatesAsync(HttpClient dv, Guid id) =>
        await dv.GetFromJsonAsync<JsonElement>(
            $"sprk_events({id})?$select=sprk_duedate,sprk_finalduedate,sprk_basedate,sprk_completeddate,sprk_approveddate,sprk_meetingdate");

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
            ["sprk_analysis_number"] = "ZZ-097-AN-001",
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

    private static async Task<JsonElement> ReadEventAsync(HttpClient dv, Guid id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"sprk_events({id})?$select=statuscode,statecode,sprk_priority,sprk_completeddate,_sprk_regardingmatter_value," +
            "_sprk_regardingproject_value,_sprk_regardinganalysis_value,_sprk_regardingrecordtype_value,sprk_regardingrecordid," +
            "sprk_regardingrecordname,sprk_regardingrecordnumber,sprk_regardingrecordurl,sprk_regardingrecordtypelogicalname," +
            "_ownerid_value,_sprk_assignedto_value,_sprk_createdbyperson_value");
        request.Headers.Add("Prefer", "odata.include-annotations=\"Microsoft.Dynamics.CRM.lookuplogicalname\"");
        using var response = await dv.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Expected owner (I-6), computed independently: the record's business unit's default owner team.</summary>
    private static async Task<Guid> DefaultOwnerTeamOfRecordAsync(HttpClient dv, string set, Guid id)
    {
        var bu = (await dv.GetFromJsonAsync<JsonElement>($"{set}({id})?$select=_owningbusinessunit_value"))
            .GetProperty("_owningbusinessunit_value").GetGuid();
        return await DefaultOwnerTeamOfBusinessUnitAsync(dv, bu);
    }

    private static async Task<Guid> DefaultOwnerTeamOfUserAsync(HttpClient dv, Guid systemUserId)
    {
        var bu = (await dv.GetFromJsonAsync<JsonElement>($"systemusers({systemUserId})?$select=_businessunitid_value"))
            .GetProperty("_businessunitid_value").GetGuid();
        return await DefaultOwnerTeamOfBusinessUnitAsync(dv, bu);
    }

    private static async Task<Guid> DefaultOwnerTeamOfBusinessUnitAsync(HttpClient dv, Guid businessUnitId) =>
        (await dv.GetFromJsonAsync<JsonElement>(
            $"teams?$select=teamid&$filter=_businessunitid_value eq {businessUnitId} and isdefault eq true and teamtype eq 0"))
        .GetProperty("value").EnumerateArray().Single().GetProperty("teamid").GetGuid();

    /// <summary>The user's linked contact (sprk_primarycontact), or null.</summary>
    private static async Task<Guid?> LinkedContactAsync(HttpClient dv, Guid systemUserId)
    {
        var row = await dv.GetFromJsonAsync<JsonElement>($"systemusers({systemUserId})?$select=_sprk_primarycontact_value");
        return row.TryGetProperty("_sprk_primarycontact_value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetGuid() : null;
    }

    private static async Task<int> CountMineAsync(HttpClient dv, Guid systemUserId, Guid? contactId, Guid[] exclude)
    {
        var filter = $"_ownerid_value eq {systemUserId} or _sprk_createdbyperson_value eq {systemUserId}"
            + (contactId is { } c ? $" or _sprk_assignedto_value eq {c}" : "");
        var rows = await dv.GetFromJsonAsync<JsonElement>($"sprk_events?$select=sprk_eventid&$filter={filter}");
        return rows.GetProperty("value").EnumerateArray().Count(r => !exclude.Contains(r.GetProperty("sprk_eventid").GetGuid()));
    }

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
        private readonly ITestOutputHelper _out;
        private readonly HttpClient _metadata;

        public LiveEventsFactory(string dataverseUrl, TokenCredential credential, ITestOutputHelper output)
        {
            _credential = credential;
            _out = output;
            LiveConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = dataverseUrl,
            }).Build();
            _metadata = DataverseClientAsync(dataverseUrl, credential).GetAwaiter().GetResult();
        }

        /// <summary>Only the live services read this; the rest of the host keeps the fixture's fake endpoints.</summary>
        public IConfiguration LiveConfiguration { get; }

        /// <summary>The real systemuser the requests act as (the probe's principal, the list's impersonation target).</summary>
        public Guid? CallerSystemUserId { get; private set; }

        /// <summary>That user's real Entra object id — the inbound <c>oid</c> claim (<see cref="LiveUserAuthHandler"/>).</summary>
        public string? CallerObjectId { get; private set; }

        /// <summary>Act as <paramref name="systemUserId"/>: its real oid becomes the inbound identity, so the PRODUCTION
        /// CallerSystemUserResolver, RecordOwnershipResolver (RequestedBy) and probe all resolve that user.</summary>
        public async Task ActAsAsync(Guid systemUserId)
        {
            var user = await _metadata.GetFromJsonAsync<JsonElement>($"systemusers({systemUserId})?$select=azureactivedirectoryobjectid");
            CallerSystemUserId = systemUserId;
            CallerObjectId = user.GetProperty("azureactivedirectoryobjectid").GetString();
            _out.WriteLine($"acting as systemuser {systemUserId} (oid {CallerObjectId})");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // The generic row reads, entity-set lookups and the record-type catalog the production resolvers use —
            // all answered from the real environment (the fixture's SDK client is a mock; no secret may be created).
            DataverseServiceMock.Setup(s => s.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string logicalName, CancellationToken ct) => MetaAsync(logicalName, ct).ContinueWith(t => t.Result.Set, ct));
            DataverseServiceMock.Setup(s => s.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string logicalName, CancellationToken ct) => LiveRecordTypeRefAsync(logicalName, ct));
            DataverseServiceMock.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string logicalName, Guid id, string[] columns, CancellationToken ct) => LiveRetrieveAsync(logicalName, id, columns, ct));
            // The PRODUCTION RecordOwnershipResolver (I-6) reads through IGenericEntityService.RetrieveMultipleAsync;
            // its simple equality QueryExpressions are translated to the Web API against the real environment.
            DataverseServiceMock.Setup(s => s.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken ct) => LiveRetrieveMultipleAsync(q, ct));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DataverseWebApiService>();
                services.AddSingleton<DataverseWebApiService>(sp => new LiveDataverseWebApiService(
                    new HttpClient(), LiveConfiguration,
                    sp.GetRequiredService<ILogger<DataverseWebApiService>>(),
                    sp.GetRequiredService<IRecordShareWriteObserver>(), _credential));
                services.RemoveAll<IEventDataverseService>();
                services.AddSingleton<IEventDataverseService>(sp => sp.GetRequiredService<DataverseWebApiService>());

                services.RemoveAll<CallerRecordAccessProbe>();
                services.AddSingleton<CallerRecordAccessProbe>(sp => new LiveProbe(this, LiveConfiguration,
                    sp.GetRequiredService<ILogger<CallerRecordAccessProbe>>()));

                services.RemoveAll<CoreAncestorResolver>();
                services.AddSingleton(sp => new CoreAncestorResolver(
                    sp.GetRequiredService<IGenericEntityService>(), LiveColumnsAsync,
                    sp.GetRequiredService<ILogger<CoreAncestorResolver>>()));

                // The linked contact (task 141), read live: the user's sprk_primarycontact, as the production service
                // reads first. Substituted only because the production service sits on the fixture's mocked SDK client.
                var identity = new Mock<IIdentityNormalizationService>();
                identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .Returns((Guid id, CancellationToken ct) => LinkedContactAsync(_metadata, id)
                        .ContinueWith(t => new PersonIdentity(id, ContactId: t.Result), ct));
                services.RemoveAll<IIdentityNormalizationService>();
                services.AddSingleton(identity.Object);

                // The inbound identity carries the acting user's REAL oid (substitution 1 narrowed: the token is still
                // fake, the identity in it is real), so the production CallerSystemUserResolver maps it for real.
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
            if (logicalName == "usersettings")
            {
                // Task 098: plain (non-lookup) columns of the caller's settings row.
                var settings = await _metadata.GetFromJsonAsync<JsonElement>(
                    $"usersettingscollection({id})?$select={string.Join(',', columns)}", ct);
                var result = new Entity(logicalName, id);
                foreach (var column in columns)
                    if (settings.TryGetProperty(column, out var value) && value.ValueKind == JsonValueKind.Number)
                        result[column] = value.GetInt32();
                return result;
            }

            var meta = await MetaAsync(logicalName, ct);
            string Col(string attr) => meta.Types.TryGetValue(attr, out var t) && IsLookup(t) ? $"_{attr}_value" : attr;
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{meta.Set}({id})?$select={string.Join(',', columns.Select(Col))}");
            request.Headers.Add("Prefer", "odata.include-annotations=\"Microsoft.Dynamics.CRM.lookuplogicalname\"");
            using var response = await _metadata.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var row = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var entity = new Entity(logicalName, id);
            foreach (var column in columns)
            {
                if (!row.TryGetProperty(Col(column), out var v) || v.ValueKind == JsonValueKind.Null)
                    continue;
                if (row.TryGetProperty($"{Col(column)}@Microsoft.Dynamics.CRM.lookuplogicalname", out var target))
                    entity[column] = new EntityReference(target.GetString(), v.GetGuid());
                else
                    entity[column] = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            }

            return entity;
        }

        private async Task<Entity?> LiveRecordTypeRefAsync(string logicalName, CancellationToken ct)
        {
            // The same columns the production QueryRecordTypeRefAsync selects (incl. the number column, round 9).
            var rows = await _metadata.GetFromJsonAsync<JsonElement>(
                $"sprk_recordtype_refs?$select=sprk_recordtype_refid,sprk_recorddisplayname,{RegardingRecordType.RecordNumberFieldColumn}"
                + $"&$filter=sprk_recordlogicalname eq '{logicalName}' and statecode eq 0&$top=1", ct);
            return rows.GetProperty("value").EnumerateArray().Select(r =>
                {
                    var e = new Entity("sprk_recordtype_ref", r.GetProperty("sprk_recordtype_refid").GetGuid());
                    foreach (var column in new[] { "sprk_recorddisplayname", RegardingRecordType.RecordNumberFieldColumn })
                        if (r.TryGetProperty(column, out var v) && v.ValueKind == JsonValueKind.String)
                            e[column] = v.GetString();
                    return e;
                })
                .FirstOrDefault();
        }
        private readonly Dictionary<string, (string Set, string PrimaryId, Dictionary<string, string> Types)> _meta = new();

        private async Task<(string Set, string PrimaryId, Dictionary<string, string> Types)> MetaAsync(string logicalName, CancellationToken ct)
        {
            if (_meta.TryGetValue(logicalName, out var m))
                return m;
            var def = await _metadata.GetFromJsonAsync<JsonElement>(
                $"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName,PrimaryIdAttribute", ct);
            var attrs = await _metadata.GetFromJsonAsync<JsonElement>(
                $"EntityDefinitions(LogicalName='{logicalName}')/Attributes?$select=LogicalName,AttributeType", ct);
            m = (def.GetProperty("EntitySetName").GetString()!, def.GetProperty("PrimaryIdAttribute").GetString()!,
                attrs.GetProperty("value").EnumerateArray().ToDictionary(
                    a => a.GetProperty("LogicalName").GetString()!, a => a.GetProperty("AttributeType").GetString()!));
            _meta[logicalName] = m;
            return m;
        }

        private static bool IsLookup(string type) => type is "Lookup" or "Owner" or "Customer";

        /// <summary>
        /// Translates the simple QueryExpressions RecordOwnershipResolver sends (equality conditions, a column set, TOP)
        /// into Web API reads against the real environment. Anything else throws — the harness never guesses.
        /// </summary>
        private async Task<Microsoft.Xrm.Sdk.EntityCollection> LiveRetrieveMultipleAsync(
            Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken ct)
        {
            var meta = await MetaAsync(query.EntityName, ct);
            string Col(string attr) => IsLookup(meta.Types[attr]) ? $"_{attr}_value" : attr;
            string Lit(object v) => v switch
            {
                Guid g => g.ToString("D"),
                bool b => b ? "true" : "false",
                int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string s => $"'{s.Replace("'", "''")}'",
                _ => throw new NotSupportedException($"Condition value type {v.GetType()} is not translated."),
            };
            var filters = query.Criteria.Conditions.Select(cond =>
                cond.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal && cond.Values.Count == 1
                    ? $"{Col(cond.AttributeName)} eq {Lit(cond.Values[0])}"
                    : throw new NotSupportedException($"Condition {cond.AttributeName} {cond.Operator} is not translated."));
            var columns = query.ColumnSet.Columns.Append(meta.PrimaryId).Distinct().Select(Col);
            var url = $"{meta.Set}?$select={string.Join(',', columns)}&$filter={string.Join(" and ", filters)}"
                + (query.TopCount is { } top ? $"&$top={top}" : "");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Prefer", "odata.include-annotations=\"Microsoft.Dynamics.CRM.lookuplogicalname\"");
            using var response = await _metadata.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var collection = new Microsoft.Xrm.Sdk.EntityCollection();
            foreach (var row in body.GetProperty("value").EnumerateArray())
            {
                var entity = new Entity(query.EntityName, row.GetProperty(meta.PrimaryId).GetGuid());
                foreach (var attr in query.ColumnSet.Columns)
                {
                    if (!row.TryGetProperty(Col(attr), out var v) || v.ValueKind == JsonValueKind.Null)
                        continue;
                    entity[attr] = IsLookup(meta.Types[attr])
                        ? new EntityReference(row.GetProperty($"{Col(attr)}@Microsoft.Dynamics.CRM.lookuplogicalname").GetString(), v.GetGuid())
                        : v.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Number => v.GetInt32(),
                            _ => Guid.TryParse(v.GetString(), out var g) ? g : v.GetString(),
                        };
                }

                collection.Entities.Add(entity);
            }

            Trace($"    I-6 read: {url} -> {collection.Entities.Count} row(s)");
            return collection;
        }

        internal HttpClient MetadataClient => _metadata;    }

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
            var principal = factory.CallerSystemUserId;
            if (principal is null)
                return AccessRights.None;
            var rights = await RetrievePrincipalAccessAsync(token, principal.Value, entitySet, recordId, ct);
            factory.Trace($"    probe: RetrievePrincipalAccess(principal {principal}, {entitySet}({recordId})) = {rights}");
            return rights;
        }

        /// <summary>
        /// The Create-privilege check (round 6, F3) for the principal under test: the same Dataverse function the
        /// production probe calls (RetrieveUserSetOfPrivilegesByNames), read by its production parser.
        /// </summary>
        public override async Task<bool> CallerHoldsPrivilegeAsync(string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            var token = await factory.DataverseTokenAsync(ct);
            var principal = factory.CallerSystemUserId;
            var names = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { privilegeName }));
            var response = await factory.MetadataClient.GetAsync(
                $"systemusers({principal})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1={names}", ct);
            var holds = response.IsSuccessStatusCode
                && ResponseGrantsPrivilege(await response.Content.ReadAsStringAsync(ct), privilegeName);
            factory.Trace($"    probe: {privilegeName} held by principal {principal} = {holds}");
            return holds;
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

    /// <summary>Authenticates every request as the acting user's REAL Entra object id (see LiveEventsFactory.ActAsAsync).</summary>
    private sealed class LiveUserAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        LiveEventsFactory factory)
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
