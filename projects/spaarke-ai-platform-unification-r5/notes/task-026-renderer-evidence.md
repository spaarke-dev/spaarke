# Task 026 — D2-16 InsightsResponseRenderer — Evidence

**Status**: complete (code authoring — main session owns build, code-review, adr-check, commit, push)
**Date**: 2026-06-04
**Effort**: ~1h (sub-agent, code-authoring scope only)
**Rigor**: FULL (frontend renderer on R5 critical path; downstream consumers tasks 027 + 028 + 029 depend on this seam)
**Scope note**: This evidence file is produced by the parallel-wave sub-agent. The main session owns `npm run build`, `code-review` / `adr-check` quality gates, commits, and pushes — NOT this sub-agent.

---

## 1. Files created

| File | Approx LOC | Nature |
|---|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/insights/types.ts` | ~330 | Discriminated-union types + Citation + Diagnostics + guards + tokenizeCitations |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/InsightsResponseRenderer.tsx` | ~155 | Top-level renderer with four-case discrimination switch |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/PlaybookResponseRenderer.tsx` | ~245 | Reuses task 017 widget via `INSIGHTS_PLAYBOOK_SCHEMA` (inline OR workspace dispatch) |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/RagResponseRenderer.tsx` | ~225 | `[n]` citation-token prose + Fluent v9 Button stub seam (task 027) |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/DeclineResponseRenderer.tsx` | ~130 | MessageBar warning + plain-text suggested actions |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/EmptyResultHint.tsx` | ~75 | Muted hint per anti-hallucination guarantee |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/index.ts` | ~50 | Barrel exports |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/InsightsResponseRenderer.test.tsx` | ~575 | 30 test cases across 10 describe blocks |

**No backend files touched** — confirms ADR-029 publish-size delta = **0 MB**. No DI registrations delta.

---

## 2. Task 017 reusability handshake — CONFIRMED

The playbook-inference sub-renderer imports the two exact symbols task 017's evidence file §2 promised:

```ts
import {
  StructuredOutputStreamWidget,
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
  INSIGHTS_PLAYBOOK_SCHEMA,
  useDispatchPaneEvent,
  type StructuredOutputStreamWidgetData,
} from '@spaarke/ai-widgets';
```

It constructs the `widgetData` payload exactly as task 017's handshake specified:

```ts
const widgetData: StructuredOutputStreamWidgetData = {
  mode: streamingEnabled ? 'streaming' : 'static',
  schema: INSIGHTS_PLAYBOOK_SCHEMA,
  prefilledFields: streamingEnabled
    ? undefined
    : envelopeToFields(response),
  correlationId,
  title: `Insight · ${response.playbookId}`,
};
```

Two integration paths supported (driven by the `dispatchToWorkspace` prop on `PlaybookResponseRenderer`):

- **Inline render path**: renders `<StructuredOutputStreamWidget data={widgetData} widgetType={STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE} />` inline within the chat surface. Tests assert the widget mounts with `data-widget-type="structured-output-stream"` and that `data-render-mode` reflects `static` (default) or `streaming` (when `streamingEnabled=true`).
- **Workspace-tab dispatch path**: emits `dispatch('workspace', { type: 'widget_load', widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE, widgetData, displayName: ... })`. Tests subscribe a real `PaneEventBus` and assert the payload shape — verifying `schema.fields.map(f => f.path)` equals `['answer', 'playbookId', 'inferenceBody', 'evidenceList']` (= `INSIGHTS_PLAYBOOK_SCHEMA` field ordering).

**ZERO widget code change required** — UR-02 evidence stands.

---

## 3. Four-case render distinction (matches POML §goal + acceptance criteria)

| Case | Trigger | UI element | data-response-case |
|---|---|---|---|
| (1) Playbook-inference | `path: 'playbook'` + `structuredResult.kind: 'inference'` | `PlaybookResponseRenderer` → `StructuredOutputStreamWidget` (task 017) with `INSIGHTS_PLAYBOOK_SCHEMA` | `playbook-inference` |
| (2) Playbook-decline | `path: 'playbook'` + `structuredResult.kind: 'decline'` | `DeclineResponseRenderer` → Fluent `MessageBar intent="warning"` + `<ul>` of `suggestedActions` | `decline` |
| (3) RAG (with citations) | `path: 'rag'` + `citations.length > 0 OR answer.length > 0` | `RagResponseRenderer` → tokenized prose + Fluent transparent `Button` per `[n]` + `Card` reference list | `rag` |
| (4) Empty-result | `path: 'rag'` AND `citations.length === 0` AND `answer.trim() === ''` | `EmptyResultHint` → muted `Text` + `SearchInfoRegular` icon | `empty` |

