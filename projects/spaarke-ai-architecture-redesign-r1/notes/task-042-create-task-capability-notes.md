# Task 042 — create-task capability + typed-handler confirm-RESUME (FR-P3-03) — Task Notes

> Date: 2026-07-06 · Wave W-P3-A · task-execute FULL rigor (+ TEST-MODIFYING override — tests/** touched).
> Boundaries honored: no commit/push; no TASK-INDEX/current-task edits; no `.claude/` writes; hands off task 040's
> surfaces (LinearConsumers/Workspace/Insights config, EngineOutputLedgerAdapter, InvokePlaybookHandler area).
> Shared files (golden-utterances.json, ConsumerTypes.cs, eval harnesses) edited append-style after fresh re-reads.

## What shipped

FR-P3-03 in two halves:

1. **The `create-task` capability as catalog data** — CREATE-TASK@v1 prompted Action + create-task Binding
   (spaarkedev1 rows below). The Action drafts a well-formed task proposal grounded in session file text
   (structured output: `title → description → priority_suggestion → cited_refs`, `cited_refs` minItems 1);
   its `sprk_inputschema` declares `due_date` + `assign_to` REQUIRED with maker elicitation prompts, so the
   walkthrough step-11 clarifying turn ("What's the due date… assign to you or someone else?") comes from
   FR-P2-03 loop-native elicitation for free (032 note 5 anticipated exactly this). The WRITE leg is the
   EXISTING `dataverse.create_record` typed handler (task 009) — **no new tool handler, no routing outside
   the Binding table** (ADR-039). The Binding's `sprk_tooldescription` instructs the model to carry the
   proposal + the user's elicited due date into a `sprk_event` create with `sprk_eventtype_ref = Task` and a
   provenance line in `sprk_description` naming the source document file AND the source analysis ledger key
   (`{bindingId}@t{n}`, copied verbatim from the F3 "## Session Outputs (stored ledger)" context block).

2. **Typed-handler confirm-RESUME execution** (the seam tasks 037/041 + G-P2 finding 6 deferred here) — new
   `Services/Ai/Chat/TypedHandlerResumeExecutor.cs`. On Confirm of a non-Binding (typed-handler) gate,
   `ChatEndpoints.ResolveGateAsync`:
   - **peek-resolves supportability FIRST** (`TryResolveAsync`: the suspended `PendingInvocation.ToolId` — the
     LLM function name the gate preserved — inverts to its `sprk_analysistool` row via the SAME
     `ToolHandlerToAIFunctionAdapter.SanitiseToolName` transform the projection used; handler resolves by the
     row's DECLARED `sprk_handlerclass`; chat-availability + `InvocationContextKind.Chat` required);
   - if unsupported (no row / no handler / compound-AI off) → the honest G-P2 interim path is KEPT unchanged
     (`confirmed-unexecutable` close + 422 `gate.no-binding-target`);
   - if supported → `ResumeInvocationAsync` (get-then-delete; double-confirm → 409 `gate.not-pending`;
     `confirmed` marker = the user's approval, same marker contract as the Binding leg) → execution through
     the typed-handler stack (`ValidateChat` → `ExecuteChatAsync` — the same handler contract the loop's
     adapter drives) **under the confirming user's OBO scope** (handler resolved from the gate-resolve HTTP
     request's DI scope; `IDataverseUserClient` reads that request's bearer token — a create the user lacks
     privileges for fails with the user's own `DATAVERSE_ACCESS_DENIED`);
   - **ADR-040 before rendering**: on success the seam writes an addressable `loop@t{n}` `SessionOutput`
     (BindingId `"loop"` — the reserved loop-native id; disposition `record`; payload = handler result data;
     SourceRefs = citation ids) + a `SessionToolChain` audit entry (NFR-07: args re-summarized through THE one
     summarizer `AgentTurnContract.SummarizeArguments` — identifiers verbatim, free text/content-bearing names
     redacted) BEFORE returning; the endpoint returns `GateResolveResult("confirmed", summary)`;
   - execution failure → 502 `gate.dispatch-failed` with the handler's error (mirrors the Binding leg).
   - **DI posture (ADR-010/032)**: NOT registered — `TryCreate(requestServices)` composes it per-request from
     services already behind the compound-AI gate; null ⇒ honest fallback. Zero new DI lines, no Null peer needed.

   This makes create-task, `dataverse.*` writes, AND task 041's `email.draft` confirm legs live end-to-end
   (041 integration note 1: "GU-051/052/057's confirm legs all activate together" — the handler side needed
   zero changes).

3. **Client leg** — `SprkChat.handleActionConfirm` success branch now renders the result + completion message
   as an assistant message IN THE TRANSCRIPT (`✅ {action} completed. {summary}`) instead of a transient toast
   (the G-P2 finding-6 lesson). The `gate.no-binding-target` honest branch is KEPT for genuinely unsupported
   kinds, reworded from "arrives in the next phase" to environment-honest copy. Error toast unchanged; reject
   unchanged (server-side `rejected` marker).

## Dataverse rows created (spaarkedev1, 2026-07-06, Dataverse MCP; post-create read_query round-trips shown in transcript)

| Row | GUID | Key values |
|---|---|---|
| `sprk_analysisaction` **CREATE-TASK@v1** | **`b66c8dda-8279-f111-ab0e-7ced8ddc4cc6`** | `sprk_actioncode=CREATE-TASK@v1`, `sprk_name=Create Task for Chat`, `sprk_kind=100000000 (Prompted)`, `sprk_modeltier=100000001 (Standard)`, `sprk_inputschema` = `{fileIds?, due_date! (elicitation_prompt "What's the due date for this task?"), assign_to! (elicitation_prompt "Should I assign it to you or someone else?")}` (standard JSON-schema `required` array + per-property `required:true` dual declaration), `sprk_systemprompt` = CREATE-TASK@v1 JPS (artifact: [`notes/jps/CREATE-TASK-v1.jps.json`](jps/CREATE-TASK-v1.jps.json); jps-action-create Step 4 checks passed), `sprk_outputschemajson` = strict 4-field schema (mirrored at `infra/dataverse/outputschemas/create-task-v1.schema.json`) |
| `sprk_playbookconsumer` **create-task** | **`3d9724e5-8279-f111-ab0e-7ced8ddc4cc6`** | `sprk_consumertype=create-task`, `sprk_consumercode=default`, `sprk_environment=*`, `sprk_priority=500`, `sprk_enabled=true`, `sprk_ucid=UC-H-1`, `sprk_disposition=100000000 (Informational)` (the proposal render; the record write is dataverse.create_record's), `sprk_risk=100000000 (None)` (the gate fires on the TOOL's declared Write class), `sprk_capturemode=100000000 (Loop Elicitation)` (conversational-confirm presentation), `sprk_surfaces=assistant`, `sprk_chiptransitions=[]`, `sprk_tooldescription` = maker intent surface + the write-composition instruction (sprk_event item shape, provenance-line contract, eventtype_ref Task lookup `124f5fc9-98ff-f011-8406-7c1e525abd8b`, gate notice), `sprk_action` → CREATE-TASK@v1 |

No new `sprk_analysistool` row — the write-shape IS task 009's `dataverse.create_record`
(`18b3531f-ba78-f111-ab0e-7ced8ddc4a05`, declared `sprk_sideeffectclass=Write`), reused per the POML/ADR-039.
`ConsumerTypes.CreateTask` constant added (+`All`) — FR-P0-04 constants↔rows parity holds (row live on spaarkedev1;
`RoutingConsumerTypeHealthCheck` suite green).

## POML-vs-data-model conflict (§6.5-style record — surfaced, resolved per POML)

The POML/spec prescribe **`sprk_event (type=task)`**; `docs/architecture/spaarke-todo-architecture.md` (R3,
2026-06-10) made **`sprk_todo`** the first-class To Do entity and the POML's own background sentence ("Spaarke
To Do surfaces then pick the record up with zero extra wiring") is only true of `sprk_todo`. **Live-environment
verification** (Dataverse MCP describe/read_query, shown in transcript): `sprk_event` is alive and self-describes
as THE tasks/action-items table ("When users ask about tasks, to-dos, deadlines… query this table"), carries
`sprk_eventtype_ref` with a live **Task** row (`124f5fc9-98ff-f011-8406-7c1e525abd8b`) and Task-type rows exist;
`sprk_todo` also exists (Kanban To Do). Verdict: the POML's prescription is NOT clearly stale — implemented **per
the POML** (`sprk_event` + eventtype_ref Task). The background-sentence claim is the inaccurate part, flagged for
the operator. **Re-pointing is pure catalog data**: switching the capability to `sprk_todo` later = editing the
Binding's `sprk_tooldescription` (+ JPS context sentence) — zero code changes, because the write goes through the
generic `dataverse.create_record` handler. Note `sprk_event` has NO document regarding lookup, so provenance rides
`sprk_description` (a text provenance line carrying both refs); `sprk_todo` has `sprk_regardingdocument` if the
operator re-points later.

## Confirm-resume end-to-end evidence

- **suspend → confirm → executed → ledger `confirmed` + result**: new live fact
  `CreateTask_ConfirmedWriteInvocation_ExecutesThroughTheTypedHandlerStack_LedgerConfirmed_RecordCarriesLedgerRefs`
  (P2LoopInjectionEvalSuiteTests — REAL `SideEffectGateAIFunction` over a REAL `ToolHandlerToAIFunctionAdapter`
  over the REAL `DataverseCreateRecordHandler`, Dataverse boundary mocked at `IDataverseUserClient` per its
  documented mock-boundary contract): write suspends (POST count 0) → `TryResolveAsync` inverts the sanitised
  name to the row+handler → `ResumeInvocationAsync` → executed EXACTLY once → **the posted record body carries
  the source-analysis ledger key (`{bindingId}@t{n}`) AND the source-document file name** (acceptance 3) → gates
  = pending + `confirmed` (append-only, same gate id) → SessionOutput `loop@t1` (disposition `record`, recordId
  payload) + ToolChain entry (ArgsSummary redaction-safe) written before return → double-confirm → null (409).
- **reject unchanged**: `Injection_SuspendedHostileWrite_RejectRemovesPayload_ResumeAfterRejectYieldsNothing`
  untouched, green.
- **injection suspension tests still green**: GU-051 (`Injection_WriteDeclaredToolInvocation_Suspends…`) and
  GU-052 (`Injection_EmbeddedApprovalTextAndArgs_DoNotBypassTheGate`) untouched, green — the gate still
  suspends; only the confirm leg went live.
- **honest fallback kept**: `CreateTask_UnsupportedSuspendedTool_ResolutionIsNull_HonestFallbackPathKept` (+ the
  endpoint branch reuses the F6-tested `confirmed-unexecutable` close; `ConfirmationGateUnificationTests` green).
- **Unit seam coverage**: new `TypedHandlerResumeExecutorTests` (9 green) — sanitised-name inversion; playbook-only
  and handler-missing rows unsupported; stored-args + oid + RequestedToolName flow into the handler context
  (user-OBO); success writes SessionOutput (`loop@t1`, `record`, SourceRefs=citation ids) + redaction-safe
  ToolChain; handler failure → error + no output + still audited; ValidateChat failure never executes;
  `SummarizeArgsJson` identifier/redaction matrix + malformed→null.
- One production `virtual` added: `AnalysisToolService.ListToolsAsync` (test-double subclass at the Dataverse
  module boundary — ChatSessionManager/PendingPlanManager virtual convention; ADR-038, no `Mock<HttpMessageHandler>`).

## Eval suite (NFR-02 / NFR-06)

- `golden-utterances.json`: **GU-059** added (create-task write leg + confirm-resume; clarify-shaped gate
  presentation, `catalogStatus=existing`); GU-027/028/029 + GU-044 flipped `planned → existing` (the guard's own
  prescription once `ConsumerTypes.CreateTask` landed); GU-057's stale "completes when the seam lands" note
  updated (seam landed here). Suite: **59 cases / 21 families** (README updated).
- New live surface fact `P3CreateTaskSurface_BindingResolvesAndProjectsThroughTheClosedCatalog_ElicitationDeclared`
  (GoldenUtteranceEvalSuiteTests, 041 pattern): real `ListTextProjectableBindingsAsync` over a stub shaped like
  the seeded rows (real GUIDs) → `capability_create-task` projection + catalog-authored description +
  **elicitation pin** (`BindingInputSchemaValidator.GetRequiredFields` derives exactly `due_date`+`assign_to`,
  each with a maker prompt) + CREATE-TASK@v1 output-schema pin (required fields, declaration order,
  `cited_refs.minItems=1`, NO due-date/assignee in the proposal).
- **Eval gate (`Category=GoldenUtteranceEval`): 35/35 green.**
- **Mid-wave collision fixed per the guard's prescription** (fixwave precedent): parallel task 040 added
  `InsightsAsk`/`InsightsSearch` constants while GU-021/022/023 still declared `catalogStatus=planned` —
  flipped to `existing` (3 one-line JSON edits; noted here for 040's awareness).

## Test results (2026-07-06, shared wave tree — task 040 edits in flight during runs)

- `TypedHandlerResumeExecutorTests`: **9/9 green**.
- Eval gate: **35/35 green**.
- Targeted adjacent (TypedHandlerResumeExecutor + ConfirmationGateUnification + LoopElicitation +
  AgentTurnLoopContract + RoutingConsumerTypeHealthCheck + DataverseCreateRecordHandler +
  SprkChatAgentFactoryTests): **118/118 green**.
- Client: shared-lib `tsc --noEmit` clean; SprkChat jest **364/364** (24 suites).

### Full-suite triage (2026-07-06, shared wave tree)

**7763 total — 7656 passed, 101 skipped, 6 failed.** The 6 = the 5 KNOWN pre-existing verbatim
(ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver,
PlaybookTemplateContextBuilder TextOnly, SessionFilesCleanup) + the documented AuditLogService flake
(the re-run listing showed only the 5 unique pre-existing names). **Zero failures attributable to task 042.**
Note: task 040's edits were in flight on the shared tree during the runs (two transient compile windows +
one file-lock contention window were waited out; final runs on a clean 0-error build).

## Publish size (ADR-029 / NFR-01)

`dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` → **141.61 MB uncompressed /
270 files**; **46.86 MB compressed** (PowerShell `Compress-Archive -CompressionLevel Optimal` — same compressor
lineage as tasks 032/036/037/041). Baseline (task 041 / G-P2 fixwave, same lineage): 46.85 MB →
**delta +0.01 MB** (shared wave tree — includes task 040's in-flight changes; task 042's own contribution is
one ~500-line source file + endpoint branch + one constant). `git diff HEAD -- *.csproj` **empty → 0 NuGet
changes → no new CVE surface by construction**. Ceiling 60 MB: far clear; ≪ the +2 MB justification line.
(Measured before two Step 9.5 code-only fixes — unused-param removal + catch broadening; size impact nil,
033/041 precedent.)

## Step 9.5 quality gates (2026-07-06 — FULL rigor + TEST-MODIFYING override)

- **code-review: PASS — 0 Critical.** Findings + dispositions:
  - **W1 (FIXED in-task)**: `AppendToolChainAsync` carried unused `context`/`outcome`/`resultCount` params with
    discard assignments — signature reduced to what the entry shape uses.
  - **W2 (FIXED in-task)**: `ExtractCitationIds` caught `JsonException` only; arbitrary handler metadata could
    throw other serialization exceptions AFTER a real side effect and fail the resume — broadened to a tolerant
    catch (best-effort audit enrichment; mirrors the adapter's metadata post-processing posture).
  - **W3 (accepted, documented)**: the `confirmed` marker is written by `ResumeInvocationAsync` BEFORE execution —
    a crash in the marker→execution window leaves an approval marker with no execution evidence. SAME window and
    contract as the Binding leg (032); `confirmed` records the user's approval; execution evidence is the
    SessionOutput/ToolChain pair.
  - **W4 (accepted, documented)**: the resume executes without a per-turn budget wrap — no turn is active; the
    SUSPENDING call already consumed its budget unit (032/037), and a resume is 1:1 with an explicit user click,
    not LLM-amplifiable (NFR-09 honored).
  - **W5 (accepted)**: one catalog list-read per confirm click (`ListToolsAsync`, page 200) — same cost class as
    the projection's per-session read; no cache added (§11 don't-over-engineer; projector precedent).
  - **W6 (flagged for wave PR)**: `golden-utterances.json` + eval harnesses + `ConsumerTypes.cs` are shared
    surfaces with task 040; all task-042 edits append-style after fresh re-reads; 3 insights-case flips made on
    040's behalf per the grounding guard's prescription (fixwave precedent).
  - AI-smell scan: 0 new interfaces, 0 DI registrations, no catch-log-rethrow (catches either rethrow
    cancellation, degrade documented, or terminate the operation), no code-restating comments; nested
    `ResumeResolution`/`ResumeOutcome` records are the seam's own contract types.
- **adr-check: PASS — 0 violations; no §6.5 path needed.**
  - **ADR-039**: capability = catalog rows only; write-shape REUSES `dataverse.create_record` (no new handler,
    no routing outside the Binding table); resume resolution keys exclusively on catalog declarations
    (`SanitiseToolName(sprk_name)` inversion + `sprk_handlerclass`) — never name lists; grounded outputs
    (`cited_refs` minItems 1; elicitation prompts from the declared schema only).
  - **ADR-040**: suspension marker-before-presentation unchanged (037, test-proven); resume writes append-only
    `confirmed` resolution correlated by gate id + SessionOutput `loop@t{n}` (the reserved loop-native binding id)
    + ToolChain BEFORE the result renders. The `record`-disposition entry is written directly (documented: the
    record ALREADY exists — this is ledger evidence of an executed write, not OutputRouter's not-yet-landed
    Binding-output→record rendering leg).
  - **ADR-010/032**: zero new DI lines/interfaces; `TryCreate` per-request composition; unavailable services ⇒
    honest fallback (no Null peer needed — nothing registered to gate). One `virtual`
    (`AnalysisToolService.ListToolsAsync`) per the existing test-double convention.
  - **ADR-013/014/015/016**: AI-internal placement (`Services/Ai/Chat/**`); tenant scoping unchanged; NFR-07
    identifiers/counts-only logs + THE one args summarizer for the ToolChain; ArgsJson Tier-3 only.
  - **ADR-019**: stable errorCodes preserved (`gate.not-pending` / `gate.no-binding-target` /
    `gate.dispatch-failed`); no new codes; 422 semantics narrowed (unsupported-only), never repurposed.
  - **ADR-028 / spec user-OBO MUST**: execution under the confirming request's scope; `IDataverseUserClient`
    fail-closed OBO; no app-only path reachable.
  - **ADR-029**: size verified above; **ADR-038**: eval additions at the KEEP path; executor unit tests at the
    sibling handler-suite path (030-W5/033-W2/041-W3 precedent — defend at /test-diet); no banned patterns
    (`IDataverseUserClient` is the documented mock boundary).
  - **CLAUDE.md §10/§11**: no new endpoints/DI/packages/background work; §11 three-question justification with
    grep evidence in the class doc-comment; **NFR-08**: the honest-unexecutable path is RETAINED by design as
    the fallback for genuinely unsupported kinds (POML scope statement) — nothing superseded left shimmed.
- Lint: `dotnet build` 0 errors (warnings pre-existing); `tsc --noEmit` clean.

## Gate-048 UAT additions (browser, spaarkedev1 — deploy this branch first; rows already live)

Walkthrough steps 10-14 (FR-P3-03 acceptance) in the Assistant:

1. Upload a document (NDA-style) → auto classify + summarize render.
2. Type **"create a follow-up task to review the indemnity findings"** (or click a task chip) → assistant runs
   `capability_create-task`; with no due date given, the clarifying turn asks for **due date + assignee** using
   the maker prompts (steps 10-11).
3. Reply **"7/9/2026 and yes me"** (GU-044's exact shape) → elicitation resolves; the task PROPOSAL renders
   (title/description/priority/citations) and the assistant moves to create the record (step 12).
4. The `dataverse.create_record` write SUSPENDS → **ActionConfirmationDialog** presents the proposed record
   (step 13's gate). **Reject** → cancels (ledger `rejected`), nothing created.
5. Re-drive and **Confirm** → the record is created UNDER YOUR USER (check: a user without create rights on
   `sprk_event` gets their own access error); the transcript shows the ✅ completion message with the created
   record id (step 14) — NOT the old "isn't enabled yet in this build" message.
6. Open the created `sprk_event`: `sprk_eventtype_ref = Task`, due date 2026-07-09, and `sprk_description`
   ends with the **Provenance:** line naming the uploaded file and a `{bindingId}@t{n}` ledger key.
7. **041's email.draft confirm leg** (now live on the same seam): "draft the client letter…" → review card →
   "save it as an email draft to …" → confirm → a Draft-status `sprk_communication` record exists (041 note 2's
   script step now fully executable).
8. Injection re-check (task-037 UAT item 3) unchanged: hostile document text still yields suspensions/refusals,
   never executed side effects — confirm-resume only ever fires from YOUR explicit click.

## Files created / modified (task 042 only)

**Created**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/TypedHandlerResumeExecutor.cs` ·
`infra/dataverse/outputschemas/create-task-v1.schema.json` ·
`projects/spaarke-ai-architecture-redesign-r1/notes/jps/CREATE-TASK-v1.jps.json` ·
`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/TypedHandlerResumeExecutorTests.cs` · this notes file.
**Modified (server)**: `Api/Ai/ChatEndpoints.cs` (`ResolveGateAsync` confirm leg — typed-handler resume; honest
fallback kept) · `Services/Ai/Chat/SideEffectGateAIFunction.cs` (doc: resume-surface paragraph) ·
`Services/Ai/Chat/PendingPlanManager.cs` (doc: `confirmed-unexecutable` narrowed to fallback) ·
`Services/Ai/AnalysisToolService.cs` (`ListToolsAsync` → `virtual`) ·
`Services/Ai/PublicContracts/ConsumerTypes.cs` (+`CreateTask`; append-style, fresh-read).
**Modified (client)**: `components/SprkChat/SprkChat.tsx` (confirm result → transcript message; honest-fallback copy).
**Modified (tests)**: `tests/integration/contract/Eval/golden-utterances.json` (GU-059; GU-027/028/029/044 flips;
GU-057 note; +3 insights flips for 040's mid-wave constants) · `Eval/GoldenUtteranceEvalSuiteTests.cs`
(P3 create-task surface fact) · `Eval/P2LoopInjectionEvalSuiteTests.cs` (confirm-resume + unsupported facts,
StubToolCatalog helper, +1 using) · `Eval/README.md` (59 cases; P3 row: create-task surface + resume seam LIVE).
**NOT modified** (boundaries): LinearConsumers/Workspace/Insights config surfaces, EngineOutputLedgerAdapter,
InvokePlaybookHandler, SessionDispatchOrchestrator, Narrators/briefing surfaces, DataverseCreateRecordHandler
(reused as-is), EmailDraftToolHandler (reused as-is).

## Seed row shapes (re-creation in another environment)

```json
// sprk_analysisaction — CREATE-TASK@v1 (systemprompt = notes/jps/CREATE-TASK-v1.jps.json minified;
// outputschemajson = infra/dataverse/outputschemas/create-task-v1.schema.json)
{
  "sprk_name": "Create Task for Chat",
  "sprk_actioncode": "CREATE-TASK@v1",
  "sprk_kind": 100000000,
  "sprk_modeltier": 100000001,
  "sprk_inputschema": "{\"type\":\"object\",\"properties\":{\"fileIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Optional subset of session file ids the task should be grounded in. Omit to use all session files.\"},\"due_date\":{\"type\":\"string\",\"required\":true,\"elicitation_prompt\":\"What's the due date for this task?\",\"description\":\"The task's due date as the user stated it (e.g. 7/9/2026).\"},\"assign_to\":{\"type\":\"string\",\"required\":true,\"elicitation_prompt\":\"Should I assign it to you or someone else?\",\"description\":\"Who the task is assigned to — 'me' or a person's name.\"}},\"required\":[\"due_date\",\"assign_to\"]}"
}
// sprk_playbookconsumer — create-task default Binding (sprk_action → CREATE-TASK@v1; tooldescription
// carries the write-composition instruction incl. the environment's sprk_eventtype_ref Task GUID —
// per-environment value; on spaarkedev1: 124f5fc9-98ff-f011-8406-7c1e525abd8b)
{
  "sprk_name": "create-task (default)", "sprk_consumertype": "create-task", "sprk_consumercode": "default",
  "sprk_environment": "*", "sprk_priority": 500, "sprk_enabled": true, "sprk_ucid": "UC-H-1",
  "sprk_disposition": 100000000, "sprk_risk": 100000000, "sprk_capturemode": 100000000,
  "sprk_surfaces": "assistant", "sprk_chiptransitions": "[]"
}
```

## Deferred / operator notes

- **Scope-index refresh** (`scripts/Refresh-ScopeModelIndex.ps1`, jps-action-create Step 7) not run — it writes
  `.claude/catalogs/` (sub-agent write boundary); main session may run it at the next catalog refresh (041 precedent).
- **JPS example mirror** into `.claude/skills/jps-action-create/examples/` — same boundary; artifact lives at
  `notes/jps/CREATE-TASK-v1.jps.json`.
- **sprk_event vs sprk_todo** — operator decision recorded above; re-pointing is catalog-data-only if desired.
- **Streamed resume presentation** — the gate-resolve response remains JSON (client renders the transcript
  message from it); an SSE-streamed resume render stays the known deferred upgrade (032 suggestion).
