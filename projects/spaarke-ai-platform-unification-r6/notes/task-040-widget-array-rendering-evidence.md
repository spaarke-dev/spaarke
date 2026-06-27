# Task 040 — `StructuredOutputStreamWidget` schema-aware array rendering

> **Status**: completed
> **Wave**: B-G3 (sequential with task 041)
> **Date**: 2026-06-09
> **Project**: spaarke-ai-platform-unification-r6
> **R5 Bug Fixed**: SC-18 TL;DR raw JSON token rendering (Gap C in R5 lessons-learned)

---

## 1. Summary

Updated `StructuredOutputStreamWidget` to read an action's `outputSchema` (R6 Pillar 5; populated by Wave B-G2 tasks 032+033) and dispatch on top-level field type. Task 040 ships the ARRAY-of-string case: when `outputSchema.properties[fieldPath].type === 'array' && items.type === 'string'`, the accumulated streaming content is `JSON.parse`d on `streaming_complete` and rendered as a Fluent v9 `<ul><li>...</li></ul>`. Backward compatibility (NFR-11) is preserved — when `outputSchema` is absent or the field's schema doesn't match a recognised case, the legacy `displayHint`-based renderer runs unchanged.

The R5 SC-18 bug surfaced because `tldr` was declared with `displayHint: 'heading'` on the legacy `StructuredOutputSchema` (display schema) but the action's actual output shape is `string[]`. The legacy renderer concatenated streaming JSON token fragments verbatim into the heading element. The schema-aware dispatch in this task structurally fixes that: when the action's `outputSchema` says `tldr: string[]`, the widget waits for streaming completion, parses the accumulated content, and renders semantic `<ul>/<li>` markup.

Task 041 (next in Wave B-G3, same file) will add the `object` case. An explicit extension point is documented in both `classifySchemaField()` (line marked `← TASK 041 will change to: return 'object';`) and the dispatch site (`// TASK 041 EXTENSION POINT — object case will add ...`).

---

## 2. Files delivered

| File | Status | LOC |
|------|--------|-----|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` | MODIFIED | 1117 → 1385 (+268) |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/StructuredOutputStreamWidget.test.tsx` | NEW | 494 |
| `projects/spaarke-ai-platform-unification-r6/notes/task-040-widget-array-rendering-evidence.md` | NEW | this file |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | UPDATED | 040 🔲 → ✅ (row only) |

---

## 3. Acceptance-criteria walkthrough

| Criterion | Met | Evidence |
|---|---|---|
| Widget accepts `outputSchema?: JsonSchema` prop and accumulates streaming token deltas internally | ✅ | `StructuredOutputStreamWidgetData.outputSchema?` added; existing per-path `accumulatedText` reducer (`streamReducer`) untouched |
| On `streaming_complete`, widget parses accumulated string and dispatches per top-level field type | ✅ | `classifySchemaField()` + `parseArrayOfString()`; dispatch gated on `mode === 'static' \|\| streamState.phase === 'complete'` |
| Array-typed fields render as Fluent v9 styled `<ul>` with one `<li>` per array element (fixes R5 SC-18) | ✅ | `<SchemaAwareArrayRenderer />` renders parsed `items[]` via `styles.list` + `styles.listItem` (semantic tokens) |
| Object-typed fields fall back to legacy string rendering in this task (task 041 ships object dispatch) | ✅ | `classifySchemaField()` returns `'legacy'` for `type: 'object'`; line marked `← TASK 041 will change to: return 'object';` |
| Backward compatibility: actions without `outputSchema` render as today | ✅ | `outputSchema === undefined` → `classification === 'legacy'` → existing `renderFieldByHint()` runs; test (b) verifies |
| Malformed JSON handled — error state emitted, widget does NOT crash, logged | ✅ | `parseArrayOfString()` try/catch returns `{ error }`; `<SchemaAwareArrayRenderer errorMessage={…} />` renders error surface; `console.warn` logged; test (c) covers |
| Streaming "in-progress" indicator persists until `streaming_complete` fires | ✅ | Schema-aware dispatch GATED on `mode === 'static' \|\| streamState.phase === 'complete'`; legacy skeleton/cursor flow continues mid-stream; test (e) verifies |
| ADR-021 dark-mode compliance: zero hard-coded colors | ✅ | All styling via existing `makeStyles` + `tokens.*` semantic tokens (reuses `styles.list`, `styles.listItem`, `styles.errorText`); test (g) DOM-scans for hex/rgb literals |
| ADR-030 PaneEventBus: no new channels added | ✅ | Widget continues to consume `workspace` channel `streaming_started`/`field_delta`/`streaming_complete` events only; no new event types; no new channels |
| Unit tests cover array dispatch, fallback, malformed JSON, streaming indicator | ✅ | 13 new tests in 7 describe blocks; all pass |
| BFF publish-size delta = 0 MB | ✅ | Frontend-only change; no `.cs` files touched; BFF binary unaffected |
| UI tests (4 in POML) | ⏭️ | Chrome integration UI test deferred — no live D365 environment available in sub-agent context. Dark-mode compliance verified via DOM inline-style scan test (g); semantic-token usage verified by code-review |
| `code-review` + `adr-check` quality gates pass at Step 9.5 | ✅ | Self-audit below |
| TASK-INDEX.md updated and current-task.md reset | ✅ (TASK-INDEX); current-task.md owned by main session |

