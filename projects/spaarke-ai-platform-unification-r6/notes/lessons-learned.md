# R6 Lessons Learned

> **Project**: spaarke-ai-platform-unification-r6 (closed 2026-06-29)
> **Author**: Project wrap-up (task 090)
> **Successor reads this as**: R7-style carry-forward, mirroring how R6 read R5's lessons-learned.md
> **Companion**: [`r7-backlog.md`](r7-backlog.md) — the explicit deferred-work list with GitHub pointers

---

## 0. Bottom line

R6 set out to **converge** the conversational chat-agent with the production playbook engine — closing nine systemic architecture gaps R5 surfaced (persona hardcoded, tool registry ignored, FK bypass, schema-aware rendering missing, workspace ↔ assistant one-way, execution opaque, memory unwired, command vocabulary informal, widget visibility contract missing). It shipped 9 pillars across 4 phases + 1 parallel handler workstream + a Q7-expanded memory UI; total: 82 tasks ✅ in TASK-INDEX, 1 withdrawn (094 — already wired), 4 DEFs pulled back into scope at closeout and shipped to master. Backend deployed via PR #401 + master-merge `ecb650e44`. After R6, R7+ feature work should default to **"design a playbook in data + declare its output schema + reference scopes"** instead of writing more bespoke C# bridges.

---

## 1. The four R5 gap families — addressed status

R5 surfaced four architectural gap families. R6's pillars map 1:1 onto closing them.

### Gap A — Two upload paths, one mental model (R5) → CLOSED by Pillar 5

**R5 framing**: Inline upload (FR-07) and server-side upload created two parallel routing surfaces that had to be kept in sync manually. Result: "Path A vs Path B parallelism" was a smell that surfaced rendering bugs.

**R6 close**: Pillar 5 (Q5 re-shape) moved routing intent OFF of the upload path and ONTO the playbook's per-node config: `destination` + `widgetType` on the node, `outputSchema` on the action. The widget reads from action's outputSchema and renders schema-aware. The CapabilityRouter (later retired by chat-routing-redesign-r1) enforced one user intent → one route → one playbook → one render. The "two paths" became "two surface entry points routing to one structural destination."

**Residual gap**: Until chat-routing-redesign-r1's FR-23 tool-filtering replacement (PR #509 merged 2026-06-27) the dedup invariant lived in CapabilityRouter. That work is now in the chat-handler chain. DEF-003 (#474) shipped the maker-facing config UI so playbook authors can set the routing without hand-editing `sprk_configjson`.

### Gap B — Chat persona + tool registry + playbook FK bypass (R5) → CLOSED by Pillars 1, 2, 4

**R5 framing**: `SprkChatAgentFactory.BuildDefaultSystemPrompt()` hardcoded persona text in C#; chat tools were 12 hardcoded classes instead of `sprk_analysistool` rows; `summarize-document-for-chat@v1` referenced an action by alternate key instead of FK.

**R6 close**:
- **Pillar 1**: `sprk_aipersona` standalone entity with `scopetype` (Global / Tenant / PlaybookAttached); most-specific-wins resolution (SYS- < CUST- < playbook-attached) via `IScopeResolverService` → `AnalysisPersonaService.GetEffectivePersonaAsync`. SYS-DEFAULT seeded with prior hardcoded prompt verbatim. **DEF-002 closeout (2026-06-29)** discovered the playbook-attached layer was a stub that filtered only by `scopetype` (returning the FIRST PlaybookAttached row regardless of which playbook) — fixed by reading `sprk_analysisplaybook.sprk_playbookpersona` FK + resolving persona by PK.
- **Pillar 2**: `IToolHandler` registry with `AvailableInContexts` enum (Playbook/Chat/Both) + `JsonSchema` field. All 10 pre-R5 chat tools migrated to handlers (Q9 batch, 3 sub-waves). 8 new typed handlers built (DateExtractor, FinancialCalculator, ClauseComparison, FinancialCalculation, EntityExtractor, ClauseAnalyzer, RiskDetector, InvoiceExtraction).
- **Pillar 4**: Playbook FK chain validated at startup; `SessionSummarizeOrchestrator` invokes `PlaybookExecutionEngine.ExecuteAsync(playbookId)` directly. Alternate-key lookup removed from chat path.

