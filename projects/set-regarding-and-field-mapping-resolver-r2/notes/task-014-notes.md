# Task 014 — Same-entity (matter→matter) support + no source===target guard — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high · Verdict: COMPLETED

## What was done

Audited `FieldMappingService.ts` (built by tasks 010/012/013) for any
`sourceEntity === targetEntity` or `sourceField === targetField` short-circuit
and confirmed — by full read + targeted grep — that none exists anywhere in
the engine or the BFF fetch URL construction. No production code change was
needed; this task's deliverable is the two same-entity tests the prior tasks'
notes explicitly deferred to task 014.

### Files changed

- **`src/client/shared/Spaarke.UI.Components/src/services/__tests__/FieldMappingService.test.ts`**
  (extended, same file tasks 012/013 grew — per the established pattern of
  landing all engine tests in one file):
  - Added `makeAuthenticatedFetchForPair(rules, sourceEntity, targetEntity)` —
    a fixture variant of `makeAuthenticatedFetch` whose mocked profile body
    actually carries the requested `{sourceEntity, targetEntity}` pair (the
    original helper hardcodes `sprk_matter`/`sprk_event`, which would be
    misleading for a same-entity test).
  - Added `describe('FieldMappingService — same-entity support (task 014)')`
    with 2 tests (see §2).

## 1. Verdict: COMPLETED (not escalated)

No escalation trigger fired. The audit confirmed the engine was already
same-entity-safe by construction (tasks 010/012/013 explicitly designed
around this — see their notes' "No `source === target` guard" constraint
confirmations). This task's job was to add the tests proving it, not to
change behavior.

## 2. Audit evidence — no source===target / sourceField===targetField guard

Read `FieldMappingService.ts` in full (601 lines) and ran a targeted grep:

```
grep -n "sourceEntity\s*===\s*targetEntity|targetEntity\s*===\s*sourceEntity|sourceField\s*===\s*targetField|targetField\s*===\s*sourceField" FieldMappingService.ts
```

**Single hit**, and it is an explanatory comment, not code:

```ts
// NOTE: intentionally NO `sourceEntity === targetEntity` guard here or
// anywhere below — same-entity mapping is a supported scenario (task 014).
```

Confirmed by reading the full control flow:
- `applyFieldMappings` passes `sourceEntity`/`targetEntity` straight into
  `fetchProfile`'s URL construction (`.../profiles/{source}/{target}`) with
  no comparison of the two values anywhere before or after.
- `applyCopy`/`applyCopyScalar`/`applyCopyLookup`/`applyDefault`/
  `applyExpressionRule` dispatch purely on `rule.mappingType` and
  `rule.targetFieldType` — never on whether `rule.sourceField === rule.targetField`
  or whether the entity pair is self-referential.
- `fetchSourceRecordForRules` builds its `$select` from rule field names only;
  it has no entity-pair-aware branch.

This matches (and empirically verifies) the constraint confirmations already
recorded in the task-010/012/013 notes — this task adds the missing proof
(a runnable test), it does not discover or fix a violation.

## 3. Tests added

