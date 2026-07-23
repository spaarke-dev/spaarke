# VisualHost Decoupling & `@spaarke/visuals` Extraction — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-10
> **Source**: `design.md` (this project folder)
> **Supersedes**: `ASSESSMENT.md` (stale-tarball framing retired)

## Executive Summary

VisualHost is the only PCF that consumes the shared library by bundling its raw `src/`, which (a) drags shared-lib transitive deps into VisualHost's build and forces a React 16/17-vs-19 skew — the inbound build/test conflict — and (b) traps VisualHost's 18 reusable visualization components inside the PCF bundle, causing five other surfaces to re-implement the same visual vocabulary — the outbound duplication. This project switches the "+" Create button to launch wizards via `Xrm.Navigation.navigateTo` (decoupling inbound) and extracts the visuals into a new canonical `@spaarke/visuals` sibling package (enabling outbound reuse). It ships the pending `cleanGuid` fix as its first increment and amends ADR-012 to sanction the new library.

## Scope

### In Scope
- **Phase A — Wizard launch via `navigateTo`**
  - A0: restore a green VisualHost build and ship the `cleanGuid` fix (deploy v1.4.35 to dev).
  - A1: build the missing Invoice and Report Card wizard code pages; add a shared `bootstrapWizardPage()` factory.
  - A2: wire `initialAssociation`/`lockAssociation` + a `themeOption` flag from the navigate URL in all three code pages.
  - A3: switch VisualHost's "+" from inline `React.lazy` embedding to `navigateTo`; delete the inline embedding, the lazy `@spaarke/auth` bootstrap, and the three React-skew casts.
- **Phase B — Extract `@spaarke/visuals`**
  - B1: scaffold `src/client/shared/Spaarke.Visuals/` (source-only sibling; `@fluentui/react-charting` peer dep; zero internal Spaarke deps).
  - B2: move the 15 presentational visuals + 7 pure utils + visualization types; reconcile the drifted duplication.
  - B3: refactor the 3 self-fetch visuals to props-in; move fetch to a PCF container; split `ViewDataService`.
  - B4: repoint VisualHost to consume visuals from `@spaarke/visuals`; verify build + bundle size.
  - B5: author the ADR-012 amendment.
- **Card chrome / tool registry**: keep `CardChrome` + a config-driven tool registry host-side (PCF); compose shared tool components where they exist (`AiSummaryPopover`); normalize the "+" and drill/expand buttons toward a shared source where one already exists.
- **Hygiene** (folded into Phase A): remove the two git-tracked `.tgz` artifacts + add a `files` allow-list to the shared-lib `package.json`; remove committed `storybook-static/` + gitignore it.

