# R7 Defer / Issue Tracking

> **Purpose**: track deferred work + newly-discovered issues per CLAUDE.md §13 + project tracking conventions.
> **Filing protocol**: invoke `/project-defer-issue-tracking` (alias `/defer`) — writes to BOTH this file AND a GitHub Issue. Skipping the GH Issue side breaks visibility.
> **Last Updated**: 2026-06-29

---

## DEF-001 — Wire AiAnalysisNodeExecutor to Wave 11 Option B inputBinding pattern

| Field | Value |
|---|---|
| **Type** | DEF |
| **Filed** | 2026-06-29 |
| **Filed by** | Wave 11 T111 implementation (Option B scoping) |
| **GitHub Issue** | (to be filed via /project-defer-issue-tracking on next push) |
| **Severity** | MEDIUM — non-blocking for R7 ship (no AiAnalysis consumer in scope for R7 UAT), but architectural completeness gap |
| **Effort estimate** | 1-2 hours |

### Description

T111 wires `AiCompletionNodeExecutor` to extract resolved `inputBinding` from configJson and pass to `PromptSchemaRenderer.Render` as `runtimeInput` (Wave 11 Option B Layer 2). For symmetry, `AiAnalysisNodeExecutor` should get the same wiring — every AI executor in the BFF should consume the same Option B pattern.

### Why deferred from T111

`AiAnalysisNodeExecutor` does NOT call `PromptSchemaRenderer.Render` directly. It builds a `ToolExecutionContext` that is passed to tool handlers (`GenericAnalysisHandler`, `SummaryHandler`, `DocumentClassifierHandler`, `SemanticSearchToolHandler`). Each tool handler calls `_promptSchemaRenderer.Render`. Wiring inputBinding through that chain touches ~6 files (vs 1 for AiCompletion).

The deployed `DAILY-BRIEFING-NARRATE` playbook (Wave 11 UAT target) uses only `AiCompletion` nodes — no `AiAnalysis`. R7 UAT closure does NOT require AiAnalysis wiring. Scoping T111 to AiCompletion only keeps the PR focused + reviewable.

### Scope of the deferred work

1. Add `JsonElement? InputBinding` property to `ToolExecutionContext`
2. Add `ExtractInputBindingAsJsonElement` helper to `AiAnalysisNodeExecutor` (mirror of T111's AiCompletion helper)
3. Set `InputBinding` in `AiAnalysisNodeExecutor.CreateToolExecutionContextAsync`
4. Update 4 tool handlers to pass `runtimeInput: ctx.InputBinding` to `_promptSchemaRenderer.Render`:
   - `GenericAnalysisHandler.cs` (2 call sites)
   - `SummaryHandler.cs`
   - `DocumentClassifierHandler.cs`
   - `SemanticSearchToolHandler.cs`
5. Tests: extend `AiAnalysisNodeExecutorTests` with inputBinding extraction + handler integration tests

### When to address

- After Wave 11 ships (don't extend T111 scope)
- Before any AiAnalysis-based playbook consumer is authored that needs structured input
- Naturally fits as a small follow-up wave after R7 closes, OR as part of an R8 cleanup wave

### How to apply

Architecture pattern already documented (T111a). Implementation is mechanical. Estimate: 1-2 hours including tests.

---

## DEF-002 — SpaarkeAi assistant chat hostContext refresh on workspace tab change (audit 120 Gap D)

| Field | Value |
|---|---|
| **Type** | DEF |
| **Filed** | 2026-06-30 |
| **Filed by** | Wave 12 T153 audit 120 disposition (Gap D explicit defer per audit §5) |
| **GitHub Issue** | (to be filed via /project-defer-issue-tracking on next push) |
| **Severity** | LOW — non-blocking for MVP; URL-launched matter context (the documented Wave 12 AC13/AC14 success criterion) IS unblocked by T150+T151+T152 |
| **Effort estimate** | 1-2 days |

### Concrete behaviour that fails without this work

When an operator opens SpaarkeAi from matter X (URL-launched, entityType=sprk_matter&entityId=<X>), then switches to a workspace pane showing project Y's documents, the chat continues to think it's working on matter X. The PaneEventBus emits `active_widget_changed` events that ConversationPane could subscribe to and refresh hostContext from, but does not today.

Specifically: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx:853-871` subscribes only to `tab_change` (line 854) and `selection_changed` (line 859) — no `active_widget_changed` handler. `ThreePaneShell.tsx:580-591` builds `entityContext` via `React.useMemo` on `[entityLogicalName, entityId, matterId]`, which come from URL props that don't change post-mount.

### Why deferred from T153

Audit 120 §5 disposition table marks this gap "DEFER post-MVP" with rationale: operator has not surfaced this specific UX expectation in Wave 12 AC13/AC14 wording. The documented Wave 12 success path is URL-launched matter-context (Scenario A in audit 120 §4), which T150 + T151 + T152 fully unblocks. Tab-follow-chat semantics are a separate UX question that should be operator-decided before any implementation.

### Scope of the deferred work

1. Decide UX semantics: chat-follows-active-tab vs chat-stays-on-launch-context (operator decision)
2. If chat-follows-tab: subscribe ConversationPane to `active_widget_changed` events from the pane bus
3. Plumb a setter into AiSessionProvider for entityContext
4. Decide session-recreate vs in-place update semantics (each has UX trade-offs around lost conversation history)
5. Add e2e Playwright tests covering tab-switch behaviour

### When to address

- After R7 MVP UAT completes (T136+T154 deploy + W12.5 close-out)
- When operator specifies the desired tab-switch UX behaviour
- Likely in a follow-up "assistant-workspace-context-sync" project (1 sprint)

---

## R7-NOACTION-001 — Audit 120 Gaps E/F/G/H disposition (no code action; documented)

The remaining 4 gaps from audit 120 are explicitly NOT code tasks per the audit's own disposition table. Recording here so the gap inventory is closed:

- **Gap E** "No current-matter tool surfaced to LLM" — subsumed by Gaps A+B+C (T150+T151+T152). System-prompt enrichment now answers "what matter am I in?" deterministically. NO TOOL NEEDED.
- **Gap F** "Verify matter documents indexed in spaarkedev1" — UAT verification step, not a code task. Owned by T136+T154 deploy + UAT smoke. If post-deploy smoke shows 0 hits on `"what documents are in this matter?"`, file a follow-up ISS-NNN at that point.
- **Gap G** "LoadKnowledgeNodeExecutor is NOT the chat retrieval path" — documentation-only clarification. Already captured in audit 120 §3 Gap G. Wave 12 plan §2.3 + W11 architecture doc §5 already document the two retrieval paths.
- **Gap H** "Deployment coordination across spaarkedev1" — operator decision tracked in wave12-mvp-completion-plan.md §10 Q3. Not a code task; deployment scripting (T136+T154) owns.

No GitHub Issues filed for these because they are not deferred work — they are explicit no-action dispositions from the audit itself. Recording the disposition rationale in this file is sufficient closure per audit 120 §5.
