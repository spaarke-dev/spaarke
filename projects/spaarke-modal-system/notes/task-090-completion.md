# Task 090 Completion — P7 OOB Size Scale Constants + Route via Hubs (FR-11/FR-18)

> 🔒 RIGOR LEVEL: **FULL** (POML-declared — new typed shared surface + ~85–100 call-site repoint, high blast radius on how every OOB modal opens). Model tier sonnet @ effort high. Step mode: directional. Solo task, no deps.

## 1. Constants module summary

New file: `src/client/shared/Spaarke.UI.Components/src/utils/adapters/oobModalSizes.ts` — React-free (verified by a dedicated unit test that greps the source for any `react` import), typed, exports:

```ts
export type OobModalSizeName = 'record' | 'createForm' | 'wizard';
export const OOB_MODAL_SIZES: Readonly<Record<OobModalSizeName, OobModalSize>> = Object.freeze({
  record:     { width: {value:85,unit:'%'}, height: {value:85,unit:'%'} },
  createForm: { width: {value:70,unit:'%'}, height: {value:80,unit:'%'} },
  wizard:     { width: {value:60,unit:'%'}, height: {value:70,unit:'%'} },
});
export function getOobModalSize(name: OobModalSizeName): OobModalSize { ... }
```

Exported from `utils/adapters/index.ts` → flows to the main `@spaarke/ui-components` barrel (for Code Pages / shared-lib consumers) and is separately reachable via `@spaarke/ui-components/dist/utils/adapters/oobModalSizes` (deep-dist path, the established PCF precedent from task 070) or a relative-source import (the established convention already used by `VisualHostRoot.tsx`/`DueDateCardList.tsx`/`CalendarWorkspaceWidget.tsx` for other shared-lib pieces).

## 2. Hub consumption evidence

- **`xrmNavigationServiceAdapter.ts`** (`openRecordModal`) — was an inline `{value:85,unit:'%'}` literal; now `OOB_MODAL_SIZES.record.width/height`. `openDialog` never hardcoded a percentage (pass-through only) — no change needed there.
- **`wizardLaunchers.ts`** — `DEFAULT_WIDTH`/`DEFAULT_HEIGHT` (feeding `fireNavigateTo` + `navigateToWebResourceSurfaceAsync`) now source from `OOB_MODAL_SIZES.wizard` instead of inline literals. `navigateToEntityRecordSurfaceAsync` **reassigned** from the shared wizard default (60%×70%) to `OOB_MODAL_SIZES.createForm` (70%×80%) — this launches an OOB entity **CREATE** form, exactly the scenario `createForm` was named for; verified via repo-wide grep that this function has **exactly one caller** (`services/surfaceHandoff/launchSurface.ts`, the Assistant "create-task" hand-off), so the blast radius of this reassignment is narrow and known.

Both hub files pass a scoped jest run (see §5) and the full `Spaarke.UI.Components` build.

## 3. Inventory totals (full detail in `notes/oob-navigateto-inventory.md`)

| Classification | Count |
|---|---|
| Hub definitions | 5 |
| Repointed — `record` (value-fixed 70–80%→85%) | 14 |
| Repointed — `record` (value-neutral dedup) | 6 |
| Repointed — `createForm` (value-fixed/reassigned) | 3 |
| Repointed — `wizard` (value-neutral, incl. 4× `resolveXrmNavigation` dedup) | 19 |
| Repointed — ribbon JS (`wizard`, literal + comment, no bundler) | 4 source sites (~11 ribbon-button handlers) |
| Flagged for visual review (5 named groups) | 10 |
| Intentionally-left (page nav / no options arg) | 8 |
| Assigned-091 (`navigation.ts` ×2 copies) | 4 |
| Assigned-092 (`sprk_DocumentOperations.js` ×2 copies) | 16 |
| **Total real call sites inventoried** | **89** (within the ~85–100 estimate) |

**Flagged list** (leave-as-is, surfaced for the owner — none silently collapsed):
- Playbook Builder webresource dialog, 95%×95% ("near-full-screen experience," `sprk_playbook_commands.js`).
- 3× VisualHost chart drill-through dialogs, 90%×85% (`VisualHostRoot.tsx`).
- 4× "open SpaarkeAi as a modal" pattern, 80%×80%, consistent across `sprk_analysis_commands.js`, `launch-resolver.ts` (×2), `PlaybookLibrary/main.tsx`.
- `NavigationService.ts`'s `openSemanticSearchPage` parameterized default, 80%×80%.
- Daily Digest auto-popup, 60%×80% (`useDailyDigestAutoPopup.ts`).

