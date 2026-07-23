# CLAUDE.md — VisualHost Decoupling & `@spaarke/visuals` Extraction

> Project-scoped context. Loads when working this project. Defers to repo-root `CLAUDE.md`.

## Mission
Decouple VisualHost from shared-lib source-consumption (Phase A: wizards → `navigateTo`) and extract a canonical `@spaarke/visuals` package (Phase B). Ship `cleanGuid` first.

## Non-negotiables
- **Auth**: route everything through `@spaarke/auth` (`initAuth`/`authenticatedFetch`/`useAuth`, ADR-028). Never instantiate `PublicClientApplication` or pass token props. navigateTo must *reduce* PCF auth surface, not add it. (See project memory: auth is a high-risk area.)
- **`@spaarke/visuals` is presentational only** — no `Xrm`/`WebAPI`/`ComponentFramework`/FetchXML. Data binding stays with the host.
- **Drill-through/expand stays host-side + config-driven** (`ClickActionHandler` + `sprk_chartdefinition`), wired via `onDrillInteraction`.
- **Card chrome + tool registry stay host-side** (PCF), config-driven from the chart-definition JSON; compose shared tool components (`AiSummaryPopover`) where they exist.
- **PCF builds** use `npm run build:prod` (not `build`). Bump version in all 5 VisualHost locations on release.
- **`.claude/` writes are main-session only** (ADR-012 amendment, this file).

## Applicable ADRs
- **ADR-012** — amended here (path B). Sanctions `@spaarke/visuals` sibling; restates anti-fragmentation boundary.
- **ADR-022** — PCF platform React (why the skew is types-only; navigateTo removes the constraint).
- **ADR-028** — Spaarke Auth v2 (code-page auth; unchanged/reinforced).
- **ADR-024** — Regarding (wizard-service resolution; unchanged).
- **ADR-044** — Dataverse GUID Canonicalization (the `cleanGuid` ADR, merged from master). Use shared `cleanGuid` from the `@spaarke/ui-components` barrel at every Xrm-GUID boundary; never hand-roll. Applies to the VHVU-030 navigate envelope `entityId`.

## Key references
- Launcher: `src/client/webresources/js/sprk_wizard_commands.js`
- Code-page bootstrap: `src/solutions/CreateEventWizard/src/main.tsx`
- Sibling package shape: `src/client/shared/Spaarke.Events.Components/`
- Wizard template: `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/`
- Prebuild freshness: `src/client/shared/Spaarke.UI.Components/scripts/ensure-dist-fresh.js`

## Gotchas (from the design investigation)
- Build only fails because VisualHost bundles shared-lib `src`; navigateTo removes it. Confirmed green recipe: build sdap-client + auth `dist`, declare `@spaarke/auth` on ui-components, repoint ui-components to directory dep, fix the implicit-any.
- `themeOption` token mismatch: `applyMdaTheme` writes `darkmode`, `detectDarkModeFromUrl` matches `dark` — normalize in A2.
- Only `CreateEventWizard` has a code page; Invoice + Report Card must be built (A1).
- `EventDueDateCard` is duplicated in VisualHost + shared lib — reconcile in B2.
- **cleanGuid (PR #603/#609)**: it's a merged shared component in `@spaarke/ui-components` — consume via the dependency, never re-implement. VisualHost imports it only transitively (wizard services → barrel). **Verify it's in a build:prod bundle with `grep -oc 'trim().toLowerCase()'` (the normalizer body), NOT `grep cleanGuid`** — the identifier is minification-mangled and false-negatives. This is how the 5 code-page deploys were confirmed.
