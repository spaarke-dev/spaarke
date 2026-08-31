# Task 040 — NavigatorPane code page — deviation notes

> Task: `tasks/040-navigatorpane-codepage.poml` · Completed 2026-08-13 (sonnet, FULL rigor)

## Deviation 1 — Theme detection mechanism (Path C — pivot to comply, CLAUDE.md §6.5)

**Task constraint text**: "code-page theme detection (prefers-color-scheme + theme URL param); --sprk-ui-scale via scaledTheme/useUiScale (NFR-07)."

**What was built instead**: `NavigatorBody.tsx` calls the existing `resolveCodePageTheme()` +
`setupCodePageThemeListener()` from `@spaarke/ui-components` (localStorage → URL `flags` param →
Dataverse navbar DOM color detection — the 3-tier cascade already documented at the top of
`src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts`). No raw
`window.matchMedia('(prefers-color-scheme: dark)')` listener was added.

**Why**: ADR-021 (`.claude/adr/ADR-021-fluent-design-system.md`) and every theme-detection function in
`themeStorage.ts` / `xrmContext.ts` explicitly state OS `prefers-color-scheme` is **intentionally NOT
consulted** — "ADR-021 requires the Spaarke theme system (not the OS) to control all UI surfaces." Adding
an OS-level media-query listener would have contradicted this documented rule and introduced a second,
parallel theme-detection mechanism alongside the one every sibling code page (`CalendarSidePane`,
`EventDetailSidePane`) already uses. The task's constraint phrase appears to be boilerplate generalized
from the ADR-021 concise summary table (which does say "Code Pages... read parameters from
URLSearchParams") rather than a literal instruction to add OS-preference detection.

**Path chosen**: **C — pivot to comply.** The ADR-compliant, already-proven mechanism
(`resolveCodePageTheme`/`setupCodePageThemeListener`) meets the "theme detection" requirement equally
well (arguably better, since it is the one every other Navigator-adjacent surface already uses and the
Navigator pane must stay visually consistent with them). No exception or amendment needed.

## Deviation 2 — a11y fix caught during self-run code-review (Step 9.5)

Not a deviation from the task spec, but worth recording: the first draft of the search-bar info Tooltip
used a bare, non-focusable `Info16Regular` icon as the trigger with `Tooltip relationship="label"`. Two
problems:

1. A bare SVG icon (no `tabIndex`/interactive role) is not keyboard-reachable — a keyboard-only user could
   never open the tooltip. WCAG 2.1 AA is a MUST under ADR-021.
2. `relationship="label"` makes the tooltip's content **become** the trigger's accessible name (replacing
   any existing `aria-label`) — since the tooltip text ("Search wiring lands in a later task.") differs
   from the icon's own `aria-label` ("Search availability"), this would have silently produced the wrong
   accessible name.

**Fix**: the trigger is now a `Button appearance="transparent" size="small" icon={<Info16Regular />}
aria-label="Search availability"` (keyboard-focusable, matches the `SprkSidePaneHost` rail-icon Button
pattern), and the `Tooltip` uses `relationship="description"` (adds `aria-describedby` without touching the
Button's own name). Verified by `npx tsc --noEmit` (clean) and `npx jest` (5/5 still passing) after the fix,
then the Vite bundle was rebuilt (cache-cleared) and the known-string check re-verified.

## Deviation 3 — jest tooling additions (not scope creep, infrastructure-only)

`src/solutions/NavigatorPane/jest.config.cjs` needed two additions beyond the `Notepad`/`SmartTodo`
sibling-config template, both infrastructure-only (no product-code implication):

1. `@testing-library/dom` added to `devDependencies` — a missing peer of `@testing-library/react` v16 that
   the sibling configs happened not to need (their tests don't render via `@testing-library/react` the same
   way, or the peer was already hoisted incidentally in their trees).
2. `transformIgnorePatterns` extended to allow `d3-force|d3-dispatch|d3-quadtree|d3-timer|marked` —
   `@spaarke/ui-components`'s `dist/index.js` barrel transitively pulls in `useForceSimulation.ts` (which
   imports the ESM-only `d3-force`) even though `NavigatorBody` never touches it. This exact allow-list
   already exists in the shared lib's own `jest.config.js`; NavigatorPane's config mirrors it rather than
   inventing a narrower one.
3. `moduleNameMapper` gained `^react$` / `^react-dom$` / `^react-dom/client$` / `^react/jsx-runtime$`
   entries forcing resolution to NavigatorPane's own `node_modules/react`. `@spaarke/ui-components` is a
   `file:`-referenced package (no npm workspace hoisting in this repo), so it has its own, separate
   `node_modules/react`. Without this mapping, `useUiScale()` (called from the pre-built `dist/hooks/`
   bundle) executes its `useState` call against a *different* React module instance than the one
   `react-dom` used to render the test tree — that module's internal hook dispatcher is `null` outside an
   active render, producing `TypeError: Cannot read properties of null (reading 'useState')`. This is the
   first NavigatorPane-adjacent test to call a *hook* re-exported from the shared lib's dist bundle inside
   a consumer package's Jest run (`SprkSidePaneHost.test.tsx` doesn't hit this because it tests source
   files from *within* the same package, with one React copy). Future code pages that call shared-lib hooks
   from Jest should copy this `moduleNameMapper` block rather than rediscovering the fix.
