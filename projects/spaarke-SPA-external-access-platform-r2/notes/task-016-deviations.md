# Task 016 — Outside Counsel widgets — notes & deviations

> Date: 2026-08-06 · Rigor FULL (sonnet @ high) · BFF-touching (bff-api) + frontend-touching

## /conflict-check result

SAFE (silent pass). Branch `work/spaarke-SPA-external-access-platform-r2`. No open PR touches
`Sprk.Bff.Api` / `external-spa` / `ExternalAccess` paths (only dependabot `.csproj` bumps). The
`teams-app-r1` worktree branch is 1 commit ahead of master and that single commit is
`chore(teams-app-r1): archive project — Issue #724 closed (Completed)` with **zero file diff**
vs master — teams-app-r1 is fully merged/archived (consistent with this project's own commit
`2d9739285 spec(external-access-r2): incorporate teams-app-r1 Option-A delivery`). Safe to proceed.

## §10 BFF Hygiene — Placement Justification

- **Where**: all BFF changes are 2 existing files in the isolated external-access corner
  (`Infrastructure/DI/ExternalAccessModule.cs`, `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs`)
  — no new file, no new endpoint, no new route. Six `AddExternalModule(...)` calls (documents /
  invoices / work-assignments / matters / grid-configuration, plus the perf-only `EmptyRecordIds`
  static field) added to the SAME composition method task 015 established.
- **No new AI dependency**: zero `IOpenAiClient`/`IPlaybookService`/AI-internal type references.
- **Feature-module DI (ADR-010)**: `ExternalModuleDescriptor` instances registered via the existing
  `AddExternalModule` extension (concrete, no interface) — the SAME pattern as the "collaboration"
  module from task 015. No new interfaces added anywhere.
- **Authz (ADR-008)**: unchanged — the new modules ride the SAME inherited group filter
  (`ExternalCollaboration` policy + `CallerPrincipalAuthorizationFilter`) as every other module; no
  route/filter/handler change.
- **Publish size**: Release, compressed (incl. PDBs, measured via `Compress-Archive -CompressionLevel
  Optimal` — note the measurement TOOL differs from task 015's, which may explain part of the delta;
  no new NuGet package was added, so the content-level delta should be near-zero) = **48.29 MB**
  vs 46.91 MB baseline → **+1.38 MB** (ceiling 60 MB; the +5 MB single-task escalation threshold is
  NOT hit; the 55 MB cumulative-review threshold is NOT hit). `dotnet list package --vulnerable
  --include-transitive` → **no vulnerable packages** (no package reference changed).
- **Tests (ADR-038)**: one existing KEEP-path contract test
  (`tests/integration/contract/Api/ExternalAccess/ExternalModuleDataContractTests.cs`) updated —
  see D-016-3 below. No new banned patterns (`Mock<HttpMessageHandler>`, DI-registration, ctor-null)
  introduced.

## How this extends the shipped seam (does NOT redesign)

Five `AddExternalModule` registrations, mirroring the "collaboration" module's shape exactly:
`documents` (`sprk_document`), `invoices` (`sprk_invoice`), `work-assignments`
(`sprk_workassignment`), `matters` (`sprk_matter`), `grid-configuration`
(`sprk_gridconfiguration`). No handler, route, or filter change; no new endpoint.

## Deviations

### D-016-1 — Matters Tier-2 scope is intentionally ALWAYS-EMPTY (Path A exception, per CLAUDE.md §6.5)

**ADR/constraint in tension**: NFR-08 (per-module record-scope) implicitly expects a REAL Tier-2
predicate per module — "being entitled to the widget does not reveal all records" presumes there
IS a non-trivial accessible set to enforce against.

