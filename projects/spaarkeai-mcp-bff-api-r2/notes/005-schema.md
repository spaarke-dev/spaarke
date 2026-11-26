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
| `sprk_playbooknoderun` | `sprk_confidence` | Decimal (precision 10, ±1000) | the node's numeric confidence score, so a stored run's detail returns it (a number, not content; added after the adversarial review) |

**Regarding record — pair, not polymorphic lookup.** A polymorphic lookup needs one relationship per target table and a schema change for every new subject; the run's subject comes from `PlaybookParameterPolicy.RecordIdentityParameters` (`matterId` → `sprk_matter`, `projectId` → `sprk_project`, `invoiceId` → `sprk_invoice`), which grows by code. The pair holds any of them with no further schema change. Cost: no referential integrity or cascade; acceptable for an audit-style run record.

### 2.1 Run-record columns (D-run-record-decided, 2026-11-25)

The owner decided `sprk_playbookrun` ("AI Run") is the one AI-run record for engine, prompted-Action and coded-workflow runs (`notes/decisions.md`). The same script therefore also carries the shared columns below. A profile-version lookup and a gate-decision reference are **not** here: specialist-legal-agents-r1 Phase 1 owns them.

| Table | Column | Change | Type / target | Required level | Choice made |
|---|---|---|---|---|---|
| `sprk_playbookrun` | `sprk_playbookid` | make optional (metadata update of the existing lookup; skipped if already `None`) | Lookup → `sprk_analysisplaybook` | ApplicationRequired → **None** | A prompted or coded run has no playbook. Done by GET of the lookup attribute, `RequiredLevel.Value = "None"`, PUT with `MSCRM.MergeLabels` (the repo's `Add-CustomerIdColumn.ps1` pattern) |
| `sprk_playbookrun` | `sprk_runkind` | add | Choice, **local** option set: Engine `100000000`, Prompted `100000001`, Coded `100000002` | None (form default Engine) | Spaarke's 100000000+ value convention; local, not a global set, because only this column uses it (the pattern of `Add-AnalysisToolAvailableInContexts.ps1`). The form default is Engine; a Web API write without the column stores null (DefaultFormValue applies to forms only), so the store always writes it and a future kind reader must treat null as unknown, not Engine (005 re-check K3) |
| `sprk_playbookrun` | `sprk_analysisactionid` | add | Lookup → `sprk_analysisaction` | None | Created through its 1:N relationship (a lookup cannot be created on the Attributes endpoint, per the skill) |
| `sprk_playbookrun` | `sprk_playbookconsumerid` | add | Lookup → `sprk_playbookconsumer`, display name "Binding" | None | same |
| `sprk_playbookrun` | `sprk_parentrunid` | add | Lookup → `sprk_playbookrun` (self) | None | Self-relationship, like `sprk_parentpersonaid` (`Create-AiPersonaEntity.ps1`) |
| `sprk_playbooknoderun` | `sprk_playbooknodeid` | make optional | Lookup → `sprk_playbooknode` | ApplicationRequired → **None** | An Action run's row has no node |
| `sprk_playbooknoderun` | `sprk_analysisactionid` | add | Lookup → `sprk_analysisaction` | None | same relationship pattern |

Plus the 9 columns in the table above (5 on `sprk_playbookrun`, 4 on `sprk_playbooknoderun`).

Relationship naming: `sprk_{referenced}_{referencing}_{lookup}` with the `sprk_` prefix dropped from the table names, as `scripts/Create-AiChatContextMapEntity.ps1` does (the skill's formula would double the prefix: `sprk_sprk_analysisaction_…`). Names: `sprk_analysisaction_playbookrun_analysisactionid`, `sprk_playbookconsumer_playbookrun_playbookconsumerid`, `sprk_playbookrun_playbookrun_parentrunid`, `sprk_analysisaction_playbooknoderun_analysisactionid`. Delete behaviour is **RemoveLink** on all four: a run record is an audit row and must outlive the Action, Binding or parent run it points at.

Idempotency: a missing column or lookup is added; an existing one is skipped when its type matches (Picklist / Lookup) and stops the script when it does not; the two required-level updates are skipped when the lookup is already `None`. `-WhatIf` reports every one of the 5 new columns/lookups and 2 updates without writing. The script's end-of-run verification re-reads all columns, lookups and the two required levels.

Code that goes with it (`PlaybookRunRecordStore`): the run write is an upsert that now also writes `sprk_runkind` always (Engine from `PlaybookOrchestrationService`), and `sprk_playbookid` / `sprk_analysisactionid` / `sprk_playbookconsumerid` / `sprk_parentrunid` only when the record carries them (the engine path sets only the playbook id). The node write skips a missing `sprk_playbooknodeid` and writes `sprk_analysisactionid` when given.

**Reading a row with no playbook id.** `GetOwnedRunAsync` (behind `GetRunStatusAsync` / `GetRunDetailAsync`) returns `null` for a stored row with no playbook id: it is a prompted or coded run, `PlaybookRunStatus` carries a playbook id, and a status with an empty `playbookId` would mislead. The run routes answer it like an unknown run id (404). It never throws, and the check runs before the owner lookup and node query. The read still names no new column (it keys off the existing `sprk_playbookid`), so the "no new column before the schema is applied → null → 404" property in §3 is kept. A reader for the other kinds is a later addition to the same store, built with its first caller. Tests: `PlaybookRunPersistenceTests` (`GetRunStatusAndDetail_StoredRunWithNoPlaybookId_ReturnNullWithoutThrowing`, `Store_PromptedRun_WritesKindAndActionBindingParent_…`, and the engine row carrying kind Engine and none of the new lookups).

**Owner go: NOT given.** The script is written, parse-checked and reviewed but has NOT been run against any environment, including no Web API write and no `-WhatIf` run (a `-WhatIf` run reads metadata with a token; none was made). POML escalation trigger 1; wave instruction. Result on dev: not run.

What the owner approves: running `pwsh scripts/dataverse/Add-PlaybookRunColumns.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (adds the 9 gap columns of §2 and the run-kind column plus 4 lookups of §2.1 to `SpaarkeCore`, makes the two existing lookups optional, then publishes the two tables). It is additive and idempotent; it deletes nothing (the two required-level changes loosen a constraint). `-WhatIf` prints the plan without writing.

## 3. Decisions (task 005)

- **Client: app-only `IGenericEntityService`** (the BFF managed identity), not the caller's OBO client. The run record is the BFF's own record of its execution, the tables are organization-owned, and a user without create rights on `sprk_playbookrun` must not lose run status. The caller is recorded in `sprk_triggeredby`.
- **`StartedByOid` is rehydrated from `sprk_triggeredby`** → `systemuser.azureactivedirectoryobjectid` (no stored oid column). `sprk_triggeredby` is written only for a Person run with a resolved systemuserid (the HTTP path's caller: WhoAmI over the caller's OBO token, so its AAD oid is the caller's oid). A System run has no triggered-by and rehydrates a null oid. Difference from memory: an app-only *Person* run has `StartedByOid = null` in memory but rehydrates its person's oid (that person may then read it).
- **Stored-run reads are owner-scoped.** `PlaybookOrchestrationService` is Scoped and `_activeRuns` is an instance field, so today a status/detail request never sees another request's run; the route ledger (`RouteAuthorizationGuardTests.Ledger.cs`, `CallerScopedOnly`, task 167) relies on that. A durable store read for anyone would expose every caller's runs to anyone holding a run id. So the store fallback returns a stored run only to the HTTP caller whose oid equals the stored `StartedByOid`; anyone else (and a call with no HTTP caller) gets the same null → 404 as today. No REST route was edited; no new status code.
- **Content stays out of the store (ADR-015).** Not persisted: node `TextContent` / `StructuredData` / tool results / warnings (status `outputs`), the detail `outputPreview`, and the user's `userContext`. For a stored run `outputs`, `outputPreview` and `userContext` come back null. Persisted: ids, timings, counts, tokens, the node's numeric confidence score, the redacted error text the REST routes already return, document ids (`sprk_inputcontextjson` = `{"documentIds":[…]}`), and a deterministic summary built from counts.
- **Node rows.** One row per node the run reached: Completed / Failed for a node that stored an output, Skipped for a node skipped before it produced one (branch not selected, dependency skip). Stored-run metrics derive from the rows: total = Completed + Failed rows, tokens summed over Completed rows, current node = the highest sequence.
- **Only Person runs are recorded.** A System run (the scheduler's hourly per-user fan-out) has no starter, so the owner-scoped read would never return it; recording it would add rows and Dataverse calls every hour for no reader. FR-13's readers (REST status, task 030's tool) are all Person-run readers.
- **No `IPlaybookRunRecordStore` interface (deviation from the POML, ADR-010 Path C).** The test seam is the Dataverse client (`IGenericEntityService`, ADR-038), so a second interface over the store would have one implementation and no double. The class is `public sealed` (not internal) because `PlaybookOrchestrationService`'s public constructor takes it; it is registered beside its only consumer in `AnalysisServicesModule.AddPlaybookServices`. ADR-013: only `Services/Ai` code uses it; task 030 reaches run status through `IPlaybookOrchestrationService`.
- **Correlation id** = the starting request's `TraceIdentifier`, captured on the run context at start (the run can outlive its request; a pooled `HttpContext` must not be read later); the run id for an app-only run. It is the same id the run's failure SSE event carries.
- **Legacy-mode runs** are now marked Running at their start event (they stayed Pending until the end); visible only in memory to the starting request, and it makes the stored start state Running for both modes.
- **Failure codes** (`sprk_errorcode`, Failed runs only): `playbook_node_failed`, `playbook_run_exception`, `playbook_has_no_nodes`, `legacy_requires_http_context`, `legacy_run_error`.
- **Fail-safe writes.** Every store call is bounded at 5 s by its token *and* by `WaitAsync` (the wait ends at the bound even if the Dataverse client ignores the token; the call is abandoned). A write failure logs `playbook_run_persist_failed` (run id, playbook id, node id, operation, error type, elapsed ms) and the run carries on unchanged. The run row is written by **upsert on the run id** at start and at completion, so the final write lands whether the start write failed, timed out after Dataverse committed it, or succeeded. If the start write fails, the node rows are skipped; the final write is still attempted. A Dataverse outage or missing columns therefore cost two failed calls per run.
- **Read failures are a 500, not a 404.** A failed or timed-out store read throws `InvalidOperationException("playbook_run_read_failed: …")`; the run routes already answer an exception with their 500. A 404 would tell the owner, during a Dataverse fault, that their run does not exist (a terminal answer). The read fails before ownership or existence is known, so the 500 leaks neither. Before the schema is applied no run row can exist (every write fails), and the run query names no new column, so the read returns null → 404 as before.
- **Owner check before node rows.** `PlaybookRunRecordStore.GetOwnedRunAsync` compares the caller's oid with the run's starter before it queries `sprk_playbooknoderun`, so another caller's request costs two queries, not three.

## 4. Known limits and found issues (owner decisions and issues to file; see task result)

Acceptance criterion 3 ("serialise identically") cannot hold byte for byte; items 1–3 and the ADR-015 exclusions above are the complete list of differences, and `PlaybookRunPersistenceTests` asserts each one explicitly (its fake keeps DateTime to the second, as Dataverse does). **Owner decision needed:** accept the criterion as "identical except these documented differences".

1. **Insights L2 gate-fail skip** (a node that both stores an output and counts as skipped): in memory it counts in `completedNodes` *and* `skippedNodes`; stored, it is a Completed row, so a stored run's `skippedNodes` is one lower on that Insights-only path. `sprk_noderunstatus` has no value for "skipped with an output", so an exact fix needs a new option value or run-level count columns — **owner decision** (bundled with criterion 3).
2. **Timestamps**: Dataverse DateTime keeps whole seconds, so a stored run's `startedAt` / `completedAt`, and the `metrics.duration` / `durationMs` derived from them, are the in-memory values truncated to the second. Node `durationMs` is stored as an integer and is exact.
3. **Node order and current node**: the in-memory detail lists nodes in `ConcurrentDictionary` hash order (not deterministic across processes); a stored run lists them by start sequence. In a parallel batch the in-memory `currentNodeId` is whichever node set it last; stored, it is the highest sequence.
4. **Orphaned Running runs (ISSUE TO FILE, owner: task 030 / FR-14)**: a run whose BFF instance restarts mid-run, or whose final write fails once, stays `Running` in Dataverse; the status route then reports Running indefinitely. The upsert closes the "start write timed out after committing" case; the rest needs a staleness rule (Running older than the longest legitimate run → reported as Failed `playbook_run_abandoned`), whose threshold is a product decision. Proposed acceptance: a stored Running run older than the threshold reads as Failed with that code; a test with a back-dated row proves it.
5. **Cancel across requests**: `POST /runs/{id}/cancel` still cancels only a run in this request's memory. For the owner's stored run it now returns 200 `{cancelled:false, "Run is not active (current state: …)"}` instead of 404. A non-owner still gets 404.
6. **Retention (ISSUE TO FILE)**: ADR-015 asks for retention on persisted AI artifacts. These rows hold run metadata plus the redacted error text, written for every interactive Person run, and no retention or bulk-delete rule exists for `sprk_playbookrun` / `sprk_playbooknoderun`.
7. **Status read right at the end of a run**: the final state is written before the SSE stream completes, so a client that waits for the stream to end reads the final state. A client polling from another request *between* the final event and the end of the stream can still read Running, once.
8. **Write cost (measure in task 019 / the latency step)**: every HTTP Person run makes 2 + N inline Dataverse calls (node writes run after each node, before the next batch). Insights ask, analysis execute and workspace summary are Person runs too. Whether every entry point should be recorded, or only the run routes and MCP, is a follow-up decision once measured.
9. **Deploy order**: until the columns exist, every Person run's two writes fail, and `DataverseServiceClientImpl` logs each at Error level (plus this service's Warning). The schema script must run in an environment before this build reaches it.

## 5. CAPABILITY-INDEX row proposal (main session applies; `.claude/` is out of a sub-agent's reach)

| Capability | Canonical component | Notes |
|---|---|---|
| AI run record (`sprk_playbookrun` "AI Run" / `sprk_playbooknoderun`) | `Services/Ai/PlaybookRuns/PlaybookRunRecordStore.cs`: **the one AI-run record writer for every run kind** (engine / prompted / coded; `PlaybookRunKind`, `sprk_runkind`) | Engine runs are written by `PlaybookOrchestrationService`; prompted and coded runs are written by their own callers (specialist-legal-agents-r1 Phase 1) through the same store and `PlaybookRunRecord`, never a second writer (ADR-002). Read engine run status through `IPlaybookOrchestrationService.GetRunStatusAsync` / `GetRunDetailAsync` (owner-scoped), never the store directly (ADR-013). D-run-record-decided, FR-13, task 005 |

**Extension point: write mode.** `PlaybookRunRecord` (and `PlaybookNodeRunRecord`) are request objects with init-only members, not positional parameter lists. Today every write is fail-safe: the store throws on failure and the caller logs `playbook_run_persist_failed`, bounds the call at 5 s and carries on. A later "required" write mode (fail closed for a gated side-effect run: the run does not proceed when its record cannot be written) is one more member with a fail-safe default on the same request object, read by the write path, so no existing caller changes. It is NOT built now. When it is added, the bounding and swallow logic now in `PlaybookOrchestrationService` (`BoundedAsync`, the catch in `PersistRunAsync`) should move into the store beside the mode, so a second caller does not copy it.

## 6. Ledger text (uac-r2 to review)

The `CallerScopedOnly` reasons for the four `/api/ai/playbooks/runs/{runId}…` routes in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` said "unreachable by construction" (a scoped in-memory store). Task 005 rewrote them to the new basis: the request's own in-memory run, else the stored run for the caller who started it only (`ReadOwnedStoredRunAsync`). No route, filter or status code changed.

## Rework re-check (main session, 2026-11-25; scoped sonnet pass over 99e18a53b3)

- F1, fixed by the main session: the required-level PUT now sets `@odata.type` = `Microsoft.Dynamics.CRM.LookupAttributeMetadata`, as `Add-CustomerIdColumn.ps1:126` does. Without it, Dataverse reads the PUT as the abstract base type and refuses it (0x80048d19), which would stop the owner's run after the columns were created. The script parse-checks clean. It still has not been run anywhere.
- K1: the end-of-run verification checks column and lookup types and the two required levels, but not option values, lookup targets or delete behaviour.
- K2: a missing attribute is detected by HTTP 404; any other status throws, which fails safe.
- K3: fixed in §2.1. The form default does not apply to Web API writes.
- K4: the read path tells "not an engine run" apart by a null playbook id, not by `sprk_runkind`. The first reader for prompted or coded runs must switch to the run kind.
- Unchanged: before the columns exist, every engine run's persist write fails safe (`playbook_run_persist_failed`), as before the rework (known limit 9: deploy order).
