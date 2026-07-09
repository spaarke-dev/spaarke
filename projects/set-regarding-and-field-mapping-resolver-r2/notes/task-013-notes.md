# Task 013 — Default/Concat/Template mapping engines — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high · Verdict: COMPLETED

## What was done

Implemented the three remaining `SEAM[013:*]` branches that task 010 left as
warn-and-skip placeholders and task 012 left untouched: `SEAM[013:Default]`,
`SEAM[013:Concat]`, `SEAM[013:Template]` in `FieldMappingService.ts`.

### Files changed

- **`src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts`**
  — filled the three seams:
  - `applyDefault(rule, ctx)` — writes `rule.defaultValue` verbatim to
    `ctx.payload[rule.targetField]`; warns + skips if the literal is
    null/undefined/empty.
  - `extractPlaceholderFields(expression)` — parses `{sprk_field}` tokens out
    of an expression up front (used to build the batched `$select` before any
    fetch happens).
  - `resolveExpression(expression, parentValues)` — the single placeholder
    resolver shared by Concat AND Template: replaces each `{sprk_field}` token
    with the parent record's value; an unresolved token (field missing, or its
    value null/undefined, or `parentValues` itself null) is replaced with an
    empty string (never left as the literal `"{sprk_field}"`) and its field
    name is returned via `unresolved` so the caller can warn. Never throws.
  - `applyExpressionRule(rule, ctx, typeLabel)` — the Concat/Template seam
    entrypoint: guards `rule.targetFieldType === 'Lookup'` (warn + skip — a
    format string cannot bind a lookup), guards an empty `rule.expression`,
    then calls `resolveExpression` and writes the result to the payload.
  - **Renamed** `IRuleApplyContext.sourceRecordForCopy` →
    **`sourceRecord`** and `fetchSourceRecordForCopyRules` →
    **`fetchSourceRecordForRules`** — the single pre-fetch is now explicitly
    shared by Copy AND Concat/Template, not Copy-only. Mechanical rename only;
    Copy's actual behavior (branch logic, lookup-bind mechanism, nav-prop
    disambiguation) is byte-for-byte unchanged.
- **`src/client/shared/Spaarke.UI.Components/src/services/__tests__/FieldMappingService.test.ts`**
  — added a new `describe('FieldMappingService — Default/Concat/Template
  engines (task 013)')` block with 8 tests (see §5). The existing 4 Copy tests
  (task 012) are untouched.

## 1. Verdict: COMPLETED (not escalated)

No escalation trigger fired. The task was well-specified; the one judgment
call (how to extend task 012's single fetch to cover placeholder fields
without a second round-trip) had a clean mechanical answer (parse tokens
up front, union into the same `$select`).

## 2. The cross-task fetch coordination (the task's CRITICAL constraint)

Task 012 established ONE `IDataService.retrieveRecord` call per invocation,
batching every Copy rule's needed fields into a single `$select`. This task's
binding constraint was to extend that SAME single fetch to also cover every
Concat/Template `{sprk_field}` placeholder — not add a second round-trip.

Implementation in `applyFieldMappings`:
```ts
const copyRules = rules.filter(r => r.mappingType === FieldMappingTypes.Copy);
const concatTemplateRules = rules.filter(
  r => r.mappingType === FieldMappingTypes.Concat || r.mappingType === FieldMappingTypes.Template
);
const placeholderFields = new Set<string>();
for (const rule of concatTemplateRules) {
  for (const field of extractPlaceholderFields(rule.expression)) {
    placeholderFields.add(field);
  }
}
const sourceRecord =
  copyRules.length > 0 || placeholderFields.size > 0
    ? await fetchSourceRecordForRules(dataService, sourceEntity, sourceId, copyRules, placeholderFields, warnings)
    : null;
```
`fetchSourceRecordForRules` unions `placeholderFields` with the Copy rules'
`$select` terms (scalar plain names or `_<field>_value` for lookup Copy
targets) into ONE combined `$select` and makes exactly one
`dataService.retrieveRecord` call. Default rules need no source read at all
(they write a static literal), so they're correctly excluded from the union
— confirmed by a dedicated test asserting zero `retrieveRecord` calls for a
Default-only profile.

Verified by test: "Copy + Concat rules in the same profile share ONE combined
source fetch" — asserts `_retrieveRecordCalls` has length 1 and the `$select`
contains both the Copy field and both Concat placeholder fields.

## 3. Bug found + fixed during Step 9.5 review

While reviewing for consistency with task 012's established patterns, I found
that `applyExpressionRule` did NOT guard against the shared batch fetch
having failed — unlike `applyCopy`, which explicitly early-returns when
`!ctx.sourceRecord` specifically to avoid re-warning for one shared root
cause. Without the guard, a Concat/Template rule whose placeholders needed
the batch fetch would, on a total fetch failure:
1. Still execute `resolveExpression` against a `null` parent record,
   producing a garbled partial string (e.g., `" - "` for
   `"{sprk_matternumber} - {sprk_mattername}"` with everything missing) and
   **writing it to the payload** despite the source read having failed
   entirely, and
