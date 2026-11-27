# Task 037 — playbook run results widget: decisions

Status: **Owner chose option 1a on 2026-11-27** (notes/decisions.md D-037-results). Steps 2-6 resume on that basis.

## 1. Spike — where a finished run's outputs land, and how the caller reads them by run id (2026-11-26)

Base: `work/spaarkeai-mcp-bff-api-r2` at ded963dcf7 merged with `origin/master` (c438119bca). Paths are under
`src/server/api/Sprk.Bff.Api/` unless they start with `scripts/` or `projects/`.

### 1.1 The path a Copilot run takes

- `spaarke_run_playbook` → `IPlaybookRunAi.StartAsCallerAsync` → `DetachedPlaybookRunner.RunDetachedAsync`. The runner opens a
  new DI scope (`Services/Ai/PlaybookRuns/DetachedPlaybookRunner.cs:357`) and drains the engine's own entry,
  `IPlaybookOrchestrationService.ExecuteAsync` (`:376-377`). It does **not** go through the Analysis wrapper
  (`IAnalysisOrchestrationService.ExecutePlaybookAsync`), and no `sprk_analysis` record exists for the run.
- The runner keeps, per run, counts and state only (`DetachedRun.Apply`, `DetachedPlaybookRunner.cs:576-606`: it counts
  `NodeCompleted` and never reads `evt.NodeOutput`). This is 030's condition 2 (ADR-015).
- The engine keeps node outputs (`TextContent`, `StructuredData`) in `PlaybookRunContext` inside `_activeRuns`, an **instance
  field of a Scoped service** (`Services/Ai/PlaybookOrchestrationService.cs:63`, `:236`). The detached run's engine instance
  belongs to the scope the runner disposes when the drain ends (`DetachedPlaybookRunner.cs:357`, `await using`); the entry is
  also removed an hour later (`ScheduleCleanup`, `PlaybookOrchestrationService.cs:248`, `:877-878`). No later request (a status
  poll gets its own scope) can see it.
- The durable run record keeps no content: `sprk_outputsjson` and `sprk_outputjson` are not written; a stored run's `outputs`
  and `outputPreview` read back null (`projects/spaarkeai-mcp-bff-api-r2/notes/005-schema.md:19`, `:35`, `:91`). Adding them is
  forbidden by this POML and the 005 decision.

### 1.2 Per playbook kind (the runnable set, `spaarke_list_playbooks` = owned + public JPS playbooks)

The seed set (`scripts/seed-data/ai-content/sprk_analysisplaybook.json`, `sprk_playbooknode.json`, 13 playbooks / 26 nodes)
falls into four kinds:

