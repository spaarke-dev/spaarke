# Task 046 — Widget layer: registry dedupe + ExecutionTraceWidget ledger bridge + FieldDelta cutover (FR-P3-07) — Task Notes

> Date: 2026-07-07 · Wave W-P3-D · task-execute FULL rigor.
> Hard boundaries honored: no commit/push; no TASK-INDEX/current-task edits; no `.claude/` writes.

## 1. Registry dedupe (one `register-context-widgets` module client-wide)

- `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` is now THE single
  registration module: 8 shell context widgets (progress-tracker, playbook-gallery, get-started-cards,
  entity-info, findings, file-preview, execution-trace, pinned-memory-list) + the 6 R1 source widgets
  (DocumentViewer, WebSource, LegalLibrary, Citation, ImageViewer, CodeViewer) — all via `safeRegister`
  isolation + lazy dynamic-import factories.
- DELETED: `src/widgets/context/register-context-widgets.ts` (the drifted duplicate) + its test.
- `index.ts` (barrel): the six INLINE `registerContextWidget()` calls removed; ONE side-effect import of
  the single module; `registerContextWidgets` re-exported for direct entry points. Both mount paths
  (dashboard wrapper / barrel consumers + direct widget wrapper / non-barrel entry points) now consume
  the same module.
- Grep evidence (shown in transcript): `find src -name "register-context-widgets*"` → exactly the ONE
  module + its test; all import sites point at `registry/register-context-widgets`.

## 2. ExecutionTraceWidget — ADR-040 ledger ToolChain bridge

- **Server** (`ChatEndpoints.FlushToolChainLedgerAsync`): after `AppendToolChainAsync` persists a
  ToolChain segment to the session ledger, the SAME segment is emitted as a `context_event` SSE frame
  (discriminant `tool_chain`) — storage strictly precedes rendering (ADR-040). No new endpoint: the
  bridge rides the existing per-request chat SSE + `context_event` pipe (the consolidated event path
  the POML prescribes), so no ledger read GET was needed.
- **Wire** (`ContextSseEventDto` + new `ContextToolChainCallDto`): `contextTurn` +
  `contextToolChainCalls[{toolId, argsSummary, resultCount, citationCount, durationMs}]` — NFR-07
  identifiers/filters/counts only; `argsSummary` is redacted at RECORDING time by
  `AgentTurnContract.SummarizeArguments`; citation ids stay in the ledger (a COUNT rides the wire).
- **Client**: SprkChat `IChatSseEventData` + SpaarkeAi `useContextEventBridge` forward the payload
  (explicit per-field copy, never a spread) to the `context` bus as the new additive `tool_chain`
  `ContextPaneEvent` discriminant (+ exported `TraceToolCallSummary` type; ADR-030 — no new channel).
- **Widget**: `ExecutionTraceWidget` rewritten to render ONLY persisted ledger ToolChain segments
  (rows: tool id, redacted args summary, result/citation counts, duration, HH:mm:ss; grouped under
  "Turn N" badges). The legacy R6 live-telemetry source (six `tool_call_started`-family events) is no
  longer rendered — jest proves those events are IGNORED. The widget never synthesizes trace data.
- The six legacy `context.*` trace discriminants + `ContextEventEmitter` emissions REMAIN as bus/
  telemetry vocabulary (meters/logs) — removing them is server telemetry surface, out of this task's
  widget-layer scope; flagged as a Track-B / FR-P4-01 candidate (emitters now have no rendering
  consumer).

## 3. FieldDelta dual-render path — DELETED at last-playbook cutover (amended ADR-037)

**Precondition verified (verify-dead-first, shown in transcript)**: `AnalysisChunk.FromDelta` has ZERO
call sites server-side — no live playbook/endpoint emits `type:"delta"` chunks. The frozen-engine
`IncrementalJsonParser`/`FieldDeltaEvent` and the insights `AssistantQueryChunk` "delta" leg serve
NON-widget surfaces and are untouched (frozen per FR-P3-05). The ONLY remaining producer was the
client-side `dispatchConsumer` bridge (live "delta" case = dead; synthesized-from-complete = live).