## 4. Files modified / created

**New:**
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/oobModalSizes.ts`
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/__tests__/oobModalSizes.test.ts`
- `projects/spaarke-modal-system/notes/oob-navigateto-inventory.md`
- `projects/spaarke-modal-system/notes/task-090-completion.md` (this file)

**Hubs:**
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/xrmNavigationServiceAdapter.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts`
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/index.ts` (barrel export)

**Bypass repoints (value or import):**
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`
- `src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/openEmailCompose.ts` (+ test)
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/openEmailRecord.ts` (+ test)
- `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx`
- `src/client/pcf/VisualHost/control/components/DueDateCardList.tsx`
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/NavigationService.ts`
- `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (+ test)
- `src/client/pcf/CommunicationConnections/CommunicationConnections/CommunicationConnectionsApp.tsx` (+ test)
- `src/client/code-pages/SemanticSearch/src/components/EntityRecordDialog.ts` (+ test, + `jest.config.js`)
- `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/{SubRowLink,NarrativeBullet,DailyBriefingApp}.tsx` (+ 2 tests + `test/__mocks__/spaarke-ui-components.tsx`)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/{MeetingScheduleWidget,FindSimilarWizardWidget,EmailComposeWidget,CreateProjectWizardWidget}.tsx`
- `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`
- `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx`
- `src/solutions/LegalWorkspace/src/sections/todo.registration.ts`
- `src/solutions/SmartTodo/src/SmartTodoApp.tsx`
- `src/solutions/SpaarkeAi/src/components/workspace/ManageWorkspacesPane.tsx`
- `src/solutions/EventDetailSidePane/src/services/sidePaneService.ts`
- `src/solutions/FindSimilarCodePage/src/App.tsx`
- `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js` (comment-only)
- `src/client/webresources/js/{sprk_wizard_commands.js,sprk_analysis_commands.js,sprk_regardingrecordnumber_hyperlink.js,sprk_event_sidepane_form.js}`

**Not modified** (hard boundaries, confirmed via no Edit/Write beyond the above): `pcf-safe.ts`, `SprkModal/**`, `TASK-INDEX.md`, `current-task.md`, `.claude/**`, `src/solutions/{LegalWorkspace,SmartTodo}/src/utils/navigation.ts` (091-owned), either `sprk_DocumentOperations.js` copy (092-owned). No `git add`/`commit` performed.

## 5. Verification — builds + tests per affected package

| Package | Build | Tests |
|---|---|---|
| `Spaarke.UI.Components` | `npm run build` (tsc) — **PASS**, 0 errors | Full suite: **11 failed / 189 passed suites, 22 failed / 2495 passed tests (2517 total)** — **exact match** to the documented pre-existing baseline ("11 suites/22 tests — zero NEW failures"). Scoped run (`src/utils/adapters src/components/WorkspaceShell src/components/DataGrid src/components/EmailComposer`): only the 2 pre-existing failures (`buildDynamicWorkspaceConfig`, `SendEmailDialog.characterize`) remain after fixing 2 tests that asserted the old sizes (`openEmailRecord.test.ts`, `openEmailCompose.test.ts`). New `oobModalSizes.test.ts`: **6/6 pass**. `toolbarLaunchDefaults.test.ts`'s `LAYOUT_1_MODAL` sub-test: **pass** (its `NOTEPAD_MODAL`/webresource-name sub-test failures are pre-existing, untouched by this task). |
| `VisualHost` (PCF) | `npm run build:prod` — **PASS** (763 KiB bundle, 3 size warnings only) — after installing the sibling `Spaarke.Visuals` package's node_modules (missing entirely in this fresh worktree; unrelated pre-existing environment gap, not caused by this task — first build attempt failed on that gap alone, zero errors referencing my changes) | No dedicated jest suite for the touched files |
| `SemanticSearchControl` (PCF) | `npm run build:prod` — **PASS** (752 KiB bundle, 17 pre-existing unused-var warnings, 0 errors) | No dedicated jest suite for the touched `NavigationService.ts` |
| `RegardingResolver` (PCF) | `npm run build:prod` — **PASS** (2.14 MiB bundle) | `RegardingResolverApp.test.tsx`: **56/56 pass** (after adding `OOB_MODAL_SIZES` to the file's inline `jest.mock('@spaarke/ui-components', …)` factory — see §6) |
| `CommunicationConnections` (PCF) | `npm run build:prod` — **PASS** (2.18 MiB bundle) | `CommunicationConnectionsApp.test.tsx`: **10/10 pass** (same mock-factory fix) |
| `SpaarkeAi` | `npm run build` (full: html-reset + tsc-surface-gate + vite + ribbon) — **PASS**. `tsc-surface-gate`: "73 pre-existing error(s) in shared libs (deferred to Phase B). Surface-owned: 0." (matches the documented baseline exactly). Vite: 4022 modules, 21.5s. Ribbon: 4/4 scripts. | No dedicated suite for `ManageWorkspacesPane.tsx`'s touched lines |
| `Spaarke.AI.Widgets` | `npm run build` (tsc) — **PASS**, 0 errors | Full suite: **1 failed / 37 passed suites, 1 failed / 677 passed tests (678 total)** — the one failure (`register-workspace-widgets.test.ts`, a widget display-name "Communications"→"Messages" mismatch) is **completely unrelated** to navigateTo/sizing and pre-existing |
| `Spaarke.DailyBriefing.Components` | `npm run build` (tsc --noEmit) — 1 pre-existing, unrelated error (`SendEmailDialog` `title` prop, line 839 — confirmed via `git stash`: identical error present with this task's changes stashed out, just at a shifted line number) | Full suite: **5 failed / 18 passed suites, 1 failed / 192 passed tests (193 total)** after adding `OOB_MODAL_SIZES` to `test/__mocks__/spaarke-ui-components.tsx`. All 5 remaining suite failures are pre-existing (3× a stale mock gap for `RichFilePreviewDialog` predating this task, 1× an unrelated `sectionRegistry.ts` `widthPreference` type error, 1× an unrelated TTL/`onKeep` assertion) |
| `Spaarke.Events.Components` | `npm run build` (tsc --noEmit) — **PASS**, 0 errors | No dedicated test file for `CalendarWorkspaceWidget.tsx` |
| `FindSimilarCodePage` | `npm run build` (vite) — **PASS** | — |
| `EventDetailSidePane` | `npm run build` (vite) — **PASS** | — |
| `SemanticSearch` code-page | `npm run build` (webpack) — **PASS** (deep-import specifier, avoids pulling the full barrel; bundle actually **shrank** 3.52 MiB→1.04 MiB vs. an interim main-barrel-import attempt, confirming the deep-import choice is also production-bundle-friendly) | `EntityRecordDialog.test.ts`: **18/18 pass** (after adding a narrow `jest.config.js` `moduleNameMapper` scoped to the one file — see §6) |
| `SmartTodo` | `npm run build` (vite) — **PASS** | — |
| `LegalWorkspace` (known-broken build, Issue #712) | Scoped `npx tsc --noEmit` — **238 pre-existing errors total** (exact match to task 080's documented count). Filtered for my 3 touched files (`WorkspaceGrid.tsx`, `todo.registration.ts`, `SmartToDo.tsx`): **5 hits, all pre-existing `TS6133` unused-var hygiene** (`WorkspaceHeader`, `isLayoutsLoading`, `layoutError`, `dismissingIds`, `handleDismiss` — none touched by this task) | — |

**ADR-021 diff gate**: `git diff` across all modified files, grepped for hex colors / `'1px'` / `"1px"` literals — **zero matches** (the percentage-number literals in `oobModalSizes.ts` are the intended content, not a violation).

## 6. Notable fixes made along the way (test-infrastructure gaps this task's changes exposed)

1. **`RegardingResolverApp.test.tsx` / `CommunicationConnectionsApp.test.tsx`** — both have an inline `jest.mock('@spaarke/ui-components', () => ({...}))` factory; my new `OOB_MODAL_SIZES` import resolved to `undefined` in test context until added to each factory (mirroring the real module's exact values). Root cause of the observed symptom: `OOB_MODAL_SIZES.record.width` threw inside the click handler's `try {…} catch {}`, silently swallowing the exception so `navigateToMock` was never reached — confirmed and fixed.
2. **`Spaarke.DailyBriefing.Components`** — `test/__mocks__/spaarke-ui-components.tsx` is a hand-maintained manual mock of the whole package; added `OOB_MODAL_SIZES` to it. Discovered along the way that `RichFilePreviewDialog` was ALSO missing from this mock — a pre-existing gap unrelated to this task (that import already existed in `DailyBriefingApp.tsx` before I touched it).
3. **`SemanticSearch` code-page** — this package's `jest.config.js` had `moduleNameMapper` entries for `@spaarke/auth` and `@spaarke/document-operations` (mirroring its webpack alias) but **none for `@spaarke/ui-components`** — a pre-existing gap, never exercised because no file in this package previously imported it. My first attempt (a blanket `^@spaarke/ui-components$` mapping to the full source barrel) surfaced a *different*, deeper pre-existing limitation: the barrel pulls in `useForceSimulation.ts` → `d3-force` (ESM-only), which `ts-jest` cannot transform without extra `transformIgnorePatterns` config. Resolved by using a **deep import** (`@spaarke/ui-components/utils/adapters/oobModalSizes`) with a correspondingly narrow jest mapping — avoids the ESM issue entirely, and (bonus) keeps the production bundle smaller than a barrel import would have.
4. **`node_modules/@spaarke/ui-components` staleness** — several consumer packages (`Spaarke.DailyBriefing.Components`, `RegardingResolver`, `SemanticSearch` code-page) resolve `@spaarke/ui-components` via an npm `file:` dependency that **copies** (not symlinks) the dist at install time. Rebuilding the shared lib's `dist/` did not propagate automatically; required deleting each `node_modules/@spaarke/ui-components` and re-running `npm install` to force a fresh copy.
5. **Operational note**: during a `git stash`/`git stash pop` pair used to verify a pre-existing error (see §5 DailyBriefing.Components), a directory-path miscalculation caused the `pop` to run outside the repo and fail. Caught immediately via `git stash list`, confirmed the correct stash entry, and popped it from the correct root — `git status` confirmed full restoration, zero data loss.

## 7. Step 9.5 gates (FULL rigor, self-run)

**Self code-review:**
- Every `OOB_MODAL_SIZES` consumer either imports the constant (TS-capable surfaces: Code Pages, PCFs via dist-path or relative-source, same-package shared-lib files) or, where no bundler exists (ribbon JS), keeps a literal in sync via an explicit cross-reference comment — no bypass site is left with an unexplained, undocumented percentage.
- Classification discipline applied consistently: `pageType:entityrecord` + existing id → `record`; `pageType:entityrecord` + no id (or a webresource proxying a create flow) → `createForm`; webresource step-driven creation flows → `wizard`; anything not cleanly fitting one of the three → flagged, not silently forced.
- `navigateToEntityRecordSurfaceAsync`'s bucket reassignment (wizard→createForm) is the one true behavior change to a previously-shipped hub function; verified single caller before proceeding, documented prominently (not buried).
- `resolveXrmNavigation` 4-way duplication in `Spaarke.AI.Widgets` eliminated via import from the existing `wizardLaunchers.ts` export — net code reduction, zero behavior change (verified structurally identical before removing).
- All discovered test-infrastructure gaps (§6) were root-caused before being patched, not papered over.

**adr-check:**
- **ADR-012** (single shared module, no duplication): `oobModalSizes.ts` is the one canonical constants module; `DataGrid.tsx`'s `LAYOUT_1_NAV_OPTIONS` and `toolbarLaunchDefaults.ts`'s `LAYOUT_1_MODAL` (2 independent pre-existing duplicates, discovered along the way) now both defer to it. `resolveXrmNavigation`'s 4-way duplication also collapsed.
- **ADR-021** (no hex/`'1px'`/inline color): diff-gated, zero violations (§5).
- **Record-modal-selection invariant** (85%×85%, no per-entity variance): every `pageType:entityrecord`-existing site now uses the identical `OOB_MODAL_SIZES.record` reference — cannot vary per entity by construction.
- **NFR-04** (dual-React / React-free module): `oobModalSizes.ts` imports nothing from `react`, verified by a dedicated unit test; proven safe across React 16 (4 PCF `build:prod` runs), 18/19 (Code Pages, SpaarkeAi).
- **NFR-05** (client-only, no BFF): zero touches to `src/server/api/Sprk.Bff.Api/**`; zero `main.aspx` iframe embedding introduced.

## 8. POML acceptance-criteria checklist

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | A single OOB size-constants module exports `record` (85%×85%), `createForm` (70%×80%), and `wizard` (60%×70%) | **PASS** — `oobModalSizes.ts`, verified by `oobModalSizes.test.ts` (6/6) |
| 2 | `xrmNavigationServiceAdapter` and `wizardLaunchers` consume the named constants; neither hardcodes the percentages inline | **PASS** — both hubs repointed (§2); zero remaining inline percentage literals in either file |
| 3 | Every inventoried `navigateTo` launch routes through one of the two hubs with a named size; no bypass site hardcodes its own percentage | **PASS with two documented, reasoned exceptions**: (a) ribbon JS (no module system — literal + comment is the best achievable, per §5 of the inventory doc); (b) 10 sites explicitly flagged for visual review rather than silently collapsed (per the escalation clause) |
| 4 | Record opens at 85%×85% regardless of entity; OOB main-form editing still uses `navigateTo` | **PASS** — every entity-record-existing site now references the same `OOB_MODAL_SIZES.record` object; `navigateTo` retained everywhere (no site converted to a proprietary dialog) |
| 5 | All affected surfaces build green under `@types/react` 18 and React 19; the constants module imports no React | **PASS** — see §5 build matrix (React 16 ×4 PCFs, 18/19 ×~12 Code Pages/shared-libs); `oobModalSizes.ts` React-free per its own test |

**All 5 acceptance criteria: PASS.**

## 9. Escalations / deviations

No `<escalation>` trigger fired in the "STOP and ask" sense — every genuinely non-conforming size was handled via the POML's own sanctioned escalation path (flag + leave unchanged + report), not by inventing a fourth constant or silently forcing a fit. Deviations from a literal reading, each a deliberate, reasoned choice:

1. **`navigateToEntityRecordSurfaceAsync` bucket reassignment** (wizard 60×70 → createForm 70×80) — not explicitly called out step-by-step in the POML, but directly required by FR-11's `createForm` definition ("an OOB entity CREATE form opened as a modal") applied to the one function whose own docstring already says "entity create form" verbatim. Verified zero unintended blast radius (single caller) before proceeding.
2. **`resolveXrmNavigation` 4-way dedup in `Spaarke.AI.Widgets`** — beyond the literal ask (fix sizes), but squarely inside "route through the hubs" (reusing the hub's own exported helper) and root CLAUDE.md §11's binding reuse principle; zero risk (verified structurally identical), full build+suite green.
3. **`toolbarLaunchDefaults.ts`'s `LAYOUT_1_MODAL` / `DataGrid.tsx`'s `LAYOUT_1_NAV_OPTIONS`** — two additional pre-existing duplicate-source-of-truth constants discovered along the way (neither was in the original file list, both found via careful tracing of `LAYOUT_1_MODAL`/`NOTEPAD_MODAL` consumers after the `.call()`-invocation discovery). Repointed for the same ADR-012 reason as the explicitly-named hubs.
4. **`NOTEPAD_MODAL` (25%×35%) — deliberately NOT touched.** Discovered as part of the same investigation; its own in-file comment documents an **already-reviewed, shipped UX decision** (R1 spec was 70%×80%; live QA on v1.0.6 found it oversized for a "quick scratchpad" task and shrank it). This is not a pending escalation — it is a prior, settled decision outside this task's 3-bucket scale. Left completely untouched; not even added to the flagged-for-review list (that list is for *undecided* cases).
5. **`EntityRecordDialog.ts`'s deep-import path** (rather than the main barrel) — a considered response to a genuine pre-existing test-infra constraint (d3-force/ESM), not a shortcut; documented in §6.
6. **CalendarWorkspaceWidget.tsx / VisualHostRoot.tsx / DueDateCardList.tsx use a relative-source import**, not the `@spaarke/ui-components` package name — matches each file's own pre-existing import convention (already used for other shared-lib pieces in the same file) rather than introducing a new, inconsistent pattern.

## 10. pcf-safe export requests

**None required.** Every PCF touched (`VisualHost`, `SemanticSearchControl`, `RegardingResolver`, `CommunicationConnections`) already had an established, working import route for shared-lib code that does not go through `pcf-safe.ts`:
- `SemanticSearchControl`'s `NavigationService.ts` — deep-dist path (`@spaarke/ui-components/dist/utils/adapters/oobModalSizes`), matching its sibling files' existing `dist/...` deep-import convention.
- `VisualHost`'s `VisualHostRoot.tsx`/`DueDateCardList.tsx` — relative-source import, matching their existing `AiSummaryPopover`/`Spaarke.Visuals` import convention.
- `RegardingResolver`/`CommunicationConnections` — main-barrel import (`@spaarke/ui-components`), matching their existing imports of `PolymorphicPicker`/`TODO_REGARDING_CATALOG`/`cleanGuid` etc. from the same barrel.

`oobModalSizes.ts` is React-free, so it would be a trivially safe `pcf-safe.ts` addition if a future PCF prefers that route — noting this for the record per the task's guidance, but no PCF touched by this task needed it.