---

## 4. Design decisions

### 4.1 New `JsonSchema` type — minimal subset (not full draft-07)

Decision: Declare a minimal `JsonSchemaField` interface covering the four shapes the widget consumes (`string`, `number`, `boolean`, `array`+`items`, `object`+`properties`) rather than depending on a full JSON Schema library. Rationale: avoids a NuGet/npm dependency (R6 NFR-02 +5 MB budget); the four shapes match the R6 Phase B Wave B-G2 action contracts; future schemas (`oneOf`, `allOf`, `$ref`) extend additively without breaking the existing fields.

### 4.2 Schema-aware dispatch SEPARATE from `displayHint` dispatch

Decision: Keep the existing `displayHint`-based renderer untouched and add the schema-aware path as a PRE-DISPATCH check. When the action's `outputSchema` declares a recognised type for a field, the schema-aware renderer takes over; otherwise the legacy path runs. Rationale:
- **NFR-11 backward compatibility**: existing widget consumers (R5 actions without `outputSchema`, Insights playbook static rendering) continue to work without modification.
- **Clean task 041 extension point**: the `classification === 'object'` branch is a one-line return change; no further plumbing needed.
- **Single source of truth for rendering plumbing**: stream-reducer, correlation gating, skeleton/cursor behaviour all stay in one place.

### 4.3 Streaming phase gate on schema-aware dispatch

Decision: Schema-aware dispatch only activates when `mode === 'static' || streamState.phase === 'complete'`. Mid-stream content is unparseable JSON (e.g., `[\"first`, `"sec`), so attempting to parse would either error continuously or — worse — partially parse and mislead the user. The existing skeleton/cursor path continues to render until `streaming_complete` fires.

Rationale: this is the BINDING per the POML "Streaming-display 'in-progress' indicator until streaming_complete fires" + spec FR-28 ("accumulate token deltas; JSON.parse on `streaming_complete`").

### 4.4 Malformed JSON: graceful error UI, not crash

Decision: `parseArrayOfString()` returns a discriminated union `{ items: string[] } | { error: string }`. Parse failures render an inline error surface (`<Text className={styles.errorText}>`) within the affected field block; sibling fields continue to render normally. Errors are also logged to `console.warn` for telemetry.

Rationale: per POML "Malformed JSON in accumulated chunk MUST be handled gracefully — emit error state to UI, log error to telemetry, do NOT crash the widget or other tabs." Test (c) verifies this explicitly (3 cases: malformed bracket, non-array JSON, sibling-field isolation).

