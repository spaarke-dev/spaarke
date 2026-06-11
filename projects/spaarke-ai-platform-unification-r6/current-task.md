# Current Task State — spaarke-ai-platform-unification-r6

> **Last Updated**: 2026-06-09 (Wave B-G1 ✅ committed `f8ee93bf`; Wave B-G2 ✅ all 4 complete, uncommitted; preparing aggregate commit + Wave B-G3 dispatch)
> **Recovery**: Read "Quick Recovery" section first
> **Last Commit**: `f8ee93bf` (Wave B-G1: 030 + 031)
> **Branch**: `work/spaarke-ai-platform-unification-r6` (Wave B-G2 changes uncommitted; aggregate commit pending)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | **C (Pillar 6 — tri-directional workspace)** sub-phase 6a in progress, parallel with Phase B UI walkthrough |
| **Mode** | **Autonomous execution** per user pre-stated preference (`feedback_pipeline-execution-style.md`); user UI walkthrough in parallel |
| **Wave B-G1** | ✅ COMMITTED `f8ee93bf`. Tasks 030 + 031. |
| **Wave B-G2** | ✅ COMMITTED `4f585448`. 032 SUM-CHAT@v1 (existing schema verified + node destination=chat); 033 Option A — new workspace playbook referencing shared SUM-CHAT@v1; 034 matter-prefill (data work via timed-out sub-agent; main-session closeout); 035 project-prefill (~12 min runtime; NFR-07 inspection-based). |
| **Wave B-G3** | ✅ COMMITTED `4de9b67d`. 040 array dispatch + 041 object dispatch. Widget 1117 → 1775 LOC; 23 tests; both R5 SC-18 Gap C bugs structurally fixed. |
| **Wave B-G4** | ✅ COMMITTED `200f8becb`. Task 042 CapabilityRouter dedup via SelectedPlaybookId + system-prompt directive (NFR-01-preserving). |
| **Wave B-G5** | ✅ COMMITTED `2934c6bcf`. Task 048 integration test — 6 scenarios programmatically PASS; 8 live-UI items deferred to exit-gate manual walkthrough; 0 regressions vs Wave B-G4 baseline. |
| **Wave B-G6** | ✅ COMMITTED `dbbeffe56`. Task 049 exit-gate doc. |
| **Wave B-G7** | ✅ COMMITTED `949ee71ec`. Flag #3 stale GUID + flag #2 ProjectPreFill tests. |
| **Wave B-G8** | ✅ COMMITTED `a62cccb48`. BingGrounding startup hardening + Spaarke Dev config drift unblock. BFF + SpaarkeAi + LegalWorkspace deployed to Spaarke Dev. |
| **Wave C-G1 partial** | ✅ COMMITTED `7fed830c8`. Tasks 050 + 051 + 052. |
| **Wave C-G2** | ✅ COMMITTED `4d272fb88`. Tasks 071 (Pillar 9 visibility contract) + 059 (context.* events) + 060 (workspace.* events). |
| **🚨 Phase B walkthrough surfaced CRITICAL bugs (2026-06-10)** | **Phase B exit-gate sign-off HELD pending hotfix Wave B-G9**. See "Walkthrough Findings + Hotfix B-G9 Plan" section below. |
| **Task 053 (HELD)** | Awaiting B-G9 hotfix completion + walkthrough sign-off. |
| **Next** | **POST-COMPACT: dispatch hotfix B-G9**. See plan below. |
| **Phase A exit-gate doc** | `projects/spaarke-ai-platform-unification-r6/notes/phase-a-exit-gate.md` |
| **Wave B-G2 evidence** | `notes/task-032-migration-evidence.md`, `notes/task-033-migration-evidence.md`, `notes/task-034-migration-evidence.md`, `notes/task-035-migration-evidence.md` |
| **R7 follow-up candidate** | Matter-prefill technical-debt sweep + R5-era comment cleanup on SUM-CHAT@v1 outputSchema + investigate Output Format field. |

---

## 🚨 Walkthrough Findings + Hotfix Wave B-G9 Plan (2026-06-10)

User performed UI walkthrough on Spaarke Dev. **Phase B exit-gate sign-off HELD** pending these fixes.

### Bug catalog (severity-ordered)

#### CRITICAL (blocks Phase B exit) — Widget schema-aware dispatch broken in production

**Symptoms (Workspace Summary tab)**:
- `tldr: array of string` renders as BOLD paragraph containing `"A method...","The system uses...","Private transactions..."` (literal array as comma-quoted text, NOT bullets per task 040)
- `entities: object { organizations[], persons[] }` renders as bullets containing `organizations":[]` / `"persons":[]` (raw JSON syntax fragments, NOT labeled blocks per task 041)
- Section HEADERS (TL;DR / SUMMARY / KEYWORDS / ENTITIES) ARE present — so widget IS aware of schema
- Keywords (string per schema) renders as colored badges (custom per-field rendering)
- Summary (string) renders as paragraph — correct

