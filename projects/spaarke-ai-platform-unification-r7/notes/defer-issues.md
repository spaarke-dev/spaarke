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
