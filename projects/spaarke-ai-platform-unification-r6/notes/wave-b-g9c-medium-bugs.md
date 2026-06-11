# Wave B-G9c (MEDIUM) — Phase B Walkthrough Bug Investigation

**Date**: 2026-06-10
**Bugs**: B6, B7, B8, B9 — 4 medium-severity issues from Phase B UI walkthrough
**Status**: SUCCESS — all 4 addressed (3 SURFACED, 0 fixed unilaterally; B7 has clear fix path but surfaced because it touches an intentional R5 task 038 design)

---

## Bug B6 — Different summaries for the SAME file across runs

### Symptom
User uploaded the same PDF twice and got two materially different summaries.

### Root cause investigation

The chat-summarize pipeline has THREE different LLM entry points on `OpenAiClient`, and they apply DIFFERENT temperature policies:

| Method | Temperature | Used by |
|---|---|---|
| `StreamStructuredCompletionAsync` | **0f (hardcoded)** | `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` — the slash `/summarize` + agent-tool path |
| `GetStructuredCompletionAsync<T>` | **0f (hardcoded)** | Generic structured deserialization (`InsightsIntentClassifier`, etc.) — docstring says "Temperature is set to 0 for deterministic classification/extraction results" |
| `GetStructuredCompletionRawAsync` | **`_options.Temperature` (default 0.3)** | `SummaryHandler`, `SemanticSearchToolHandler`, `DocumentClassifierHandler`, `EntityExtractorHandler`, `ClauseAnalyzerHandler`, `RiskDetectorHandler`, `GenericAnalysisHandler`, `InvoiceExtractionToolHandler`, `InsightsIntentClassifier` |
| `GetCompletionAsync` / `StreamCompletionAsync` | `_options.Temperature` (default 0.3) | Plain (non-structured) prompts |

**Inconsistency**: Among the three structured-output methods, two pin Temperature=0 (correct for deterministic JSON-shaped output) and one uses the configurable default (0.3). This is the root cause of any non-determinism in structured tool handler outputs (e.g., `SummaryHandler`).

### Why the slash `/summarize` path is NOT the symptom source

`PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (line 318-320) calls `StreamStructuredCompletionAsync` which IS pinned to `Temperature = 0f` (OpenAiClient.cs line 816). So the chat-pane `/summarize` slash path is already deterministic. Re-running an identical input through this path should produce byte-identical output (Azure OpenAI guarantees this with structured outputs + Temperature=0).

### Probable symptom source

If the user ran summarize via:
1. **Natural-language path** ("summarize this document") that routed through `SummaryHandler` (which calls `GetStructuredCompletionRawAsync` with Temperature=0.3) — non-determinism is EXPECTED.
2. Agent-tool path through `InvokePlaybookHandler` → orchestration layer → handler — if the handler chain ends in `GetStructuredCompletionRawAsync`, same issue.

### SURFACED question (do not fix unilaterally)

**Question 1**: Should `GetStructuredCompletionRawAsync` be pinned to `Temperature = 0f` like its siblings? The docstring of `GetStructuredCompletionAsync<T>` says "Temperature is set to 0 for deterministic classification/extraction results" — implying the design intent. The Raw variant is structurally identical (constrained decoding via `response_format: json_schema`) so the same rationale applies.

**Trade-off**:
- PRO: Makes the three structured methods consistent. Removes a class of non-determinism bugs across ~8 tool handlers. Matches the documented design intent.
- CON: Tool handlers that DO want non-zero temperature for creative output (e.g., summarization style variation) would need an explicit override parameter. None of the current 8 callsites use the temperature configurability — they pass no `model` and no explicit temperature.

**Minimal fix candidate** (if approved):

```csharp
// OpenAiClient.cs line 717-725: change Temperature = _options.Temperature → Temperature = 0f
// Add docstring sentence: "Temperature is set to 0 for deterministic structured output —
// callers that require non-zero temperature should add an explicit overload."
```

This is a 1-line change but it changes the operational semantics of 8 tool handlers. **Surfacing to main session for risk evaluation.**

**Question 2**: For SUM-CHAT@v1 specifically, do we want a per-action `sprk_temperature` field on `sprk_analysisaction` for future flexibility? Current schema does NOT include one (verified by grepping the `sprk_analysisaction-*.json` seed rows — none carry temperature). Adding one would be a Dataverse schema change (R7 deferral candidate, NOT an R6 task). For now, ALL chat-summarize traffic flows through `StreamStructuredCompletionAsync` (Temp=0) so no per-action override is needed.

### Commit-message fragment (if Q1 approved)

```
fix(r6 G9c-B6): pin GetStructuredCompletionRawAsync to Temperature=0 for deterministic structured output