**Cutover scope (all landed together — no dual-render fallback retained, NFR-08):**
- `dispatchConsumer` (UI.Components): the dead `"delta"` case + `AnalysisFieldDelta` type deleted; the
  terminal `complete` result now bridges onto `section_started` + `section_completed` pairs (one per
  top-level key, declaration order; strings → `finalContent`, arrays/objects → `finalStructuredData`).
  `DispatchWorkspaceEvent` re-typed to the section vocabulary.
- `PaneEventTypes.ts` (widget layer): `field_delta` discriminant + `fieldPath`/`fieldContent`/`sequence`
  fields DELETED from `WorkspacePaneEvent`; docs rewritten (section-name-keyed streaming is THE binding
  contract per ADR-037-as-amended 2026-07-05).
- `StructuredOutputStreamWidget`: per-field-delta reducer machinery (`FieldState`, `fields` map,
  `mostRecentPath`, `field_delta` action + event case) DELETED. Streaming mode renders sections ONLY
  (+ skeleton waiting UI pre-first-section); the schema-aware field renderer (tasks 040/041) now serves
  STATIC (`prefilledFields`) mode exclusively — Insights static envelope path unchanged.
- **SectionRenderer upgrade (B-G9a regression guard)**: because the Summarize-this-only flow now rides
  sections, non-string `finalStructuredData` would have rendered as compact JSON `<pre>` — the exact
  raw-JSON failure class B-G9a fixed. Added value-shape-typed rendering (schema-agnostic per FR-54):
  array-of-strings → bulleted list; flat record of strings/string-arrays → labeled rows (prettyName
  keys, nested lists); anything else → the unchanged compact-JSON fallback. Jest asserts the negative
  (no raw JSON syntax in DOM for SUM-CHAT-shaped payloads).
- `WorkspacePane.tsx` (SpaarkeAi) + `FilePreviewContextWidget` comments updated (the auto-focus
  override logic itself keys on `streaming_started`/`streaming_complete` — unchanged).

**Grep-zero (NFR-08, output shown in transcript):**
- `grep -rn "FieldDelta\|field_delta" src/client/shared/Spaarke.AI.Widgets/src` → exit 1 (ZERO).
- Same for `Spaarke.UI.Components/src` and `src/solutions/SpaarkeAi/src` → ZERO (full client sweep).
- `git grep AnalysisFieldDelta -- src` → ZERO.
- Server `AnalysisChunk.FieldDelta` record + `FromDelta` factory remain (now fully dead) — server-model
  deletion deferred to Track-B / FR-P4-01 per POML scope ("widget layer"); noted for the audit.

## 4. Dispositions / escalations

- **`playbook_options` client leg (045 flag)**: NOT deleted. The 046 POML's steps/outputs enumerate
  three deliverables only; `usePlaybookOptions` is ConversationPane wiring (FR-P3-06 surface), not the
  widget layer. Deleting it also ripples into the SprkChat prop contract. Remains a Track-B / FR-P4-01
  deletion candidate exactly as 045 recorded it.
- **047 §escalation-2 (Binding disposition on the terminal AnalysisChunk for work_product rendering)**:
  NOT implemented — the 046 POML does not prescribe it (no disposition/work_product mention). The
  additive wire change remains available (extend `AnalysisChunk` + dispatchConsumer when a task
  prescribes the §3.10.3 "Workspace-primary" render split).
- **Legacy trace emitters (`ContextEventEmitter` six trace events + `useContextEventBridge` mappings)**:
  retained as telemetry vocabulary; no rendering consumer remains → Track-B / FR-P4-01 candidate.

## 5. Verification summary (2026-07-07, shown in transcript)

- **BFF**: `dotnet build` 0 errors (warnings pre-existing). Targeted suites
  (AgentTurnLoopContract + ChatSessionLedgerRoundTrip + TypedHandlerResumeExecutor) **43/43**.
  **Publish size (NFR-01 / §10 bullet 4): 45.47 MB compressed** vs 45.65 MB baseline (−0.18 MB;
  ceiling 60 MB — PASS).