**Hypothesis**: tasks 040+041 added `classifySchemaField()` + `SchemaAwareArrayRenderer` + `SchemaAwareObjectRenderer`, but the EXISTING hardcoded `tldr`/`entities` path tracking in the widget (lines around 267, 285 of pre-task-040 widget) is taking precedence over the new dispatch. Unit tests passed because they tested the new path directly, but production code flows through legacy path.

**File to investigate**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` (1775 LOC post-041)

**Fix approach**:
1. Read the widget IN FULL — understand the actual render path under live invocation
2. Identify why `classifySchemaField()` isn't winning over the hardcoded TL;DR / Entities sections
3. Verify the `schemaAwareReady` gate fires correctly
4. Verify `outputSchema` prop is reaching the widget (might be missing from the streaming envelope?)
5. Check whether tasks 040+041's new components are even reachable from the production render path

#### HIGH — PDF "can't extract" hallucination + duplicate response

**Symptoms**:
- Upload PDF → chat shows "It appears the attached document does not contain extractable text content for me to summarize directly..." message
- A few seconds later: structured summary appears
- `.doc` and `.txt` files DO NOT trigger this — only PDF
- The summary streams to BOTH Assistant chat AND Workspace Summary tab (duplicate fire)

**Root cause**: Task 042 dedup directive only applies for non-chat destinations. For chat destination, LLM is free to respond conversationally — but it has NO synchronous access to extracted PDF text (extraction is async via Document Intelligence), so it hallucinates "no text" message. For .doc/.txt the text is synchronous so no hallucination.

**Fix**: extend task 042's directive logic in `SprkChatAgentFactory.cs` to ALSO apply single-sentence ack directive to **chat destination** when a playbook is going to render the primary result. NFR-01 preserved (single-sentence ack is still conversational, just terser). Same file task 053 will eventually wire — convenient co-location.

#### HIGH — Browser cache / session corruption (regular browser fails; InPrivate works)

**Symptoms**:
- Regular Chrome: no Summary tab appears OR appears empty/Waiting; "can't extract" message + delayed summary; closing+reopening tab → hangs at "Waiting"
- Console error: `"A listener indicated an asynchronous response by returning true, but the message channel closed before a response was received"` (Chrome extension or stale message-channel pattern)
- InPrivate Chrome: clean behavior; Summary tab appears and renders (with the CRITICAL bug above on TL;DR/Entities content)

**User actions taken**: disabled all extensions, hard cache clear. Still failing in regular browser.

**Likely root cause**: Power Apps client-side web-resource cache serving stale SpaarkeAi version. InPrivate forces fresh resolution.

**User remediation steps to try (in priority order)**:
1. **DevTools → Application → Storage → "Clear site data"** (clears IndexedDB + SW + localStorage + sessionStorage; more thorough than Ctrl+Shift+Delete)
2. **Power Apps Maker → Solutions → ⋯ → Publish All Customizations** (invalidates client-side resource cache server-side)
3. **DevTools → Application → Service Workers → "Unregister"** (if any SW registered)
4. **Different Chrome profile** (eliminates all profile state)
5. **Edge or Firefox** (eliminates Chrome-specific issues)

**Code-level fix candidates** (if user remediation insufficient):
- Add cache-bust query string to SpaarkeAi web resource URL on each deploy
- Bump web resource version explicitly in registration
- Investigate sessionStorage cleanup in tab close handler

#### MEDIUM bugs

| # | Finding | Fix approach |
|---|---|---|
| B6 | Different summaries for SAME file across runs | Check if SUM-CHAT@v1 action's LLM call sets `temperature: 0`. Investigate `PlaybookExecutionEngine` to find the temperature setting. |
| B7 | Summary tab appears as DEFAULT tab on workspace (should only appear when summary executes) | Investigate workspace tab init / hardcoded default-tabs config; remove Summary from defaults |
| B8 | Summary tab REPLACED each run (vs new tab per run) | UX decision — confirm with user. If new-tab-per-run desired, investigate WorkspaceTabManager / executeSummarizeIntent.ts |
| B9 | `/summarize` slash vs natural-language give different output detail | Investigate CapabilityRouter slash routing vs natural-language routing — same playbook should give same output. Check if context-injection differs between paths. |

#### R7 candidates (deferred)

- **B10a**: Action's outputSchema has R5-era `$comment-property-order` references — load-bearing for streaming order, but R5 task 006 phrasing is confusing now. Clean up.
- **B10b**: Action's "Output Format" field not set — investigate if it's required or unused.
- **B10c**: Playbook has only ONE node (just calls the action) — sophistication expansion path (more nodes, scopes, skills, tools, knowledge) is R7+ scope.
- **NULL-schema action fallback test (B12)**: deferred — no admin UI to invoke arbitrary actions directly; programmatic coverage from task 040 unit tests accepted.
- **Workspace-summarize entry point (B5/Q5)**: deferred — new workspace playbook from task 033 Option A is wired in Dataverse but has no UI trigger yet; needs Phase C work.

### Hotfix Wave B-G9 dispatch plan (POST-COMPACT)

**Sequence**:

1. **B-G9a (CRITICAL)** — Widget schema-aware dispatch root-cause investigation + fix
   - Sub-agent reads `StructuredOutputStreamWidget.tsx` IN FULL (1775 LOC post-041)
   - Identifies why hardcoded TL;DR/Entities legacy rendering wins over `classifySchemaField()` dispatch
   - Fixes the precedence so 040+041 work in production
   - Adds an INTEGRATION test (not just unit test) that actually invokes the widget in a live-streaming scenario to prevent regression
   - 90-min budget
   - File boundaries: widget file + tests; do NOT touch SprkChatAgentFactory (B-G9b will)

2. **B-G9b (HIGH)** — Extend task 042 dedup directive to chat destination
   - Modify `SprkChatAgentFactory.cs` directive logic: apply single-sentence ack directive for chat destination too WHEN a playbook will render
   - Update CapabilityRouterDedupTests to cover chat destination
   - 30-min budget
   - File boundaries: SprkChatAgentFactory.cs + dedup tests; do NOT touch widget

3. **B-G9c (MED)** — Other bug fixes (parallel after a + b)
   - B6 (temperature)
   - B7 (Summary tab not default)
   - B8 (decide tab-per-run UX with user OR keep replace behavior)
   - B9 (slash vs natural-language detail)

4. **Re-deploy + user re-test** — BFF + SpaarkeAi + LegalWorkspace; user walkthrough again

5. **Phase B exit-gate sign-off** → dispatch task 053 → Phase C cascade unblocks

### File state at compaction

- `git status --short`: clean (working tree synced through `4d272fb88`)
- BFF on Spaarke Dev: `a62cccb48` deployed (Wave B-G8); widget consumers deployed
- Branch ahead of master by ~38 commits (Phase A + B + C-G1 + C-G2 + Wave B-G7/G8 hotfixes)

### Post-compaction recovery sequence (UPDATED FOR HOTFIX)

1. Read this file's "Walkthrough Findings + Hotfix Wave B-G9 Plan" section
2. Dispatch B-G9a sub-agent (widget root-cause + fix) FIRST — critical path
3. After B-G9a lands: dispatch B-G9b (CapabilityRouter chat-destination dedup) — can run in parallel with B-G9c
4. After all 3 land: deploy BFF + redeploy SpaarkeAi + LegalWorkspace web resources to Spaarke Dev
5. Surface for user re-walkthrough
6. Phase B exit-gate sign-off → dispatch task 053 → Phase C cascade

---

## Phase B overview

Pillar 5 — **Schema-aware output** (Q5 re-shaped design):
- `outputSchema` on **action** (intrinsic data shape; action-fixed)
- `destination` + `widgetType` on **node config** (per-playbook routing)
- `StructuredOutputStreamWidget` schema-aware (array → bullets; object → labeled key-value blocks)
- Duplicate-fire fix at **CapabilityRouter** (one user intent → one route → one playbook → one DeliverOutput)
- Migrate 4 existing actions: summarize-document-for-chat, summarize-document-for-workspace, matter-prefill, project-prefill

**Phase B task chain**:
1. **030** (D-B-01) — outputSchema field on action [FULL; parallel with 031]
2. **031** (D-B-02) — destination + widgetType on node config [FULL; parallel with 030]
3. **032** (D-B-03) — migrate summarize-document-for-chat [STANDARD; depends on 030, 031]
4. **033** (D-B-04) — migrate summarize-document-for-workspace [STANDARD; depends on 030, 031]
5. **034** (D-B-05) — migrate matter-prefill **NFR-07 regression** [FULL; depends on 030, 031]
6. **035** (D-B-06) — migrate project-prefill **NFR-07 regression** [FULL; depends on 030, 031]
7. **040** (D-B-07) — StructuredOutputStreamWidget array rendering [FULL; depends on 032, 033]
8. **041** (D-B-08) — StructuredOutputStreamWidget object rendering [FULL; depends on 040; sequential — same file]
9. **042** (D-B-09) — CapabilityRouter dedup [FULL; depends on 041, 025]
10. **048** — Phase B integration test [STANDARD; depends on 042, 034, 035]

Estimated Phase B duration: 1-2 weeks (per spec calendar).

---

## Post-Compaction Recovery Protocol (READ THIS BEFORE RESUMING)

When this conversation is compacted and a new context starts, do these steps IN ORDER:

### Step 1 — Verify branch + clean working tree

```bash
git branch --show-current    # expect: work/spaarke-ai-platform-unification-r6
git status                    # expect: clean working tree
git log --oneline -5          # expect: latest commit is the TASK-INDEX cleanup commit
```

### Step 2 — Verify the build + test baseline still holds

```bash
dotnet build src/server/api/Sprk.Bff.Api/        # expect: 0 errors, 16 baseline warnings
dotnet test tests/unit/Sprk.Bff.Api.Tests/       # expect: 6820+/6929 pass, 0 fail, 109 skip
```

If either command fails: investigate before dispatching Phase B work. Phase A closure
depends on these baselines holding.

### Step 3 — Read Phase A closure context (skim these in order)

1. `notes/phase-a-exit-gate.md` — the binding artifact for Phase A closure (4 GREEN exit criteria, 2 documented YELLOW flags, commit cross-reference table)
2. `CLAUDE.md` (project-scoped) — operating principles including "ADRs Are Defaults" (§ first invocation = ADR-033 / Wave 9)
3. `tasks/TASK-INDEX.md` — task statuses (all Phase A tasks now ✅)
4. This file's "Quick Recovery" section (above)

### Step 4 — Phase B kickoff

Tasks **030 + 031** are the parallel-safe entry into Phase B. Per the Phase B critical-path notes above:

- Both modify Dataverse entity schemas in different files (030 = `sprk_analysisaction`; 031 = playbook node config). Independent file targets → safe to dispatch in parallel.
- Both are **FULL rigor** tasks (production schema changes).
- Both are **Confirmation Triggers** per CLAUDE.md before deploying to Spaarke Dev.
- The 4 downstream action-migration tasks (032-035) depend on BOTH 030 and 031 landing.

**Dispatch approach** (proven across Phase A):
- Read each task's POML before dispatching (the POML is the binding spec).
- Sub-agent prompts include:
  - Full POML path reference
  - The "Stop and surface" trigger per "ADRs Are Defaults" principle
  - Explicit "files you OWN" and "files you MUST NOT touch" lists
  - Verification commands the sub-agent runs before reporting

#### Phase B sub-agent prompt alignment (MANDATORY for tasks 042, 048; recommended for 034, 035)

After merging master `6a717e43` (PR #360 — bff-ai-architecture-audit-r1), the repo gained 3 patterns + a `bff-extensions.md` §F that **govern any new DI registration in `Sprk.Bff.Api`**. Phase B sub-agent prompts MUST reference these when relevant:

| Task | Reference patterns | Why |
|---|---|---|
| 030, 031 | none (Dataverse schema only) | No BFF DI impact |
| 032, 033 | `public-contracts-facade.md` if action migration touches `IWorkspacePrefillAi` surface | preserves Pre-fill facade contract |
| **034, 035** | `public-contracts-facade.md`; `bff-extensions.md` NFR-07 binding | NFR-07 regression tests; pre-fill facade signature MUST stay unchanged |
| 040, 041 | none (TS/React widget only) | Frontend |
| **042** | **`endpoint-di-symmetry.md`** + **`bff-extensions.md` §F.1/F.2/F.3** if CapabilityRouter dedup touches DI in `Services/Ai/Chat/` | New DI = symmetric registration check required |
| **048** | **`bff-extensions.md` §F.2** (Fixture-Config-FIRST Inspection Protocol) | When integration tests fail/skip with DI symptom, inspect fixture config BEFORE code fix |

**Inline prompt snippet to paste into 042 + 048 dispatches**:

> ### DI-aware binding (post 2026-06-08 merge of PR #360)
>
> If your changes touch DI registration in any `*Module.cs` file: read `.claude/patterns/ai/endpoint-di-symmetry.md` and `.claude/constraints/bff-extensions.md` §F.1 BEFORE designing. Any conditional service registration MUST have a Null peer (per ADR-032 P3) registered in `AddNullObjectsForCompoundOff` for both compound-OFF branches. The §F.1 static-scan recipe is binding.
>
> If a test is Skip'd or fails with a DI / fixture symptom: §F.2 Fixture-Config-FIRST Inspection Protocol applies. Inspect `WorkspaceTestFixture` config + claims + mocks for missing/non-contract values BEFORE proposing a code fix.

### Step 4.5 — FR-21 acceptance criteria (added 2026-06-08)

Cross-project request from `spaarke-insights-engine-r2` audit (rationale: closes the LATENT BUG #1 class). All 5 criteria are satisfied by current Phase A code + tests; the spec.md FR-21 update codifies them as binding:

1. ✅ `NullInvokePlaybookAi.cs` exists at `Services/Ai/PublicContracts/NullInvokePlaybookAi.cs`; throws `FeatureDisabledException("ai.invoke-playbook.disabled", …)` per ADR-032 P3
2. ✅ Real impl registered in `AddPublicContractsFacade`; Null peer in `AddNullObjectsForCompoundOff` (both branches)
3. ✅ Transitive ctor-dep symmetry verified via `bff-extensions.md` §F.1 static-scan
4. ✅ Unit test `Null_InvokePlaybookAsync_ThrowsFeatureDisabledException` + `Null_InvokePlaybookAsync_ExceptionConvertsToProblemDetails503`
5. ✅ Integration test `ExecuteChatAsync_WhenFacadeIsNullKillSwitchActive_ReturnsFailureToolResult_NotInvalidOperationException` — added 2026-06-08

**errorCode**: aligned from `ai.playbook-invocation.disabled` → `ai.invoke-playbook.disabled` per R2 team's spec. The change is safe — task 020 was the introducing commit; no external clients had switched on the old string yet.

### Step 5 — Memory check (verify cross-session memories still relevant)

Memory files at `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke-wt-spaarke-ai-platform-unification-r6\memory\`:

- `MEMORY.md` — index of all memories
- `feedback_adrs-are-defaults-not-laws.md` — binding operating principle; codified in project CLAUDE.md; ADR-033 is the first worked example
- `feedback_pipeline-execution-style.md` — autonomous parallel dispatch + confirmation triggers
- `feedback_no-backcompat-hacks-for-small-counts.md` — small-count migration philosophy
- `project_r6-decisions.md` — Q1-Q11 binding decisions

These memories are still load-bearing for Phase B. No revisions needed at compaction.

### Key commits to know after resume

| Commit | What it represents |
|---|---|
| `9567cc1f` | Phase A exit-gate document |
| `99dd7a03` | Phase A closure bookkeeping (TASK-INDEX + current-task.md) |
| `26061612` | Task 028 (Phase A vertical-slice integration test) |
| `a7a0e051` | DI cycle fix (lazy IToolHandlerRegistry resolution) — unblocked WorkspaceEndpointsTests |
| `cc6d8e3b` | Tasks 022 + 025 (Pillar 4 closes; Option A engine extension per "ADRs Are Defaults") |
| `3ccb5304` | Wave 9 (ADR-033 streaming side-channel) — Q9 closes at 10/10 |

### What Phase B should produce (preview)

- Dataverse schema additions (030 + 031)
- 4 action migrations (032-035) — including 2 NFR-07 pre-fill regression tests (034 + 035)
- 1 widget refactor (040 + 041 sequential)
- 1 router dedup (042)
- 1 integration test (048)

Estimated 10 tasks, 1-2 weeks per spec calendar. Mostly STANDARD or FULL rigor; no new ADR expected (the Phase B work is implementation of the Q5-reshaped design).

---

### ✅ Wave 7c verification (2026-06-08, post-compaction)

The Wave 7c content was committed pre-compaction as a "checkpoint" (commit `52201189`) because the test suite had not yet been run at that time. Post-compaction verification:

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors, 16 baseline warnings** ✅
- `dotnet test --filter "Handlers.KnowledgeRetrievalHandlerTests|Handlers.VerifyCitationsHandlerTests"`: **55/55 PASS** (266ms) ✅
- `dotnet test --filter "Services.Ai.Handlers|ToolHandlerToAIFunctionAdapter|SprkChatAgentFactory"`: **450/450 PASS** (368ms) ✅

Wave 7c content (in commit `52201189`):
- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/KnowledgeRetrievalHandler.cs` (730 LOC)
- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/VerifyCitationsHandler.cs` (535 LOC)
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/KnowledgeRetrievalHandlerTests.cs` (580 LOC)
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/VerifyCitationsHandlerTests.cs` (508 LOC)
- NEW: 3 Dataverse seed row JSON files (`knowledge-source-get`, `knowledge-base-search`, `citation-verify`)
- MODIFIED: `ChatInvocationContext.cs` (adds `KnowledgeScope` field)
- MODIFIED: `SprkChatAgentFactory.cs` (removes hardcoded `KnowledgeRetrievalTools` + `VerifyCitationsTool` registration blocks)
- MODIFIED: `scripts/Seed-TypedHandlers.ps1` (adds 3 `$RowFiles` entries)

### Files Modified Since Last Compaction (committed in `66da08ca`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolResult.cs` — added Metadata field + ToolResultMetadataKeys + envelope records
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` — citationAccumulator + sseWriter ctor params + PostProcessMetadataAsync
- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` — AnalysisTool DTO + RequiredCapability field
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs` — mapper + $select for sprk_requiredcapability
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — data-driven block filters by RequiredCapability; passes citation/sseWriter to adapter
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` — 2 new sections: Returning Citations/Widget + Capability-Gated Tools
- `scripts/Add-AnalysisToolRequiredCapability.ps1` — NEW idempotent column deployment script
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ToolHandlerToAIFunctionAdapterTests.cs` — +13 tests
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` — +10 tests
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryToolResolutionTests.cs` — +9 tests
- `projects/spaarke-ai-platform-unification-r6/notes/wave-07b-adapter-infra.md` — bookkeeping
- `projects/spaarke-ai-platform-unification-r6/notes/wave-07b-capability-filter.md` — bookkeeping

### Critical Context

R6 is migrating 10 pre-R5 chat tool C# classes to data-driven `IToolHandler` implementations. The Q9 "big-bang" approach was REVISED to 3 sub-waves after Wave 7's first attempt surfaced architectural gaps. Wave 7b just built the infrastructure (citations-in-metadata + capability filter) that Wave 7c + Wave 8 depend on. The **"ADRs Are Defaults — Challenge When Warranted"** operating principle was established + codified during this session (project CLAUDE.md + feedback memory `feedback_adrs-are-defaults-not-laws.md`) — 4 surfacing events this session validated the principle works as designed.

---

## Wave Progress

### Completed (commits in chronological order)

| Commit | Wave | What landed |
|---|---|---|
| `274e2e41` | Init | Project artifacts (plan, CLAUDE.md, TASK-INDEX, 80 POML tasks) |
| `8c8230eb` | W1 | 001 (sprk_aipersona entity), 006 (IToolHandler rename) |
| `04e96a1d` | W2 | 002, 003, 004, 007, 009 |
| `786b9820` | W3 | 005 (Pillar 1 closed), 008 (JsonSchema), 100 (handler gate) |
| `acca4500` | W4 | 010 (adapter), 101-104 (4 deterministic handlers) |
| `1ff4d234` | W5 | 105-108 (4 LLM-assisted handlers) |
| `e598de76` | W6 | 011 (data-driven tool registration), 109 (handler dispatch tests) |
| `e5b39788` | Audit | Items 1+3+4-partial + CLAUDE.md ADRs-as-defaults principle + memory |
| `2147ed05` | Audit | Item 4 consolidation (Option A) + Item 2 R7 deferral |
| `9e3d4f93` | W7 partial | AnalysisQuery + TextRefinement migrated (2 of 4 trivial); KnowledgeRetrieval + VerifyCitations surfaced gaps |
| `66da08ca` | W7b infra | ToolResult.Metadata + adapter post-process; sprk_requiredcapability column + filter |
| `52201189` | W7c (checkpoint) | KnowledgeRetrieval + VerifyCitations handlers + tests + 3 seed rows; verified post-compaction 450/450 tests green |
| `b7c089cc` | W7c marker | current-task.md update; 450/450 verification documented |
| `3eb7d17d` | W8 | 4 handlers (DocumentSearch, WebSearch, CodeInterpreter, LegalResearch) + 7 seed rows + 115 new tests; factory −81 lines; deployed to Spaarke Dev 2026-06-08 |
| `2f8b7a79` | W8 marker | current-task.md update; W8 verification documented |
| `3ccb5304` | W9 | ADR-033 (concise + full + INDEX) + WorkingDocumentHandler + 3 seed rows + 40 new tests; ChatInvocationContext gains DocumentStreamWriter + AnalysisId; legacy WorkingDocumentTools factory block removed (REMOVED comment matches W7/7c/8 pattern); deployed to Spaarke Dev 2026-06-08. **Q9 closes at 10/10** ✅ |

### Pending

| Wave | Tasks | Notes |
|---|---|---|
| **10 (NEXT)** | Delete AnalysisExecutionTools (replaced by Pillar 3 invoke_playbook) + delete InvokeSummarizePlaybookTool + InvokeInsightsQueryTool bridges (replaced by Pillar 3) + optionally delete now-unused legacy migrated tool classes (per-class audit needed for non-LLM consumers — TextRefinementTools has known RefineTextAsync consumer in ChatEndpoints, so likely kept) | Cleanup; no ADR needed. Low risk. |
| 9 | ADR-032 Streaming chat-tool contract + WorkingDocumentTools | Per "ADRs are defaults" principle — yielded NFR-03 for this case |
| 10 | Delete AnalysisExecutionTools (replaced by Pillar 3 invoke_playbook) + delete InvokeSummarize + InvokeInsightsQueryTool bridges (task 023) | Cleanup |
| 028, 029 | Phase A integration test + Phase A exit gate | MUST address the 9 pre-existing WorkspaceEndpointsTests failures before exit |

---

## Wave 8 Dispatch Plan (next action)

Migrate 4 citation/SSE-state chat tools to data-driven `IToolHandler` implementations, using the validated Wave 7b infrastructure (ToolResult.Metadata citations/widget envelope + adapter post-processing + sprk_requiredcapability filter).

### Targets
1. **DocumentSearchTools** — search SharePoint-Embedded indexed documents; returns citations + widget
2. **WebSearchTools** — Bing/web search; returns citations
3. **CodeInterpreterTools** — Python sandbox; returns rich widget output
4. **LegalResearchTools** — legal corpus retrieval; returns citations + capability-gated

### Pattern (apply to each)
- NEW `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/{Tool}Handler.cs` returning `ToolResult { Metadata = { citations, widget } }`
- Remove hardcoded block in `SprkChatAgentFactory.cs` (registration replaced by data-driven row enumeration)
- NEW `infra/dataverse/sprk_analysistool-{toolcode}-row.json` seed row(s) — descriptive UPPER-KEBAB-CASE toolcode (no `@v1` suffix)
- Set `sprk_requiredcapability` correctly: null if open to all playbooks; capability key if gated (LegalResearch likely gated)
- Append `$RowFiles` map entry in `scripts/Seed-TypedHandlers.ps1`
- Unit tests under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/{Tool}HandlerTests.cs`
- Bookkeeping note `projects/spaarke-ai-platform-unification-r6/notes/wave-08-{tool}-migration.md`

