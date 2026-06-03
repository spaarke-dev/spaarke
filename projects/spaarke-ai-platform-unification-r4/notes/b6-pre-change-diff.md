# B-6 pre-change diff — CalendarSection: local copy vs shared lib

**Task**: 055 (B-6) — Reconcile CalendarSidePane.CalendarSection
**Date**: 2026-05-26
**Author**: Claude (task-execute, FULL rigor)
**Status**: 🚨 **BLOCKED — operator decision required** (see Recommendation section)

---

## The two files

| | Local copy | Shared lib (canonical post-R10/R13) |
|---|---|---|
| Path | `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx` | `src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarSection.tsx` |
| LOC | ~925 | ~845 |
| VERSION constant | `2.3.0` ("All Dates default, clear on page navigation") | `1.1.0` |
| File size | 30 KB | 33 KB |

The original assumption in backlog.md + the POML — that the local copy was the "pre-hoist version" and the shared lib was the "post-hoist version" of the same component — is **incorrect**. The two files implement substantively different components built for different purposes, with non-overlapping feature sets in places. Both have evolved independently since the R3 task 114 hoist.

---

## Purpose divergence

| | Local copy | Shared lib |
|---|---|---|
| Primary host | Dataverse side pane (`Xrm.App.sidePanes`) on record forms | Workspace widget (Pattern D — task 115) + EventsPage standalone (CalendarDrawer) |
| User mental model | "Filter builder" — pick date fields, set From/To, click Apply | "Calendar view" — click a day to filter the grid; Shift+click for a range |
| Output channel | `postMessage` to parent record form | `onFilterChange` callback to parent widget/page |
| State persistence | `sessionStorage["sprk_calendar_filter_state"]` with auto-restore on mount | None (parent owns state) |

---

## Prop signature divergence

### Local copy `CalendarSectionProps`
```ts
{
  eventDates?: IEventDateInfo[];
  onFilterChange: (filter: CalendarFilterOutput | null) => void;
  initialSelectedDate?: string;
  initialRangeStart?: string;
  initialRangeEnd?: string;
}
```

### Shared lib `CalendarSectionProps`
```ts
{
  eventDates?: IEventDateInfo[];
  onFilterChange: (filter: CalendarFilterOutput | null) => void;
  initialDate?: string;     // ← different name
  height?: number;
  viewDate?: Date;          // controlled mode (task 116)
  monthsToShow?: number;    // responsive (task 116)
  layout?: "vertical" | "horizontal";  // task 116
  selectedDate?: Date | null;          // controlled selection (task 118)
  onSelectDate?: (date: Date | null) => void; // task 118
}
```

**Overlap**: `eventDates`, `onFilterChange` only.
**Local-only**: `initialSelectedDate`, `initialRangeStart`, `initialRangeEnd` (different name from shared `initialDate`).
**Shared-only**: `height`, `viewDate`, `monthsToShow`, `layout`, `selectedDate`, `onSelectDate`.

---

## Filter-output type divergence

### Local copy
```ts
interface CalendarFilterSingle { type: "single"; date: string; dateFields: string[]; }   // dateFields REQUIRED
interface CalendarFilterRange  { type: "range";  start: string; end: string; dateFields: string[]; }  // REQUIRED
interface CalendarFilterClear  { type: "clear"; }
```

### Shared lib
```ts
interface CalendarFilterSingle { type: "single"; date: string; dateFields?: string[]; }  // dateFields OPTIONAL
interface CalendarFilterRange  { type: "range";  start: string; end: string; dateFields?: string[]; }  // OPTIONAL
interface CalendarFilterClear  { type: "clear"; }
```

The local copy ALWAYS emits `dateFields`; the shared lib NEVER emits `dateFields` (it has no UI for the date-field selector). Existing CalendarSidePane consumers (record-form parent JS that reads `postMessage`) likely depend on `dateFields` being present.

---

## Feature divergence (load-bearing — these are the blockers)

