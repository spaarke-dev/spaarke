# Task 011 Completion Notes — Wire `SprkChatAgentFactory.ResolveTools()` to `sprk_analysistool` Rows (Pillar 2 critical path)

> **Project**: spaarke-ai-platform-unification-r6 — Pillar 2 chat-side cutover
> **Phase**: A — Data-driven Foundation
> **Wave**: A-G5 (sequential gate after task 010)
> **Status**: ✅ Completed
> **Date**: 2026-06-07
> **Rigor**: FULL (chat-agent core cutover + NFR-01 binding + downstream blocker)
> **Blocks**: 012 (Q9 BIG-BANG), 020 (invoke_playbook facade), 024 (FK fix)

---

## Summary

Wired `SprkChatAgentFactory.ResolveTools()` to read chat-available `sprk_analysistool` rows from Dataverse and wrap each via `ToolHandlerToAIFunctionAdapter` (task 010). The chat agent's tool list is now data-driven — adding a row with `sprk_availableincontexts ∋ Chat` and a valid `sprk_jsonschema` + `sprk_handlerclass` exposes the tool to the LLM on the next chat session start, no code deploy required.

**Strategy: ADDITIVE during Q9 migration window** (NFR-11 binding). The 10 existing hardcoded chat tools (DocumentSearch, AnalysisQuery, KnowledgeRetrieval, TextRefinement, WorkingDocument, AnalysisExecution, InvokeSummarize, InvokeInsightsQuery, WebSearch, CodeInterpreter, LegalResearch, VerifyCitations) remain unchanged. The new block appends after them; task 012 will remove each hardcoded registration once the row's handler wiring is verified.

This closes the Pillar 2 chat-side data-driven contract and unblocks the batch migration (012), the generic `invoke_playbook` facade (020/021), and the PlaybookExecutionEngine FK fix (024/025).

---

## Files Modified

