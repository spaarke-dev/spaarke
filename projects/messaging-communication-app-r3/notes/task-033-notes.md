# Task 033 Notes — "Email & Messages" record tab (FR-15)

> Sub-agent execution notes. Main session reconciles TASK-INDEX.md / current-task.md.

## Summary

Delivered a record-filtered "Email & Messages" DataGrid tab using the sanctioned DataGrid
web-resource framework — no PCF, no bespoke JS filter, no second regarding mechanism, no
second grid config. Matter is the pilot; the mechanism generalizes to all 11 ADR-024
regarding-family entities without further code changes (only a per-entity FormXml patch is
needed to roll out beyond Matter).

## Files created / modified

- **Modified** `src/solutions/sprk_communicationspage/src/main.tsx` — the existing R2 global
  "All Communications" Code Page now ALSO serves as the record-scoped grid. Added
  `parseRegardingEnvelope()` (reads `regardingAttribute`/`regardingValue` from the URL query
  string or a `data=` envelope) and forwards a `hostFilters` condition via
  `<DataGridPageShell additionalHostFilters={…}>`. Absent envelope = unchanged global/launcher
  behavior (no regression). `onBack` (back arrow) is suppressed whenever any regarding envelope
  is detected (record-tab mode has no dialog to close).
- **Created** `src/dataverse/forms/sprk_matter/communicationsGridOnLoad.ts` (+ compiled
  `.js`) — entity-agnostic form OnLoad script. Resolves the host record's entity name + id,
  maps to the `sprk_regarding{type}` field via a client-side mirror of
  `RegardingFieldMap.cs` (11 entries), and pushes a `data=` envelope onto the "Email &
  Messages" tab's Web Resource control via the documented `setData()` Client API.
- **Created** `src/dataverse/forms/sprk_matter/communicationsGridTab.FormXml.patch.xml` —
  FormXml patch (library registration + OnLoad handler binding + new tab/section/control),
  mirroring the existing `insightCardMount.FormXml.patch.xml` convention in this folder.
  Consumed by task 034 (deploy).
- **Modified** `src/dataverse/forms/sprk_matter/tsconfig.json` — added the new `.ts` file to
  the `files` list so it compiles alongside the sibling Matter Health scripts.

## fetchXmlOverlay.ts confirmation

**YES** — `hostFilters` (the `overlayHostFilters` function + `<DataGrid hostFilters>` /
`<DataGridPageShell additionalHostFilters>` prop) carries an arbitrary FetchXML column
condition, including a `sprk_regarding{type}` lookup attribute with operator `eq` and a GUID
value. No modification to the shared framework was needed or made — `fetchXmlOverlay.ts`,
`DataGrid.tsx`, and `DataGridPageShell.tsx` were read-only for this task. Evidence:
`HostFilterCondition { attribute: string; operator: HostFilterOperator; value?: … }` accepts
any attribute name (not restricted to a fixed set), and `DataGridPageShellProps` exposes
`additionalHostFilters` as the exact prop for a host with its own filter-derivation logic
(our onLoad-driven URL envelope).

## Scoping design (how `{type}` is derived + generalized)

1. **Form OnLoad** (`communicationsGridOnLoad.ts`) reads `formContext.data.entity.getEntityName()`
   + `formContext.data.entity.getId()`.
2. Maps the entity logical name → `sprk_regarding{type}` field via `REGARDING_FIELD_MAP`, a
   client-side mirror of the server's single source of truth,
   `Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs` (11 entries: `sprk_matter`,
   `sprk_project`, `sprk_invoice`, `sprk_servicerequest`, `sprk_workassignment`, `sprk_event`,
   `sprk_budget`, `sprk_analysis`, `sprk_organization`, `account`, `contact`).
3. Pushes `regardingAttribute=<field>&regardingValue=<id>` onto the Web Resource control via
   `setData()`.
4. The Code Page (`main.tsx`) reads that envelope and passes
   `additionalHostFilters=[{ attribute, operator: 'eq', value }]` to `<DataGridPageShell>`,
   which the framework overlays into the existing communications grid config's FetchXML.
