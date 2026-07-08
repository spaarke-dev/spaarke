# Wave 12 — Client-Shared Linear Consumer Progress (Follow-Up Notes)

**Date**: 2026-07-02
**Author**: sub-agent (background task)
**Context**: R7 Wave 12 client-side migration off the hardcoded numbered step
visual in the Summarize Files wizard, using a new shared hook + presenter
that mirrors the honest scrolling-text pattern from the Document Upload
wizard.

---

## What was extracted + why

### Files added

- `src/client/shared/Spaarke.UI.Components/src/hooks/useLinearRunProgress.ts`
  — new shared React hook that consumes the canonical `AnalysisStreamChunk`
  SSE envelope from any Linear AI Consumer endpoint. Wraps
  `fetch()` + `ReadableStream.getReader()` (chosen over `EventSource` because
  File Summarize uses multipart/form-data uploads which `EventSource` cannot
  send). Exposes an append-only `LinearRunEvent[]` history plus terminal
  state (`result`, `error`, `tokenUsage`, `status`).

- `src/client/shared/Spaarke.UI.Components/src/components/LinearRunProgress/LinearRunProgressList.tsx`
  — presenter component that renders `LinearRunEvent[]` as a scrolling text
  list. Uses Fluent v9 semantic tokens (ADR-021, dark-mode compatible).
  Two modes: `scrolling-text` (default; full history + timestamps + streaming
  indicator; `role="log"` for AT) and `compact` (single-line latest status;
  `role="status"`). NO hardcoded step numbers, NO client-side step
  interpretation — the list shows exactly what the server sent, in order.

- Companion barrel + wiring:
  - `src/client/shared/Spaarke.UI.Components/src/components/LinearRunProgress/index.ts`
  - `src/client/shared/Spaarke.UI.Components/src/hooks/index.ts` (added export)
  - `src/client/shared/Spaarke.UI.Components/src/components/index.ts` (added export)

### Why

Before Wave 12 there were two divergent progress patterns:

| Surface | Pattern | Verdict |
|---|---|---|
| Document Upload wizard | scrolling text of server-emitted messages | honest — reflects real backend state |
| Summarize Files wizard | hardcoded 5-step numbered visual (`AiProgressStepper` + `DOCUMENT_ANALYSIS_STEPS`) | misleading — steps were client-defined and could not accommodate new server steps without a UI change |

The new hook + presenter is the shared substrate that lets every future
Linear-consumer surface adopt the honest pattern with zero server-format
interpretation on the client. It also keeps future changes small: when the
BFF adds a new progress step (e.g. `"reranking_candidates"`), the client
list picks it up automatically — no client PR needed.

### Files modified (migration of Summarize Files wizard)

- `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/summarizeService.ts`
  — added optional `onProgressEvent(step, message)` callback alongside the
  existing `onProgress(step)`. Preserves back-compat for any consumer that
  still wants the numbered stepper mapping; new consumers should prefer the
  event callback (or better: the `useLinearRunProgress` hook directly).

- `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummaryResultsStep.tsx`
  — replaced `<AiProgressStepper variant="inline" steps={DOCUMENT_ANALYSIS_STEPS} …/>`
  with `<LinearRunProgressList events={events} isStreaming />` inside the
  loading branch. Dropped `activeStepId` + `completedStepIds` props (only
  internal callers); added `events?: LinearRunEvent[]` prop.

- `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeFilesDialog.tsx`
  — replaced `activeStepId` + `completedStepIds` state with a single
  `progressEvents: LinearRunEvent[]` list. The `runAnalysis` callback now
  wires `onProgressEvent` from the service into the events list and passes
  it to `SummaryResultsStep`. Public `ISummarizeFilesDialogProps` is
  unchanged, so downstream code-page consumers (`src/solutions/SummarizeFilesWizard/`,
  `src/solutions/SpaarkeAi/`) do not need to change.

The `AiProgressStepper` component is still exported and used by the
Document Analysis / PlaybookBuilder Execution overlays. Removing it is a
separate, larger cleanup task (see §Follow-on cleanup below).

---

## How SprkChat's Context pane will consume this

The Context pane (R6 Pillar 6c, `context_event` SSE bridge) already
subscribes to a specific consumer's stream via the `useSseStream` +
`onContextEvent` callback pattern. The new hook/presenter fits alongside
without disruption:

- **Consumer contract stays the same.** The Context pane will call
  `useLinearRunProgress({ start: () => fetch(bffUrl, { method: 'POST', body }) })`
  with whatever Linear-consumer endpoint the pane is bound to (initially
  the Document Profile stream, later any linear consumer routed via
  playbook config).
- **Same event history, different rendering.** The Context pane can render
  the returned `events` with either mode of `LinearRunProgressList`:
  `scrolling-text` for the primary progress view or `compact` for a
  status-line variant embedded in a header. Both drop-in.
- **No duplicate parsing logic.** Both SprkChat and the Summarize wizard
  will parse `AnalysisStreamChunk` in exactly one place (the hook),
  eliminating the drift risk we hit last year when the SprkChat
  `useSseStream` and the standalone `Spaarke.AI.Context/hooks/useSseStream`
  diverged (see the CONSOLIDATION NOTE in `useSseStream.ts` line 26-36).