**Test A — self-named Copy rule applies (not a no-op)**:
`sourceEntity = targetEntity = 'sprk_matter'`, `rule.sourceField =
rule.targetField = 'sprk_practicearea'` (both same entity AND same field
name — the exact double-same scenario the task's constraint calls out).
Asserts `payload['sprk_practicearea']` receives the source record's value
(`'Litigation'`), `fieldsMapped` includes it, and no warnings — proving the
Copy engine treats same-name-same-entity as a real copy between two distinct
records, never a skipped no-op.

**Test B — negative test: same-entity pair is not short-circuited**:
Same fixture, but asserts on the *mechanism*, not just the outcome:
- `fetchMock` (the injected `authenticatedFetch`) was called exactly once,
  and the called URL contains `/profiles/sprk_matter/sprk_matter` — proving
  the BFF profile fetch actually fires for a same-entity pair (a guard would
  have prevented this call, or the URL would show suspicious substitution).
- `result.profileFound === true` and `result.fieldsMapped.length > 0` —
  proving the apply path ran to completion, not skipped.
- `dataService._retrieveRecordCalls` has length 1 — proving the Copy rule's
  source-record read also executed normally.

Together these two tests satisfy both acceptance criteria: same-entity Copy
applies correctly, and no guard intercepts same-entity pairs anywhere in the
call chain (fetch → apply → source read).

## 4. Constraint confirmations

- **No `if (sourceEntity === targetEntity)` guard anywhere**: confirmed by
  full read + grep (§2). None added by this task either.
- **No `sourceField === targetField` no-op**: confirmed the same way — no
  such comparison exists in `applyCopy`/`applyCopyScalar`/`applyCopyLookup`.
  Test A additionally exercises this exact case at runtime (both fields
  named `sprk_practicearea`) and shows the value IS copied.
- **No update-time or multi-hop cascade added**: this task added tests only;
  `applyFieldMappings` remains a single-invocation, single-hop, creation-time
  call exactly as tasks 010–013 left it. No new recursion, no polling, no
  second profile fetch.
- **Never-throw contract holds**: unchanged (no production code touched);
  both new tests use the existing never-throw fixtures/assertions style used
  throughout the file's other `describe` blocks.
- **No `ComponentFramework` import**: unchanged — this task's only file
  touches (test file) reuse the same imports already present (`IDataService`,
  `AuthenticatedFetchFn`, `IFieldMappingRule` — all context-agnostic types).
- **Additions kept minimal**: `FieldMappingService.ts` itself is byte-for-byte
  unchanged (0 lines added) — only the test file grew (~99 lines: 1 helper +
  2 tests), consistent with the task's "at most trivial code, not bulk"
  constraint.

## 5. Verification

- **Build**: `npm run build` (tsc, shared lib) → **0 errors**.
- **Tests**: `npx jest FieldMappingService` → **14/14 passed** (12
  pre-existing Copy/Default/Concat/Template tests unchanged + 2 new
  same-entity tests).
- **Regression sweep**: `npx jest "PolymorphicResolver|invoiceService|eventService|reportCardService|FieldMapping"`
  → **6 suites / 77 tests, all passed**.
- **Lint**: `npx eslint src/services/__tests__/FieldMappingService.test.ts` →
  0 problems (exit 0; one unrelated Node ESM/CJS module-type warning printed
  by eslint's own loader, not a lint finding).

## 6. Step 9.5 quality gates

- **code-review**: CLEAN — 0 Critical / 0 Warning / 1 low-value Suggestion
  (the `(fetchMock as unknown as jest.Mock)` double-cast in Test B is a minor
  style wart to inspect `.mock.calls`; consistent with how `AuthenticatedFetchFn`
  mocks are already cast elsewhere in this same file, e.g.
  `makeAuthenticatedFetch`'s `as unknown as AuthenticatedFetchFn`). No AI
  code smells detected (no new interfaces, no restating comments, no
  null-checks-on-non-nullable, no catch-log-rethrow, no >3-responsibility
  methods). §11 Component Justification does not fire — this task only
  modified an existing file (added tests), no new file/endpoint/DI/package.
- **adr-check**: CLEAN — ADR-012 ✅ (grep-confirmed no `ComponentFramework`/
  `Xrm.WebApi`/PCF references outside doc comments; test additions reuse only
  already-imported context-agnostic types). ADR-010/ADR-028: N/A (no DI
  registrations, no auth/fetch code changed). 0 violations, 0 warnings.

## Notes for downstream tasks

- Task 015 (full engine test sweep) can continue extending
  `FieldMappingService.test.ts` — now at 14 tests across 3 describe blocks
  (Copy; Default/Concat/Template; same-entity support) — per the established
  single-growing-file pattern.
- `makeAuthenticatedFetchForPair` is available for any future test needing a
  profile fixture whose `sourceEntity`/`targetEntity` body fields must match
  a specific (non-`sprk_matter`/`sprk_event`) pair.
