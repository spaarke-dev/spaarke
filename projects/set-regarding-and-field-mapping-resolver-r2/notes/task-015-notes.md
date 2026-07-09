# Task 015 — Engine unit tests (closes Phase 1: Client Engine) — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high · Verdict: COMPLETED

## What was done

Consolidated the 14 existing tests (tasks 012/013/014) into the same growing
`FieldMappingService.test.ts` file with a rewritten file-header docblock that
explicitly maps every item in the task's 12-item closed-set coverage checklist
to the test(s) that cover it. Added 3 new tests to close the 3 gaps that were
not yet covered, bringing the suite to **17 tests / 4 describe blocks**.

## 1. Verdict: COMPLETED (not escalated)

No escalation trigger fired. One genuine production defect was found during
test authoring (see §3) and fixed per the task's explicit instruction ("if a
test reveals a real defect... fix it, note it prominently, and re-run gates").

## 2. Closed-set coverage map

| # | Closed-set item | Status | Test(s) |
|---|---|---|---|
| 1 | Copy scalar | Existing (012) | `scalar Copy assigns the source value to the target payload field` |
| 2 | Copy lookup (`@odata.bind` via annotation) | Existing (012) | `lookup Copy resolves the referent entity and writes navProp@odata.bind (...)` |
| 3 | Default (literal + empty→warn) | Existing (013) | `Default rule writes the defaultValue literal...` + `a Default rule with an empty defaultValue warns and skips` |
| 4 | Concat (placeholder resolution) | Existing (013) | `Concat/Template rule resolves "{sprk_matternumber} - {sprk_mattername}"...` |
| 5 | Template (distinct from Concat) | Existing (013) | `a Template rule behaves identically to Concat (shared resolveExpression)` — exercised via a distinct expression/target, proving the Template dispatch branch independently invokes the shared resolver |
| 6 | Same-entity self-map (matter→matter, self-named) | Existing (014) | `a same-entity (matter -> matter) self-named Copy rule applies...— not a no-op` |
| 7 | No source===target guard (fetch+apply still run) | Existing (014) | `a same-entity pair (...) is NOT short-circuited: the BFF profile fetch still fires and the apply path still runs` |
| 8 | No-profile no-op (404 → `{profileFound:false, fieldsMapped:[], warnings:[]}`, no throw) | **ADDED (015)** | `no profile configured (BFF 404) is a graceful no-op...` |
| 9 | Missing-source-field → warning | **ADDED (015)** | `a Copy rule whose source field is absent from the parent record warns and skips (FR-09) — other rules still apply` |
| 10 | Unresolved-placeholder → warning + token omitted | Existing (013) | `an unresolved placeholder warns and is omitted from the output...` |
| 11 | Unresolvable-lookup → warning + skip | Existing (012) | `unresolvable lookup (missing lookuplogicalname annotation) warns and skips — never throws` |
| 12 | Never-throw (failing path warns, other rules still apply) | **ADDED (015)** | `never-throw: a profile mixing a failing Copy-lookup rule, a failing Copy-scalar rule, and a succeeding Default rule completes without throwing and still applies the succeeding rule` |

All 12 items have an explicit passing test. Items 1-7, 10-11 were already
covered by tasks 012-014; items 8, 9, 12 were added by this task.

## 3. Production defect found + fixed

**Defect**: `applyCopyScalar` (in `FieldMappingService.ts`) did not implement
the spec's FR-09 contract:

> "Type-incompatibility, missing source field, or unresolved placeholder
> produce a non-fatal warning + skip that rule and never abort record
> creation." (spec.md FR-09)

The pre-015 implementation was:

```ts
function applyCopyScalar(rule: IFieldMappingRule, ctx: IRuleApplyContext): void {
  const value = ctx.sourceRecord?.[rule.sourceField];
  ctx.payload[rule.targetField] = value;
  ctx.fieldsMapped.push(rule.targetField);
}
```

When `rule.sourceField` was absent from the parent record (the field wasn't
returned by the batched `$select` — e.g. a stale/renamed field reference in
the rule config), `value` was `undefined`, and the function silently wrote
`payload[targetField] = undefined` AND recorded the field as successfully
mapped — no warning, and a bogus `undefined` write onto the create payload.
This directly contradicts FR-09 and the task's own closed-set item 9.

**Fix applied** (minimal, single guard branch):

```ts
function applyCopyScalar(rule: IFieldMappingRule, ctx: IRuleApplyContext): void {
  const value = ctx.sourceRecord?.[rule.sourceField];
  if (value === undefined) {
    ctx.warnings.push(
      `Copy rule "${rule.sourceField}"→"${rule.targetField}" skipped: source field "${rule.sourceField}" ` +
        `is missing from the parent record.`
    );
    return;
  }
  ctx.payload[rule.targetField] = value;
  ctx.fieldsMapped.push(rule.targetField);
}
```

