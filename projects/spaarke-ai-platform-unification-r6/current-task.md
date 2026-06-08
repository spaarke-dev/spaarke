# Current Task State — spaarke-ai-platform-unification-r6

> **Last Updated**: 2026-06-08 (by context-handoff before compaction at ~21% remaining)
> **Recovery**: Read "Quick Recovery" section first
> **Last Commit**: `66da08ca` (Wave 7b infra — adapter citations/widget + sprk_requiredcapability filter)
> **Branch**: `work/spaarke-ai-platform-unification-r6` (pushed to origin; clean working tree)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | A (data-driven foundation) — ~25 of ~30 Phase A tasks complete |
| **Wave** | 7c PARTIAL (work on disk, not yet verified end-to-end) |
| **Last Committed Wave** | 7b infra (`66da08ca`) |
| **Status** | ⚠️ Uncommitted Wave 7c work present — handlers + seed rows + tests on disk; build is CLEAN (0 errors) but full test suite has not yet been run + the work has not been committed |
| **Next Action** | (1) Run full test suite to verify Wave 7c work; (2) commit Wave 7c as one atomic commit; (3) push; (4) proceed to Wave 8 (4 citation/SSE-state tools) |

### ⚠️ Uncommitted Wave 7c partial work (on disk; build clean; tests NOT yet run)

These files exist on disk but are not yet committed. Source: somewhere between Wave 7b commit and current state, an agent (or manual edit) produced this work. Build verified clean. **Test suite has NOT yet been run against this state.**

- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/KnowledgeRetrievalHandler.cs` (730 LOC)
- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/VerifyCitationsHandler.cs` (535 LOC)
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/KnowledgeRetrievalHandlerTests.cs` (580 LOC)
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/VerifyCitationsHandlerTests.cs` (508 LOC)
- NEW: `infra/dataverse/sprk_analysistool-knowledge-source-get-row.json`
- NEW: `infra/dataverse/sprk_analysistool-knowledge-base-search-row.json`
- NEW: `infra/dataverse/sprk_analysistool-citation-verify-row.json`
- MODIFIED: `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` (likely adds KnowledgeScope field — Wave 7b infra question that was flagged)
- MODIFIED: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (likely removes hardcoded KnowledgeRetrievalTools + VerifyCitationsTool blocks)
- MODIFIED: `scripts/Seed-TypedHandlers.ps1` (likely adds 3 new $RowFiles entries)

**Recommended action on resume**: run `dotnet test tests/unit/Sprk.Bff.Api.Tests/` to verify; if tests pass, commit as one Wave 7c commit; if tests fail, investigate before committing.

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

### Pending

| Wave | Tasks | Notes |
|---|---|---|
| **7c (NEXT)** | KnowledgeRetrieval + VerifyCitations re-attempt | Using new Wave 7b infra |
| 8 | DocumentSearch + WebSearch + CodeInterpreter + LegalResearch | 4 citation/SSE-state migrations using validated pattern |
| 9 | ADR-032 Streaming chat-tool contract + WorkingDocumentTools | Per "ADRs are defaults" principle — yielded NFR-03 for this case |
| 10 | Delete AnalysisExecutionTools (replaced by Pillar 3 invoke_playbook) + delete InvokeSummarize + InvokeInsightsQueryTool bridges (task 023) | Cleanup |
| 028, 029 | Phase A integration test + Phase A exit gate | MUST address the 9 pre-existing WorkspaceEndpointsTests failures before exit |

---

## Wave 7c Dispatch Plan (next action)

Dispatch 2 parallel sub-agents:

### Sub-agent A: KnowledgeRetrievalHandler
- **Files**: NEW `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/KnowledgeRetrievalHandler.cs` + 2 seed rows (`infra/dataverse/sprk_analysistool-knowledge-source-get-row.json` + `sprk_analysistool-knowledge-base-search-row.json`)
- **Hardcoded block to remove**: `SprkChatAgentFactory.cs` lines ~816-846 (`--- KnowledgeRetrievalTools ---`)
- **Uses Wave 7b infra**: returns `ToolResult { Metadata = { citations, widget } }`; adapter post-processes
- **Open design question to handle**: Does `ChatInvocationContext` carry `KnowledgeScope`? If not, agent should either (a) extend context, or (b) resolve via DI accessor. Stop-and-surface if non-trivial.
- **2 LLM functions, same handler**: dispatch via `sprk_configuration.method` discriminator (TextRefinement pattern)
- **Capability gate**: This tool is NOT capability-gated in the hardcoded version → `sprk_requiredcapability = null`
- **Descriptive toolcodes**: `KNOWLEDGE-SOURCE-GET`, `KNOWLEDGE-BASE-SEARCH`

### Sub-agent B: VerifyCitationsHandler
- **Files**: NEW `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/VerifyCitationsHandler.cs` + 1 seed row (`infra/dataverse/sprk_analysistool-citation-verify-row.json`)
- **Hardcoded block to remove**: `SprkChatAgentFactory.cs` lines ~1223-1265 (`--- VerifyCitationsTool ---`)
- **Uses Wave 7b infra**: capability filter via `sprk_requiredcapability = "verify_citations"` (matches existing `PlaybookCapabilities.VerifyCitations`)
- **Single LLM function**: 1 row, no method dispatch needed
- **Capability gate preserved**: row sets `sprk_requiredcapability = "verify_citations"` — Wave 7b filter ensures only playbooks with that capability see the tool
- **Descriptive toolcode**: `CITATION-VERIFY`

### Both agents must
- Follow project CLAUDE.md "ADRs Are Defaults" principle — stop-and-surface on any new trade-off
- Use bookkeeping pattern: write `projects/spaarke-ai-platform-unification-r6/notes/wave-07c-{handler}-migration.md`; NOT touch current-task.md or TASK-INDEX.md
- Add to `scripts/Seed-TypedHandlers.ps1` `$RowFiles` map (append-only)
- Run idempotent seed deploy + MCP verify
- Add unit tests under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/{Handler}Tests.cs`

### After Wave 7c completes (main session)
- Build verify (`dotnet build src/server/api/Sprk.Bff.Api/`)
- Test verify (`dotnet test tests/unit/Sprk.Bff.Api.Tests/`)
- Commit + push as one Wave 7c commit
- Proceed to Wave 8 (4 citation/SSE-state migrations using same pattern)

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
2. **Verify clean state**: `git status` (should show working tree clean; last commit `66da08ca`)
3. **Verify branch**: `git branch --show-current` should show `work/spaarke-ai-platform-unification-r6`
4. **Continue with Wave 7c**: dispatch 2 parallel sub-agents per "Wave 7c Dispatch Plan" section above
5. **After Wave 7c**: build verify + commit + push; then Wave 8 (4 parallel handler migrations)

To get the prior session's full final status: read commit messages from `git log --oneline -15` for the wave-by-wave landed work.

---

*This checkpoint is the source of truth for resuming work. Quick Recovery section is bulletproof against context loss.*
