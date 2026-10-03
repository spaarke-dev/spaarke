# Task 157 — external grids lose the view selector; column allow-lists shrink

> **Status**: code complete on `task/uac-r2-157` (review rounds 1–6 on `-r1`, `-r1-r2`, `-b1`/`-b2`, `-b2-r1`, `-b2-r2`; round 7 on `-c1`; round 8 on `-c1-r1`; residual 4 put in CI on `-c2`; its CI self-protection hardened on `-c2-r1`; made advisory, with the router left unchanged, on `-c2-r2`). **Live steps PENDING** (deploy order in §5, manual gate in §6).
>
> **Fix round c2-r2 (2026-10-03): owner round 13 items 11 and 12 applied. Supersedes the "BLOCKING" and router parts of the c2 and c2-r1 paragraphs below.** Item 11: `datagrid-external-host-gate` lands **ADVISORY** (job-level `continue-on-error: true`), as `office-addins-tests.yml` and the compose client gate did, and becomes blocking after **N = 3** consecutive green runs on `ubuntu-latest`; the flip procedure is in §7, "Fix round c2-r2". Item 12: `ci-router.yml` is **not changed** by this project. The c2 / c2-r1 router edits are reverted (the file is byte-identical to its pre-task content); the gate is classified inside Tier 1's `classify-tier1` instead. The router's pre-existing `docs_only` skip stays open and belongs to `ci-cd-unit-test-remediation-r1`; **the main session files a GitHub issue there** (§8 F3). So residual 4 is now *reported* by CI, not refused, until the flip. The owner's open item from c2-r1 (the unconfirmed waiver of "N green runs") is answered by item 11 and closed.
>
> **Fix round c2 (2026-10-03): residual 4 CLOSED** *(blocking claim superseded by c2-r2 above)*. The shared DataGrid's jest folder, `DataGrid.externalHost.test.tsx` included, runs as a BLOCKING Tier 1 job (`datagrid-external-host-gate`), classified by the router. Seed J4 now fails CI. See §7, "Fix round c2".
>
> **Fix round c2-r1 (2026-10-03): review c2's findings closed** *(router changes reverted in c2-r2)*. Review c2 seeded four actionlint-clean disarmings that left the c2 guard green. These were a `needs:` on a skipped job, a step-level `if:`, `ui_components='false'` in the router's `docs_only` step, and `if (false)` in the assertion. The guard now refuses each of them, proven on disk. The router also counts `src/client/external-spa/**` and `tests/Spaarke.ArchTests/**` as code, so a PR that edits either plus a task note no longer skips Tier 1. The disclosure now states the verdict change, and `datagrid-jest-results.json` is gitignored. The job has run under Node 20 locally, but not yet on `ubuntu-latest`; the owner has not confirmed that waiver. See §7, "Fix round c2-r1".
>
> **Fix round c1 (2026-10-03):** the invariant now holds BY CONSTRUCTION AT RUNTIME. The shared `DataGrid` itself refuses the view picker, and never requests the saved-query list, inside the external SPA (decision G6, §4). Criterion 1 was amended to the owner's goal (POML `criterion-amendment`). F1 (§8) is closed: the grid no longer lists saved queries with the picker off, and the BFF's external savedquery routes return only registered views (404 otherwise).
> **Closes**: task 134 open item D1. **Owner decision**: round 4 item 7 (2026-10-01): "Yes: set
> `showViewSelector=false` on the external SPA grids, so the column allow-lists can shrink to what the grids show."

## 1. What changed

| Piece | Where |
|---|---|
| The one Spaarke `<DataGrid>` mount in the external SPA passes `showViewSelector={false}`. Header comment says why it is a security setting. **Review round 3:** the mount moved into a runtime guard: `GridWidgetBody` now mounts `ExternalDataGrid`. | `src/client/external-spa/src/widgets/GridWidgetBody.tsx` |
| **Review round 3 (new):** `ExternalDataGrid`, the only external-SPA import path to the shared grid. It renders the grid with `showViewSelector={false}` AFTER the caller's props, so for every JSX mount no prop or spread, and no `cloneElement` / `createElement` of an `<ExternalDataGrid>` ELEMENT, can turn the picker on. Its props type omits `showViewSelector`. **Fix round b2-r1:** it does NOT protect the element it returns when called as a plain function (Q1/Q2, §7), so the guard now refuses any non-JSX use of it; the JSDoc says so. | `src/client/external-spa/src/widgets/ExternalDataGrid.tsx` (new) |
| Four module allow-lists shrink. Three are unchanged. The derivation comment drops rule (b) and warns that re-enabling a selector needs rule (b) back first. | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` |
| Section 6: each list pinned to its live derivation; shrink-only proven against the task-134 lists; every dropped column refused by `/fetch` and `/record $select` and stripped from a `/record` read | `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` |
| Arch guard: every Spaarke `DataGrid` mount under `src/client/external-spa/src` passes `showViewSelector={false}` as its last top-level attribute, with no spread after it. It fails closed (review round 1): any import of `DataGrid`/`DataGridDefault` (named, aliased, `default as`, default, mixed) is a mount source; the grid may appear only as a JSX tag; `DataGridPageShell(Default)`, namespace imports, re-exports that could carry the grid, dynamic `import()`/`require()` of the shared lib, and unparseable import clauses are refused. Review round 2 adds: generic mounts are refused; comments are scanned as code; the tag walk is string-aware and refuses what it cannot follow; and the shared-library path is matched anywhere in the specifier (§7). Review round 3 adds: only `ExternalDataGrid.tsx` may import the grid, and its text is pinned; module-specifier and dynamic-load rules; `import.meta.glob`, `cloneElement`/`createElement` and `\u` escapes are refused; a shared-library fan-in pin (§7). Fix round b2 adds: side-effect imports get the specifier rules, a bare specifier must be a declared package, `eval`/`Function` are refused, and `index.html` may only load scripts from `src/` (§7). Fix round b2-r1 adds: the wrapper may be used only as a JSX tag outside its own file; a library-naming specifier (the alias resolved, not trusted) must land in the library's shipped `src`, not beside it and not in a test / `__*` path; shipped library files may not load such modules either; `?query` / `#fragment` and bare `..` segments are refused (§7, round 5). Fix round b2-r2 adds: a comment between `from` / `import` / `require` and the specifier or `(` is refused (one such comment hid an import from every rule); JSX pragmas are refused; `\u` escapes and a literal U+FEFF are refused in every SPA file; `import.meta.glob` is matched across comments; no `node_modules` folder may exist under, or be imported from, a scanned `src`; shipped library files get the comment, pragma and `node_modules` rules too (§7, round 6). Negative and positive controls are included. | `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs` (new) |

**Fix round c1 (2026-10-03) adds:**

