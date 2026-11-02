# Task 019 — live checks A, B, C, negative, App Insights (dev)

> Run 2026-11-02 19:00-19:10Z against `spaarke-bff-dev` (master 3740e1a105, live since 18:52Z) and `spaarkedev1`.
> Caller: the owner's admin account (ralph.schroeder@spaarke.com, System Administrator), a delegated user token.
> Owner approved the `FMSWP-019` test-row creates in the coordinator's session on 2026-11-02.
> Rules kept: creates only, every read-back a GET, no metadata change, no bulk write, no update or delete. No token is recorded here.

## Result summary

| Check | Result |
|---|---|
| A. Child-records route, Event under the Matter | **PASS** on owner, stamp, number, search index and response. The "mapped fields" clause is **not met by design** (see A, "Mapped fields"): the owner needs to amend the acceptance text. |
| B. Office quick-create, Project under the same Matter | **PASS** (owner, mapping, search index, number, response shape). P2-2: alternate key exists and is Active (the 412 itself remains unverified, as instructed). |
| C. Chat-style create | **NOT RUN.** No non-UI route calls the `dataverse.create_record` handler. The owner runs it in the Spaarke Console chat (prompt below). |
| Negative (uniform refusal for an un-appendable parent) | **NOT RUN.** Needs a narrower user and that user's token. Candidates below. |
| App Insights | No 5xx. **One new exception type** since the deploy (see "Needs the owner", item 2). The child-records request and its logs are **missing from App Insights**. |

No 5xx, no owner mismatch between the two surfaces, no failed check, so the stop rule did not fire.

## Parent Matter

- **MAT-000032**, "Matter 123456.01 Word Add In 10-22-2026", id `49b8eaf8-df75-4724-b003-4391cf893907`.
- `sprk_issecure` = false, `statecode` = 0, name non-empty.
- Owner: team "Spaarke Business Unit 1", `cf15f587-baa0-f111-aaac-000d3a99d1d7`. In `team`: `teamtype` = 0 (Owner), `isdefault` = true. It is the default owner team of BU `cb15f587-baa0-f111-aaac-000d3a99d1d7`.
- BU search-index defaults: `sprk_searchindexname` = `spaarke-files-index`, `sprk_ai_search_index` = `fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6`.
- Field-mapping profiles (read-only): "Matter to Event (Attorney Matrix)" `24dc0ed2-537b-f111-ab0e-7ced8ddc4a05` and "Matter to Project (Attorney Matrix)" `69e55b30-92ce-f111-a05d-3833c5e9614d`. Each has 8 active Copy rules over the assigned-resource lookups.
- The matter has 6 of the 8 populated: attorney1 and 2, paralegal1 and 2, assigned-to-internal and assigned-to-external. Law firm 1 and 2 are empty.
- It was chosen because it has the most populated source fields. `modifiedon` was 2026-11-02T13:35Z, before these checks.

## Check A — `POST /api/v1/child-records/sprk_event`

**Request** (sent 19:00:44Z):
```
POST https://spaarke-bff-dev.azurewebsites.net/api/v1/child-records/sprk_event
Authorization: Bearer <omitted>
Content-Type: application/json

{"sprk_eventname":"FMSWP-019 A","sprk_RegardingMatter@odata.bind":"/sprk_matters(49b8eaf8-df75-4724-b003-4391cf893907)"}
```

**Response:** `201 Created` after about 8 s (response `Date` 19:00:52Z).
- `Location: /api/v1/child-records/sprk_event/fcdd4724-e243-4d37-9583-d694a8644318`
- Body: `{"id":"fcdd4724-e243-4d37-9583-d694a8644318"}`. This matches the route's contract: `{ id }` with no `warnings`.

**Row created: `sprk_event` `fcdd4724-e243-4d37-9583-d694a8644318`** (read back by GET at 19:01Z):

