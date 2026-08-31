# Task 010 (DateField renderer) — deviations from the POML plan

> Task: `projects/record-header-and-notepad-r2/tasks/010-renderer-datefield.poml`
> Status: implementation complete, both quality gates (code-review, adr-check) clean.

None of these are scope changes — all are within-step engineering decisions the directional `<steps>` mode explicitly permits, documented here per step 7.

## 1. Escalation trigger checked, not fired — `@fluentui/react-datepicker-compat` added

The POML's `<escalation>` block required stopping before adding the picker package if it (a) failed the
React 16/17 platform-library boundary, (b) dragged heavy transitive deps into the shared-lib dist, or (c)
couldn't be themed with semantic tokens. Checked with evidence before adding:

- Fetched the published package's `peerDependencies` from the npm registry: `react`/`react-dom`
  `>=16.14.0 <20.0.0` — compatible with both the PCF platform-library runtime (17.0.2) and Code Page React 19.
- Checked `node_modules` before installing: **8 of the package's 9 transitive `@fluentui/*` dependencies were
  already present** (pulled in transitively by the existing `@fluentui/react-components` tree —
  `react-popover`, `react-positioning`, `react-tabster`, `react-portal`, `react-field`, `keyboard-keys`,
  `react-shared-contexts`, `@griffel/react`). Only `@fluentui/react-datepicker-compat` itself and
  `@fluentui/react-calendar-compat` are net-new packages.
- It's Griffel/token-based (same v9 styling substrate as the rest of the codebase) — themeable per ADR-021.

None of the three conditions fired, so no escalation was raised. Added
`@fluentui/react-datepicker-compat@^0.6.37` to `devDependencies` (mirroring how `@fluentui/react-components`
itself is declared — devDependency only, not peerDependency, matching the existing precedent in this
package.json).

**Deferred, out of scope for this task**: no PCF currently imports `DateField` (barrel export is task 015's,
consumer wiring is tasks 022/031/033). Whichever task first wires `DateField` into a PCF's webpack build will
need to confirm the bundle-optimization triad (`webpack.config.js` externals) handles this new package — it is
NOT covered by the "Fluent" platform-library manifest declaration (that only externalizes
`@fluentui/react-components` imports, not other Fluent v9 family packages).

## 2. `autoFocus` replaced with ref + effect focus

The FR-10 contract requires the edit input to `autoFocus` (mirroring TextField). Using the native `autoFocus`
HTML attribute on `<DatePicker>` caused a real, empirically-reproduced failure: React's `commitMount` calls
`.focus()` synchronously during the commit phase, which collides with Fluent's `useEventCallback` render-phase
guard on DatePicker's internal `onFocus` handler → `"Cannot call an event handler while rendering"` (thrown,
caught by the test suite — 8 of 20 tests failed before the fix).

Fix: a `dateInputRef` + `React.useEffect(() => { if (editing) dateInputRef.current?.focus(); }, [editing])`.
Passive effects run after commit finishes, avoiding the collision. Same UX result (input focused on entering
edit mode), documented inline in the component's JSDoc and code comments.

## 3. Keyboard commit/cancel scoped to "calendar popup closed"

`DatePicker` (compat) claims Enter/Escape internally for its own popup-open/close semantics (verified by
reading the library source: `useDatePicker.tsx`'s `onInputKeyDown`). To deliver the required Enter=commit /
Escape=cancel contract on the DateField's own value without fighting the vendor component's keyboard
navigation for its calendar grid, DateField tracks `pickerOpen` (controlled via `DatePicker`'s own
`open`/`onOpenChange` props) and only applies its own Enter/Escape/blur commit/cancel logic while the popup is
closed. While the popup is open, those keys are left entirely to the popup's own navigation/dismiss handling.
Selecting a calendar day (`onSelectDate`) commits immediately regardless of popup state — this matches the
form-buffer dirty-state UX described in the ui-tests section ("pick a date; verify the form goes dirty").

Trade-off, noted for a later a11y pass (not a regression against any acceptance criterion): a keyboard user
cannot press Enter to *open* the calendar from a closed state (Enter there means commit); ArrowDown still
opens it.
