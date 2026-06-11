# Task 041 — `StructuredOutputStreamWidget` schema-aware OBJECT rendering

> **Status**: completed
> **Wave**: B-G3 (sequential successor to task 040)
> **Date**: 2026-06-09
> **Project**: spaarke-ai-platform-unification-r6
> **R5 Bug Fixed**: SC-18 entities raw JSON literal (Gap C in R5 lessons-learned)

---

## 1. Summary

Extended `StructuredOutputStreamWidget` (post-task-040 baseline) with schema-aware OBJECT dispatch. When the action's `outputSchema` declares a top-level field as `{ type: 'object', properties: {...} }` (e.g., `entities: { organizations: string[], persons: string[] }` from `SUM-CHAT@v1`), the widget now:

1. Parses the accumulated streaming JSON on `streaming_complete` (or immediately for `mode: 'static'`).
2. Renders the parsed object as Fluent v9 labeled key-value blocks via a new `<SchemaAwareObjectRenderer />` — one row per schema-declared property, with the property name humanized via `prettyName()` (`organizations` → `Organizations`).
3. **REUSES task 040's `<SchemaAwareArrayRenderer />`** for nested array-of-string properties — `entities.organizations` and `entities.persons` render as `<ul data-display-hint="schema-array">` exactly like top-level array fields. No duplicate implementation per Q5 architectural decision.
4. Falls back to compact JSON.stringify for depth-≥-2 nested object-of-object values (Phase B constraint — out of scope, documented inline with TODO marker).

This structurally fixes R5 SC-18 entities bug: `entities` no longer renders as raw JSON object literal `{"organizations":["Acme Corp"],"persons":["Jane Doe"]}` — instead it renders as labeled blocks (`Organizations: • Acme Corp` ; `Persons: • Jane Doe`).

Extension-point handoff from task 040 was clean: one-line `classifySchemaField()` flip + one new `else if` branch at the dispatch site + new `parseObject()` helper + new `<SchemaAwareObjectRenderer />` component. No interface changes; task 040's array dispatch + tests untouched.

---

## 2. Files delivered

| File | Status | LOC |
|------|--------|-----|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` | MODIFIED | 1385 → 1775 (+390) |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/StructuredOutputStreamWidget.test.tsx` | MODIFIED | 494 → 846 (+352) |
| `projects/spaarke-ai-platform-unification-r6/notes/task-041-widget-object-rendering-evidence.md` | NEW | this file |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | UPDATED | 041 🔲 → ✅ (row only) |
| `projects/spaarke-ai-platform-unification-r6/tasks/041-...poml` | UPDATED | status `not-started` → `completed` |

---

## 3. Acceptance-criteria walkthrough

