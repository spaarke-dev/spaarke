# R5 chat-agent parallel-build audit (response to Insights team heads-up)

> **Date**: 2026-06-04
> **Trigger**: Insights Engine r3 team flagged that r2 Wave E2 built `InsightsIntentClassifier` as a parallel intent classifier without leveraging existing `PlaybookDispatcher` + `playbook-embeddings`. They asked R5 to audit our own chat-agent layer for similar parallel-build oversights.
> **R5 state at audit**: 9 of 22 Phase 2 tasks complete (010, 011, 012, 013, 016, 017, 018, 019, 022) + all of Phase 1.

---

## Audit scope

Insights team's five suggested audit targets:

1. **`PlaybookDispatcher`** — two-stage vector + LLM matching for "what playbook applies?"
2. **`DynamicCommandResolver`** — slash command resolution from Dataverse playbook trigger phrases
3. **`SprkChatAgent` / `SprkChatAgentFactory`** — chat-agent tool registry
4. **`PlaybookChatContextProvider`** — playbook context loading
5. **`CompoundIntentDetector`** — plan-preview gating before write-back actions

---

## Findings per R5 task

### Task 010 + 011: Dataverse seed deploys

- **Reuses**: `scripts/Deploy-Playbook.ps1` (canonical) + new sibling `scripts/Deploy-AnalysisAction.ps1`. ✅ no parallel build
- **GAP**: The newly-deployed `summarize-document-for-chat@v1` playbook is **NOT yet indexed in `playbook-embeddings`**. `PlaybookIndexingService` is signal-driven (bounded Channel; only indexes when explicitly enqueued via a trigger endpoint). Our deploy did not enqueue it.
- **Why this is a gap**: when the future R5 chat-pane integration (or any other chat surface) needs intent classification for natural-language Summarize requests, `PlaybookDispatcher` Stage 1 vector search will MISS the new playbook because it's not indexed.
- **Recommendation**: Add a one-time indexing step. Options:
  - (a) Find the existing trigger endpoint that enqueues into `PlaybookIndexingService._indexingChannel`; POST it with the new playbook ID after deploy.
  - (b) Add a `-IndexAfterDeploy` switch to `Deploy-Playbook.ps1` that calls the trigger endpoint.
  - (c) Defer to R6 — the Insights r3 reconciliation work item F-4 explicitly covers "Index `predict-matter-cost@v1` (and any future Insights playbooks) in `playbook-embeddings`". R5's `summarize-document-for-chat@v1` falls into the same backlog.
- **Decision (for now)**: defer to R6 unless task 015's smoke test surfaces a routing miss. Note in coordination doc for Insights r3 to include `summarize-document-for-chat@v1` in their F-4 backfill.

### Task 012: SessionSummarizeOrchestrator

- **Reuses**: `RagService` (with sessionId filter), `OpenAiClient.StreamStructuredCompletionAsync`, `IncrementalJsonParser`, `ChatSessionManager`, `AnalysisChunk` factories, `R5SummarizeTelemetry`. ✅
- **Does NOT use `PlaybookDispatcher`**: correct by design. `PlaybookDispatcher` is for INTENT CLASSIFICATION (which playbook does this user message want?). The orchestrator is invoked with explicit intent — caller already chose `SUM-CHAT@v1`. ✅ not a parallel build
- **Does NOT use `SprkChatAgent`**: correct by design. SprkChatAgent is the ABOVE layer that calls into orchestrators. Task 015 (agent-tool registration) is where SprkChatAgent integration happens.
- **Justification documented in code**: per task 012 evidence note + class XML doc, the orchestrator is justified as a NEW kind of orchestration (chat-session-scoped) that the existing `AnalysisOrchestrationService` (document-record-scoped) does not cover. ✅

### Task 015 (NOT YET DONE): InvokeSummarizePlaybookTool

