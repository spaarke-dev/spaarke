# Task 026 — DocumentUploadWizard `resolveSearchIndexNameForRecord` handoff

**Date**: 2026-06-07
**Status**: ✅ Completed
**Rigor**: FULL
**Blocks**: Task 027 (DocumentRecordService.buildRecordPayload)

## What downstream task 027 consumes

A new helper, exported from `AssociateToStep.tsx` (via re-export from the
standalone `searchIndexResolver.ts` module — see Module-layout rationale
below):

```ts
import { resolveSearchIndexNameForRecord } from "./AssociateToStep";
// or, equivalent direct import:
import { resolveSearchIndexNameForRecord } from "./searchIndexResolver";
```

### Signature

```ts
export interface IXrmWebApiLike {
  WebApi: {
    retrieveRecord: (
      entity: string,
      id: string,
      options: string
    ) => Promise<Record<string, unknown>>;
  };
}

export async function resolveSearchIndexNameForRecord(
  xrm: IXrmWebApiLike,
  entityLogicalName: string,
  recordId: string
): Promise<string>;
```

`IXrmWebApiLike` is a deliberately narrow structural type — covers only the
`retrieveRecord` shape the resolver uses. Task 027 callers can pass the full
`XrmHandle` (from `AssociateToStep.tsx`) or a structural double for testing.

### FR-WIZ-06 3-step chain (binding)

1. Parent record's `sprk_searchindexname` (when non-empty)
2. Parent record's owning BU's `sprk_searchindexname` (when non-empty)
3. Empty string — caller MUST omit the field from the create payload so the
   BFF tenant-default chain (FR-BFF-04) takes over server-side

### "Empty" semantics (mirrors INV-5)

Treated as empty (cascade continues):
- `undefined`, `null`
- Empty string `""`
- Whitespace-only strings (`"   "`)
- Non-string types (`0`, `false`, etc.)

Treated as a real value (cascade short-circuits):
- Any string whose trim length > 0 (e.g., `"spaarke-knowledge-index-v2"`)

### Never throws

Read failures at step 1 or step 2 degrade gracefully to the next chain step
(or to step 3). The function returns `""` rather than propagating an error —
empty is a legitimate result per FR-WIZ-06 (server-side tenant default applies).

This is the SEMANTIC DIFFERENCE vs the sibling `resolveContainerIdForRecord`,
which DOES throw when no container is resolvable (container is required for
upload).

## Module layout rationale

Resolver lives in **`src/components/searchIndexResolver.ts`** (a standalone
module) and is **re-exported from `AssociateToStep.tsx`** to satisfy the task
contract ("next to the existing `resolveContainerIdForRecord` function")
while still being unit-testable without dragging in JSX / Fluent / Xrm-full-handle
dependencies.

```
src/components/
  AssociateToStep.tsx          ← re-exports the resolver + IXrmWebApiLike
  searchIndexResolver.ts       ← the actual implementation (pure TS)
  searchIndexResolver.test.ts  ← 11 unit tests (jest)
```

Task 027 can import from either path — both are stable.

## INV-5 boundary (this task vs task 027)

This task is a **READ helper**. INV-5 enforcement (don't overwrite explicit
values on the create payload) is the responsibility of task 027 when it
applies the resolved value to the `sprk_document` payload via the existing
`EntityCreationService.applyDefaultSearchIndexName` granular helper (delivered
by task 020).

Suggested wiring for task 027:

```ts
// In DocumentRecordService (or its caller):
const resolvedIndexName = await resolveSearchIndexNameForRecord(
  xrm,
  parentEntityType,
  parentEntityId
);
// Apply INV-5-safely (helper skips when payload already has a non-empty value):
EntityCreationService.applyDefaultSearchIndexName(payload, resolvedIndexName);
// CRITICAL: do NOT add sprk_containerid here — sprk_graphdriveid is the
// canonical Document container field (design.md INV).
```

## Single-roundtrip optimization

Step 1 selects BOTH `sprk_searchindexname` AND `_owningbusinessunit_value` in
the same `retrieveRecord` call. That way, when step 1 returns empty, step 2's
BU lookup needs only one additional roundtrip (to `businessunit`), not two.
Total worst case: 2 roundtrips (parent + BU); best case: 1 (parent has the
value).

`_owningbusinessunit_value` is a standard Dataverse OData lookup column
available on every owned entity, so this selector is universal.

## Test coverage

`src/components/searchIndexResolver.test.ts` — 11 tests, all passing:

