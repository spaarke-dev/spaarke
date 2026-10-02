# Task 157 — external grids lose the view selector; column allow-lists shrink

> **Status**: code complete on `task/uac-r2-157` (review rounds 1–3 on `-r1`, `-r1-r2`, `-b1`/`-b2`). **Live steps PENDING** (deploy order in §5, manual gate in §6).
> **Closes**: task 134 open item D1. **Owner decision**: round 4 item 7 (2026-10-01): "Yes: set
> `showViewSelector=false` on the external SPA grids, so the column allow-lists can shrink to what the grids show."

## 1. What changed

| Piece | Where |
|---|---|
| The one Spaarke `<DataGrid>` mount in the external SPA passes `showViewSelector={false}`. Header comment says why it is a security setting. **Review round 3:** the mount moved into a runtime guard: `GridWidgetBody` now mounts `ExternalDataGrid`. | `src/client/external-spa/src/widgets/GridWidgetBody.tsx` |
| **Review round 3 (new):** `ExternalDataGrid`, the only external-SPA import path to the shared grid. It renders the grid with `showViewSelector={false}` AFTER the caller's props, so no prop, spread, `cloneElement` or `createElement` can turn the picker on. Its props type omits `showViewSelector`. | `src/client/external-spa/src/widgets/ExternalDataGrid.tsx` (new) |
| Four module allow-lists shrink. Three are unchanged. The derivation comment drops rule (b) and warns that re-enabling a selector needs rule (b) back first. | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` |
| Section 6: each list pinned to its live derivation; shrink-only proven against the task-134 lists; every dropped column refused by `/fetch` and `/record $select` and stripped from a `/record` read | `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` |
| Arch guard: every Spaarke `DataGrid` mount under `src/client/external-spa/src` passes `showViewSelector={false}` as its last top-level attribute, with no spread after it. It fails closed (review round 1): any import of `DataGrid`/`DataGridDefault` (named, aliased, `default as`, default, mixed) is a mount source; the grid may appear only as a JSX tag; `DataGridPageShell(Default)`, namespace imports, re-exports that could carry the grid, dynamic `import()`/`require()` of the shared lib, and unparseable import clauses are refused. Review round 2 adds: generic mounts are refused; comments are scanned as code; the tag walk is string-aware and refuses what it cannot follow; and the shared-library path is matched anywhere in the specifier (§7). Review round 3 adds: only `ExternalDataGrid.tsx` may import the grid, and its text is pinned; module-specifier and dynamic-load rules; `import.meta.glob`, `cloneElement`/`createElement` and `\u` escapes are refused; a shared-library fan-in pin (§7). Fix round b2 adds: side-effect imports get the specifier rules, a bare specifier must be a declared package, `eval`/`Function` are refused, and `index.html` may only load scripts from `src/` (§7). Negative and positive controls are included. | `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs` (new) |

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
- **G2: the guard strips comments before scanning.** Its first run flagged `GridWidgetBody.tsx`'s own JSDoc (`<DataGrid configId=… />`). Block comments keep their newlines so line numbers stay true. **Revised in review round 1:** only comments that START their line are stripped (a `//` line, a `/* … */` or JSX `{/* … */}` block opening the line). The first version also stripped a `/*` or a whitespace-preceded `//` mid-line, so a string such as `'src/**/*.ts'` or `'a // b'` could blank real code (fail open). Now a mid-line marker is left alone; the cost is fail-closed (a mount quoted in a trailing comment is reported). Controls cover both string cases and the URL case. **Revised again in review round 2: comments are no longer stripped at all.** Inside JSX children, a line starting `//` or `/*` is text, so a mount on that line renders live (shapes S2/S3, see §7). Every comment is now scanned as code. GridWidgetBody's JSDoc was reworded so that no comment names the binding.
- **G4: the guard fails closed (review round 1).** The verifier seeded nine files, covering eight evasion shapes, that the first version passed: (A) default import, (B) the barrel's `DataGridDefault`, (C) mixed default+named import, (D) `showViewSelector={false}` followed by `{...rest}`, (E) `DataGridPageShellDefault`, (F) a local re-export file, (G) `React.createElement(DataGrid, …)`, (H) `React.lazy(() => import(…DataGrid))`. The rewritten detector treats every import of `DataGrid`/`DataGridDefault` from the shared lib (package alias or a path into `Spaarke.UI.Components`) as a mount source, requires the LAST top-level `showViewSelector` to be `{false}` with no spread after it, refuses any non-JSX reference to the grid binding (createElement, aliasing, `export { X }`, `export default X`, HOCs), and refuses `DataGridPageShell(Default)`, namespace imports, re-exports that could carry the grid, any dynamic `import()`/`require()` of the shared lib and any import clause it cannot parse. Each shape has a Theory control row. The same nine files were re-seeded under `src/client/external-spa/src/zzseed`. The production fact went red with eight violations, one per shape; F was caught through `F_reexport.ts`. They were then deleted. P1 was re-run against the new detector: red at `GridWidgetBody.tsx:71`, then restored and touched.
- **G5: a runtime guard, because a source scan is not a proof (review round 3).** The round-2 scan passed three shapes the verifier compiled with `vite build` into a live mount with the picker on: `import.meta.glob` of `DataGrid.tsx` (V1), a unicode-escaped identifier `DataGrid` (V2), and `React.cloneElement(<compliant mount>, { showViewSelector: true })` (V3). V3 cannot be caught by a regex in general: `(React as any)['clone' + 'Element']` spells nothing. So the guarantee moved to runtime:
  - `ExternalDataGrid.tsx` renders `<DataGrid {...props} showViewSelector={false} />`. The bundle shows `e=>u.jsx(ole,{...e,showViewSelector:!1})`, and `showViewSelector:!0` occurs nowhere in `dist/assets/app.js`. A clone or `createElement` of `ExternalDataGrid` sets the WRAPPER's props, and the forced `false` wins.
  - The scan's job is now narrow: only the wrapper may import a grid binding (a fully compliant mount anywhere else is refused), and the wrapper's text is pinned, whitespace-insensitive. A computed clone inside the wrapper (P5 below) passes every regex rule but fails the pin.
  - The scan's cheap rules stay as defence in depth. Every `from '…'` must be escape-free, attributed to a parsed import when it names the library, and resolve inside the SPA source or into the library. Every `import()` / `require()` takes one plain local literal. `import.meta.glob` is refused in any form. In a grid-binding file, `cloneElement`, `createElement` and `\u` are refused.
  - **Shared-library fan-in pin.** The scan refuses `DataGridPageShell` by name, and that only works while it is the one shared component that mounts the grid. `SharedLibrary_OnlyKnownModulesCarryTheGrid` pins the four modules that import or re-export it (`DataGrid/index.ts`, `DataGridPageShell.tsx`, `components/index.ts`, `src/index.ts`).
  - §11 justification for the new file: see the POML round-3 outcome.
  - **Fix round b2** found and closed a fourth compiled shape (V4): a side-effect `import` of a module outside `src/` that parks the grid on `window`. Side-effect imports now get the specifier rules. Also new: a bare specifier must be a package declared in `package.json`; `eval` / `Function` are refused; `index.html` may only load scripts from `src/` (§7).
  - **Residuals** (the scan is not a proof; full list in §7 and in the guard's remarks): a new npm dependency that re-exports the grid; a `vite.config.ts` alias or plugin that re-points a declared name; a shared-library `import()` built from a variable; edits to the shared `DataGrid.tsx`; runtime reflection over React internals.

    The first four are reviewed changes outside `external-spa/src`. In every case the BFF allow-list still refuses the internal views' columns with a 400.
- **G3: the `/record` tests use a caller granted the id as project, matter and work assignment at once.** Row scope then passes for every module, including invoices, whose record gate checks the scope dimensions. Any refusal or strip in those tests can only come from the column scope.
- **No new service, DI registration, endpoint, option, job or package.** This change edits data in existing registrations, one prop on an existing mount, and tests. Review round 3 adds one 20-line client component (`ExternalDataGrid.tsx`, the runtime guard; §11 justification in the POML round-3 outcome) and a comment-only BFF edit (no IL change). Placement: the column scope stays in the BFF's existing external read seam (task 134 §5). NuGet: no change.

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

**Review round 2 (`task/uac-r2-157-r1-r2`, 2026-10-02): the guard still failed open on five shapes.** All five compiled
(esbuild) into a live mount with the picker on, and all five are now refused:

| Shape | Why it passed | Fix |
|---|---|---|
| S1 `<DataGrid<any> configId="x" />` | the mount regex needed whitespace, `/` or `>` after the name | a mount is `<X` followed by anything that cannot continue the identifier; type arguments are refused (the scan cannot tell where they end) |
| S2 / S3: a JSX-children line starting `//` or `/*` that quotes a mount | inside JSX children such a line is TEXT and the element renders, but the guard stripped it as a comment | comments are no longer stripped: every comment is scanned as code. GridWidgetBody's JSDoc was reworded (doc-only) so no comment names the binding |
| S4 `<DataGrid showViewSelector={false} configId="{" {...p} />` | the `{` in the string shifted the brace depth, hiding the later spread | the tag walk skips string literals as strings and refuses any string holding `{ } < >` (attribute) or `{ }` (inside a prop expression) |
| S5 `import { DataGrid } from '../../node_modules/@spaarke/ui-components/…'` | the shared-library pattern matched the alias only at the START of the specifier | the specifier may name `@spaarke/ui-components` or `Spaarke.UI.Components` anywhere |

The string-aware walk also closes neighbours found while fixing S4: a `>` in a string attribute ending the tag
early, a comment between attributes (`/* > */`), a bare JSX element as an attribute value (`empty=<X/>`), a
bare `showViewSelector` after the false one (it is `true`), a namespaced `x:showViewSelector`, a spread
straight after the name (`<DataGrid{...p}/>`), and a tag that never closes. A `/` anywhere in a grid tag except
its closing `/>` is refused, so complex prop values must be hoisted to a const.

Proof it bites: seeded under `external-spa/src/zzseed` (S1–S5 as the verifier wrote them, plus five extras), the
production fact went red with 10 violations, one per seeded mount; seeds deleted. P1 re-run: red at
`GridWidgetBody.tsx:73`, restored and touched.

**Runs (review round 2):**

| Suite | Result |
|---|---|
| `ExternalSpaGridViewSelectorGuardTests` | 45 / 45 passed (29 + 16 round-2 Theory rows; the comment control now asserts comments ARE reported; the sanctioned control now has 2 compliant mounts) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 386, Failed 0, Total 386 (370 + 16) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 13627, Failed 0, Skipped 54, Total 13681 (Debug, 22m15s) |
| SPA `npm run build` (vite) | exit 0 (GridWidgetBody.tsx changed in comments only) |

**Review round 3 (fix rounds `task/uac-r2-157-b1` and `-b2`, 2026-10-02): the guard failed open a third time.**
Three shapes compiled (`vite build`) into a live mount with the picker on. The fix is a runtime guard plus a narrower
scan (decision G5). Fix round b1 wrote it and was cut off by a usage limit before verifying anything. Fix round b2
re-checked b1's work, re-ran every proof below itself, and closed one more compiled bypass (V4).

| Shape | Why it passed | Fix |
|---|---|---|
| V1 `import.meta.glob('…/DataGrid/DataGrid.tsx', { eager: true, import: 'DataGrid' })` | the dynamic-load rule matched only `import(` / `require(` | `import.meta.glob` is refused in any form, because its pattern need not name the library. Every `import()` / `require()` must take one plain literal that resolves inside the SPA source or names a declared package |
| V2 `const G = Data\u0047rid` | the reference rule matched only the literal name | `\u` is refused in a grid-binding file, and only the wrapper may hold a grid binding. Escaped module specifiers and string-named import specifiers are refused too |
| V3 `React.cloneElement(<compliant mount>, { showViewSelector: true })` | the tag walk never sees the clone. In general a regex cannot catch it (`React['clone' + 'Element']`) | **runtime:** `ExternalDataGrid` forces `false` after the caller's props, and its text is pinned. `cloneElement` / `createElement` are also refused in a grid-binding file |
| **V4 (found in b2)** `import '../../zzoutside/gridGlobal'`, a module outside `src/` that imports the grid and sets `window.SprkGrid`; then `<G configId="x" />` with `G = window.SprkGrid` | the specifier rules read only `from '…'`; a side-effect import has no `from` and no binding name | side-effect imports get the same specifier rules (b2) |

The classes behind them are closed as well:
- a module outside the SPA source that could re-export the grid, or park it on a global: relative, `/` and `@/` specifiers, in `from '…'` AND side-effect `import '…'`, must resolve inside the SPA source or into the library;
- another `@spaarke/*` package;
- **(b2)** any other bare specifier must name a package declared in `external-spa/package.json`. A resolve alias, a `#…` subpath import or a `node:` builtin is refused;
- **(b2)** `eval(…)` and the `Function` constructor are refused (code built from a string can import anything);
- **(b2)** every `<script>` in `external-spa/index.html` must load a file under `src/` and hold no inline code. Vite bundles every entry it names, and the scan reads only `src/`;
- a new shared component that embeds the grid: the fan-in pin.

Proof it bites (fix round b2's own runs; each seed was deleted or restored with `git checkout`, then touched; the
temporary `main.tsx` imports used to make Vite bundle the seeds were reverted the same way):

| Seeded | Result |
|---|---|
| `vite build` with five seeds wired into `main.tsx`: V1, V2, V3, V4, and a clone of `ExternalDataGrid` with `showViewSelector: true` | V3 and V4 compile to live picker-on mounts over the shared grid: `window.SprkGrid=<grid>; … u.jsx(<that global>,{configId:"x"})` and `cloneElement(u.jsx(<grid>,{…,showViewSelector:!1}),{showViewSelector:!0})`. (The verifier had already compile-proven V1 and V2.) The `ExternalDataGrid` clone compiles to `cloneElement(<wrapper element>,{showViewSelector:!0})`. The wrapper renders `u.jsx(<grid>,{...e,showViewSelector:!1})`, so the forced `false` wins |
| The same files under `src/zzseed`, with the round-3 guard **before** the b2 change | production fact red on V1, V2 and V3 only. **V4 passed**: this is the b2 bypass. (In this run V2 held a plain `DataGrid`, because the file-writing tool had decoded the `\u0047`; it was red for importing the grid outside the wrapper. It was then rewritten with the literal escape for the next row) |
| The same files, after the b2 change | production fact red on V1 (`import.meta.glob`), V2 (import outside the wrapper and `\u`), V3 (import outside the wrapper and `cloneElement`) and V4 (`a module outside the external SPA source`). The `ExternalDataGrid` clone is correctly NOT flagged |
| `src/zzseed/Z3_subpath.tsx` `import { Grid } from '#grid'`, plus a second `<script src="/zzoutside/gridGlobal.ts">` in `index.html` | production fact red on both (`undeclared bare specifier`; `index.html: <script …> must load a file under src/`) |
| P4: spread moved after the forced prop in the wrapper (`<DataGrid showViewSelector={false} {...props} />`) | 2 red: the production fact (`ExternalDataGrid.tsx:20`) and the pin |
| P1: `GridWidgetBody` imports and mounts the shared grid directly, with compliant JSX | 1 red: the production fact (`GridWidgetBody.tsx:39 … outside ExternalDataGrid.tsx`) |
| P6: a new shared component `components/ZzSeed/ZzSeed.tsx` imports `DataGrid as Table` from `../DataGrid` | 1 red: the fan-in fact (`ZzSeed.tsx:2`) |

P5 (a computed clone inside the wrapper) was claimed by b1 and not re-run: any change to the wrapper's text fails
the whitespace-insensitive pin, which P4 shows.

**Residuals (documented in the guard's remarks). The scan is not a proof:**
1. a NEW npm dependency that bundles and re-exports the grid;
2. build configuration (a `vite.config.ts` alias or plugin) that re-points a DECLARED name at another file;
3. a shared-library `import()` whose path is built in a variable elsewhere;
4. edits to the shared `DataGrid.tsx` itself;
5. runtime reflection over React internals (a fiber walk from a rendered `ExternalDataGrid` to the inner component type, then `createElement` of it). This is deliberate obfuscation, and no fixed spelling identifies it.

Items 1–4 are reviewed changes outside `external-spa/src`. In every case the BFF allow-list still refuses every
internal-view column with a 400. The guard removes the pressure to widen that list; it is not the data control.

**Runs (review round 3, fix round b2, on `task/uac-r2-157-b2` after merging `work/unified-access-control-r2` at `1f3e7bd76`):**

| Suite | Result |
|---|---|
| `ExternalSpaGridViewSelectorGuardTests` | 81 / 81 passed: 72 from b1 + 8 round-4 Theory rows (Z1–Z8) + 1 `index.html` control |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 426, Failed 0, Total 426 |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14274, Failed 0, Skipped 54, Total 14328 (Debug, 18m54s) |
| SPA `npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` (vite) | exit 0. The bundle holds `nle=e=>u.jsx(ole,{...e,showViewSelector:!1})` and `showViewSelector:!0` occurs 0 times. `npx tsc --noEmit`: 6 errors, all pre-existing (`mock-data.ts`, `OutsideCounselDashboard.tsx`, shared `EntityCreationService.ts`), none in a changed file. The npm-touched `package-lock.json` was reverted |

## 8. Read-only findings (not acted on; out of scope)

- **F1: the external SPA still fetches the internal sibling-view list.** With the selector off, `DataGrid.tsx` still calls `retrieveSavedQueriesForEntity` (`GET /api/v1/external/api/dataverse/savedqueries/{entity}`) on every grid load and discards the result. The external `savedqueries` and `savedquery/{id}` routes return internal view definitions (names, FetchXML, layoutXml) to external callers app-only. They carry no record data, and `/fetch` refuses any of those queries that names a non-allow-listed column. But the internal view design stays visible, and each load makes a wasted call. Possible follow-up, needing an owner decision: skip the sibling fetch when `showViewSelector` is false (a shared-lib change), and/or restrict the external `savedquer*` routes to the module grids' own needs.
- **F2: pre-existing SPA typecheck debt.** `npx tsc --noEmit` over the SPA reports 24 errors. All are the same React-types mismatch class (TS2786 / TS2322 between the SPA's `@types/react` 18 and the shared lib's types), including the existing `<DataGrid>` line in `GridWidgetBody.tsx`. None involves `showViewSelector`. `vite build` does not typecheck, and it succeeds.
