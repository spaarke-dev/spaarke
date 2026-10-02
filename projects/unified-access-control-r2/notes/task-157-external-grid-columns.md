# Task 157 — external grids lose the view selector; column allow-lists shrink

> **Status**: code complete on `task/uac-r2-157`. **Live steps PENDING** (deploy order in §5, manual gate in §6).
> **Closes**: task 134 open item D1. **Owner decision**: round 4 item 7 (2026-10-01): "Yes: set
> `showViewSelector=false` on the external SPA grids, so the column allow-lists can shrink to what the grids show."

## 1. What changed

| Piece | Where |
|---|---|
| The one Spaarke `<DataGrid>` mount in the external SPA passes `showViewSelector={false}`. Header comment says why it is a security setting. | `src/client/external-spa/src/widgets/GridWidgetBody.tsx` |
| Four module allow-lists shrink. Three are unchanged. The derivation comment drops rule (b) and warns that re-enabling a selector needs rule (b) back first. | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` |
| Section 6: each list pinned to its live derivation; shrink-only proven against the task-134 lists; every dropped column refused by `/fetch` and `/record $select` and stripped from a `/record` read | `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` |
| Arch guard: every Spaarke `DataGrid` mount under `src/client/external-spa/src` passes `showViewSelector={false}` as its last top-level attribute, with no spread after it. It fails closed (review round 1): any import of `DataGrid`/`DataGridDefault` (named, aliased, `default as`, default, mixed) is a mount source; the grid may appear only as a JSX tag; `DataGridPageShell(Default)`, namespace imports, re-exports that could carry the grid, dynamic `import()`/`require()` of the shared lib, and unparseable import clauses are refused. Negative and positive controls are included. | `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs` (new) |

The shared `DataGrid`'s default (`showViewSelector = true`) is **unchanged**. Internal surfaces keep the picker.

## 2. Premise corrections (POML vs. source)

1. **Only one file mounts the Spaarke `DataGrid`.** That file is `GridWidgetBody.tsx`, and every outside-counsel widget and the Service Requests widget resolve to it through `createGridWidgetBody`. The POML also lists `OutsideCounselDashboard.tsx`, `ContactsOrganizations.tsx` and `DocumentLibrary.tsx` as "modify". They mount **Fluent's** `DataGrid` (`@fluentui/react-components`), which has no view picker and no `showViewSelector` prop. They are not changed. The arch guard ignores Fluent's `DataGrid` by import source.
2. **The SPA has no `build:prod` script.** Its documented production build is `npm run build` (`vite build`), the same command `.github/workflows/deploy-external-spa.yml` runs.
3. Within `@spaarke/ui-components`, only `DataGridPageShell` wraps the shared `DataGrid`. It has no selector pass-through, so the guard forbids the external SPA from importing it.

## 3. Live re-derivation (dev `spaarkedev1`, READ-ONLY, 2026-10-01)

**Sources read:**
- the six `sprk_gridconfiguration` records (`sprk_configjson`). All are `source.type="inline"` and were last modified 2026-08-07..13, unchanged since task 134.
- `EntityDefinitions` `PrimaryIdAttribute` / `PrimaryNameAttribute` for the seven entities.
- `Attributes?$filter=IsSecured eq true` for the seven entities.

**Rule**: (a) every attribute the grid configuration references, (c) the scope attributes, and (d) the `/record` default projection (primary id and primary name). For (a), "references" means the FetchXML `attribute`, `condition` and `order` elements, the layoutXml `cell` elements, the configjson `columns` keys, and `filterChips`. No config declares `filterChips`, so the grid falls back to text chips over its own visible columns. Rule (b), the sibling-view columns, is **retired**.

**Escalation trigger: did NOT fire.** No grid references a pointer column (`ExternalModuleRegistry.PointerColumns`). No module entity has any FLS-secured attribute (`IsSecured eq true` returned none for all seven). No grid uses an alias or an aggregate.

| Module / entity | (a) grid config columns | (c) scope | (d) id / name | New list | Dropped (rule (b) only) |
|---|---|---|---|---|---|
| collaboration / `sprk_project` (61711823) | sprk_projectid, sprk_projectname, sprk_projectnumber, statuscode, modifiedon | sprk_projectid | sprk_projectid / sprk_projectnumber | the 5 (a) columns | statecode, createdon, ownerid, sprk_practicearea, sprk_projecttype_ref |
| documents / `sprk_document` (3af4102c) | sprk_documentid, sprk_documentname, sprk_documenttype, createdon, sprk_project, sprk_matter, sprk_workassignment | sprk_project, sprk_matter, sprk_workassignment | sprk_documentid / sprk_documentname | unchanged (7) | none |
| invoices / `sprk_invoice` (3ff4102c) | sprk_invoiceid, sprk_invoicenumber, sprk_invoicedate, sprk_invoicestatus, sprk_totalamount, sprk_project, sprk_matter | sprk_matter, sprk_project | sprk_invoiceid / **sprk_name** (not on the grid; kept by (d)) | (a) + sprk_name (8) | sprk_visibilitystate, modifiedon, statecode |
| work-assignments / `sprk_workassignment` (42f4102c) | sprk_workassignmentid, sprk_workassignmentnumber, sprk_priority, sprk_responseduedate, statuscode, sprk_regardingproject | sprk_workassignmentid | sprk_workassignmentid / **sprk_name** (kept by (d)) | (a) + sprk_name (7) | statecode, createdon, ownerid, sprk_assignedto |
| matters / `sprk_matter` (583a2a33) | sprk_matterid, sprk_mattername, sprk_matternumber, statuscode | sprk_matterid | sprk_matterid / sprk_matternumber | the 4 (a) columns | statecode, createdon, sprk_mattertype, sprk_practicearea |
| service-requests / `sprk_servicerequest` (403e5d37) | sprk_servicerequestid, sprk_servicerequestnumber, sprk_name, statuscode, createdon, sprk_requestedby | sprk_requestedby | sprk_servicerequestid / sprk_name | unchanged (6) | none |
| grid-configuration / `sprk_gridconfiguration` | (no grid) the shared DataGrid's `retrieveRecord(…, ['sprk_configjson'])` | sprk_gridconfigurationid | sprk_gridconfigurationid / sprk_name | unchanged (3) | none |

**Drop set: 16 columns across 4 modules.** Every new list is a subset of the task-134 list, so nothing was added. `LiveDerivation_ComparedWithTask134_OnlyShrinksAndDropsExactlyTheRuleBColumns` pins this.

Primary id / name (live, unchanged from task 134 §8): project → sprk_projectnumber; document → sprk_documentname; invoice, work assignment, service request and grid configuration → sprk_name; matter → sprk_matternumber.

## 4. Decisions

- **G1: where the "no selector" assertion lives.** The SPA has no test runner, so the assertion is a source-scan arch guard in `tests/Spaarke.ArchTests`. That is the established home for client↔server agreement guards (`ClientUploadRouteAgreementTests`, `SpeAdminClientRouteAgreementTests`). It is a test file, not new product surface, so no §11 justification is needed.
- **G2: the guard strips comments before scanning.** Its first run flagged `GridWidgetBody.tsx`'s own JSDoc (`<DataGrid configId=… />`). Block comments keep their newlines so line numbers stay true. **Revised in review round 1:** only comments that START their line are stripped (a `//` line, a `/* … */` or JSX `{/* … */}` block opening the line). The first version also stripped a `/*` or a whitespace-preceded `//` mid-line, so a string such as `'src/**/*.ts'` or `'a // b'` could blank real code (fail open). Now a mid-line marker is left alone; the cost is fail-closed (a mount quoted in a trailing comment is reported). Controls cover both string cases and the URL case.
- **G4: the guard fails closed (review round 1).** The verifier seeded nine files, covering eight evasion shapes, that the first version passed: (A) default import, (B) the barrel's `DataGridDefault`, (C) mixed default+named import, (D) `showViewSelector={false}` followed by `{...rest}`, (E) `DataGridPageShellDefault`, (F) a local re-export file, (G) `React.createElement(DataGrid, …)`, (H) `React.lazy(() => import(…DataGrid))`. The rewritten detector treats every import of `DataGrid`/`DataGridDefault` from the shared lib (package alias or a path into `Spaarke.UI.Components`) as a mount source, requires the LAST top-level `showViewSelector` to be `{false}` with no spread after it, refuses any non-JSX reference to the grid binding (createElement, aliasing, `export { X }`, `export default X`, HOCs), and refuses `DataGridPageShell(Default)`, namespace imports, re-exports that could carry the grid, any dynamic `import()`/`require()` of the shared lib and any import clause it cannot parse. Each shape has a Theory control row. The same nine files were re-seeded under `src/client/external-spa/src/zzseed`. The production fact went red with eight violations, one per shape; F was caught through `F_reexport.ts`. They were then deleted. P1 was re-run against the new detector: red at `GridWidgetBody.tsx:71`, then restored and touched.
- **G3: the `/record` tests use a caller granted the id as project, matter and work assignment at once.** Row scope then passes for every module, including invoices, whose record gate checks the scope dimensions. Any refusal or strip in those tests can only come from the column scope.
- **No new service, DI registration, endpoint, option, job or package.** This change edits data in existing registrations, one prop on an existing mount, and tests. Placement: the column scope stays in the BFF's existing external read seam (task 134 §5). NuGet: no change.