5. **Generalization**: the script contains NO Matter-specific logic — it resolves the
   entity dynamically. The SAME compiled `.js` can be registered, unchanged, on any of the
   other 10 regarding-family entity forms; only a per-entity FormXml patch (tab + control +
   library/handler registration, same shape as `communicationsGridTab.FormXml.patch.xml`) is
   needed to roll it out. That per-entity rollout is explicitly out of this task's scope
   (Matter pilot first, per spec FR-15) — not a defect, not deferred as an issue (it's
   planned follow-on repetition of a proven pattern, not new discovered work).

## Correctness hardening (Step 9.5 finding — fixed)

Initial draft of `parseRegardingEnvelope()` fell back to **unfiltered/global rendering**
whenever either `regardingAttribute` or `regardingValue` was missing — including a
**malformed** envelope (one key present, the other absent), which can only arise from a host
wiring bug (the onLoad script's `buildDataEnvelope` always sets both keys together, so a
one-key-only envelope is never legitimate). Falling back to "show everything" on a malformed
envelope would be an over-disclosure failure mode masquerading as "it still works."

**Fix**: `parseRegardingEnvelope()` now returns a 3-way discriminated result — `none` (no
envelope at all → correct global no-op), `valid` (both keys → scoped filter), `malformed`
(exactly one key → **fail CLOSED**). On `malformed`, the grid is given an impossible
`hostFilters` condition (`sprk_communicationid eq 00000000-0000-0000-0000-000000000000`) that
can never match any row, so the grid renders **empty** instead of falling back to
**everything** — plus a `console.error` for diagnosability. This mirrors the same fail-safe
principle already documented in `communicationsGridOnLoad.ts` (control API absent →
leave unwired, never silently proceed).

## Step 9.5 quality gate — findings + resolutions

- **Scoping correctness (adversarial focus)**: verified `eq` operator + single scalar GUID
  value → exact single-record match; `overlayHostFilters`'s own pre-filter additionally drops
  any condition missing `attribute`/`value`, so even a coding mistake in this task's payload
  construction fails safe (skipped condition → visible in UAT as "shows everything" only if
  BOTH our envelope-level and the framework's own guard were bypassed — two independent
  layers). Malformed-envelope fail-closed hardening above is the one Critical-adjacent finding;
  fixed. No other scoping gaps found.
- **ADR-006 (Path C)**: confirmed compliant — the record-scoping mechanism is 100% within the
  sanctioned DataGrid web-resource framework (`hostFilters`/`additionalHostFilters`). The form
  OnLoad script does NOT implement a filter itself; it only resolves entity/id → field name and
  hands the scoping condition to the framework via the documented `setData()`/URL-envelope
  contract. See "ADR Tensions" note below for the deploy PR.