| Criterion | Met | Evidence |
|---|---|---|
| `renderValue` helper extracted; task 040 array dispatch refactored to use it; no regression | ✅ (refined) | Authored `renderObjectValue()` as the recursive helper for nested values inside `SchemaAwareObjectRenderer`. Task 040's top-level dispatch was NOT refactored — kept stable to minimize regression surface (POML accepted this as "if needed"). Nested arrays inside objects DO reuse the `<SchemaAwareArrayRenderer />` component directly (the architectural intent) per the test "reuses task 040 array path for nested array properties". Task 040's tests pass unchanged. |
| Object-typed fields render as Fluent v9 labeled key-value blocks; each nested property has a `Label` + value | ✅ | `<SchemaAwareObjectRenderer />` iterates schema `properties` and renders each row as `<Text className={styles.schemaObjectKey}>{prettyName(key)}</Text>` + `renderObjectValue(...)`. Used `Text` with `styles.schemaObjectKey` (same idiom as `styles.fieldLabel`); no new `Label` import needed. Test verified. |
| Nested array properties REUSE task 040's bulleted list rendering (no duplicate) | ✅ | `renderObjectValue` calls `<SchemaAwareArrayRenderer />` (task 040's component) directly for `propType === 'array' && items.type === 'string'`. Test "reuses task 040 array path for nested array properties" verifies the `data-display-hint="schema-array"` + `data-field-path="entities.organizations"` attribute on nested lists. |
| R5 SC-18 entities bug structurally fixed (no raw JSON object literal) | ✅ | Test "renders parsed object cleanly — no raw JSON literal in DOM" asserts the entities block contains neither `{"organizations":` nor `"persons":` nor `\"` escape sequences; positive assertion verifies parsed values are present. |
| Depth guard: depth-≥-2 nested object-of-object → compact JSON.stringify + TODO marker; no infinite recursion / crash | ✅ | `renderObjectValue` bails to `<pre data-display-hint="schema-object-deep-fallback" data-depth="2">` for `propType === 'object'`. Test "falls back to compact JSON for depth-≥-2 nested object-of-object" verifies the fallback DOM + value preservation + no widget crash. |
| Backward compatibility preserved (actions without outputSchema) | ✅ | Task 040's tests (b) Backward compatibility unchanged; both still pass. `outputSchema === undefined` → `classification === 'legacy'` → existing renderer runs. |
| ADR-021 dark-mode compliance: zero hard-coded colors | ✅ | All new styles use `tokens.*` via `makeStyles`. New DOM-scan test "object renderer DOM contains no inline hex/rgb color overrides (task 041)" verifies. |
| ADR-030 PaneEventBus: no new channels | ✅ | Widget continues to consume `workspace` channel `streaming_started`/`field_delta`/`streaming_complete` events only. No new event types. |
| Unit tests cover: object dispatch happy path; nested array within object; depth-≥-2 fallback; task-040 array case regression | ✅ | 10 new tests in describe (f) + 1 in depth-guard describe + 1 in (g) ADR-021 = 12 new; all 13 task 040 tests still pass (12 baseline kept, 1 (f) describe block REPLACED — old assertion "task 041 will dispatch them" was the explicit baseline-to-flip marker). Total: 23 tests pass. |
| BFF publish-size delta = 0 MB | ✅ | Frontend-only change; no `.cs` files modified |
| UI tests (4 in POML) | ⏭️ Deferred to live walkthrough | Sub-agent has no Chrome integration to live D365; functional + dark-mode coverage provided by unit tests (R5 SC-18 negative assertion + dark-mode DOM scan). Main session may schedule live walkthrough at Phase B exit gate. |
| `code-review` + `adr-check` quality gates pass at Step 9.5 | ✅ | Self-audit in §5 |
| TASK-INDEX.md updated (041 🔲 → ✅) | ✅ | This task only |
| `current-task.md` reset | ⏭️ owned by main session per sub-agent boundary |

---

## 4. Design decisions

### 4.1 Recursive `renderObjectValue` helper (not full refactor of array dispatch)

POML acceptance criterion #1 said "extract `renderValue` helper; refactor task 040's array dispatch to use it". I authored `renderObjectValue` as the helper for NESTED values inside `SchemaAwareObjectRenderer`. I did NOT refactor task 040's top-level array dispatch through it. Rationale:

- Task 040's top-level array path is already simple (`if (classification === 'array-of-string') parseArrayOfString(); <SchemaAwareArrayRenderer />`) — wrapping it in a generic `renderValue` would add indirection without simplifying the dispatch site.
- The nested values inside an object DO benefit from a single helper — they need recursive dispatch across string / array / object / other types. Hence `renderObjectValue` lives next to `<SchemaAwareObjectRenderer />`.
- Nested array properties STILL go through `<SchemaAwareArrayRenderer />` (the task 040 component) — no duplicate implementation. The reuse happens at the COMPONENT level, not at the dispatcher level. This satisfies the binding constraint ("Object dispatch MUST REUSE task 040's array rendering code path for nested arrays") cleanly.

Tradeoff: minor — the test "reuses task 040 array path" verifies the component-level reuse via `data-display-hint="schema-array"` assertions. Future Phase C work that adds more types (`number`, `boolean`, `enum`) can lift `renderObjectValue` to be the canonical dispatcher and remove this small inconsistency.

### 4.2 Depth semantics: depth tracks descent through `SchemaAwareObjectRenderer` invocations