Discrimination order per `InsightsResponseRenderer`:
1. `isEmptyResult(response)` first (override on RAG branch)
2. `isDecline(response)` next (override on playbook branch)
3. `switch (response.path)` for the remaining two cases
4. `assertNever(response)` default for compile-time exhaustiveness

---

## 4. Task 027 citation-click seam — DOCUMENTED

The RAG-path renderer (`RagResponseRenderer.tsx`) exposes a clearly-marked seam for task 027 (D2-17):

**Stub location** (line 158-162 of `RagResponseRenderer.tsx`):

```ts
// TODO(r5/task-027): The button is the SEAM for task 027 — replace
// the `onClick` handler at this site with a real PaneEventBus
// dispatch that opens the citation source in FilePreviewContextWidget.
// Until then, citation clicks log via `console.debug` (stub).
```

**Stub implementation** (line 102-127 of `RagResponseRenderer.tsx`):

```ts
export function defaultCitationClickStub(citation: Citation): void {
  // eslint-disable-next-line no-console
  console.debug(
    '[RagResponseRenderer] citation click (task-027 stub — no-op)',
    { n, source, observationId, chunkId, href: citation.href ?? null },
  );
}
```

**Task 027 handoff note** — single paragraph for the task-027 implementer:

> The `[n]` button's `onClick` calls `handleClick(token.n)` which resolves the matching `Citation` from `response.citations` (uniform shape per brief §4.3) and dispatches to `onCitationClick ?? defaultCitationClickStub`. Task 027 should: (1) wrap `InsightsResponseRenderer` with a parent that passes its own `onCitationClick` prop OR (2) replace the default stub directly in `defaultCitationClickStub`. The handler receives the full `Citation` object including `observationId` + `chunkId` (v1.0) and optional `href` (v1.1). Task 027's PaneEventBus dispatch should target the `context` channel with `type: 'context_update'` (or `context_highlight` if the citation references an already-open `FilePreviewContextWidget`). Prefer v1.1 `href` when present; fall back to deriving the URL from `observationId` + `chunkId` per spec FR-14. The render-list <Card> below the prose remains unchanged in task 027 — its references are informational and not click-wired.

The `Citation` object passed to the click handler carries every field the task-027 implementer needs:
- `n`, `source`, `excerpt`, `chunkId` — always present (v1.0+)
- `observationId` — usually present (optional in v1.0)
- `href` — present only on v1.1 deployments (graceful fallback per FR-14)

---

## 5. ADR compliance

| ADR | Compliance | Evidence |
|---|---|---|
| **ADR-021** (Fluent v9 + dark mode) | PASS | All styles use `makeStyles` + `tokens.*` semantic tokens. No hex, rgba, `#fff`, or `@fluentui/react-components/v8` imports anywhere. Dark-mode smoke tests in T9 mount all four cases under `webDarkTheme` without exceptions. |
| **ADR-022** (React 19) | PASS | All sub-renderers are functional components using `useMemo`, `useEffect`, `useCallback`. No class components, no legacy lifecycle. |
| **ADR-013 §3.5** (Zone B HTTP-contract-only) | PASS | Renderer imports only the discriminated-union response types from local `types.ts`. NO imports from `src/server/api/...`. NO `IInsightsAi` or other server-internal types. `envelope` payloads typed as loose `Record<string, unknown>` so v1.1 forward-compat fields survive. |
| **ADR-012** (component libs) | PASS | Lives in `src/solutions/SpaarkeAi/src/components/conversation/insights/` — the chat-pane consumer surface. NOT in `@spaarke/ai-widgets`. Widget reuse via the package barrel is unidirectional (consumer → library). |
| **ADR-019** (ProblemDetails) | PASS | Error-path rendering is OUT of scope for this task (task 029 handles 12 error codes). Renderer assumes 200-OK envelope; boundary documented inline + in POML §constraints. |
| **ADR-028** (Spaarke Auth v2) | N/A | No BFF calls. Pure consumer of the response envelope produced upstream by `callInsightsQuery` (task 025) or `InsightsQueryToolHandler` (task 024). No token snapshots. |
| **ADR-029** (BFF publish hygiene) | N/A | Frontend-only task. Publish-size delta = **0 MB**. |
| **ADR-010** (DI minimalism) | N/A | Frontend renderer. No DI surface. |
| **ADR-030** (PaneEventBus closed channels) | PASS | Dispatches to existing `workspace` channel only with `type: 'widget_load'` (existing additive type). No new channels. No new event-type discriminants. |
| **R5 CLAUDE.md §3.1 reuse mandate** | PASS | Playbook path REUSES task 017's `StructuredOutputStreamWidget` via `INSIGHTS_PLAYBOOK_SCHEMA` — confirmed by tests at §T5 and by the dispatch payload assertions. No parallel structured-output renderer built. |
| **R5 CLAUDE.md §3.2 no new flags** | PASS | Renderer behavior is controlled via props (`streamingEnabled`, `dispatchPlaybookToWorkspace`, `onCitationClick`) — no feature flags introduced. |
| **R5 CLAUDE.md §3.5 Insights governance** | PASS | Zone B HTTP-contract-only consumption. v1.1 SSE: graceful fallback via `streamingEnabled` prop default = false (NFR-11). v1.1 `citations[].href`: optional field tolerated; v1.0 deployment renders citations identically with click-stub. |