Aligns with GetStructuredCompletionAsync<T> + StreamStructuredCompletionAsync which already pin Temperature=0
(per docstring: "set to 0 for deterministic classification/extraction results"). Removes non-determinism
across 8 tool handler callsites (SummaryHandler, SemanticSearchToolHandler, DocumentClassifierHandler,
EntityExtractorHandler, ClauseAnalyzerHandler, RiskDetectorHandler, GenericAnalysisHandler,
InvoiceExtractionToolHandler). The chat-pane /summarize slash path was already pinned at Temp=0
(StreamStructuredCompletionAsync) — this fix closes the gap for natural-language + agent-tool paths.
```

---

## Bug B7 — Summary tab appears as DEFAULT tab on workspace (BEFORE any summarize runs)

### Symptom
When user opens the workspace (BEFORE any summarize action has run), the Summary tab is already present AND selected as default.

### Root cause investigation

This is **intentional behavior** introduced by R5 task 038 — see `WorkspacePane.tsx` lines 546-637:

```typescript
// Auto-install Summary tab — R5 task 038 (2026-06-05)
//
// Operator feedback from SC-18 cycle 4: the structured Summarize output
// (TL;DR / Summary / Keywords / Entities) streamed into the chat pane
// instead of into the Workspace pane. Root cause: the existing
// `StructuredOutputStreamWidget` (R5 task 017) subscribes to
// `workspace.streaming_*` events but was never registered as a workspace-
// pane tab in SpaarkeAi.
//
// This effect installs a "Summary" tab as the FIRST (leftmost) workspace
// tab using the new `prependTab` method on `WorkspaceTabManager`. The tab
// hosts the existing widget with `mode: 'streaming'` + `SUMMARIZE_SCHEMA`
// + `correlationId` set to the active chat sessionId so it consumes only
// events for the current session...
```

Specifically:
- Line 599: `const tabId = manager.prependTab(STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE, widgetData, "Summary")`
- Line 614: `manager.setActiveTab(tabId)` — activates Summary IMMEDIATELY

The tab is pre-installed as a **persistent sink** for streaming events. Without it, summarize events with `correlationId === chatSessionId` would have no widget to subscribe.

### Tension with user expectation

The user expects: **Summary tab should only appear AFTER a summarize action executes.**

The R5 design choice: **Summary tab is always present, ready to receive events from any future summarize**.

This is a UX disagreement, not a bug. The R5 design prioritized "no missed events" (event arrives before widget mounts → lost). The user reports the UX is jarring because the tab is empty until used.

### SURFACED question (do not fix unilaterally)

**Question 3**: Two acceptable UX patterns, which is preferred?

**Option A — keep R5 design (no change)**: Summary tab is permanent, shows empty-state placeholder until first stream. Document the empty-state UX as expected; if user dislikes it, update the placeholder copy to "Run /summarize on a file to see results here" so the empty state is informative.

**Option B — defer Summary tab installation to first summarize trigger**: Remove the auto-install effect (lines 582-637 in WorkspacePane.tsx). Add an effect that subscribes to `workspace.streaming_started` and installs the Summary tab on-demand. Risk: event arrives before tab mounts → race. Mitigation: buffer the first event in `PaneEventBus` for replay, OR install the tab synchronously inside the `streaming_started` handler before the next event.

**Recommendation**: Option A with improved empty-state copy is lower-risk. Option B is a deeper refactor and may reintroduce the SC-18 cycle 4 bug (events landed in chat-pane instead of workspace).

**Minimal fix for Option A** (if approved):

The empty state is rendered inside `StructuredOutputStreamWidget` when `phase === 'idle'`. Locate the idle-phase render path and update the copy. (Wave B-G9a owns `StructuredOutputStreamWidget.tsx` — DO NOT TOUCH directly per this task's MUST-NOT-TOUCH list.)

### Commit-message fragment (if Option B chosen)

```
fix(r6 G9c-B7): defer Summary tab auto-install until first summarize stream