Practical migration for the Context pane in a follow-on task:

1. Replace the pane's bespoke SSE parser (whichever one it uses today) with
   `useLinearRunProgress`.
2. Bind the pane's "session" widget to `LinearRunProgressList events={run.events}`
   in `compact` mode for a status-line variant, or `scrolling-text` for a
   detail popover.
3. Keep the `context_event` bridge for higher-level trace events (those
   have a different envelope shape, `event: 'context_event'` rather than
   the `AnalysisStreamChunk` `type` discriminator).

No BFF changes needed — the wire contract is already the shared
`AnalysisStreamChunk` per Wave 12 task 111a.

---

## Document Upload wizard — cleanup opportunity

The Document Upload wizard currently has its own inline SSE parser (search
`onmessage` / `EventSource` in `useAiSummary.ts`, lines 388-471). It could
adopt the shared hook + presenter for two wins:

1. **Delete the inline parser.** ~80 LOC of chunk-type dispatch (metadata /
   chunk / done / error handling) collapses into `useLinearRunProgress`
   consumption. The document-specific bits (`updateDocument`, batched
   concurrency queue) stay in `useAiSummary`; the SSE plumbing moves out.
2. **Adopt `LinearRunProgressList` for the per-document detail popover.**
   The wizard currently renders progress as `summary` state accumulation +
   an ad-hoc status line. Replacing with the presenter gives it timestamps,
   auto-scroll, accessibility roles, and consistent look with Summarize
   for free.

**Recommendation**: DO the cleanup in a follow-on Wave 12 sub-task, not in
the initial migration PR. Reasons:

- `useAiSummary` has multi-document concurrent-stream orchestration
  (`activeStreamsRef`, `pendingQueueRef`, `maxConcurrent`) that
  `useLinearRunProgress` doesn't model. That layer stays; only the
  per-document SSE loop moves.
- The wizard has stable analytics + regression coverage; a bigger change
  risks distracting from the Summarize migration.
- The shared hook is deliberately single-stream so it's easy to reason
  about. Multi-stream orchestration should stay in `useAiSummary` as a
  thin wrapper that calls into the shared hook per document.

Estimated effort for the cleanup follow-on: ~4-6h including tests.

---

## Design decisions where the brief was ambiguous

1. **`start()` semantics.** Brief said `start: () => void` on the return.
   I implemented it as a fresh-run trigger — every call cancels any
   in-flight stream and resets the events list. This matches the
   "each run is a discrete history" mental model implied by
   `LinearRunEvent[]` being append-only within one run.

2. **Which event kinds render in the list.** Brief said "renders `events`
   filtered to `kind === 'progress'`". I included `error` in the visible
   list too so a failed run leaves a visible trail in the UI. Metadata /
   chunk / result / done are correctly filtered out per the brief.

3. **`done` chunk vs `[DONE]` terminator.** Brief listed both as
   termination signals. I treat them equivalently: either transitions
   `status: 'running' → 'complete'` and stops the reader loop. If both
   arrive, the second is a no-op.

4. **`result` parsing.** Brief said "parsed JSON if a 'result' chunk
   arrived". I try `JSON.parse(chunk.content)` and fall back to the raw
   string when parse fails, so callers can inspect malformed payloads
   during triage. `state.result` is `unknown` — callers cast to their
   consumer-specific shape.

5. **Presenter mode when list is empty.** Added an `emptyText` prop with a
   sensible default ("Waiting for progress updates…") so the loading
   container isn't blank between the moment the fetch is issued and the
   first `progress` chunk arrives. Renders in a muted italic style.

6. **AiProgressStepper retirement.** Left it in the library — retiring is
   a bigger cross-cutting change (PlaybookBuilder ExecutionOverlay,
   AnalysisWorkspace still use it). Removing untouched.

---

## Verification

- `cd src/client/shared/Spaarke.UI.Components && npm run build` → clean
  (`tsc` exit 0, no diagnostics).
- `cd src/solutions/SummarizeFilesWizard && npm run build` → clean
  (`vite build` succeeded, dist/index.html generated, ~1.67 MB, ~458 KB
  gzip — same order of magnitude as before).
- No changes to public `ISummarizeFilesDialogProps` surface, so the
  SpaarkeAi ConversationPane + SummarizeFilesWizard code-page consumers
  keep working without changes.

---

## Non-goals / not done

- Did NOT touch BFF. Wire contract unchanged.
- Did NOT delete `AiProgressStepper` — still used elsewhere.
- Did NOT migrate the Document Upload wizard's `useAiSummary` to the new
  hook (see §Cleanup above; deliberate follow-on).
- Did NOT wire the Context pane consumption — that's a separate task
  the main session or a follow-on wave will handle.
- Did NOT touch the LegalWorkspace copy under
  `src/solutions/LegalWorkspace/src/components/SummarizeFiles/` since
  LegalWorkspace standalone is being retired (OC-R4-05).
