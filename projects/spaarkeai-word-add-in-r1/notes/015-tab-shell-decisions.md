# Task 015 — FR-03 Save|Find tab shell, enable navigation in Word

> **Rigor**: FULL · **Model tier**: sonnet @ high · **Step mode**: DIRECTIONAL
> **Gate**: task 010 complete (`HostAdapterFactory` is live — verified in `CLAUDE.md` Decisions Made 2026-09-09)

---

## 1. Renderer decision — `TaskPaneToolbar` vs `TaskPaneNavigation`

**Decision: `TaskPaneToolbar` is the ONE live tab-row renderer. `TaskPaneNavigation`'s own
`TabList` render stays in the codebase, explicitly documented as an unmounted helper/test
surface — not deleted.**

Verified before choosing:

- `TaskPaneShell.tsx:188-200` mounts `TaskPaneToolbar`, passing it `showTabs`, `selectedTab`,
  `onTabChange`. `TaskPaneToolbar` is the only tab-row component reachable from the render
  tree that starts at `App.tsx`.
- `TaskPaneNavigation`'s exported `TaskPaneNavigation` React component (the `<nav><TabList>…`
  JSX) has **zero import sites outside its own test file** — grep across
  `src/client/office-addins` before this task found it referenced only in
  `TaskPaneNavigation.tsx` itself, `components/index.ts` (re-export), and
  `__tests__/TaskPaneNavigation.test.tsx`. So the "two competing renderers" the task
  description warned about were never simultaneously *wired* — only simultaneously *present
  in the file tree*, which is the confusing-but-not-broken state the task asked to resolve
  explicitly.
- `getAvailableTabs(hostType)` — the actually-shared piece — is consumed by `TaskPaneToolbar`
  (`TaskPaneToolbar.tsx:118`) and now also by `App.tsx` (`showNavigation` derivation, §3
  below). `getDefaultTab` is consumed by `TaskPaneShell.tsx:152`. Both survive unchanged.

**Why keep the component instead of deleting it** (the other sanctioned path per the POML):
`__tests__/TaskPaneNavigation.test.tsx` has six tests covering behavior `TaskPaneToolbar`
does not have equivalent coverage for in isolation (compact-mode icon-only rendering,
`disabled` prop → `aria-disabled`, per-host tab filtering) without the toolbar's additional
concerns (overflow menu, auth gating, theme picker). Deleting the component would either
lose that isolated coverage or force rewriting it against the heavier `TaskPaneToolbar`
surface — a bigger, riskier diff than documenting the component as intentionally unmounted.
Cost of keeping it: a one-time reader has to notice the file-header note before assuming it's
live. Cost of deleting it: lost isolated test coverage + a same-PR test rewrite outside this
task's stated scope (extend the tab model + shell, not re-architect test coverage).