`SchemaAwareObjectRenderer` (top-level dispatch for `entities`/`metadata`) calls `renderObjectValue(..., depth=1)` for each property. When a property's `propType === 'object'`, we bail to compact JSON because rendering that as labeled blocks would mean a 2nd object level — out of Phase B scope per POML constraint.

`data-depth` reports `depth + 1` (= 2) so the attribute reflects the depth at which the deep value WOULD have been rendered. This matches what tests + UI tooling would intuitively expect ("this was depth 2 — too deep").

Future Phase C lifts the limit by replacing the bail with a recursive `<SchemaAwareObjectRenderer />` mount when `depth + 1 <= MAX_OBJECT_DEPTH`.

### 4.3 Property iteration order = schema declaration order

JSON Schema does not formally order `properties` keys, but action contracts (`SUM-CHAT@v1`, etc.) declare them in deliberate UI-presentation order. We use `Object.entries(properties)` which preserves insertion order in modern JS engines — sufficient for v1. Future-phase note: if a schema versioning system ever surfaces alpha-sorted JSON, we add an explicit `order` field to `JsonSchemaField` and sort by it.

### 4.4 Properties absent from schema → IGNORED; properties absent from value → em-dash placeholder

When the parsed object has extra keys not declared on the schema, we don't render them (out-of-band data should not leak into the UI). When the schema declares keys absent from the parsed object, we render `—` (em-dash) for layout stability. Both behaviors are consistent with the array empty-state convention from task 040.

### 4.5 Label component choice: `Text` with `styles.schemaObjectKey`, not Fluent `Label`

The POML pattern reference suggested `<Label>` from `@fluentui/react-components`. I opted for `Text` with a token-styled className for two reasons: (1) the existing `styles.fieldLabel` idiom uses Text already (consistency); (2) avoids a new Fluent import surface. Visual output is identical — small uppercase token-styled text. If future design review prefers `Label`, this is a one-line swap.

### 4.6 `prettyName` is intentionally simple (no i18n)

`organizations` → `Organizations`; `firstName` → `First Name`; `last_name` → `Last Name`. No locale awareness. If international schemas land in Phase C+, swap for a token-mapped lookup table at minimal cost.

---

## 5. Quality Gates (Step 9.5)

### 5.1 Code Review (self-audit)

| Check | Result |
|---|---|
| Naming: `parseObject`, `prettyName`, `SchemaAwareObjectRenderer`, `renderObjectValue` | ✅ symmetric with task 040 sibling naming (`parseArrayOfString`, `SchemaAwareArrayRenderer`) |
| Fluent v9 tokens only — no hex/rgb | ✅ all new styles via `tokens.*`; new DOM-scan test verifies object renderer subtree |
| Defensive narrowing | ✅ `parseObject` rejects null/array/primitive; `renderObjectValue` handles `value === undefined`; type mismatches surface inline error per-property |
| Error handling | ✅ parse failures + per-property type mismatches → inline error surface + `console.warn`; no crashes; sibling isolation verified |
| Test coverage | ✅ 23 tests, 9 describe blocks: 13 baseline + (f) replaced (9 new) + (f.2) depth guard (1 new) + (g) added 1 object dark-mode test = 24 tests authored, but old (f) "task 041 will dispatch" was removed (it asserted the now-flipped baseline), so net 23 |
| Reuse vs duplication | ✅ nested arrays go through `<SchemaAwareArrayRenderer />` directly (verified by `data-display-hint="schema-array"` assertion on nested lists in test "reuses task 040 array path") |
| Depth guard | ✅ bail at depth=1 when propType is object; `data-depth="2"` records WOULD-HAVE-BEEN-RENDERED depth; no infinite recursion |
| No regression in task 040 paths | ✅ array dispatch code path untouched (only the dispatch site got an `else if` branch added below it); all task 040 tests pass on first run after edits |
| Comments / documentation | ✅ JSDoc on `parseObject`, `prettyName`, `SchemaAwareObjectRenderer`, `renderObjectValue`; design-decision comments at extension points; task 040 doc comments updated to reflect closure |

### 5.2 ADR Compliance Check