- **ADR-024 / NFR-06**: confirmed no second regarding mechanism (reused the existing 11-entry
  `sprk_regarding{type}` family; no new lookup fields) and no second grid-config default
  (reused `sprk_gridconfiguration` GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` unchanged).
- **ADR-021**: confirmed — `DataGridPageShell` already owns FluentProvider + theme
  resolution + listener; this task didn't touch theme handling. Dark-mode passthrough
  unaffected.
- **Minor** (not fixed, noted): `communicationsGridOnLoad.js` is hand-listed in
  `sprk_matter/tsconfig.json`'s `files` array (matches the existing repo convention for this
  folder) rather than driven by a glob — if a 12th onLoad script is added to this folder later,
  it must be added to that list too. Pre-existing pattern, not introduced by this task.

## Build result

`npm run build` (Vite production build) in `src/solutions/sprk_communicationspage/` —
**clean**, 3158 modules transformed, `dist/index.html` 1,643 kB / gzip 448.95 kB. Pre-existing
Rollup `#__PURE__` comment-position warnings from `@microsoft/applicationinsights-*` are
unrelated to this change (present before + after).

`tsc -p tsconfig.json` in `src/dataverse/forms/sprk_matter/` — clean, no errors (compiles the
new script alongside the existing sibling scripts).

Existing DataGrid framework jest suite (`Spaarke.UI.Components`, `--testPathPatterns=DataGrid`)
— 5 suites / 58 tests, all passing (regression smoke check; this task consumed existing
exports only, did not modify the framework).

`npx prettier --write` run on both changed/created `.ts`/`.tsx` files.

## Acceptance criteria

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | `fetchXmlOverlay.ts` confirmed to carry `sprk_regarding{type}` via `hostFilters` | ✅ MET | Read `fetchXmlOverlay.ts` — `HostFilterCondition.attribute` is an unconstrained string; no framework edit needed. |
| 2 | `sprk_communicationspage` renders the grid using the EXISTING config, accepts `hostFilters` from host | ✅ MET | `main.tsx` reuses `CONFIG_ID = e1826c4c-…` unchanged; new `additionalHostFilters` prop added. |
| 3 | Form onLoad script derives + scopes `sprk_regarding{type}`, parameterized across 11 entities | ✅ MET | `communicationsGridOnLoad.ts` `REGARDING_FIELD_MAP` (11 entries) + dynamic `getEntityName()`. |
| 4 | Matter form "Email & Messages" tab shows ONLY that Matter's communications, no PCF/bespoke resource | ✅ MET (design/build level) | `communicationsGridTab.FormXml.patch.xml` adds the tab + Web Resource control (classid `{9FDF5F91-…}`, the standard "Web Resource" control — not a PCF); actual live-form verification is task 034 (deploy) per this task's boundary (no browser/live env here). |
| 5 | No second regarding mechanism, no second grid-config default | ✅ MET | Reused `RegardingFieldMap` fields + reused config GUID; no new Dataverse schema, no new `sprk_gridconfiguration` record. |
| 6 | Dark mode via host FluentProvider, no console errors | ✅ MET (design level) | `DataGridPageShell` unchanged theme handling; live console-error verification is task 034 (ui-test requires a browser + deployed env, out of this task's tools). |

Note: criteria 4 and 6's LIVE verification (open a real Matter record, confirm cross-matter
isolation + console cleanliness in-browser) is explicitly task 034's job (deploy + UAT) per
this task's own `<ui-tests>` section and the "no live ui-tests here" instruction — this task's
scope was build-clean + design-correct, which is met.

## ADR-006 Path-C note (for the deploy PR, per CLAUDE.md §6.5)

**ADR-006 in question**: "New UI defaults to Custom Pages, NOT PCFs" / "MUST NOT use a PCF +
custom page wrapper when a Code Page achieves the same result."

**Tension**: the "Email & Messages" tab is NOT opened as a dialog/drill-through (the
established Code Page pattern) — it is mounted directly and persistently on a record form tab
via a "Web Resource" control, with a companion form `onLoad` script performing entity-aware
scoping logic (map lookup + `setData()` call).

**Resolution — Path C (pivot-to-comply reasoning, not a violation)**: this is squarely inside
the sanctioned DataGrid web-resource framework's designed host-surface set
(`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` §5 "Host surface patterns"
already lists form-embedded web-resource mounting as a first-class pattern alongside
drill-through Custom Pages and workspace widgets). The form `onLoad` script does not
reimplement grid/filter logic — it is a thin, documented conduit (resolve entity+id → map to
field name → call the framework's own `setData()`/hostFilters contract). No PCF was built; no
bespoke JS filter was written. This mirrors the established `communicationsGridOnLoad.ts`'s
sibling precedent in this same folder (`insightWidgetOnLoad.ts` / `insightCardMount.ts`),
which the codebase already accepts as compliant form-library scripting (distinct from ADR-006's
"ribbon/command scripts must be invocation-only" rule, which targets command-bar buttons, not
form OnLoad libraries).

**No escalation fired** — the POML's `<escalation>` trigger ("hostFilters cannot carry the
condition") did not apply; `fetchXmlOverlay.ts` fully supports the required condition with zero
framework changes.
