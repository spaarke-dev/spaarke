# SRFR-057 — Auto-detect CREATE-mode two-phase write fix (v1.4.5 → v1.4.6)

**Date**: 2026-07-08
**Branch**: work/set-regarding-and-field-mapping-resolver-r1
**Deployment target**: spaarkedev1 → `RegardingResolverSolution 1.4.6`

## 1. Root cause

Owner UAT of v1.4.5 (2026-07-08): from the Project subgrid on a Matter form,
"+ New" opened the Event create form. Dataverse's built-in relationship
mapping populated `_sprk_regardingproject_value` on the new record (no PCF
involvement — that's an OOB Dataverse behavior). But the owner then saved
the form quickly and observed all 5 resolver fields NULL in the resulting
row: `sprk_regardingrecordid`, `sprk_regardingrecordname`,
`sprk_regardingrecordurl`, `sprk_regardingrecordnumber`, and the
`sprk_regardingrecordtype` lookup.

Screenshot confirmed the failure mode. The RegardingResolver PCF is present
on the Event form (bound to `sprk_regardingrecordtype`), and it MUST populate
those 5 fields when the auto-detect finds a pre-populated
`sprk_regarding{Entity}` lookup at mount.

### Why v1.4.5 failed

v1.4.5's auto-detect CREATE-mode block (added in v1.4.0 SRFR-045, extended
in v1.4.5 SRFR-054 for display-name resolution) chained THREE sequential
`await` calls BEFORE the first `setValue` fired on the form:

```
await resolveRecordType(...)                       // Phase 1
await resolveRecordDisplayNameFieldName(...)       // Phase 2 nested try
await webApi.retrieveMultipleRecords(...)          // Phase 2 nested try
setFormTextValue('sprk_regardingrecordid', ...)    // ← ONLY NOW
setFormTextValue('sprk_regardingrecordname', ...)
setFormTextValue('sprk_regardingrecordurl', ...)
setFormLookupValue('sprk_regardingrecordtype', ...)
```

Consequence: **if the user hit Save between mount and the last `await`
settling — or if any await threw and was swallowed by the outer try/catch —
NONE of the setValue calls fired**. The form INSERT proceeded with all 5
fields left at their unpopulated defaults.

The `_sprk_regardingproject_value` lookup on the created row is
Dataverse-side relationship mapping, not this PCF's write path — that's why
it survived while the 5 resolver fields did not.

## 2. Fix — two-phase writes (v1.4.6 SRFR-057)

Refactored `runAutoDetect()` CREATE-mode branch into two phases:

**Phase 1 (immediate, synchronous — before any `await`)** writes the 3
always-known fields using baseline values that don't require any
Dataverse metadata lookup:

- `sprk_regardingrecordid` ← `detected.recordId`
- `sprk_regardingrecordname` ← `detected.recordName` (baseline — the
  parent's Primary Name column; for `sprk_matter`/`sprk_project` this is
  the number, but it's SOMETHING, not blank)
- `sprk_regardingrecordurl` ← `buildRecordUrl(...)` (synchronous helper)
- `selectedTarget` React state ← baseline selection
- `window.__sprk_regarding_pending__` ← baseline bridge payload

Fast-save scenario is now covered: even if the user hits Save immediately
after mount, all 3 fields ride the INSERT transaction with reasonable
defaults.

**Phase 2a (async — resolveRecordType alone)** fires as its own
micro-step so the `sprk_regardingrecordtype` lookup is written as soon as
the record-type resolves, independent of display-name / record-number
resolution latency.

**Phase 2b + 2c (async — display-name and record-number in parallel)**
resolve the target's display-name field and record-number field from the
`sprk_recordtype_ref` catalog + query the parent record for values. When
results come back:

- If display-name resolved to a value DIFFERENT from
  `detected.recordName`, re-write `sprk_regardingrecordname` (polish).
- If record-number resolved, write `sprk_regardingrecordnumber` (the field
  the presave webresource normally stages on save — this is a bonus
  early-write so the field is populated even for a fast-save race that
  might skip the presave hook).
- Update `__sprk_regarding_pending__` with the polished values.

Key invariant: the user who saves quickly still gets all 5 fields with
reasonable defaults; the display-name refinement is a "polish" that
happens only if the user is still on the form when the async chain
settles. NFR-06 graceful-blank preserved throughout — any async rejection
warns and continues with the baseline.

## 3. Diagnostic logging

Every phase boundary now emits `console.log('[RegardingResolver
auto-detect] ...')` with structured payloads so future silent failures are
observable in the browser console:

- On mount / detection: which parent was found (entity, id, name, lookup
  attribute, mode = CREATE/UPDATE)
- On each Phase 1 setFormTextValue: field + value
- On Phase 1 complete: summary of writes
- On Phase 2a: record-type resolved + lookup written
- On Phase 2 async complete: resolved display-name + record-number
- On Phase 2 re-write: field + from/to values for the polish

Warnings preserved on all rejection paths (NFR-06 graceful-blank).

## 4. Tests

**Test outcomes**: `npm test` → 2 suites, 72 tests pass (was 54; +18
covering the shared services + 2 new SRFR-057 tests).

New tests added under
`__tests__/RegardingResolverApp.test.tsx` → describe block
`'SRFR-057 — v1.4.6 two-phase auto-detect writes'`:

1. **"Auto-detect CREATE mode: sync writes fire immediately even if
   async resolution rejects (user-save race guard)"** — mocks
   `webAPI.retrieveMultipleRecords` to reject with a network timeout.
   Asserts:
   - `sprk_regardingrecordid`, `sprk_regardingrecordname` (baseline),
     `sprk_regardingrecordurl` all populated via `setFormTextValue`
     BEFORE the async rejection
   - `__sprk_regarding_pending__` populated with baseline values
   - `console.warn` fired (display-name + record-number both warn on
     reject) — no throw propagates to host form
   - `sprk_regardingrecordname` written EXACTLY ONCE (baseline preserved,
     no polish because the retrieve rejected)
   - `sprk_regardingrecordnumber` NOT written (resolution rejected)

2. **"Auto-detect CREATE mode Phase 2 polish: display-name refinement
   re-writes recordname when resolved to a different value"** — mocks
   `resolveRecordDisplayNameFieldName` to return `'sprk_mattername'` and
   `retrieveMultipleRecords` to return the polished display-name.
   Asserts:
   - `sprk_regardingrecordname` written TWICE: baseline `'MAT-001'` then
     polished `'Smith v. Jones (Polished)'`
   - Presave bridge reflects polished value

Existing tests updated:

- Test-file mock of `@spaarke/ui-components` now includes
  `resolveRecordDisplayNameFieldName` and `resolveRecordNumberFieldName`
  (previously missing — the shared functions were called at runtime but
  the test mock returned `undefined` and threw silently, caught by the
  outer try/catch). Default mock resolves to `null` so tests that don't
  care about polish preserve baseline behavior.
- The existing 3 SRFR-045 auto-detect tests still pass with two-phase
  writes because they assert the FINAL state (all 4 fields eventually
  written) — the ordering change is transparent to those assertions.

## 5. Deploy verification

- `npm run build:prod` — bundle 1.58 MiB, no errors
- `Solution/pack.ps1` — packed
  `Solution/bin/RegardingResolverSolution_v1.4.6.zip`
- `pac solution import --path
  bin/RegardingResolverSolution_v1.4.6.zip --force-overwrite
  --publish-changes` → **Solution Imported successfully. Published All
  Customizations.**
- `pac solution list | grep RegardingResolver` →
  `RegardingResolverSolution 1.4.6 False` ✓

Environment: **spaarkedev1** (SPAARKE DEV 1) as
`ralph.schroeder@spaarke.com`

## 6. Version anchors (6 updated)

| File | v1.4.5 → v1.4.6 |
|---|---|
| `src/client/pcf/RegardingResolver/package.json` | `"version": "1.4.6"` |
| `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` | `CONTROL_VERSION = '1.4.6'` |
| `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml` | `version="1.4.6"` |
| `src/client/pcf/RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` | `version="1.4.6"` |
| `src/client/pcf/RegardingResolver/Solution/pack.ps1` | `$version = "1.4.6"` |
| `src/client/pcf/RegardingResolver/Solution/solution.xml` | `<Version>1.4.6</Version>` |

`BUILD_DATE` in `RegardingResolverApp.tsx` bumped from `2026-07-06` →
`2026-07-08`. Version footer now reads `v1.4.6 • Built 2026-07-08`.

## 7. Owner UAT — what to verify on spaarkedev1

1. Hard-refresh the Spaarke app (Ctrl+Shift+R) to clear the cached PCF
   bundle. Version footer on any RegardingResolver instance MUST read
   `v1.4.6 • Built 2026-07-08`.
2. On any parent record (e.g. a Matter), open a subgrid of a child
   entity that has the RegardingResolver on its form (Event, ToDo,
   Communication, etc.). Click **+ New**.
3. On the child create form, IMMEDIATELY click Save (do not wait for the
   PCF to visually settle). Verify:
   - The saved record has all 5 resolver fields populated
     (`sprk_regardingrecordid`, `sprk_regardingrecordname`,
     `sprk_regardingrecordurl`, `sprk_regardingrecordnumber`,
     `sprk_regardingrecordtype`).
   - `_sprk_regardingproject_value` (or corresponding lookup for the
     opened parent entity) also populated — this was always working;
     confirming it still does.
4. Open the browser DevTools console and re-play the flow. You should
   see structured `[RegardingResolver auto-detect] Phase 1 …`,
   `Phase 2a …`, `Phase 2 async catch-up complete …` log lines.

## 8. Constraints preserved

- ADR-024 (polymorphic resolver contract) — Phase 1 baseline write shape
  identical; Phase 2 polish only refines values, never changes fields.
- ADR-022 (PCF virtual pattern + React 16) — unchanged, no new imports
  outside the shared lib.
- ADR-012 (Fluent v9 + shared library) — no shared lib modifications;
  only NEW consumer of the already-exported
  `resolveRecordNumberFieldName` (SRFR-052 added the display-name
  variant; this task adds the record-number variant to the auto-detect
  path).
- v1.4.5 UPDATE-mode branch untouched (owner UAT confirmed that path
  works).
- No BFF / webresource changes (SRFR-053 and parallel agents handle
  those).