| Kind | Examples (seed) | Where the output goes on the Copilot path | Caller-readable by run id? |
|---|---|---|---|
| **A. Analysis / report** — the output is text for the user | PB-009 Summarize an NDA, PB-004 Full Contract Analysis, PB-007 Risk Scan, PB-003 Email Analysis (no nodes: legacy mode); PB-010 Patent Office Action Review, PB-012 Document Summary (node-based, ending in a Deliver Output node, executor 40); PB-015 Summarize File (AI node only) | **Nowhere persisted.** Node-based: Deliver Output is pure — it formats `TextContent` and writes nothing (`Services/Ai/Nodes/DeliverOutputNodeExecutor.cs`: no Dataverse or store call); Deliver Composite likewise only carries a `destination` routing hint (`DeliverCompositeNodeExecutor.cs:221`, `:280-296`). The text reaches only the event stream, which the runner drops. Legacy (no nodes): the engine calls the wrapper with **no** `AnalysisId` (`PlaybookOrchestrationService.cs:908-913`), so the wrapper mints a random one (`Services/Ai/AnalysisOrchestrationService.cs:177`) and its writes of `sprk_workingdocument` (`:577`, `:821`, via `WorkingDocumentService.cs:44-50`, app-only `IGenericEntityService.UpdateAsync("sprk_analysis", …)`) target a record that does not exist; the failure is caught and logged (`WorkingDocumentService.cs:52-58`). The only other copy is a 2-hour app-level Redis entry keyed by that random id (`AnalysisDocumentLoader.cs:70-83`) | **No** |
| **B. Record writers** — the output is written onto the subject record | PB-002 Document Profile (Update Record → `sprk_document.sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, …); `matter-health-single` (Update Record `persistEnvelope` → `sprk_matter`) | On the subject record, by the Update Record node (`Services/Ai/Nodes/UpdateRecordNodeExecutor.cs`; field map in the node's `sprk_configjson`) | **Indirectly.** The run record holds the document ids (`sprk_inputcontextjson`, 005 §3) but the run view does not expose them (`IPlaybookRunAi.PlaybookRunView`), and which fields a run wrote is known only from the playbook's node config. The fields are readable as the caller (they are the document's / matter's own columns) |
| **C. Pre-fill** — structured fields for a form | PB-008 Create New Matter Pre-Fill, Create New Project Pre-Fill | Stream only (meant for a wizard's form); not persisted | **No** (and of little use in Copilot) |
| **D. Triggered / chat** | PB-013 Finance Invoice Processing (trigger 2, automatic), PB-014 SprkChat Document Assistant | Their own pipelines (invoice records; chat) | Out of scope for a user-started Copilot run |

Kind A is the main kind a Copilot user runs ("summarize this NDA", "run a risk scan"). Its output is not persisted anywhere,
so **escalation trigger 1 fires**. Every way to show it without changing where the run writes needs either output content in
the run record (forbidden, 005), an app-level cache read (Redis keyed by a random id: an app-only read of content, ADR-028), or
keeping content in BFF memory, which is per instance and lost on restart — not durable, against 030 condition 2. So
**escalation trigger 2 also fires.** Trigger 3 (64 KiB) is not reached: a report section can be truncated with a "more in
Spaarke" link, but only once there is somewhere for that link to open.

The REST/Console run has the same gap only when it calls the engine directly; the Analysis Workspace works because it first
creates the caller's `sprk_analysis` record (`Api/Ai/AnalysisEndpoints.cs:194`, "the ONE server create", gated by
`AnalysisAuthorizationFilter`: Create on `sprk_analysis` + `analysis.attach`) and then runs the wrapper with that id, which
persists the Deliver Output text into `sprk_workingdocument` (`AnalysisOrchestrationService.cs:281-287`, `:351-358`).

## 2. Options for the owner

| # | Option | What the user sees | Changes | Rules it touches |
|---|---|---|---|---|
| **1 (recommended)** | **The Copilot run writes the caller's analysis record**, like the Analysis Workspace: the start creates `sprk_analysis` as the caller (reusing the create route's core and `AnalysisAuthorizationFilter`), and the runner drains the existing wrapper `IAnalysisOrchestrationService.ExecutePlaybookAsync` with that id instead of the engine entry. The results tool reads `sprk_analysis.sprk_workingdocument` **as the caller** (`IDataverseUserClient`), so Dataverse security gives the uniform not-found | Kind A: the full report, in sections, with "open in Spaarke" opening the analysis in the Console (which today has no run view). Kind B: option 3's view can be added later | `DetachedPlaybookRunner` start path (030 code) and one more refusal at start (no Create on `sprk_analysis`). Engine untouched. **Open sub-question:** how the results tool finds the analysis from a run id. Candidates: (a) pass `analysisId` as a run parameter and add `analysisId → sprk_analysis` to `PlaybookParameterPolicy.RecordIdentityParameters`, so the existing `sprk_regardingentitytype`/`sprk_regardingrecordid` pair (005 §2, no new column) links them and 007 checks the caller can read it; (b) carry the analysis id only in the tools' `_meta` (widget-only) — fails when the widget opens from a later `spaarke_get_playbook_run` in a new turn on another instance | ADR-039: no engine edit, but option (a) edits the parameter policy beside the engine — confirm it is not frozen. ADR-015: content stays in the analysis record, as the Workspace does today. Writes `sprk_workingdocument` through the wrapper's existing app-only client (existing Workspace behaviour, unchanged). The wrapper also runs its canvas-to-node sync per run (`AnalysisOrchestrationService.cs:197-211`), as the Workspace does |
| 2 | **Status-only widget**: progress, then node names and states (completed / failed / skipped), no content | That it finished and which steps ran — not what it found | Widget + `_meta.ui` only; no results tool | Does not meet owner Q17 ("what it found") or the POML goal |
| 3 | **Subject-record view for kind B only**: the results tool reads, as the caller, the fields the playbook's Update Record nodes write on the run's documents / matter | Document Profile summary/TL;DR/keywords; matter health | A run-view member exposing the run's document ids; a read of the playbook's node config for the field map | Leaves kind A (the main kind) with nothing; reads node config (not an edit) |
| 4 | **In-memory results** in the runner's run table for 1 hour, served only to the starting caller | The report, sometimes: a poll on another instance, a restart or a scale-in loses it | Runner keeps content | Against 030 condition 2 (counts only) and ADR-015; not durable. Not recommended |

**Recommendation: option 1 with sub-option (a)**, plus option 3 later if Document Profile results matter in Copilot. It reuses
the record the Console already uses for analysis output (no new store, table or column), keeps every read as the caller, and
gives `consoleLink` a real target. It reopens 030's start path, so it needs the owner's go and a re-review of the runner (ADR-052
amendment conditions still hold: same detached helper, same caps, same pre-warm).

## 3. Implementation stop (2026-11-27): draining the wrapper loses the run

Base: `work/spaarkeai-mcp-bff-api-r2` b606cd8b41 merged with `origin/master` 6f4d7bc7e4. No code was written for steps 2-6.

D-037-results says the detached runner should drain `IAnalysisOrchestrationService.ExecutePlaybookAsync` (the wrapper) with the
caller's analysis id instead of the engine entry. It also says the run keeps 030's conditions, its `PlaybookRunRecordStore` record, and
a link to the analysis through `sprk_playbookrun`'s regarding columns. As the code stands, the wrapper cannot do all of that unless
the wrapper or the engine changes. Paths are under `src/server/api/Sprk.Bff.Api/Services/Ai/`.

| # | Gap | Evidence |
|---|---|---|
| G1 | **Legacy (no-node) playbooks get no run.** The wrapper runs them itself and never calls the engine. So there is no engine run id and no `sprk_playbookrun` record: the start has no run id to return, and `spaarke_get_playbook_run` answers not-found once the run leaves the run table (after 1 hour, or on another instance). These are kind A's main seed playbooks: PB-009, PB-004, PB-007 and PB-003 (§1.2) | `AnalysisOrchestrationService.cs:225-259`: only the node branch calls the engine. `:402` onward: the legacy branch runs its tools in sequence. Only the engine writes the record: `PlaybookOrchestrationService.cs:728-745`, `:905` |
| G2 | **Node playbooks: the runner cannot learn the engine's run id.** The wrapper yields `AnalysisStreamChunk`, and its bridge drops `RunId`. RunStarted becomes a text chunk, and the metadata and done chunks carry the analysis id | `AnalysisOrchestrationService.cs:195`, `:860-890` |
| G3 | **Node playbooks: the run loses its parameters.** The wrapper builds its own `PlaybookRunRequest` with no `Parameters` and no `RunUserId`. So 007's authorized parameters never reach the run, and the record's regarding (`ResolveRegarding(context.Parameters)`) stays empty: sub-option (a)'s run-to-analysis link cannot be written | `AnalysisOrchestrationService.cs:261-267`; `PlaybookOrchestrationService.cs:835`, `:845` |
| G4 | **No progress counts.** The runner counts `PlaybookStreamEvent` types, and the wrapper's chunks are text | `PlaybookRuns/DetachedPlaybookRunner.cs:576-606` |

Draining the engine, as 030 does today, keeps the run id, the record, the parameters and the counts. But the engine never writes
`sprk_workingdocument`:
- The node branch's persistence lives only in the wrapper: Deliver Output, then `AnalysisResultPersistence.UpdateWorkingDocumentAsync`
  and `FinalizeAnalysisAsync` (`AnalysisOrchestrationService.cs:319-370`).
- The engine's legacy mode calls the wrapper with no analysis id (`PlaybookOrchestrationService.cs:908`, #3285).

Three things are **not** the problem:
- **The HttpContext stop condition does not fire.** The wrapper reads only `httpContext.RequestServices` (`AnalysisOrchestrationService.cs:259`)
  and the OBO services the detached context already serves. It builds no `DefaultHttpContext`.
- **The analysis create is already shared.** `Api/Ai/AnalysisEndpoints.StartAnalysisAsCallerAsync` (task 031) is the create core:
  `AnalysisAuthorizationFilter.AuthorizeAnalysisCreateAsync`, then `CreateAnalysisRecordAsync`.
- **Every Copilot run has a document to anchor the analysis.** `RunPlaybookTool` requires `documentIds` with `minItems: 1`, and the
  analysis needs a document as its anchor.

### Options (the owner or the main session decides)

| # | Option | Edits | Keeps | Costs |
|---|---|---|---|---|
| **B (recommended)** | **Engine-first, with the analysis id on the run request.** Add `AnalysisId` to `PlaybookRunRequest`. The engine's legacy mode forwards it to the wrapper, which fixes #3285 at its root. A node run with an `AnalysisId` saves its Deliver Output at the end of the run through `AnalysisResultPersistence`, as the wrapper's node branch does: the wrapper's code moves to one shared method rather than being copied. The runner keeps draining the engine, with `AnalysisId` set from the analysis the server created | `PlaybookOrchestrationService` (one field forwarded, one save call); `PlaybookRunRequest`; the wrapper's node save moved to a shared method | Every 030 condition, the run id, the record, 007's parameters, the counts and the regarding link | An engine edit, which needs an ADR-039 exception (precedent: #3268's Path C). The REST/Workspace `/execute` route gets the field too, unused unless it sets it |
| A | **Wrapper-first, with pass-through.** Add `Parameters`, `RunUserId` and a run-id callback to `PlaybookExecuteRequest`. The wrapper forwards them and reports the engine's run id | `AnalysisOrchestrationService`; `PlaybookExecuteRequest` | For node playbooks: the run id, the record, the parameters and the regarding link | Legacy playbooks still get no run (G1). Either the runner mints its own id and `spaarke_get_playbook_run` needs a second durable source (the analysis row), or Copilot refuses legacy playbooks. The wrapper is the engine's legacy executor, so this is arguably an engine edit too |
| C | **Engine-first, node playbooks only, no engine edit.** The runner keeps draining the engine and binds the analysis id on the server. On a `NodeCompleted` event with `IsDeliverOutput`, it hands the text to `AnalysisResultPersistence` | `DetachedPlaybookRunner` | No engine edit | The runner reads node output, though 030 condition 2 says it keeps counts only. It copies the wrapper's node save (F2 duplication). Kind A's legacy playbooks (PB-009, PB-004, PB-007) show "results are in Spaarke" with no content until they move to nodes |

**Security condition, under every option:** the analysis id comes only from the server's own create during the start, never from
tool arguments or HTTP parameters.
- Under B it is a typed field on the run request, and no HTTP route binds that field from a request body. So `PlaybookParameterPolicy`
  needs no change and no uac-r2 review.
- Under A or C, if the id travels in parameters, it must be a server-owned key refused from HTTP callers (D-037-results).

## 4. Task 037a: the engine and start-path foundation (built 2026-11-27, D-037-engine option B)

Parts b (the results read tool) and c (the widget, `_meta.ui`, the CI wiring and the publish-size delta) are not built here.

### 4.1 What was built

| # | Change | Where (under `src/server/api/Sprk.Bff.Api/`) |
|---|---|---|
| 1 | `PlaybookRunRequest.AnalysisId` (`Guid?`, init). Only the Copilot start sets it | `Services/Ai/IPlaybookOrchestrationService.cs` |
| 2 | The analysis create core moved, unchanged, out of `Api/Ai/AnalysisEndpoints` into `AnalysisStartService : IAnalysisStartAi`, a PublicContracts facade. `AnalysisStartResult` and `AnalysisStartOutcome` moved with it, and `AnalysisStartReasonCodes` is new. The route handler calls `AnalysisStartService.CreateAnalysisRecordAsync`. `spaarke_start_analysis` and `DetachedPlaybookRunner` call `IAnalysisStartAi.StartAsCallerAsync`. The route's payload (`BuildCreatePayload`) and its refusal rendering (`CreateRefusalResult`, now `internal`) stay on the route. The route's bytes are unchanged (the analysis contract and auth suites pass unchanged) | `Services/Ai/AnalysisStartService.cs`, `Services/Ai/PublicContracts/IAnalysisStartAi.cs` |
| 3 | The Copilot start creates the caller's `sprk_analysis` after 007's authorization and the pre-warm, before the launch. It is created on `documentIds[0]`, with the playbook, and named `Copilot playbook run {yyyy-MM-dd HH:mm} UTC` (fixed text and the start time, no record text).<br>A create that does not return Created answers `PlaybookRunStartOutcome.AnalysisRefused`, with the create's result on `PlaybookRunStartResult.Analysis`, and nothing runs. `spaarke_run_playbook` maps it to `isError` with the create's `reasonCode` and status: `analysis.create_failed` and 500 when the create carries no code.<br>The ADR-052 amendment conditions are unchanged: the same helper, the cap of 3 (a refusal releases its reservation), the 50-minute cap, fail-closed and the pre-warm | `Services/Ai/PlaybookRuns/DetachedPlaybookRunner.cs`, `Api/Mcp/LegalOperations/Playbooks/RunPlaybookTool.cs` |
| 4 | The wrapper's node-branch save moved into one shared method, `AnalysisResultPersistence.SaveNodeRunAsync`, over `NodeRunResultCollector`. The wrapper calls it, and its output is unchanged. A characterization test, written before the move, proves it: `AnalysisNodeRunSaveTests` | `Services/Ai/AnalysisResultPersistence.cs`, `Services/Ai/NodeRunAnalysisSave.cs` |
| 5 | The engine: only the two changes in §4.3 | `Services/Ai/PlaybookOrchestrationService.cs` |
| 6 | The run record's existing regarding columns, through `PlaybookRunRecordStore.LinkRunsToAnalysis(analysisId)` (state on the scoped store). `DetachedPlaybookRunner.RunDetachedAsync` calls it on the detached scope's store before the engine starts. Every `sprk_playbookrun` write of that run then carries `sprk_regardingentitytype = sprk_analysis` and `sprk_regardingrecordid = {analysisId}`. There is no engine change and no schema change, and `PlaybookParameterPolicy` is unchanged | `Services/Ai/PlaybookRuns/PlaybookRunRecordStore.cs` |

### 4.2 The both-present rule

A run that has an `AnalysisId` **and** a record parameter (`matterId`, `projectId` and so on) is regarding **the analysis**:
- The analysis is the run's output record, and the only place the caller can read the results from. Part b reads it.
- The regarding columns hold one target, so one has to win. The record parameter is not lost: it stays in the run's parameters,
  and the engine uses it as before. The analysis is filed under the document.
- A run the engine starts inside the same detached scope is linked too. The legacy wrapper starts none; a future nested run would
  belong to the same analysis, which is accurate.
- **A behaviour change, stated (review K-4).** Before 037a, a Copilot run started with a `matterId` (or another record parameter)
  had its `sprk_playbookrun` regarding **the matter**. Since 037a it is regarding **the analysis**. A view or report that finds a
  matter's Copilot runs through the run record's regarding columns no longer finds them there; the matter id is still in the run's
  parameters (`sprk_inputcontext`). Runs started outside the Copilot start (REST, Console) are unchanged.

Tests:
- `PlaybookRunPersistenceTests.ExecuteAsync_LinkedToAnalysis_RegardingIsTheAnalysis_OverTheRecordParameter`: the real engine, with
  `matterId` and the link, checked on both writes.
- `WriteRunAsync_NoLink_WritesTheRunsOwnRegarding`.
- `LinkRunsToAnalysis_SetOnce`.

### 4.3 The engine diff (12 lines added, 2 removed; ADR-039 Path C, owner-approved)

- **(a)** In `ExecuteLegacyModeAsync`, the `PlaybookExecuteRequest` handed to the wrapper gets `AnalysisId = request.AnalysisId`
  (#3285). It is `null` for every entry except the Copilot start, so the wrapper mints its own id as before.
- **(b)** In the node branch of `ExecuteInternalAsync`, the writer passed to `ExecuteNodeBasedModeAsync` is
  `NodeRunAnalysisSave.SavingWriter(writer, request.AnalysisId, context.HttpContext?.RequestServices, context.RunId, _logger)`.
  - With no `AnalysisId`, it returns `writer` itself.
  - With one, it returns a decorator that watches the events. On the first terminal event (RunCompleted or RunFailed) it awaits
    `SaveNodeRunAsync` once, then forwards the event. The working document is therefore written before the reader sees the run end.
  - A save failure is logged (`playbook_run_analysis_save_failed`, with the exception type only) and never changes the run.
  - `AnalysisResultPersistence` is resolved from the run's request services, because the engine's constructor is already at the
    ADR-010 parameter limit.
- Nothing else changes: no executor, node, graph, control-flow or routing change, and the `ExecuteNodeBasedModeAsync` signature is
  the same.

### 4.4 Security: how `AnalysisId` is kept from callers

- `PlaybookRunRequest` is built in handlers, never bound from a request. ArchTest
  `PlaybookRunAnalysisIdGuardTests.NoApiCodeBindsThePlaybookRunRequest` checks that no method in `Sprk.Bff.Api.Api` takes one
  (lambdas included) and that no Api type carries one.
- Only `DetachedPlaybookRunner.StartReservedAsync` sets the field (an IL scan, `OnlyTheCopilotStartSetsTheAnalysisId`). It uses the id
  that `IAnalysisStartAi.StartAsCallerAsync` has just returned for this caller.
- Only `RunDetachedAsync` links the run records (`OnlyTheDetachedRunLinksTheRunRecords`).
- Must-fire and must-not-fire fixtures: `Detector_MustFireAndMustNotFire`.
- `spaarke_run_playbook` refuses an `analysisId` argument, and its schema has none
  (`PlaybookRunToolsContractTests.Start_AnalysisIdArgument_IsRefused`).

So the app-only save writes only to the analysis the server has just created for this caller.

### 4.5 Placement (CLAUDE.md §10; `.claude/constraints/bff-extensions.md`)

- Everything stays in the BFF, next to the code it moves or extends.
- `IAnalysisStartAi` sits in `Services/Ai/PublicContracts`, because bff-extensions says an MCP tool reaches AI services only through
  that facade. It references no Api type.
- `AnalysisStartService` reads two internal helpers on `AnalysisEndpoints`: the payload helper and the refusal helper. Other
  Services/Ai code reads `Api.Filters` in the same way (for example `DetachedPlaybookRunner`).
- No package, endpoint, store, schema or background work is added.
- There is one new singleton registration, `IAnalysisStartAi` (stateless). The ADR-010 1:1 ceiling is raised from 172 to 173 for
  it, with an entry that gives the reason.

### 4.6 Known limits

- **An analysis with no run.** The create can succeed and the launch then fail to start: NotAccepted after 5 s, Unavailable, or the
  caller cancelling. The empty analysis stays. It is the caller's own record and does no harm. Part b should show it as "no results".
  Cleanup (delete it as the caller, or leave it) is an owner question.
- **A retry with a new idempotency key creates a second analysis**, as `spaarke_start_analysis` does today.

## 5. Task 037b: the app-only results tool (built 2026-11-27)

Part c (the widget, the `_meta.ui` links on the two existing tools, the CI wiring and the publish-size delta) is not built here. Paths are
under `src/server/api/Sprk.Bff.Api/` unless they start with `tests/`.

### 5.1 What was built

| # | Change | Where |
|---|---|---|
| 1 | `IPlaybookRunAi.ReadResultsAsCallerAsync(HttpContext, runId, ct)` → `PlaybookRunResults?` (`null` = the uniform not-found), with `PlaybookRunResultsState` (`Pending`, `Available`, `None`) and `PlaybookRunSourceDocument` | `Services/Ai/PublicContracts/IPlaybookRunAi.cs` |
| 2 | The runner implements it: `ReadAsCallerAsync` (the one owner rule), then the run's link. A run still in the run table links through its in-memory analysis id and document ids (identifiers only, added to `DetachedRun`). Any other run links through the record's regarding columns, read by the new `PlaybookRunRecordStore.GetOwnedRunLinkAsync`, which re-checks the owner (`sprk_triggeredby` → oid) as `GetOwnedRunAsync` does | `Services/Ai/PlaybookRuns/DetachedPlaybookRunner.cs`, `PlaybookRunRecordStore.cs` |
| 3 | The reads, as the caller: `PlaybookRunResultsReader` (static, no DI) | `Services/Ai/PlaybookRuns/PlaybookRunResultsReader.cs` |
| 4 | `spaarke_get_playbook_run_results`: app-only, read-only, idempotent, title "Get a JPS playbook run's results", input `{ runId }` only (`additionalProperties: false`) | `Api/Mcp/LegalOperations/Playbooks/GetPlaybookRunResultsTool.cs` |
| 5 | The startup check: `McpToolAuthorization.AppOnlyTools`, an allow-list like `ModelCallableWrites`. `ValidateTool` refuses a tool named there whose `_meta.ui.visibility` is not exactly `["app"]` (model-visible, missing or empty) | `Api/Mcp/McpToolAuthorization.cs` |

The engine, the two existing playbook tools and the run record's columns are unchanged. Nothing is written.

### 5.2 How the results are read (as the caller, except identifiers)

1. `ReadAsCallerAsync`: another caller's run or an unknown id gives `null`.
2. The link. The analysis id comes only from the server: the run table's copy of `PlaybookRunRequest.AnalysisId`, or the record's
   `sprk_regardingentitytype = sprk_analysis` and `sprk_regardingrecordid`. 037a writes both. No argument names it, and the tool refuses
   any argument but `runId`.
3. With an analysis:
   - The analysis-read rule runs unchanged (`AnalysisAuthorizationFilter.IsAnalysisReadableAsync`, as `spaarke_get_analysis` uses it):
     Read on every anchor (the run's document), the creator branch and the external document narrowing. Its app-only read covers the
     anchor ids only.
   - Then the caller's own OBO read: `sprk_analysises({id})?$select=sprk_name,_sprk_documentid_value[,sprk_workingdocument]`. A 403 or
     404 is the not-found. Any other failure throws, and the tool answers 503 `playbook.run-read-failed` (the status tool's code).
   - The working document is selected only once the run has finished.
4. Without an analysis, the run's first document is its subject (see 5.3).
5. The source documents:
   - ONE OBO query: `sprk_documents?$select=sprk_documentid,sprk_documentname&$filter=sprk_documentid eq … or …`.
   - Then `InternalPlaneDocumentNarrowing.ForCallerAsync`.
   - A document the caller cannot read is left out and never named. Nothing is read while the run is still running.
6. The playbook name: the caller's OBO read of `sprk_analysisplaybooks({id})?$select=sprk_name`. A refused or failed read leaves it out,
   because the name is a label, not the subject.

### 5.3 Not-found and "no results" (the decision this task owed)

| Case | Answer | Why |
|---|---|---|
| An unknown run id; another caller's run (live or stored) | Not-found, byte-identical to `spaarke_get_playbook_run`'s | One owner rule |
| The analysis-read rule refuses (an unreadable anchor document or matter, the external narrowing, a row it cannot read) | Not-found | The run's subject |
| The caller's own read of the analysis is refused (403 or 404) | Not-found | The analysis the caller cannot read |
| A run with no analysis whose first document the caller can no longer read | Not-found | Its subject |
| **An owned run with no linked analysis** (started before 037a, a REST run, or a record writer whose regarding is a matter) | **`results: "none"`**. `consoleLink` is the Console on its first document; the readable documents are listed | The run is the caller's own, and the status tool already reports it. A not-found here would contradict a `completed` status on the same run id and show the widget an error for a run that worked. It reveals nothing the status tool does not |
| **An analysis whose working document is empty or whitespace** (a playbook that wrote to a record, a run that failed before its save, or the §4.6 known limit) | **`results: "none"`**. `consoleLink` is the analysis in the Console | The same reason; the caller can open the analysis |
| A run still running | `results: "pending"`, with its state and progress but no sections and no documents. The working document is not read | POML step 3 |
| A stored run still Running past the cap plus the margin | `status: "failed"`, `outcome: "interrupted"`, `results: "none"` | The status tool's own mapping (D-030 condition 7) |

Tests:
- `PlaybookRunResultsToolTests`: `EmptyWorkingDocument_IsNone`, `RunWithNoAnalysis_IsNone`, `RunRegardingAMatter_IsNone`,
  `InterruptedRun_IsNone`, `RunningRun_ReturnsItsStateWithNoContent`.
- `PlaybookRunResultsAuthTests`: every not-found row.

### 5.4 The result (part c's widget consumes this)

`structuredContent` uses `DecisionToolCall.Json` (camelCase; null members are omitted):

```
{
  runId: string (GUID),
  status: "running" | "completed" | "failed",                 // as the status tool
  outcome: "running" | "completed" | "failed" | "cancelled" | "interrupted",
  results: "pending" | "available" | "none",
  playbookId: string (GUID),
  playbookName?: string (at most 200 chars, one line),
  progress?: int 0-100 (only while running),
  reasonCode?: string (at most 64 chars),
  analysisId?: string (GUID),
  sections: [ { heading?: string (at most 200 chars, one line), text: string (Markdown), truncated: bool } ],
  truncated: bool,
  moreLink?: string (only when truncated; equals consoleLink),
  consoleLink?: string,
  documents: [ { documentId: string (GUID), name: string (at most 80 chars, one line), link?: string } ]
}
```

- `content` is one line with no output text, for example "JPS playbook run {id} (completed): 3 result section(s).".
- Sections: the working document is split at Markdown ATX headings (`#` to `######`, outside fenced code). Text before the first heading,
  or a document with no heading, is one section without `heading`. Section text keeps its Markdown.
- `consoleLink`:
  - With an analysis: `HandoffUrlBuilder.BuildAnalysisWorkspaceUrl(analysisId, documentId)`. It opens `sprk_spaarkeai` with `analysisId`,
    which is the Console's existing "open an existing analysis" entry (`launch-resolver.ts`, case 2d).
  - With no analysis: `IPlaybookRunAi.RunConsoleLink` on the run's first document (030's target).
  - A document's `link` is `RunConsoleLink` on that document.
