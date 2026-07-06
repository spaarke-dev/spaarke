# Task 036 — Legacy Chat/Tools/* deletion (FR-P2-07) — Task Notes

> Date: 2026-07-06 · Wave W-P2-D · Executed under task-execute FULL rigor (TEST-MODIFYING override).
> Order honored: MIGRATE FIRST, THEN DELETE (NFR-08 hard-cutover doctrine — no transition period, no shims).

## Migration inventory (what was live vs dead at execution time)

Post-034 caller census over the 11 `Services/Ai/Chat/Tools/*.cs` classes + `PlaybookOutputHandler`:

| Class | Live caller at census | Disposition |
|---|---|---|
| `AnalysisExecutionTools` | **LIVE** — hardcoded group in `AgentToolCatalogProjector.ResolveToolsAsync` (gated by `reanalyze` capability; the LAST hardcoded tool group) | **MIGRATED** → new typed `AnalysisExecutionHandler` (2 catalog rows: `analysis.rerun` / `analysis.refine`), then deleted |
| `TextRefinementTools` | **LIVE** — `ChatEndpoints.RefineTextAsync` used `BuildRefineMessages` (streaming refine endpoint; NOT an LLM tool call). The 3 LLM methods were already migrated in R6 Wave 7 (`TextRefinementHandler`, TEXT-* rows) | **MIGRATED** — `BuildRefineMessages(text, instruction, surroundingContext?)` hoisted onto `TextRefinementHandler` as public static; `RefineTextAsync` re-pointed; class deleted |
| `WorkingDocumentTools` | dead (superseded R6 Wave 9 → `WorkingDocumentHandler`; 031's comment-only touch notwithstanding) | deleted (see CAUTION-1 below) |
| `DocumentSearchTools`, `KnowledgeRetrievalTools`, `WebSearchTools`, `CodeInterpreterTools`, `LegalResearchTools`, `VerifyCitationsTool` | dead (superseded R6 Waves 7c/8 by their typed handlers) | deleted |
| `DataverseQueryTools`, `CompareDocumentsTool` | dead (no constructor call anywhere in src/) | deleted (audit F-2b / O-1 legs closed) |
| `PlaybookOutputHandler` | callers were `ChatEndpoints.ExecutePlaybookAsync` + factory `CreatePlaybookOutputHandler` — BOTH deleted by parallel task 035 before this deletion landed (verified live in-worktree) | deleted, incl. its orphaned `ChatSseDialogOpenData`/`ChatSseNavigateData` SSE DTOs (sole emitter) |

## The migration (typed handlers, closed catalog)

- **`AnalysisExecutionHandler`** (`Services/Ai/Handlers/AnalysisExecutionHandler.cs`, chat-only, method discriminator per the TextRefinementHandler two-row precedent — two rows because the two methods carry DIFFERENT declared side-effect classes and the ONE gate fires on the row's declaration, never a name list):
  - `SYS-Analysis Rerun` — `sprk_toolid=analysis.rerun`, `sprk_namespace=analysis`, `side_effect_class=Write` (engine persists a new analysis output → gate suspends by declaration), `permission_scope=app-only-engine` (honest F-1 declaration), `budget_class=heavy`, `sprk_requiredcapability=reanalyze` (preserves the legacy task-079 gate).
  - `SYS-Analysis Refine` — `sprk_toolid=analysis.refine`, `side_effect_class=Read`, `budget_class=standard`, same `reanalyze` gate.
  - Rows created in **spaarkedev1** (`2b09dfb5-…`, `55521abc-…`) + seed JSONs `infra/dataverse/sprk_analysistool-analysis-{rerun,refine}-row.json` + registered in `scripts/Seed-TypedHandlers.ps1`.
- **Loop-contract plumbing** (ADR-033 side-channel precedent): `ChatInvocationContext` gains `PlaybookId`, `DocumentId` (set by the projector's context factory from session context) and `SseWriter` (forwarded by `ToolHandlerToAIFunctionAdapter` from its ctor sseWriter) so the rerun method emits per-stage `progress` + completion `document_replace` SSE DURING execution. Null writer degrades silently.
- Legacy `RefineAnalysisAsync` wiring bug NOT carried over: the projector passed `analysisId: null` to the legacy class (refine always failed with "no analysis output"); the handler reads `ChatInvocationContext.AnalysisId`, which the adapter actually populates.
- **Re-namespacing (FR-P0-03 contract / step 3)**: the three TEXT rows gained `sprk_toolid` (`text.refine` / `text.keypoints` / `text.summary`), `sprk_namespace=text`, `side_effect_class=Pure`, `permission_scope=none`, `budget_class=standard` — in spaarkedev1 AND the seed JSONs. Catalog now: 11 rows with unique namespaced toolids (6 dataverse.* + 3 text.* + 2 analysis.*); remaining legacy rows stay null-toolid (tolerated by the FR-P0-04 health check; they gain ids when their families are re-namespaced).
- **FR-P0-04 bijection**: `AnalysisExecutionHandler` ↔ 2 active rows (handler serving several rows is explicitly legal); no duplicate `sprk_toolid` (verified by catalog query, shown in transcript); handler auto-discovered (DI contract test `HandlerType_IsRegisteredInDi` green).
- `AgentToolCatalogProjector` now resolves tools EXCLUSIVELY from the FR-11 data-driven catalog block — the last hardcoded group is gone; per-group isolation counters + the "additive strategy" comments removed; unused `IChatClient` ctor param dropped (factory call site updated).

## CAUTION-1 / CAUTION-2 rulings (kept-with-reason)

- **`WorkingDocumentHandler` KEPT** — F-1/F-2d accept-until-cutover leg explicitly assigned to task 044 (POML 044 step 3 names `InvokePlaybookHandler`, `AnalysisQueryHandler`, `WorkingDocumentHandler(+Tools)`). The `WorkingDocumentTools` CLASS however is 036's (POML 036 step 5 lists it as a grep-zero target; it was already dead code) — deleted here; 044's grep-zero for the Tools leg is pre-satisfied.
- **`InvokePlaybookHandler` KEPT** — task 044 deletes it after task 040 re-homes the E-2 `EngineOutputLedgerAdapter` (CAUTION-2). Untouched by this task.
- **`AnalysisQueryHandler` KEPT** — same 044 assignment.
- New F-1-adjacent surface note: `AnalysisExecutionHandler.rerun` retains the pre-existing app-only engine leg (`IAnalysisOrchestrationService.ExecutePlaybookAsync`) under the G-P0 accept-until-cutover ruling — now EXPLICITLY declared in the catalog (`permission_scope=app-only-engine`, `side_effect_class=Write` → confirmation-gated). Flag for task 044's F-1 re-trace: add `AnalysisExecutionHandler` to the three legs it remediates (delete leg or OBO-migrate; its engine dependency dies with the FR-P3-05 engine-shell deletions).

## Grep-zero evidence (NFR-08) — SHOWN

```
$ for sym in AnalysisExecutionTools TextRefinementTools WorkingDocumentTools WebSearchTools \
    DataverseQueryTools DocumentSearchTools KnowledgeRetrievalTools CodeInterpreterTools \
    LegalResearchTools CompareDocumentsTool VerifyCitationsTool PlaybookOutputHandler; do
    git grep -c "$sym" -- src tests scripts ...
GREP-ZERO OK: AnalysisExecutionTools  -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: TextRefinementTools     -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: WorkingDocumentTools    -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: WebSearchTools          -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: DataverseQueryTools     -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: DocumentSearchTools     -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: KnowledgeRetrievalTools -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: CodeInterpreterTools    -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: LegalResearchTools      -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: CompareDocumentsTool    -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: VerifyCitationsTool     -> 0 tracked hits in src/ tests/ scripts/
GREP-ZERO OK: PlaybookOutputHandler   -> 0 tracked hits in src/ tests/ scripts/
```

Both `Services/Ai/Chat/Tools/` and `tests/.../Chat/Tools/` directories no longer exist. ~90 comment/doc
references across handlers, DI modules, module CLAUDE.md, HandlerRegistrationConventions.md, client
hooks/widgets, and the seed script were reworded (034 precedent) so grep-zero is literal, not comment-tolerant.

## Tests

- **New**: `AnalysisExecutionHandlerTests` — 4 handler-contract tests + chat-only contract + ValidateChat
  + rerun (session-id targeting, progress/document_replace SSE, null-writer degradation, missing
  playbook/document/HttpContext diagnostics, engine-error chunk) + refine (fetch+LLM, missing analysis/
  instruction) + ADR-015 telemetry (no instruction/content leakage). All green.
- **Reworked**: `ChatRefineEndpointTests` — prompt-builder tests re-pointed at static
  `TextRefinementHandler.BuildRefineMessages` (class `TextRefinementPromptBuilderTests`); legacy ctor-null
  test dropped (ADR-038 B4). `VerifyCitationsTests` — legacy explicit-invocation sections deleted
  (covered by `VerifyCitationsHandlerTests`); `CitationSafetyCheck` sections kept intact.
- **Deleted** (tests of deleted classes; none under deletion-protected KEEP paths):
  `Chat/Tools/{AnalysisExecutionTools,CompareDocumentsTool,DataverseQueryTools,DocumentSearchTools,WebSearchTools,WorkingDocumentTools}Tests.cs`,
  `PlaybookOutputHandler{SideEffect,WorkspaceCase,FormPrefill,BothCase}Tests.cs`,
  `StreamingWriteIntegrationTests.cs` (exercised only the legacy WorkingDocument chat-tools; typed-handler
  streaming covered by `WorkingDocumentHandlerTests`). `.reliability-registry.json` entry for the deleted
  CompareDocuments perf test removed.
- **Targeted run**: AnalysisExecutionHandler + TextRefinement + ChatRefine + VerifyCitations + adapter +
  factory + AgentTurnLoop = **246 passed / 1 pre-existing skip**.
- **Full unit suite**: **7698 total — 7591 passed, 101 skipped, 6 failed**. All 6 on the KNOWN pre-existing
  list (ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver,
  PlaybookTemplateContextBuilder TextOnly, SessionFilesCleanup — 5 reproduced in isolation — plus the
  AuditLogService flake, which PASSES in isolation). **Zero failures attributable to task 036.**
  (Total count dropped from 034's 8037 → 7698: the 035 + 036 test deletions in the shared worktree.)
- **Eval suite (NFR-02 merge gate)**: `--filter Category=GoldenUtteranceEval` = **12/12 green**. No eval
  case referenced the migrated capabilities by legacy name (verified by grep), so no eval-case rewrites
  were required; the suite runs the loop path end-to-end post-deletion.

## Publish size (ADR-029 / NFR-01)

`dotnet publish -c Release -o deploy/api-publish` → **46.83 MB compressed** (PowerShell `Compress-Archive
-CompressionLevel Optimal`, incl. 1.87 MB PDBs) / 141.53 MB uncompressed / 270 files.
Baseline (task 034, same compressor + lineage): 46.95 MB → **NET REDUCTION −0.12 MB**. Caveat: the shared
worktree means this figure includes parallel task 035's dispatcher-stack deletions landed before this
measurement; the combined W-P2-C/D deletion wave is a net reduction as the phase demands. **ZERO csproj/
NuGet changes** (`git diff HEAD -- *.csproj` empty) → no new CVE surface by construction. Far below the
60 MB ceiling.

## Escalations

- None blocking. FYI for gate 038 / task 044 operator review: (1) `analysis.rerun` is now confirmation-gated
  (declared `side_effect_class=write`) — a UX change vs the ungated legacy tool, but the honest ADR-039
  declaration (engine persistence IS a write); (2) 044's F-1 re-trace should include the new
  `AnalysisExecutionHandler` engine leg alongside `InvokePlaybookHandler`/`AnalysisQueryHandler`/
  `WorkingDocumentHandler` (see CAUTION section above).

## Step 9.5 quality gates (FULL rigor + TEST-MODIFYING override)

### ADR validation report (adr-check protocol)

Scope: task-036 changed files (new `AnalysisExecutionHandler` + tests; `ChatInvocationContext`/
adapter/projector/factory/ChatEndpoints/TextRefinementHandler edits; 23 deletions; catalog rows).

✅ Compliant: ADR-039 (closed catalog — handlers register via `sprk_analysistool` rows with declared
side_effect_class/permission_scope/budget_class; gate by declaration, zero tool-name lists; no second
intent mechanism; the projector is now catalog-ONLY) · ADR-040 (migrated invocations ride the task-030
loop/ledger path via the adapter — inherited, no bypass added) · ADR-010 (zero manual DI lines; handler
auto-discovered; `IHttpContextAccessor` already registered) · ADR-013 (handler AI-internal; no CRUD→AI
injection) · ADR-014 (TenantId validated on chat path) · ADR-015 (IDs/outcome/duration only — sentinel
test green) · ADR-016 (inner LLM via the loop's `IChatClient`) · ADR-028 (no auth surface changes) ·
ADR-029 (publish verified, net reduction, 0 NuGet) · ADR-032 (no conditional registrations added or
orphaned — the deleted output-router was factory-instantiated, never DI-registered; its Null-factory
override was removed by 035) · ADR-033 (SseWriter follows the documented side-channel principle) ·
NFR-08/NFR-09 satisfied. BFF hygiene §10 checklist: no new endpoints/DI/packages/background work;
placement = canonical `Services/Ai/Handlers/` slot; §11 three-question justification in these notes.
NetArchTest: 18 passed / 5 failed — the 5 are the KNOWN pre-existing failures (ADR-007 Graph isolation,
ADR-009 IDistributedCache, ADR-010 ×3); count unchanged → no new arch violations.

⚠️ Warnings (accepted, documented):
1. `HandlerType_IsRegisteredInDi` is a DI-registration-shaped test (ADR-038 B3) — RETAINED because
   `HandlerRegistrationConventions.md` (binding for every handler PR) mandates the 4 contract tests;
   all 30+ sibling handler test files carry it. Documented exception; candidate for the /test-diet
   reconciliation if the conventions doc is ever amended.
2. `analysis.rerun` keeps the app-only engine leg (F-1) — accept-until-cutover per the G-P0 ruling,
   now EXPLICITLY declared in the catalog and confirmation-gated; flagged to task 044's F-1 re-trace.

❌ Violations: none.

### Code review report (code-review protocol)

- Security: no secrets/config/auth changes; HttpContext→engine pass-through is the pre-existing OBO
  download pattern; LLM args schema-validated at the adapter boundary; untrusted-input posture unchanged
  (NFR-03 middleware wraps all loop output).
- Performance: no new allocations on hot paths; catalog projection unchanged (one query per session);
  rerun streams via existing IAsyncEnumerable.
- Style/maintainability: migration preserves legacy behavior verbatim where sane; fixed one latent
  legacy bug (refine's always-null analysisId); projector shed ~200 lines of dead comment/scaffolding;
  no compat shims, no dead params (unused `IChatClient` ctor param removed with call-site update).
- AI-code-smells: none introduced (no speculative abstraction; two rows instead of a config flag because
  the GATE contract demands per-row side-effect declaration).
- Lint/build: `dotnet build` clean for API + tests (only pre-existing NullConnectionMultiplexer
  nullability warning). Critical issues: 0. Result: ✅ PASS.

Gates: Code Review ✅ · ADR Check ✅ (0 violations, 2 documented warnings) · Lint ✅.