Removes the eager auto-install of the Summary workspace tab on WorkspacePane
mount (R5 task 038). The tab is now created on the first `workspace.streaming_started`
event for the active chat session. Eliminates the confusing empty Summary tab
on initial workspace load.

Risk: first streaming event may arrive before the tab mounts. Mitigated by
buffering the first event in PaneEventBus and replaying on tab mount.
```

---

## Bug B8 — Summary tab REPLACED on each summarize run (vs new tab per run)

### Symptom
Running summarize on file A produces a Summary tab. Then running summarize on file B REPLACES the Summary tab content with file B's summary. File A's summary is lost.

### Root cause investigation

This is a direct consequence of the R5 task 038 design (Bug B7 source):

1. **One Summary tab is auto-installed** on `WorkspacePane` mount (lines 599-614). Its `widgetData.correlationId` is set to `chatSessionId` (line 595).
2. **All chat-pane summarize streams use `streamId === chatSessionId`** (`ConversationPane.tsx` line 1134, `executeSummarizeIntent` line 203).
3. The single Summary tab receives **every** stream and updates its widgetData in place via the StructuredOutputStreamWidget's reducer.

**Per-file "Summarize this only"** (via FilePreviewContextWidget's `dispatchSummarizeOnly`, lines 600-660):
- Dispatches `widget_load` with a **unique `correlationId = crypto.randomUUID()`** (FilePreviewContextWidget.tsx line 611-612).
- WorkspacePane's `widget_load` subscriber calls `manager.addTab(...)` which CREATES A NEW TAB.
- BUT no HTTP call is made by this widget — the user must trigger the actual `/summarize` separately, AND the server SSE bridge tags events with `streamId === chatSessionId`, NOT the unique correlationId.
- Result: the new per-file tab never receives its events; they flow to the always-installed Summary tab instead.

**So the observed symptom matches the auto-installed Summary tab**: each new summarize run overwrites the prior result.

### UX decision required

**Question 4**: What is the desired behavior?

**Option A — Replace-in-place (current)**: One Summary tab; each run overwrites prior content. PRO: simple, no tab clutter. CON: loses history; user can't compare summaries side-by-side. This is the R5 implemented behavior.

**Option B — New tab per file/run**: Each summarize invocation creates its own tab labeled "Summary: {fileName}" or "Summary 1, 2, 3…". PRO: history preserved, side-by-side compare. CON: tab clutter; needs cap (MAX_WORKSPACE_TABS = 8 already FIFO-evicts).

**Option C — Hybrid**: Pin one "Live Summary" tab for streaming (current behavior), and ALSO snapshot completed runs to "Summary: {fileName}" tabs on `streaming_complete`. PRO: best of both. CON: more state, more code.

**Note on `allowMultiple: true`**: The widget IS registered with `allowMultiple: true` (`register-structured-output-stream-widget.ts` line 52, comment "FR-06 requires that each Summarize invocation opens its own workspace tab so the user can compare outputs side-by-side"). So Option B was the ORIGINAL design intent per FR-06. R5 task 038 then collapsed back to a single auto-installed tab because of the SC-18 cycle 4 bug (events landing in chat pane). There's a documented architectural conflict here.

### SURFACED to main session — UX policy decision required

I am explicitly NOT picking a UX policy. The task POML for this wave directs that B8 must be surfaced ("Whether to make this a per-file tab (Summary: <fileA name>, Summary: <fileB name>) or a per-run tab (Summary 1, Summary 2). Either is a small change; the question is product-UX. Surface this question to main session; don't decide unilaterally").

**Implementation skeletons** (for whichever option is chosen):

**Option B implementation** (in `WorkspacePane.tsx`):
- Remove the auto-install effect (lines 582-637).
- Subscribe to `workspace.streaming_started`. When received, if no current "active" Summary tab for the streamId, `prependTab` a NEW tab labeled `Summary: {fileName ?? "Document"}` with `correlationId = event.streamId`. Otherwise no-op (event flows to existing tab).
- This requires the ConversationPane to emit unique streamIds per summarize run instead of reusing `chatSessionId`. That's a bigger refactor — surfaces the tradeoff.

**Option C implementation**:
- Keep the auto-install (existing behavior).
- On `streaming_complete`, ALSO snapshot the final widgetData and `addTab` a "Summary: {fileName}" tab with `mode: 'static'`.

### Commit-message fragment (if Option B chosen)

```
fix(r6 G9c-B8): each summarize run opens its own Summary tab (FR-06 restoration)

