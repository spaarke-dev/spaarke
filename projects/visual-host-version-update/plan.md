# Implementation Plan — VisualHost Decoupling & `@spaarke/visuals` Extraction

> **Source**: `spec.md` (this folder) · **Created**: 2026-07-10
> **Branch**: `work/visual-host-version-update`

## Architecture Context

### Discovered Resources
- **ADRs**: ADR-012 (Shared Component Library — **amended by this project**), ADR-022 (PCF Platform Libraries / React-16-safe), ADR-028 (Spaarke Auth v2), ADR-024 (Regarding).
- **Skills**: `pcf-deploy`, `code-page-deploy`, `dataverse-deploy`, `adr-check`, `code-review`, `ui-test`, `repo-cleanup`.
- **Patterns / references**:
  - `src/client/webresources/js/sprk_wizard_commands.js` — navigateTo webresource-dialog launcher (60%×70%).
  - `src/solutions/CreateEventWizard/src/main.tsx` — code-page auth + theme bootstrap reference.
  - `src/client/shared/Spaarke.Events.Components/` — source-only sibling package shape.
  - `src/client/shared/Spaarke.UI.Components/scripts/ensure-dist-fresh.js` — prebuild freshness pattern.
  - `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/` — common wizard template + `ICreateRecordWizardConfig`.
- **Guides**: `docs/guides/PCF-DEPLOYMENT-GUIDE.md`, `docs/standards/MODAL-DECISION-CRITERIA.md`, `src/client/pcf/CLAUDE.md`, `src/client/shared/CLAUDE.md`.

### Key facts established during design (do not re-derive)
- VisualHost bundles shared-lib **raw `src/`** → transitive-dep leakage (`@spaarke/sdap-client`, `@spaarke/auth`) + React 16/17-vs-19 skew. Confirmed green build recipe exists (Phase A0).
- The `cleanGuid` fix reaches the wizards via live `src` on any rebuild; the tarball was never the blocker.
- All three wizards are thin config wrappers over `CreateRecordWizard`; `initialAssociation`/`lockAssociation` are first-class config.
- `@fluentui/react-charting` is a real dep of Bar/Line/Donut charts → justifies a separate `@spaarke/visuals` package.
- 15 of 18 visuals are cleanly presentational; 3 self-fetch (`CalendarVisual`, `DueDateCard`, `DueDateCardList`).

## Phase Breakdown

### Phase A — Wizard launch via `navigateTo` (decouples inbound; ships `cleanGuid`)

**A0 — Ship the `cleanGuid` fix (green build first)**
- Harden the build: build + keep-fresh `@spaarke/sdap-client` and `@spaarke/auth` `dist` (extend the `ensure-dist-fresh` pattern); declare `@spaarke/auth` as a `@spaarke/ui-components` dependency (latent-bug fix). Verify a clean-worktree green `build:prod` with `cleanGuid` in the bundle.
- Hygiene: remove the two `.tgz` artifacts + add a `files` allow-list to the shared-lib `package.json`; remove committed `storybook-static/` + gitignore.
- Bump VisualHost to **v1.4.35** (5 locations), rebuild, deploy to **dev**, UAT the braced-GUID create path for Event/Invoice/Report Card.

**A1 — Standalone wizard code pages**
- Build the **Invoice** and **Report Card** wizard code pages (mirror `CreateEventWizard`); add a shared `bootstrapWizardPage()` factory (auth + services + theme) and adopt it in all three pages.

**A2 — Launch-context + theme wiring**
- Parse `entityType`+`entityId` from the navigate URL → build `initialAssociation` → pass `lockAssociation: true` in all three pages; honor `themeOption=dark|light`; normalize the `dark`/`darkmode` token mismatch.
- Verify regarding-resolver + field-mapping parity from the "+" for all three entities.

**A3 — Cut over VisualHost's "+"**
- Replace inline `React.lazy` embedding with `Xrm.Navigation.navigateTo`; delete the inline embedding, the lazy `@spaarke/auth` bootstrap, and the three React-skew casts; confirm the leak set is gone from VisualHost's build graph.
- Deploy updated VisualHost + wizard pages to dev; UAT the "+" via navigateTo (all three, dark + light).

### Phase B — Extract `@spaarke/visuals` (enables outbound reuse)

**B1 — Scaffold the package**
- Create `src/client/shared/Spaarke.Visuals/` as a source-only sibling (`"main": "./src/index.ts"`, subpath `exports`, peer-deps incl. `@fluentui/react-charting`); zero internal Spaarke deps.

**B2 — Move visuals + reconcile duplication**
- Move the 15 presentational visuals + 7 pure utils + visualization types into `@spaarke/visuals`.
- Reconcile the drifted duplication: `@spaarke/visuals` becomes the single owner of `VisualType`/`IChartDefinition`/`ICardConfig`/`DrillInteraction` and `EventDueDateCard`; `@spaarke/ui-components` re-exports or removes stale copies.

**B3 — Refactor self-fetch visuals**
- Refactor `CalendarVisual`, `DueDateCard`, `DueDateCardList` to props-in; move fetch + FetchXML-building to a PCF-side container; split `ViewDataService` (pure helpers → package; WebAPI executors → PCF).

**B4 — Consume + verify**
- Repoint VisualHost to consume visuals from `@spaarke/visuals`; keep `CardChrome` + the config-driven tool registry host-side; verify build, bundle size (≈1.27 MiB), and unchanged drill-through/expand. Deploy + UAT parity on dev.

**B5 — ADR-012 amendment**
- Author the amendment (concise `.claude/adr` + full `docs/adr`) sanctioning `@spaarke/visuals` and restating the anti-fragmentation boundary. *(Main-session, sequential — `.claude/` write boundary.)*

### Wrap-up
- Update README status, lessons-learned, `/test-diet` if tests changed, repo-cleanup, deferred-adoption note.

## Parallel & Dependency Notes
- Mostly sequential (build-chain dependencies). Limited parallelism: A1 Invoice + Report Card pages (010/011) can run in parallel **after** the `bootstrapWizardPage()` factory (012) exists.
- `.claude/`-touching tasks (B5 amendment, project CLAUDE.md) are main-session sequential.
- Deploy tasks (A0, A3, B4) are gates — verify before proceeding.

## Timeline / Estimated Effort
- Phase A: ~2–3 days (A0 ~0.5 day and independently shippable).
- Phase B: ~2–4 days (B3 refactor is the main variable).
- Deferred adoption (other surfaces): separate, post-project.

## References
- `spec.md`, `design.md` (this folder); `ASSESSMENT.md` (superseded).
- ADR-012, ADR-022, ADR-028, ADR-024.