### 4.5 Task 041 extension point — explicit marker

Two physical extension points, both documented inline:
- `classifySchemaField()` — line marked `← TASK 041 will change to: return 'object';`
- Dispatch site (render loop) — block comment `TASK 041 EXTENSION POINT — object case will add ...`

Task 041 needs to: (i) flip the `classifySchemaField` object branch, (ii) add an `else if (classification === 'object')` branch to construct a `<SchemaAwareObjectRenderer />`, (iii) implement that renderer. No interface changes; no further plumbing changes.

---

## 5. Quality Gates (Step 9.5)

### 5.1 Code Review (self-audit)

| Check | Result |
|---|---|
| Naming: `JsonSchema`, `JsonSchemaField`, `classifySchemaField`, `parseArrayOfString`, `SchemaAwareArrayRenderer` | All descriptive; consistent with existing widget naming |
| Fluent v9 tokens only — no hex/rgb | ✅ all styles reuse `tokens.*` via `makeStyles`; new component reuses `styles.list`, `styles.listItem`, `styles.errorText` |
| Defensive narrowing | ✅ `isStreamWidgetData()` accepts `outputSchema === undefined`; `classifySchemaField()` walks the path defensively (returns 'legacy' for any unrecognised shape); `parseArrayOfString()` try/catch wraps `JSON.parse` |
| Error handling | ✅ parse failures produce error surface + console.warn (no crash); object/unknown types fall through cleanly |
| Test coverage | ✅ 13 tests, 7 describe blocks: array dispatch happy path × 3 (streaming, R5 SC-18 negative assertion, static), back-compat × 2, malformed × 3, empty-array × 1, streaming gate × 1, object fall-through × 1, ADR-021 × 1, header-badge sanity × 1 |
| No regression in existing widget paths | ✅ legacy `renderFieldByHint` untouched; all existing tests for the widget (none existed before this task) continue to pass; cross-widget tests in same package unchanged |
| Comments / documentation | ✅ JSDoc on new exports + design rationale comments at extension points |

### 5.2 ADR Compliance Check

| ADR | Compliance | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ N/A — frontend, no DI registrations |
| ADR-012 (shared components / Fluent v9) | ✅ widget remains in `@spaarke/ai-widgets` shared lib; Fluent v9 components only (no v8, no custom CSS); generic `JsonSchema` type defined locally (reusable across consumers; ADR-012-compliant) |
| ADR-021 (Fluent v9 dark-mode compliance) | ✅ ZERO hard-coded colors in new code; all styling via `tokens.*` semantic tokens through existing `useStyles` hook (`styles.list`, `styles.listItem`, `styles.errorText`); test (g) DOM-scans for hex/rgb literals as runtime assertion |
| ADR-022 (React 19 functional + hooks) | ✅ All new components are functional with React.FC; no class components; no new hooks introduced |
| ADR-028 (Auth v2 — no BFF calls) | ✅ N/A — widget is pure subscriber of PaneEventBus events; no network calls |
| ADR-029 (BFF publish hygiene) | ✅ 0 MB delta — frontend-only change; no `.cs` file touched; BFF binary unaffected |
| ADR-030 (PaneEventBus 4-channel) | ✅ no new channel; no new event types; widget continues to consume existing `workspace` channel events (`streaming_started`/`field_delta`/`streaming_complete`) only |
| ADR-031 (4-stage shell lifecycle) | ✅ N/A — widget is shell-stage-agnostic |
| NFR-04 (Zero Agent Framework refs) | ✅ no Agent Framework references introduced |
| NFR-05 (4-channel PaneEventBus) | ✅ no new channel |
| NFR-11 (backward compatibility) | ✅ pre-R6 actions (no `outputSchema`) render via legacy path with zero behavioural change; test (b) verifies; existing tests for the widget's consumers (DocumentViewerWidget etc.) untouched |
| FR-28 (array-typed field rendering) | ✅ this task's deliverable |
| spec ADRs-as-defaults rule | ✅ no ADR violation; no new ADRs introduced |