**Conflict**: `sprk_matter` has **no lookup back to `sprk_project`** in the current schema (verified
against `docs/data-model/sprk_matter-related-tables.md` + `entity-relationship-model.md`: Matter and
Project are documented as **PEER top-level parents** — "Matter/Project" is one combined ERD box, not
a parent→child chain — and a full-column grep of the Matter dump found zero `sprk_project`-target
lookup). The one candidate scope source, `sprk_matter.sprk_assignedoutsidecounsel` →
`sprk_organization` (the law firm assigned to the matter), cannot be evaluated because
`CallerPrincipal` carries **no organization affiliation** for the calling Contact anywhere in this
codebase — building that resolution (Contact→Organization affiliation) is a genuinely NEW capability,
outside this task's "minimal BFF touch, prefer reuse" mandate (POML notes: "add read endpoints only
if 015's path does not cover an entity").

**Proposed path**: **A — project-scoped exception.** Register the `matters` module with an
ALWAYS-EMPTY `AccessibleRecordIds` (fail-closed — `ScopeRows` returns zero rows for every caller,
regardless of participation). Pair with the grid config's `emptyStateMessage`: **"Matter-level
workspace access is coming soon."** — the VERBATIM copy from R1's own `OutsideCounselDashboard.tsx`
stub (`MyMattersSection`). Since R1 never shipped a working Matters surface either (only that same
placeholder text), this is R1-parity-preserving, not a regression — task 016's own acceptance
criterion #2 ("R1 parity... same columns/filters/actions, no loss of behavior") is trivially met
because there is no R1 Matters *behavior* to lose.

**Rationale**: Building a WRONG Tier-2 predicate (e.g., silently scoping by something unrelated) would
risk either over-exposure (NFR-08 violation) or a confusing false-negative that looks like a bug
rather than a documented gap. An always-empty, explicitly-commented, fail-closed predicate is
security-honest and matches R1's own stub behavior exactly.

**Alternative considered (and rejected)**: extending `CallerPrincipal` + both plane strategies
(`CiamContactPrincipalStrategy`, `WorkforcePrincipalStrategy`) with a new Contact→Organization
affiliation resolution, then scoping Matters by `sprk_assignedoutsidecounsel == callerOrgId`. Rejected
for THIS task because (a) it requires a brand-new resolution capability that doesn't exist anywhere
in the codebase today (Contact has no verified organization-affiliation field consumed by any
existing service), (b) it touches the SHARED plane-strategy code both CIAM and workforce depend on —
a much larger blast radius than "register one module," and (c) the POML explicitly scopes this task
to "mostly authors grid config + thin wrappers + registry entries."

**Impact of Path A**: the Matters widget renders correctly (tab, entitlement gating, dark/Teams
theme all work) but always shows the empty state — functionally identical to R1. A follow-up project
task (not filed as a GitHub issue by this task — flagging here per the ADR Conflict Resolution
Protocol for the orchestrating session to route via `/project-defer-issue-tracking` if desired) would
build the real Contact→Organization affiliation resolver + a genuine Matter Tier-2 predicate.

### D-016-2 — `sprk_gridconfiguration` registered as an additional Tier-2 module (static allow-list)

