# Task 005 — playbook run tables: schema read, gap columns, decisions

## 1. Existing schema on dev (read-only, 2026-11-25)

Read with the Dataverse Web API `EntityDefinitions` endpoint (az CLI token, `https://spaarkedev1.crm.dynamics.com`), GET only. Both tables are **OrganizationOwned**, have **0 rows**, and have **no alternate keys**. Solutions containing `sprk_playbookrun`: Default, SpaarkeMasterTest2, SPRKDOCINTELLIGENCE, SpaarkeCore, SpaarkeAdmin, SpaarkeMaster (the script defaults to `SpaarkeCore`).

### `sprk_playbookrun` (entity set `sprk_playbookruns`, primary key `sprk_playbookrunid`, primary name `sprk_name`)

| Column | Type | Required | Notes |
|---|---|---|---|
| `sprk_name` | String(200) | ApplicationRequired | |
| `sprk_playbookid` | Lookup → `sprk_analysisplaybook` | ApplicationRequired | |
| `sprk_status` | Picklist, global `sprk_playbookrunstatus` | ApplicationRequired | see values below |
| `sprk_triggeredby` | Lookup → **`systemuser`** | ApplicationRequired | confirmed systemuser (not contact) |
| `sprk_startedon` | DateTime | None | |
| `sprk_completedon` | DateTime | None | |
| `sprk_errormessage` | Memo(100000) | None | |
| `sprk_inputcontextjson` | Memo(1048576) | None | |
| `sprk_outputsjson` | Memo(1048576) | None | not written by task 005 (ADR-015) |
| `statecode` / `statuscode` | State / Status | | |

`sprk_status` (`sprk_playbookrunstatus`): **0 Pending, 1 Running, 2 Completed, 3 Failed, 4 Cancelled** — the same five states as `PlaybookRunState`, so no escalation (POML trigger 2 does not fire). The code maps them by name (switch), not by ordinal.

### `sprk_playbooknoderun` (entity set `sprk_playbooknoderuns`, primary key `sprk_playbooknoderunid`)

| Column | Type | Required |
|---|---|---|
| `sprk_name` | String(200) | ApplicationRequired |
| `sprk_playbookrunid` | Lookup → `sprk_playbookrun` | ApplicationRequired |
| `sprk_playbooknodeid` | Lookup → `sprk_playbooknode` | ApplicationRequired |
| `sprk_status` | Picklist, global `sprk_noderunstatus` | ApplicationRequired |
| `sprk_durationms` | Integer | None |
| `sprk_tokensin` / `sprk_tokensout` | Integer | None |
| `sprk_errormessage` | Memo(100000) | None |
| `sprk_inputjson` / `sprk_outputjson` | Memo(1048576) | None (not written: ADR-015) |
| `sprk_validationwarnings` | Memo(100000) | None |

`sprk_status` (`sprk_noderunstatus`): **0 Pending, 1 Running, 2 Completed, 3 Failed, 4 Skipped**.

`systemuser.azureactivedirectoryobjectid` exists (Uniqueidentifier) — used to rehydrate `StartedByOid`.

## 2. Gap columns (`scripts/dataverse/Add-PlaybookRunColumns.ps1`)

| Table | Column | Type | Why |
|---|---|---|---|
| `sprk_playbookrun` | `sprk_correlationid` | String(100) | correlation / idempotency id. **No alternate key**: today's value (the request trace id, else the run id) is not unique per run by contract; task 030 decides uniqueness when it adds its idempotency key |
| `sprk_playbookrun` | `sprk_errorcode` | String(100) | stable failure code (closed set below) |
| `sprk_playbookrun` | `sprk_regardingentitytype` | String(100) | regarding record, **entity-name + id pair** (choice below) |
| `sprk_playbookrun` | `sprk_regardingrecordid` | String(100) | regarding record id (GUID "D" format) |
| `sprk_playbookrun` | `sprk_summary` | Memo(4000) | short, content-free summary |
| `sprk_playbooknoderun` | `sprk_startedon` | DateTime | |
| `sprk_playbooknoderun` | `sprk_completedon` | DateTime | |
| `sprk_playbooknoderun` | `sprk_sequence` | Integer (≥ 0) | start order within the run |

**Regarding record — pair, not polymorphic lookup.** A polymorphic lookup needs one relationship per target table and a schema change for every new subject; the run's subject comes from `PlaybookParameterPolicy.RecordIdentityParameters` (`matterId` → `sprk_matter`, `projectId` → `sprk_project`, `invoiceId` → `sprk_invoice`), which grows by code. The pair holds any of them with no further schema change. Cost: no referential integrity or cascade; acceptable for an audit-style run record.

**Owner go: PENDING.** The script is written and was reviewed but has NOT been run against any environment (POML escalation trigger 1; wave instruction). Result on dev: not run.