### Out of Scope
- Bundling the shared library (rejected in favor of navigateTo + source-only visuals).
- Migrating other PCFs' shared-lib consumption model (they use `dist/`, no leak).
- **Adoption of `@spaarke/visuals` by other surfaces** (the five duplicate `MetricCard`s / Daily Briefing `StatTiles` / WorkspaceShell / `MetricsDashboardWidget`) — deferred, tracked separately.
- Broader PCF/code-page toolbar consolidation — separate initiative (already on the owner's list).
- Any BFF / server change. Prod promotion of v1.4.35.

### Affected Areas
- `src/client/pcf/VisualHost/` — control, components, services, manifest, Solution (version bump); "+" launch rewrite; PCF-side fetch container.
- `src/client/shared/Spaarke.UI.Components/package.json` — declare `@spaarke/auth`; add `files` allow-list; remove `.tgz` artifacts.
- `src/client/shared/Spaarke.SdapClient/`, `src/client/shared/Spaarke.Auth/` — ensure `dist` build freshness (prebuild hardening).
- `src/client/shared/Spaarke.Visuals/` — **new** package.
- `src/solutions/` — new Invoice + Report Card wizard code pages (Event page enhanced).
- `.claude/adr/ADR-012-*`, `docs/adr/ADR-012-*` — amendment.

## Requirements

### Functional Requirements

1. **FR-01 (A0)**: VisualHost builds green via `npm run build:prod` from a clean worktree, and the shipped bundle contains the `cleanGuid` normalization. Acceptance: `[build] Succeeded`; `cleanGuid` present in `out/controls/control/bundle.js`; braced-GUID create no longer 400s (`Error in query syntax`) in dev UAT.
2. **FR-02 (A0)**: The build-ordering fixes are persisted: `@spaarke/sdap-client` and `@spaarke/auth` `dist` are built (prebuild hardened to keep them fresh, mirroring `ensure-dist-fresh`); `@spaarke/auth` is declared as a `@spaarke/ui-components` dependency; `@spaarke/ui-components` is repointed to the directory dependency; the `VisualHostRoot.tsx:505` implicit-any is fixed. Acceptance: a fresh `npm install` + `build:prod` succeeds with no undeclared-module errors.
3. **FR-03 (A0)**: VisualHost version is bumped to v1.4.35 in all 5 locations (manifest, footer, `solution.xml`, packed `ControlManifest.xml`, `pack.ps1`) and deployed to dev. Acceptance: footer shows `v1.4.35`; `pac solution list` shows the imported version.
4. **FR-04 (A1)**: Standalone wizard code pages exist and build for **Invoice** and **Report Card** (Event already exists), each mounting the shared `Create{Entity}Wizard`. A shared `bootstrapWizardPage()` factory provides auth + service + theme setup so the three pages don't triplicate it. Acceptance: both pages build via the solution Vite pipeline; deployed as web resources.
5. **FR-05 (A2)**: All three wizard code pages parse `entityType`+`entityId` from the navigate `data` envelope → build `initialAssociation` → pass `lockAssociation: true`; and honor a `themeOption=dark|light` flag. Acceptance: launching a page with the envelope pins the host record as parent (Associate-To step hidden) and renders in the passed theme.
6. **FR-06 (A2)**: Creating an Event / Invoice / Report Card via the "+" fires the regarding resolver + field mapping identically to the current inline path. Acceptance: created record has the correct `sprk_regarding*` fields + mapped fields (parity with the standalone-page create).
7. **FR-07 (A3)**: VisualHost's "+" launches all three wizards via `Xrm.Navigation.navigateTo` (webresource dialog, 60%×70%); the inline `React.lazy` embedding, the lazy `@spaarke/auth` bootstrap, and the three `as unknown as React.ComponentType` casts are deleted. Acceptance: no `../../../../shared/**/src/**` wizard imports remain in VisualHost; `@spaarke/sdap-client`/`@spaarke/auth` no longer appear in VisualHost's build graph.
8. **FR-08 (B1)**: `@spaarke/visuals` exists at `src/client/shared/Spaarke.Visuals/` as a source-only package (mirrors `@spaarke/events-components`: `"main": "./src/index.ts"`, subpath `exports`, peer-deps incl. `@fluentui/react-charting`) with zero internal Spaarke dependencies. Acceptance: package resolves and typechecks (`tsc --noEmit`).
9. **FR-09 (B2)**: The 15 presentational visuals + 7 pure utils + visualization types are moved into `@spaarke/visuals`; the drifted duplication is reconciled — `@spaarke/visuals` is the single owner of `VisualType`/`IChartDefinition`/`ICardConfig`/`DrillInteraction` and of `EventDueDateCard`; stale copies in `@spaarke/ui-components` re-export or are removed. Acceptance: one `VisualType` definition repo-wide; no duplicate `EventDueDateCard`.
10. **FR-10 (B3)**: `CalendarVisual`, `DueDateCard`, `DueDateCardList` are refactored to accept fetched data via props (no internal `webApi`/`window.Xrm`); the fetch + FetchXML-building moves to a PCF-side container; `ViewDataService` is split (pure FetchXML helpers → `@spaarke/visuals`; WebAPI executors → PCF). Acceptance: the three visuals contain no data-access calls; behavior (data shown, drill-through) unchanged.
11. **FR-11 (B4)**: VisualHost consumes the visuals from `@spaarke/visuals`; drill-through/expand stays host-side and config-driven (`ClickActionHandler` + `sprk_chartdefinition`), wired via the `onDrillInteraction` callback. Acceptance: `build:prod` green; drill-through/expand behavior unchanged; bundle size within tolerance (see NFR-01).
12. **FR-12 (B5)**: ADR-012 is amended (concise + full) to sanction `@spaarke/visuals` as a governed canonical sibling and restate the anti-fragmentation boundary. Acceptance: amendment merged; `.claude/adr` + `docs/adr` consistent.
13. **FR-13 (hygiene)**: The two `.tgz` artifacts are removed and a `files` allow-list added to the shared-lib `package.json`; committed `storybook-static/` (92 files) is removed and gitignored. The committed Solution `bundle.js` is retained (repo convention). Acceptance: `git ls-files` shows the `.tgz`s + `storybook-static/` gone; a fresh `npm pack` of the shared lib excludes `coverage/`/`.storybook/`/`storybook-static/`.

### Non-Functional Requirements
- **NFR-01**: VisualHost bundle size stays within tolerance of the current **1.27 MiB** (no unexpected growth from the extraction/refactor). Verify per pcf-deploy skill ranges.
- **NFR-02**: Green `build:prod` reproducible from a clean worktree with no manual `--no-save` installs (the prebuild + declared deps make it deterministic).
- **NFR-03**: All auth flows route exclusively through the shared `@spaarke/auth` service (ADR-028); navigateTo reduces VisualHost's auth surface (no PCF-side MSAL/token wiring for the create flow).
- **NFR-04**: The React-16-safety constraint no longer binds the wizard path (wizards run on the code page's own React 19); no runtime regressions in the "+" flow.
- **NFR-05**: `@spaarke/visuals` is context-agnostic — no `Xrm`/`WebAPI`/`ComponentFramework`/FetchXML references (enforced by the B3 refactor).

## Technical Constraints

### Applicable ADRs
- **ADR-012** Shared Component Library — **amended by this project** (see ADR Tensions).
- **ADR-022** PCF Platform Libraries / React-16-safe — explains why the skew is types-only; navigateTo removes the constraint for the wizard path.
- **ADR-028** Spaarke Auth v2 — code-page auth path; unchanged and reinforced.
- **ADR-024** Regarding — wizard-service regarding resolution; unchanged.
- **ADR-044** Dataverse GUID Canonicalization (merged from master 2026-07-10) — this is the `cleanGuid` ADR. MUST normalize any Xrm-sourced GUID to bare-lowercase at the boundary you own, via the shared `cleanGuid` from the `@spaarke/ui-components` barrel (PR #609) — never a hand-rolled normalizer. **Direct impact on VHVU-030**: the `entityId` marshaled into the navigateTo data envelope is Xrm-sourced and MUST be `cleanGuid`'d before it enters the URL/wizard state.

### MUST Rules
- ✅ MUST route all token acquisition through `@spaarke/auth` (`initAuth`/`authenticatedFetch`/`useAuth`); MUST NOT instantiate `PublicClientApplication` or pass token props.
- ✅ MUST keep `@spaarke/visuals` presentational and data-binding-agnostic (no data fetch, no Xrm/FetchXML).
- ✅ MUST keep drill-through/expand host-side and config-driven (`sprk_chartdefinition`).
- ✅ MUST use `npm run build:prod` (not `build`) for PCF production builds (AP-1).
- ✅ MUST bump version in all 5 VisualHost locations on release.
- ❌ MUST NOT introduce a competing viz library in a solution/PCF (ADR-012 amended boundary).

### Existing Patterns to Follow
- `src/client/webresources/js/sprk_wizard_commands.js` — `navigateTo` webresource-dialog launcher (60%×70%).
- `src/solutions/CreateEventWizard/src/main.tsx` — code-page auth + theme bootstrap reference.
- `src/client/shared/Spaarke.Events.Components/` — source-only sibling package shape.
- `src/client/shared/Spaarke.UI.Components/scripts/ensure-dist-fresh.js` — prebuild freshness pattern to extend to auth/sdap-client.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/` — the common wizard template + `ICreateRecordWizardConfig` (`initialAssociation`/`lockAssociation`).

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-012 | "one shared component library (`@spaarke/ui-components`) as single source of truth for reusable UI" | Visualization requires a heavyweight `@fluentui/react-charting` peer dep that non-viz consumers should not inherit; the literal single-library reading forces the dep on everyone or keeps the visuals trapped | **B (amendment)** | ADR-012's intent is anti-fragmentation, not "exactly one package." Amend to sanction `@spaarke/visuals` as a governed canonical sibling and restate the boundary. Alternative (namespace inside `ui-components`) rejected: re-introduces the charting-dep bloat and buries the library. The heavyweight-dep quarantine is the concrete §11 trigger justifying a separate package. |

## Success Criteria
1. [ ] `cleanGuid` shipped to VisualHost's "+" path; braced-GUID create no longer 400s — Verify: dev UAT create Event/Invoice/Report Card via native picker.
2. [ ] `build:prod` green from clean worktree, no undeclared-dep failures — Verify: fresh `npm install` + build.
3. [ ] "+" launches all three wizards via `navigateTo`; no shared-lib-`src` wizard imports; React-skew casts deleted — Verify: grep VisualHost + build graph.
4. [ ] Regarding resolver + field mapping fire from the "+" for all three entities — Verify: inspect created record fields vs standalone-page parity.
5. [ ] Drill-through/expand unchanged, config-driven from `sprk_chartdefinition` — Verify: UAT expand/open-list on a configured chart.
6. [ ] `@spaarke/visuals` exists; VisualHost consumes from it; bundle ≈1.27 MiB — Verify: package resolves; bundle-size check.
7. [ ] Duplication reconciled (one `VisualType`, one `EventDueDateCard`) — Verify: repo grep.
8. [ ] ADR-012 amendment merged — Verify: `.claude/adr` + `docs/adr`.
9. [ ] Theme inherits correctly under navigateTo (explicit + auto via `themeOption`) — Verify: dev UAT in dark + light.

## Dependencies

### Prerequisites
- Shared-lib `node_modules` installed; `@spaarke/sdap-client` + `@spaarke/auth` `dist` built (A0 hardens this).
- Dev environment access + `pac` auth for PCF/code-page deploys.

### External
- None (no BFF/server change; no new Azure resources).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Fix direction | Inline band-aid vs navigateTo vs bundle for the wizard launch | **navigateTo (#3)** | Decouples inbound; removes leak + React skew |
| Package | `@spaarke/visuals` now, or namespace in ui-components | **Separate package now**, top-level sibling (not buried) | B1 scaffolds a new sibling |
| ADR-012 | Amend to accommodate | **Yes, update ADR-012** | B5 authors amendment |
| Sequencing | GUID fix first, ship it, then single project | **Yes — A0 first, then A+B one project** | A0 is independently shippable |
| Wizards used | Are Invoice + Report Card "+" launches in use | **Yes, both used** | A1 builds both code pages (not deferrable) |
| CardChrome / tools | Tools defined per chart-definition JSON, vary per deployment; AI summary is a shared component | Keep chrome + config-driven tool registry host-side; compose shared tool components | Registry seam host-side; visuals stay pure |
| A0 deploy target | Deploy v1.4.35 to dev for UAT, no prod promotion | **OK** | A0 scope bounded to dev |
| B3 refactor | Behavior-preserving (change where fetch happens, not what renders) | **OK** | Self-fetch visuals → props-in, container fetches |
| Shared-lib dep | Declare `@spaarke/auth` on `@spaarke/ui-components` (shared surface) | **OK** (additive; others externalize via `dist/`) | A0 persists the declaration |

## Assumptions
- **Deployment**: v1.4.35 to dev only; prod promotion is a separate operator action.
- **B3**: refactor preserves rendered data + drill-through; only relocates data-loading.
- **`themeOption` token**: verify `dark`/`darkmode` mismatch (`applyMdaTheme` writes `darkmode`; `detectDarkModeFromUrl` matches `dark`) during A2 and standardize.
- **Deferred adoption** is tracked on the owner's existing list (no new idea filed).

## Unresolved Questions
- [ ] None blocking. `themeOption` token normalization (above) to be confirmed empirically during A2 — does not block decomposition.

---
*AI-optimized specification. Original design: `design.md`.*