### Dispatch decision (parallel vs serial)
4 sub-agents working in different handler files = parallel-safe. All edit the same `SprkChatAgentFactory.cs` to remove their hardcoded blocks → file-overlap risk. **Recommended**: dispatch 4 in parallel BUT each agent only removes their own labeled `--- {Tool}Tools ---` block; main session resolves any merge artifacts in factory file before commit.

### Stop-and-surface triggers
Per CLAUDE.md "ADRs Are Defaults" principle. Likely triggers in Wave 8:
- Tool needs streaming chat-tool contract (defer to Wave 9 ADR-032 path) → surface, don't extend interface unilaterally
- Tool has per-session stateful coupling beyond ChatInvocationContext → surface
- Tool requires new ChatInvocationContext field → name the field + scope, then proceed (low risk; same pattern as Wave 7c KnowledgeScope)

### After Wave 8 completes (main session)
- Build verify
- Full handler+adapter sweep verify
- Commit + push as one Wave 8 commit
- Proceed to Wave 9 (ADR-032 + WorkingDocumentTools)

---

## Known Issues (NOT blocking continuation)

### Pre-existing integration test failures (9 tests in WorkspaceEndpointsTests)

Failing tests in `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.dll`:
- `WorkspaceEndpointsTests.AiSummary_ValidRequest_Returns200WithAiSummaryShape`
- `WorkspaceEndpointsTests.AiSummary_MissingEntityType_Returns400WithFieldError`
- `WorkspaceEndpointsTests.AiSummary_EmptyEntityId_Returns400WithFieldError`
- `WorkspaceEndpointsTests.AiSummary_SupportedEntityTypes_AllReturn200`
- `WorkspaceEndpointsTests.AiSummary_UnsupportedEntityType_Returns400`
- `WorkspaceEndpointsTests.AiSummary_ContextTooLong_Returns400`
- `WorkspaceEndpointsTests.MatterPreFill_InvalidFileType_Returns400`
- (+2 others — exact names in commit `66da08ca` message)