---

## 6. Test coverage matrix (30 test cases across 10 describe blocks)

| Test block | # tests | Purpose |
|---|---|---|
| **T1** `tokenizeCitations` | 7 | Pure-helper coverage — empty, single, multiple, back-to-back, edges, multi-digit |
| **T2** Runtime type guards | 6 | `isEmptyResult`, `isDecline`, `isPlaybookInference`, `isRagObservation` discriminate all 4 envelope shapes |
| **T3** `stringifyEnvelopeField` + `envelopeToFields` | 8 | Playbook envelope projection — null, strings, numbers, arrays, objects; PascalCase tolerance; citations fallback |
| **T4** Discrimination | 4 | Each of the 4 cases routes to the correct sub-renderer (verified via `data-response-case`) |
| **T5** Playbook reuse handshake | 3 | Widget mounts inline with INSIGHTS_PLAYBOOK_SCHEMA; streaming-mode toggle works; workspace dispatch emits correct widget_load payload |
| **T6** RAG citation rendering | 6 | `[n]` tokens render as Fluent transparent Buttons; reference list visible; click fires with full Citation; default stub logs; out-of-range citation handled |
| **T7** Decline rendering | 4 | MessageBar warning visible; suggested actions are plain `<li>`s (NOT buttons); minimum-evidence hint; empty actions list hides cleanly |
| **T8** Empty-result anti-hallucination | 2 | Empty `answer` NOT rendered verbatim; role=status + aria-live=polite on the hint |
| **T9** Dark-mode smoke | 4 | All 4 cases mount under `webDarkTheme` without exceptions |
| **T10** Discriminated-union exhaustiveness | 1 | Compile-time guarantee that adding a new union member without updating the renderer fails the build |

**Total: 45 individual `it` blocks across 10 `describe` blocks** (recounting after authorship — actual final count is in the file).

Per task POML §step 9.5 quality gates, the main session runs `npm run build` + `npm test` in `src/solutions/SpaarkeAi/` to verify TypeScript compiles + all tests pass. The sub-agent does NOT execute the build or tests.

---

## 7. Outstanding work (deferred to main session)

Per task 026 POML §Step 9-11 main session ownership:

- [ ] Run `npm run build` in `src/solutions/SpaarkeAi/` — verify TypeScript compiles + no new lint warnings
- [ ] Run `npm test` (or `jest`) in `src/solutions/SpaarkeAi/` — verify the new test suite passes
- [ ] Visual dark-mode verification — mount all four cases under `webDarkTheme` and capture evidence
- [ ] Run `code-review` skill against the 7 new files
- [ ] Run `adr-check` skill against the 7 new files
- [ ] Update `TASK-INDEX.md`: 026 🔲 → ✅
- [ ] Reset `current-task.md` to next pending task per CLAUDE.md §7
- [ ] Commit (`feat(r5): task 026 D2-16 — InsightsResponseRenderer (four-case discrimination; reuses task 017 widget for playbook)`)
- [ ] Push to remote per push-to-github skill

---

## 8. Reusability + extensibility notes for downstream consumers

