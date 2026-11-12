# Task 001 — Shared `RecordCommandBar` + record-action icon map (FR-04, FR-05)

Executed 2026-11-11 as a parallel wave-1 agent (with task 002). Rigor **FULL** (new `.ts`/`.tsx` in the shared library). Model: Opus @ high (POML `<model-tier>opus</model-tier>`, `<effort>high</effort>`). Steps: directional.

## What was built

`src/client/shared/Spaarke.UI.Components/src/components/RecordCommandBar/`:

| File | Content |
|---|---|
| `types.ts` | `RecordCommandBarAction` (exactly `id`, `label`, `icon`, `onClick`, `visible`, `tooltip`, optional `overflow`; no `disabled`) and `RecordCommandBarProps` (`actions`, `ariaLabel`, optional `className`), all JSDoc'd |
| `recordActionIcons.ts` | `RECORD_ACTION_ICONS` (frozen, the twelve FR-05 entries keyed by action id) and `RecordActionIconId` |
| `RecordCommandBar.tsx` | Filters `visible === true`; maps inline actions to `HeaderToolbar` slots; overflow actions render in a Fluent v9 `Menu` behind one "More actions" slot that exists only when a visible action overflows |
| `index.ts` | Exports the component, both types, the icon map and its key type |
| `__tests__/RecordCommandBar.test.tsx` | 14 Jest tests (RTL inside `FluentProvider`) |

`src/components/index.ts` gains `export * from './RecordCommandBar';` beside the HeaderToolbar export. Not added to `pcf-safe.ts` (per `src/client/shared/CLAUDE.md`).

HeaderToolbar's files are unchanged; its public contract (`IHeaderToolbarSlot`, `IHeaderToolbarProps`) was used as-is. No new npm dependency.

## Decisions and deviations

- **Overflow menu anchored through `buttonRef`, not `<MenuTrigger>`.** The trigger is a HeaderToolbar slot, so the `Menu` is controlled (`open` / `onOpenChange`) and positioned with `positioning.target` = the slot's button (held in state via `buttonRef`). Fluent's Menu attaches its own focus-return and outside-click handling only to a `<MenuTrigger>` child, so the component supplies three small pieces itself:
  1. **Focus first item on open.** Fluent's own focus-first runs when `open` flips, before the portalled list has mounted (verified under jsdom: even a stock `<MenuTrigger>` menu opened with Enter leaves focus on the trigger). Without this a keyboard user would sit on the trigger, where arrow keys do nothing. Runs once per opening; skipped if focus is already inside the list.
  2. **Focus return on close.** Mirrors Fluent's rule: on open → closed, if focus is on `<body>` or inside the closing popover, return it to the trigger. A dialog that an item opened keeps its focus.
  3. **Trigger click is not an outside click.** Fluent's outside-click listener runs in the capture phase, before the trigger's onClick; it is ignored when the target is the trigger, and the trigger's onClick toggles the menu.
- **Menu-button semantics set imperatively.** `aria-haspopup="menu"` and `aria-expanded` are set on the trigger element in an effect, because the slot contract has no aria props and must not change (escalation trigger 1 did not fire: no HeaderToolbar type change was needed).
- **`visible === true` only** renders an action (fails closed for an untyped descriptor).
- **Overflow menu items**: visible text = `label`; tooltip and `aria-label` = `tooltip` (AC: tooltip text is the accessible name). JSDoc asks callers to keep the label inside the tooltip (WCAG 2.5.3 label-in-name).
- **"More actions" icon** is `MoreHorizontal20Regular`, the repo's existing overflow icon (`DataGrid/commandBar/CommandBar.tsx`). It is the overflow control, not a record action, so FR-05's map does not cover it.
- **Bar root** is `role="toolbar"` with the caller's `ariaLabel` (precedent: `DataGrid/commandBar/CommandBar.tsx`). Controls are Tab stops, as the AC requires.
- **POML note drift:** the POML lists "Open full form" among `EmailToolbar`'s icon buttons; the current file removed it (comment at `EmailToolbar.tsx:159`). The candidate evidence below uses the current file.
- **No runtime barrel test.** Importing `src/index.ts` or `src/components/index.ts` under Jest pulls `@spaarke/sdap-client`, which resolves only after that package is built; CI's shared-lib Jest jobs run from a clean checkout without sibling `dist/` (see `ci-tier1-blocking.yml` "NO SHARED-LIB DIST NEEDED"). Barrel importability was verified instead by `tsc --noEmit` over the package with a temporary probe importing `RecordCommandBar`, `RecordCommandBarProps`, `RecordCommandBarAction`, `RECORD_ACTION_ICONS` and `RecordActionIconId` from `src/index.ts` (and a `@ts-expect-error` proving `disabled` is rejected). The probe was deleted.
- **ADR-038 scope:** ADR-038 states it does not apply to React/PCF Jest tests; the shared-lib test convention (`src/client/shared/CLAUDE.md` "Tests") was followed.