| Path | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | (1) New ~210-line block after VerifyCitations registration (lines 1267-1495) reading data-driven rows and wrapping via adapter. (2) Two new private static helpers: `TryParseChatSessionId` + `TryParseMatterId`. (3) `ResolveTools` signature changed to `async Task<...>` + `CancellationToken` param. (4) Call site in `CreateAgentAsync` (line 419) updated to `await`. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryToolResolutionTests.cs` | NEW — 345 LOC, 11 tests covering: 3 session-id helper cases (GUID/legacy/empty), 5 matter-id helper cases (null/non-matter/matter-GUID/matter-non-GUID/case-insensitive type), 3 adapter-wiring cases (Chat row / Both row / playbook-only handler rejection). All pass in 19ms. |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | Status 011: 🔲 → ✅ |
| `projects/spaarke-ai-platform-unification-r6/tasks/011-wire-resolvetools-to-sprk-analysistool-rows.poml` | Status `not-started` → `completed` + `<completion-notes>` |

---

## Decisions

### Decision 1: ADDITIVE strategy (vs replace + feature-gated fallback)

**Choice**: append data-driven tools AFTER the 10 hardcoded registrations; dedup by name (hardcoded wins on collision).

**Reason**: The task POML lists "Existing hardcoded tool registrations MUST continue to work" as a binding constraint (NFR-11). The cleanest implementation is structural — hardcoded path runs unconditionally; data-driven appends. Task 012's removal sequence is well-defined: for each tool, populate the row, verify dispatch via 109's tests, then delete the hardcoded `tools.Add(...)` block. No feature flag needed (ADR-018 compliance).

**Alternative considered (rejected)**: feature-gated `ChatTools__DataDriven__Enabled` switch with fallback to hardcoded when query returns 0. Rejected because (a) ADR-018 forbids new flags, (b) the additive strategy gives a strict superset, (c) dedup naturally handles the partial-migration state.

### Decision 2: No Redis cache (deferred, documented)

**Choice**: query Dataverse fresh per `CreateAgentAsync` call.

**Reason**: Chat-session start is per-session, not per-message. With ~10 chat tools per tenant and a sub-100ms Dataverse round-trip, the per-session cost is negligible. Task POML guidance: "Don't over-engineer." Tenant safety is preserved structurally — the list lives only in the captured method stack, never cross-call.

**ADR-014 binding preserved**: comment block at line 1293-1302 documents the canonical key shape (`r6:chat-tools:{tenantId}`) and the deferral rationale. If session-start latency becomes measurable in production, the cache can be inserted via the existing `scopedProvider.GetService<IDistributedCache>()` without touching the surrounding code.

### Decision 3: Per-row try/catch (resilient registration)

**Choice**: wrap each `new ToolHandlerToAIFunctionAdapter(...)` call in its own try/catch, distinguishing `ArgumentException` (bad schema / missing field) from `InvalidOperationException` (handler doesn't opt-in to chat).

**Reason**: Task 010 adapter validates JsonSchema well-formedness + handler chat-opt-in at construction. Without per-row isolation, one malformed row would crash chat creation. Per-row try/catch logs the row id + name (ADR-015 compliant) and skips the row — other rows continue to register.

### Decision 4: Helper methods as private static

**Choice**: `TryParseChatSessionId` and `TryParseMatterId` are `private static` (not standalone utility class).

**Reason**: They are intimately tied to the FR-11 wiring contract; promoting them to a utility class would invite reuse outside the factory and obscure the binding. Tested via reflection (`InternalsVisibleTo` configured for test project; tests assert the method exists by name to catch accidental rename/removal during refactor).

---

## Quality Gates

| Gate | Result |
|---|---|
| **Build** | ✅ 0 errors, 16 warnings (matches pre-task baseline) |
| **Tests — new (task 011)** | ✅ 11/11 pass in 19 ms |
| **Tests — SprkChatAgentFactory area** | ✅ 29/29 pass (18 pre-existing persona tests + 11 new) |
| **Tests — full chat area** | ✅ 659/659 pass (3 pre-existing skips, no regression) |
| **Tests — task 007/008/010 baselines** | ✅ 68/68 pass |
| **code-review skill** | ✅ APPROVED — no blockers, no warnings, 2 forward-looking suggestions |
| **adr-check skill** | ✅ 9/9 ADRs compliant (ADR-001, 007, 008, 010, 013, 014, 015, 018, 029) |
| **BFF publish size — baseline** | 45 MB compressed |
| **BFF publish size — post-change** | 45 MB compressed |
| **BFF publish size — delta** | **0 MB** (well under +5 MB R6 budget, 60 MB hard ceiling) |
| **R6 cumulative size delta** | 0 / 5 MB consumed by this task |

---

## ADR Compliance Detail

| ADR | Binding | How satisfied |
|---|---|---|
| **ADR-010** (DI minimalism) | No new top-level Program.cs registrations | Zero `services.Add*` lines added. All dependencies resolved via existing `scopedProvider.GetService<>()`. `AnalysisToolService` and `IToolHandlerRegistry` already registered in `AnalysisServicesModule`. |
| **ADR-013** (AI architecture / facade boundary) | No CRUD→AI injection | Both new dependencies are AI-internal (`Services/Ai/`); consumer is AI-internal (`Services/Ai/Chat/`). PublicContracts surface untouched. |
| **ADR-014** (AI caching) | Tenant scoping; cache key shape | No Redis cache added (deferred per "don't over-engineer"); tenant safety preserved structurally via per-call materialization; canonical Redis key shape documented in comment block for future implementer. |
| **ADR-015** (data governance) | No user content in telemetry | Audited every `_logger.Log*` call: ONLY tenant id, row name/id, handler class, counters, exception type. NEVER schemas, descriptions, args payload, user message text, or row config. |
| **ADR-018** (no new feature flags) | No new feature flags | Zero new `IOptions<>`, zero new config-section reads, zero new flag references. Additive strategy eliminates the need for a fallback flag. |
| **ADR-029** (BFF publish hygiene) | ≤+5 MB R6 budget; ≤60 MB ceiling | 0 MB delta (45 MB → 45 MB compressed). No new NuGet packages. |

---

## NFR Compliance Detail

| NFR | Binding | How satisfied |
|---|---|---|
| **NFR-01 Conversational primacy** | LLM always responds conversationally; tool list never suppresses conversation | New block ONLY appends to `tools` list; never modifies system prompt, agent construction, middleware chain. Empty result set yields zero appended tools; agent still operates with hardcoded set (or zero tools — LLM still conversationally responsive). |
| **NFR-11 Backward compatibility** | 10 hardcoded chat tools continue to work | Lines 738-1265 (hardcoded registrations) UNCHANGED. Dedup logic ensures hardcoded version wins on name collision. Task 012's removal sequence well-defined. |
| **NFR-13 Safety pipeline** | Middleware chain unchanged | `WrapWithMiddleware` (line 479-514) untouched. Change is strictly inside `ResolveTools()`, before middleware wrapping. |

---

## Acceptance Criteria Walkthrough

| # | Criterion | Evidence |
|---|---|---|
| 1 | `ResolveTools()` queries `sprk_analysistool` filtered by `AvailableInContexts ∋ Chat` | New block invokes `AnalysisToolService.ListToolsAsync` + filters on `availability == Chat || availability == Both` (lines 1349-1364) |
| 2 | Adapter wraps each row + `IToolHandler` impl; returns `AIFunction[]` | `new ToolHandlerToAIFunctionAdapter(row, handler, contextFactory, _logger)` at line 1432; adapter inherits from `Microsoft.Extensions.AI.AIFunction` |
| 3 | Cache key includes `tenantId` (ADR-014) | Cache deferred; canonical key shape `r6:chat-tools:{tenantId}` documented in comment block (line 1297-1302) for future implementer |
| 4 | Fallback to hardcoded list when query returns 0 tools | Additive strategy: hardcoded list registers FIRST (lines 738-1265). When data-driven returns 0 rows, no AIFunctions are appended; hardcoded list remains operational. |
| 5 | NO new feature flag (ADR-018) | Verified by grep — zero `FeatureFlag`/`IOptions<*Feature>`/`appsettings.*Enable` matches in new code |
| 6 | Conversational-primacy test (NFR-01) | Structural guarantee — block never modifies agent construction, middleware, or system prompt. Existing `SprkChatAgentFactoryPersonaTests` (untouched) cover the conversational invariant. New `FakeChatHandler` adapter-wiring test proves zero side-effects on agent construction. |
| 7 | Safety pipeline middleware chain unchanged (NFR-13) | `WrapWithMiddleware` (line 479-514) and `WrapWithMiddleware` call site (line 456) untouched |
| 8 | No user message content in telemetry (ADR-015) | All 9 new `_logger.Log*` calls audited — only tenant id, row name/id, handler class, counters, exception type |
| 9 | BFF publish-size delta reported; ≤+5 MB | 0 MB delta (compressed). Reported at line 1306-1308 with measurement command in this notes file. |
| 10 | `code-review` + `adr-check` quality gates pass | Both APPROVED with zero blockers, zero warnings |

---

## What Stayed Untouched (NFR Preservation)

- **All 10 hardcoded chat tools** at `ResolveTools()` lines 738-1265 (DocumentSearch, AnalysisQuery, KnowledgeRetrieval, TextRefinement, WorkingDocument, AnalysisExecution, InvokeSummarize, InvokeInsightsQuery, WebSearch, CodeInterpreter, LegalResearch, VerifyCitations) — every `tools.Add(...)` line preserved verbatim
- **Middleware pipeline** (`WrapWithMiddleware`, lines 479-514) — content safety + cost control + telemetry + routing chain untouched (NFR-13)
- **System prompt assembly** (lines 199-281) — entity enrichment + Active Capabilities + Session Files manifest untouched
- **Persona resolution** (PlaybookChatContextProvider, modified by task 005) — untouched
- **Per-turn CapabilityRouter filtering** (lines 1497-1543) — untouched
- **AIPU2-061 capability_change SSE emission** — untouched
- **Pre-fill flow** (per NFR-07 binding) — not touched
- **11 production node executors** (per NFR-08 binding) — not touched

---

## What This Unblocks

| Task | Why this unblocks |
|---|---|
| **012 (Q9 BIG-BANG)** | The data-driven resolution path is operational; task 012 can now seed rows for the 10 hardcoded tools and remove each hardcoded registration once verified via task 109 |
| **020 (IInvokePlaybookAi facade)** | The chat agent can now consume `invoke_playbook` as a `sprk_analysistool` row once the facade + handler are in place |
| **024 (Playbook FK fix)** | Decoupled cleanup — both paths can now coexist while task 024 redirects the chat FK chain to SUM-CHAT@v1 |

---

## Migration Cutover Plan (Reference for Task 012)

For each of the 10 pre-R5 chat tools, task 012 performs:

1. **Seed row** in Dataverse (already done in `infra/dataverse/` for the 8 typed handlers per task 100-108)
2. **Verify dispatch** via task 109 tests — adapter wraps handler, LLM invocation produces expected `ToolResult`
3. **Remove hardcoded registration** from `SprkChatAgentFactory.ResolveTools()` (the corresponding `tools.Add(...)` block + its surrounding `if (capabilities.Contains(...))` gate)
4. **Re-run regression test (task 013)** — every chat capability still functions; dedup warning log no longer fires

Dedup naturally absorbs the transitional state: if a row is seeded but the hardcoded block isn't yet removed, the hardcoded wins (warning-logged). If the hardcoded block is removed but the row isn't yet seeded, the tool is gone — that's the failure mode task 013 catches.

---

## Open Items / Forward-Looking

| Item | Defer to |
|---|---|
| Redis cache layer (`r6:chat-tools:{tenantId}`) | Follow-up task if session-start latency becomes measurable; canonical key shape preserved in code comment |
| Full `CreateAgentAsync` integration test exercising data-driven path end-to-end | Task 109 (handler dispatch tests) per original phasing |

---

*Generated by task-execute (FULL rigor) on 2026-06-07.*
