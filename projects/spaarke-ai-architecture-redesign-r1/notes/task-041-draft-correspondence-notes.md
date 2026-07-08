# Task 041 — draft-correspondence capability + email.draft (FR-P3-02) — Task Notes

> Date: 2026-07-06 · Wave W-P3-A · task-execute FULL rigor (+ TEST-MODIFYING override — tests/** touched).
> Boundaries honored: no commit/push; no TASK-INDEX/current-task edits; no `.claude/` writes; hands off the
> G-P2 fix agent's files (SprkChat/ConversationPane/ChatEndpoints/SessionDispatchOrchestrator/SessionFileTextSource)
> and task 043's Narrators/briefing surfaces.

## What shipped

The `draft-correspondence` proving capability end-to-end as **catalog data + one typed executor**, per the
ratified owner decision (project CLAUDE.md 2026-07-05): delegation to **Spaarke's own Communication (Email)
service — NOT Outlook drafts; DRAFT-ONLY** (assistant-initiated send is the named FR-P4-07 deferral).

### The two-tool composition (loop-as-composer, per the landed P2 architecture)

1. **Drafting leg (prompted, read-shaped)** — `draft-correspondence` Binding → **DRAFT-CORR@v1** prompted
   Action. Projects into the agent loop as `capability_draft-correspondence` via the Binding's
   `sprk_tooldescription` opt-in (FR-P2-01 projection); executes by Binding id through
   `SessionDispatchOrchestrator` → ActionRunner + PromptSchemaRenderer, with the SessionOutput ledger write
   BEFORE the review render (ADR-040 — pre-existing dispatch stack, zero routing code added). Output schema
   (declaration order = streaming order): `subject → body → recipients_suggestion → cited_refs`
   (`cited_refs` minItems 1 — an uncited draft is invalid, ADR-039 grounded outputs).
2. **Write leg (communicate, gated)** — `email.draft` typed handler
   (`Services/Ai/Handlers/EmailDraftToolHandler.cs`). The model carries the reviewed draft text + the ledger
   refs (`{bindingId}@t{n}` keys from the session-context block / prior tool results) into `email.draft`
   args; the tool row DECLARES `sprk_sideeffectclass = Communicate (100000002)`, so
   `SideEffectGateAIFunction` (task 037) suspends the invocation into THE unified pending store
   (`PendingPlanManager`) — pending `SessionGate` marker BEFORE the `action_confirmation` presentation
   (ADR-040). **No gating logic lives in the handler** (ADR-039: by declaration, never tool-name lists).
   On execution the handler creates a `sprk_communication` record via `IDataverseUserClient` (user-OBO,
   no app-only path) using the Communication service's exact column contract.

### DRAFT-ONLY enforcement (the invariant, server-pinned)

- `statuscode = 1 (Draft)` + `statecode = 0` are **constants inside the handler**; the closed argument
  parser carries no status/direction/type vocabulary and ignores unknown members — no prompt content can
  produce a sent/queued communication (test-proven, incl. a hostile status-override attempt).
- The handler performs **zero Graph calls**; its complete Dataverse surface for a valid draft is ONE
  metadata GET + ONE record POST (test-proven via `VerifyNoOtherCalls`). Sending remains user-initiated in
  the Communication service (`CommunicationService.SendAsync` — untouched; spec §External Dependencies:
  consumed, not structurally modified).
- Regarding association reuses the write-mapper (`DataverseWriteItemMapper`) for metadata-resolved
  `@odata.bind` and mirrors `CommunicationService.RegardingLookupMap` (matter/org/contact/project/analysis/
  budget/invoice/workassignment) + denormalized `sprk_regardingrecordid`/`name`/`sprk_associationcount`.

## Dataverse rows created (spaarkedev1, 2026-07-06, via Dataverse MCP `create_record`; post-create `read_query` round-trips shown in transcript)

| Row | GUID | Key values |
|---|---|---|
| `sprk_analysisaction` **DRAFT-CORR@v1** | **`4b8b50f4-6a79-f111-ab0e-7ced8ddc4cc6`** | `sprk_actioncode=DRAFT-CORR@v1`, `sprk_name=Draft Correspondence for Chat`, `sprk_kind=100000000 (Prompted)`, `sprk_modeltier=100000001 (Standard)`, `sprk_inputschema` = JSON-schema `{fileIds?: string[]}` (no required fields — no elicitation trigger), `sprk_systemprompt` = DRAFT-CORR@v1 JPS (artifact: [`notes/jps/DRAFT-CORR-v1.jps.json`](jps/DRAFT-CORR-v1.jps.json)), `sprk_outputschemajson` = strict 4-field schema (mirrored at `infra/dataverse/outputschemas/draft-corr-v1.schema.json`) |
| `sprk_playbookconsumer` **draft-correspondence** | **`f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6`** | `sprk_consumertype=draft-correspondence`, `sprk_consumercode=default`, `sprk_environment=*`, `sprk_priority=500`, `sprk_enabled=true`, `sprk_ucid=UC-G-2`, `sprk_disposition=100000000 (Informational)` (the review card; the record write is email.draft's), `sprk_risk=100000000 (None)`, `sprk_capturemode=100000000 (Loop Elicitation)`, `sprk_surfaces=assistant`, `sprk_chiptransitions=[]`, `sprk_tooldescription` = maker intent surface (drafts are REVIEWABLE, never sent; instructs the email.draft follow-up with source_refs), `sprk_action` → DRAFT-CORR@v1 |
| `sprk_analysistool` **email.draft** | **`bc11e90d-6b79-f111-ab0e-7ced8ddc4cc6`** | `sprk_name=SYS-Email Draft`, `sprk_toolcode=EMAIL-DRAFT`, `sprk_toolid=email.draft`, `sprk_namespace=email`, **`sprk_sideeffectclass=100000002 (Communicate)`**, `sprk_permissionscope=dataverse-user-context`, `sprk_budgetclass=light`, `sprk_handlerclass=EmailDraftToolHandler`, `sprk_availableincontexts=100000001 (Chat)`, `sprk_jsonschema` = closed args schema (subject/body/to/cc/body_format/regarding/source_refs; additionalProperties false) |

Seed mirror: `infra/dataverse/sprk_analysistool-email-draft-row.json` (Seed-TypedHandlers.ps1 upsert contract).
`ConsumerTypes.DraftCorrespondence` constant added (+`All`) — FR-P0-04 constants↔rows parity holds (row exists;
`RoutingConsumerTypeHealthCheckTests` generate from `ConsumerTypes.All`, green). The registered-handler↔tool-row
bijection health check stays Healthy at next deploy: handler class + row both land together.

## Gate behavior evidence

- **Gate fires on declared communicate** (never tool-name): new live fact
  `DraftCorrespondence_CommunicateDeclaredEmailDraftInvocation_SuspendsIntoTheOneGate_InnerNeverExecutes`
  (P2LoopInjectionEvalSuiteTests harness — REAL `PendingPlanManager` + `ChatSessionManager`): inner
  executions == 0; pending marker `Kind=confirmation`, `Status=pending`, `SideEffectClass="communicate"`;
  resumable payload retrievable with the ledger `source_refs` intact.
- **Seed-row gate contract**: `FullCatalog_NamespacedToolRowSeeds_DeclareTheGateContract` extended with a
  `declaredCommunicates = ["email.draft"]` group — asserts the mirror declares `Communicate` AND
  `RequiresConfirmation(Communicate) == true`; read/pure half still asserts non-gating (policy matches
  declarations, not names).
- **DRAFT-only proven by test**: `EmailDraftToolHandlerTests` (20 tests) — statuscode pinned to Draft(1)
  even under hostile status-override args; exactly one metadata GET + one POST (`VerifyNoOtherCalls`);
  no Graph dependency exists in the class (ctor deps: `IDataverseUserClient` + logger only); user-OBO
  error surfacing (403 → user's own `DATAVERSE_ACCESS_DENIED`; invisible table 404s BEFORE any write);
  ADR-015/NFR-07 telemetry (counts/ids only — subject/body/addresses asserted absent from logs).
- **Confirm-resume seam (documented, NOT landed here)**: typed-handler confirm still closes
  `confirmed-unexecutable` + 422 `gate.no-binding-target` (ChatEndpoints `ResolveGateAsync` — owned by the
  parallel G-P2 fix agent; task-037 notes name FR-P3-03 as the first legitimate consumer to land the
  typed-handler resume execution). Suspend-only remains the safe posture; reject works end-to-end. The
  G-P3 "draft the client letter" browser step needs that seam live by gate task 048 — flagged under
  Integration notes below.

## Eval suite (NFR-02 / NFR-06)

- `golden-utterances.json`: **GU-056** (dispatch, `draft-correspondence`, `schemaConformance=DRAFT-CORR@v1`,
  `citationIntegrity=true`) + **GU-057** (clarify — the Communicate gate presentation; consumerType named
  per the task-033 clarify-grounding evolution). Family `draft-correspondence` satisfies the
  `FullCatalog_EveryClosedCatalogConsumerType_HasAnEvalFamily` generator for the new constant. Both P3
  (pending NL-loop assertion; live surface assertions landed now). Suite: 57 cases / 21 families
  (README updated).
- New live fact `P3DraftCorrespondenceSurface_BindingResolvesAndProjectsThroughTheClosedCatalog`
  (GoldenUtteranceEvalSuiteTests, task-033 refusal-surface pattern): real
  `ListTextProjectableBindingsAsync` over a stub shaped like the seeded row (real GUIDs) →
  `capability_draft-correspondence` projection + catalog-authored description + DRAFT-CORR@v1 schema pin
  (required fields, declaration order, `cited_refs.minItems=1`).
- **Eval gate (`Category=GoldenUtteranceEval`): 31/31 green** (29 before task; +2 live facts).

## Test results (2026-07-06)

- `EmailDraftToolHandlerTests`: **20/20 green**.
- Adjacent targeted (ConfirmationGateUnification + AgentTurnLoopContract + RoutingConsumerTypeHealthCheck +
  DataverseCreateRecordHandler + RefusalCapabilityTool): **85/85 green**.
- Eval gate: **31/31 green**.
- Full unit suite: see Full-suite triage below (run on the SHARED wave tree — G-P2 fix agent's in-flight
  edits present in ChatEndpoints/SessionDispatchOrchestrator/PendingPlanManager/SideEffectGateAIFunction/client files).

### Full-suite triage (2026-07-06, shared wave tree)

Final run (after Step 9.5 fixes): **7750 total — 7644 passed, 101 skipped, 5 failed**. An earlier run
additionally had the `AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` flake, which passed
on re-run — its documented behavior. All 5 are the KNOWN pre-existing list verbatim: ExecutorConfigSchemas,
KnowledgeDeploymentConfig, DailyBriefingCollector resolver, PlaybookTemplateContextBuilder TextOnly,
SessionFilesCleanup. **Zero failures attributable to task 041.** (Publish size was measured before the two
post-review code edits — enum-cast constants + one hashset entry — compile-time-equivalent; size impact nil,
same precedent as task 033.)

## Publish size (ADR-029 / NFR-01)

- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` → **141.56 MB
  uncompressed / 270 files**; **46.85 MB compressed** (Compress-Archive Optimal — same compressor lineage
  as tasks 032/036/037).
- Baseline (task 037, same compressor): 46.83 MB → **delta +0.02 MB**. NOTE: the working tree is shared
  with the G-P2 fix agent's in-flight changes, so this is a wave-tree measurement; task 041's own
  contribution is one ~600-line source file + one constant (tens of KB of IL). `git diff HEAD -- *.csproj`
  empty → **0 NuGet changes** → no new CVE surface by construction. Ceiling 60 MB: far clear; no
  escalation threshold approached (≪ +2 MB justification line).

## Integration notes

1. **For task 042 / FR-P3-03 (create-task) — the typed-handler confirm-resume seam**: `email.draft` is now
   the SECOND suspended-typed-handler consumer waiting on the resume execution leg (422
   `gate.no-binding-target` today). When the seam lands (route the resumed `PendingInvocation` with a
   ToolId + no BindingId through the tool execution path), email.draft's confirm leg becomes end-to-end
   with no further changes here — the handler is stateless and executes on invocation. GU-051/052/057's
   confirm legs all activate together. ChatEndpoints is the touchpoint (G-P2 fix agent / 042 owns it).
2. **For gate task 048 (G-P3 browser script)**: deploy this branch first; seed rows already live on
   spaarkedev1. Script step: upload doc → auto-summary → "draft the client letter about these findings,
   citing the summary and the matter we created earlier" → the draft renders as a review card (SessionOutput
   `f7dc4a00-6b79-f111-ab0e-7ced8ddc4cc6@t{n}`) → "save it as an email draft to …" → ActionConfirmationDialog
   (communicate class) → confirm → verify the Draft-status `sprk_communication` record opens in
   Communications with regarding = the created matter, and that NO email was sent. Requires note 1's seam.
3. **Maker note**: the drafting Action declares NO required args — elicitation never fires for it; the
   model supplies `fileIds` optionally. Recipient capture happens at the email.draft step (args), not via
   elicitation — deliberate, because the dispatch seam forwards only `fileIds` to the prompted executor at
   this phase (P1 args contract in `SessionDispatchOrchestrator`, hands-off file for this task).
4. **JPS artifact placement**: skill convention saves JPS examples under
   `.claude/skills/jps-action-create/examples/` — sub-agents cannot write `.claude/` (write boundary), so
   the artifact lives at `notes/jps/DRAFT-CORR-v1.jps.json` (task-033 REF-CHAT precedent). Main session MAY
   mirror it into the skill examples dir if desired. Same applies to the scope-index refresh
   (`scripts/Refresh-ScopeModelIndex.ps1` — jps-action-create Step 7): not run here (it commits to
   `.claude/catalogs/`); flag for the main session / next catalog refresh.

## Files created / modified (task 041 only)

**Created**: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EmailDraftToolHandler.cs` ·
`infra/dataverse/sprk_analysistool-email-draft-row.json` ·
`infra/dataverse/outputschemas/draft-corr-v1.schema.json` ·
`projects/spaarke-ai-architecture-redesign-r1/notes/jps/DRAFT-CORR-v1.jps.json` ·
`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EmailDraftToolHandlerTests.cs` · this notes file.
**Modified**: `Services/Ai/PublicContracts/ConsumerTypes.cs` (+DraftCorrespondence) ·
`tests/integration/contract/Eval/golden-utterances.json` (GU-056/057) ·
`tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs` (P3 surface fact) ·
`tests/integration/contract/Eval/P2LoopInjectionEvalSuiteTests.cs` (communicate seed-row group + live suspension fact) ·
`tests/integration/contract/Eval/README.md` (counts + P3 row).
**NOT modified** (boundaries): ChatEndpoints.cs, SessionDispatchOrchestrator.cs, SessionFileTextSource.cs,
SprkChat/ConversationPane client files, Services/Ai/Narrators/**, CommunicationService (consumed as-is),
AnalysisServicesModule (handler auto-discovered by the assembly scan — zero DI edits needed).

## Step 9.5 quality gates (2026-07-06)

- **code-review: PASS — 0 Critical.** Findings + dispositions:
  - **W1 (accepted)**: handler file ~575 lines (>500 threshold) — ~40% is load-bearing XML contract
    documentation (DRAFT-ONLY invariant, delegation contract, §11 justification) + the closed-args
    parser; single cohesive responsibility; largest method ~15 branches.
  - **W2 (FIXED in-task)**: mirrored int constants for the communication option sets replaced with
    direct enum casts from `Services/Communication/Models` (same assembly — drift-proof pins). The
    `RegardingLookupColumns` mirror remains (its source map is `private` in `CommunicationService`)
    and is documented as mirroring that contract.
  - **W3 (accepted, precedent 030-W5/033-W2)**: handler unit-test placement outside the 6 KEEP paths —
    consistent with the entire sibling handler suite; eval additions ARE at the KEEP path. Defend at
    /test-diet. `HandlerType_IsRegisteredInDi` mirrors the sibling 4-point contract template (asserts
    assembly-scan discovery anchoring the FR-P0-04 bijection health check — not a B3 DI-resolution test).
  - **W4 (flagged for wave PR)**: `golden-utterances.json` + eval harness files are shared surfaces with
    parallel agents; task-041 additions are append-style.
  - Suggestions: merge duplicate `ReadString`/`GetString` helpers (cosmetic); consider asserting the
    output schema's `status: const "draft"` in the seed-mirror test at P4.
  - AI-smell scan: 0 new interfaces, 0 DI registrations, no catch-log-rethrow, no code-restating
    comments; `ExecuteChatAsync` multi-concern shape accepted per the 030-W1/033-W1 dispatch-pipeline
    precedent.
- **adr-check: PASS — 0 violations** across ADR-039 (catalog-only capability; no routing config outside
  the Binding table; gate by declaration, never names; grounded outputs incl. `cited_refs` minItems 1),
  ADR-040 (marker/output before render — live-test-proven), ADR-013-amended (all code in
  `Services/Ai/**`; enum-only AI→Communication-models reference; Communication service consumed, not
  modified), ADR-010/032 (0 interfaces/DI; no gated registration), ADR-014/015/016 (tenant scoping
  unchanged; NFR-07 counts/ids-only telemetry; budget-wrapped via projection), ADR-029 (size verified),
  ADR-038 (KEEP-path eval additions; no banned patterns). No §6.5 resolution path needed.
  - **NFR-07 refinement applied during the check**: `"subject"` added to
    `AgentTurnContract.AlwaysRedactedArgNames` (email.draft introduces a content-bearing arg name the
    030-W2 list didn't cover; the drafted subject derives from document text) + regression fact
    `SummarizeArguments_EmailDraftSubject_RedactsByName` in `AgentTurnLoopContractTests`. Post-fix
    verification: loop-contract + handler + refusal + eval gate = 93/93 green.
- Lint: `dotnet build` clean (0 errors; warnings pre-existing).

**Additional files touched by Step 9.5 fixes**: `Services/Ai/Chat/AgentTurnContract.cs` (+1 redaction
entry — additive; not on any parallel agent's hands-off list) ·
`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/AgentTurnLoopContractTests.cs` (+1 regression fact) ·
`EmailDraftToolHandler.cs` (enum-cast constants).