- **Spaarke.AI.Widgets**: `npm run build` (tsc) exit 0. Touched suites **82/82 + 30/30 re-run + 18/18**
  (registry ×2, ExecutionTraceWidget, StructuredOutputStreamWidget main/sections/integration).
  Full-package jest: 9 suites fail — ALL empirically proven PRE-EXISTING by stash-baseline re-run
  (`git stash` → same 9 suites fail on the pre-change tree; d3-force ESM jest-transform via the
  `@spaarke/ui-components` barrel — the known parallel-branch issue documented in
  `register-execution-trace-widget.test.ts`).
- **Spaarke.UI.Components**: `npm run build` (tsc) exit 0; dispatchConsumer jest **24/24**.
- **SpaarkeAi**: conversation jest **216/216** (12 suites); production `npm run build`
  (vite + gates + ribbon) exit 0.
- **Dark mode (ADR-021)**: all new/changed rendering uses Fluent v9 semantic tokens exclusively
  (ExecutionTraceWidget styles, SectionRenderer additions reuse existing token-based classes; one
  inline layout style replaced with a makeStyles class in review). Jest includes the no-hardcoded-color
  DOM scans. Browser dark-mode verification deferred to gate-048 UAT (no Chrome integration in this
  session) — see §6.

## 6. Gate-048 UAT additions (browser rule NFR-11 — operator on spaarkedev1)

1. **Trace widget, real chain**: in the SpaarkeAi Assistant, send a text-path turn that invokes tools
   (e.g. "what documents are on this matter?" → dataverse.read). Open the Context pane's Execution
   Trace widget → it shows a "Turn N" group with one row per tool call (tool id, redacted args summary
   like `entity=…; top=5`, result count, duration) — matching the actual run. Rows appear only AFTER
   the response begins rendering (ledger-write-then-emit ordering).
2. **NFR-07 spot-check**: no user text or document content appears in any trace row (free-text args
   show as `<redacted:len>`).
3. **Widgets still mount (both paths)**: context widgets resolve on the dashboard wrapper AND the
   direct widget path (e.g. entity-info + file-preview + execution-trace mount; a `context_update`
   DocumentViewer resolves) — single registration module.
4. **Summarize this only (section cutover)**: upload a file → Context pane file preview → "Summarize
   this only" → the Workspace `structured-output-stream` tab renders SECTIONS: Tldr as bullets,
   Summary as text, Entities as labeled Organizations/Persons lists — NO raw JSON anywhere.
5. **Chip click path**: classify chips → "Summarize this document" → summary renders in conversation;
   any workspace-targeted dispatch renders sections; console error-free.
6. **Dark mode (ADR-021)**: repeat 1 + 4 in dark theme — trace rows, turn badges, section lists/labeled
   rows all adapt (tokens only).
7. **Console clean** throughout the above.

## 7. Step 9.5 quality gates

- code-review: PASS after 1 fix (inline `style={{flex:1}}` → makeStyles class). No Critical.
- adr-check verdicts: ADR-037-am ✅ (cutover executed at verified last-playbook point; section-keyed
  only); ADR-040 ✅ (append-then-emit; widget renders persisted records, never synthesizes);
  ADR-039 ✅ (no routing/intent in widget layer; Binding-addressed dispatch untouched); NFR-07 ✅
  (identifiers/counts wire + explicit per-field copies + leak-guard test); NFR-08 ✅ (grep-zero shown;
  no fallback retained); ADR-030 ✅ (4 channels; additive `tool_chain`); ADR-021 ✅ (tokens only);
  ADR-013-am ✅ (no new client surface beyond the existing SSE); ADR-019 ✅ (unchanged error posture);
  ADR-038 ✅ (tests updated at KEEP paths; no banned patterns). No path-A/B escalations required —
  the one deliberate type-surface deletion (`field_delta` off the workspace union) is the cutover the
  amended ADR-037 pre-authorizes.