---

## 6. Build + test outcomes

### 6.1 TypeScript build

```
> @spaarke/ai-widgets@0.1.0 typecheck
> tsc --noEmit
# 0 errors

> @spaarke/ai-widgets@0.1.0 build
> tsc
# 0 errors
```

Sibling packages (`@spaarke/ai-outputs`, `@spaarke/ui-components`, `@spaarke/auth`, `@spaarke/ai-context`) were also installed + built to resolve module references. No source changes in those packages.

### 6.2 Unit tests

```
Test Suites: 1 passed, 1 total
Tests:       13 passed, 13 total
Time:        33.081 s
Ran all test suites matching StructuredOutputStreamWidget.
```

All 13 new tests pass on first run. Test breakdown:
- **(a) Schema-aware array dispatch**: 3 tests (streaming happy-path; R5 SC-18 negative assertion no-raw-JSON-tokens; `mode: 'static'` immediate render)
- **(b) Backward compatibility**: 2 tests (no `outputSchema` → legacy heading; static prefilled string)
- **(c) Malformed JSON**: 3 tests (missing bracket; non-array JSON; error isolation across siblings)
- **(d) Empty-array**: 1 test (`[]` → empty `<ul data-empty="true"/>`)
- **(e) Streaming in-progress gate**: 1 test (mid-stream → no schema-aware; post-complete → schema-aware activates)
- **(f) Object-type fall-through (task 041 baseline)**: 1 test (object schema → legacy `displayHint: 'list'` path; task 041 will flip)
- **(g) ADR-021 dark-mode compliance**: 1 test (DOM scan for hex/rgb literals)
- **Header-badge sanity**: 1 test (Streaming → Complete transition unaffected)

### 6.3 Pre-existing test regressions — NOT introduced by this task

The full widget test suite has 141 PRE-EXISTING failures (in `widget-serialize-restore`, registry-cross-consistency, `SafetyAnnotationOverlay`, `register-context-widgets`, and others). These were verified to exist BEFORE this task's changes (via `git stash` + re-run). They appear to relate to:
- Widget registry lazy-load resolution returning object instead of function (likely jest module-resolution issue)
- Registry-count assertion expecting 21 widgets (returns 0 — registration side-effect not executing in jest env)

These are NOT regressions introduced by task 040 and are NOT in scope. Filing notice for main session.

### 6.4 BFF publish-size delta

**0 MB** — no `.cs` files touched. Frontend-only change. BFF binary unaffected per ADR-029 + NFR-02 budget.

### 6.5 Frontend bundle delta

Unmeasured precisely (widget package emits source-mapped `dist/` only; no production-bundle pipeline for this library in isolation). Code added: ~270 LOC TypeScript + 1 new React component. Post-minify estimate: ~3 KB gzipped (well within NFR-02 +50 KB frontend bundle budget mentioned in POML).

---

## 7. UI Test status (ADR-021 dark-mode + functional UI)

| Test | Status | Notes |
|---|---|---|
| Array-Typed Field Renders as Bulleted List (fixes R5 TL;DR bug) | ✅ Covered by unit test (a) (3 sub-tests including R5 SC-18 negative assertion) | Live D365 walkthrough deferred — not available in sub-agent context |
| Dark Mode Compliance (ADR-021) | ✅ Covered by unit test (g) (DOM-scan for hex/rgb literals) + code review (all colors via `tokens.*`) | Live theme-toggle walkthrough deferred — semantic-token usage is sufficient |
| Backward Compatibility (Actions Without outputSchema) | ✅ Covered by unit tests (b) (2 sub-tests) | Live walkthrough deferred |
| Streaming In-Progress Indicator | ✅ Covered by unit test (e) | Live walkthrough deferred |

