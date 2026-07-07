# SRFR-052 — Regarding Name display-name resolution

**Date**: 2026-07-06
**Version**: RegardingResolver 1.4.3 → **1.4.4**
**Solution**: `RegardingResolverSolution_v1.4.4.zip` deployed to `spaarkedev1`

---

## Root cause

Owner UAT on the RegardingResolver revealed the "Regarding Name" field on the
sprk_todo form showed the target record's **number**, not its **name**.

Investigation:

- `Xrm.Utility.lookupObjects` returns the record's **Primary Name** column.
- For `sprk_matter`, the Primary Name column is `sprk_matternumber` (not
  `sprk_mattername`).
- Consequence: `applyResolverFields()` received the number as
  `parentRecordName` and wrote it verbatim to `sprk_regardingrecordname`.

## Schema fix (already applied before this task)

Added new SLT column to `sprk_recordtype_ref`:

- **`sprk_recorddisplaynamefield`** — names the DISPLAY-NAME column on the
  target entity (mirrors the existing `sprk_regardingrecordnumberfield`).

Populated for all 12 catalog rows:

| Entity              | Display-Name Field       |
| ------------------- | ------------------------ |
| sprk_matter         | sprk_mattername          |
| sprk_project        | sprk_projectname         |
| sprk_event          | sprk_eventname           |
| sprk_invoice        | sprk_invoicename         |
| sprk_workassignment | sprk_workassignmentname  |
| sprk_organization   | sprk_organizationname    |
| sprk_todo           | sprk_name                |
| sprk_analysis       | sprk_analysisname        |
| sprk_budget         | sprk_budgetname          |
| sprk_document       | sprk_documentname        |
| account             | name (OOB)               |
| contact             | fullname (OOB)           |

## Code fix (this task)

### Shared library — `PolymorphicResolverService.ts`

1. New helper `resolveRecordDisplayNameFieldName(webApi, entityLogicalName)`
   — parallels `resolveRecordNumberFieldName`. Queries
   `sprk_recordtype_ref.sprk_recorddisplaynamefield` for the target entity.
   Returns the field name or null. Cached per page-lifetime (matches the
   number-field cache pattern).
2. `IApplyResolverFieldsOptions` extended with optional
   `sourceDisplayNameField` override (parallel to `sourceRecordNumberField`).
3. `IApplyResolverFieldsResult` extended with optional `displayName` field
   (the resolved display-name value or fallback).
4. `applyResolverFields` extended:
   - Consolidated the target-record query into a **single** `retrieveMultipleRecords`
     call whose `$select` combines both the number and display-name resolved
     source fields (one round-trip per SRFR-052 design).
   - Reads the display-name value from the target record and writes it to
     `entity['sprk_regardingrecordname']`.
   - NFR-06 graceful fallback: when metadata field is null OR the target
     record's value is null/empty, falls back to the picker-provided
     `parentRecordName` — never leaves the field blank.
   - `console.warn` on each fallback path for diagnostics.

### Backward compatibility

- Existing callers passing only `parentRecordName` (no override) still work.
  When the catalog `sprk_recorddisplaynamefield` is null (fresh environment),
  the resolver silently falls back to `parentRecordName` — historical
  4-field write shape preserved.
- The 5th field (`sprk_regardingrecordnumber`) resolution is UNCHANGED —
  still skips (not falls back) per Q-06 owner clarification.

### `services/index.ts`

Exported `resolveRecordDisplayNameFieldName` and
`_resetDisplayNameFieldCacheForTests` alongside their number-field siblings.

### PCF surface

- `RegardingResolverApp.tsx`: bumped `BUILD_DATE` to `'2026-07-06'`.
- `ResolverWriteHandler.ts`: **no code changes** — passes through to
  `applyResolverFields` which handles display-name resolution internally.

## Version bump: 1.4.3 → 1.4.4

Updated 6 anchor files:

- `src/client/pcf/RegardingResolver/package.json`
- `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` (CONTROL_VERSION)
- `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml`
- `src/client/pcf/RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml`
- `src/client/pcf/RegardingResolver/Solution/pack.ps1`
- `src/client/pcf/RegardingResolver/Solution/solution.xml`

## Test outcomes

### Shared library — `PolymorphicResolverService.test.ts`

**30 / 30 pass** (previous 22 existing + 8 new):

- 5 tests — `resolveRecordNumberFieldName` (unchanged, still pass)
- 11 tests — `applyResolverFields — 5-field write (happy path)` (mock fixture
  updated to include display-name column; assertions unchanged)
- 4 tests — `applyResolverFields — NFR-06 graceful-blank` (target-record read
  failure warn message updated to reflect new consolidated `$select`)
- 3 tests — `applyResolverFields — backward compat + explicit override`
- **4 new tests** — `resolveRecordDisplayNameFieldName` (returns
  `sprk_mattername`; returns `name` for account; returns `fullname` for
  contact; caches per page)
- **3 new tests** — `applyResolverFields — SRFR-052 display-name resolution`
  (writes target's display-name value; falls back on metadata-null; falls
  back on target-value-null)

### Full shared-lib suite

- 1444 pass, 5 pre-existing unrelated failures (RichFilePreview,
  EntityCreationService.cascade, ThemeService, DailyBriefingClient) —
  confirmed pre-existing on branch, no regression from SRFR-052.

### RegardingResolver PCF

- **70 / 70 pass** — all existing tests continue to pass with the shared-lib
  behavior change (fallback preserves picker name → visible behavior in test
  fixtures unchanged).

## Build outcome

- `npm run build:prod` → succeeded (1 warning: bundle size 1.58 MiB —
  pre-existing, unrelated to this task).

## Deploy verification

- `pac solution import` → **Solution Imported successfully. Published All Customizations.**
- `pac solution list | grep RegardingResolver` →
  `RegardingResolverSolution   Regarding Resolver Solution   1.4.4   False`

## LOC diff (approximate)

- `PolymorphicResolverService.ts` — **+140 / -25** (new helper, new cache,
  extended options+result types, consolidated target-record read, new
  fallback branch, updated docstrings)
- `PolymorphicResolverService.test.ts` — **+230 / -20** (multi-field `$select`
  fixture support, 4 new tests in new describe blocks, minor assertion
  adjustment for consolidated-select warn)
- `services/index.ts` — **+7 / -0** (2 new named exports)
- `RegardingResolverApp.tsx` — **+1 / -1** (BUILD_DATE)
- 5 version anchors — **+5 / -5** each 1.4.3 → 1.4.4

## Constraints respected

- **ADR-024** — 5-field write pattern preserved (extended resolution logic, not
  field set).
- **ADR-022** — React 16 + Fluent 9 preserved.
- **ADR-012** — `resolveRecordDisplayNameFieldName` added as a NEW shared
  primitive in `@spaarke/ui-components/services`, not entity-specific.
- Backward compatible — existing callers keep working via fallback to
  `parentRecordName`.
- Sub-agent write boundary (§3) — no writes to `.claude/`.