## 5. Deployment order (LIVE STEP — not run here; main session runs it)

The external SPA must deploy **BEFORE, or together with**, the BFF that carries the shrunk lists. If the BFF goes first, a still-offered view errors: a user who picks an internal view in the old SPA's selector gets `DV_FETCHXML_COLUMN_NOT_PERMITTED` instead of rows. The reverse order is safe. The new SPA only sends each grid's own FetchXML, and the old BFF still admits those columns.

1. **SPA** (Azure Static Web Apps `swa-spaarke-external-spa-dev`, manual workflow):
   `gh workflow run deploy-external-spa.yml --ref <branch-or-master-containing-157>`
   This workflow builds with `npm install --legacy-peer-deps --no-audit --no-fund` and `npm run build`. The Teams tab serves the same SPA, so no separate Teams deploy is needed for this change.
2. Verify: open any external grid. No view picker shows in the header.
3. **BFF**: `pwsh scripts/Deploy-BffApi.ps1` (the `bff-deploy` skill) from the same commit or later.
4. Run the §6 gate.

## 6. MANUAL live gate — PENDING

On dev, after deploying the SPA and then the BFF, sign in as an outside-counsel contact (a CIAM test contact with grants). Ask the owner for a test user if no suitable one exists (round 4 item 1).
1. Open Projects, Documents, Invoices, Work Assignments and Matters. Then, as a workforce user in Teams, open Service Requests. For each:
   - no view selector shows;
   - each grid renders its configured columns and the same rows as before;
   - sorting and header filters still work.
