# Task 042 — Recent (Edited) toggle: decisions & deviations

> **Task**: 042 — Viewed/Edited segmented control via `modifiedby=me` per-entity queries
> **Date**: 2026-08-13

## 1. Core entity set — validated live against spaarkedev1, NONE dropped

Per the task instructions, the proposed core set was validated against the LIVE
spaarkedev1 schema (`EntityDefinitions`, via `az account get-access-token` + Web API
`GET`, mirroring `spike/Deploy-SidePaneSpike.ps1`'s auth pattern) before writing
`editedByMeService.ts`:

| Entity | Result | `LogicalCollectionName` | `PrimaryNameAttribute` |
|---|---|---|---|
| `sprk_matter` | ✅ present | `sprk_matters` | `sprk_matternumber` |
| `sprk_project` | ✅ present | `sprk_projects` | `sprk_projectnumber` |
| `sprk_document` | ✅ present | `sprk_documents` | `sprk_documentname` |
| `sprk_todo` | ✅ present | `sprk_todos` | `sprk_name` |
| `sprk_event` | ✅ present | `sprk_events` | `sprk_eventname` |
| `sprk_communication` | ✅ present | `sprk_communications` | `sprk_name` |

All six logical names from spec.md's "Assumptions" (OQ-5) exist as-proposed. **No
entity was dropped.** `CORE_ENTITY_SET` in `editedByMeService.ts` is the full six-entity
set.

Note the `PrimaryNameAttribute` for `sprk_matter`/`sprk_project` is a **number** field
(`sprk_matternumber`/`sprk_projectnumber`), not a human title field like
`useSprkMemoRepository.ts`'s `PARENT_PRIMARY_NAME_FIELD` hardcoded map uses
(`sprk_mattername`/`sprk_projectname`). Rather than reconcile/hardcode a second map,
`editedByMeService.ts` resolves the display-name field generically via
`Xrm.Utility.getEntityMetadata(entity).PrimaryNameAttribute` — the SAME pattern
`navigatorCaptureService.ts`'s `resolveDisplayName` already established for exactly
this "I only know the logical name at runtime, and the six-entity set doesn't share one
label convention" problem (see that file's own doc comment contrasting itself against
`useSprkMemoRepository`'s hardcoded map). This is reuse of an established pattern, not
a new one.

## 2. Escalation trigger — evaluated, did NOT fire

Task's `<escalation>`: *"If the `modifiedby=me` OData current-user literal (`@me`
binding) does not resolve reliably on the current UCI build (plan.md R2), STOP and
escalate rather than hardcoding a user id or falling back to the audit entity."*

**R2 was already resolved by the task-001 spike** (`notes/task-001-spike-report.md`):
Dataverse OData has no literal `@me`; `_modifiedby_value eq {userId}` with `{userId}`
interpolated from `Xrm.Utility.getGlobalContext().userSettings.userId` was validated
live against spaarkedev1 (`account` entity probe, 3 rows returned). This task's
`editedByMeService.ts` follows that exact resolved pattern — no `@me` literal used, no
hardcoded user id, no audit entity anywhere in the module. **Trigger did not fire; no
new blocker was found.**

## 3. Segmented toggle — local implementation, not an extension of `ViewToggle`

`src/client/shared/Spaarke.UI.Components/src/components/ViewToggle/` is an existing
Fluent v9 segmented-control component, but it is **icon-only** with a hardcoded
`'list' | 'card'` domain (no text-label support). The Recent tab's Viewed/Edited toggle
needs visible TEXT labels, not icons.

**Decision (CLAUDE.md §11 three-question template)**:
1. **Existing**: `ViewToggle` — 2-segment icon-only toggle group, `'list'|'card'`-specific.
2. **Extension**: generalizing `ViewToggle`'s public API to accept arbitrary
   text-labeled segments would change a shared component's contract for every existing
   consumer, for a domain (`'viewed'|'edited'`) that has exactly ONE consumer today.
   That is a higher blast-radius change than the value it returns right now.
3. **Cost of doing nothing** (not extending `ViewToggle`): none — `RecentTab.tsx` adds a
   small local two-`Button` group, styled with the SAME Griffel border/radius/
   selected-state pattern as `ViewToggle.styles.ts` (tokens, not hardcoded colors), so it
   is visually consistent without touching the shared component's contract.

This is a documented reuse-pattern decision, not an ADR conflict (no ADR mandates using
`ViewToggle` specifically) — recorded here per CLAUDE.md §11's "answer three questions"
requirement, and inline in `RecentTab.tsx`'s module docblock ("Segmented toggle" note).

## 4. Edited list — lazy-loaded, no pin star

- **Lazy-load on first toggle**: the Edited query (6 WebApi round-trips) only fires the
  first time the user switches to Edited (`editedStatus === 'idle'` gate in
  `handleModeChange`), not on mount. A user who never leaves Viewed never pays the cost.
- **No pin star on Edited rows**: pinning is out of this task's acceptance criteria.
  Edited rows render a name + type chip and are click-to-navigate only. The Viewed tab
  already offers pinning once a record has been captured there. Documented in
  `RecentTab.tsx`'s module docblock.

## 5. Shared-lib edits — NONE this task

Unlike task 041 (which added three exports to
`src/client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts`),
task 042 touches **zero** shared-lib files. `editedByMeService.ts` is NavigatorPane-local
(`src/solutions/NavigatorPane/src/services/`) and only consumes the already-exported
top-level `@spaarke/ui-components` barrel (`getXrm`, `XrmContext`, `XrmUtility` — all
already re-exported via `export * from './utils'` in the shared-lib's `index.ts`). No new
tsconfig/vite alias entries were needed (no deep-subpath import, unlike 041's
`navItemRepository` import) — the top-level barrel resolution NavigatorBody.tsx already
uses was sufficient.

## 6. `npm run lint` — pre-existing gap, not introduced by this task

`npm run lint` (`eslint src --ext .ts,.tsx`) fails with "'eslint' is not recognized" —
`eslint` is not listed in NavigatorPane's `package.json` `devDependencies` and is not
resolvable via `npx` in this workspace. This gap predates task 042 (present since task
040/041; `RecentTab.tsx`'s task-041 deviation notes did not report running lint either).
Not fixed here — out of this task's scope (would require adding an eslint config +
dependency across the whole NavigatorPane solution). `tsc --noEmit` (strict mode,
`noUnusedLocals`/`noUnusedParameters`) ran clean and is the type-safety gate that is
actually wired up for this package.

## Verification run (task 042)

- `cd src/client/shared/Spaarke.UI.Components && npm run build` (tsc) — clean.
- `cd src/solutions/NavigatorPane && npx jest` — **3 suites / 27 tests, all green**
  (`NavigatorBody.test.tsx`, `RecentTab.test.tsx` incl. new task-042 toggle describe
  block, `editedByMeService.test.ts`).
- `cd src/solutions/NavigatorPane && npx tsc --noEmit` — clean.
- `cd src/solutions/NavigatorPane && rm -rf dist node_modules/.vite .vite && npm run build`
  (Vite production, cache-cleared) — succeeds; `dist/index.html` contains
  `recent-tab-mode-edited` / `recent-tab-edited` / `_modifiedby_value` strings, confirming
  the new code is actually bundled (not just present in source).
