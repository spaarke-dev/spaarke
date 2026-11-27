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