**Residual gap**: ISS-003 (#510) surfaced that `spaarke-daily-update-service`'s `daily-briefing-narrate.json` deploy writes to the vestigial `sprk_capabilities` field (DEF-004 removed it from schema) — their next deploy will fail until they switch to `sprk_playbookcapabilities`. Tracked on backlog.

### Gap C — Schema-aware rendering was implicit (R5) → CLOSED by Pillar 5 + Pillar 9

**R5 framing**: String-only widget; TL;DR + Entities rendering bugs in R5 walkthrough showed implicit shape assumptions.

**R6 close**:
- **Pillar 5**: `sprk_analysisaction.outputSchema` JSON field (intrinsic shape); `StructuredOutputStreamWidget` reads schema and renders schema-aware (array → bullets; object → labeled key-value blocks). 4 actions migrated (summarize-chat, summarize-workspace, matter-prefill, project-prefill).
- **Pillar 9**: `getAgentVisibleState(): SerializedWidgetState` widget contract. `WorkspaceWidgetRegistry` extended with optional `getVisibleState?: () => unknown`. Per-widget implementations land back-shape data into agent system prompt for Assistant-visible tabs.

**Residual gap**: 4 of the 11 production widget types ship `getAgentVisibleState`; the other 7 default to opaque (intentional — privacy by default per Pillar 6a). Future widgets opt in case-by-case.

### Gap D — Workspace ↔ Assistant one-way (R5) → CLOSED by Pillar 6 (6a/6b/6c split)

**R5 framing**: Agent could write to workspace via Send-to-Workspace but couldn't READ tab state. No execution-trace surface.

**R6 close**:
- **6a**: `WorkspaceTab` canonical TS interface; Redis hot tier (24h) + Cosmos durable persistence on pin (Q4 hybrid); `GET /api/workspace/state` endpoint; per-turn snapshot in `SprkChatAgentFactory.CreateAgentAsync` system prompt.
- **6b**: Chat tools `send_workspace_artifact` / `update_workspace_tab` / `close_workspace_tab` / `get_workspace_tab_content` (4th added by chat-routing-redesign-r1 task 118b) — registered as `sprk_analysistool` rows. User affordances: "Send to Workspace", "Add to Assistant" toggle (DEF closeout 098), "Pin to Matter". Q8 conflict-resolution: user wins; agent reads `lastUserEditAt` and refuses on stale write with re-read prompt.
- **6c**: Execution-trace widget in Context pane (Claude-Code-like ordered timeline). Additive `context.*` events on the existing 4-channel PaneEventBus (ADR-030 — no 5th channel). **DEF-001 closeout (2026-06-29)**: BFF wiring of the trace bridge via `IContextSseRelay` per-request scoped service — singleton emitter resolves the relay through `IHttpContextAccessor` and SSE writes are gated by a SemaphoreSlim.

**Residual gap**: ISS-002 (#475) — chat-handler write-side and playbook-output write-side have NOT been unified; chat handlers don't emit SSE events on tab mutation (commented as ADR-030 compliance, but ADR-030 doesn't forbid additive events on existing channels). Filed to successor; chat-routing-redesign-r1 owns.

---

## 2. R5 carry-forward patterns — evolved status

| R5 pattern | R6 status | Note |
|---|---|---|
| **Cross-package File-ref forwarding** (optional fields for future consumers) | **RETAINED** | Used verbatim for Pillar 6a `WorkspaceTab` shape + Pillar 9 `SerializedWidgetState`. |
| **FileList is live** (snapshot before clearing) | **RETAINED** | Q8 conflict-resolution applies same snapshot semantics to workspace tabs (snapshot at agent prompt assembly time). |
| **Wire-stream contract assertions** (parser ↔ widget boundary tests) | **EVOLVED** | R6 added `Pillar8ToPlaybookEngineTests.cs` (13 tests) at the Pillar 8 → Pillar 4 → Pillar 5 → Pillar 6c integration boundary. Q6 closed-vocabulary integrity now a binding contract. |
| **Diagnostic logs as one-shot probes** (commit in bug PR, remove in closeout PR) | **RETAINED** | Used during Pillar 2 typed-handler migration; all probe logs removed before merge per R5 process learning. |
| **`SprkChat.onBeforeSendMessage` informational-only** (can't suppress; accept duplicate-fire) | **SUPERSEDED by Pillar 5** | R5 lesson said accept the duplicate-fire; R6 closed it structurally at CapabilityRouter (single intent → single render). Pattern retired. |
| **Two-wrapper widget architecture** (PaneEventBus event → tab install; widget renders independently) | **RETAINED** | Pillar 6a/6c reused the pattern verbatim. Calendar widget Pattern D (per `BUILD-A-NEW-WORKSPACE-WIDGET.md`) is the canonical R6 example. |
| **Path A vs Path B parallelism is a smell** (one intent → one path) | **EVOLVED into design principle** | R6 made this STRUCTURALLY enforceable via Pillar 5 (`destination` + `widgetType` on node config). R7+ should treat parallel paths as a refactor signal, not a feature. |

---

## 3. New R6 patterns to carry forward

These are R6-original patterns worth promoting into R7's carry-forward list.

1. **Command-router architecture (Pillar 8)** — `CommandRouter` builds `Intent { command, references[], rawText }` BEFORE agent invocation. Hard slashes are deterministic dispatch (<100ms, no LLM round-trip); soft slashes decorate the chat body with a `commandIntent` hint that the LLM prioritizes. References (`#scope` / `@<entity>` / `#<filename>`) resolve at parse time and inject resolved entities into the agent prompt. Pattern: **parser → intent → optional decoration → existing agent path** preserves NFR-11 backward compat for natural-language users while giving power users keyboard shortcuts.
2. **Data-driven persona resolution via scope library (Pillar 1)** — most-specific-wins (SYS- < CUST- < playbook-attached) traversal applied to the existing 4-scope schema. `sprk_aipersona` mirrors the same pattern, so the resolution mental model transfers. Pattern: **5th scope type, same resolution algorithm**.
3. **Schema-aware widget rendering with outputSchema-on-action (Pillar 5)** — action declares intrinsic shape; node config declares routing destination; widget reads schema and renders accordingly. Pattern: **shape lives with what produces it (action); routing lives with where it goes (node); widget reads both at render time**. This is the structural fix for R5's TL;DR + Entities bugs.
4. **Tri-directional workspace state with per-turn snapshot in agent prompt (Pillar 6a)** — agent's system prompt includes a snapshot of Assistant-visible workspace tabs. Tabs are typed (`WorkspaceTab` interface), persisted (Redis hot + Cosmos warm on pin), and queryable from chat tools. Pattern: **agent can read its environment; user controls what's visible via "Add to Assistant" toggle (privacy default = invisible)**.
5. **Pinned-memory CRUD UI in Context pane (Pillar 7 — Q7 expansion in R6)** — what was R7-deferred in spec became R6 scope expansion. UI sits in the Context pane (not the workspace), respects the same 4-stage shell lifecycle (ADR-031), uses additive events on the existing context channel (ADR-030). Pattern: **memory UX is context, not workspace; expansion fit predicted Phase C +1–2 weeks and that's exactly what it took**.
6. **Generic invoke_playbook facade replacing specialized bridges (Pillar 3)** — `IInvokePlaybookAi` in `Services/Ai/PublicContracts/` per ADR-013. `invoke_playbook(playbookId, parameters)` with dynamic playbook list in tool description. Specialized bridges (`InvokeSummarizePlaybookTool`, `InvokeInsightsQueryTool`) deleted. Pattern: **one generic tool with data-driven dispatch beats N specialized tools that drift over time**.
7. **Widget visibility contract with getAgentVisibleState (Pillar 9)** — opt-in per widget; privacy by default. Summary returns `{ widgetType, summary, tldr, hasUserEdits }`; DocumentViewer returns `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }`. Pattern: **agents have read-access to widget state only when widget AND user both opt in**.
8. **Per-request streaming side-channel via scoped IContextSseRelay (DEF-001 / ADR-033 precedent)** — singleton emitter (any layer) writes to a per-request scoped relay; relay's `Writer` is assigned at SSE stream start by the endpoint and cleared in finally. SemaphoreSlim serializes concurrent writes so SSE frames never interleave. Pattern: **decouple "what to emit" (singleton, knows the event shape) from "where to emit it" (scoped, knows the response object)**. Reusable for any BFF stream-with-side-channel scenario.

---

## 4. Process learnings

These are workflow + judgment-level lessons; some confirm R5's lessons, some are new.

- **Q9 batch tool migration outcome**: Initial design fear was "10 tools at once is too risky." Actual outcome: 3 sub-waves grouped by migration shape (trivial / citations-aware / streaming) landed in 4 days with comprehensive regression gate. NFR-08 invariant (`git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` empty) caught one accidental scope creep. **Lesson**: batch migrations with a regression gate are cheaper than incremental migrations with N×latency per merge cycle.
- **Q7 Memory UI scope expansion fit**: Predicted Phase C +1–2 weeks; actual was +1.5 weeks. Scope-expansion-after-spec is sustainable IFF the expansion has its own pillar (here: Pillar 7 already existed for memory infrastructure; Q7 just extended its surface).
- **Q5 re-shaping mid-spec**: Initial spec had `outputType` everywhere; Q5 reshape moved it to action / node split. Doing this BEFORE Pillar 5 implementation started saved a re-migration. **Lesson**: spec deltas in the first quarter of a phase are cheaper than late-phase pivots — surface them at kickoff Q-review.
- **Pillar 6 split-by-6a/6b/6c effectiveness**: 6a-first gating prevented contract drift between 6b (chat tools needing tab state) and 6c (trace widget needing event types). Both could parallelize after 6a landed. **Lesson**: when a pillar has multiple parallel-safe sub-surfaces, ship the contract surface first as its own task.
- **BFF publish-size NFR-02 cumulative tracking via TASK-INDEX**: TASK-INDEX rows carry per-task delta + cumulative. R6 closed at +0.41 MB cumulative (well under +5 MB ceiling). **Lesson**: tracking the delta per-task makes NFR-02 self-enforcing — no separate audit needed.
- **Confirmation triggers for ADR/DI/Dataverse-schema changes**: User-specific execution-style preference (autonomous parallel-agent default; explicit confirmation for ADR/contract/schema/build-fail). Used multiple times during R6; prevented at least 3 cases of silently working around an ADR (per CLAUDE.md "ADRs Are Defaults" — example: Wave 9 Q9 streaming ADR-033 case). **Lesson**: this should be the default workflow for any project touching shared architecture.
- **DEF / defer-issue two-write rule**: `/project-defer-issue-tracking` enforces project-notes + GitHub-issue pairing. At closeout, 4 of 8 defer entries (DEF-001/002/003/004) were "expected to be done" — the rule made them visible enough to pull back. **Lesson**: deferred work hidden in `notes/` becomes invisible; the two-write rule fixes this. Adopt for every project.
- **Worktree-to-master sync hygiene**: Branch lived 22 days; merged 122 master commits at closeout with ZERO conflicts. Reason: R6's changes were mostly additive (new files, new endpoints, additive event types) — not modification-heavy in shared hot paths. **Lesson**: additive changes parallelize naturally; rewriting shared hot paths (like CapabilityRouter retirement in successor PR #509) creates merge churn. Sequence accordingly.
- **Successor coordination**: chat-routing-redesign-r1 retired CapabilityRouter mid-R6. The successor's FR-23 tool-filtering replacement preserved the Layer 0.5 prioritization invariant — R6's Phase D exit-gate signs off on the UX contract, not the specific implementation. **Lesson**: spec exit criteria should describe contracts, not implementations, so successor projects can refactor without invalidating prior phases.

---

## 5. What did NOT work / would do differently

- **Original task 091 (Builder UI persona dropdown) descoped at closeout**: BFF wiring (DEF-002) was the critical fix; the maker-portal main form already exposes `sprk_playbookpersona`. The dedicated Builder UI dropdown was nice-to-have, not need-to-have. **Would do differently**: when a maker-facing field has a Power Apps form fallback, treat the in-Builder UI as a future enhancement, not a closeout blocker.
- **TIER-C primary fix was Dataverse data, not C# code**: 2 of 4 workspace handler rows were missing from `Seed-TypedHandlers.ps1`. C# code was wired correctly. Diagnostic took 2 hours when the seed-script audit was the right starting point. **Would do differently**: when an endpoint's plumbing is asserted via tests, but UAT fails, audit seed scripts first.
- **DEF tracking momentum was uneven**: Some defer entries collected for 2+ weeks before being filed as GitHub issues; one entry (ISS-001 / #470) is still upstream-owner ambiguous. **Would do differently**: the `/project-defer-issue-tracking` skill should auto-bump entries left "Open" >7 days, prompting "still open? close or work on it?"
- **Lessons-learned was authored at the very end**: R5 + R6 both authored lessons-learned in the wrap-up task. By that point, much fine-grained context has compacted out. **Would do differently**: maintain a running `notes/insights-log.md` from project kickoff, captured as Q-decisions are made — wrap-up just synthesizes the log.

---

## 6. R6 metrics

| Metric | Value | Source |
|---|---|---|
| Tasks shipped | 82 ✅ + 1 ❌ withdrawn | TASK-INDEX.md status sync 2026-06-29 |
| Calendar | 6.5 weeks (kickoff 2026-06-07 → close 2026-06-29) | Q7 expansion added ~1.5 weeks as predicted |
| Phases | A (Pillars 1-4) → B (Pillar 5) → C (Pillars 6/7/9) → D (Pillar 8 + integration) | spec §Phase Structure |
| Cumulative BFF publish-size delta | +0.41 MB (compressed); 46.71 MB total at closeout | NFR-02 ≤+5 MB; ADR-029 ≤60 MB hard |
| PRs merged | #375, #395, #401, #488, plus master fast-forward `ecb650e44` for DEF closeout | All on `work/spaarke-ai-platform-unification-r6` |
| GitHub issues opened during closeout | 6 defer/issue + 1 idea = 7 (#470, #471, #472, #473, #474, #475, #476, #510, #511) | `/project-defer-issue-tracking` x 7 + `/devops-idea-create` x 1 |
| Vertical-slice integration test | Pillar8ToPlaybookEngineTests.cs — 13 tests passed in 23ms | task 087 evidence |
| Q-decisions resolved at kickoff | 11 (Q1–Q11) | plan.md §2 |
| NFR-08 violations | 0 (verified: `git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` empty across R6) | task 087 evidence |

---

## 7. References

| Document | Purpose |
|---|---|
| [spec.md](../spec.md) | Original 59 FRs + 16 NFRs across 9 pillars |
| [plan.md](../plan.md) | Implementation WBS, Q-decisions, phase exit criteria |
| [README.md](../README.md) | Project overview + graduation criteria |
| [CLAUDE.md](../CLAUDE.md) | Binding context (NFRs, Q-decisions, ADRs Are Defaults principle) |
| [r7-backlog.md](r7-backlog.md) | Explicit deferred-work list with GitHub pointers |
| [phase-d-exit-checklist.md](phase-d-exit-checklist.md) | Phase D sign-off evidence |
| [defer-issues.md](defer-issues.md) | All defer + issue entries (8 GitHub issues paired) |
| [r6-deliverables-audit.md](r6-deliverables-audit.md) | Source of truth for "shipped vs surfaced vs working" per pillar |
| [vertical-slice-evidence.md](vertical-slice-evidence.md) | task 087 9-pillar evidence map |
| [R5 lessons-learned](../../spaarke-ai-platform-unification-r5/notes/lessons-learned.md) | R6's predecessor + mandatory carry-forward source |

---

*End of R6 lessons-learned. R7+ feature work: load this file as carry-forward; default to playbook-data-driven design; treat parallel routing paths as refactor signals; preserve NFR-11 backward compat for natural-language users.*