| Column | Value | Check |
|---|---|---|
| `ownerid` / type | `cf15f587-baa0-f111-aaac-000d3a99d1d7`, **team** "Spaarke Business Unit 1" | PASS. It is a team (not the admin user), and the Matter's owning team and the BU default owner team (`isdefault` = true). |
| `owningteam` / `owningbusinessunit` | the same team / `cb15f587-…` | OK |
| `createdby` / `modifiedby` | `# mi-bff-api-dev` (the app-only create) | as designed |
| `sprk_createdbyperson` | Ralph Schroeder (`1d02f31c-1872-f011-b4cb-7c1e52671ad0`) | OK. The creator is stamped. |
| I-1 stamp: `sprk_regardingmatter` | `49b8eaf8-…` (MAT-000032) | PASS |
| Other `sprk_regarding*` columns | The other core lookups (project, work assignment) are null. The ADR-024 pair (`sprk_regardingrecordid`, `…recordtype`, `…name`, `…url`, `…number`) is **null**: my body did not send it, and this route does not derive it. | recorded |
| `sprk_eventnumber` | **`EVT-000001`** | PASS. The format went live today, and this is the first event numbered under it. |
| `sprk_searchindexname` | `spaarke-files-index` | PASS (FR-38). It equals the owning BU's value; I sent none. |
| `sprk_ai_search_index` | `fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6` (spaarke-files-index) | PASS (FR-38). It equals the owning BU's lookup. |
| `sprk_accesspermission` | 100000000 (Standard) | OK |
| `statecode` / `statuscode` | 0 Active / 1 Draft | OK |

**Mapped fields.** None are present on the Event (no `sprk_assigned*` column set), although the "Matter to Event" profile exists.
- This matches the code: `ChildRecordEndpoints.CreateAsync` carries the remark "No field mapping is asked for here: the browser writers still map on the client until their wizard moves (FR-15/FR-16, tasks 020-029)", and it passes no `Mapping`.
- POML step 5 and the acceptance criterion ("mapped fields present") therefore cannot hold for this route until 020-029 land. Classified **K, by design**.
- The mapping on the server write path is proven by check B (Office quick-create passes a `Mapping`).
- **Owner decision:** amend the acceptance text, or confirm that W3 wires `Mapping` into this route.

**Verdict A: PASS** (owner, stamp, number, search index, response), with the mapped-fields clause marked by design.

## Check B — `POST /api/office/quickcreate/project`

**Request** (sent about 19:01:20Z):
```
POST https://spaarke-bff-dev.azurewebsites.net/api/office/quickcreate/project
Authorization: Bearer <omitted>
X-Idempotency-Key: fmswp-019-b-<epoch>
Content-Type: application/json

{"name":"FMSWP-019 B","description":"FMSWP-019 live check B","sourceEntityType":"sprk_matter","sourceRecordId":"49b8eaf8-df75-4724-b003-4391cf893907"}
```

**Response:** `201 Created`.
- `Location: /office/quickcreate/project/1cccf07d-0e0a-43f9-8ea0-f40533d5d691`
- `X-Idempotency-Status: new`, `X-RateLimit-Limit: 5`, `X-RateLimit-Remaining: 4`
- Body: `{"id":"1cccf07d-0e0a-43f9-8ea0-f40533d5d691","entityType":"Project","logicalName":"sprk_project","name":"FMSWP-019 B","url":null}`
- It matches `QuickCreateResponse` and `OfficeQuickCreateProjectContractTests`: 201; `id`, `entityType` "Project", `logicalName` "sprk_project", trimmed `name`; **no `warnings`** member on a clean create.
- The server-side duration was **18.3 s** (App Insights). The ~11 s gap before the final "PostCreate" trace is the six sequential assigned-access grants (see "Rows created by side effects").
- I did not exercise the Office error codes (owner_unresolved, OFFICE_022, OFFICE_009, record_number_unavailable): a negative would need a narrower user or a provoked collision.

**Row created: `sprk_project` `1cccf07d-0e0a-43f9-8ea0-f40533d5d691`:**