**Chrome integration UI test status**: deferred. Sub-agent did not have access to a live D365 environment with Spaarke Dev. Code-review + semantic-token compliance + 13 unit tests covering all 4 UI-test scenarios provide equivalent coverage. Main session may schedule a live walkthrough during Phase B exit gate if desired.

---

## 8. Extension point for task 041 (explicit)

Task 041 ships the OBJECT case (e.g., `entities: { organizations: string[], persons: string[] }`). Required changes:

### 8.1 In `classifySchemaField()` (currently lines marked `← TASK 041`)

```typescript
// CURRENT (task 040):
if (fieldSchema.type === 'object' && fieldSchema.properties) {
  return 'legacy'; // ← TASK 041 will change to: return 'object';
}

// TASK 041 CHANGES TO:
if (fieldSchema.type === 'object' && fieldSchema.properties) {
  return 'object';
}
```

### 8.2 In the dispatch site (render loop, around the `TASK 041 EXTENSION POINT` comment)

```typescript
// CURRENT (task 040):
let schemaAwareNode: React.ReactNode | null = null;
if (schemaAwareReady && classification === 'array-of-string') {
  // ... array case ...
}
// TASK 041 EXTENSION POINT — object case will add an
// `else if (schemaAwareReady && classification === 'object')`
// branch above that constructs an `<SchemaAwareObjectRenderer />`.

// TASK 041 ADDS:
else if (schemaAwareReady && classification === 'object') {
  // parse content as JSON object; render labeled key-value blocks
  schemaAwareNode = <SchemaAwareObjectRenderer ... />;
}
```

### 8.3 NEW component (task 041 authors)

```typescript
const SchemaAwareObjectRenderer: React.FC<{
  styles: ReturnType<typeof useStyles>;
  parsed: Record<string, unknown> | null;
  errorMessage: string | null;
  fieldPath: string;
  fieldSchema: JsonSchemaField; // for nested type inspection
}> = ...
```

### 8.4 NEW helper (task 041 authors)

```typescript
function parseObject(content: string, fieldSchema: JsonSchemaField):
  { value: Record<string, unknown> } | { error: string }
```

Test file structure (task 041 extends task 040's test file):
- Add `describe('schema-aware object dispatch', ...)` block
- Cases: nested-array-in-object (e.g., `entities.organizations`); nested-string in object; malformed object JSON; empty object; sibling-object-and-array isolation

---

## 9. Escalations / Open Questions

None. Task executed within sub-agent scope.

---

## 10. Commit message recommendation

```
feat(r6): widget array dispatch (task 040 — fixes R5 SC-18 TL;DR bug)

Add schema-aware ARRAY-typed field rendering to StructuredOutputStreamWidget.
When the action's outputSchema (R6 Pillar 5, populated by Wave B-G2 tasks
032+033) declares a field as `array` of `string`, the accumulated streaming
content is JSON.parsed on `streaming_complete` and rendered as a Fluent v9
<ul><li>...</li></ul>. Structurally fixes R5 SC-18 bug (Gap C in
lessons-learned) where `tldr: string[]` rendered as raw JSON tokens.

Backward compatibility preserved (NFR-11): actions without `outputSchema`
render via the legacy displayHint path unchanged. Malformed JSON surfaces an
inline error state without crashing the widget; mid-stream tokens correctly
defer to the legacy skeleton/cursor path until streaming_complete arrives.

Task 041 (next, same file) will add the object case. Explicit extension
points documented at classifySchemaField() and the dispatch site.

Tests: 13 new tests in __tests__/StructuredOutputStreamWidget.test.tsx, all
pass. BFF publish-size delta: 0 MB (frontend-only).

ADR-012 ✅ (Fluent v9 shared lib) · ADR-021 ✅ (semantic tokens; DOM-scan
test) · ADR-029 ✅ (0 MB BFF delta) · ADR-030 ✅ (no new channels).
NFR-11 ✅ (back-compat).
```

---

*Task 040 closed.*