**Verified NOT caused by Wave 7b** via stash+rerun diff — failures are pre-existing R6 regressions from an earlier wave. Investigation deferred to Phase A integration test (task 028) + exit gate (task 029) where they must be addressed before Phase A exit.

### `sprk_analysistool` 10-char `sprk_toolcode` limit (FIXED)

User manually extended column to 100 chars during audit Item 4. New rows use descriptive UPPER-KEBAB-CASE codes (no `@v1` suffix). Pattern documented in `HandlerRegistrationConventions.md`.

### Legacy `scripts/seed-data/` directory (partial cleanup pending)

`Deploy-Tools.ps1` + `tools.json` archived to `scripts/_archive/2026-06-pre-r6-tool-seeding/`. The orchestrator `Deploy-All-AI-SeedData.ps1` now fails if invoked (references missing archived script). Follow-up cleanup needed: update orchestrator to skip archived script OR archive orchestrator itself. Non-blocking; documented in archive README.

### `FallbackScopeCatalog.cs` misaligned names

Hardcoded `SYS-TL-001..009` names in `FallbackScopeCatalog.cs` are inert AI-builder hints, NOT runtime keys. Names don't match actual Dataverse rows post-consolidation. Worth cleanup separately; doesn't affect R6 runtime.

---

## Session Log (2026-06-07 → 2026-06-08)