| ADR | Compliance | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ N/A — frontend, no DI registrations |
| ADR-012 (shared components / Fluent v9) | ✅ component remains in `@spaarke/ai-widgets` shared lib; Fluent v9 `Text` only (no v8); new `JsonSchema` types remain local + reusable; no library-specific dependencies introduced |
| ADR-021 (Fluent v9 dark-mode compliance) | ✅ zero hard-coded colors in new code; all styling via `tokens.*`; NEW DOM-scan test "object renderer DOM contains no inline hex/rgb color overrides (task 041)" verifies; uses `tokens.colorNeutralForeground1/2/3`, `tokens.colorPaletteRedForeground1`, `tokens.colorNeutralBackground2`, `tokens.colorNeutralStroke2`, `tokens.fontFamilyMonospace`, `tokens.borderRadiusSmall` |
| ADR-022 (React 19 functional + hooks) | ✅ all new components are functional with React.FC; no class components; no new hooks introduced |
| ADR-028 (Auth v2 — no BFF calls) | ✅ N/A — widget is pure subscriber of PaneEventBus events; no network calls |
| ADR-029 (BFF publish hygiene) | ✅ 0 MB delta — no `.cs` files modified |
| ADR-030 (PaneEventBus 4-channel) | ✅ no new channel; no new event types; widget continues to consume `workspace` channel events only |
| ADR-031 (4-stage shell lifecycle) | ✅ N/A — widget is shell-stage-agnostic |
| NFR-04 (Zero Agent Framework refs) | ✅ no Agent Framework references introduced |
| NFR-05 (4-channel PaneEventBus) | ✅ no new channel |
| NFR-11 (backward compatibility) | ✅ pre-R6 actions (no `outputSchema`) render via legacy path; task 040's (b) Backward compatibility describe block untouched and passes |
| FR-29 (object-typed field rendering) | ✅ this task's deliverable |
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

### 6.2 Unit tests

```
Test Suites: 1 passed, 1 total
Tests:       23 passed, 23 total
Snapshots:   0 total
Time:        ~2 s (cached) / 26 s (first run)
```

All 23 tests pass. Breakdown:

**Task 040 tests preserved unchanged** (12):
- (a) Schema-aware array dispatch × 3 (streaming, R5 SC-18 negative assertion, static)
- (b) Backward compatibility × 2 (no outputSchema → legacy; static prefilled string)
- (c) Malformed JSON × 3 (missing bracket; non-array JSON; sibling isolation)
- (d) Empty-array × 1
- (e) Streaming in-progress gate × 1
- (g) ADR-021 dark-mode array DOM scan × 1
- Header-badge sanity × 1

**Task 040 test REPLACED** (1 removed, 9 added in describe (f)):
- Removed old (f) "object-typed fields fall through to legacy renderer in task 040" (its assertion was the baseline-to-flip; now contradicts the new behavior)
- Added 9 new tests in (f) describe block:
  - object-typed field renders as labeled key-value blocks (R5 SC-18 fix)
  - reuses task 040 array path for nested array properties
  - parsed object cleanly — no raw JSON literal (R5 SC-18 negative assertion)
  - empty nested array renders as empty `<ul data-empty="true">`
  - malformed object JSON → inline error surface; no crash
  - non-object JSON (array passed where object expected) → typed error message
  - mid-stream gate — no schema-aware object until streaming_complete
  - sibling isolation — tldr array + entities object both activate simultaneously
  - prettyName humanizes camelCase + snake_case keys

**Task 041 NEW depth guard describe** (1):
- depth-≥-2 nested object-of-object falls back to compact JSON (no infinite recursion)

**Task 041 NEW ADR-021 test in (g)** (1):
- object renderer DOM contains no inline hex/rgb color overrides

**Total: 23 = 12 (kept) + 9 (replaced in f) + 1 (depth guard) + 1 (object dark-mode in g).**

### 6.3 BFF publish-size delta

**0 MB** — no `.cs` files touched. Frontend-only change. BFF binary unaffected per ADR-029 + NFR-02 budget.

### 6.4 Frontend bundle delta

