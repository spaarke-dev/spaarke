# Task 037 — playbook run results widget: decisions

Status: **STOPPED at step 1 (escalation trigger 1 and 2 fired).** No code written. The owner picks an option in §2.

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

## 3. Not done

Steps 2-6 (results read, app-only tool, widget, `_meta.ui`, CI wiring) wait for the owner's choice. No `.claude/` change and no
CAPABILITY-INDEX row are proposed yet (nothing new was added).