Key decision points + outcomes:

1. **Q9 path revised**: Original "all 10 in one batch" replaced with 3 sub-waves (7 + 8 + 9) + Wave 10 cleanup. Catalyst: Wave 7 first sub-agent dispatch surfaced architectural gaps that wouldn't have been visible in big-bang execution.

2. **ADRs-Are-Defaults principle established**: User pushed back on initial framing that "needs ADR therefore can't be done." Reframed: ADRs are scope-discipline defaults, not architectural walls. NFR-03 (no new ADRs in R6) is revisable per-case. Codified in project CLAUDE.md + feedback memory + applied to Wave 9 ADR-032 streaming contract decision.

3. **Audit 5-item surface**: Beyond Q9, identified 5 places where R6 implementation may have rule-followed instead of optimal-chosen: (1) JSON Schema validator deferral, (2) Redis cache for tool list, (3) rate-limit gap on scope endpoints, (4) toolcode 10-char workaround, (5) WorkingDocumentTools deferral. Items 1+3+4 fixed in R6; item 2 R7-deferred with measurement criteria; item 5 to be addressed in Wave 9 with ADR-032.

4. **4 sub-agent surfacing events this session** (all correct):
   - Wave 7 KnowledgeRetrieval: ToolResult.Metadata + adapter post-processing gap → resolved in Wave 7b A
   - Wave 7 VerifyCitations: per-playbook capability filter gap → resolved in Wave 7b B
   - Item 4 first attempt: seed script upsert key (sprk_toolcode vs sprk_handlerclass) → resolved with Option A
   - Item 4 second attempt: 4 findings (A: wrong OData key, B: 3 Playbook-only intent, C: nested envelope, D: pre-R6 row dependency check) → all resolved