- Errors are `isError` results:
  - `mcp.invalid_argument` (400) and `invalid_id` (400);
  - `sdap.access.deny.record_unavailable` (404, the uniform not-found);
  - `DATAVERSE_USER_CONTEXT_REQUIRED` (401);
  - `playbook.run-read-failed` (503).

### 5.5 Size (NFR-02)

- The sections share 36 KiB of worst-case JSON, plus 64 bytes per section, with at most 50 sections. Every character the default encoder
  may escape counts six bytes: non-ASCII, `"`, `\`, `<`, `>`, `&`, `'`, `+`, `` ` ``, controls and each surrogate half.
- Past the budget:
  - the section the cap falls in is cut, never mid surrogate pair, and dropped when less than 256 bytes would remain;
  - later sections are dropped;
  - `truncated` is true and `moreLink` opens the full analysis.
- 20 documents at 80 characters (six bytes each), the links and the header keep the worst case under 64 KiB. The test,
  `HugeResult_IsTruncatedUnder64KiB_WithAMoreLink`, uses 400 non-ASCII sections, 20 documents with 850-character CJK names and a
  5,000-character playbook name.

### 5.6 Logs

- The per-call line (`DecisionToolCall.LogAroundAsync`): tool, outcome, rows (= sections), ms and correlation id.
- One more line: the results state, the result's bytes, the section count, truncated, and the correlation id.
- A fault logs the exception type only; the reader logs status codes only.
- Test: `Logs_CarrySizesNotContent`. A marker in the text, a document name and the playbook name never reaches a log line.

