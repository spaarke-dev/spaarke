# VisualHost Decoupling & `@spaarke/visuals` Extraction

> **Status**: Design (pre-spec) — input to `/design-to-spec` → `/project-pipeline`
> **Author**: Claude Code session, 2026-07-10
> **Supersedes**: `ASSESSMENT.md` (the "stale tarball → mini project" framing is retired; see §2)
> **Folder-name note**: the project folder is `visual-host-version-update` (from the original narrow trigger). Actual scope is broader — decoupling VisualHost from shared-lib source-consumption and extracting a canonical visuals library. Folder kept for continuity; title reflects true scope.

---

## Executive Summary

VisualHost is the only PCF that consumes the shared library by bundling its **raw `src/`** (deep relative imports) rather than the built package. That single fact produces two independent problems pointing in opposite directions:

- **Inbound** (shared-lib *wizards* → VisualHost): bundling shared-lib source drags the shared lib's transitive dependencies (`@spaarke/sdap-client`, `@spaarke/auth`) into VisualHost's build graph and forces a React 16/17-vs-19 version skew. This is the build/test conflict.
- **Outbound** (VisualHost *visuals* → the rest of the app): VisualHost's 18 visualization components are trapped inside the PCF bundle, so five other surfaces have independently re-implemented the same `MetricCard`/chart/bar vocabulary.

This project fixes both: **(A)** switch the "+" Create button to launch wizards via `Xrm.Navigation.navigateTo` (the established Spaarke wizard-modal pattern) — decoupling the inbound dependency; and **(B)** extract the visuals into a new canonical `@spaarke/visuals` sibling package — enabling the outbound reuse. It ships the pending `cleanGuid` fix as its first increment and amends ADR-012 to sanction the new library and restate the anti-fragmentation boundary.

---

## Problem Statement

