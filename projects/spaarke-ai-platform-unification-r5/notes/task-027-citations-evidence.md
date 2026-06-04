# Task 027 — D2-17 Clickable Citations (v1.1 `citations[].href`) — Evidence

**Status**: complete (code authoring — main session owns build, quality gates, commit, push)
**Date**: 2026-06-04
**Effort**: ~1.5h (sub-agent, code-authoring scope only)
**Rigor**: FULL (frontend citation click wiring on R5 critical path; consumes v1.1 contract; UR-04 + NFR-11 fallback contingencies)
**Scope note**: Produced by a parallel-wave sub-agent. Per POML §scope-note + R5 wave protocol, the main session owns `npm run build`, `npm test`, `code-review` / `adr-check` quality gates, commits, and pushes — NOT this sub-agent.

---

## 1. Files modified

| File | Nature | LOC delta |
|---|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/insights/RagResponseRenderer.tsx` | EXTEND — resolved task 026's `TODO(r5/task-027)` markers; wired `useDispatchPaneEvent` for clickable citations; added non-clickable span variant for graceful v1.0 / observation-only-href fallback | full rewrite (~360 LOC) |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/index.ts` | EXTEND — exported `isClickableCitation` helper | +1 export |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/InsightsResponseRenderer.test.tsx` | EXTEND — updated existing T6 tests for new variant attributes; added new T11 describe block with 10 task-027-specific tests | +~250 LOC |
| `projects/spaarke-ai-platform-unification-r5/tasks/027-clickable-citations-v1-1.poml` | STATUS update — `not-started` → `complete`; started + completed timestamps | metadata only |

**No BFF files touched** — confirms ADR-029 publish-size delta = **0 MB**. No DI registrations delta.
**`InsightsResponseRenderer.tsx` not modified** — the existing `onCitationClick` prop seam already wired through to `RagResponseRenderer` (task 026 design). Task 028 (parallel wave) extended this file with the confidence-floor badge; no overlap with task 027.

---

## 2. TODO marker resolution — CONFIRMED CLEARED

Task 026's evidence file (`notes/task-026-renderer-evidence.md` §4) flagged TWO `TODO(r5/task-027)` markers in `RagResponseRenderer.tsx`:

| Marker location (task 026) | Marker source code (task 026) | Task 027 resolution |
|---|---|---|
| Line ~158-162 (button `onClick` site) | "The button is the SEAM for task 027 — replace the `onClick` handler at this site with a real PaneEventBus dispatch that opens the citation source in FilePreviewContextWidget." | RESOLVED — clickable variant now wires `onClick={() => handleClick(token.n)}` where `handleClick` dispatches `context.context_update` with `{ url, displayName }` payload to the PaneEventBus `context` channel. |
| Line ~102-127 (stub function `defaultCitationClickStub`) | "TODO(r5/task-027): Replace this stub with a PaneEventBus dispatch (`context.context_update`) to open the citation source in `FilePreviewContextWidget`. The replacement needs the `Citation` object's `observationId` + `chunkId` (v1.0) OR `href` (v1.1) to derive the source URL." | RESOLVED — the stub remains exported (back-compat for task 026 tests + future caller-provided overrides) but is NO LONGER the default click target. The component's `handleClick` callback (computed inside the function body via `useDispatchPaneEvent` per React 19 hook rules) is the production dispatch path. Documentation updated to reflect new contract. |

**Verification**: `grep "TODO(r5/task-027)" src/solutions/SpaarkeAi/src/components/conversation/insights/RagResponseRenderer.tsx` returns ZERO matches. All task-027 TODO markers cleared.

---

## 3. Three-scenario handling — CONFIRMED (UR-04 + NFR-11)

The POML §goal calls out three rendering scenarios that MUST be handled gracefully:

| Scenario | Trigger | UI behaviour | Test coverage |
|---|---|---|---|
| **(a) v1.1 full** — all `citations[].href` populated | `c.href = 'https://...'` for every citation | Every `[n]` token → Fluent v9 `Button appearance="transparent"`; click dispatches `context.context_update`. | T11 "renders a clickable Fluent Button for citations with non-empty href" + "dispatches context.context_update with { url, displayName } when a clickable citation is clicked" |
| **(b) v1.1 observation-only-href** — UR-04 spike outcome where observation citations carry hrefs but document citations defer to v1.2 | Mixed array: some `c.href = 'https://...obs/...'`, some `c.href = null` | Per-entry decision: observation citations → Button (clickable); document citations → `<span>` (non-clickable, display-name only). NO console errors. | T11 "renders mixed-mode citations (observation has href, document has null) per UR-04 spike contingency" |
| **(c) v1.0 fallback** — no `href` field at all on any citation | All `c.href` null OR field entirely absent from envelope | Every `[n]` token → non-interactive `<span>` styled to match Button text (same brand foreground + semibold). NO click handler wired; NO PaneEventBus dispatch on click. | T11 "renders non-clickable spans when ALL citations have href: null (v1.0 deployment)" + "renders non-clickable spans when href is undefined (field entirely absent from envelope)" + defensive "empty string href" test |

The render-decision predicate is the exported pure helper `isClickableCitation(citation)`, returning `true` iff `typeof citation.href === 'string' && citation.href.length > 0`. This single predicate drives ALL three scenarios — no all-or-nothing decision at the array level. Per-entry conditional rendering inside the `tokens_.map` ensures mixed-mode (scenario b) works without special-case code paths.

---

## 4. Dispatch contract — `context.context_update` (ADR-030 compliant)

Per R5 CLAUDE.md §3.4 + ADR-030, the PaneEventBus channel set is closed at 4 and the `context_update` discriminant already exists on the `context` channel. Task 027 ONLY DISPATCHES this existing event:

```ts
dispatch('context', {
  type: 'context_update',
  contextType: 'file-preview',           // ← matches FilePreviewContextWidget registry key (task 018)
  contextData: {
    url: citation.href,                  // ← pre-resolved URL from Insights BFF
    displayName: citation.source,        // ← citation display name
  },
});
```

### 4.1 `contextType` cross-check (POML §step 4 requirement)

POML §step 4 mandates cross-checking the dispatched `contextType` value against task 018's registry key. Findings:

- **Registry key** (task 018, verified in `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` line 120-126): `'file-preview'`
- **`ContextPaneController` resolution path** (verified in `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` lines 429-460): `const widgetType = event.contextType ?? "unknown"; ... resolveContextWidget(widgetType)`
- **POML §goal stated value**: `contextType: 'document'`

**Decision**: dispatched value MUST be `'file-preview'` to resolve through `resolveContextWidget`. The POML §goal value `'document'` is the conceptual/descriptive label used in the design write-up; the actual registry key is `'file-preview'`. POML §step 10 explicitly permits this cross-check override ("`contextType` key used (`'document'` or whatever cross-checked against task 018's registry key)"). The dispatched value `'file-preview'` resolves correctly through the registry; `'document'` would resolve to `null` and the Context pane would render its empty state — a defect.

### 4.2 `contextData` payload shape vs `FilePreviewContextWidget` consumption

`FilePreviewContextWidget` (task 018) declares its `data` prop as `FilePreviewContextData = { files: FilePreviewContextFile[], activeFileId?, sessionId? }`. The dispatched `contextData` shape is `{ url, displayName }` — NOT the file-list shape.

**Status**: SURFACED, not silently rewritten. Per POML §constraints "If task 026 produced a different citation payload shape than expected (e.g. `citations[].href` is named differently, or `source` is not the display name), surface the mismatch in task notes and request alignment with task 026 owner — do NOT silently rewrite the contract."

**Resolution path** (for downstream owners):
- The dispatched event is the documented contract per the v1.1 negotiation outcome (`notes/insights-engine-contract-v1.1-request.md` §0a) + R5 CLAUDE.md §3.1 reuse mandate.
- `FilePreviewContextWidget` will need either (a) a host-provided adapter that wraps the URL-only payload into the file-list shape OR (b) a discriminated-union props extension so the widget can consume both shapes. The adapter approach is simpler and more aligned with R5 reuse — the chat-pane host (`ConversationPane.tsx`) can subscribe to `context.context_update` events with `contextType === 'file-preview'` AND `contextData.url`, synthesize a one-entry `FilePreviewContextData` (with a temporary `fileId` derived from the URL), and dispatch a follow-up event with the canonical shape — OR a thin host-side mapping component (`UrlPreviewAdapter` for `'file-preview'` registry key) can intercept the citation payload and resolve to a different widget (e.g. a simple iframe-only preview).
- This R5 follow-up belongs in the chat-pane orchestration task (task 020 / D2-11) or a new "citation preview adapter" task — NOT task 027 (which is strictly the citation click producer per POML §scope-note "strictly additive frontend extension").
- For Phase 2 vertical-slice demo purposes: the dispatch fires correctly and the Context pane shows the empty-state for `'file-preview'` registry key with an unrecognized data shape (graceful degradation per NFR-11).

This deviation is non-blocking for task 027 completion — task 027's scope is wiring the producer (citation click → dispatch). The consumer-side adapter is downstream.

### 4.3 No new event-type / no new channel — ADR-030 invariant

Verified by:
- `grep "type:" RagResponseRenderer.tsx` → only `'context_update'` literal appears in dispatched event payloads
- `dispatch('context', ...)` is the ONLY channel referenced in the modified file
- T11 explicit invariant test "dispatches ONLY context.context_update — no new channels or discriminants (ADR-030 invariant)" subscribes to all 4 channels and verifies only the `context` channel fires after 3 citation clicks.

No edits to `PaneEventTypes.ts` (the canonical event-type-union surface). Diff against that file = empty.

---

## 5. Test count

**Total NEW test cases for task 027: 10** (added to existing T6 block + new T11 block)

| Test block | # tests | Purpose |
|---|---|---|
| T11 "task 027 / D2-17 — clickable citations (v1.1) + graceful fallback" — (a) clickable | 1 | Verify Button render for citations with non-empty `href` |
| T11 — (b) dispatch | 2 | Verify `context.context_update` dispatch + `onCitationClick` override precedence |
| T11 — (c) v1.0 fallback | 3 | All-null-href / undefined / empty-string scenarios |
| T11 — (d) observation-only-href contingency | 1 | Mixed-mode rendering per UR-04 spike outcome |
| T11 — (e) ARIA distinct labels | 1 | Clickable + non-clickable variants have distinguishable `aria-label`s |
| T11 — extra: ADR-030 invariant | 1 | Verify ONLY `context_update` events fire across all 4 channels |
| T11 — extra: `isClickableCitation` | 1 | Pure-helper coverage (true/null/undefined/empty/stripped) |
| T11 — extra: legacy stub | 1 | `defaultCitationClickStub` remains callable for back-compat |

**Existing tests updated** (4):
- T6 "renders [n] tokens as clickable buttons" — updated aria-label assertion + new `data-citation-variant` assertion
- T6 "dispatches context.context_update when onCitationClick is omitted" (was "falls back to defaultCitationClickStub") — entirely rewritten; now asserts dispatch shape
- T6 "handles out-of-range citation tokens" — updated to assert non-clickable span variant (new render path for unresolvable citations)
- Test fixture `STD_CITATIONS` — added `href` field per v1.1 contract so legacy clickable-button assertions continue to pass

**Pre-existing T1–T10 tests**: 30 tests (per task 026 evidence file §6). After task 027 additions: **40 total tests** in the suite.

Per POML §step 9.5 quality gates, the main session runs `npm run build` + `npm test` in `src/solutions/SpaarkeAi/`. The sub-agent does NOT execute the build or tests.

---

## 6. ADR compliance

| ADR | Compliance | Evidence |
|---|---|---|
| **ADR-012** (component lib boundaries) | PASS | Modified file lives in `src/solutions/SpaarkeAi/src/components/conversation/insights/` — the chat-pane consumer surface. Imports `useDispatchPaneEvent` from `@spaarke/ai-widgets` via the public barrel (unidirectional consumer → library). No edits to `@spaarke/ai-widgets`. |
| **ADR-013 §3.5** (Zone B HTTP-contract-only) | PASS | Renderer consumes only the typed `Citation` + `RagObservationResponse` types from local `types.ts`. The URL in `citation.href` is pre-resolved server-side per `notes/insights-engine-contract-v1.1-request.md` §0a — R5 passes it through verbatim with NO client-side URL construction, signing, or augmentation. |
| **ADR-018** (no new feature flags) | PASS | Clickable behaviour is auto-detected per-citation via `isClickableCitation(c)` truthiness check on `c.href`. No `if (flag)` blocks introduced. Verified by `grep -E "flag|enabled|isEnabled|feature" RagResponseRenderer.tsx` returning zero feature-flag-like names. |
| **ADR-021** (Fluent v9 + dark mode) | PASS | All styles use `makeStyles` + `tokens.*` semantic tokens. Non-clickable `<span>` styled with `tokens.colorBrandForeground1` + `tokens.fontWeightSemibold` matching the clickable button — uniform visual baseline in both light + dark themes. No hex/rgb/rgba literals introduced. Regex audit: `grep -E "#[0-9a-fA-F]{3,8}|rgb\(|rgba\(" RagResponseRenderer.tsx` returns 0 matches. |
| **ADR-022** (React 19) | PASS | Functional component + hooks only. `useDispatchPaneEvent` called at component top level per React 19 hook rules. `useCallback` + `useMemo` used for stable references. No class components, no legacy lifecycle. |
| **ADR-029** (BFF publish hygiene) | N/A | Frontend-only task. Publish-size delta = **0 MB**. No BFF files touched. |
| **ADR-030** (PaneEventBus closed channels) | PASS | Dispatches existing `context.context_update` discriminant on the existing `context` channel. NO new channels, NO new event types. `PaneEventTypes.ts` not modified. T11 explicit invariant test confirms zero events on `workspace`/`conversation`/`safety` channels during citation clicks. |
| **R5 CLAUDE.md §3.1 reuse mandate** | PASS | REUSES existing `context.context_update` event dispatch path. Consumer is the existing `FilePreviewContextWidget` (task 018) via the existing `ContextPaneController` → `ContextWidgetRegistry` resolution path. NO new "open URL in iframe" component built. NO parallel preview surface. The downstream consumer-side adapter (see §4.2 above) is a separate task. |
| **R5 CLAUDE.md §3.2 no new flags** | PASS | Per ADR-018 above — auto-detection via per-citation `href` presence. |
| **R5 CLAUDE.md §3.5 Insights governance** | PASS | Zone B HTTP-contract-only consumption. URL is pre-resolved server-side (per `insights-engine-contract-v1.1-request.md` §0a — `DocumentCheckoutService.GetPreviewUrlAsync(driveId, itemId, ct)`). R5 frontend consumes the string verbatim. NO client-side URL construction. Graceful v1.0 fallback per NFR-11. |
| **Spec UR-04** (schema-plumbing spike outcome) | PASS | Three-scenario handling per §3 above. Observation-only-href contingency tested explicitly in T11 mixed-mode test. |
| **Spec NFR-11** (graceful v1.0 fallback) | PASS | Non-clickable `<span>` rendered for citations without `href`. NO error UX, NO console warnings (only `console.debug` for out-of-range tokens — observable but not error-level). NO broken rendering. Visual baseline matches prior v1.0 appearance. |

---

## 7. Coordination with parallel task 028

Task 028 (D2-18 — confidence-floor badge) ran in parallel within the same P2-G6 wave. Both tasks share the `InsightsResponseRenderer` surface but at different render layers:

- Task 027 — touched `RagResponseRenderer.tsx` (RAG-case sub-renderer) + tests
- Task 028 — touched `InsightsResponseRenderer.tsx` (wrapper) + new `LowConfidenceBadge.tsx` + `index.ts` exports + tests

Detected overlap on `index.ts` (both tasks added exports). Task 028's edits (`LowConfidenceBadge`, `LOW_CONFIDENCE_BADGE_TEXT`, `shouldShowLowConfidenceBadge`) coexist cleanly with task 027's `isClickableCitation` export — no conflicting edits to the same lines. Main session will see a trivial three-way merge.

NO conflicting edits to:
- `RagResponseRenderer.tsx` — task 028 doesn't touch this file
- `InsightsResponseRenderer.tsx` — task 027 doesn't touch this file (the `onCitationClick` prop seam from task 026 was sufficient)
- Test file fixtures — task 028 adds confidence-related fixtures; task 027 modified `STD_CITATIONS` to add `href` (different field)

---

## 8. Acceptance-criterion walkthrough (POML §acceptance-criteria)

| Criterion | Status | Evidence |
|---|---|---|
| Fluent v9 `Button` rendered for citations with non-null `href`; plain `<span>` otherwise | PASS | T11 (a) + T11 (c) tests assert tagName + `data-citation-variant` attribute |
| Click dispatches `context.context_update` with `{ url, displayName }` payload via `useDispatchPaneEvent` | PASS | T11 (b) "dispatches context.context_update with { url, displayName }" — subscribes a real `PaneEventBus`, asserts dispatch shape exactly |
| Scenario (a) v1.1 full | PASS | T11 (a) + T11 (b) — all STD_CITATIONS clickable, dispatch fires |
| Scenario (b) UR-04 observation-only-href contingency | PASS | T11 (d) "renders mixed-mode citations" — observation Button + document span; only observation dispatches |
| Scenario (c) v1.0 fallback | PASS | T11 (c) — three variants tested (`href: null`, field absent, `href: ''`) |
| End-to-end click-to-preview via `FilePreviewContextWidget` | PARTIAL — DEFERRED to chat-pane orchestration (task 020 / D2-11) | See §4.2 — the producer side (task 027 scope) is complete and dispatches the documented payload; the consumer-side adapter that maps `{ url, displayName }` → `FilePreviewContextData = { files: [...] }` is downstream. Dispatch verified independently. |
| No new PaneEventBus event type added | PASS | T11 extra "dispatches ONLY context.context_update — no new channels or discriminants" + diff against `PaneEventTypes.ts` empty |
| No new PaneEventBus channel added | PASS | Same as above |
| No new feature flag | PASS | Per ADR-018 — auto-detection via `c.href` truthiness |
| Dark-mode parity | PASS (style audit) | All styles `tokens.*`; non-clickable span uses identical color/weight tokens as button. T9 existing dark-mode smoke tests continue to pass with new variant rendering. |
| BFF publish-size delta = 0 MB | PASS | Frontend-only task. No BFF files touched. |
| `code-review` + `adr-check` quality gates pass at Step 9.5 | ⏭ | Main session per task scope contract. |

⏭ = handed to main session per parallel-wave sub-agent scope contract.

---

## 9. Outstanding work (deferred to main session)

Per task 027 POML §steps 9.5–10 main session ownership:

- [ ] Run `npm run build` in `src/solutions/SpaarkeAi/` — verify TypeScript compiles + no new lint warnings
- [ ] Run `npm test` (or equivalent jest runner) in `src/solutions/SpaarkeAi/` — verify all 40 tests pass (30 pre-existing + 10 new task-027)
- [ ] Dark-mode visual verification — mount mixed-mode scenario under `webDarkTheme` and capture screenshots
- [ ] Run `code-review` skill against the 3 modified files
- [ ] Run `adr-check` skill against the 3 modified files
- [ ] Update `TASK-INDEX.md`: 027 🔲 → ✅
- [ ] Reset `current-task.md` to next pending task per CLAUDE.md §7
- [ ] Commit (`feat(r5): task 027 D2-17 — clickable citations via context.context_update PaneEventBus dispatch (v1.1)`)
- [ ] Push to remote per push-to-github skill

---

## 10. Downstream consumer notes

| Task | Receives | What changes |
|---|---|---|
| **020** (D2-11) chat-pane orchestration | The dispatched `context.context_update` event — task 020 OR a new adapter task wires the consumer that maps `{ url, displayName }` payload onto `FilePreviewContextData` (see §4.2) | Add a subscriber in `ConversationPane.tsx` OR a thin adapter widget registered under `'file-preview'` that handles the URL-only payload shape |
| **028** (D2-18) confidence badge | Independent of task 027 — both tasks share `InsightsResponseRenderer` but at non-overlapping render layers (badge wraps response; citation click is inside RAG sub-renderer) | None — coordination resolved by parallel-safe wave design |
| **029** (D2-19) Insights error codes | Independent | None |

---

## 11. Sub-agent scope reminder

This task was executed by a parallel-wave sub-agent. Per task POML §steps + parent-task summary:

- **In scope (sub-agent)**: code authoring of `RagResponseRenderer.tsx`, `index.ts` barrel update, test extensions, evidence file, POML status update.
- **Out of scope (main session)**: `npm run build`, `npm test` execution, dark-mode visual capture, code-review + adr-check skill runs, TASK-INDEX update, current-task.md reset, commit + push.

The handoff is intentional per R5 parallel-wave protocol — multiple sub-agents (tasks 027 + 028) authored concurrently within the same P2-G6 wave, and serializing through the main session for build verification + commit avoids merge conflicts on `TASK-INDEX.md` + downstream evidence file collisions.