### 5.7 Placement and justification (CLAUDE.md §10, §11)

- No package, endpoint, DI registration, store, schema or background work. The tool is found by `[McpArea]`; the reader is static.
- The new surface, with its three answers:
  - `ReadResultsAsCallerAsync`. Existing: `ReadAsCallerAsync` (state only) and `spaarke_get_analysis`'s read (an app-only loader).
    Extension: extending `ReadAsCallerAsync` would change the status tool's frozen view, and the route's read is app-only for content.
    Cost of doing nothing: the widget has no caller-scoped way to reach the results.
  - `GetOwnedRunLinkAsync`. Existing: `GetOwnedRunAsync`, which reads node rows and returns a shape the engine maps. Extension: adding
    the regarding columns there would change the query of every status read. Cost of doing nothing: a run no longer in the run table
    cannot find its analysis.
  - `McpToolAuthorization.AppOnlyTools`. Existing: the write check, which covers only tools that are not read-only. Cost of doing
    nothing: the POML's "refused at startup if declared otherwise" has no enforcement for a read-only tool.
- The ArchTest ledger row is in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.Playbooks.cs`.

### 5.8 Known limits and open questions

- **K1. Three OBO reads and the rule per finished results call** (the analysis, the documents, the playbook name). The widget should call
  this tool once per finished run, not per poll (the status tool is the poll). Part c should keep it that way.
- **K2. The no-analysis case lists the run's readable documents for a REST run too** (any run the caller started, read by run id). They
  are the run's own inputs, which the caller can read, and the status tool already exposes the run.
- **Q1 (part c).** Whether the widget renders section Markdown or plain text: the tool returns the Markdown as stored.
- **Q2 (owner, carried from §4.6).** An analysis created at the start whose run never started shows "none" here; its cleanup is still
  open.

## 6. Task 037c: the run widget and its wiring (built 2026-11-27)

Paths are under `src/server/api/Sprk.Bff.Api/` unless they start with `src/client/`, `tests/`, `scripts/` or `.github/`.

### 6.1 What was built

| # | Change | Where |
|---|---|---|
| 1 | The widget `ui://spaarke/playbook-run.html`, copied from 035's scaffold (Vite single file, `check-bundle-size.mjs`, jest, surface guard). No new npm package | `src/client/widgets/spaarke-playbook-run/` |
| 2 | The resource, a copy of 035's shape: registered only in `McpModule`'s resource collection, `_meta.ui.csp` empty, embedded with `Condition=Exists` (logical name `Sprk.Bff.Api.Widgets.playbook-run.html`), not built → a JSON-RPC error (never stale HTML), one startup warning, `Mcp:Enabled=false` → 503 | `Api/Mcp/LegalOperations/Playbooks/PlaybookRunWidgetResource.cs`, `Infrastructure/DI/McpModule.cs`, `Api/Mcp/McpEndpoints.cs`, `Sprk.Bff.Api.csproj` |
| 3 | `_meta.ui.resourceUri` on `spaarke_run_playbook` and `spaarke_get_playbook_run`. Nothing else on either changed: their text and `structuredContent` are pinned byte for byte, and their descriptors without `_meta` by SHA-256 (`PlaybookRunToolsContractTests.Frozen_*`, committed before the change and passing on both sides of it) | `RunPlaybookTool.cs`, `GetPlaybookRunTool.cs` |
| 4 | CI, mirroring every place the search results widget appears: build + size gate + embed check (publish and deploy), the built-case gate, the tier-1 build and jest steps and path filters, the conformance script and `Deploy-BffApi.ps1`'s widget loop | `.github/workflows/publish-bff-artifact.yml`, `deploy-bff-api.yml`, `ci-tier1-blocking.yml`, `tests/integration/Mcp.Conformance.Host/run-conformance.sh`, `scripts/Deploy-BffApi.ps1` |
| 5 | The shared transport guard lists the new widget's files, so it is proven to scan them (no fetch, no MSAL, no storage, no raw colour) | `src/client/widgets/shared/mcp/__tests__/widgetMcpTransport.test.ts` |