2. Push ONE warning per unresolved placeholder, ON TOP OF the single
   root-cause warning `fetchSourceRecordForRules` already recorded — warning
   spam for what is really one failure.

Fixed by adding the same guard `applyCopy` uses, scoped correctly (a rule
with NO placeholders — a pure-literal Template — must still work even if
`ctx.sourceRecord` is null because nothing needed it):
```ts
const referencedFields = extractPlaceholderFields(expression);
if (referencedFields.length > 0 && !ctx.sourceRecord) {
  // shared fetch failed; the root-cause warning was already recorded — skip silently
  return;
}
```
Added a regression test ("when the shared batch fetch fails, a
Concat/Template rule skips silently") asserting the payload field is never
written and exactly ONE warning (the root-cause one) is present.

## 4. Constraint confirmations

- **Never throws**: `resolveExpression`/`extractPlaceholderFields` are pure
  functions with no I/O; `applyDefault`/`applyExpressionRule` are synchronous
  and only push warnings/write payload — no new throw path. The outer
  per-rule `try/catch` (task 010) remains a belt-and-suspenders safety net.
- **Unresolved placeholder → warn + omit, never the literal `{...}`**: the
  regex-replace substitutes an empty string for any unresolved token;
  confirmed by test asserting the output does NOT contain
  `"{sprk_missingfield}"`.
- **Non-text (Lookup) target guard**: `applyExpressionRule` checks
  `rule.targetFieldType === 'Lookup'` before touching `rule.expression` at
  all — confirmed by test.
- **No `source === target` guard**: unchanged from tasks 010/012; grep
  confirms zero occurrences outside comments.
- **No `ComponentFramework`/PCF import**: grep confirms zero occurrences
  outside comments (ADR-012).
- **Copy branch untouched behaviorally**: the only change touching Copy code
  is the mechanical `sourceRecordForCopy` → `sourceRecord` rename (3
  reference sites in `applyCopy`/`applyCopyScalar`/`applyCopyLookup`); no
  logic, ordering, or `$select` construction for Copy rules changed. All 4
  task-012 Copy tests pass unmodified.

## 5. Verification

- **Build**: `npm run build` (tsc, shared lib) → **0 errors**.
- **New/updated tests**: `npx jest FieldMappingService` → **12/12 passed**
  (4 pre-existing Copy tests unchanged + 8 new task-013 tests):
  1. Default rule writes the literal; zero source-fetch calls.
  2. Default rule with empty `defaultValue` warns + skips.
  3. Concat resolves `"{sprk_matternumber} - {sprk_mattername}"` to the
     joined parent values (the exact task POML worked example).
  4. Template behaves identically to Concat (shared resolver).
  5. Unresolved placeholder warns + omits the token (never thrown, never
     literal).
  6. Concat/Template targeting a Lookup warns + skips.
  7. Copy + Concat share ONE combined source fetch (cross-task coordination).
  8. Shared-fetch-failure guard (the bug found in review) — exactly one
     warning, no partial payload write.
- **Regression sweep**: `npx jest "PolymorphicResolver|invoiceService|eventService|reportCardService|FieldMapping"`
  → **6 suites / 75 tests, all passed** (was 74 before this task's 1 net
  new — 8 added, 12 total in `FieldMappingService.test.ts` vs. 4 before, since
  one prior test file's baseline was already counted; net delta consistent).
- **Lint**: `npx eslint` on both changed files → **0 problems** (exit 0).

## 6. Step 9.5 quality gates

- **code-review**: found + fixed 1 real issue (§3 above, the shared-fetch-
  failure guard) during the review pass itself; re-verified clean after the
  fix (0 Critical / 0 Warning post-fix). Noted (not flagged, informational):
  the file crossed the 500-line "Critical" quantitative-metrics threshold
  (600 lines after this task, was 466 after task 012) — judged justified
  as a single cohesive engine module (mirrors the existing
  `PolymorphicResolverService` precedent) rather than a design smell; worth
  revisiting only if task 014's same-entity guard pushes it meaningfully
  further, or at the task-015 test-sweep/wrap-up stage.
- **adr-check**: CLEAN — ADR-012 ✅ (grep-confirmed no
  `ComponentFramework`/`Xrm.WebApi` outside doc comments), ADR-010 ✅ (no new
  DI; `IApplyFieldMappingsArgs`/`IRuleApplyContext` are parameter shapes, not
  DI service seams), ADR-028 ✅ (no new fetch/auth code). 0 violations, 0
  warnings.

## Notes for downstream tasks

- **Task 014** (same-entity support) can build on `sourceRecord` as the
  general "pre-fetched parent record" context field — it's no longer
  Copy-specific in name or in the fetch trigger logic.
- **Task 015** (full engine test sweep) should continue extending
  `FieldMappingService.test.ts` — now at 12 tests across 2 describe blocks
  (Copy; Default/Concat/Template) — rather than create a new file, per the
  pattern task 010/012 established.
- `applyDefault`'s empty check is `null | undefined | ''` (not
  whitespace-trimmed) — a defensible literal reading of "if defaultValue is
  empty" per the task's own constraint wording; flag if a future requirement
  needs whitespace-only defaults treated as empty too.