| Feature | Local copy | Shared lib |
|---|---|---|
| **From/To text input fields** (manual date entry + click-to-fill) | ✅ Yes | ❌ No |
| **Date-field multi-select Dropdown** ("Filter by Date Field" — All Dates / Due Date / Created On / 13 others) | ✅ Yes | ❌ No |
| **"Apply" button** (filter is NOT live; user applies explicitly) | ✅ Yes | ❌ No (filter emits on every click) |
| **`sessionStorage` filter-state persistence** (auto-restore on mount with 200ms delay → re-emit filter to parent) | ✅ Yes | ❌ No |
| **Month navigation arrows** (Prev/Next) | ✅ Yes (internal) | ✅ Yes (CONTROLLED mode only — parent provides `viewDate`) |
| **Click-to-fill From/To inputs** (first click → From; second click → To) | ✅ Yes | ❌ No |
| **3-month vertical stack** | ✅ Hardcoded 3-month current+2 | ✅ Default 3, parent configurable via `monthsToShow` |
| **Horizontal layout** (months side-by-side, no internal scroll) | ❌ No | ✅ Yes (`layout="horizontal"`) |
| **Controlled selectedDate mode** (parent owns single-day selection, task 118) | ❌ No | ✅ Yes (`selectedDate` + `onSelectDate`) |
| **Shift+click range selection** (task 118) | ❌ No (range is built via From/To inputs) | ✅ Yes |
| **Event-day brand-tint highlight** (task 122 — blue bg + white text on days with events) | ❌ No | ✅ Yes (`dayWithEvents` style) |
| **In-range brand-tint visualization** | ✅ Yes (`dayCellInRange`) | ✅ Yes (`dayCellInRange`) |
| **Selected-day grey/neutral background** (task 129 R13 follow-up) | ❌ No (uses brand-background) | ✅ Yes (`colorNeutralBackgroundInverted`) |
| **Hidden other-month days** (task 127 R13 — empty placeholder, no day number) | ❌ No (shows muted other-month days) | ✅ Yes |
| **Apply/Clear footer buttons** | ✅ Yes | ✅ Yes (vertical layout only; suppressed in horizontal) |
| **Version footer** | ✅ Yes (v2.3.0) | ✅ Yes (v1.1.0, vertical layout only) |
| **Internal `<header>` with title + dismiss icon** | ❌ No (commented out — "Header removed - title comes from Xrm side pane") | ✅ Yes (vertical only; "Calendar" + Calendar24Regular icon) |
| **`toIsoDateString()` timezone safety** (task 120 — local components, not `.toISOString()`) | ❌ No (uses `.toISOString().split("T")[0]` — has UTC off-by-one bug in non-UTC timezones) | ✅ Yes (local getFullYear/Month/Date) |

---

## Styling divergence

Both use Fluent v9 tokens (ADR-021 compliant — no hex, no rgba). Token-level differences:

- **Local copy** day cell colors: `tokens.colorBrandBackground` for selected (blue), `tokens.colorBrandBackground2` for in-range (lighter blue).
- **Shared lib** day cell colors: `tokens.colorNeutralBackgroundInverted` for selected (grey/near-black — task 129 R13), `tokens.colorBrandBackground2` for in-range, `tokens.colorBrandBackground` for event-day (`dayWithEvents`).

The selected-day color change (R13 task 129) was made to disambiguate "user-clicked day" from "day-with-events" since both were previously blue.

---

## Other observations

1. **`CalendarSidePane/package.json` does NOT depend on `@spaarke/events-components`**. Adding the dependency is a prerequisite to the import swap. Currently CalendarSidePane only depends on `@spaarke/ui-components` (line 16 of package.json).
2. The local copy's session-storage persistence has an interesting failure mode: on mount with `persistedState`, it waits 200 ms then re-emits the filter via `onFilterChange`. This is necessary because `App.tsx` listens for `setupMessageListener` from the parent — but the parent may not be ready in <200 ms. The shared lib has no equivalent because the workspace widget host doesn't restore state across pane open/close events.
3. The local copy uses a special `__ALL_DATES__` sentinel value for the "All Dates" meta-option in the date-field Dropdown. This drives the difference between "filter all 15 individual date fields" vs "filter just the user-picked subset."

---

## Recommendation: this is NOT a 4-hour import swap

The POML's stated goal (a–c) — "delete local copy; import from shared lib; CalendarSidePane visually + behaviorally identical to embedded Calendar widget" — **cannot be achieved without regression** because the embedded Calendar widget and the side pane are NOT visually + behaviorally identical in production today:

- The embedded Calendar widget has no "Filter by Date Field" dropdown, no From/To inputs, no Apply button — it's a click-day-to-filter calendar.
- CalendarSidePane has all those features and is essentially a **multi-control filter builder** whose calendar is one of three controls (calendar + From/To text inputs + date-field selector).