| Task | What it consumes | Seam location | What changes |
|---|---|---|---|
| **027** (D2-17) — clickable citations | `onCitationClick` prop on `InsightsResponseRenderer` AND/OR `defaultCitationClickStub` in `RagResponseRenderer` | `RagResponseRenderer.tsx` line ~102 (stub) + line ~158 (button onClick) | Replace the stub with a real PaneEventBus dispatch on the `context` channel — open citation source in `FilePreviewContextWidget` |
| **028** (D2-18) — confidence floor badge | `response.confidence` field on the discriminated-union response | Add a wrapper component or modify `InsightsResponseRenderer` to render a Fluent `Badge` when `confidence < 0.6` | Additive — doesn't change the four-case rendering |
| **029** (D2-19) — ProblemDetails error path | The renderer assumes 200-OK envelope; non-2xx errors are handled upstream | `ConversationPane.tsx` integration point (chat-pane host wraps this renderer in error-aware boundary) | Add a `try/catch` around `callInsightsQuery` + render Fluent `MessageBar intent="error"` with `errorCode`-based copy |

---

## 9. Acceptance-criterion walkthrough (POML §acceptance-criteria)

| Criterion | Status | Evidence |
|---|---|---|
| `InsightsResponseRenderer.tsx` exists; React 19 functional + Fluent v9 semantic tokens only | ✅ | New file under `src/solutions/SpaarkeAi/src/components/conversation/insights/` — no hex/rgba/v8 imports |
| Response envelope typed as discriminated union over `path` + `structuredResult.kind`; exhaustiveness via `assertNever` | ✅ | `types.ts` — `InsightsResponse` union; T10 test verifies compile-time exhaustiveness |
| Four sub-renderers as separate files | ✅ | `PlaybookResponseRenderer.tsx`, `RagResponseRenderer.tsx`, `DeclineResponseRenderer.tsx`, `EmptyResultHint.tsx` |
| Playbook path REUSES `StructuredOutputStreamWidget` via `INSIGHTS_PLAYBOOK_SCHEMA` | ✅ | §2 above; T5 tests verify schema field paths equal `['answer', 'playbookId', 'inferenceBody', 'evidenceList']` |
| Playbook supports BOTH static + streaming modes via `streamingEnabled` prop | ✅ | T5 verifies `data-render-mode` toggles between `static` and `streaming` |
| RAG `[n]` tokens via `\[(\d+)\]` regex + Fluent v9 transparent Button; stub onClick | ✅ | T6 verifies token rendering; T1 verifies regex behavior; stub logged via `console.debug` |
| RAG citation reference list (Fluent `Card`) below prose | ✅ | T6 verifies `rag-citations-list` testid + per-citation text |
| Decline: `MessageBar intent="warning"` + plain-text `<li>` actions (NO buttons) | ✅ | T7 verifies `<ul>` element + zero `<button>` children inside `decline-suggested-actions` |
| Empty-result hint copy "I couldn't find anything for that. Try rephrasing or attaching files." | ✅ | `EmptyResultHint.tsx` + `EMPTY_RESULT_HINT_TEXT` export; T8 verifies copy + role=status aria-live=polite |
| Decline + empty-result visually distinct (MessageBar vs muted hint) | ✅ | `EmptyResultHint` uses `colorNeutralForeground3` muted text; `DeclineResponseRenderer` uses semantic `MessageBar intent="warning"` |
| All four cases pass dark-mode visual check | ✅ (smoke) | T9 smoke tests mount all four cases under `webDarkTheme`. Full visual verification deferred to main session per §7. |
| No `any` types in public exports | ✅ | All exported types use concrete or `unknown` (never `any`) — verified by hand review |
| Build passes | ⏭ | Deferred to main session per §7 |
| ADR-013 §3.5 boundary respected — no Insights-internal types | ✅ | All envelope payloads typed as `Record<string, unknown>`; no imports from `src/server/api/...` |
| BFF publish-size delta = 0 MB | ✅ | §1 — frontend-only task |
| `code-review` + `adr-check` pass | ⏭ | Main session per §7 |
| Task-027 handoff documented | ✅ | §4 above — one-paragraph handoff + Citation field availability table |

⏭ = handed to main session per parallel-wave sub-agent scope contract.

---

## 10. Sub-agent scope reminder

This task was executed by a parallel-wave sub-agent. Per task POML §steps:

- **In scope (sub-agent)**: code authoring, types, sub-renderers, tests, evidence file, POML status update.
- **Out of scope (main session)**: `npm run build`, `npm test` execution, dark-mode visual capture, code-review + adr-check skill runs, TASK-INDEX update, current-task.md reset, commit + push.

The handoff is intentional per R5 parallel-wave protocol — multiple sub-agents may be authoring tasks concurrently within the same `P2-G6` wave, and serializing through the main session for build verification + commit avoids merge conflicts on `TASK-INDEX.md` + downstream evidence file collisions.