Restores the FR-06 design intent (per-run Summary tab) that was collapsed in R5 task 038.
ConversationPane now generates a unique streamId per summarize run; WorkspacePane installs
a new "Summary: {fileName}" tab on `streaming_started` instead of reusing the auto-installed tab.
History preserved; user can compare summaries side-by-side. MAX_WORKSPACE_TABS = 8 FIFO eviction
remains in force.
```

---

## Bug B9 — `/summarize` slash vs natural-language give different output detail

### Symptom
Using `/summarize` slash command produces a summary with less detail than typing "summarize this document" in natural language. Same underlying playbook, but different context-injection paths produce different LLM output.

### Root cause investigation

The two paths use **DIFFERENT code, different prompts, and DIFFERENT temperature**:

#### Slash command `/summarize`
- Frontend: `ConversationPane.executeSummarizeIntent` POSTs to `/api/ai/chat/sessions/{id}/summarize`.
- Endpoint: `SummarizeSessionEndpoint` → `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync`.
- Engine: `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (PlaybookExecutionEngine.cs line 185).
- LLM call: `_openAiClient.StreamStructuredCompletionAsync(messages, jsonSchema, "DocumentSummary", ...)` (line 318-320).
- **Temperature**: 0f (hardcoded, OpenAiClient.cs line 816).
- **Prompt structure**: System prompt = `actionConfig.SystemPrompt` (from sprk_analysisaction.sprk_systemprompt for SUM-CHAT@v1). User content = `BuildUserContent(uploadedFiles, fileIds, ragResponse, styleHint)` — RAG-derived chunks.
- **Output**: Structured Outputs (Azure OpenAI strict mode), streamed token-by-token, parsed by `IncrementalJsonParser`.

#### Natural-language "summarize this document"
- Frontend: ConversationPane handles like any chat turn — sends message via the chat endpoint.
- The agent (SprkChatAgent) decides to call `invoke_playbook(playbookId, parameters)` via `InvokePlaybookHandler`.
- Handler: `InvokePlaybookHandler.ExecuteChatAsync` → `InvokePlaybookAi.InvokePlaybookAsync` → `IPlaybookOrchestrationService.ExecuteAsync` → orchestrator graph → `AnalysisOrchestrationService` → tool handler chain.
- LLM call: depending on the playbook node executor:
  - For `AiAnalysisNodeExecutor` → uses `NodeExecutionContext.Temperature` (default **0.3**, NodeExecutionContext.cs line 108).
  - For `SummaryHandler` (if dispatched) → calls `_openAiClient.GetStructuredCompletionRawAsync` which uses `_options.Temperature` (default **0.3**).
- **Prompt structure**: PromptSchemaRenderer-rendered prompt INCLUDING template parameters like `format`, `includeSections`, `usePlainLanguage`, `highlightTimeSensitive`, `formatInstructions` (SummaryHandler.cs lines 340-346). This is a RICHER prompt than the chat-summarize action seed's system prompt.

### Differences summary

| Aspect | `/summarize` slash | Natural-language |
|---|---|---|
| Temperature | 0f | 0.3 |
| Prompt | SUM-CHAT@v1 sprk_systemprompt (concise JPS) | PromptSchemaRenderer with template params (more detail-oriented) |
| Schema | DocumentSummary (tldr/summary/keywords/entities, 4 fields) | Per-handler schema (varies) |
| Streaming | Yes (token-by-token, FieldDelta events) | No (whole-response delivery) |

**Why the natural-language output is more detailed**: (a) the prompt explicitly asks for `includeSections = ["executive_summary", "key_terms", "obligations", "notable_provisions"]` by default; (b) Temperature=0.3 produces longer + more variable output than 0; (c) the schema accommodates the richer structure.

### SURFACED question (do not fix unilaterally)

**Question 5**: Is the divergence intentional, or should the two paths be normalized?

**Option A — Document the difference as intentional**: Slash command = terse, deterministic, streaming-aware. Natural-language = richer, contextual, allows the LLM to adapt the response. Update user-facing docs to clarify the distinction.

