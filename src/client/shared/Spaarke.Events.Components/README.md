# @spaarke/events-components

Shared React components for Events + Tasks surfaces.

**Source of**: standalone `sprk_eventspage` (`src/solutions/EventsPage/`) and
the SpaarkeAi Calendar workspace widget (task 115).

**Components**: `CalendarSection`, `CalendarDrawer`, `GridSection`,
`AssignedToFilter`, `RecordTypeFilter`, `StatusFilter`, `ColumnFilterHeader`,
`ColumnHeaderMenu`, `ViewSelectorDropdown`.

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