**Design decision**: the guard checks `=== undefined` specifically (not
`== null`), so a field that is genuinely present on the parent record but
explicitly `null` (a legitimate empty value, e.g. an optional text field that
was never populated) is still copied through as `null` — only a truly
*absent* key (not present in the fetched record at all) is treated as
"missing" per FR-09's wording ("missing source field", not "empty source
field" — Default already owns the "empty" semantics for its own literal).

This fix is scoped to the Copy-scalar seam only. The Copy-lookup seam
(`applyCopyLookup`) already implemented an equivalent guard for its own
"missing annotation" case (that's the pre-existing "unresolvable lookup"
test, item 11) — no change was needed there.

## 4. Tests added (detail)

- **No-profile no-op**: `makeAuthenticatedFetch404()` (new fixture) returns
  `{ ok: false, status: 404 }`. Asserts the full result equals
  `{ profileFound: false, fieldsMapped: [], warnings: [] }` exactly (not just
  individually), that no throw occurs, and that `dataService._retrieveRecordCalls`
  is empty — proving the engine never got past the profile fetch (single-BFF-call
  contract preserved even on the no-op path).
- **Missing-source-field warning**: a profile with two Copy rules — one whose
  `sourceField` genuinely isn't a key on the mocked parent record, one that
  succeeds normally. Asserts the failing rule's target is absent from the
  payload, exactly one warning is recorded matching the new message text, and
  the sibling rule still applied (`fieldsMapped` contains only the succeeding
  target).
- **Never-throw (mixed failures)**: a 3-rule profile — an unresolvable Copy-lookup,
  a missing-source-field Copy-scalar, and a succeeding Default rule. Asserts no
  throw, exactly 2 warnings (one per failing rule), neither failing rule's
  target present in the payload, and the Default rule's target IS present —
  proving the payload is still creatable despite two independent rule failures
  in the same invocation.

## 5. Verification

- **Build**: `npm run build` (tsc, shared lib, from
  `src/client/shared/Spaarke.UI.Components`) → **0 errors**.
- **FieldMappingService suite**: `npx jest FieldMappingService --verbose` →
  **17/17 passed** (14 pre-existing + 3 new), 4 describe blocks (Copy engine;
  Default/Concat/Template engines; same-entity support; graceful degradation).
- **Regression sweep**: `npx jest --testPathPatterns "PolymorphicResolver|invoiceService|eventService|reportCardService|FieldMapping"`
  → **6 suites / 80 tests, all passed** (up from 77 pre-015 — the 3 new
  FieldMappingService tests account for the delta; no sibling suite
  regressed).

## 6. Step 9.5 quality gates

- **code-review**: CLEAN — 0 Critical / 0 Warning / 1 low-value Suggestion
  (pre-existing git-index artifact on the test file — staged-deleted +
  untracked, from an earlier task's git operations, not introduced by 015;
  flagged for the wrap-up/commit step so `git add` resolves it cleanly).
  0 AI code smells (no new interfaces, no restating comments — the new
  `applyCopyScalar` comment explains the FR-09 rationale and the null-vs-
  undefined distinction, which is "why" not "what" — no catch-log-rethrow, no
  null-check-on-non-nullable since the guard is on a genuine
  `Record<string, unknown>` runtime possibility, no >3-responsibility
  methods).
- **adr-check**: CLEAN — ADR-012 confirmed via grep (all `ComponentFramework`/
  `Xrm.WebApi` hits in both files are doc-comment references, not code; both
  files import only already-present context-agnostic types). ADR-010: N/A (no
  new DI-registered interfaces). ADR-024: N/A (no new recordtype/lookup logic;
  reuses the already-audited task-014 same-entity path). 0 violations, 0
  warnings.

## Notes for downstream tasks

- This closes Phase 1 (Client Engine). Tasks 020/021/022 (Wave B wiring —
  the 7 Create*Wizard services) can now proceed in parallel against the
  finished, fully-tested engine.
- `FieldMappingService.test.ts` is now the single growing test file for the
  engine (17 tests, 4 describe blocks) — any future engine change should
  extend this file rather than create a sibling.
- The FR-09 missing-source-field fix changes observable behavior for any rule
  whose configured `sourceField` doesn't exist on the parent record — worth
  keeping in mind if a downstream wizard-wiring task (020/021/022) sees a
  new warning appear that wasn't there before; that warning is now the
  *correct* FR-09 behavior, not a regression.
