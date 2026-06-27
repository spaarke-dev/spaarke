# Task 076 (B.5) — CalendarSidePane shape migration

> **Status**: ✅ complete
> **Date**: 2026-05-27
> **Branch**: `work/spaarke-ai-platform-unification-r4`
> **Predecessor**: R4 task 055 (B-6) — promoted `CalendarFilterPane` to `@spaarke/events-components` with the new `CalendarFilterPaneOutput` shape; carry-over CO-4 noted in `notes/b11-cast-inventory.md`.

## Problem

`src/solutions/CalendarSidePane/src/App.tsx:61` had a TypeScript error:

```
error TS2345: Argument of type 'CalendarFilterOutput | null' is not assignable to parameter
of type 'CalendarFilterPaneOutput | (() => CalendarFilterPaneOutput | null) | null'.
  ...
  Property 'dateFields' is missing in type 'CalendarFilterSingle' but required
  in type 'CalendarFilterPaneSingle'.
```

`App.tsx` already imported and used the new `CalendarFilterPaneOutput` type (from R4 task 055 / B-6), but `getInitialFilterState` in `utils/parseParams.ts` still returned the legacy in-file `CalendarFilterOutput` shape. The new shape requires `dateFields: string[]` on `single` and `range` variants.

## Fix

Migrated `src/solutions/CalendarSidePane/src/utils/parseParams.ts` and `src/solutions/CalendarSidePane/src/utils/postMessage.ts` to use the new shape exported from `@spaarke/events-components`.

### Changes

**`src/utils/parseParams.ts`**
- Removed the legacy in-file types: `CalendarFilterType`, `CalendarFilterSingle`, `CalendarFilterRange`, `CalendarFilterClear`, `CalendarFilterOutput` (per task constraint: don't keep legacy as parallel option).
- Added `import type { CalendarFilterPaneOutput } from "@spaarke/events-components"`.
- Updated `getInitialFilterState(params)` return type from `CalendarFilterOutput | null` → `CalendarFilterPaneOutput | null` and added `dateFields: []` to the `range` and `single` return branches (the `clear` variant in the new shape has no `dateFields` property, consistent with the old type — and `getInitialFilterState` never returned `clear`, so that branch is unaffected).
- Updated `buildCalendarUrl(filter: CalendarFilterOutput | null, …)` → `buildCalendarUrl(filter: CalendarFilterPaneOutput | null, …)`. Body unchanged — only the `type` and `date`/`start`/`end` properties are read; `dateFields` is intentionally NOT serialized into the URL because URLs only encode the date selection, not the field constraint (the field constraint is selected via the side pane Dropdown and persisted via sessionStorage by `CalendarFilterPane` itself).

**`src/utils/postMessage.ts`**
- Changed the import from `import { CalendarFilterOutput } from "./parseParams"` → `import type { CalendarFilterPaneOutput } from "@spaarke/events-components"`.
- Renamed all five references to the type (in `CalendarFilterChangedMessage.payload.filter`, `CalendarReadyMessage.payload.currentFilter`, `sendFilterChanged(filter)`, `sendCalendarReady(currentFilter)`) from `CalendarFilterOutput` → `CalendarFilterPaneOutput`.

**`src/App.tsx`** — no change. It was already using `CalendarFilterPaneOutput`.

## Behavior change analysis

**Bottom line**: no observable behavior change.

The only semantic difference is the postMessage payload now carries `dateFields: []` on the initial `CALENDAR_READY` message (when URL params seeded a filter). Three reasons this is safe:

1. The parent record-form JS that consumes `CALENDAR_READY` previously received no `dateFields` field at all (the legacy `CalendarFilterSingle`/`Range` types didn't carry it). An empty array `[]` is the structurally faithful "field is now present but signals no constraint" choice.
2. The initial-state semantics in `CalendarFilterPane` itself (Apply not yet pressed) is "no date-field filter applied yet"; an empty array on the initial postMessage matches that. The Apply action in the component will subsequently send a populated `dateFields` array (defaulting to all individual field values when "All Dates" is selected — see the `getActualDateFields` helper in `CalendarFilterPane.tsx`).
3. URLs (and thus URL-driven initial state) cannot encode the date-field selection — there is no `dateFields` URL parameter, by design. The field constraint is owned by the side pane Dropdown plus sessionStorage. So there is no URL contract change.

If a future task discovers the parent expects a populated default on the initial `CALENDAR_READY`, the fix is a one-line change inside `getInitialFilterState` (e.g., import `ALL_DATE_FIELD_VALUES` from the shared lib and use it). Not done here because (a) no evidence the parent needs it, (b) the in-component default is `[ALL_DATES_VALUE]` (the sentinel, not the expanded array — only Apply expands it), and (c) the task constraint says "no behavior change unless inevitable due to the shape change".

## Verification

| Check | Result |
|---|---|
| `npx tsc --noEmit` own-src errors | 0 (was 1) |
| `npx tsc --noEmit` overall | 8 pre-existing errors in `@spaarke/events-components` package (out of scope — CO-2 / pre-existing per `notes/b11-cast-inventory.md`) |
| `npm run build` | ✅ 0 errors, dist/index.html generated (3290 modules transformed in 12.23s) |

## Files modified

- `src/solutions/CalendarSidePane/src/utils/parseParams.ts` (legacy types removed, new type imported, two functions migrated)
- `src/solutions/CalendarSidePane/src/utils/postMessage.ts` (import + 5 type references updated)

## Guardrails honored

- ✅ No touch to `TASK-INDEX.md`, `CLAUDE.md`, `current-task.md`, `.claude/`
- ✅ No commit / no push
- ✅ Legacy `CalendarFilterOutput` shape fully removed — not retained as parallel option
- ✅ No behavior change beyond the inevitable `dateFields` field-shape addition (analyzed above)
- ✅ ADR-012: uses the type exactly as exported from `@spaarke/events-components`

## Related

- R4 task 055 (B-6): promoted `CalendarFilterPane` + `CalendarFilterPaneOutput` to shared lib
- `notes/b11-cast-inventory.md` CO-4: cataloged this loose end
- `notes/b6-pre-change-diff.md`: documents `dateFields: string[]` REQUIRED contract on the new shape