The task constraint says:
> "ZERO behavioral OR visual regression on CalendarSidePane record-form rendering. If divergence between local and shared `CalendarSection` exists (different props/defaults), the unification MUST resolve it via the shared lib's canonical form OR be documented and operator-approved."

A naive import swap deletes ~80 LOC of side-pane-specific UI (Apply button, From/To inputs, date-field dropdown, session-storage) and changes the filter output shape (`dateFields` becomes optional and is never emitted). This **WOULD** introduce visual + behavioral regression — likely breaking the postMessage contract with the parent record form. This is the explicit "MUST NOT" case.

### Three viable paths forward (all require operator decision)

| Option | Description | Effort | Pros / Cons |
|---|---|---|---|
| **A — Extend shared lib** | Add From/To inputs, date-field dropdown, Apply button, session-storage as OPTIONAL features in `@spaarke/events-components/CalendarSection`. CalendarSidePane opts in via props. Embedded Calendar widget continues to opt out. | ~12–20 h | True unification. Highest fidelity. But scope-explodes B-6 4× → consider re-scoping as its own task. |
| **B — Create separate shared component** | Move local copy to `@spaarke/events-components/src/components/CalendarFilterPane/` (or similar) as a SECOND shared component. Delete local copy. CalendarSidePane imports the new shared one. Embedded Calendar still uses `CalendarSection`. | ~6–8 h | Honest naming (the two are different components). Some code duplication (calendar grid logic) — partially mitigated by sharing helpers. |
| **C — Document + defer the unification; complete B-6 as "single source of truth = the side pane copy is the side-pane variant"** | Add a comment to both files clarifying they are NOT the same component. Update backlog/spec language. Mark B-6 as "documented divergence, no code change." Schedule a future R5 task to refactor. | ~1 h | Minimal disruption. Honest. But leaves ADR-012 violation in place (two CalendarSection files). |

### My recommendation

**Option B** — promote the side-pane copy to a separate shared component (`CalendarFilterPane` or similar). Rationale:
- Honestly describes the two components' purposes (one is a side pane filter builder, the other is a workspace widget calendar).
- Eliminates the local-copy-in-solutions ADR-012 violation that B-6 was meant to address.
- Lower risk than Option A (no behavior changes — just relocation).
- Higher integrity than Option C (actually fixes the structural problem).
- Fits in a follow-up 6–8h task, not the 4h R4 budget but close.

**For R4 B-6 right now**: I recommend **deferring code changes**, completing B-6 as a documentation update (this file + a clarifying comment on both `CalendarSection.tsx` files), and re-scoping the unification work for a follow-up task in R5. The current spec acceptance criterion FR-09 ("CalendarSidePane on record forms looks and behaves visually + behaviorally identical to SpaarkeAi's embedded Calendar widget") needs to be revisited — they are NOT supposed to look identical because they serve different user intents.

---

## What this sub-agent did NOT do (per parent guardrails)

- **Did NOT delete** the local copy.
- **Did NOT modify** `App.tsx` to switch import paths.
- **Did NOT add** `@spaarke/events-components` to `CalendarSidePane/package.json`.
- **Did NOT deploy** anything.
- **Did NOT touch** `TASK-INDEX.md`, `current-task.md`, root `CLAUDE.md`, or any `.claude/` files (per parent instructions).

This file is the only artifact produced; it documents the divergence for operator decision per the POML's
"document the resolution in `notes/b6-divergence-resolution.md` for R4 lessons-learned" Step 12 guidance,
escalated upstream from Step 1 because the divergence blocks the rest of the task.

---

## References

- POML: `projects/spaarke-ai-platform-unification-r4/tasks/055-b6-reconcile-calendar-sidepane.poml`
- Backlog entry: `projects/spaarke-ai-platform-unification-r4/backlog.md` §B-6 (line 356)
- Spec FR-09: `projects/spaarke-ai-platform-unification-r4/spec.md` line 123
- ADR-012 (single source of truth): `.claude/adr/ADR-012-shared-components.md`
- ADR-021 (Fluent v9 + dark mode): `.claude/adr/ADR-021-fluent-design-system.md`
- Local copy: `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx` (v2.3.0)
- Shared lib: `src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarSection.tsx` (v1.1.0)
- Related R3 tasks that shaped the shared lib: 114 (hoist), 115 (Calendar widget), 116/118/120/122/127/129 (R10-R13 polish)