5. **No BFF API deploys yet** (only Dataverse data deploys). Confirmed with user 2026-06-08: BFF code changes are in working tree + branch + origin; no Azure App Service deployment of R6 work yet. Likely deployment checkpoint: after Phase A exit (task 029) or before Wave 9.

---

## Files To Read When Resuming

In priority order:

1. This file (current-task.md) — first
2. `projects/spaarke-ai-platform-unification-r6/CLAUDE.md` — project rules including the new ADRs-Are-Defaults section
3. `projects/spaarke-ai-platform-unification-r6/notes/wave-07b-adapter-infra.md` — pattern for Wave 7c handlers returning Metadata
4. `projects/spaarke-ai-platform-unification-r6/notes/wave-07b-capability-filter.md` — pattern for capability-gated migrations
5. `projects/spaarke-ai-platform-unification-r6/notes/wave-07-knowledge-retrieval-migration.md` — original surfacing analysis
6. `projects/spaarke-ai-platform-unification-r6/notes/wave-07-verify-citations-migration.md` — original surfacing analysis
7. `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` — patterns updated by Wave 7b
8. `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — overall task status

Memory files at `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke-wt-spaarke-ai-platform-unification-r6\memory\`:
- `feedback_pipeline-execution-style.md` — execution preferences
- `feedback_no-backcompat-hacks-for-small-counts.md` — design preferences
- `feedback_adrs-are-defaults-not-laws.md` — NEW this session; binding operating principle
- `project_r6-decisions.md` — Q1-Q11 + sequencing decisions

---

## Resume Instructions

When context returns:

1. **First**: read this file's "Quick Recovery" section
2. **Verify clean state**: `git status` (should show working tree clean; last commit `52201189`)
3. **Verify branch**: `git branch --show-current` should show `work/spaarke-ai-platform-unification-r6`
4. **Continue with Wave 8**: dispatch 4 parallel sub-agents per "Wave 8 Dispatch Plan" section above
5. **After Wave 8**: build verify + commit + push; then Wave 9 (ADR-032 + WorkingDocumentTools)

To get the prior session's full final status: read commit messages from `git log --oneline -15` for the wave-by-wave landed work.

---

*This checkpoint is the source of truth for resuming work. Quick Recovery section is bulletproof against context loss.*