### 6.2 The widget

- **Opens from** the start result `{ runId, status, consoleLink }` or a status result `{ runId, status, progress?, result? }` (the host's `structuredContent`). An
  opening failure body (`{ reasonCode, message, status: <number> }`) is shown in plain words and nothing is called.
- **Header:** the playbook's name (from the results tool; "Playbook run" until then, because the status tool carries no name) and a state badge
  (Queued, Running, Completed, Failed, Stopped = cancelled, Interrupted), Fluent `Badge` palette colours.
- **While queued or running:** a `ProgressBar` from the status tool's `progress` (indeterminate when absent) and a line from its counts.
- **Poll schedule (bounded):** `spaarke_get_playbook_run { runId }` after 2, 3, 5, 8, 13 and 20 s, then every 30 s; never past the run's 50-minute cap
  (`PlaybookRunLimits.MaxRunDuration`) counted from `result.startedAt`, or from when the widget first saw the run when no start time is known (a start time in
  the widget's future is not trusted). The last poll lands on the cap. At most about 110 polls a run. It stops on a terminal state (`completed` / `failed`),
  on the uniform not-found, after 5 failed polls in a row, at the cap, on a new opening result and on unmount. After the cap or the failures it says why and
  offers "Check again" (one poll per press past the cap; the schedule resumes before it).
- **Once terminal:** `spaarke_get_playbook_run_results` ONCE per run (K1). The reader can ask again only after an error ("Try again") or a `pending` answer
  ("Check again"); neither is polled.
- **Results:** a Fluent `Accordion` (multiple, collapsible, the first section open); a section without a heading is "Results" (one section) or "Part N". A
  cut section says "This section was shortened. The full text is in Spaarke."; a truncated result says so and shows "More in Spaarke" (the `moreLink`,
  read only when `truncated` is true). "Open in Spaarke" is the `consoleLink`. The source documents are listed, each name a link-styled button when it has a link.
- **Plain-word states:** `none` ("The run finished but saved no results to show here."), not-found ("This playbook run was not found, or you can no longer open
  it."), errors by class (401: sign in again; a transport `widget.*` code: the host did not answer; others: Spaarke could not read the run) — never the server's text.
- **Links:** every link is a button that calls the host's `openLink` with an http(s) URL; the widget renders no anchor at all. A refused link shows a warning.
- **Theme and keyboard:** `FluentProvider` with the host theme; semantic tokens only. Every control is a native button (accordion headers, actions, document
  links), so it is keyboard operable (NFR-09); the progress line is a `role="status"` live region; errors are `role="alert"`.

### 6.3 Q1: how section Markdown is rendered (decided)

**A small safe subset, rendered as React text only** (`src/run/markdown.tsx`, about 300 lines, no dependency). Blocks: ATX headings (shown one level below the
section title), bullet and numbered lists, fenced code, block quotes, table rows (shown preformatted, as text), thematic breaks, and paragraphs whose lines are
kept with line breaks. Inline: bold, italic, inline code; `[text](url)` is shown as "text (url)", never as a clickable link (the widget's links are the
server-built ones). `__` and `_` inside a word are literal. Every string reaches the DOM as a text child: there is no `dangerouslySetInnerHTML`, no HTML parser
and no Markdown library (the surface guard forbids the first, and fails the build on it). Tests render `<script>`, `<img onerror>`, `<iframe>`, `<style>`, an
HTML and a Markdown `javascript:` link, and HTML inside code: no element, attribute or handler is created and the characters show as written. Rejected:
`react-markdown`/`marked` (+40-120 KB and an HTML-or-sanitizer path for a feature the subset covers) and plain text (analysis output is headed and listed, so it
would read as raw Markdown).

**The second renderer is a stated decision (review K-3).** The shared library already has one:
`src/client/shared/Spaarke.UI.Components/src/services/renderMarkdown.ts` (`marked` to HTML, then `DOMPurify`). The widget does not use it, for two reasons:
- **Its output is an HTML string**, which reaches the DOM only through `dangerouslySetInnerHTML` / `innerHTML`. The widget's surface guard
  (`widgetSurface.test.ts`, the same pattern list as the other MCP widgets) fails the build on either, so that model text can never become markup in the
  Copilot host's sandbox. A sanitizer is a second line of defence the widget does not need when no HTML is produced at all.
- **Bundle size.** `marked` and `DOMPurify` would add tens of KB (the estimate above; not measured) to a single-file widget that must stay under its 1 MB aim (0.89 MB now), for a
  subset of Markdown the widget renders without them.
Any other surface that renders model Markdown as HTML should keep using `renderMarkdown.ts`; this renderer is for the sandboxed widgets that may not
create markup.

### 6.4 Sizes

- **Bundle:** 928,100 bytes (0.89 MB), under the 1 MB aim and the 3 MB ceiling.
- **Publish size (all of 037, ADR-029):** two fresh worktrees, `C:\wt037m` (`origin/master` e140b01761) and `C:\wt037b` (this branch at 3808732bf5,
  with 037a, b and c and `origin/master` merged), `dotnet publish -c Release`, zipped with `Compress-Archive -CompressionLevel Optimal` (all files). Only this
  widget's `dist/` was present (in `wt037b`); no other widget was built on either side, so their embeds count on neither.

  | Build | Zip | Folder | Files |
  |---|---|---|---|
  | master | 43,719,619 bytes (41.69 MB) | 144,846,487 | 194 |
  | this branch | 43,641,929 bytes (41.62 MB) | 145,005,183 | 186 |

  Delta: **-77,690 bytes zipped (-0.07 MB)**, +158,696 bytes in the folder. The file counts differ by 8, all on master's side: `Microsoft.Agents.*` (7 dlls)
  and `AdaptiveCards.dll`, 1,029,704 bytes, which r2 task 001 removed with the legacy agent gateway (8a92da8751). Every other file is on both sides. The branch
  is the whole unmerged r2 project, so the delta is r2's, not 037's alone; 037's own share is the C# (a few KB) and this widget's embed (928,100 bytes raw,
  about 0.25 MB zipped by 035's ratio): `Sprk.Bff.Api.dll` is 30,533,120 bytes against master's 29,325,824. Under the 5 MB justification line; 41.62 MB total,
  under 55 MB. The embed was confirmed in the branch's published dll (`Sprk.Bff.Api.Widgets.playbook-run.html`).
- **CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package was added.

### 6.5 Placement Justification (CLAUDE.md §10; `.claude/constraints/bff-extensions.md`)

One more static, state-free `ui://` resource on the existing `/mcp/legal-operations` host, registered in `McpModule`'s resource collection (the one
sanctioned place), and a `_meta.ui.resourceUri` on two existing tool descriptors. No package, endpoint, DI registration, store, schema or background work; no
AI-internal type. Component justification is in `PlaybookRunWidgetResource.cs` (a fourth copy of the widget-resource shape; the shared factory is #3227/#3228).

### 6.6 Tests

- **Widget (jest, 107 pass):** `App.test.tsx` 24 (the schedule's exact poll times, the stop on a finished run with the results tool called once, the cap counted
  from `startedAt` and from first sight, 5 failures then "Check again", not-found, a new opening result, unmount; every results state; links through `openLink`
  and no anchor; a refused link; truncation and `moreLink`; HTML in a section inert; host theme; buttons), `markdown.test.tsx` 13 (the subset, six inert
  payloads, HTML in code), `playbookRun.test.ts` 22 (the three shapes, the schedule's bound), `widgetSurface.test.ts` 48 (source and built page; must-fire
  rows). `npm run build` (tsc, vite, size gate, built-page scan) passes.
- **Fail-before (widget, each restored):** `dangerouslySetInnerHTML` in `MarkdownText` → 10 fail; no cap in `nextPoll` → 2 App tests fail (and the bound test in
  `playbookRun.test.ts` never ends); polling resumed after a terminal state → 1 fails; `moreLink` read without `truncated` → 2 fail.
- **Shared transport guard:** `widgetMcpTransport.test.ts` + `hostTheme.test.ts`, 45 pass, the new files listed.
- **Server:** `PlaybookRunWidgetResourceTests` (8 + the build-dependent pair: the built case ran and passed here; the not-built case ran and passed with `dist/`
  moved away), `PlaybookRunToolsContractTests.Frozen_*` (3, committed first, pass before and after), `DecisionsWidgetResourceTests` (resource list),
  `PlaybookRunResultsToolTests.RealHost_PublishesTheToolAppOnly` (now expects the two links). Fail-before: with the `McpModule` entry removed and the tools'
  `resourceUri` renamed, 4 fail (resource list x2, built read, both descriptors); the frozen tests still pass under that change, as they should.
- **Runs:** `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "WidgetResource|Playbook|McpHostContract|McpCallerPath|Api.Mcp"`: 2,022 passed, 0 failed,
  4 skipped (each widget's other build-dependent twin). ArchTests `Mcp|RouteAuthorization`: 117 passed. `dotnet build -warnaserror`: 0 warnings.
  `check-changed-cs-format.sh origin/master`: clean.

### 6.7 Known limits and open questions

- **K3.** The playbook's name appears only once the run has finished (the status tool has no name, and the results tool is called once, K1).
- **K4.** A widget opened from a status result (not the start) has no console link while the run is running (the status result carries none).
- **K5.** The cap is counted on the widget's clock against the server's `startedAt`; a clock running behind the server's polls a little past the cap, one
  running ahead stops a little early ("Check again" covers it).
- **Q3.** Live rendering in Copilot Chat and Cowork, light and dark, is unverified until the dev deploy (039/043's E2E record), as for 035.

## 7. Task 037 review fixes (2026-11-27)

One fix per review finding. Each has a regression test that failed before the fix. That was shown by temporarily putting the pre-fix behaviour back in the
working tree (always finalize, no clip, the raw writer in the engine's catch, no `unsaved` state, no document cap, no fault catch, the old heading regex, no
AI-off check, no owner-rule texts), running the new tests (12 failed; the 3 that should pass under the old code passed: the heading-equivalence theory, the
failed-run negative case and the pure clip test), then restoring the fixed sources. For the widget, the committed versions of `App.tsx`, `playbookRun.ts` and
`markdown.tsx` were run against the new jest tests (6 failed: four hostile lines at 448-2,678 ms, the `unsaved` test and the `moreDocuments` test).

### 7.1 F-A: a failed save was swallowed

- `IWorkingDocumentService.TryUpdateWorkingDocumentAsync` writes `sprk_workingdocument` and returns false on failure. It logs only
  `analysis_working_document_write_failed`, the analysis id, the content length and the exception type (ADR-015). `UpdateWorkingDocumentAsync` is unchanged
  for its other callers (the legacy streaming path).
- `AnalysisResultPersistence.SaveNodeRunAsync` (shared by the Analysis wrapper's node branch and the engine's saving writer) writes through it. When the
  write fails it returns `ContentSaved: false` and does **not** finalize, so the analysis stays at statuscode 1. The content is clipped to 100,000 characters
  with a visible `[truncated — ...]` marker; a high surrogate at the cut is dropped, so no pair is split.
- `SavingRunWriter` logs `playbook_run_analysis_save_failed` (type only) when `ContentSaved` is false. The run is unchanged.
- `PlaybookRunResultsReader` reads `statuscode`: a run that **completed** with no text and an analysis not at 2 (finalized) is `Unsaved`. A failed run with no
  text stays `None`. The runner marks a run Completed only after the save, so a reader never sees a transient `unsaved`.
- The results tool returns `results: "unsaved"`, `resultsReason: "analysis_save_failed"` and the text "... finished, but its results could not be saved." The
  widget shows a warning: "The run finished but its results could not be saved. Open the analysis in Spaarke or run it again."
- Tests: `NodeRun_WithAnalysisId_SaveFails_IsLogged_TheAnalysisIsNotFinalized_TheRunStillCompletes` (the real `WorkingDocumentService` over a failing
  `IGenericEntityService`; asserts the outcome code and no finalize), `WriteFails_TheAnalysisIsNotFinalized_AndTheChunksAreUnchanged` (wrapper),
  `OversizeOutput_IsSavedClippedWithTheMarker`, `ClipToWorkingDocumentLimit_KeepsSurrogatePairsWhole`, `CompletedRun_UnfinalizedEmptyAnalysis_IsUnsaved`,
  `FailedRun_UnfinalizedEmptyAnalysis_IsNone`, and the widget's `unsaved` test.
- **Behaviour change for the Analysis wrapper:** its node branch also skips finalize on a failed write and clips its saved content. Its SSE chunks and
  the `Content` it returns are unchanged.

### 7.2 F-B: a node run that ended by an exception skipped the save

The saving writer is built once, before the `try`, and is used by the node branch and by the general `catch`'s RunFailed write. The whole engine diff
(`PlaybookOrchestrationService.ExecuteInternalAsync`):

```diff
+        // ... Task 037 review F-B: built once, so a node run that ends by an exception (the RunFailed below) saves its streamed text too;
+        // a cancelled run is not saved, as the wrapper does not.
+        var nodeRunWriter = NodeRunAnalysisSave.SavingWriter(writer, request.AnalysisId, context.HttpContext?.RequestServices, context.RunId, _logger);
         try
 ...
-                await ExecuteNodeBasedModeAsync(context, nodes,
-                    NodeRunAnalysisSave.SavingWriter(writer, request.AnalysisId, context.HttpContext?.RequestServices, context.RunId, _logger),
-                    cancellationToken);
+                await ExecuteNodeBasedModeAsync(context, nodes, nodeRunWriter, cancellationToken);
 ...
-            await writer.WriteAsync(PlaybookStreamEvent.BuildRunFailedEvent(
+            await nodeRunWriter.WriteAsync(PlaybookStreamEvent.BuildRunFailedEvent(
```

The 037a comment moved with the writer. Nothing else in the engine changed. Legacy mode still writes to the raw writer. An exception in legacy mode now
sends RunFailed through the saving writer, which has seen no text, so it writes and finalizes nothing, as before.

- **Cancel (decided): no save,** matching the wrapper. The wrapper reads the engine's channel with `ReadAllAsync(cancellationToken)`, so a caller's cancel
  throws before its save. The cancel `catch` still writes RunCancelled to the raw writer. Known edge: a Stop through the run context's own token in the wrapper
  would save partial text; Copilot runs do not use it.
- Test: `UpstreamTimeout_SavesTheStreamedText` (a node's `TaskCanceledException` that the caller did not ask for ends the run in the general catch; the
  streamed text is saved). A cancel test was dropped because it depended on timing.

### 7.3 F-C: a backtracking heading regex could throw out of the tool

- `GetPlaybookRunResultsTool.HeadingText` is a linear scanner that reads headings as the old pattern did (the `HeadingText_MatchesTheOldPattern` theory).
  Building and logging the result is wrapped: any fault logs the exception type only and returns `GetFailed`.
- Widget: `parseHeading` replaces the HEADING regex. `parseInline` memoises its forward searches (backtick, `**`, `__`, emphasis closers, `](` and `)`) and runs
  the intraword checks before any search, so a line is read in linear time.
- Tests: `LongWhitespaceHeading_IsLinear_AndTheToolAnswers` ("# a" + 50,000 spaces + "x"; 996 ms before the fix, quick after),
  `ResultBuildingFault_IsCouldNotRead`, and the widget's "hostile input" set (seven 36 KB lines, under 250 ms each; all 4 ms or less after the fix).

### 7.4 F-D: the document list was uncapped

`PlaybookRunLimits.MaxDocuments = 20` (shared with `RunPlaybookTool`'s input cap). The reader queries at most the first 20 ids and reports the rest as
`MoreDocuments`. The tool returns `moreDocuments`, and the widget shows "And N more documents." when it is above 0. Test:
`SixtyDocuments_AreCappedAtTwenty_UnderTheSizeBound` (under 64 KiB, at most 20 ids queried).

### 7.5 K-1 to K-4

- **K-1.** With AI off (`NullPlaybookOrchestrationService`), `DetachedPlaybookRunner` returns Unavailable before it creates the analysis. Test:
  `Start_AiDisabled_IsUnavailable_AndCreatesNoAnalysis`.
- **K-2.** An owner-rule 403 from the analysis create (`SecureDesignationRemoval.NotPermittedReasonCode`, `PermissionUnverifiableReasonCode`) gets text that
  matches its code; the code and status are unchanged. This changes `spaarke_run_playbook`'s model-visible error text. That text was added by 037a, and the
  review asked for this change. Test: `Start_OwnerRuleRefusal_TextMatchesItsCode` (3 rows).
- **K-3.** §6.3 now names `renderMarkdown.ts` and says why the widget does not use it.
- **K-4.** §4.2 now records that a Copilot run's record is regarding the analysis, not the matter.

### 7.6 Sizes and runs

- Widget bundle: 929,020 bytes (+920 bytes).
- .NET `PlaybookRun|AnalysisNodeRunSave|AnalysisResultPersistence|WorkingDocument|AnalysisOrchestrationService|DetachedPlaybookRunner|McpHostContract|
  McpCallerPath|PlaybookRunResults|WidgetResource|Api.Mcp|Playbook`: 2,068 passed, 0 failed, 4 skipped (the widgets' build-dependent twins).
- ArchTests, the whole project (which includes the AnalysisId guard, ADR010, RouteAuthorization and DetachedHttpContext): 2,886 passed, 0 failed.
  `check-changed-cs-format.sh origin/master`: clean.
- Widget jest: 119 passed (one is skipped until `dist/` is built). `npm run build`: OK. BFF build: 0 warnings.

### 7.7 Left open (not fixed here)

- `WorkingDocumentService.UpdateWorkingDocumentAsync` (unchanged, legacy streaming path) still logs the exception object, which may carry Dataverse message
  text (an ADR-015 question).
- `FinalizeAnalysisAsync` still swallows its own failures: a failed finalize after a saved write leaves the analysis at statuscode 1, which the reader shows
  as `available` because there is text.
- K-4's reporting impact on matter-regarding views is not assessed.