| Piece | Where |
|---|---|
| **New:** `DataGridExternalHost` — the external-host switch. A React context (default `false`), its provider (`true`), the build constant `__SPAARKE_DATAGRID_EXTERNAL_HOST__` read behind a `typeof` guard, and `useDataGridExternalHost()` that ORs them. | `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGridExternalHost.tsx` (new) |
| The shared grid applies the rule: under the external host, `showViewSelector` is forced off (so `externalViews` is ignored too), a picked view is never honoured, a `savedquery-set` source is refused with an error, and `retrieveSavedQueriesForEntity` is never called. On every host, the sibling saved-query list is fetched only when the picker is on (F1 (a)). | `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` |
| The SPA's root component mounts `<DataGridExternalHostProvider>` around its whole tree, above the one `<BrowserRouter>`. | `src/client/external-spa/src/App.tsx` |
| The SPA's build defines the constant as `true`, so every copy of the grid in the bundle is external, whatever tree, React root or module instance renders it. | `src/client/external-spa/vite.config.ts` |
| `ExternalDataGrid` stays as defence in depth; its JSDoc now says the rule lives in the shared grid (code tokens unchanged; pin updated). `GridWidgetBody` JSDoc reworded the same way (comments only). | `src/client/external-spa/src/widgets/ExternalDataGrid.tsx`, `GridWidgetBody.tsx` |
| BFF, F1 (b): `ExternalModuleDescriptor.SavedQueryIds` (the views a module's grid is registered to use; empty by default) and `ExternalModuleRegistry.FindBySavedQueryId`. `GET …/savedquery/{id}` returns only a registered view whose entity is the registering module's, and `GET …/savedqueries/{entity}` returns only the module's registered views; everything else is the same 404 (`DV_SAVEDQUERY_NOT_FOUND`), with no Dataverse read. Every module registers none (all six grids are inline, read live 2026-10-02). | `ExternalModuleRegistry.cs`, `ExternalModuleDataEndpoints.cs`, `ExternalAccessModule.cs` |
| Tests: the jest suite (7 tests); the guard gains the provider, define and rule-pin facts, the round-7 import rule and their controls (60 new rows); the BFF gains `ExternalModuleSavedQueryScopeTests` (28) and 3 HTTP contract rows (a fourth in fix round c1-r1: the by-id route must 404 an internal `sprk_project` view the app really serves, §7). | `…/DataGrid/__tests__/DataGrid.externalHost.test.tsx` (new), `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs`, `tests/integration/auth/UnifiedAccessControl/ExternalModuleSavedQueryScopeTests.cs` (new), `tests/integration/contract/Api/ExternalAccess/ExternalModuleDataContractTests.cs` |

The shared `DataGrid`'s default (`showViewSelector = true`) is **unchanged**. Internal surfaces keep the picker: neither switch is set anywhere else.

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
  - `ExternalDataGrid.tsx` renders `<DataGrid {...props} showViewSelector={false} />`. The bundle shows `e=>u.jsx(ole,{...e,showViewSelector:!1})`, and `showViewSelector:!0` occurs nowhere in `dist/assets/app.js`. A clone or `createElement` of an `<ExternalDataGrid>` element sets the WRAPPER's props, and the forced `false` wins. **Corrected in fix round b2-r1:** the wrapper has no hooks, so `ExternalDataGrid(props)` called as a plain function returns the INNER grid element; cloning it (Q1) or mounting its `.type` (Q2) turns the picker on. The runtime guard covers JSX mounts only; the scan now refuses every non-JSX use of the wrapper (§7, round 5).
  - The scan's job is now narrow: only the wrapper may import a grid binding (a fully compliant mount anywhere else is refused), and the wrapper's text is pinned, whitespace-insensitive. A computed clone inside the wrapper (P5 below) passes every regex rule but fails the pin.
  - The scan's cheap rules stay as defence in depth. Every `from '…'` it reads (keyword, whitespace, quote; since b2-r2 a comment there is refused) must be escape-free, attributed to a parsed import when it names the library, and resolve inside the SPA source or into the library. Every `import()` / `require()` takes one plain local literal. `import.meta.glob` is refused in any form. In a grid-binding file, `cloneElement`, `createElement` and `\u` are refused.
  - **Shared-library fan-in pin.** The scan refuses `DataGridPageShell` by name, and that only works while it is the one shared component that mounts the grid. `SharedLibrary_OnlyKnownModulesCarryTheGrid` pins the four modules that import or re-export it (`DataGrid/index.ts`, `DataGridPageShell.tsx`, `components/index.ts`, `src/index.ts`).
  - §11 justification for the new file: see the POML round-3 outcome.
  - **Fix round b2** found and closed a fourth compiled shape (V4): a side-effect `import` of a module outside `src/` that parks the grid on `window`. Side-effect imports now get the specifier rules. Also new: a bare specifier must be a package declared in `package.json`; `eval` / `Function` are refused; `index.html` may only load scripts from `src/` (§7).
  - **Fix round b2-r1** closed five more compiled shapes from the verifier (Q1–Q5) and two found while fixing them (Q6 `?query`, Q7 bare `..`) **as spelled**; see §7, round 5. Review round 6 reopened Q1/Q2 and the dynamic-load rule with one comment after `from` / `import`.
  - **Fix round b2-r2** closed the five round-6 shapes (R1–R5) and three more of the same class found while fixing them (U+FEFF, an escaped letter in `glob`, a comment before `.glob`); see §7, round 6. The scan refuses the routes to the grid it knows of, in the spellings it recognises. It does not guarantee that every route goes through a JSX mount of the wrapper.
  - **Residuals through fix round b2-r2** (superseded by G6): a new npm dependency that re-exports the grid; a `vite.config.ts` alias or plugin, or a `tsconfig.json` / React-plugin JSX option, that re-points a declared name or the JSX runtime; a shared-library `import()` built from a variable; edits to the shared `DataGrid.tsx`; element-tree peeling by hand (`.props.children` / `.type`, or a React-internals fiber walk), widened in b2-r1 with a compiled example (Q8).

    The first four are reviewed changes outside `external-spa/src`. In every case the BFF allow-list still refuses the internal views' columns with a 400.
- **G6: the rule moves INTO the shared grid (fix round c1, 2026-10-03).** Seven review rounds each compiled a shape the scan let through (round 7, V7: a carrier file with no extension, or one the scan does not read). So the invariant no longer depends on how the grid is reached.
  - `DataGrid.tsx` reads `useDataGridExternalHost()`. When it is true, the grid forces the picker off (so `externalViews` is ignored too), never honours a picked view, refuses a `savedquery-set` source with an error, and never calls `retrieveSavedQueriesForEntity`. Props, wrappers, clones, spreads, peeled element trees and duplicate module instances all reach the same grid, and it applies the rule itself.
  - **Two switches, either is enough; both are off in every other host.** (1) The React context: `App.tsx` returns its whole tree inside `<DataGridExternalHostProvider>`, above the app's one `<BrowserRouter>`. (2) The build constant: `vite.config.ts` defines `__SPAARKE_DATAGRID_EXTERNAL_HOST__` as `'true'`. Vite replaces it in every bundled module, so every grid in the bundle is external, whatever tree, React root or module instance renders it. That closes the two holes a context alone would leave: a second `createRoot`, and a peeled raw `Context.Provider value={false}`.
  - **Bundle evidence (vite build, this round).** With the define, the host check compiles to `function soe(){return!0}` and Rollup drops the picker branches as dead code: the picker's `Select view (currently` label occurs **0** times, and the constant occurs 0 times. With the define removed (contrast build, restored after): the label occurs once and the constant twice (`typeof` guard). The provider alone then switches the picker off at run time.
  - **What the scan now asserts as its robust core:** the root mounts the provider above every route, and no other router is imported anywhere in the SPA (`ExternalSpa_RootMountsTheExternalHostProviderAboveEveryRoute`); the define (`ExternalSpa_ViteBuildDefinesTheExternalHostConstant`); and the rule's text in the shared library, meaning the host module verbatim plus the grid's statements and counts (`SharedDataGrid_EnforcesTheExternalHostRule`). The behavioural proof is the jest suite `DataGrid.externalHost.test.tsx`. Until fix round c2 no CI workflow ran that package's jest, which is why the pin exists. The pin checks statements and counts, not scopes: an ADDED nested-scope shadow of the forced value passes it (review round 8, seed J4; residual 4 below, closed in c2 by running the jest folder blocking in Tier 1). Every earlier scan rule is kept as defence in depth, because each is zero-false-positive on the current source.
  - **Item 3 (V7 itself):** the remaining scan no longer goes blind. Every path-like specifier in a static, side-effect or dynamic import of an SPA file or a shipped library file is resolved as Vite resolves it (exact file, then the default extensions, then `index`). It must land on a file the scans read; a directory holding a `package.json` is refused. A specifier with nothing to load is left alone: a real import of it fails the build, and library JSDoc examples quote such paths. Neither `package.json` may declare a `browser` field (Vite remaps through it).
  - **Residuals now (honest):** (1) a NEW npm dependency that ships its OWN grid or view picker, which is not the shared grid, so the rule does not apply; the BFF's savedquery routes 404 every unregistered view (F1) and the column lists refuse other views' columns. (2) Build configuration: dropping or overriding the define (that fact fails; the provider still covers the app tree), or an alias / plugin that re-points the grid's module at a copy without the rule. (3) A NEW picker UI inside `DataGrid.tsx` under another component name, gated on something other than the pinned switch. (4) **REPORTED by CI since fix round c2 (2026-10-03); refused only after the flip.** Fix round c2 ran the jest folder in Tier 1 (§7, "Fix round c2"); c2-r1 hardened the gate's self-protection (§7, "Fix round c2-r1"). Fix round c2-r2 (owner round 13 items 11 and 12) made the job advisory until three green runs on `ubuntu-latest`, and reverted c2-r1's `docs_only` router change, so a PR that edits the grid, the external SPA or the arch guard together with only docs-class files still skips Tier 1 (§7, "Fix round c2-r2"; §8 F3). The record below is how it stood after c1-r1. **Narrowed, not closed** (corrected in fix round c1-r1; c1 listed it as closed, "now pinned"): an edit to `DataGrid.tsx` that ignores the switch without touching a pinned statement. The pin refuses removing or rewriting the rule's statements, but review round 8's seed J4, one added line `const showViewSelector = true;` in the load effect after `fetchConfigRecord`, passed the CI-gated guard 194/194 (re-run in c1-r1: 194/194). On the external host the grid would then request `/savedqueries/{entity}` again. Only the jest suite catches J4 (5 of 7 red; re-run in c1-r1: 5 of 7 red), and CI does not run that package's jest (`sdap-ci.yml` builds `Spaarke.UI.Components` but runs no jest for it). The data impact is nil: the BFF's external savedquery routes 404 every view no module grid registers, which today is every view (F1). Running the DataGrid jest folder in CI would close it; that is a `ci-workflows` hot-path change this project does not declare, so it is not made here. **Closed and removed:** a shared-library `import()` built from a variable, and element-tree peeling (Q8).
  - §11 justification for the new module and the define: POML fix-round-c1 outcome.
- **G3: the `/record` tests use a caller granted the id as project, matter and work assignment at once.** Row scope then passes for every module, including invoices, whose record gate checks the scope dimensions. Any refusal or strip in those tests can only come from the column scope.
- **No new service, DI registration, endpoint, option, job or package.** This change edits data in existing registrations, one prop on an existing mount, and tests. Review round 3 adds one 20-line client component (`ExternalDataGrid.tsx`, the runtime guard; §11 justification in the POML round-3 outcome) and a comment-only BFF edit (no IL change). Placement: the column scope stays in the BFF's existing external read seam (task 134 §5). NuGet: no change.

## 5. Deployment order (LIVE STEP — not run here; main session runs it)

The external SPA must deploy **BEFORE, or together with**, the BFF that carries the shrunk lists. If the BFF goes first, a still-offered view errors: a user who picks an internal view in the old SPA's selector gets `DV_FETCHXML_COLUMN_NOT_PERMITTED` instead of rows. The reverse order is safe. The new SPA only sends each grid's own FetchXML, and the old BFF still admits those columns.

Fix round c1 adds nothing that changes this order. The new SPA never calls the savedquery routes (its grids are inline and the list is never requested). An old SPA against the new BFF gets a 404 from `/savedqueries/{entity}`; its grid catches that, falls back to a single-entry picker for the active view, and keeps rendering. Only the column shrink makes BFF-first unsafe.

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
3. (Fix round c1, F1) With the same token: `GET /api/v1/external/api/dataverse/savedqueries/sprk_project` and `GET …/savedquery/{the id of any sprk_project MDA view}`. Expect **404 `DV_SAVEDQUERY_NOT_FOUND`** for both; before, both returned the internal view definitions. In the browser's network panel, a grid load makes no `/savedqueries/` call.
4. Record screenshots and response captures here.

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
2. build configuration (a `vite.config.ts` alias or plugin, or, named since b2-r2, a `tsconfig.json` / React-plugin JSX option such as `jsxImportSource`) that re-points a DECLARED name or the JSX runtime at another file;
3. a shared-library `import()` whose path is built in a variable elsewhere;
4. edits to the shared `DataGrid.tsx` itself;
5. runtime reflection over React internals (a fiber walk from a rendered `ExternalDataGrid` to the inner component type, then `createElement` of it). This is deliberate obfuscation, and no fixed spelling identifies it. **Widened in fix round b2-r1** to element-tree peeling in general (Q8 below).

Items 1–4 are reviewed changes outside `external-spa/src`. In every case the BFF allow-list still refuses every
internal-view column with a 400. The guard removes the pressure to widen that list; it is not the data control.

**Review round 4 → fix round b2-r1 (`task/uac-r2-157-b2-r1`, 2026-10-02): the guard failed open a fifth time.** The
verifier compiled five shapes (Q1–Q5) into live picker-on mounts past the round-4 guard (81/81 green). b2-r1 re-seeded
them, plus three of its own (Q6–Q8), wired all eight into `main.tsx`, and ran `vite build`. All eight compiled into a
live mount: Q1–Q5, Q7 and Q8 onto the shared grid, and Q6 onto a second instance of the grid module. The round-4 guard
was still 81/81 green with all eight present.

| Shape | Why it passed | Fix |
|---|---|---|
| Q1 `React.cloneElement(ExternalDataGrid({ configId }), { showViewSelector: true })` | the wrapper has no hooks, so a plain call returns the INNER grid element; `cloneElement` was refused only in grid-binding files | outside the wrapper, any binding imported from the wrapper module (any path-like spelling, extension and case ignored) may appear only as a JSX tag, after `typeof`, or as a member name; `cloneElement` / `createElement` / `\u` are refused in such a file |
| Q2 `(ExternalDataGrid({})).type` mounted | the public `element.type`, not React internals, so residual 5 did not cover it | same rule (a function call is a non-JSX reference). Also refused: a namespace import, a re-export, a string-named or side-effect import, and a dynamic `import()` / `require()` of the wrapper module |
| Q3 a carrier in `Spaarke.UI.Components/zzoutside/` (the library root, beside `src/`) | a library-naming specifier was accepted anywhere under the library ROOT, while the fan-in fact reads only `src/` | a library-naming specifier must resolve into the library's `src` (directly or through the npm link) |
| Q4 a carrier in `src/components/DataGrid/__zz__/` | the fan-in fact skips `__*` and test files as "not bundled", but the SPA could import them | such a specifier may not resolve into a `__*` segment or a `.test` / `.spec` / `.stories` file. **And** every shipped library file (the allowed carriers included) may not load a module outside `src/` or from such a path, so the fan-in exclusion is now sound |
| Q5 `@spaarke/ui-components/../../../external-spa/zzoutside/gridAlias2` | the `@spaarke/` branch returned "fine" for any alias-prefixed string without resolving it | the alias is resolved to the library's `src` (as `vite.config.ts` does) and checked by the same rule |
| **Q6 (found in b2-r1)** `import Table from '@spaarke/ui-components/components/DataGrid/DataGrid?v=1'` | Vite loads a second module instance; the default-export rule read the last segment `DataGrid?v=1` as a non-grid name | a `?query` or `#fragment` in any specifier (static, side-effect, dynamic) is refused |
| **Q7 (found in b2-r1)** `import { Grid } from 'react-window/../../zzoutside/gridAlias2'` | `react-window` is declared and has no `exports` map, so its subpath stepped out to any file | a bare specifier with a `.` or `..` segment is refused |
| **Q8 (found in b2-r1, NOT closed — residual 5)** in a file that never names the wrapper: `const outer = (createGridWidgetBody('x') as any)({}).props.children; const T = outer.type(outer.props).type;` then `<T configId="x" />` | element-tree peeling: every React element exposes `.props.children` and `.type`, and any component can be called by hand | **not closable** by a scan with zero false positives (computed keys, `Object.values`, a helper in another file; refusing every `.type` read would refuse `event.type`, `file.type`). It is not closable by a same-realm runtime wrapper either, because every layer peels the same way. Documented as residual 5 with this compiled example |

Proof it bites (each seed deleted, or restored with `git checkout` and touched; the npm-touched `package-lock.json` reverted):

| Seeded | Result |
|---|---|
| Q1–Q8 under `external-spa/src/zzseed` (+ the carriers in `Spaarke.UI.Components/zzoutside`, `src/components/DataGrid/__zz__` and `external-spa/zzoutside`), before the fix | guard class 81/81 **green** (all eight bypass it) |
| The same, after the fix | production fact **red on exactly Q1–Q7** (Q1: `cloneElement` + non-JSX reference; Q2: non-JSX reference; Q3: outside the library's src; Q4: a test or `__*` path; Q5: resolves outside; Q6: query; Q7: `..` segment). Q8 not flagged (residual) |
| A new shipped file `components/ZzSeed/ZzSeed.ts` re-exporting from `../../../zzoutside/gridAlias` and `../DataGrid/__zz__/grid` | fan-in fact red, 2 entries |
| A line appended to the allowed carrier `src/index.ts`: `export { Grid as ZzGrid } from '../zzoutside/gridAlias'` | fan-in fact red, 1 entry (the allowed carriers are not exempt) |

Comment-only client edits: `ExternalDataGrid.tsx` JSDoc (pinned; the pin was updated in the same change, code tokens
unchanged) now states what the runtime guard does NOT cover. `GridWidgetBody.tsx` JSDoc names the wrapper only as a
tag (`<ExternalDataGrid>`), because comments are scanned as code and its three plain mentions were non-JSX references.

**Runs (fix round b2-r1):**

| Suite | Result |
|---|---|
| `ExternalSpaGridViewSelectorGuardTests` | 109 / 109 passed: 81 + 26 round-5 Theory rows + 1 sanctioned-wrapper control + 1 `SharedEscapes` control |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 454, Failed 0, Total 454 (426 + 28) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14274, Failed 0, Skipped 54, Total 14328 (Debug, 19m56s) |
| SPA `npm install --legacy-peer-deps --no-audit --no-fund` + `npx vite build` (clean, seeds removed) | exit 0. The bundle holds `nle=e=>u.jsx(ole,{...e,showViewSelector:!1})`; `showViewSelector:!0` occurs 0 times. `npx tsc --noEmit`: 6 errors, all pre-existing, none in a changed file |

**Review round 6 → fix round b2-r2 (`task/uac-r2-157-b2-r2`, 2026-10-02): the guard failed open a sixth time.** The
verifier seeded five shapes (R1–R5) in a throwaway worktree; `vite build` compiled all five into live picker-on mounts and
the b2-r1 guard stayed 109/109 green. Q1–Q7 had been closed only as spelled: one comment reopened Q1/Q2 and the dynamic-load
rule.

| Shape | Why it passed | Fix |
|---|---|---|
| R1 `import { DataGrid as G } from /* c */ '@spaarke/…/DataGrid'` | every specifier rule needs `from\s*['"]`, so the import was invisible | a comment after `from` / `import` / `require` that is followed by a quote or `(` is refused |
| R2 the same comment on the wrapper import, then `(E as any)({…}).type` mounted | the wrapper binding was never recognised, so the round-5 reference rule never ran | same rule |
| R3 `React.lazy(() => import /* c */ ('@spaarke/…/DataGrid'))` | `DynamicCall` is `\b(?:import\|require)\s*\(` | same rule (a comment INSIDE the parentheses already failed the plain-literal rule) |
| R4 `/** @jsxImportSource ../../zzoutside/rt */` | esbuild imports `…/rt/jsx-runtime` with no import statement | `@jsx` in any form is refused anywhere in an SPA file |
| R5 a carrier at `src/node_modules/zzc`, imported as `'../node_modules/zzc'` | the scan skips `node_modules`, but the specifier rule accepted it as "inside the SPA source" | such a specifier is refused, and the production facts fail if any `node_modules` folder exists under the SPA's or the library's `src` (Vite resolves even a declared bare name through the nearest one) |
| **(found in b2-r2)** `from` + U+FEFF + `'…'` | U+FEFF is JavaScript whitespace, not .NET `\s` | a literal U+FEFF is refused in every SPA file; the token gap treats it as whitespace |
| **(found in b2-r2)** `import.meta.glob` with one letter of `glob` written as a `\u` escape, in a file with no binding | `\u` was refused only in binding files | `\u` is refused in every SPA file (none today) |
| **(found in b2-r2)** `import.meta /* c */ .glob(…)` | `ImportMetaGlob` read only whitespace between tokens | it reads comments and U+FEFF between tokens, accepts `?.`, and refuses any computed `import.meta[…]` |

The three found here were each compile-proven first: wired into `main.tsx`, `vite build` produced
`u.jsx(vp,{configId:"zzr6"})` (vp is the shared grid: the wrapper renders `u.jsx(vp,{...e,showViewSelector:!1})`) and two
`Object.assign({"…/DataGrid.tsx":dE})` globs (dE is the grid module, `DataGrid:vp`).

**Why a refusal and not "treat comments as whitespace".** Inside JSX children `/*` is text. A specifier regex that skips a
"comment" can then run to the next `*/` and swallow a real import in between (regex matches do not overlap), and in the
rules whose matches are attributed or excluded that hides code. A refusal has no such failure: every match is a violation.
(Reasoned, not compiled.) The comment bounds are JavaScript's: a block ends at the first `*/`, a line at `\n`, `\r`,
U+2028 or U+2029.

**The library rule is narrower.** In shipped library files the same comment rule fires only when what follows could load a
library module (a relative, root-relative or alias path, or text naming the library or `DataGrid`). The SPA form flagged
library prose: `surfaceLaunchRegistry.ts:161` ends a comment line with "from", then a comment line, then the object key
`'nda-review'`. A literal U+FEFF is not refused there either: `csvExport.ts:32` holds the UTF-8 BOM in a string on purpose.
The library rule is a lookahead from each keyword, so a misread "comment" cannot swallow a later keyword.

Proof it bites (seeds written by a script so U+FEFF and `\u` stayed literal; all deleted, `main.tsx` restored with
`git checkout` and touched, `package-lock.json` reverted, `external-spa/zzoutside` removed):

| Seeded | Result |
|---|---|
| `zzseed/R1`–`R5` (+ `src/node_modules/zzc/index.ts` carrying the grid), `R6feff`, `R7glob`, `R8metacomment` | production fact red on all 8 files and on the `src/node_modules` folder |
| library `components/ZzSeed/ZzSeed.ts` (comment after `from`; `import // c` then `(…)`; U+FEFF after `from`; an `@jsxImportSource` pragma) + `src/node_modules/zzc` | fan-in fact red with 5 entries, one per seed |

**Not changed:** Q8 (element-tree peeling) stays residual 5. Item 13 (process): the project's integration hard gate
(`tests/integration/Sprk.Bff.Api.IntegrationTests` and `tests/integration/Spe.Integration.Tests` in full) was not run in
this fix round; the main session runs both before the PR.

**Runs (fix round b2-r2):**

| Suite | Result |
|---|---|
| `ExternalSpaGridViewSelectorGuardTests` | 134 / 134 passed: 109 + 24 round-6 Theory rows + 1 neighbours control (the `SharedEscapes` control gained 7 asserts) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 479, Failed 0, Total 479 (454 + 25) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14274, Failed 0, Skipped 54, Total 14328 (Debug, 18m31s) |
| SPA `npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` (vite), seeds removed | exit 0. The bundle holds `ole,{...e,showViewSelector:!1}`; `showViewSelector:!0` occurs 0 times. No client file changed |
| Integration suites (project hard gate) | not run here; the main session runs both before the PR |

**Review round 7 → fix round c1 (`task/uac-r2-157-c1`, 2026-10-03; base `f55f3d426` = b2-r2 + `work/unified-access-control-r2`, clean merge): the scan arms race stops.** The verifier's V7 (a carrier file with no extension, or one the scan does not read) is the seventh compiled bypass in seven rounds. Instead of an eighth scan rule, the invariant moved into the shared grid (decision G6). V7 is closed by that, and the remaining scan was made to fail closed on V7's class (item 3).

| Proof | Result |
|---|---|
| **V7 compiled, runtime rule in place.** `src/zzseed/carrier` (no extension) imports the grid and sets `window.SprkGridV7`; `src/zzseed/V7.tsx` imports it and mounts `<G configId="zzv7" showViewSelector={true} />`; wired into `main.tsx`; `vite build` | compiled into `u.jsx(window.SprkGridV7,{configId:"zzv7",showViewSelector:!0})`, yet the bundle holds NO picker code (`Select view (currently` 0 times). The host check is `function TG(){return!0}`. The runtime rule closes V7 |
| The same seed, guard run | red on exactly one violation, the new round-7 rule (`'./carrier' resolves to …/zzseed/carrier, a file these scans do not read`). The b2-r2 rules passed the file |
| Contrast build with the define removed (restored after) | picker label 1, constant 2 (the `typeof` guard): without the define the picker code ships and only the provider keeps it off |
| jest seeds (each restored hash-matched, touched): A1 context check removed from the hook; A2 the grid ignores the host; B build check removed; C sibling fetch unconditional; D savedquery-set refusal removed | 4 / 4 / 1 / 5 / 1 red of 7 |
| arch seeds on disk (restored, touched): P7 provider removed from `App.tsx`; P8 define removed; P9 grid ignores the host; P10 hook drops the context check; P11 `MemoryRouter` imported in a page | P7: root fact red. P8: define fact red. P9: pin fact + 8 in-memory controls red. P10: pin fact + 10 controls red. P11: root fact red |
| BFF seeds (each rebuilt, restored hash-matched, touched): B1 by-id allow-list skipped; B2 list filter removed; B3 list no-view 404 removed; B4 entity match removed; B5 `Guid.Empty` check removed; B6 duplicate check removed; B7 the project module registers an internal view | 4 / 1 / 9 / 2 / 1 / 1 / 4 red. The HTTP `savedquery/{id}` contract row alone does not bite B1 (the fixture's Dataverse returns no view); the core rows do. **Gap (review round 8, X1): no row guarded the by-id ROUTE wiring itself. Closed in fix round c1-r1, below** |

The first run of the round-7 rule reported 12 library JSDoc examples (`import { CreateEventWizard } from './components/CreateEventWizard'` and similar) that resolve to no file. A specifier with nothing to load cannot carry the grid: a real one fails the build. So those are left alone, and only an existing unscanned target, or a `package.json` directory, is refused. The `browser` field check was added for the one Vite remap the resolver does not model.

**Runs (fix round c1):**

| Suite | Result |
|---|---|
| jest `DataGrid.externalHost.test.tsx` | 7 / 7 passed |
| jest, Spaarke.UI.Components full suite (`jest --ci`) | 3381 tests: 3367 passed, 14 failed in 9 suites. None imports `DataGrid.tsx` or `DataGridExternalHost.tsx` (AccessGrantModal userShare, TimelineComposeBox, ConversationView ×2, RichFilePreview, RecordHeader configResolution, WorkspaceShell buildDynamicWorkspaceConfig, surfaceLaunchRegistry, todoScoreMappings). Every DataGrid suite passed. This package's suite is not run by CI; the baseline was not re-measured here |
| `ExternalSpaGridViewSelectorGuardTests` | 194 / 194 passed: 134 + 3 new facts + 57 control rows |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 540, Failed 0, Total 540 |
| BFF affected (`ExternalModuleSavedQueryScopeTests` 28, `ExternalModuleColumnAllowListTests`, `ExternalModuleDataContractTests` 18 incl. 3 new, registry / guard / scope tests) | 226 / 226 passed |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14305, Failed 1, Skipped 54, Total 14360 (Debug, 23m52s). The one failure, `PinnedMemoryEndpointsContractTests.CreatePin_Authenticated_Returns201AndEmitsCounter` (a metric tag read from another test's emission, `tenant-test-fixture`; unrelated surface), passed on an isolated re-run: 15 / 15. Contention |
| Integration `tests/integration/Sprk.Bff.Api.IntegrationTests` | Passed 104, Failed 0, Skipped 0, Total 104 (first run on this branch: item 4) |
| Integration `tests/integration/Spe.Integration.Tests` | Passed 403, Failed 0, Skipped 25, Total 428 (7m32s; first run on this branch: item 4) |
| SPA `npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` (vite) | exit 0 (clean tree, seeds removed). Bundle: constant 0, picker label 0, `showViewSelector:!0` 0, wrapper `{...e,showViewSelector:!1}` present |
| Shared library `npm run build` (tsc) | exit 0, after building the Spaarke.Auth and Spaarke.SdapClient dists in CI order |
| SPA `tsc --noEmit` | 25 errors. The one on a changed line is `App.tsx` TS2786 on the provider mount: the React 18/19 types class (F2) of the pre-existing TS2786 on `ExternalDataGrid.tsx`'s grid mount. `vite build` does not typecheck |

**Review round 8 → fix round c1-r1 (`task/uac-r2-157-c1-r1`, 2026-10-03; base `55dfb9d8f` = the c1 commit).** Review round 8 verified items 2, 3, 7–10 and 12 and raised one blocking gap, one overclaim and one count note.

- **Item 1 (BLOCKING), the by-id ROUTE was unguarded. CLOSED.** The verifier's seed X1 (the `savedquery/{id}` handler skips `GetSavedQueryCoreAsync` and returns to the pre-157 passthrough, which serves any view of a registered entity) left every test green: each by-id negative drove the core directly, and the one HTTP row used an unknown id, which 404s whether or not the allow-list runs. New HTTP contract row `ModuleSavedQuery_WhenTheIdIsAnInternalViewOfARegisteredEntity_Returns404` (`ExternalModuleDataContractTests`): it plants an internal `sprk_project` view in the distributed cache that the app's own `SavedQueryService` reads first (key `sdap:dv:savedquery:{id:D}`; the Dataverse read needs a live ServiceClient), asserts as a precondition that the service really returns that view, then requires the route to answer 404 `DV_SAVEDQUERY_NOT_FOUND` with no part of the view (name, FetchXML column) in the body. The cache key is removed in `finally`. **Seed X1 re-planted** (the handler's one `return` replaced by the old passthrough; built; restored hash-matched `19e6e832…` and touched): exactly this row red, *expected 404 but found 200*: the F1 leak, seen over HTTP. The 46 other rows stayed green, which is the verifier's finding reproduced.
- **Item 4 (MINOR), overclaim corrected.** "Edits to `DataGrid.tsx` that ignore the switch" was listed as a closed residual ("now pinned"). Seed J4 re-planted here (`const showViewSelector = true;` after `fetchConfigRecord` in the load effect; restored hash-matched `944dd3fb…` and touched): the guard class passed 194/194, and jest `DataGrid.externalHost.test.tsx` went 5 of 7 red (7/7 before and after). It is now residual 4, **narrowed, not closed**, in G6 (§4), the guard's class remarks and `ScanSharedGridRule`'s summary, and the POML. Data impact nil (the BFF 404s every unregistered view). Running the DataGrid jest folder in CI would close it; that is a `ci-workflows` hot-path change (this project declares `ci-workflows` N), so it is not made here.
- **Item 11 (INFO), the SPA `tsc --noEmit` count.** See F2 (§8).
- **Items 5 and 6 (INFO)** need no change: residual 2 already states that a build config overriding the define leaves only the provider (S2), and a standalone `DataGridViewSelector` in the SPA would list nothing, because both external savedquery routes 404 every view (F1).
- No source file changed in c1-r1: one test row added, comments and notes corrected.

**Runs (fix round c1-r1):**

| Suite | Result |
|---|---|
| BFF affected (`ExternalModuleSavedQueryScopeTests` + `ExternalModuleDataContractTests`) | 47 / 47 (46 + 1 new). Seed X1: 1 failed (the new row), 46 passed. Restored: 47 / 47 |
| `ExternalSpaGridViewSelectorGuardTests` | 194 / 194. Seed J4: still 194 / 194 (the documented gap) |
| jest `DataGrid.externalHost.test.tsx` | 7 / 7. Seed J4: 5 of 7 failed |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14307, Failed 0, Skipped 54, Total 14361 (Debug, 23m7s; one more than c1's 14360: the new row) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 540, Failed 0, Total 540 |
| Integration `tests/integration/Sprk.Bff.Api.IntegrationTests` | Passed 104, Failed 0, Skipped 0, Total 104 |
| Integration `tests/integration/Spe.Integration.Tests` | Passed 403, Failed 0, Skipped 25, Total 428 (7m35s) |

The four final suites ran together on one machine. No client file changed, so no SPA build was run in this round. The SPA `package-lock.json` that npm touched was reverted.

**Fix round c2 (`task/uac-r2-157-c2`, 2026-10-03; base `381707788` = c1-r1 + `work/unified-access-control-r2`, clean merge): residual 4 CLOSED.** This follows a main-session ruling under owner round 10: the DataGrid jest folder becomes a BLOCKING check on the Router path, not in the retiring `sdap-ci.yml`.

- **Premise correction.** The Compose Client Gate is not a blocking Router gate. It exists only in `sdap-ci.yml`, with `continue-on-error: true`, and Tier 1 had no client gate at all. The wiring therefore copies the mechanism Tier 1's BFF-conditional job (`changed-surface-smoke`) already uses: the router's `classify` job (dorny/paths-filter) passes a string input to `ci-tier1-blocking.yml`.
- **`ci-router.yml`.** New filter output `ui_components` (`src/client/shared/Spaarke.UI.Components/**`). It is passed to Tier 1 as `ui_components_changed` = `ui_components` OR `ci_workflows`, so an edit to the gate job runs the gate. It is also added to the `docs_only` exclusion, plus a summary row. **Found while wiring it:** `docs_only` counted only BFF, SpaarkeAi and CI workflows as code. A PR editing `DataGrid.tsx` plus any `*.md` file was therefore docs-only and skipped Tier 1 entirely, new gate included.
- **`ci-tier1-blocking.yml`.** New input `ui_components_changed`, default `'true'`. The job's `if:` is `!= 'false'`, so an omitted input or a `workflow_dispatch` run executes the gate (fail closed). New job `datagrid-external-host-gate`: ubuntu-latest, Node 20, 15-minute hang guard, no `continue-on-error`. It runs these steps:
  1. `npm install --legacy-peer-deps --no-audit --no-fund` in `Spaarke.UI.Components`.
  2. `npx jest --ci --json … src/components/DataGrid/`: 8 suites, 75 tests. There is no `--coverage`, because the package's global `coverageThreshold` would fail a folder run.
  3. A node step that fails unless `DataGrid.externalHost.test.tsx` ran with at least 7 tests, all passing. Without it, a rename, deletion or `skip` would leave the folder green.
  4. The existing `summarize-jest-results.js` job summary, plus a results artifact on failure.
- **No shared-library dist needed.** From a clean worktree (fresh install, no `Spaarke.Auth` / `Spaarke.SdapClient` dist): 8/8 suites, 75/75 tests. The same holds under `TZ=UTC`, `America/Los_Angeles` and `Asia/Kolkata` with `--randomize`.
- **Freeze.** The three tier files are frozen for the shadow window. The window's GATE REPAIR carve-out names "a guard present but not armed", and this is that case: no PR-triggered workflow ran this suite. The change is disclosed in a new `projects/INDEX.md` disclosure record, and the window re-baselines from this merge.
- **Self-protection.** New arch fact `SharedDataGrid_ExternalHostJestSuiteIsABlockingTier1Gate`, which runs in the blocking `arch-tests` job, plus a 10-row negative-control Theory over the real workflow text. The rows cover: job renamed; `continue-on-error`; `|| true`; `if:` changed; jest narrowed; suite assertion dropped; input default flipped; router filter dropped; `docs_only` exclusion removed; input not passed.
- **Hot path.** `design.md` now declares `ci-workflows` = Y. The project's `INDEX.md` row already read Y; it now notes the correction and this change.
- **Not observed on `ubuntu-latest`.** Every run here was on Windows (Node 22.14; the job pins Node 20 like the repo's other client jobs). The repo's "green runs on the runner before blocking" standard is waived by the ruling that this gate lands blocking. If the first runner result is red for an environment reason, fix the environment rather than add `continue-on-error`.

| Proof it bites (each restored hash-matched, touched) | Result |
|---|---|
| Seed J4 in `DataGrid.tsx` (restored `944dd3fb…`); the job's steps run from the YAML's own `run:` text | jest step exit 1 (`Tests: 5 failed, 70 passed`); assertion step exit 1 (`2 of 7 passed`) |
| `DataGrid.externalHost.test.tsx` renamed | jest exit 0 (8/8 suites); assertion step exit 1 (`… did not run`) |
| One of its tests `it.skip`'d | jest exit 0 (`1 skipped`); assertion step exit 1 (`6 of 7 passed`) |
| `continue-on-error: true` written into the job on disk | the new fact red (`carries continue-on-error`) |
| The `docs_only` exclusion removed from the router on disk | the new fact red (`docs_only must exclude ui_components`) |
| 10 in-memory disarmings of the real text (the Theory) | each produces a violation |

**Runs (fix round c2):**

| Suite | Result |
|---|---|
| actionlint 1.7.12 (`-shellcheck=`, as `workflows-validate.yml` runs it) | exit 0 on both edited files and on all of `.github/workflows` (exit 0 before the change too) |
| jest `src/components/DataGrid/` (clean install; also under 3 time zones with `--randomize`) | 8 / 8 suites, 75 / 75 tests (the external-host suite 7 / 7) |
| `ExternalSpaGridViewSelectorGuardTests` | 205 / 205 (194 + 1 fact + 10 Theory rows) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 551, Failed 0, Total 551 (540 + 11) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14307, Failed 0, Skipped 54, Total 14361 (Debug, 19m11s; unchanged from c1-r1, no BFF test changed) |
| Integration `tests/integration/Sprk.Bff.Api.IntegrationTests` | Concurrent run: Passed 103, Failed 1, Total 104. The failure was `PlaybookByNameDeprecationTests.ByName_SetsActivityTag_DeprecatedEndpoint_OnEveryCall`, an activity-tag capture on an unrelated surface. It passed on an isolated re-run (class 3 / 3), and the whole suite then passed 104 / 104. Contention |
| Integration `tests/integration/Spe.Integration.Tests` | Passed 403, Failed 0, Skipped 25, Total 428 (7m31s) |

No product source file changed in c2: two workflow files, one arch-test file, and the notes. The `datagrid-jest-results.json` that the local runs wrote was deleted.

**Fix round c2-r1 (`task/uac-r2-157-c2-r1`, 2026-10-03; base `0f719645f` = c2): review c2's items closed.** Review c2 confirmed items 1 to 4: the blocking wiring, the jest runs, actionlint, and all four suites. Its findings, by item:

- **Items 5 and 12 (MEDIUM): the self-protection overclaimed.** Four actionlint-clean disarmings, seeded on disk, left the c2 fact green: (a) `needs: changed-surface-smoke` on the job; (b) a non-constant false step-level `if:` on the jest and assert steps; (c) `ui_components='false'` in the router's `docs_only` step; (d) `if (false)` in the assertion. `ScanDataGridCiGate` now also refuses each of the following:
  - **Job-level keys.** Any key outside `name`, `runs-on`, `timeout-minutes`, `if`, `steps` (`DataGridGateJobKeys`). A `needs:` on a job that is skipped skips the gate.
  - **Step-level `if:`.** Any step-level `if:` except `always()`, or `failure()` on the artifact-upload step (`uses: actions/upload-artifact`, no `run:`). The dash-line form `- if:` is covered too.
  - **Assertion script.** It is pinned verbatim (`DataGridGateAssertScript`, compared line by line after trimming).
  - **The router's `docs_only` run block.** Its shape is pinned. Each of the 7 variables must be assigned exactly once, in order, from its own filter output. The `if [[ "$docs" == "true" …` line may contain only `&& "$x" != "true"` terms, and it must name all 6 exclusions. `value=true` appears only in the then-branch. Nothing else is allowed.
  - **Router wiring.** The classify job's `docs_only` output source and the tier1 call's `if: needs.classify.outputs.docs_only != 'true'`.

  The Theory went from 10 rows to 31. It now includes the four review seeds and their near variants (`environment:`, install-step `if:`, `failure()` on the jest step, `process.exit(0)`, an added line, a reassignment, an `||` term, an unconditional `value=true`, a forced `docs_only` output, `if: false` on the tier1 call). The job header comment, the fact's summary and the class remarks now describe a text check that refuses the disarmings it names, not every conceivable one.
- **Item 6 (MEDIUM): `docs_only` still skipped Tier 1 for the external SPA and for the arch guard itself.** `ci-router.yml` has two new paths-filters, `external_spa` (`src/client/external-spa/**`) and `arch_tests` (`tests/Spaarke.ArchTests/**`). They are used only in the `docs_only` derivation (assignments, exclusion terms, summary rows), not passed to any tier. Tier 1's `arch-tests` job runs whenever Tier 1 runs, so a PR editing `App.tsx`, `vite.config.ts`, `ExternalDataGrid.tsx` or this guard file, plus a task note, now runs `ExternalSpaGridViewSelectorGuardTests`. The guard pins both filters and both exclusions. The wider gap remains: any other unclassified path, e.g. `src/client/pcf/**`, plus a `.md` file is still docs-only. That is pre-existing and outside this task's guards, so it is not changed here (§8 F3).
- **Item 7 (LOW): disclosure imprecision.** The `projects/INDEX.md` disclosure now separates the two verdict impacts. The first is the new job. The second is the `docs_only` reclassification: PRs touching `Spaarke.UI.Components`, the external SPA or the arch tests together with only docs-class files now run ALL of Tier 1 and Tier 2, where before they skipped both. That is a verdict change for that class of PR, and the shadow-window comparison must read it as one.
- **Item 8 (LOW): never run on `ubuntu-latest` / Node 20.** Narrowed, not closed. In a clean short-path worktree (`C:\wvd157r1`, removed afterwards), on Node **20.20.2** (the major the job pins), every step passed. `npm install --legacy-peer-deps --no-audit --no-fund` left the lockfile unchanged. The job's jest call gave 8/8 suites and 75/75 tests, exit 0. The assertion step printed `7 of 7 passed`, exit 0. Seed J4 gave jest exit 1 (`5 failed, 70 passed`); the source was restored and the hash matched. `ubuntu-latest` still cannot be run from here, because a push is not allowed. The "N green runs before blocking" waiver remains a main-session ruling that the owner has not confirmed (owner round 10, "decided by the main session … recorded for the owner"). **Open for the owner.**
- **Item 9 (LOW): untracked results file.** The root `.gitignore` now ignores `src/client/shared/Spaarke.UI.Components/datagrid-jest-results.json` (`git check-ignore -v` confirms it). The c2 base, which does not have that line, showed the file as `??` after the Node 20 run.
- **Item 10 (INFO): conflict check.** #1110 (`work/spaarkeai-word-add-in-r1`) touched `projects/INDEX.md`, and it is now **MERGED** (`62277d50a`, 2026-10-03T17:05Z). `git merge-tree` of c2 against `origin/master` after that merge is clean. No open PR touches `ci-tier1-blocking.yml`, `ci-router.yml`, `.gitignore` or the guard file. #935 touches `projects/INDEX.md`. #909 and #894 touch `ci-tier2-advisory.yml` only.
- **Item 11 (INFO): disclosure channel.** The banner says "Disclose in a note to `ci-cd-unit-test-remediation-r1`". That note now exists: `projects/ci-cd-unit-test-remediation-r1/notes/gate-repair-disclosure-2026-10-03-uac-r2-157.md`. It points to the INDEX.md record, which stays the authoritative one.
- **Items 1 to 4: verified by review c2, no change.** Publish size does not apply: no BFF or product source change.

| Proof it bites (c2-r1, on disk; each file restored hash-matched and touched) | actionlint | `SharedDataGrid_ExternalHostJestSuiteIsABlockingTier1Gate` |
|---|---|---|
| (a) `needs: changed-surface-smoke` on the job | exit 0 | red: `carries the job-level key needs:` |
| (b) `if: github.event_name == 'never'` on the jest and assert steps | exit 0 | red: `step … carries if: github.event_name == 'never'` |
| (c) `ui_components='false'` in the `docs_only` step | exit 0 | red: `docs_only step line 5 must be ui_components='${{ steps.filter.outputs.ui_components }}'` |
| (d) `if (false) {` in the assertion | exit 0 | red: `must run the pinned assertion script verbatim` |
| item 6: ` && "$external_spa" != "true"` removed | exit 0 | red: `condition must exclude external_spa` |
| item 6: `arch_tests` filter narrowed to `README.md` | exit 0 | red: `paths filter arch_tests must classify 'tests/Spaarke.ArchTests/**'` |
| 31 in-memory disarmings of the real text (the Theory) | n/a | each produces a violation |

**Runs (fix round c2-r1):**
| Suite | Result |
|---|---|
| actionlint 1.7.12 (`-shellcheck=`) | exit 0 on both edited files and on all of `.github/workflows`; exit 0 on each of the six on-disk seeds |
| jest `src/components/DataGrid/` on **Node 20.20.2** (clean install, short-path worktree) | 8 / 8 suites, 75 / 75 tests; the assertion step `7 of 7 passed`; seed J4: 5 failed, 70 passed, exit 1 |
| `ExternalSpaGridViewSelectorGuardTests` | 226 / 226 (205 + 21 new Theory rows) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 572, Failed 0, Total 572 (551 + 21) |
| Integration `tests/integration/Sprk.Bff.Api.IntegrationTests` | Passed 104, Failed 0, Total 104 |
| Integration `tests/integration/Spe.Integration.Tests` | Passed 403, Failed 0, Skipped 25, Total 428 (7m41s) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | **Contention, not this change.** The machine was running about 216 dotnet test processes at about 87% CPU, from other agents. Full run 1: Passed 14218, Failed 89, Skipped 54, Total 14361 (45m). Full run 2 (trx): Passed 14128, Failed 179, Skipped 54, Total 14361 (1h03m). Of those 179, 178 were `TaskCanceledException: The operation was canceled` (host HTTP calls timing out after 3 to 15 minutes each). The other one was `ComposeFidelityGateHarnessTests` (`alternate-content-duplicate-paraid.docx`). An isolated re-run of all 66 failing classes gave Passed 597, Failed 9, Skipped 10, Total 616. The fidelity case passed, and the 9 were again 3-minute timeouts. A re-run of those 6 classes gave 50 / 50 passed. c2-r1 changes no BFF source or BFF test: no file under `tests/unit` reads the workflows, `.gitignore` or `INDEX.md`. |

No product source file changed in c2-r1: two workflow files, `.gitignore`, one arch-test file, and the notes. No live read or write.

**Fix round c2-r2 (`task/uac-r2-157-c2-r2`, 2026-10-03; base `802f98d2f` = c2-r1): owner round 13 items 11 and 12.**

- **Item 11 (BINDING): `datagrid-external-host-gate` lands ADVISORY, blocking after N green runs on the CI runner.**
  - `ci-tier1-blocking.yml`: the job now carries job-level `continue-on-error: true` with an `ADVISORY …` comment, the same construction as `sdap-ci.yml`'s `compose-client-gate`. The jest step lost "merge-blocking" from its name. The header's CONTENTS item 5 and the job's comment block say advisory, give the reason, and carry the FLIP CONDITION below. While advisory, a red job shows in the run but does not fail Tier 1 or `CI / Router`. That is GitHub's documented behaviour for job-level `continue-on-error`; no run on this repo's runner has shown it yet, because nothing can be pushed from here.
  - **N = 3.** That is the repo's standard as `compose-client-gate` states it ("three consecutive green … runs on unchanged code ON THIS RUNNER"). `office-addins-tests.yml` names the rule ("N consecutive green runs ON `ubuntu-latest`") without a number.
  - **FLIP PROCEDURE (recorded here and in the job's comment):**
    1. **Count.** Three consecutive runs of `datagrid-external-host-gate` on `ubuntu-latest`, on diffs that do not change `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/`. In each run, the steps `DataGrid jest folder (external-host rule)` and `Assert the external-host suite ran` must both conclude `success`. A red such run in between resets the count. Read the **step** conclusions: with job-level `continue-on-error` the run is green even when the job fails. Commands: `gh run list --workflow ci-router.yml --limit 50 --json databaseId,headBranch,event,conclusion`, then for each id `gh run view <id> --json jobs --jq '.jobs[] | select(.name | endswith("DataGrid External-Host Gate (jest)")) | {name, conclusion, steps: [.steps[] | {name, conclusion}]}'`. A run where the job was skipped (`datagrid_gate` = `false`) does not count.
    2. **Flip, in ONE pull request:** delete the job's `continue-on-error: true` line in `.github/workflows/ci-tier1-blocking.yml`; set `DataGridGateAdvisory` to `false` in `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs`; rewrite the job comment and header item 5 from advisory to blocking; update the `projects/INDEX.md` disclosure record (the flip IS a verdict change, so disclose it there); put the three run ids in the PR description. Nothing else about the job changes.
    3. **The guard enforces the pairing.** While `DataGridGateAdvisory` is `true` it requires exactly one job-level `continue-on-error: true`. Once it is `false`, it refuses any. A YAML-only flip or a YAML-only un-flip fails the blocking arch-tests job. Proven both ways below.
    4. **Red on the runner.** If a counted run is red for an environment reason, fix the environment and restart the count. Do not change the assertion or the jest call to make it pass.
- **Item 12 (BINDING): the router's `docs_only` skip is NOT changed here.**
  - `ci-router.yml` was restored to its pre-task content (`git checkout 381707788 -- .github/workflows/ci-router.yml`; `git diff 381707788` on that file is empty). That removes c2's `ui_components` filter, output, summary row, `docs_only` exclusion and the `ui_components_changed` pass-through, and c2-r1's `external_spa` and `arch_tests` filters and exclusions.
  - The gate's classification moved inside Tier 1. `classify-tier1` (dorny/paths-filter, already used for `auth` and the smoke namespaces) gains the output `datagrid_gate`, from `src/client/shared/Spaarke.UI.Components/**` or `.github/workflows/**` (the same two triggers as before). The job `needs: classify-tier1` and has `if: ${{ github.event_name == 'workflow_dispatch' || needs.classify-tier1.outputs.datagrid_gate != 'false' }}`. That fails closed: an empty output runs the gate, and a manual dispatch always runs it, as before. The `ui_components_changed` `workflow_call` input is removed, so Tier 1's contract with the router is back to `bff_changed` and `spaarke_ai_changed`. `classify-tier1` always runs, so `needs:` cannot skip the gate the way `needs: changed-surface-smoke` did in review c2. The guard pins that `classify-tier1` has no job-level `if:`.
  - **Consequence, stated plainly.** A PR that edits `DataGrid.tsx`, `src/client/external-spa/**` or `tests/Spaarke.ArchTests/**` together with only docs-class files (`*.md`, `projects/**`, `docs/**`, `.claude/**` …) is `docs_only` again. Tier 1 is skipped, and with it this job and the arch guard. This was the state before task 157. It is recorded in §8 F3, in the `INDEX.md` disclosure and in the note to `ci-cd-unit-test-remediation-r1`.
  - **The GitHub issue: the main session files it** (round 13 item 12: "the main session files a GitHub issue there"). It is not filed from this branch. Suggested text:
    - **Title:** `CI router: docs_only fails open for unclassified code paths (Tier 1 skipped when code ships with a *.md or task note)`
    - **Labels / project:** `ci-cd-unit-test-remediation-r1`.
    - **Body:** `ci-router.yml`'s `Derive docs_only flag` sets `docs_only=true` when the `docs` filter matched and none of `bff`, `spaarke_ai`, `ci_workflows` did. Any path no filter names therefore counts as docs when it ships with a `*.md` file or a `projects/**` note, and Tier 1 and Tier 2 are skipped, so `CI / Router` is green by construction. Examples: `src/client/shared/Spaarke.UI.Components/**` (it skips Tier 1's `datagrid-external-host-gate`, the jest proof of the external SPA's no-view-picker rule, task uac-r2 157), `src/client/external-spa/**` and `tests/Spaarke.ArchTests/**` (they skip the arch guard `ExternalSpaGridViewSelectorGuardTests`), `src/client/pcf/**`, `src/solutions/*` other than SpaarkeAi, `src/client/office-addins/**`, `scripts/**`, `infra/**`. Suggested fail-closed fix: a second `dorny/paths-filter` step with `predicate-quantifier: 'every'` and one filter, `'**'` plus the negated docs patterns, so `docs_only` is true only when every changed file is docs. It changes verdicts for that class of PR, so it needs a shadow-window re-baseline. References: `projects/ci-cd-unit-test-remediation-r1/notes/gate-repair-disclosure-2026-10-03-uac-r2-157.md`; task 157 note §8 F3; owner round 13 item 12.
- **Self-protection rewritten (`ExternalSpaGridViewSelectorGuardTests`).** `ScanDataGridCiGate` now reads only `ci-tier1-blocking.yml`. Every router assertion is removed (`RouterWorkflowFile`, `DocsOnlyVariables`, `DocsOnlyExclusions`, `DocsOnlyStepProblem`), so the guard does not constrain `ci-cd-unit-test-remediation-r1`'s router fix. The fact is renamed `SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate`. New: `DataGridGateAdvisory`, together with the job-level `continue-on-error` rule above, plus a refusal of `continue-on-error` on any step. Also new: pinned `needs: classify-tier1` and the pinned `if:`. The allowed job keys add `needs` and `continue-on-error`. `classify-tier1` may carry no job-level `if:`. Its `datagrid_gate` output must appear once, as written, and its filter must be defined once with exactly the two paths. Kept: the job present, the jest call, the verbatim assertion script, `|| true`, the step-level `if:` rule. The Theory has 24 rows (31 before; the 11 router rows are gone, 4 rows are new). Every row holds in both advisory states. The state-specific disarming is the new fact `ScanDataGridCiGate_RefusesAnAdvisoryStateTheConstantDoesNotRecord`. The two `needs` rows anchor on the gate's own `if:` line, because `needs: classify-tier1` alone also matches `changed-surface-smoke` and `auth-smoke`.

| Proof it bites (c2-r2, on disk; the workflow restored hash-matched and touched) | actionlint | `SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate` |
|---|---|---|
| (a) the job's `continue-on-error: true` line deleted, `DataGridGateAdvisory` left `true` (a YAML-only flip) | exit 0 | red: `must carry exactly one job-level continue-on-error: true while DataGridGateAdvisory is true` |
| (b) `needs: changed-surface-smoke` on the gate | exit 1 (the `if:` reads `needs.classify-tier1`) | red: `must carry exactly needs: classify-tier1` |
| (f) `needs: [classify-tier1, changed-surface-smoke]` | exit 0 | red: `must carry exactly needs: classify-tier1` |
| (c) the `datagrid_gate` filter narrowed to `…/README.md` | exit 0 | red: `paths filter datagrid_gate must be defined once and hold exactly …` |
| (d) `continue-on-error: true` on the jest step | exit 0 | red: `carries a step-level continue-on-error` |
| (e) `if: github.event_name == 'never'` on `classify-tier1` | exit 0 | red: `classify-tier1 must exist and carry no job-level if:` |
| **The flip itself**, done correctly (line deleted AND `DataGridGateAdvisory = false`; both files restored hash-matched and touched afterwards) | exit 0 | the whole class green, **220 / 220**. That includes the state fact's other branch (an added `continue-on-error` is refused once flipped) and all 24 rows in the blocking state |
| 24 in-memory disarmings of the real text (the Theory) + the state fact | n/a | each produces a violation |

A first draft of seed (b) replaced the first `needs: classify-tier1` in the file, which is `changed-surface-smoke`'s, not the gate's. The fact stayed green, correctly. The on-disk seed and the two Theory rows now anchor on the gate's own `if:` line.

**Runs (fix round c2-r2):**

| Suite | Result |
|---|---|
| actionlint 1.7.12 (`-shellcheck=`, as `workflows-validate.yml` runs it) | exit 0 on `ci-tier1-blocking.yml` and `ci-router.yml`, and on all of `.github/workflows` |
| `ExternalSpaGridViewSelectorGuardTests` | 220 / 220 (194 + 1 fact + 24 Theory rows + 1 state fact) |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 566, Failed 0, Total 566 (572 - 31 old Theory rows + 24 new + 1 state fact) |
| Integration `tests/integration/Sprk.Bff.Api.IntegrationTests` | Passed 104, Failed 0, Total 104 |
| Integration `tests/integration/Spe.Integration.Tests` | Passed 403, Failed 0, Skipped 25, Total 428 (7m38s) |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | **Contention, not this change.** About 278 `dotnet` processes from other agents were running on the machine. Full run: Passed 14177, Failed 130, Skipped 54, Total 14361 (47m42s). All 130 failures were `TaskCanceledException: The operation was canceled` (host HTTP calls timing out), spread over 65 classes. An isolated re-run of those 65 classes gave Passed 865, Failed 7, Total 872. All 7 were again `TaskCanceledException`, one in each of 7 classes. A re-run of those 7 classes gave **90 / 90 passed**. c2-r2 changes no BFF source or BFF test, and no file under `tests/unit` reads the workflows or the notes. |

No product source file changed in c2-r2: one workflow file edited, one reverted to its pre-task content, one arch-test file, and the notes and records. No client file changed, so no client build or jest run was needed (the job's jest call and assertion are unchanged from c2-r1, which ran them on Node 20.20.2). No live read or write. `.claude/**` edits needed: none.

**Runs (review round 3, fix round b2, on `task/uac-r2-157-b2` after merging `work/unified-access-control-r2` at `1f3e7bd76`):**

| Suite | Result |
|---|---|
| `ExternalSpaGridViewSelectorGuardTests` | 81 / 81 passed: 72 from b1 + 8 round-4 Theory rows (Z1–Z8) + 1 `index.html` control |
| NetArchTest `dotnet test tests/Spaarke.ArchTests` | Passed 426, Failed 0, Total 426 |
| BFF unit `dotnet test tests/unit/Sprk.Bff.Api.Tests` | Passed 14274, Failed 0, Skipped 54, Total 14328 (Debug, 18m54s) |
| SPA `npm install --legacy-peer-deps --no-audit --no-fund` + `npm run build` (vite) | exit 0. The bundle holds `nle=e=>u.jsx(ole,{...e,showViewSelector:!1})` and `showViewSelector:!0` occurs 0 times. `npx tsc --noEmit`: 6 errors, all pre-existing (`mock-data.ts`, `OutsideCounselDashboard.tsx`, shared `EntityCreationService.ts`), none in a changed file. The npm-touched `package-lock.json` was reverted |

## 8. Read-only findings (not acted on; out of scope)

- **F1: CLOSED in fix round c1 (2026-10-03)**, with no owner decision needed: owner decisions C6 / C9 say a contact gets only what it is granted. (a) The shared grid requests the sibling list only when the picker is on, and never on the external host. (b) The external `savedquery/{id}` and `savedqueries/{entity}` routes return only the views a module grid is registered to use (`ExternalModuleDescriptor.SavedQueryIds`), and the same 404 for an internal view, another entity's view, an unknown id or an entity with no module. Every module registers none: the six grid configurations were re-read live and read-only on 2026-10-02, and all are `source.type="inline"`. Tests: `ExternalModuleSavedQueryScopeTests` (28) plus 4 HTTP contract rows (the fourth, added in fix round c1-r1, serves a real internal `sprk_project` view through the app's `SavedQueryService` and requires the ROUTE to 404 it; seed X1 bites it); seeds B1–B7 and X1 in §7. The original finding, for the record:
- *(original)* **F1: the external SPA still fetches the internal sibling-view list.** With the selector off, `DataGrid.tsx` still calls `retrieveSavedQueriesForEntity` (`GET /api/v1/external/api/dataverse/savedqueries/{entity}`) on every grid load and discards the result. The external `savedqueries` and `savedquery/{id}` routes return internal view definitions (names, FetchXML, layoutXml) to external callers app-only. They carry no record data, and `/fetch` refuses any of those queries that names a non-allow-listed column. But the internal view design stays visible, and each load makes a wasted call. Possible follow-up, needing an owner decision: skip the sibling fetch when `showViewSelector` is false (a shared-lib change), and/or restrict the external `savedquer*` routes to the module grids' own needs.
- **F2: pre-existing SPA typecheck debt.** `npx tsc --noEmit` over the SPA reports 24 errors. All are the same React-types mismatch class (TS2786 / TS2322 between the SPA's `@types/react` 18 and the shared lib's types), including the existing `<DataGrid>` line in `GridWidgetBody.tsx`. None involves `showViewSelector`. `vite build` does not typecheck, and it succeeds.
  - **The count depends on the environment, not the round (measured in fix round c1-r1, one worktree, same installs).** `tsc` follows the SPA's `paths` into the shared library's `src`, so whether that library's `node_modules` is installed decides which React types load. With it installed: **24** errors at base `f55f3d426`, **25** at `55dfb9d8f` (c1). The diff is exactly one: `App.tsx(232)` TS2786 on the provider mount. `DataGrid.tsx(1234)` TS2322 is in both. With it hidden: **6** errors at `55dfb9d8f` (the `_sprk_projectid_value` and `DriveItem` ones). That explains the 6 / 24 / 25 spread recorded across earlier rounds (F2 here, §7, the POML). Compare only like with like. CI runs `vite build` for the external SPA, not `tsc`.
- **F3: the router's `docs_only` is an allow-list of code surfaces, not a deny-list of docs (found in review c2, item 6; pre-existing).** `docs_only` is true when a docs-class file changed and none of the named code filters matched. Any path that no filter names therefore counts as docs when it ships with a `.md` file or a task note. Fix rounds c2 and c2-r1 had closed that for the paths this task's guards cover (`Spaarke.UI.Components`, `src/client/external-spa/**`, `tests/Spaarke.ArchTests/**`). **Owner round 13 item 12 (2026-10-03) reversed that: no router change in this project. Fix round c2-r2 reverted `ci-router.yml` to its pre-task content, and the uac-r2 main session files a GitHub issue to `ci-cd-unit-test-remediation-r1` (suggested title and body: §7, "Fix round c2-r2").** So the skip is open for this task's paths too, and for every other unclassified path, e.g. `src/client/pcf/**`, `src/solutions/*` other than SpaarkeAi, `src/client/office-addins/**`, `scripts/**`, `infra/**` and `tests/**` outside the BFF and arch projects. A general fix would be fail-closed: a second paths-filter step with `predicate-quantifier: 'every'` and a `'**'` filter that negates the docs patterns, with docs_only true only when that filter does not match. It would change verdicts for far more PRs than this task's, so it belongs to `ci-cd-unit-test-remediation-r1`, the owner of the frozen router. It is recorded in the disclosure note to that project (`gate-repair-disclosure-2026-10-03-uac-r2-157.md`) and is not made here.