**Option B — Normalize the agent-tool path to use the same playbook as the slash**: The agent-tool path SHOULD ultimately call `IPlaybookExecutionEngine.ExecuteChatSummarizeAsync` (matching the slash path) when the LLM picks the chat-summarize playbook ID. This requires:
- Identify where `InvokePlaybookAi.InvokePlaybookAsync` dispatches the chat-summarize playbook GUID.
- If it currently flows through the generic orchestration → tool handler chain (with SummaryHandler / GenericAnalysisHandler / etc.), redirect chat-summarize to `ExecuteChatSummarizeAsync` directly.
- The SprkChatAgentFactory.cs comment lines 1023-1029 says: "Both end at the same engine methods. The session-files Azure Search filter, Structured Outputs streaming, and per-file highlights are preserved unchanged inside the engine." But the engine method is `ExecuteAsync`, not `ExecuteChatSummarizeAsync` — so they likely DON'T converge for chat-summarize specifically. This is a documentation drift.

**Recommendation**: Surface the documentation drift to the main session. The R6 task 023 (Pillar 3 cleanup) comment in SprkChatAgentFactory may have over-stated the convergence. A proper fix requires confirming whether `IPlaybookOrchestrationService.ExecuteAsync` for the chat-summarize playbook GUID actually routes to `ExecuteChatSummarizeAsync` internally, or whether it goes through the generic node-executor path. This is non-trivial investigation — likely 30-60 min itself.

### Commit-message fragment (if Option B chosen)

```
fix(r6 G9c-B9): normalize natural-language /summarize routing through chat-summarize engine

When SprkChatAgent invokes invoke_playbook with the chat-summarize playbook GUID
(SUM-CHAT@v1's playbook), route through IPlaybookExecutionEngine.ExecuteChatSummarizeAsync
(matching the slash /summarize path) instead of the generic IPlaybookOrchestrationService.ExecuteAsync
path. Eliminates Temperature divergence (0.3 → 0) and prompt divergence (PromptSchemaRenderer
templates → SUM-CHAT@v1 sprk_systemprompt) between slash + NL paths.
```

---

## Summary table

| Bug | Root cause | Status | Surfacing reason |
|---|---|---|---|
| **B6** | `GetStructuredCompletionRawAsync` uses Temperature=0.3 while sibling methods pin Temperature=0. | SURFACED | 1-line fix changes operational semantics of 8 tool handlers — risk evaluation needed. |
| **B7** | Auto-install Summary tab (R5 task 038) is by design — installed at WorkspacePane mount as event sink. | SURFACED | UX policy decision (keep R5 design + improve empty-state copy, OR defer to first stream). |
| **B8** | Single auto-installed Summary tab + all events tagged `streamId === chatSessionId` → tab replace-in-place. `dispatchSummarizeOnly`'s unique correlationId is orphaned. | SURFACED | UX policy decision per POML directive ("don't decide unilaterally"). |
| **B9** | Slash path (Temp=0, JPS system prompt, structured streaming) vs NL path (Temp=0.3, PromptSchemaRenderer templates, non-streaming). Documentation in SprkChatAgentFactory claims convergence; reality is divergence. | SURFACED | Doc drift + normalization is a non-trivial investigation (30-60 min). |

### Net effort estimate (if main session approves all fixes)

- B6 fix: 5 minutes (1-line change + 1 unit test asserting Temperature=0). Build verify needed.
- B7 Option A: 10 minutes (copy update in `StructuredOutputStreamWidget` — owned by Wave B-G9a, coordinate). Option B: 60 minutes (refactor + tests for race conditions).
- B8 Option B: 60-90 minutes (ConversationPane streamId generation + WorkspacePane tab-per-stream + tests). Option C: 90-120 minutes.
- B9 Option A: 5 minutes (docstring update). Option B: 60-120 minutes (orchestration routing change + tests).

**Total if all Option A choices**: 25 minutes. **Total if normalize everything**: 4-6 hours.

---

## Files investigated (reference only, no modifications by this wave)

- `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` (lines 137, 208, 271, 346, 527, 628, 724, 816)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs` (lines 185-427)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` (lines 125-184)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/SummaryHandler.cs` (lines 340-384)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvokePlaybookHandler.cs` (lines 238-378)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs` (lines 42-150)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (lines 1002-1029, 1818-1857)
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (lines 546-655, 720-787)
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` (lines 351-450)
- `src/solutions/SpaarkeAi/src/components/conversation/executeSummarizeIntent.ts` (lines 195-263)
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (lines 1118-1149)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` (lines 600-660, 716-734, 1132-1149)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts` (lines 30-65)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json`

**No files modified by Wave B-G9c.** All 4 bugs surfaced to main session for prioritization + decision.