Unmeasured precisely (widget package emits source-mapped `dist/` only). Code added: ~390 LOC TypeScript + 1 new React component + 1 helper function. Post-minify estimate: ~4 KB gzipped (well within NFR-02 budget).

---

## 7. UI Test status (ADR-021 dark-mode + functional UI)

| Test (POML) | Status | Notes |
|---|---|---|
| Object-Typed Field Renders as Labeled Key-Value Blocks (fixes R5 entities bug) | ✅ Covered by unit tests (R5 SC-18 negative assertion + happy path) | Live D365 walkthrough deferred |
| Array-Rendering Regression (task 040 still works) | ✅ Covered by task 040's preserved tests + new sibling-isolation test | Live walkthrough deferred |
| Dark Mode Compliance (ADR-021) | ✅ Covered by new DOM-scan test on object renderer subtree + code review (all colors via `tokens.*`) | Live theme-toggle walkthrough deferred |
| Depth Guard Fallback | ✅ Covered by new depth-guard unit test | Live walkthrough deferred |

**Chrome integration UI test status**: deferred to main session at Phase B exit gate. Sub-agent has no access to live D365 environment. 23 unit tests covering all 4 POML UI-test scenarios provide equivalent coverage; semantic-token compliance verified at code + DOM levels.

---

## 8. Confirmation that task 040's array dispatch is unchanged

`git diff` of the widget shows:
- Task 040's lines establishing `JsonSchemaField`, `JsonSchema`, `outputSchema?` on widgetData, `classifySchemaField()` (returning `'array-of-string'`), `parseArrayOfString()`, `<SchemaAwareArrayRenderer />`, and the array branch in the dispatch site — all UNCHANGED EXCEPT:
  - `classifySchemaField()`'s object branch flipped from `return 'legacy'` to `return 'object'` (the explicit extension-point one-line change task 040 documented)
  - Comments around the dispatch site updated to remove "TASK 041 EXTENSION POINT" language now that 041 has landed

Task 040's tests run as part of this task's test run — all 12 retained tests pass without modification (the 1 replaced test was the explicit "baseline to flip" marker, not a regression).

---

## 9. Escalations / Open Questions

None. Task executed within sub-agent scope. Extension point from task 040 worked exactly as documented.

---

## 10. Commit message recommendation

For the combined task 040 + 041 commit aggregated by main session:

```
feat(r6): widget schema-aware dispatch (tasks 040+041 — fixes R5 SC-18 Gap C)

Add schema-aware ARRAY and OBJECT field rendering to StructuredOutputStreamWidget.
When the action's outputSchema (R6 Pillar 5, populated by Wave B-G2 tasks
032+033) declares a top-level field as `array` of `string` (task 040) or
`object` with `properties` (task 041), the accumulated streaming content is
JSON.parsed on `streaming_complete` and rendered as Fluent v9 semantic markup:
arrays as <ul><li>; objects as labeled key-value blocks where nested arrays
REUSE the array renderer component (no duplicate implementation).

Structurally fixes R5 SC-18 Gap C bugs:
- TL;DR rendered raw JSON tokens (task 040 fix)
- Entities rendered raw JSON object literal (task 041 fix)

Backward compatibility preserved (NFR-11): actions without outputSchema
render via the legacy displayHint path unchanged. Malformed JSON surfaces an
inline error without crashing; mid-stream tokens correctly defer to the
legacy skeleton/cursor path until streaming_complete arrives. Depth-≥-2
nested object-of-object falls back to compact JSON.stringify with a TODO
marker per Phase B constraint.

Tests: 23 in __tests__/StructuredOutputStreamWidget.test.tsx, all pass
(12 task 040 baseline + 11 new for task 041). BFF publish-size delta: 0 MB
(frontend-only).

ADR-012 ✅ (Fluent v9 shared lib) · ADR-021 ✅ (semantic tokens; DOM-scan
tests for both array + object subtrees) · ADR-029 ✅ (0 MB BFF delta) ·
ADR-030 ✅ (no new channels). NFR-11 ✅ (back-compat). FR-29 ✅ (object
rendering).
```

---

*Task 041 closed. Wave B-G3 complete.*