| Column | Value | Check |
|---|---|---|
| `ownerid` / type | `cf15f587-baa0-f111-aaac-000d3a99d1d7`, **team** "Spaarke Business Unit 1" | PASS. It is the same team as A. No cross-surface owner mismatch. |
| `owningbusinessunit` / `owningteam` | `cb15f587-…` / `cf15f587-…` | OK |
| `sprk_createdbyperson` | Ralph Schroeder | OK |
| Filing pair | `sprk_regardingrecordid` = `49b8eaf8-df75-4724-b003-4391cf893907`; `sprk_regardingrecordtype` = `e8547bb4-8600-f111-8407-7c1e520aa4df` (Matter) | PASS. The pair was derived by the adapter. A project is a root: the trace shows `StampAndInheritance: Skipped (the table carries no I-1 stamp…)`. |
| Mapped fields (profile "Matter to Project") | attorney1 = Test User 1; attorney2 = Jessica Broyles; paralegal1 = Eyal Iffergan; paralegal2 = Ralph Schroeder; assigned-to-external = CIAM Test User; assigned-to-internal = Ralph Schroeder. All six equal the Matter's values. | PASS. Trace: `FieldMapping: 6 column(s) mapped (fill-only), 0 warning(s)`. Law firm 1 and 2 were empty at the source, so they were not copied. |
| `sprk_projectnumber` | **`PRJ-000044`** | PASS (platform autonumber; the request carried none) |
| `sprk_searchindexname` | `spaarke-files-index` | PASS (equals the BU's value). Trace: `SearchIndexDefault: 2 value(s) written from the owning business unit (the only writer)`. |
| `sprk_ai_search_index` | `fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6` | PASS |
| `sprk_projectname` / `sprk_projectdescription` | "FMSWP-019 B" / "FMSWP-019 live check B" | OK |
| `sprk_issecure` | false | OK |
| `sprk_accesspermission` | 100000000 (Standard) | OK |
| `createdby` | `# mi-bff-api-dev` | as designed |

The app trace for the call (correlationId `330fbf189be04c1c81805da54e7b484d`): Principal, TableRules, FieldMapping, MapPayload, CallerChecks, SecureRootPlan ("ordinary"), Owner ("the resolver's business-unit team"), SearchIndexDefault, CreatorAndDefaults, StampAndInheritance (skipped), Create (one app-only create), PostCreate. Then `created sprk_project 1cccf07d-… (owner team cf15f587-…, isolated=False, warnings=0, mapping warnings=0)`.

**Verdict B: PASS.**

### P2-2 (the autonumber retry's 412 assumption): K, unverified, per instruction

- No collision was provoked.
- **Alternate key live (read-only `EntityDefinitions(LogicalName='sprk_project')/Keys`):** `sprk_projectnumber`, `KeyAttributes` = [`sprk_projectnumber`], `EntityKeyIndexStatus` = **Active**, MetadataId `eb6dee0f-c073-f011-b4cb-6045bdd6a665`.
- `sprk_event` has **no** alternate key (`Keys` is empty), so an event number never retries. Check that `RecordTableRules` does not set `AutonumberColumn` on `sprk_event` (I did not see one).
- **What the code expects:** `RecordCreationService.IsAlternateKeyDuplicate` (≈ line 1655) treats an `HttpRequestException` with `PreconditionFailed` (412) from the PATCH upsert, or the SDK `0x80060892` forms via `DataverseServiceClientImpl.IsAlternateKeyDuplicate`, as the duplicate. `MaxNumberAttempts` = 3, then 409 `record_number_unavailable`.
- Still unverified: whether Dataverse answers a **PATCH-upsert** duplicate-key collision with exactly 412. The key being Active is a precondition for the retry to be reachable.

## Check C — chat-style create (`dataverse.create_record`)

**Not run: no direct route exists.**
- `DataverseCreateRecordHandler` is `IToolHandler`, "chat-context-only (agent-loop tool). Playbook-context invocation is unsupported."
- The only entries into it are:
  - the chat agent loop (`POST /api/ai/chat/sessions/{id}/messages` and the model's choice of tool, with the side-effect gate), and
  - the gate confirm, `POST …/sessions/{id}/gates/{gateId}/resolve`, which runs `TypedHandlerResumeExecutor`.
- `GET /api/ai/tools/handlers` is only a discovery listing. There is no tool-invocation or test endpoint.
- Driving the real chat endpoints with a model in the loop would be non-deterministic and a mock of the UI, so I did **not** improvise.

**What the owner runs, in the Spaarke Console chat bound to MAT-000032 (or with the matter named in the prompt):**
> Create a to-do named "FMSWP-019 C" filed under matter MAT-000032 (Matter 123456.01 Word Add In 10-22-2026).

An Event works equally: "Create an event named FMSWP-019 C under matter MAT-000032". Confirm the gate when the assistant asks. Then read back by name: the row must show `sprk_regardingmatter` = `49b8eaf8-df75-4724-b003-4391cf893907` (the I-1 stamp present at create), a team `ownerid`, the number (`TODO-…` or `EVT-…`), and the search-index columns equal to the BU's values (events only; a to-do is not search-indexed). The row id will be in the assistant's confirmation chip. Add it to the cleanup list.

## Negative check — a parent the caller cannot AppendTo

**Not run.** The admin can AppendTo every record. It needs a narrower user's token (a user sign-in, or a token minted for that user); I cannot obtain one from the admin `az` login.

Existing dev users (read-only lookup of `systemusers` with their roles, all in BU "Spaarke Business Unit 1", `06fbf21c-…` is its parent):
- **`# UAC Child BU Test User`** (uac.child.user@demo.spaarke.com, `d6f8f439-40bf-f111-a05b-3833c5e9614d`). Roles: Spaarke Basic User and Spaarke Core User only (no Console / Office role). Best fit for the narrowest rights.
- **Test User 1** (testuser1@spaarke.com, `8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c`). Roles: Basic User, Office Add In User, Core User. Task 076's notes already use it as the "narrower" user for Family checks.

Suggested target: the secure matter **PAT-908068** (`5d46df87-e762-f111-ab0c-000d3a4d8152`, from task 076's notes). A user without Read on it cannot AppendTo it. The request is the same as A, with that matter in `sprk_RegardingMatter@odata.bind`. The expected outcome is the uniform 404 `child_record.not_found`, and no row is created. This also fits the second half of the acceptance criterion (a denied mapping source produces no mapped values and one warning), which needs the Office route with a `sourceRecordId` the caller cannot read.

## App Insights (step 8)

- Component: the one named by the BFF's own `APPLICATIONINSIGHTS_CONNECTION_STRING` (App Service `spaarke-bff-dev`), `ApplicationId` **6a76b012-46d9-412f-b4ab-4905658a9559**. The role instance is a single one.
- Run with `az monitor app-insights query --app 6a76b012-46d9-412f-b4ab-4905658a9559 --analytics-query "<q>"`, between 19:03Z and 19:09Z.
- The deploy is at 18:52Z (`MembershipJunctionUpdaterHost starting` at 18:52:48Z).

| # | Query | Result |
|---|---|---|
| Q1 | `requests \| where timestamp > ago(30m) \| where url has "/api/v1/child-records/" or url has "/api/office/quickcreate" \| summarize n=count() by name, resultCode \| order by name asc` | `POST /api/office/quickcreate/{entityType}` 201 × 1. **No `/api/v1/child-records/*` row** (see below). |
| Q1b | `requests \| where timestamp > datetime(2026-11-02T18:52:00Z) \| where name !has "healthz" and name != "GET /" and name != "GET /ping" \| summarize n=count() by name, resultCode` | `GET /robots933456.txt` 401, `GET /api/me` 401, `POST /api/office/quickcreate/{entityType}` 201. All three are anonymous-probe 401s or ours. |
| Q2 | `requests \| where timestamp > ago(30m) \| where toint(resultCode) >= 500 \| summarize n=count() by name, resultCode` | **0 rows: no 5xx.** The same query since 18:52Z: 0 rows. |
| Q3 | `exceptions \| where timestamp > ago(30m) \| summarize n=count(), tmin=min(timestamp), tmax=max(timestamp) by type` | `System.InvalidOperationException` × 6; `System.ServiceModel.FaultException`1[[OrganizationServiceFault]]` × 20 (19:00:52.95-19:00:54.45Z). |
| Q3b | the same since `datetime(2026-11-02T18:52:00Z)`, grouped by `type, outerMessage` | The InvalidOperationException × 3 is the known 403 "readability-proof read" (acme environment, `[ID-LINK-RECON]`). The FaultException × 20 is the `sprk_userentityassociation` miss (10 inner/outer pairs). |
| Q4 | `exceptions \| where timestamp between (datetime(2026-11-01T18:52:00Z) .. datetime(2026-11-02T18:52:00Z)) \| summarize n=count(), tmin=min(timestamp), tmax=max(timestamp) by type, op=operation_Name` | `System.InvalidOperationException` × 9 (18:10-18:50Z), all with the same 403 "readability-proof read" message. **The 24 h before the deploy had no FaultException.** |
| Q5 | `traces \| where timestamp > ago(30m) \| where message has "client_search_index_dropped" or message has "search_index_unset_for_owner_bu" \| project timestamp, severityLevel, message` | **0 rows.** The same since 18:52Z: 0 rows. Both checks sent no client index value, and the BU had a value, so neither warning was expected. |

### What the App Insights result means

- **No 5xx** on any route since the deploy.
- **A new exception type** is in the post-deploy window:
  - `FaultException<OrganizationServiceFault>` "A record with the specified key values does not exist in `sprk_userentityassociation` entity", wrapped as `InvalidOperationException` "Failed to retrieve sprk_userentityassociation by alternate key…" by `DataverseServiceClientImpl.RetrieveByAlternateKeyAsync`.
  - 10 occurrences within 1.6 s (19:00:52.8-19:00:54.4Z), with no `operation_Id`.
  - The path is the expected-miss branch of `MembershipJunctionUpdater.HandleAsync` (it catches both exception shapes). This service host started with this deploy.
  - Probable trigger: the membership events published after check A (start 19:00:50Z), which is about 2 s before the first probe. This is **unproven**, as there are no request-scoped traces for A (see below). Background jobs ran in the same seconds.
  - After the miss, `Created junction row` was not logged, `sprk_userentityassociation` has **0 rows** at all, and no row exists for either created record. So I could not show that a junction row was written.
  - No failure was reported by the updater. I classify this as noise from an expected miss that surfaces as an exception in telemetry, but the owner of the membership code should confirm that the empty junction table is expected.
- **The check A request is missing from App Insights.** There is no `requests` row, no `[CHILD-RECORD]` line, no `[RECORD-CREATE] sprk_event` trace and no dependency for 19:00:44-19:00:52Z, although the row exists and the response was 201. B is present. The itemCount on all rows since the deploy is 1, so it is not adaptive sampling. The pipeline is `UseAzureMonitor()` (OpenTelemetry), whose default sampler can drop a whole trace. So the **route's own traces for A cannot be shown**. The evidence for A is the response and the read-back.

## Rows created by side effects (for the cleanup list)

The Project's post-create step wrote **6 `sprk_assignedaccess` ledger rows** (the "Assigned *" grants, one per populated assigned-resource column), created 19:01:29-19:01:37Z, each filed under the new project:
- `d45745ca-efd6-f111-84e8-002248359bb2` (sprk_assignedattorney1)
- `d95745ca-efd6-f111-84e8-002248359bb2` (sprk_assignedattorney2)
- `68714ad0-efd6-f111-84e8-002248359bb2` (sprk_assignedparalegal1)
- `6a714ad0-efd6-f111-84e8-002248359bb2` (sprk_assignedparalegal2)
- `6b714ad0-efd6-f111-84e8-002248359bb2` (sprk_assignedtointernal)
- `6e714ad0-efd6-f111-84e8-002248359bb2` (sprk_assignedtoexternal)

They probably also created sharing grants (POA) on the project for those contacts' users. Deleting the project may or may not cascade to the ledger rows; the owner should check.

## Rows created — the owner's cleanup list

| Table | Id | Name |
|---|---|---|
| `sprk_event` | `fcdd4724-e243-4d37-9583-d694a8644318` | FMSWP-019 A (EVT-000001) |
| `sprk_project` | `1cccf07d-0e0a-43f9-8ea0-f40533d5d691` | FMSWP-019 B (PRJ-000044) |
| `sprk_assignedaccess` (side effect, ×6) | ids listed above | no `FMSWP-019` name; they belong to the project |

Nothing else was created. No Matter, contact or team was changed. Numbers consumed: `EVT-000001` and `PRJ-000044` (so deleting the rows does not give the numbers back).

## Needs the owner

1. **Check C** in the Console chat (prompt above), then record the row id here.
2. **Negative check:** a token for `# UAC Child BU Test User` or Test User 1, against PAT-908068.
3. **The new exception type** (`sprk_userentityassociation` miss, 10 occurrences at 19:00:52Z): confirm that the membership updater's miss path is expected to appear in telemetry, and why the junction table stays empty.
4. **Missing telemetry for A:** the child-records POST left no request or trace in App Insights. If this is the OpenTelemetry sampler, W3's App Insights step cannot rely on request counts for this route; use the read-back.
5. **Acceptance text for A:** "mapped fields present" cannot hold on the child-records route until 020-029 wire `Mapping` in (decide: amend, or confirm that W3 owns it).
6. **P2-2** stays K, unverified: the key exists and is Active; the 412 is not observed. The Project create took 18 s (six sequential grants), which is worth knowing for the add-in's timeout.