### Trigger
The `cleanGuid` GUID-normalization fix (PR #603) was merged into `@spaarke/ui-components` and deployed to the 5 standalone wizard code pages, but could not reach VisualHost's inline "+" Create wizards. The original assessment attributed this to a stale version-pinned tarball (`spaarke-ui-components-2.0.0.tgz`, May 27).

### Actual root cause (verified this session)
The tarball is a red herring. VisualHost imports the shared lib through **two channels**:
- **Channel A** — one deep-`dist` package import (`ThemeProvider` → `@spaarke/ui-components/dist/utils/themeStorage`). Only this is governed by the tarball.
- **Channel B** — many **raw relative `src/` imports** (`../../../../shared/Spaarke.UI.Components/src/...`) for the wizard registry, adapters, `PolymorphicResolverService`, `AiSummaryPopover`, `AppInsightsService`.

The "+" wizards come through Channel B, so `cleanGuid` (which lives in the wizard services) would reach them on any rebuild — the tarball never blocked it. Repointing the tarball to the directory dependency (the assessment's recommended fix) was applied this session and **the build still failed**, proving the dependency declaration was never the issue.

### Why the build actually fails
Because webpack compiles shared-lib `src/` *inside* VisualHost's build, bare-specifier imports in those physically-shared-lib files resolve against the **shared lib's** `node_modules`, not VisualHost's. The failures were:
1. `@spaarke/sdap-client` — declared in the shared lib but its `dist` was never built → types unresolved.
2. `@spaarke/auth` — genuinely used by the shared lib (SprkChat, LookupField) but **never declared** as a dependency → absent from the shared lib's `node_modules`.
Plus one real local implicit-any in `VisualHostRoot.tsx:505`.

### The React version skew
Three React "worlds" collide in one compile: runtime **16.14** (virtual PCF platform-library), VisualHost `@types/react` **18**, shared-lib authoring **19**. Today this is **types-only cosmetic** (three `as unknown as React.ComponentType` casts; zero React-18/19-only runtime APIs in the wizard graph — audited). But the inline model imposes a permanent "React-16-safety tax": the shared wizards must stay hand-disciplined to the React-16 subset, enforced by comment banners rather than the compiler.

### The trapped-visuals problem (outbound)
VisualHost's 18 visual components are context-agnostic (only `VisualHostRoot` touches PCF types) but locked in the PCF bundle. Evidence of the cost: **five independent `MetricCard` implementations** exist (VisualHost, WorkspaceShell, ×2 LegalWorkspace, Daily Briefing `StatTiles`), duplicated trend/badge/segment-bar logic, and `MetricsDashboardWidget` carries `// Phase B: VisualHost ChartRenderer hoist` TODOs — the intent to reuse VisualHost's charts is already written into the codebase.

---

## Proposed Solution

### Two-direction fix

| Direction | Fix | Effect |
|---|---|---|
| **Inbound** — wizard launch | Replace inline `React.lazy` embedding with `Xrm.Navigation.navigateTo` to the standalone wizard **code pages** | Removes all shared-lib-`src` wizard imports → kills the dep leakage and the React-skew tax; removes `@spaarke/auth` wiring from VisualHost |
| **Outbound** — visual reuse | Extract the 18 visuals + pure utils + types into a new **`@spaarke/visuals`** sibling package; PCF becomes a thin host | Visuals become reusable by code-page dashboards, Daily Briefing, WorkspaceShell, AI widgets; single source of truth kills the five-MetricCard duplication |

### Why navigateTo (not "add the missing deps")
Three options were considered for the inbound fix: (1) declare the leaked deps on VisualHost — band-aid, re-arms the trap on every new shared-lib dep; (2) bundle the shared lib — large shared-surface change; (3) navigateTo the code pages. **#3 chosen.** It is the established Spaarke wizard-modal pattern (`sprk_wizard_commands.js` already launches the Event wizard as a `navigateTo` webresource dialog at 60%×70%, the same size as today's inline dialog; `MODAL-DECISION-CRITERIA.md` treats wizards as exactly this pattern), and it eliminates both inbound problems by construction rather than papering over them.

### Auth (explicit de-risking)
The wizard code pages already bootstrap auth correctly via the shared `@spaarke/auth` service (`resolveRuntimeConfig()` → `initAuth()` → `authenticatedFetch`). Under navigateTo, VisualHost **stops touching auth for the create flow entirely** — the code page owns it. Net: less auth surface in the PCF, one well-trodden path, no new frailty.

### Regarding resolver + field mapping (hard requirement, preserved)
`initialAssociation` + `lockAssociation` are already first-class `CreateRecordWizard` config (pin the host record as parent, hide the Associate-To step). Field mapping + regarding resolution already run inside all three wizard services (`applyFieldMappings` / `FieldMappingService`, `applyResolverFields` / `PolymorphicResolverService`). The only new work is having each code page parse `entityType`+`entityId` from the navigate URL → build `initialAssociation` → pass `lockAssociation: true`. **Acceptance criterion:** creating an Event/Invoice/Report Card via the "+" fires regarding + field mapping identically to today.

### Drill-through / expand (preserved by construction)
The drill/"open list" logic (`ClickActionHandler` using `Xrm.Navigation`/`App.sidePanes`, driven by the `sprk_chartdefinition` config) **stays host-side in the PCF** — it is not part of the visual extraction. Visuals stay presentational and expose an `onDrillInteraction` callback; the PCF host wires it to `ClickActionHandler` + config exactly as today. A future code-page dashboard wires its own handler to the same callback. **Acceptance criterion:** configured drill-through/expand behavior unchanged.

### Card chrome & tool extensibility (config-driven, host-owned)
The tools rendered on each card (AI-summary sparkle, "+", expand, and future tools) are defined per-card in the `sprk_chartdefinition` JSON and may differ per VisualHost deployment. That makes the card-chrome/tool layer a **host + configuration concern, not a visual**. Design consequence:
- `@spaarke/visuals` holds **pure presentational visuals only** (props-in, `onDrillInteraction` callback) — dependency-free; it does not know what tools surround it.
- `CardChrome` + a **config-driven tool registry** stay **host-side in the PCF**, rendering whichever tools the chart-definition JSON specifies. This is the extensibility seam for adding new tools and for per-deployment tool sets.
- The registry is designed as an open contract (add a tool = add a registry entry + a config key), so a future code-page dashboard can reuse it. Promote the registry to a shared package only when that second host is real (§11) — not pre-emptively.
- **The registry composes shared tool components where they already exist.** `AiSummaryPopover` is already a shared `@spaarke/ui-components` component; the registry should reference it rather than re-implement. During Phase A/B, verify whether the other tool buttons ("+", drill-through/expand) are shared or VisualHost-local and normalize toward the shared source where one exists — without pre-emptively creating new shared components (§11).

This also resolves the `CardChrome`/`AiSummaryPopover` dependency question: `CardChrome` stays in the PCF (composing the shared `AiSummaryPopover`), so `@spaarke/visuals` keeps zero internal Spaarke deps.

### The common wizard template (already correct — no rework)
All three wizards are already **thin config wrappers over one shared `CreateRecordWizard`** (Event's header literally reads "Thin wrapper around CreateRecordWizard"). The template-with-toggles architecture the owner asked for already exists. The one tidy worth folding in: a small `bootstrapWizardPage()` factory so the three code pages don't triplicate auth/service/theme setup (DRY, not new architecture).

---

## Scope

### Phase A — Wizard launch via `navigateTo` (ships `cleanGuid`)
- **A0 (ship the fix first):** land the confirmed green-build recipe and deploy the `cleanGuid` fix. Persisted changes: build `@spaarke/sdap-client` + `@spaarke/auth` `dist` (harden the prebuild to keep them fresh, mirroring `ensure-dist-fresh`); **declare `@spaarke/auth` as a `@spaarke/ui-components` dependency** (latent-bug fix); fix `VisualHostRoot.tsx:505` implicit-any (done); repoint `@spaarke/ui-components` to the directory dependency (done). Bump version, deploy v1.4.35, UAT the braced-GUID create path. *This increment keeps the inline model temporarily; A1 removes it.*
- **A1:** build the missing **Invoice** and **Report Card** wizard code pages (Event's already exists); add the shared `bootstrapWizardPage()` factory.
- **A2:** wire `initialAssociation`/`lockAssociation` + a `themeOption` flag from the navigate URL in all three code pages.
- **A3:** switch VisualHost's "+" from inline `React.lazy` mount to `navigateTo`; delete the inline wizard-embedding code, the lazy `@spaarke/auth` bootstrap, and the three React-skew casts. Confirm the leak set (`@spaarke/sdap-client`, `@spaarke/auth`) is gone from VisualHost's graph.

### Phase B — Extract `@spaarke/visuals`
- **B1:** scaffold `src/client/shared/Spaarke.Visuals/` as a **source-only sibling** (mirror `@spaarke/events-components`: `"main": "./src/index.ts"`, subpath `exports`, peer-deps incl. **`@fluentui/react-charting`**). Zero internal Spaarke deps (keep `CardChrome` in the PCF).
- **B2:** move the 15 cleanly-presentational visuals + all 7 pure utils + the visualization types; reconcile the drifted duplication (VisualHost's 13-member `VisualType` superset vs the shared lib's stale 8-member copy; the duplicated `EventDueDateCard`) so `@spaarke/visuals` is the single owner.
- **B3:** refactor the 3 self-fetching visuals (`CalendarVisual`, `DueDateCard`, `DueDateCardList` — the last touches `window.Xrm`) to props-in; push their fetch + FetchXML-building into a PCF-side container. Split `ViewDataService` (pure FetchXML helpers move, WebAPI executors stay).
- **B4:** repoint VisualHost to consume visuals from `@spaarke/visuals`; verify the build + bundle size.
- **B5:** author the **ADR-012 amendment** (see ADR Tensions).

### Deferred adoption items (post-project — NOT a phase of this project)
After `@spaarke/visuals` exists, migrate the five duplicate `MetricCard`s / Daily Briefing `StatTiles` / WorkspaceShell / `MetricsDashboardWidget` onto it opportunistically as those surfaces are touched. Tracked as deferred items outside this project's deliverable and close.

### Hygiene (folded into Phase A)
Remove the two git-tracked `.tgz` artifacts (`1.0.0` 192 KB, `2.0.0` 4.5 MB) + add a `files` allow-list to the shared-lib `package.json`; remove the committed `storybook-static/` (92 files — VisualHost is the only PCF committing it) + gitignore it. **Leave** the committed Solution `bundle.js` (all 10 PCFs commit it — repo convention).

## Out of Scope
- Bundling the shared library (rejected in favor of navigateTo + source-only visuals).
- Migrating other PCFs' shared-lib consumption model (they use `dist/`, no leak).
- Phase C adoptions (separate incremental work).
- Any BFF / server change.

---

## Component Justification (root CLAUDE.md §11) — `@spaarke/visuals`

1. **Existing** — overlaps five scattered implementations (VisualHost visuals, WorkspaceShell `MetricCard`, ×2 LegalWorkspace, Daily Briefing `StatTiles`) and the stale visualization types in `@spaarke/ui-components`. Verified by grep across `src/client`.
2. **Extension** — cannot extend `@spaarke/ui-components` cleanly: the charts require `@fluentui/react-charting`, a heavyweight dep absent from every current shared package. Folding it into the base library taxes every consumer (code pages, add-ins) with a chart engine they don't use — re-creating the bundle bloat VisualHost's local types file was written to avoid. A dedicated, tree-shakeable, deep-import-isolated package is the correct boundary.
3. **Cost-of-doing-nothing** — concrete failure: `MetricsDashboardWidget` today ships throwaway inline-SVG chart placeholders precisely because it cannot import VisualHost's real charts; every new dashboard/report surface re-forks a sixth `MetricCard`. Without a canonical home the duplication compounds and drill/trend/badge behavior diverges per copy.

---

## ADR Tensions (root CLAUDE.md §6.5)

**ADR-012 (Shared Component Library)** designates `@spaarke/ui-components` as *the* single source of truth for reusable UI and does not carve out visualization. A new top-level `@spaarke/visuals` is in tension with the literal "single library" framing.

- **Rule challenged:** ADR-012 "one shared component library."
- **Conflict:** viz requires a heavyweight charting peer dep (`@fluentui/react-charting`) that should not be inherited by non-viz consumers; the "single library" reading would force that dep on everyone or keep the visuals trapped.
- **Proposed path: (B) ADR amendment.** ADR-012's *intent* is anti-fragmentation — stop ad-hoc per-project shared libs — not "exactly one package forever." Amend it to (1) sanction `@spaarke/visuals` as a governed canonical sibling alongside `@spaarke/auth`, `@spaarke/events-components`, `@spaarke/sdap-client`, and (2) restate the boundary: data-viz primitives live in `@spaarke/visuals`; no solution/PCF spins up a competing viz lib.
- **Alternative considered (rejected):** namespace inside `@spaarke/ui-components` — rejected because it re-introduces the charting-dep bloat and buries the library. The heavyweight-dep quarantine is the concrete §11 trigger that justifies a separate package.
- **Impact:** amendment (concise + full), merged alongside Phase B.

---

## Hot-Path Declaration (root CLAUDE.md §10 §G)

- **BFF:** N
- **SpaarkeAi (`src/solutions/SpaarkeAi/**`):** N
- **ci-workflows:** N
- **skill-directives (`.claude/skills/**`):** N
- **root-CLAUDE.md:** N (ADR-012 lives in `.claude/adr/` + `docs/adr/`; amendment touches those, not root CLAUDE.md)

Shared-surface note: this project modifies `@spaarke/ui-components` (`package.json` dep declaration + `files` allow-list) and the wizard code pages — coordinate via `projects/INDEX.md` / `/conflict-check` before merge.

---

## Success Criteria
1. `cleanGuid` shipped to VisualHost's "+" create path; braced-GUID create no longer 400s (`Error in query syntax`). *(Phase A0)*
2. `npm run build:prod` green from a clean worktree with the hardened prebuild; no undeclared transitive-dep failures. *(A0)*
3. VisualHost's "+" launches Event, Invoice, and Report Card via `navigateTo`; no shared-lib-`src` wizard imports remain; the three React-skew casts are deleted. *(A3)*
4. Regarding resolver + field mapping demonstrably fire from the "+" for all three entities. *(A2)*
5. Drill-through / expand behavior unchanged, still config-driven from `sprk_chartdefinition`. *(B4)*
6. `@spaarke/visuals` exists as a sibling package; VisualHost consumes visuals from it; bundle size within tolerance of the current 1.27 MiB. *(B4)*
7. Drifted duplication reconciled (single `VisualType` owner; one `EventDueDateCard`). *(B2)*
8. ADR-012 amendment merged. *(B5)*
9. Theme inherits correctly under navigateTo in explicit and auto modes (via `themeOption` flag). *(A2)*

---

## Risks & Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Building Invoice/Report Card code pages surfaces missing page-side wiring | Medium | Event page is the reference; `bootstrapWizardPage()` factory standardizes it |
| Theme flash in navigateTo "auto" mode | Low | Pass `themeOption=dark\|light` navigate flag (verify token: `applyMdaTheme` writes `darkmode` while `detectDarkModeFromUrl` matches `dark`) |
| Self-fetch visual refactor (`window.Xrm`) regresses DueDate/Calendar | Medium | Move fetch to PCF container; keep `EventDueDateCard` (already props-only) as the template; unit-test the container |
| `@fluentui/react-charting` peer-dep resolution in the new package | Low | Declare as peer; validate consuming build (PCF + a code-page smoke) |
| Shared-lib `@spaarke/auth` dep declaration affects other consumers | Low | Other PCFs externalize it via `dist/`; declaration is additive + correct |
| A0 band-aid entrenches inline model if A1–A3 slip | Medium | Sequence A0→A3 in one project; A0 explicitly labeled interim |

---

## Governing ADRs
- **ADR-012** Shared Component Library (amended here)
- **ADR-022** PCF Platform Libraries / React 16-safe (why the skew is types-only; navigateTo removes the constraint)
- **ADR-028** Spaarke Auth v2 (code-page auth path; unchanged)
- **ADR-024** Regarding (wizard services; unchanged)

---

## Phasing Recommendation
**A (ship `cleanGuid` first, then decouple wizards) → B (`@spaarke/visuals` + ADR-012) as one project.** A0 is independently shippable and should go out as soon as it passes UAT, ahead of the rest. Adoption of `@spaarke/visuals` by other surfaces is deferred (post-project, tracked separately).