```
PASS  src/components/searchIndexResolver.test.ts
  isNonEmptyIndexName
    ✓ returns true for non-empty strings
    ✓ returns false for empty / whitespace / null / undefined / non-string
  resolveSearchIndexNameForRecord — FR-WIZ-06 chain
    ✓ Step 1: returns parent record's sprk_searchindexname when non-empty
    ✓ Step 2: returns parent's owning BU's sprk_searchindexname when parent value is empty
    ✓ Step 3: returns empty string when both parent and BU values are empty
    ✓ treats whitespace-only parent value as empty (cascades to BU)
    ✓ strips braces from _owningbusinessunit_value before BU lookup
    ✓ returns empty string (does not throw) when parent record read fails
    ✓ returns empty string (does not throw) when BU read fails after empty parent value
    ✓ returns empty string when parent has empty value and no owning BU reference
    ✓ requests both sprk_searchindexname and _owningbusinessunit_value in a single roundtrip

Test Suites: 1 passed, 1 total
Tests:       11 passed, 11 total
Time:        1.726 s
```

The 3 chain steps required by the task spec are covered by tests 1, 2, and 3 of
the chain group. The remaining 7 tests cover supporting concerns (INV-5
semantics, brace-stripping, graceful degradation, $select assembly).

## Build verification

- `npx tsc --noEmit` over `searchIndexResolver.ts` + `searchIndexResolver.test.ts`:
  clean (zero errors).
- `npx tsc --noEmit` over the full DocumentUploadWizard tsconfig: pre-existing
  JSX namespace / `ComponentFramework` errors (unrelated to this task) remain.
  None in any file touched by this task.
- `npx vite build` (production): clean. 2287 modules transformed, `dist/index.html`
  1,099.47 kB / gzip 298.68 kB. No regression vs prior baseline.
- `npx jest src/components/searchIndexResolver.test.ts`: 11/11 passing in 1.726 s.

## Files modified

- `src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx` — added
  re-export of `resolveSearchIndexNameForRecord` + `IXrmWebApiLike` type
  alongside the existing `resolveContainerIdForRecord` function; added inline
  documentation explaining the pattern symmetry and the semantic differences.
  (~25 LOC additive — no existing code modified.)
- `src/solutions/DocumentUploadWizard/src/components/searchIndexResolver.ts`
  — NEW. ~130 LOC; pure TS, no React imports.
- `src/solutions/DocumentUploadWizard/src/components/searchIndexResolver.test.ts`
  — NEW. 11 jest tests; ~210 LOC.
- `src/solutions/DocumentUploadWizard/package.json` — added `jest`, `ts-jest`,
  `@types/jest` devDependencies + `test` script. (DocumentUploadWizard had no
  test infrastructure prior to this task.)
- `src/solutions/DocumentUploadWizard/jest.config.js` — NEW. Minimal jest
  config (ts-jest preset, node env, `**/*.test.ts` match).

## ADR compliance

- **ADR-012** (Shared UI library is the contract): Resolver lives in the
  wizard's local code per the task spec; consumed by `DocumentRecordService`
  in the shared lib via inversion — caller passes the resolved value INTO
  the shared lib's `buildRecordPayload`. Shared lib does NOT import from
  the code-page (would violate dependency direction).
- **ADR-021** (Fluent v9): N/A — resolver is pure TS, no UI surface added.
- **ADR-022** (React version boundaries): Pure-TS helper, no React imports.
  Portable across React 16 (PCF) and React 18/19 (code-pages).
- **ADR-028** (Spaarke Auth v2): Uses host-context `Xrm.WebApi.retrieveRecord`,
  no BFF call, no `authenticatedFetch`, no MSAL plumbing. Consistent with the
  existing `resolveContainerIdForRecord` precedent and the
  `DATA-ACCESS-DECISION-CRITERIA` doc (wizard runs inside MDA → host-context
  is the right call).
- **INV-5** (explicit values are sacred): Not applicable — this is a READ
  helper. INV-5 enforcement is task 027's responsibility (the SET step) via
  `EntityCreationService.applyDefaultSearchIndexName` (delivered by task 020).

## Notes for task 027

1. Import `resolveSearchIndexNameForRecord` from `./AssociateToStep` (preferred,
   matches the public surface declared by this task) OR from
   `./searchIndexResolver` directly — both work.
2. **CRITICAL** — do NOT add `sprk_containerid` to the Document payload. The
   canonical Document container field is `sprk_graphdriveid` (per design INV).
   This resolver only produces `sprk_searchindexname`.
3. Use `EntityCreationService.applyDefaultSearchIndexName` (from task 020) to
   apply the resolved value INV-5-safely — that helper already handles the
   "skip when payload has an explicit value" check and the "leave field unset
   when input is empty" cascade behavior.