What the owner approves: running `pwsh scripts/dataverse/Add-PlaybookRunColumns.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (adds the 8 columns above to `SpaarkeCore`, then publishes the two tables). It is additive and idempotent; it deletes nothing. `-WhatIf` prints the plan without writing.

## 3. Decisions (task 005)

- **Client: app-only `IGenericEntityService`** (the BFF managed identity), not the caller's OBO client. The run record is the BFF's own record of its execution, the tables are organization-owned, and a user without create rights on `sprk_playbookrun` must not lose run status. The caller is recorded in `sprk_triggeredby`.
- **`StartedByOid` is rehydrated from `sprk_triggeredby`** → `systemuser.azureactivedirectoryobjectid` (no stored oid column). `sprk_triggeredby` is written only for a Person run with a resolved systemuserid (the HTTP path's caller: WhoAmI over the caller's OBO token, so its AAD oid is the caller's oid). A System run has no triggered-by and rehydrates a null oid. Difference from memory: an app-only *Person* run has `StartedByOid = null` in memory but rehydrates its person's oid (that person may then read it).
- **Stored-run reads are owner-scoped.** `PlaybookOrchestrationService` is Scoped and `_activeRuns` is an instance field, so today a status/detail request never sees another request's run; the route ledger (`RouteAuthorizationGuardTests.Ledger.cs`, `CallerScopedOnly`, task 167) relies on that. A durable store read for anyone would expose every caller's runs to anyone holding a run id. So the store fallback returns a stored run only to the HTTP caller whose oid equals the stored `StartedByOid`; anyone else (and a call with no HTTP caller) gets the same null → 404 as today. No REST route was edited; no new status code.
- **Content stays out of the store (ADR-015).** Not persisted: node `TextContent` / `StructuredData` / tool results / warnings (status `outputs`), the detail `outputPreview`, the user's `userContext`, and node `confidence`. For a stored run `outputs`, `outputPreview`, `userContext` and `confidence` come back null. Persisted: ids, timings, counts, tokens, the redacted error text the REST routes already return, document ids (`sprk_inputcontextjson` = `{"documentIds":[…]}`), and a deterministic summary built from counts.
- **Node rows.** One row per node the run reached: Completed / Failed for a node that stored an output, Skipped for a node skipped before it produced one (branch not selected, dependency skip). Stored-run metrics derive from the rows: total = Completed + Failed rows, tokens summed over Completed rows, current node = the highest sequence.
- **Only Person runs are recorded.** A System run (the scheduler's hourly per-user fan-out) has no starter, so the owner-scoped read would never return it; recording it would add rows and Dataverse calls every hour for no reader. FR-13's readers (REST status, task 030's tool) are all Person-run readers.
- **No `IPlaybookRunRecordStore` interface (deviation from the POML, ADR-010 Path C).** The test seam is the Dataverse client (`IGenericEntityService`, ADR-038), so a second interface over the store would have one implementation and no double. The class is `public sealed` (not internal) because `PlaybookOrchestrationService`'s public constructor takes it; it is registered beside its only consumer in `AnalysisServicesModule.AddPlaybookServices`. ADR-013: only `Services/Ai` code uses it; task 030 reaches run status through `IPlaybookOrchestrationService`.
- **Correlation id** = the starting request's `TraceIdentifier`, captured on the run context at start (the run can outlive its request; a pooled `HttpContext` must not be read later); the run id for an app-only run. It is the same id the run's failure SSE event carries.
- **Legacy-mode runs** are now marked Running at their start event (they stayed Pending until the end); visible only in memory to the starting request, and it makes the stored start state Running for both modes.
- **Failure codes** (`sprk_errorcode`, Failed runs only): `playbook_node_failed`, `playbook_run_exception`, `playbook_has_no_nodes`, `legacy_requires_http_context`, `legacy_run_error`.
- **Fail-safe.** Every store call is bounded (5 s) and caught; a failure logs `playbook_run_persist_failed` (run id, playbook id, node id, operation, elapsed ms) and the run carries on unchanged. If the run row's create fails, the run's later writes are skipped (`playbook_run_persist_skipped`), so a Dataverse outage or missing columns costs one failed call per run. A failed store read logs `playbook_run_read_failed` and reads as not found (the same 404 as before FR-13).

## 4. Known limits and found issues (to file; see task result)

1. **Insights L2 gate-fail skip** (stored output and skipped): stored as Completed, so a stored run's `skippedNodes` is one lower than memory for that Insights-only path.
2. **Timestamps**: Dataverse DateTime keeps whole seconds, so a stored run's `startedAt` / `completedAt` / `metrics.duration` / `durationMs` lose sub-second precision. Node `durationMs` is stored as an integer and is exact.
3. **Node order**: the in-memory detail lists nodes in `ConcurrentDictionary` hash order (not deterministic across processes); a stored run lists them by start sequence.
4. **Orphaned Running runs (to file)**: a run whose BFF instance restarts mid-run stays `Running` in Dataverse for good; the status route then reports Running forever. Fixing it needs a staleness rule (start-then-status consumers, FR-14 / task 030) and is out of this task.
5. **Cancel across requests**: `POST /runs/{id}/cancel` still cancels only a run in this request's memory. For the owner's stored run it now returns 200 `{cancelled:false, "Run is not active (current state: …)"}` instead of 404. A non-owner still gets 404.
6. **Retention (to file)**: ADR-015 asks for retention on persisted AI artifacts. These rows are content-free run metadata, but no retention or bulk-delete rule exists for `sprk_playbookrun` / `sprk_playbooknoderun`.
7. **Status read right at the end of a run**: the final state is written before the SSE stream completes, so a client that waits for the stream to end reads the final state. A client polling from another request *between* the final event and the end of the stream can still read Running, once.

## 5. CAPABILITY-INDEX row proposal (main session applies; `.claude/` is out of a sub-agent's reach)

| Capability | Canonical component | Notes |
|---|---|---|
| Durable playbook run status (`sprk_playbookrun` / `sprk_playbooknoderun`) | `Services/Ai/PlaybookRuns/PlaybookRunRecordStore.cs`, written and read only by `PlaybookOrchestrationService` | Read run status through `IPlaybookOrchestrationService.GetRunStatusAsync` / `GetRunDetailAsync` (owner-scoped), never the store directly (ADR-013). FR-13, task 005 |

## 6. Ledger text (uac-r2 to review)

The `CallerScopedOnly` reasons for the four `/api/ai/playbooks/runs/{runId}…` routes in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` said "unreachable by construction" (a scoped in-memory store). Task 005 rewrote them to the new basis: the request's own in-memory run, else the stored run for the caller who started it only (`ReadOwnedStoredRunAsync`). No route, filter or status code changed.
