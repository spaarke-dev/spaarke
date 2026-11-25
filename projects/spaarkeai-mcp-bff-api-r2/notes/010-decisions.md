# Task 010 - Daily Briefing data seam: inventory and decisions

> Date: 2026-11-25. Package: `src/client/shared/Spaarke.DailyBriefing.Components`. No BFF change, no consumer edit.

## Step 1 - inventory of network and Xrm calls reachable from `DailyBriefingApp`

Paths are relative to the package `src/`; lines are as of the pre-change file.

| Call | Where | Reached from | Seam handling |
|---|---|---|---|
| `authenticatedFetch` POST `/api/ai/daily-briefing/render` | `services/briefingService.ts` `fetchBriefingLive` (:517) | `hooks/useBriefingRender.ts` | `BriefingDataService.renderBriefing` |
| `authenticatedFetch` `/summarize`, `/narrate` | `briefingService.ts` `fetchAiBriefing` (:288), `fetchBriefingNarration` (:420) | not called by the component today | carried on the seam as optional `summarize` / `narrate`; default factory wraps them |
| `authenticatedFetch` POST `/api/ai/daily-briefing/email` | `briefingService.ts` `emailBriefingToColleague` (:579) | no live caller in the component | out of the seam (unused) |
| `authenticatedFetch` `/api/documents/{id}/preview-url` | `briefingService.ts` `getDocumentPreviewUrl` (:616) | `DailyBriefingApp` preview dialog | hidden when injected (preview) |
| `authenticatedFetch` `/api/documents/{id}/open-links` + `openInDesktopApp` | `DailyBriefingApp.tsx` (:940, :949) | preview dialog "Open file" | hidden when injected (preview) |
| `authenticatedFetch` as `SendEmailDialog` transport | `DailyBriefingApp.tsx` (:895) | Email Briefing / Email Item | hidden when injected (email) |
| `authenticatedFetch` BFF child create | `hooks/useInlineTodoCreate.ts` (:55, :167) | inline To Do | hidden when injected (To Do) |
| `getXrm(['webApi','utility'])` + 500 ms poll | `DailyBriefingApp.tsx` (:340, :347) | mount | skipped when injected |
| `xrm.WebApi` `sprk_userpreference` read/write | `services/preferencesService.ts` via `useBriefingPreferences` | mount, Preferences menu | `BriefingPreferencesService` (`load`, optional `save`) |
| `xrm.WebApi` systemuser search | `DailyBriefingApp.tsx` `handleSearchUsers` | email recipient picker | hidden with email |
| `xrm.Utility` user id, client URL | `DailyBriefingApp.tsx` | preferences, deep links | not resolved when injected |
| `Xrm.Navigation.navigateTo` record modal | `DailyBriefingApp.tsx` `handleOpenRecord` (:611); `NarrativeBullet.tsx` `openRecordViaXrm` fallback; `SubRowLink.tsx` (:99, sub-rows are not in the /render path) | record links, TL;DR anchors, "Open record" | `BriefingNavigationHandler.openRecord`; no handler when injected -> record identity dropped from the rows, so the link, TL;DR anchor and menu item do not render |
| `Xrm.Navigation.navigateTo` "Open To Do" | `DailyBriefingApp.tsx` (:534) | To Do success toast | hidden with To Do |
| `Xrm.Navigation.navigateTo` Playbook Library | `utils/openPlaybookLibrary.ts` | host callback `onBrowsePlaybooks`, built by the host, not the component | `navigation.browsePlaybooks` (wins over the `onBrowsePlaybooks` prop) |

## Decisions

1. **Seam = optional `dataService` and `navigation` props**, defaults preserved by *absence*: with no `dataService` the component runs the original code path unchanged (`useBriefingRender` falls back to `fetchBriefingLive`, Xrm is resolved as before). `createDefaultBriefingDataService()` is exported (spec) and wraps today's three calls; the component does not need to call it because the fallback is the same function.
2. **"Injected" means `dataService !== undefined`.** In that mode Xrm is never resolved and no code path reaches `authenticatedFetch`, `getXrm` or `window.Xrm`; the test proves it with all three spied.
3. **Hiding, not stubbing.** `NarrativeBullet` / `ActivityNotesSection` gained optional `onAddToTodo` / `onDismiss` (menu items hide when absent); the row menu is not rendered when it would be empty. Default path always passes `onDismiss`, so it renders as before. Record links are removed by emptying `primaryEntityType` / `primaryEntityId` on the rows passed down (the existing "no primary entity" shape) rather than adding a new prop to four components.
4. **No injection points for To Do create, email share or document preview.** The POML lists these as "hidden when the host did not inject the capability". Nothing in this task needs a host-supplied version: To Do writes belong to task 036 (tool), email send to task 040/011, and the preview is BFF-only. Adding speculative `createTodo` / `sendEmail` slots would be unused surface (root CLAUDE.md section 11). They are hidden whenever a data service is injected; a later task adds the slot with its consumer.
5. **`navigation.openUrl` is declared but not consumed.** The POML names it; no row in the briefing opens a bare URL today (the document preview that would is hidden). Task 034's widget adapter maps it to the MCP Apps open-link request. Kept as a type member only.
6. **Preferences:** `BriefingPreferencesService.save` is optional; without it the Preferences menu is not rendered (a Save that cannot persist would be a broken control). `useBriefingPreferences` and `useBriefingRender` take the injected service as an extra optional argument. `useBriefingRender` now also converts a rejected injected `renderBriefing` into the existing `error` state (the default `fetchBriefingLive` already never rejects).
7. **Spec unresolved question (which briefing call the tool returns) is task 034's.** This seam is call-agnostic: `renderBriefing` returns a `NarrationResult`, whichever tool call the widget adapter maps it to.

## Test environment note (not a repo change)

`npm test` in the package cannot run as committed in this worktree: the worktree path contains `.claude\worktrees`, and jest's `testMatch` globbing mis-handles the dot-directory ("No tests found"); and `tsconfig.test.json` maps `@spaarke/ui-components/types/LookupTypes` to the unbuilt `Spaarke.UI.Components/dist`. Tests were run with a scratch jest/tsconfig outside the repo (explicit `rootDir` + `testRegex`, LookupTypes mapped to `src`). Baseline before the change: 27 suites / 180 tests pass; `worklistSectionRegistration.test.tsx` fails both before and after (`@spaarke/communication-components` is not built here). After: 28 suites / 191 tests pass, same one suite failing for the same reason.

## Consumer type-check

`tsc --noEmit` in `src/solutions/DailyBriefing` and `src/solutions/LegalWorkspace` was not run: neither has `node_modules`, and `@spaarke/ui-components` / `@spaarke/auth` have no built `dist` in this worktree. Every change is additive or loosening (new optional props, previously required callbacks made optional, one extra optional argument on two hooks), the consumers import only `DailyBriefingApp`, `utils` and `widgets`, and the package type-checks clean (`tsc --noEmit` with the package's test tsconfig, errors only in files outside the package). The main session should run the two consumer checks where dependencies are built.

## Consumer type-check (task 010)
`tsc --noEmit` for `src/solutions/DailyBriefing` and `src/solutions/LegalWorkspace` not run locally (no node_modules/dist in the worktrees); CI on the PR covers it. Change is additive/loosening only.
