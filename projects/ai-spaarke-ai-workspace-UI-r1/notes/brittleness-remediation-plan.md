# Brittleness Remediation Plan — Cross-Surface

> **Source**: full-picture audit run 2026-06-09; results in this same notes folder via session conversation.
> **Scope**: SYSTEM-LEVEL fixes across all 50+ Spaarke client surfaces, not just SpaarkeAi.
> **Driver**: SpaarkeAi blanked page on iteration 2 because `tabs.some(...)` referenced a prop that was never destructured. TypeScript was happy (prop existed on the interface); vite's prod build doesn't run `tsc --noEmit`; no error boundary; no CI smoke test. Same gaps exist across most client surfaces.

## Findings the plan addresses

| # | Finding | Severity |
|---|---|---|
| 1 | **Vite/Webpack Code Pages don't run `tsc --noEmit` before bundling.** TypeScript errors ship to prod. | 🔴 Critical |
| 2 | **No app-level error boundary** in SpaarkeAi, LegalWorkspace, EventsPage, 19 Wizards. Any render error blanks the entire page. | 🔴 Critical |
| 3 | **No CI smoke test.** No PR check actually loads the built bundle in a browser to verify React mounts. | 🔴 Critical |
| 4 | **Source-aliasing pattern** (`resolveSharedLibDeps` vite plugin) lets shared-lib imports resolve to consumer's `node_modules` even when deps aren't in the shared lib's `package.json`. Hidden runtime failures. | 🟠 High |
| 5 | **10+ deploy scripts duplicate** 18–20 lines of token+update+publish boilerplate. No helper module. No staging support. | 🟠 High |
| 6 | **Registration patterns are eager + unguarded.** `LegalWorkspace/sectionRegistry.ts` has dev-mode duplicate-id checks but no render-time try/catch — factory errors propagate uncaught. Same shape in SpaarkeAi widget registry. | 🟡 Medium |
| 7 | **CI partially covers** typecheck for shared libs (Spaarke.UI.Components, Spaarke.AI.Widgets, etc.) but not for Code Pages. Office Add-ins HAVE `typecheck` script but CI doesn't call it. | 🟡 Medium |

## Existing patterns we can build on

| Already exists | Where | Use |
|---|---|---|
| Error boundary | `src/client/external-spa/.../ErrorBoundary.tsx`, `src/client/office-addins/.../ErrorBoundary.tsx`, `src/client/pcf/VisualHost/.../ErrorBoundary.tsx`, RichTextEditor | Consolidate into one shared `<AppErrorBoundary>` in `@spaarke/ui-components` |
| tsc gate for shared libs | `.github/workflows/sdap-ci.yml` lines 185–228 | Extend to Code Pages |
| Office Add-ins `typecheck` script | `src/office-addins/*/package.json` | Standardize across Code Pages, wire into CI |
| Source-aliasing plugin | 4 Vite Code Pages | Keep, but require consuming surfaces to declare shared-lib transitive deps |

## Standardization opportunities

| Opportunity | Library home | Replaces |
|---|---|---|
| `<AppErrorBoundary>` reusable class | `@spaarke/ui-components/components/AppErrorBoundary` | 4 existing local copies + the missing boundaries on 22+ Code Pages |
| `safeRegister(...)` + `createRegistry(...)` helpers | `@spaarke/ui-components/patterns/safeRegister` | Hand-rolled registry pattern in LegalWorkspace + ad-hoc registrations in SpaarkeAi widgets |
| `tsconfig.base.json` at repo root | `/tsconfig.base.json` | 20+ surfaces each maintaining their own near-identical tsconfig |
| `Deploy-Helper.ps1` PowerShell module | `scripts/Deploy-Helper.ps1` | 18–20 lines duplicated in 10+ `Deploy-*.ps1` scripts |
| Standard CI smoke-test step | `.github/workflows/sdap-ci.yml` new `code-pages-smoke` job | (nothing — gap today) |

## Remediation phases — in-session vs follow-up

> **In-session = land in PR #372 today**. Follow-up = documented + tracked as a separate Phase B project in `phase-b-followups.md`.

### Phase A — in-session, foundation