Every `<DataGrid configId=…/>` fetches its OWN `sprk_gridconfiguration` record via
`BffDataverseClient.retrieveRecord` BEFORE it can resolve an entity/fetchXml at all
(`DataGrid.tsx`'s `fetchConfigRecord`). That read goes through the SAME Tier-2-gated
`GET /api/dataverse/record/{entity}/{id}` seam — so `sprk_gridconfiguration` had to ALSO become a
registered module, or every widget's config load would be denied (403) and no grid would ever
render. This was **not called out in task 015's notes** (015 only ever consumed
`ExternalModuleDataEndpoints` for `sprk_project` reads via the ProjectPage-style hand-rolled surface,
never through `<DataGrid configId=…>` — task 016 is the FIRST real `<DataGrid>` consumer). Registered
as a small **static allow-list** of the exact 5 config-record GUIDs task 016 authored (NOT "all
gridconfiguration records") — a grid config carries only column/layout metadata, no tenant PII, no
Graph pointer, so exposing exactly these 5 records to any authenticated caller is safe.

### D-016-3 — `TryGetRecordId` extended to handle `EntityReference` (framework touch, contra the POML's "no framework change" expectation)

The POML/task-015 notes anticipated "AddExternalModule with one descriptor each, no framework
change." In practice, `ExternalModuleDescriptor.TryGetRecordId` only handled `Guid` / `string` values
— sufficient for the "collaboration" module (`RecordIdAttribute = "sprk_projectid"`, the entity's OWN
primary id, which the Dataverse SDK projects as a raw `Guid`). Documents / Invoices / Work
Assignments are scoped via a **lookup attribute pointing at `sprk_project`** instead (see below) —
and the Dataverse SDK projects lookup-typed FetchXML attributes as `Microsoft.Xrm.Sdk.EntityReference`,
not a raw `Guid`. Without a fix, `TryGetRecordId` would silently fail to extract the id (falling
through to `Guid.TryParse(raw.ToString())`, which fails for an `EntityReference` since it doesn't
override `ToString()`), and EVERY row would be excluded — the three widgets would always render
empty. Fixed by adding ONE new `case EntityReference` to the existing `switch` — purely additive; the
Guid/string cases (and the "collaboration" module's own-primary-id scoping) are byte-for-byte
unchanged. Also updated the one KEEP-path contract test this touched (see D-016-4).

**The elegant reuse this unlocked**: `RecordIdAttribute` does not have to be the module's OWN primary
id — it can be ANY attribute on the row, checked for membership in `AccessibleRecordIds(principal)`.
Documents / Invoices / Work Assignments point `RecordIdAttribute` at their typed lookup BACK to
`sprk_project` (`sprk_document.sprk_project`, `sprk_invoice.sprk_project`,
`sprk_workassignment.sprk_regardingproject` — all confirmed via `docs/data-model/**` +
`ExternalDataService.cs`'s existing `GetDocumentsAsync`), and reuse the EXACT SAME
`principal.GetAccessibleProjectIds().ToHashSet()` predicate the "collaboration" module already uses.
No extra Dataverse round-trip at predicate-evaluation time (the descriptor's delegate contract is
synchronous, reading only the already-resolved `CallerPrincipal`) — this is what made a genuine,
framework-fitting Tier-2 scope possible for 3 of the 4 non-Projects widgets without redesigning
anything.

### D-016-4 — Stale KEEP-path contract test updated (`ExternalModuleDataContractTests.cs`)

`ModuleFetch_WhenEntityHasNoRegisteredModule_Returns403` used `sprk_matter` as its "no module
registered" example entity. Since this task registers `sprk_matter` (D-016-1), the test started
failing (500, because `FetchService.ExecuteAsync` requires a live Dataverse `ServiceClient` the
`WebApplicationFactory` test host doesn't provide, and the code now reaches that far instead of
short-circuiting at the registry-lookup gate). Swapped to `contact` — an OOB entity that will never
have an external module registered — same fail-closed assertion, same scenario, no scope reduction.
Verified: 6/6 tests in this file pass; full suite (9803/0/101 skipped) passes.

### D-016-5 — Pre-existing shared-lib `tsc` hygiene fixed to unblock the first real `<DataGrid>` external-spa consumer

`external-spa`'s `tsconfig.json` has `noUnusedLocals`/`noUnusedParameters: true`. Task 016 is the
FIRST time `external-spa` deep-imports `DataGrid.tsx` / `HeaderCellContent.tsx` / `csvExport.ts` (via
the `@spaarke/ui-components/*` → `../shared/Spaarke.UI.Components/src/*` Vite alias) — pulling their
full dependency graph into `tsc`'s program for the first time and surfacing PRE-EXISTING dead code
that no prior consumer's stricter tsconfig had caught: an unused `FilterChipBar` import in
`DataGrid.tsx`; two dead local functions (`buildMetadataShim`, `boundsFromIsoState`, confirmed
unreferenced anywhere via repo-wide grep) plus their now-unused imports in `HeaderCellContent.tsx`;
and two intentionally-retained-but-`noUnusedParameters`-tripping signature-compatibility parameters in
`csvExport.ts` (renamed to the `_`-prefixed convention `noUnusedParameters` respects — zero behavior
change). Also added `@types/node` as an `external-spa` devDependency (`BffDataverseClient.ts`'s
`process.env` fallback needs the ambient `process` type — same pattern every Vite/Node project needs).
None of these touch DataGrid/BffDataverseClient runtime behavior; `npm run build` (Vite) was already
green before these fixes (Vite doesn't type-check) — only `tsc --noEmit` was affected.

### D-016-6 — Read-only command-bar lockdown (self-review finding, applied before completion)

The DataGrid framework's OOB command-bar default includes a `+ New` button (`Xrm.Navigation.openForm`)
that silently no-ops on this Xrm-free host (no `window.Xrm`) rather than crashing — safe, but not
UX-honest for a widget documented as READ-ONLY (NFR-01). All 5 grid configs now explicitly set
`commandBar.showDefaultCommands: { newRecord: false, delete: false, refresh: true, exportExcel: true,
editColumns: false, editFilters: false }` so the command bar never shows an affordance that does
nothing.

## §11 — Component justification (new client-side files)

**`gridDataverseClient.ts`** (new): (1) Existing — `BffDataverseClient` already exists, reused
as-is unmodified. (2) Extension — the SPA's existing `bffApiCall` (in `auth/bff-client.ts`) returns
parsed JSON, not a raw `Response`; `BffDataverseClient` needs the raw `Response` for its own
ProblemDetails unwrap, so it cannot be reused directly — a ~25-line local adapter mirroring
`bffApiCall`'s token-acquire + 401-retry shape is the smallest correct fix. (3) Cost of doing
nothing — without it, `new BffDataverseClient(...)` cannot be constructed, so none of the 5 widgets
can read data at all.

**`GridWidgetBody.tsx`** (new): (1) Existing — `<DataGrid>` + theme-storage utilities already exist,
reused as-is. (2) Extension — `<DataGridPageShell>` (the framework's own canonical mount) was
considered and REJECTED: it injects a GLOBAL `html/body{overflow:hidden}` CSS reset intended for a
standalone Custom Page's own document, which would fight the workspace shell's own tab-strip/dockable
assistant layout since these widgets are embedded as ONE TAB inside a larger shell, not a full page.
(3) Cost of doing nothing — without a shared factory, all 5 widget files would hand-roll their own
theme-listener + FluentProvider-adjacent wiring (duplication CLAUDE.md §11 forbids).

**5 thin widget files** (`ProjectsWidget.tsx`, `MattersWidget.tsx`, `WorkAssignmentsWidget.tsx`,
`DocumentsWidget.tsx`, `InvoicesWidget.tsx`): one-line `createGridWidgetBody(CONFIG_ID)` bindings per
the constraints doc's literal shape ("a widget = a sprk_gridconfiguration record + a thin wrapper + a
registry entry").

## Shared component reuse

- `<DataGrid configId=… />` (`@spaarke/ui-components/components/DataGrid/DataGrid`) — the ENTIRE
  data-grid surface for all 5 widgets; zero hand-rolled grid code.
- `BffDataverseClient` (`@spaarke/ui-components/services/BffDataverseClient`) — reused unmodified;
  task-015 D-015-2 flagged the `bffBaseUrl` wiring as this task's concern (now done:
  `{BFF_API_URL}/api/v1/external`).
- `resolveCodePageTheme` / `setupCodePageThemeListener`
  (`@spaarke/ui-components/utils/themeStorage`) — the SAME utilities `main.tsx` already uses for the
  app's ambient theme; reused for the grid's own popover/menu portal theming (ADR-021).

## Lookup labels without link-entity joins (hard constraint)

No widget config uses `<link-entity>`. Every FetchXML is a flat, single-entity `inline` source
(`source.type: "inline"`) — the external fetch gate (`ExecuteScopedFetchAsync`) rejects any
cross-entity reference outright (D-015-1). Where a display value would normally come from a joined
lookup label, the columns instead show DIRECT columns on the child entity itself (e.g. Documents
shows `sprk_documenttype`/`createdon`, not a joined project name) — consistent with D-015-1's
documented "R1 grids get lookup labels from Dataverse formatted values without joins" pattern (no
widget in this task actually needed a cross-entity label column; the hidden Tier-2 attribute
(`sprk_project` / `sprk_regardingproject`) is a raw lookup value, never rendered).

## Row-open / write-path (READ-ONLY, NFR-01)

No `onRecordOpen` / `onCreateNew` override is wired. The framework's default row-open handler
(`Xrm.Navigation.navigateTo`) and default `+ New` command both require `window.Xrm`, which is absent
on this CIAM-only SPA host — both no-op with a console warning rather than crashing. Combined with
D-016-6 (explicit command-bar lockdown), this keeps the widgets genuinely read-only without any
write-path code existing to audit. A future task adding a detail/drill-through view would supply an
explicit `onRecordOpen`.

## Verification

- `npx tsc --noEmit` (external-spa): **clean, exit 0**.
- `npm run build` (external-spa, Vite): **succeeded** (2514 modules; `dist/assets/app.js` 1.34 MB /
  368 KB gzip — pre-existing >500 KB chunk-size warning, not introduced by this task).
- `dotnet build src/server/api/Sprk.Bff.Api/`: **Build succeeded, 0 errors** (23 pre-existing
  warnings, none touching files this task modified).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/`: **9803 passed / 0 failed / 101 skipped** (one test
  updated per D-016-4; full suite green).
- Publish (Release, compressed incl. PDBs): **48.29 MB** (+1.38 MB vs 46.91 MB baseline — see §10
  note on measurement-tool difference; well under the 60 MB ceiling, does not hit the +5 MB /
  55 MB escalation thresholds).
- `dotnet list package --vulnerable --include-transitive`: **no vulnerable packages** (no package
  added).
- Quality gates (code-review + adr-check, self-invoked per Step 9.5): 2 actionable findings, both
  FIXED before completion — (1) `matters` module's per-call `HashSet` allocation → static
  `EmptyRecordIds` field; (2) implicit read-only reliance on `Xrm` absence → explicit
  `commandBar.showDefaultCommands` lockdown on all 5 grid configs (D-016-6). No Critical findings.
  ADR scan: 0 violations (ADR-021 no hex/v8 imports; ADR-028 the one Bearer-literal site is the
  pre-existing documented SPA-wide exception, same pattern as `bff-client.ts`; ADR-008 no global
  middleware; ADR-010 no new interfaces; ADR-013 no AI-internal type injection).

## Acceptance criteria — status

| # | Criterion | Status |
|---|---|---|
| 1 | Five role-default widget tabs, each a DataGrid backed by its `sprk_gridconfiguration` record | **Met** — `widgetRegistry.ts` lazy-loaders swapped from `placeholderLoader` to the 5 real widgets |
| 2 | R1 parity (same columns/filters/actions, no loss of behavior) | **Met** — Projects/Documents parity verified against `OutsideCounselDashboard.tsx`; Matters has NO R1 behavior to lose (R1's own stub, D-016-1); Work Assignments/Invoices are genuinely NEW (R1 never built them — see design.md §12 post-pivot framing) |
| 3 | Read via BffDataverseClient over the task-015 seam, app-only, no OBO; Tier-2 scoped to participation | **Met** — `gridDataverseClient` targets `/api/v1/external`; all 5 modules Tier-2-scoped (4 real, 1 documented fail-closed exception) |
| 4 | Negative (NFR-08): no participation → no rows | **Met** — `ScopeRows` fail-closed on empty accessible set (existing task-015 behavior, unchanged); Matters is UNCONDITIONALLY empty (strictest possible negative case) |
| 5 | Negative (NFR-01): no Graph pointer exposed; read-only | **Met** — no write path wired (D-016-6); BffDataverseClient has no create/update/delete methods |
| 6 | Dark mode + Teams theme, zero hardcoded hex | **Met** — `GridWidgetBody` reuses `resolveCodePageTheme`/listener; grep confirms zero hex in new files |