2. Hand-craft `POST /api/v1/external/api/dataverse/fetch` for `sprk_workassignment` selecting `sprk_assignedto`. Expect **400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`**. Before task 157 this returned 200.
3. Record screenshots and response captures here.

## 7. Verification

**Perturbations, all reverted (hash-compared to backups, then touched):**

| Seeded violation | Tests turned red |
|---|---|
| P1: `showViewSelector={false}` removed from `GridWidgetBody.tsx` | 1: the arch guard, which names `GridWidgetBody.tsx:71` |
| P2: `ownerid` put back on the production project list | 4: the equality pin, plus the fetch-refusal, record-`$select`-refusal and record-strip rows for (`sprk_project`, `ownerid`) |
| P3: grid column `modifiedon` removed from the production project list (a grid would blank) | 7: the equality pin, the live-grid-FetchXML-admitted test, and the 5 project fetch-refusal rows, whose positive control (the grid's own FetchXML must still run) fails first |

**Runs (review round 1, `task/uac-r2-157-r1`, 2026-10-02):**

| Suite | Result |
|---|---|
| `ExternalSpaGridViewSelectorGuardTests` | 29 / 29 passed (8 facts + 19 evasion Theory rows + 2 new controls) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 370, Failed 0, Total 370 (349 at `3d858490b` + 21 new control rows) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 13627, Failed 0, Skipped 54, Total 13681 (Debug, 20m24s); the verifier's Release run at `3d858490b` gave the same counts |
| SPA `npm run build` (vite) | exit 0 at `3d858490b` (verifier); no client file changed in round 1 |

## 8. Read-only findings (not acted on; out of scope)

- **F1: the external SPA still fetches the internal sibling-view list.** With the selector off, `DataGrid.tsx` still calls `retrieveSavedQueriesForEntity` (`GET /api/v1/external/api/dataverse/savedqueries/{entity}`) on every grid load and discards the result. The external `savedqueries` and `savedquery/{id}` routes return internal view definitions (names, FetchXML, layoutXml) to external callers app-only. They carry no record data, and `/fetch` refuses any of those queries that names a non-allow-listed column. But the internal view design stays visible, and each load makes a wasted call. Possible follow-up, needing an owner decision: skip the sibling fetch when `showViewSelector` is false (a shared-lib change), and/or restrict the external `savedquer*` routes to the module grids' own needs.
- **F2: pre-existing SPA typecheck debt.** `npx tsc --noEmit` over the SPA reports 24 errors. All are the same React-types mismatch class (TS2786 / TS2322 between the SPA's `@types/react` 18 and the shared lib's types), including the existing `<DataGrid>` line in `GridWidgetBody.tsx`. None involves `showViewSelector`. `vite build` does not typecheck, and it succeeds.