| Step | Files | Effort | Outcome |
|---|---|---|---|
| **A1** Create `tsconfig.base.json` at repo root with strict defaults (`strict: true`, `noUnusedLocals`, `noUnusedParameters`, `noEmit`, `skipLibCheck`, `moduleResolution: bundler`, `jsx: react-jsx`). | `tsconfig.base.json` (new) | 15 min | Single source of TypeScript truth. |
| **A2** Have the 3 Code Pages we touched (SpaarkeAi, WorkspaceLayoutWizard, DailyBriefing) extend the base — adds `"extends": "../../../tsconfig.base.json"`. Don't migrate the other 23 in this PR; document them as follow-up. | 3× `tsconfig.json` | 10 min | Consistent strict settings for the surfaces I'm changing. |
| **A3** Fix pre-existing tsc failures in SpaarkeAi (missing `@spaarke/ai-outputs` and `@spaarke/ai-context` paths; one test type error; 2 `noUnusedLocals` warnings). Probe showed these block `tsc --noEmit` today. | `tsconfig.json` path additions + 3 file fixes | 30 min | Clean tsc baseline. |
| **A4** Add `typecheck` + `tsc --noEmit` gate to all 3 Code Pages' `build` script (`tsc --noEmit && vite build && ...`). Confirm clean local build. | 3× `package.json` | 15 min | Local build catches type errors before vite. Today's `tabs` bug would have failed here. |
| **A5** Persist `pdfjs-dist`, `mammoth`, `@microsoft/applicationinsights-web` into SpaarkeAi `package.json` (and UI.Components's `package.json` for applicationinsights-web — pinpoint where each is actually needed). | 2× `package.json` | 10 min | Fresh clones can build SpaarkeAi without my hand-installed deps. |

### Phase B — in-session, shared safety primitives

| Step | Files | Effort | Outcome |
|---|---|---|---|
| **B1** Create `@spaarke/ui-components/src/components/AppErrorBoundary/AppErrorBoundary.tsx` — class component + `withAppErrorBoundary` HOC + telemetry hook. Read the 4 existing local copies and consolidate the best parts. | new file + barrel export | 45 min | Reusable across all client surfaces. |
| **B2** Wrap SpaarkeAi's `<App>` in `<AppErrorBoundary>` at `src/solutions/SpaarkeAi/src/main.tsx`. Fallback UI = Fluent v9 `MessageBar` + Reload button. | `main.tsx` | 15 min | Single-pane Code Page can't blank entirely. |
| **B3** Wrap LegalWorkspace's `<LegalWorkspaceApp>` and WorkspaceLayoutWizard's `<App>` and DailyBriefing's `<App>` in the same `<AppErrorBoundary>`. | 3× entry points | 20 min | All 3 Code Pages I touched get root protection. |
| **B4** Create `@spaarke/ui-components/src/patterns/safeRegister.ts` — `createRegistry(...)` helper that wraps every registration call in try/catch + telemetry; renders fallback card on factory failure. | new file + barrel export | 60 min | Foundation. |
| **B5** Migrate `register-workspace-widgets.ts` to wrap each `registerWorkspaceWidget(...)` call in try/catch (minimum-viable interim — full `safeRegister` consumption migration in Phase D below). | `register-workspace-widgets.ts` | 20 min | Today's class of bug (one bad widget kills the registry) is contained. |
| **B6** Same try/catch wrapping for `sectionRegistry.ts`. | `sectionRegistry.ts` | 15 min | LegalWorkspace section failures stay contained. |

### Phase C — in-session, CI safety net

| Step | Files | Effort | Outcome |
|---|---|---|---|
| **C1** Add playwright as a CI dev-dep at the repo root. Write a playwright config that headlessly loads a built Code Page HTML, waits for a `data-testid` that confirms React mounted, screenshots on failure. | `playwright.config.ts` + new test file | 90 min | Smoke-test infrastructure ready. |
| **C2** Add a new GitHub Actions job `code-pages-smoke` to `.github/workflows/sdap-ci.yml`. Job: build SpaarkeAi/Wizard/DailyBriefing, serve each via `npx serve`, run playwright smoke test. Fails the PR if any bundle doesn't mount. | workflow file | 30 min | Today's bug would have failed CI before merge. |
| **C3** Add a CI tsc gate that runs `npm run typecheck` for the 3 Code Pages. Today CI only runs shared-lib tsc; this extends to Code Pages. | workflow file | 20 min | Type errors block PRs. |

### Phase D — in-session, registration pattern migration (light)

| Step | Files | Effort | Outcome |
|---|---|---|---|
| **D1** Wire `createRegistry(...)` into the WorkspaceLayoutWidget render path so `sectionRegistry` consumers render via `registry.renderSafely(id, ctx)` instead of direct factory call. Section render errors → fallback card, not blank pane. | `WorkspaceGrid.tsx` or whichever consumes sections | 30 min | Render errors per-section. |
| **D2** Add per-widget Error Boundary at `WorkspaceTabManagerComponent.tsx` — each tab's widget renders inside its own `<AppErrorBoundary fallback={<WidgetError />}>`. | tab component | 30 min | One bad widget blanks its own tab, not the whole workspace pane. |

### Phase E — documented Phase B follow-ups (NOT in this PR)

| # | What | Why deferred |
|---|---|---|
| E1 | Migrate the **remaining 23 Code Pages** (LegalWorkspace standalone, EventsPage, all 19 Wizards, AnalysisWorkspace, etc.) to `tsconfig.base.json` + `tsc --noEmit` gate + `<AppErrorBoundary>`. | Mechanical but touches every Code Page; safer as a focused project than a side-quest in this PR. |
| E2 | **Deploy helper PowerShell module** — consolidate `Get-AccessToken`/`Update-WebResource`/`Publish-Customizations` across 10+ `Deploy-*.ps1` scripts. Add `-Staging` switch support. | Significant rewrite of deploy infra; deserves dedicated testing. |
| E3 | **Staging Code Page** (`sprk_spaarkeai_staging` web resource, separate CI workflow that auto-deploys feature branches there for visual review). | Requires deploy helper from E2 + CI workflow design + Dataverse resource provisioning. |
| E4 | **Full migration to `safeRegister`** for every registration in the codebase (not just WorkspacePaneMenu and sectionRegistry). | Phase B/D in this plan is the foundation; full migration is mechanical follow-up. |
| E5 | **Source-aliasing trade-off review** — decide whether to keep `resolveSharedLibDeps` plugin or move to compiled-dist consumption. | Architectural decision with build-time + dev-iteration trade-offs; not a quick fix. |

## Scaling notes

Per the user's "consider scaling": each Phase A–D primitive is designed to hold at 10× the current widget count:

- `AppErrorBoundary` is O(1) regardless of tree size — wraps once at the root.
- `safeRegister` is per-registration; cost grows linearly with widget count but each registration is independent.
- `tsconfig.base.json` extends once per surface; new surface = 1 file change.
- CI smoke test grows with Code Page count, but each smoke test is independent and CI parallelizes — no quadratic blow-up.
- Deploy helper grows linearly with deploy targets; no fan-out.

What does NOT scale and is documented in Phase E:
- The eager `register-workspace-widgets.ts` and `sectionRegistry.ts` modules will become massive as widget count grows. A true plugin architecture (lazy resolution from a manifest, not at-import-time registration) is the right answer for 100+ widgets. Phase D in this PR adds try/catch safety; E4 is the proper plugin refactor.
- 23+ duplicate `tsconfig.json` files; E1 migration eliminates this duplication when convenient.

## R6 coordination — how this plan interoperates with `spaarke-ai-platform-unification-r6`

R6 is **explicitly out of scope for frontend redesign** per its spec ("three-pane shell, SprkChat component, workspace tab strip all stay"). However R6 **extends** the same surfaces this plan touches. Each interaction below is additive — no replacement of existing patterns.

| R6 Pillar | What R6 adds | This plan's shared primitive | Coordination |
|---|---|---|---|
| **Pillar 6** Tri-directional shell + execution-trace widget | New widget types in `WorkspaceWidgetRegistry`; additive PaneEventBus event types | `createRegistry(...)` helper (Phase B.4) | R6 widget registrations consume `safeRegister`/`createRegistry` for free — get error containment + telemetry without R6 changes. New widget types just add registrations; nothing to migrate. |
| **Pillar 9** Workspace Widget Visibility Contract — `getAgentVisibleState()` per widget | New optional field on each `WorkspaceWidgetRegistration` | Same `createRegistry` helper | The registration shape is open via `T extends Registration`. R6 extends the registration type with `getAgentVisibleState?: (props) => CompactState`; my helper accepts the wider shape without modification. **Will add a generic type parameter to `createRegistry<T extends BaseRegistration>` so R6 can pass its richer type without losing safety.** |
| **Pillar 2** Tool Registry Convergence — `IToolHandler` registry on the BFF side | Backend-side; no frontend interaction | Backend-only — out of scope for this plan | None — different surface. |
| **Pillar 3** Generic `invoke_playbook` chat tool | New widget dispatches via the playbook engine | New widgets render via the registry, get error containment automatically | None additional. |
| **Pillar 6** Pinned Memory / Cross-conversation memory | Cosmos-backed; widget surface displays it | New display widget benefits from `<WidgetErrorBoundary>` (Phase D.2) | None additional. |

**Concrete promises this plan makes to R6**:

1. **`createRegistry<T>` is type-generic** — R6's richer `WorkspaceWidgetRegistration` shape (with `getAgentVisibleState`) can be passed as the type parameter without re-implementing the helper.
2. **`<AppErrorBoundary>` is consumer-agnostic** — works whether the wrapped tree is a SprkChat conversation, an execution-trace widget, a context-pane wizard, or a workspace tab. No assumptions about content.
3. **No R6 file is touched in this plan**. Phase A–D changes live in: `tsconfig.base.json`, `@spaarke/ui-components/src/components/AppErrorBoundary/`, `@spaarke/ui-components/src/patterns/safeRegister/`, `register-workspace-widgets.ts`, `sectionRegistry.ts`, 3 Code Page entry points + 3 `package.json` files + `.github/workflows/sdap-ci.yml`. None of these are in R6's BFF/AI handler scope.
4. **PaneEventBus contract is unchanged** by this plan. R6 adds event types additively; my error boundaries don't interfere with the event flow.

**Where R6 should consume the primitives** (after R6 starts):

- R6 Pillar 6's new chat-tool widget mutations: dispatch to `createRegistry` for resolution; benefit from error containment.
- R6 Pillar 9's `getAgentVisibleState`: declare on the registration shape; `createRegistry<R6WorkspaceWidgetRegistration>` flows it through.
- R6 Pillar 6's execution-trace widget: implement as a workspace widget that consumes the existing event bus; wrapped in `<AppErrorBoundary>` per default — no R6 work needed for safety.

## Phase A.3 update — AI.Context / AI.Outputs tsconfig path alignment (investigated)

Investigation concluded: the tsconfig paths pointing to `dist/index.d.ts` are stale; vite already aliases these libs at `/src` (matching the other 5 shared libs). Resolution: **change tsconfig paths to `/src/index.ts` like the other libs** — consistency with the rest of the codebase, removes ambiguity, fixes tsc. No build artifact is required for these libs.

## Out-of-scope explicitly

- Lazy widget registration architecture (E4) — needs design.
- Staging Code Page CI auto-deploy (E3) — needs design.
- Source-aliasing replacement (E5) — needs design + benchmarking.
- Migrating PCF controls' error handling (would extend Phase B beyond Code Pages, deserves a focused PCF project).

## Plan estimated effort

| Phase | Items | Estimate |
|---|---|---|
| A — foundation | 5 items | ~1.5h |
| B — shared safety primitives | 6 items | ~3h |
| C — CI safety net | 3 items | ~2.5h |
| D — registration pattern migration (light) | 2 items | ~1h |
| **TOTAL in-session** | 16 items | **~8h** |
| E — Phase B follow-ups | 5 documented items | 1–4 weeks elsewhere |

8 hours is substantial. Will commit each phase separately so anything that doesn't land in session is still net-positive on the PR.