## Candidate second consumer (ADR-012 Path C condition)

**`EmailToolbar`** — `src/client/shared/Spaarke.Communication.Components/src/components/EmailReadingPaneShell/EmailToolbar.tsx`.

- It hand-builds a record-level action bar: a Fluent `Toolbar` at `:99` whose right-hand cluster (`:126-160`) is icon-only buttons each wrapped in its own `Tooltip relationship="label"` with a matching `aria-label` (`:127` Save to SharePoint, `:135` Create Event, `:143` Create To Do, `:151` Link Invoice). That is the exact shape `RecordCommandBar` provides.
- Its actions are gated by one `recordActionsDisabled` flag (`:85`) and shown **disabled**, not hidden — the drift FR-04/FR-07's hide-don't-disable rule exists to prevent.
- Reach: rendered by `EmailReadingPaneShell.tsx:299`, which `EmailWorkspace` composes; `EmailWorkspace` is mounted by the Email code page (`src/solutions/EmailPage/src/main.tsx:468`) and by the Console's email widget (`EmailWorkspaceWidget`, resolved in `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx:1913`).
- Its header says it mirrors the `CommunicationActions` PCF bar — a second hand-built copy at `src/client/pcf/CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx:621` (`<Toolbar … aria-label="Communication actions">`).

Named only; migrating it is out of scope (POML constraint "Do not migrate any existing toolbar in this task"). A migration would also need the icons (`CloudArrowUp`, `CheckmarkCircle`, `Receipt`) reconciled with the FR-05 map — a decision for FR-34's standards proposal.

## Quality gates (Step 9.5)

### code-review (standard depth) — **Path C shared placement: APPROVED**

- Security: no secrets, no I/O, no user-supplied HTML. Performance: three array filters per render over a handful of actions; no effects on hot paths. Lint: `eslint` on the folder + barrel: 0 errors, 0 warnings.
- AI smells: none (no single-impl interfaces, no log-rethrow, no null checks on non-nullable types, comments explain why).
- Metrics: `RecordCommandBar.tsx` ~220 lines, 1 export, cohesive (one responsibility: render descriptors as a toolbar + its overflow menu). All files new (no baseline).
- §11 justification (POML `<justification>`): file:line references verified (`HeaderToolbar.tsx:134`, `DocumentToolbar.tsx:105`, `CommandBar.tsx:310`); extension answer concrete (frozen slot contract, disabled-not-hidden semantics); cost of doing nothing concrete (per-type visibility drift → a View-only caller offered an action that answers 403).
- Path C conditions: composes `HeaderToolbar` for every inline button and the overflow trigger (no copy of its button/tooltip markup; the menu uses Fluent `Menu`/`MenuItem`/`Tooltip` primitives) ✅; ships with tests ✅; no Xrm / entity literals (negative grep clean) ✅; second consumer named with evidence ✅. Placement under ADR-012's "Core Spaarke UX pattern" row is sound: a record-level action bar with per-record visibility sits next to `HeaderToolbar` and `DocumentToolbar`, and a second hand-built instance already exists in two surfaces.
- Findings — no F-class finding. Known limits (K-class), one line each:
  - K4 — `role="toolbar"` without roving arrow-key navigation; controls are Tab stops as FR-04 requires (same as `DataGrid` `CommandBar`).
  - K4 — overflow items show their tooltip when focused, repeating the visible label (AC requires a tooltip on every rendered action).
  - K4 — an async `onClick` that rejects surfaces as an unhandled rejection; same documented contract as `IHeaderToolbarSlot.onClick` (caller handles errors).
  - K4 — duplicate action ids give a React duplicate-key warning; JSDoc requires unique ids.
  - K2 — clicking the trigger from inside a shadow DOM would be read as an outside click (event retargeting); the menu then reopens via the toggle; fails safe.
  - K4 — `HeaderToolbar`'s root paints `colorNeutralBackground1`; a host on a different surface colour sees a band. HeaderToolbar property, not changed here; task 003 owns layout.

