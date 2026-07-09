# Task 022 — Wire engine into invoice + reportCard services — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high · Verdict: COMPLETED
**Parallel wave**: Wave B (with 020 event/matter/project, 021 todo/workAssignment) — this task touched ONLY invoice/reportCard surfaces.

## What was done

Wired `applyFieldMappings` (the field-mapping engine, task 010/012/013) into
`InvoiceService.createInvoice` and `ReportCardService.createReportCard`, so
Matter/Project → Invoice and Matter/Project → Report Card field-mapping
profiles (seeded in task 030) take effect at wizard creation time.

### Files changed

- **`src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/invoiceService.ts`**
  — imported `applyFieldMappings`; added `_authenticatedFetch`/`_bffBaseUrl`
  private fields (constructor already received these params — they just
  weren't stored); inserted the engine call inside the existing
  `if (association?.recordId && association.entityType)` block, immediately
  after `applyResolverFields` and before the `createRecord` try-block, on the
  same `entity` payload object. Engine warnings appended to the existing
  `warnings` array. `sourceId` passed through the file's existing `_cleanGuid`
  helper (guid hygiene only — not a field-name hard-code).
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateReportCardWizard/reportCardService.ts`**
  — same insertion pattern. **Constructor signature changed**:
  `ReportCardService` previously took only `dataService` (no
  authenticatedFetch/bffBaseUrl — it has no file-upload pipeline, so it never
  needed a BFF-authenticated fetch before now). Added `authenticatedFetch:
  AuthenticatedFetchFn` and `bffBaseUrl: string` as required constructor
  params, mirroring `InvoiceService`/`WorkAssignmentService`'s established
  injected-dependency shape (not made optional — no service in this file
  family silently defaults these).
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateReportCardWizard/CreateReportCardWizard.tsx`**
  — updated the `new ReportCardService(dataService)` call site (line ~480) to
  `new ReportCardService(dataService, authFetch ?? fetch.bind(window),
  bffBaseUrl ?? '')`, mirroring the `WorkAssignmentService` instantiation
  already in the same file. Required because of the constructor signature
  change above — without it, the wizard wouldn't compile, and without passing
  the *real* injected functions, the field-mapping engine's BFF profile fetch
  would never actually succeed in production. Not a "service file" per the
  parallel-wave boundary (owned by no other Wave-B task) and necessary for
  correctness, so included in scope.
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/__tests__/invoiceService.resolver.test.ts`**
  — `stubAuthenticatedFetch()` now branches on URL: a request to
  `/field-mappings/` returns 404 (graceful "no profile configured" — the
  engine's documented no-op path), anything else (the SPE upload PUT) gets
  the pre-existing success shape. Without this, the shared authenticated-fetch
  mock would have answered the new field-mapping profile fetch with the
  upload-success JSON shape too, which the engine would parse into a
  zero-rule "profile found" result and push a warning — flipping
  `result.status` from `'success'` to `'partial'` on every test with a host
  association and silently breaking ~7 pre-existing assertions.
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateReportCardWizard/__tests__/reportCardService.resolver.test.ts`**
  — added a `stubAuthenticatedFetch()` helper (always 404 — `ReportCardService`
  has no other authenticatedFetch consumer) and updated all 10
  `new ReportCardService(ds)` call sites to `new ReportCardService(ds,
  stubAuthenticatedFetch(), 'https://bff.example')` to match the new required
  constructor params. Same root cause as above: the tests' global `fetch`
  stub (`stubFetchNavProps`) falls back to `ok:200 / {value:[]}` for any
  unmatched URL, which would otherwise have made the field-mapping engine
  report a spurious zero-rule "profile found" for every host-associated test.

## Wiring contract confirmed (both services)

- Engine called **after** `applyResolverFields`, **before** `createRecord`, on
  the **same** payload object (`entity`).
- Guarded by the same `if (association?.recordId && association.entityType)`
  condition as `applyResolverFields` — `sourceEntity`/`sourceId` only exist
  when there's a host association; no association means nothing to map.
- Engine warnings appended via `warnings.push(...mappingResult.warnings)` —
  same array the rest of the method already uses.
- **No abort-on-failure try/catch** — the engine call is not wrapped in
  try/catch, matching its documented never-throw contract (task 010 notes:
  "every failure path appends a warning and continues").
- **No target field names hard-coded** anywhere in the wiring — `targetEntity`
  is the fixed literal `'sprk_invoice'` / `'sprk_reportcard'` (the *entity*
  name, required by the engine's public contract to select the right
  profile), never a *field* name. All per-field mapping logic lives in the
  seeded profile/rules (task 030) consumed inside `FieldMappingService.ts`.
- **No behavior change when `profileFound:false`** — verified by the updated
  test stubs: a 404 response yields `{ profileFound:false, fieldsMapped:[],
  warnings:[] }`, so `warnings.push(...[])` is a no-op and every pre-existing
  assertion (including `result.status === 'success'`) is unaffected.

## Verification

- `npx tsc --noEmit` (from `src/client/shared/Spaarke.UI.Components`) →
  **0 errors**.
- `npx jest invoiceService reportCardService` → **2 suites passed, 21 tests
  passed, 0 failed** (the single `console.error` line in the output is the
  intentional "never throws — returns status error" test exercising the
  `createRecord` rejection path; not a failure).
- Did NOT run `npm run build` per the task's explicit instruction (avoid
  dist-write races with the parallel Wave-B agents on 020/021) — the main
  session runs the authoritative emit build after all three Wave-B tasks
  land.

## Quality gates (Step 9.5)

- **code-review** (5 files: both services + `CreateReportCardWizard.tsx` +
  both test files): **CLEAN** — 0 Critical / 0 Warning / 1 Suggestion (a
  pre-existing duplicated `_cleanGuid`/`_resolveEntitySet` helper pattern
  across the sibling wizard services — not introduced by this task, left
  as-is). No AI code smells (no new interfaces, no catch-log-rethrow, no
  defensive null-checks on non-nullable types, comments explain *why* not
  *what*, engine call correctly left un-wrapped per its never-throw contract).
- **adr-check** (3 non-test files): **CLEAN** — ADR-012 ✅ (no
  `ComponentFramework` references; depends on injected `IDataService` +
  `AuthenticatedFetchFn`), ADR-028 ✅ (uses the injected `authenticatedFetch`,
  no raw fetch/Authorization header, no token bridge), ADR-024 unaffected
  (engine writes to separate business fields, no interference with the
  regarding-lookup mutual-exclusion logic). 0 violations.

## Notes for downstream tasks

- Task 040 (blocked by this task) can now assume both Invoice and Report Card
  creation apply configured field-mapping profiles.
- `ReportCardService`'s constructor is no longer a 1-arg call — any other
  future caller (tests or components) must supply `authenticatedFetch` +
  `bffBaseUrl`.