**What changed in the file**: `TaskPaneNavigation.tsx`'s header comment now states the
decision explicitly (renderer + reason + "do not wire it into the shell alongside the
toolbar"). No behavioral change to the component itself — it still renders correctly if a
future caller mounts it deliberately (e.g. a compact secondary nav), it is just not that
caller today.

**Did not do**: did not restructure `TaskPaneShell` to conditionally choose between the two
renderers, and did not add a third rendering path. That would have crossed escalation
trigger 1 ("a shell rewrite is not in this task's scope").

---

## 2. `search` vs `find` — added a NEW `find` member; did not reuse `search`

**Decision: `NavigationTab` gets a new `'find'` member. The pre-existing `'search'` member
stays, untouched, meaning something else.**

Why not reuse `'search'`:

- `'search'` is already wired in `App.tsx` (`currentTab === 'search'`) to `StatusView` with
  `refreshInterval={0}` — a **job-status** view (`ProcessingJob[]`, progress bars, stage
  lists), not a document-similarity search. Its inline comment ("Placeholder - shows
  document search in later tasks") is aspirational and does not match what the component
  actually renders. Repurposing `'search'` for Find would mean either (a) leaving that
  stale StatusView wiring in place under a now-misleading tab name, or (b) rewriting the
  `'search'` branch — both are unrelated-surface changes outside this task's declared scope
  (`TaskPaneNavigation.tsx`, `TaskPaneShell.tsx`, `App.tsx` tab model + shell only).
- `'search'` is not in `TAB_CONFIGS` (still commented out) and therefore unreachable via the
  UI today — there is no live behavior to preserve or rename by keeping it separate.
- Using a fresh `'find'` member keeps the git history and the union both honest: `find` maps
  1:1 to spec.md FR-03's "Find" tab and this task's Find frame; `search` remains a distinct,
  still-hidden legacy placeholder. Both are now explicitly documented as NOT meaning the same
  thing (see the JSDoc above `NavigationTab` in `TaskPaneNavigation.tsx`).

The union does not carry two members meaning the same thing: `find` = new Find frame (this
task + Phase 3), `search` = the pre-existing, still-unbuilt/unreachable job-status stub.

---

## 3. `showNavigation` — derived from `getAvailableTabs`, not a `hostType` literal

`App.tsx:376` was `showNavigation={hostType === 'outlook'}`. Replaced with
`showNavigation={getAvailableTabs(hostType).length > 0}` (imported from
`./components/TaskPaneNavigation`). Since `TAB_CONFIGS` now declares Save and Find as
`availableFor: ['outlook', 'word']`, this evaluates true for both hosts post-auth, without a
hardcoded host conditional — satisfying the "gate by host CAPABILITY, not scattered
`hostType` conditionals" constraint (NFR-10) and step 4's guidance to derive visibility from
`getAvailableTabs` where the chosen renderer already uses it (`TaskPaneToolbar` does).

`createTodo` stays `availableFor: ['outlook']` — unchanged, so Outlook's existing tab set
(Save, Create To Do) is preserved and now gains Find; Word goes from zero tabs to Save, Find.

---

## 4. Find frame — what was and wasn't built

New file: `shared/taskpane/components/views/FindView.tsx`. It renders a static Fluent v9
empty-state (icon + "Find is coming soon" + one line of body text). It:

- issues **no** network/fetch call,
- renders **no** results list,
- renders **no** pager, "Load more" button, or numbered-page control (ADR-051 — there being
  no list at all trivially satisfies "no pager anywhere"),
- does not import `@spaarke/ui-components` (ADR-012 Path A exception stays honored — no new
  import of the shared package was needed for a static placeholder).

Mounted in `App.tsx` via `{currentTab === 'find' && <FindView />}`, alongside the existing
`share`/`search`/`recent` branches (untouched — those remain unreachable via the UI since
their `TAB_CONFIGS` entries stay commented out, per the "do not build placeholder views for
share/recent" constraint, which does not apply to Find since Find explicitly IS this task's
placeholder to build).

Tasks 033-034 replace `FindView`'s body. The mount point (`currentTab === 'find'`) does not change.
Their prerequisite, task 032's per-row authorization on the similarity surface (plan.md finding
F-b), is already complete (`f892c8ada`).

> Corrected 2026-09-10 in the main session. This section originally said the Find view waited on 032
> "landing" and that the similarity engine had "no per-row authorization today". Both were stale:
> 032 was done before 015 started. The same claim was fixed in `FindView.tsx`, `App.tsx` and
> `TaskPaneNavigation.tsx`.

---

## 5. Accessibility — keyboard nav + `useAnnounce`

- **Keyboard navigation**: Fluent v9 `TabList`/`Tab` (used by `TaskPaneToolbar`) provide
  ARIA `tablist`/`tab` roles with built-in roving-tabindex arrow-key navigation and
  Enter/Space activation natively — no custom keyboard handling was added or needed.
- **Announcements (NFR-11)**: `TaskPaneToolbar` now calls `useAnnounce()` (the React-owned
  live-region hook, `shared/taskpane/hooks/useAnnounce.ts`, task 018 pattern) and announces
  `"{label} tab selected"` on every `onTabSelect`. `{liveRegion}` is rendered inside the
  toolbar's `<header>` per the hook's contract (must be mounted through React's own tree,
  not created out-of-tree — the React 19 defect task 018 fixed).

---

## 6. Deviations from the literal step order

None beyond what's recorded above. Steps were executed in the POML's order (renderer
decision → naming decision → tab model → `App.tsx` flip → Find frame → accessibility →
module `CLAUDE.md` → install/typecheck/build/test → ui-tests → index update → this file).

## 7. Escalation triggers checked — none fired

- Trigger 1 (shell rewrite): did not fire — the renderer decision required no restructuring,
  only documentation + one new `TAB_CONFIGS` entry.
- Trigger 2 (Save view layout regression in Word): did not fire — Save view content and
  layout are unchanged; only `showNavigation`'s boolean source changed, and Word already
  renders the same `TaskPaneToolbar`/`TaskPaneShell` components Outlook does today (no new
  layout code path).
- Trigger 3 (Find requiring a similarity/search call): did not fire — `FindView` is fully
  static.