- **Plan per POML**: register via existing `SprkChatAgentFactory` tool-registration mechanism (the canonical `ResolveTools` block ~lines 540-1040 in `SprkChatAgentFactory.cs`). ✅ aligned
- **Guardrail for sub-agent brief**: explicitly confirm the agent does NOT introduce a parallel tool registry, does NOT bypass `SprkChatAgentFactory`, and registers the tool inside the existing `AnalysisExecutionTools` block (lines ~807-834 per the task 015 POML sub-agent's earlier finding).

### Task 019 (DONE): slash command `/summarize` semantic extension

- **Implementation**: added `/summarize` to hardcoded `DEFAULT_SLASH_COMMANDS` in `slashCommandMenu.types.ts` (frontend) + tri-mode router (`routeSummarizeIntent`) in `ConversationPane.tsx`.
- **`DynamicCommandResolver` reuse check**: at first glance this looked like potential duplication (DynamicCommandResolver dynamically resolves slash commands from `sprk_triggerphrases`). HOWEVER:
  - `DynamicCommandResolver` reads `sprk_triggerphrases` — a field that **DOES NOT EXIST** on the current `sprk_analysisplaybook` entity (verified via metadata query; entity has `sprk_triggertype`, `sprk_triggerconfigjson`, `sprk_triggertypename` instead).
  - Query for playbooks with non-null `sprk_triggerconfigjson` returns **0 rows** on Spaarke Dev 1 — the dynamic-resolution mechanism is essentially dormant.
  - Verdict: **R5's hardcoded approach is correct given current Dataverse state.** When the trigger-phrases mechanism stabilizes (Insights r3 work item F-5: "JPS authoring flow: auto-populate description + triggerPhrases at playbook creation; trigger re-indexing"), R5's `/summarize` registration should migrate to a playbook trigger config. Logged as R6 follow-up.
- **`PlaybookDispatcher` reuse check**: not applicable. `PlaybookDispatcher` is invoked AFTER intent is unclear (natural-language messages); `/summarize` is explicit slash command — intent already known. The tri-mode router decides VARIANT (uploaded files vs active doc vs prompt-first), not playbook identity. ✅ not a parallel build

### Task 020 (NOT YET DONE): chat-pane orchestration UX

- **Guardrail for sub-agent brief**: confirm task 020 sub-agent considers whether `CompoundIntentDetector` should gate any plan-preview step. R5 Summarize is **read-only** (no write-back to Dataverse / SharePoint), so plan-preview gating is probably N/A. But sub-agent should document the decision (use vs N/A) explicitly.
- Per Insights team heads-up: task 020 should also consider `PlaybookChatContextProvider` if it loads any playbook-context-dependent UI. Since R5 Summarize is explicit-intent (not playbook-context-driven), this is probably N/A — but worth flagging.

### Tasks 024-029 (NOT YET DONE): Insights tool integration

- These ALL go through `SprkChatAgentFactory.ResolveTools` per their POMLs. ✅ aligned. No audit findings.

---

## Summary: actual parallel-build risk in R5

**No parallel-build oversights found in R5's shipped work** (tasks 010-022 currently committed).

**Two course-corrections for upcoming tasks**:

1. **Task 015 sub-agent brief**: include explicit guardrail that `InvokeSummarizePlaybookTool` registers inside the existing `SprkChatAgentFactory.ResolveTools` block (not a parallel tool registry).
2. **Task 020 sub-agent brief**: include explicit ask to evaluate `CompoundIntentDetector` + `PlaybookChatContextProvider` reuse; document decisions even if N/A.

**One forwarded item to Insights r3 / R6**:

3. **`playbook-embeddings` indexing for `summarize-document-for-chat@v1`**: include in Insights r3 F-4 backfill scope. Coordination via `notes/insights-r2-coordination.md` §8 update (next).

---

## ADR / pattern compliance summary

| ADR | R5 compliance | Notes |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | Zero new top-level `Program.cs` lines across all R5 commits |
| ADR-013 (BFF-only AI orchestration) | ✅ | All R5 orchestration lives in `Sprk.Bff.Api/Services/Ai/` |
| ADR-014 (tenant+session isolation) | ✅ | Enforced at index schema (task 001) + RagService (task 002) + IndexingPipeline (task 003) + cleanup job (task 007) + orchestrator (task 012) |
| ADR-018 (Feature Flag Scope Discipline) | ✅ | Zero new feature flags introduced by R5 |
| ADR-028 (Spaarke Auth v2) | ✅ | No token snapshots; fresh tokens per call |
| ADR-029 (BFF publish hygiene) | ✅ | Publish-size 45 MB compressed (vs 45.65 MB baseline; well under 60 MB ceiling) |
| ADR-030 (PaneEventBus 4 channels) | ✅ | Task 016 added 5 additive event types within existing 4 channels; zero new channels |

---

## Why this audit was easy to pass

R5 came in with explicit reuse-mandate discipline (CLAUDE.md §3.1) that was enforced at every sub-agent brief. Every task POML cites the existing services to reuse. The Insights r2 finding's general lesson — "downstream consumers don't always discover existing infrastructure" — is exactly the failure mode the §3.1 mandate was designed to prevent.

Where R5 introduced new helpers (e.g., `IncrementalJsonParser`, `SessionSummarizeOrchestrator`), each instance has an explicit "Why-New-Class Justification" in code XML docs + evidence notes, with citations to the existing services that were considered and ruled inappropriate.

---

## Action items

1. ✅ Audit recorded in this file (Done)
2. ⏳ Course-correct task 015 sub-agent brief (when dispatched in Wave C)
3. ⏳ Course-correct task 020 sub-agent brief (when dispatched in Wave D)
4. ⏳ Append entry to `notes/insights-r2-coordination.md` §8 to coordinate F-4 backfill