### adr-check — **ADR-012 Path C: APPROVED**; 0 violations, 0 warnings

ADRs considered (mapping + `INDEX.md` pass): ADR-012 ✅ (context-agnostic, exported types, tests, no `Xrm`/`ComponentFramework`/`fetch`, no entity literals, main barrel only); ADR-021 ✅ (Fluent v9 only, icons from `@fluentui/react-icons` with `currentColor`, `makeStyles`, no colour literals at all, verified under light/dark/high-contrast); ADR-022 ✅ (only `useState`/`useEffect`/`useRef`; no `useId`, no React-18-only API; not added to `pcf-safe.ts`); ADR-028 ✅ (no fetch / tokens); ADR-050 n/a (no modal; the menu's Escape handling calls `preventDefault`, so Esc closes the menu before a parent SprkModal — FR-03 friendly); ADR-051 n/a (no scrollable list); ADR-038 n/a (scope is .NET tests); ADR-001/002/007/008/010/013/052 n/a (no server code). No ADR conflict; the approved ADR-012 Path C tension's conditions are met.

## Tests and checks run

- `npx jest src/components/RecordCommandBar` — **14/14 pass**: hidden action absent + no tab stop; empty list; all-hidden list; inline tooltip = aria-label (+ tooltip shown on hover); overflow item tooltip = aria-label (+ shown on hover); no "More actions" without a visible overflow action; overflow actions only inside the menu (+ `aria-haspopup`/`aria-expanded`); trigger click toggles closed; Enter and Space on inline actions; overflow keyboard flow (Tab to trigger, Enter opens with focus on first item, Enter runs the second item once, focus returns to trigger, Space reopens, Esc closes and returns focus); light/dark/high-contrast with no `console.error`; icon map = exactly the twelve FR-05 entries and frozen.
- Must-fire check: the overflow keyboard test failed before the focus-first effect was added (focus stayed on the trigger), so it guards that behaviour.
- `npx tsc --noEmit -p tsconfig.json` (package) with the barrel probe — **exit 0**. (Required building sibling `Spaarke.Auth` and `Spaarke.SdapClient` `dist/`, gitignored, no source change.)
- `npx eslint src/components/RecordCommandBar src/components/index.ts` — clean.
- Negative grep of `components/RecordCommandBar/` for `Xrm`, `ComponentFramework`, `fetch(`, `sprk_`, hex/rgb/hsl — no matches.
- Full package Jest suite (`npx jest --ci --maxWorkers=2`) — **367/367 suites, 6112/6112 tests pass**.

Test scope beyond the AC list: the trigger-toggle test and the `aria-haspopup`/`aria-expanded` assertion guard the two behaviours this component adds on top of Fluent (outside-click exemption, menu-button semantics), which no other test covers.

## Known limits (not verifiable here)

- **Arrow-key movement inside the menu** is Fluent `MenuList`'s own tabster behaviour, which does not run under jsdom (layout-less elements count as hidden). The keyboard test places focus on the second item directly. Real-browser arrow navigation is to be checked when task 003 renders the bar in the SPA (POML `<ui-tests>` "Overflow menu").
- **UI tests in a browser** (POML `<ui-tests>`): not run — there is no consumer page until task 003, and no deploy is allowed in this wave. The three scenarios are covered at the Jest level (DOM absence + Tab order; Enter/Esc/focus return; themes); visual focus rings and high-contrast rendering need the task 003 browser pass.

rework: none task 001
