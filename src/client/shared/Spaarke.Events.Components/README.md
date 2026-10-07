# @spaarke/events-components

Shared React components for Events + Tasks surfaces.

**Source of**: standalone `sprk_eventspage` (`src/solutions/EventsPage/`) and
the SpaarkeAi Calendar workspace widget (task 115).

**Components**: `CalendarSection`, `CalendarDrawer`, `GridSection`,
`AssignedToFilter`, `RecordTypeFilter`, `StatusFilter`, `ColumnFilterHeader`,
`ColumnHeaderMenu`, `ViewSelectorDropdown`.

**Two intentional Calendar variants (moved from root CLAUDE.md §17 on 2026-10-07; corrected 2026-10-03):**
- `CalendarSection` — workspace widget: click-day filter, controlled mode, stateless.
- `CalendarFilterPane` — side-pane filter builder: Calendar + From/To + date-field dropdown + Apply; session-storage (R4 task 055 / B-6 hoist 2026-05-26). Same library, different intents (`notes/b6-pre-change-diff.md`).
- `src/solutions/CalendarSidePane/` is **not currently in use but may return, so keep it working rather than deleting it.** Its `App.tsx` imports `CalendarSection` + `CalendarFilterOutput`, NOT `CalendarFilterPane` (that migration was never finished). Two of its files (`utils/parseParams.ts`, `utils/postMessage.ts`) import the type `CalendarFilterPaneOutput` from the bare `@spaarke/events-components` specifier. The components barrel now exports `CalendarFilterPane` + its types. `IEventDateInfo` is deliberately NOT re-exported from `CalendarFilterPane`, because `CalendarSection` already exports a same-named, different type from the same barrel.

**Context**: `EventsPageContext` + provider + selector hooks (state
management for filters, active event, calendar dates, grid refresh).

**Hooks**: re-exported from `./hooks` (currently empty placeholder — hooks
will accrue as Calendar widget needs grow).

**Services**: `FetchXmlService` — `Xrm.WebApi`-based view + FetchXML
execution; no BFF dependency.

**Constraints**:
- ADR-012 (shared lib reuse): consumed by 2+ surfaces.
- ADR-021 (Fluent v9 tokens only — no hex literals).
- ADR-022 (React 19).
- ADR-028 (auth via `Xrm.WebApi` — no direct `authenticatedFetch` calls).

**Build**: `npm run build` (= `tsc --noEmit`). Vite consumers bundle the
source directly via path alias (mirrors `@spaarke/ui-components` pattern).

**Hygiene** (post-task-112): `tsconfig.json` uses `noEmit: true` and the
package is covered by the repo-wide `.gitignore` rules for `*.js`/`*.d.ts`
under `src/client/shared/*/src/**` so accidental tsc emits never shadow
the `.ts`/`.tsx` source.
