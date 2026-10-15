# Task 080 (six hazards cleanup) — deviations from the POML, recorded per step 10

> spec FR-42; `notes/reuse-verification-2026-10-02.md` §§8.7-8.10.

## Outcome in one line

Five of six hazards fixed outright. One (C-21, Pillar-9) escalated per the POML's own named example and root
CLAUDE.md §6.5 rather than guessed — a privacy/architecture judgment call, not a code-quality call. One
sub-item of C-19 (`useKeyboardShortcuts.ts`) also escalated for the same reason, at much lower stakes.

## 1. C-19 — CommandRegistry cluster: four of five files deleted, one escalated

The POML's scope line names the cluster as "the CommandRegistry cluster" and the reuse-verification audit
(X11, §8.9 C-19) lists five symbols: `CommandRegistry`, `EntityConfigurationService`, `CustomCommandFactory`,
`Toolbar/CommandToolbar`, `useKeyboardShortcuts`.

Grepping every one of those five names across the ENTIRE `src/` tree (not just the owning package) found:
- `CommandRegistry.ts`, `EntityConfigurationService.ts`, `CustomCommandFactory.ts`,
  `components/Toolbar/CommandToolbar.tsx` — **zero consumers outside their own test files and their own
  barrel re-exports.** Confirmed `CommandExecutor.ts` (the live, generic command builder the POML says to
  leave untouched) does NOT import any of these four. Deleted all four + their tests + the Toolbar folder;
  fixed the three barrel files that referenced them (`services/index.ts`, `types/index.ts`,
  `components/index.ts`) and the `jest.config.js` coverage list.
- `useKeyboardShortcuts.ts` — **has a real, non-test importer**: `components/PageChrome/CommandBar.tsx`
  (`import { useKeyboardShortcuts } from '../../hooks/useKeyboardShortcuts'`, called at runtime, two call
  sites). This is exactly the scenario the POML's own `<escalation>` names ("if CommandRegistry has a live
  consumer after all, STOP and escalate rather than deleting it") — so it was NOT deleted. Filed as
  [issue #1113](https://github.com/spaarke-dev/spaarke/issues/1113) / `defer-issues.md` ISS-006, because
  `PageChrome/CommandBar.tsx` itself appears to have zero consumers outside its own test (checked via its
  barrel-exported type names `ICommandBarProps`/`ICommandBarItem` across `src/solutions/` and `src/client/pcf/`
  — a deeper reachability trace than this task's scope covers is needed before deleting the whole chain).

A false positive worth recording: `components/DataGrid/commandBar/registry.ts` (a completely separate,
genuinely live module backing `<DataGrid>`'s own CommandBar) contains a `console.warn('[CommandRegistry] ...')`
string literal — a naming coincidence, not an import of the dead service. Verified by reading the file; left
untouched. This is the exact over-counting risk the reuse audit's own §8.6 warns about (verify by import
specifier, not by name-grep).

## 2. C-5 — both named functions deleted, plus their thin wrapper

Deleted `composeCommentThreadsToDocxAnnotations` and `composeSessionCommentThreadsToDocxAnnotations` (the
latter is a thin wrapper that only calls the former — not independently named in the POML's prose but
unambiguously in scope since it has no other purpose) from `ComposeCommentThread.types.ts`, and
`anchoredAnnotationsToDocxAnnotations` from `useComposeWordShuttle.ts` (named together with the first function
in the audit's own C-5 item, §8.7). Confirmed via grep that `ComposeEditor.tsx` (the real save path) calls only
`composeSessionCommentThreadsToAnchoredComments` — never any of the three deleted functions. Removed their
barrel exports, their own unit-test coverage, and fixed every dangling `{@link}`/prose doc reference across
4 files. Left the 7 `ComposeWorkspace.*.test.tsx` files' `anchoredAnnotationsToDocxAnnotations: () => []` mock
stubs untouched — each is a single inert property on a `jest.mock()` factory object, not a real import of the
deleted symbol, so removing them has zero behavior change and only adds blast radius for no benefit.

## 3. C-10 — converged on ONE `parseDueDate`, not a relocation of the whole formula

The reuse audit's broader §8.7 item (also labeled C-10 there) proposes hoisting the WHOLE `todoScoring.ts` file
into `Spaarke.UI.Components`. This POML's own C-10 scope is narrower and more precise: "converge the three
scorer copies on one implementation carrying the local-midnight fix." Doing the broader hoist would also
contradict a standing, documented design decision in `Spaarke.UI.Components/src/utils/todoScoreMappings.ts`'s
own header comment: the composite-score FORMULA/WEIGHTS are deliberately "locked" in
`Spaarke.SmartTodo.Components/src/utils/todoScoring.ts` and that file explicitly does NOT duplicate them
elsewhere. Relocating the whole file would also be impossible without inverting a real dependency edge:
`Spaarke.SmartTodo.Components` depends on `@spaarke/ui-components`, not the reverse, so
`TodoDetail.tsx` (which lives in `Spaarke.UI.Components`) cannot import from `Spaarke.SmartTodo.Components`.

Resolution: extracted ONLY the narrow date-parsing primitive (`parseDueDate`, the exact function with the
local-midnight fix) into a new file, `Spaarke.UI.Components/src/utils/dateLocal.ts` (the one package both
`Spaarke.SmartTodo.Components` and `TodoDetail.tsx` can reach), and pointed all three copies at it:
- `Spaarke.SmartTodo.Components/src/utils/todoScoring.ts` — now imports + re-exports it instead of defining
  its own (bit-for-bit identical logic; no behavior change for this file).
- `Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts` — its own buggy local `parseDueDate` (plain
  `new Date(isoString)`, no local-midnight handling) deleted; now imports the canonical one. **This is the
  actual bug fix** — Kanban bucketing previously disagreed with `todoScoring.ts`'s score by a day in
  negative-UTC-offset zones for date-only due dates.
- `Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx` — same fix, same bug class.

The composite formula/weights themselves are untouched in all three files — only the shared date-parsing
primitive moved, which keeps `todoScoreMappings.ts`'s "locked formula" doc comment accurate rather than stale.

**A finding not required to fix, left alone**: `src/solutions/LegalWorkspace/src/hooks/useKanbanColumns.ts` is
a FOURTH, older, apparently-dead copy of this same bucketing logic (same bug class — plain
`new Date(todo.sprk_duedate)`), but it is NOT one of "the three copies" the audit names in §8.10 (those three
are `todoScoring.ts`, the SmartTodo.Components `useKanbanColumns.ts`, and `TodoDetail.tsx` — all in different
packages than LegalWorkspace's). Grepped for any importer of this specific file across the whole repo and found
none — the live Kanban hook used by `src/solutions/SmartTodo/src/components/SmartToDo.tsx` is the hoisted
`@spaarke/smart-todo-components` one, not this LegalWorkspace-local fork. Left untouched: it is outside this
task's named scope, and per "resist the urge to redesign," fixing or deleting a fourth, seemingly-unreferenced
copy was not attempted without a dedicated audit of its own.

Tests added: `Spaarke.UI.Components/src/utils/__tests__/dateLocal.test.ts` (TZ-pinned to America/New_York,
following the established pattern in `EntityInfoWidget.tz.test.tsx`) and
`Spaarke.SmartTodo.Components/__tests__/todoScoring.kanbanParity.tz.test.ts` (cross-file forcing-function test:
asserts `todoScoring.ts`'s and `useKanbanColumns.ts`'s `parseDueDate` are the SAME function reference — not
just equal output today — plus a behavioral TZ-pinned agreement check between Kanban bucketing and the
composite score for a due-tomorrow date).

## 4. C-22 — removed the dead render path (did not build the missing bundle)

The POML offers two valid remediations: "either restore the mount bundle or remove the dead render path."
Building the missing `@spaarke/ai-widgets` browser-IIFE bundle (`window.SpaarkeAiWidgets`) was not attempted:
the package has NO existing bundler config at all (`package.json` `build` script is plain `tsc`, no
esbuild/webpack/rollup anywhere in the package), so standing one up — entry point, global name, React/ReactDOM
inlining decisions, Fluent theme bridging, cross-widget singleton concerns — is new build infrastructure, not
a cleanup fix. That is Task 043's original, never-completed scope, not this task's.

Took the second option: removed `insightCardMount.ts`'s `_renderPlaceholder` function and both call sites,
so the bundle-absent case now behaves identically to the already-existing host-not-found case (log + skip, no
DOM mutation). Recompiled via `tsc -p src/dataverse/forms/sprk_matter/tsconfig.json` to regenerate the shipped
`insightCardMount.js` web resource (verified 0 remaining references to the removed code in the compiled
output).

**A second, more significant placeholder was found that the audit's X16 entry didn't separately name**:
`src/dataverse/forms/sprk_matter/html/matter_insight_card_host.html` renders its OWN iframe-scoped placeholder
card with live Subject/Topic/Mode/Envelope facts, and its own header comment explains WHY it exists
independently of `insightCardMount.ts`: the host `<div>` lives inside a Power Apps WebResource iframe, so the
PARENT-document script's `document.getElementById` can never reach it cross-document — meaning
`insightCardMount.ts`'s React-mount code path was likely already unreachable regardless of the bundle, and
THIS html file's inline script is the actually-visible surface on the Matter form. Rewrote it the same way:
removed the visible placeholder DOM, kept the contract-verification logic as console-only diagnostics.

## 5. C-21 — escalated, not fixed (see `defer-issues.md` ISS-005 / issue #1112)

Verified the audit's X15 claim directly rather than trusting it (per CLAUDE.md §6 operational-traps note that
one prior claim in this same audit was wrong once): `getWorkspaceWidgetVisibleStateFn` genuinely has zero
non-test call sites, and `SprkChatAgentFactory.cs`'s `TryDeriveVisibleState`/`BuildWorkspaceStateBlock` do
independently re-derive the same shapes server-side — and in fact (a detail the audit didn't have) a LATER
server-side change (R3 task 011) trims the per-tab prompt output down to `{type, label, active}` only, so even
the server's OWN richer derivation is mostly unused today. All three remediation paths (delete / wire-live /
re-document-as-non-authoritative) carry real tradeoffs only the owner can weigh — wiring it live in particular
would be a security/privacy architecture decision root CLAUDE.md §6 reserves for human sign-off. Filed as
issue #1112 with the three paths laid out; no path chosen.

## 6. C-23 — root CLAUDE.md corrected + CalendarFilterPane promoted to the public barrel

Verified the X19 finding directly: `src/solutions/CalendarSidePane/src/App.tsx` does import `CalendarSection` +
`CalendarFilterOutput` (not `CalendarFilterPane`), and `utils/parseParams.ts` + `utils/postMessage.ts` do
import the type `CalendarFilterPaneOutput` from the bare `@spaarke/events-components` specifier even though
neither `src/index.ts` nor `src/components/index.ts` re-exported it — confirmed by reading both barrel files.

Fixed by adding `CalendarFilterPane`'s exports to `Spaarke.Events.Components/src/components/index.ts` (flows
through to the root barrel via its existing `export * from './components'`). Did NOT re-export
`CalendarFilterPane`'s own `IEventDateInfo` type: `CalendarSection`'s barrel entry already exports a
same-named, different type from the same file, and neither live consumer needs `CalendarFilterPane`'s copy —
re-exporting it would be a duplicate-named-export compile error. Corrected the root `CLAUDE.md` row to state
the actual consumer accurately and to record the barrel fix.

**`.claude/CHANGELOG.md` entry NOT added by this agent** — `.claude/` is outside the sub-agent write boundary
(root CLAUDE.md §3). The exact entry text is included in the handback report to the main session, which owns
adding it per CLAUDE.md §18's "every PR touching this file MUST add an entry" rule.

## 7. Build/test verification — see the handback report for exact commands + output

All five touched npm packages (`Spaarke.UI.Components`, `Spaarke.SmartTodo.Components`,
`Spaarke.Compose.Components`, `Spaarke.Events.Components`, and the `src/dataverse/forms/sprk_matter` `tsc`
project) were built/tested directly rather than assumed green, per ADR-038 + this project's CLAUDE.md §5 rule
("a path that throws consistently is a defect to diagnose, never noise to tolerate"). Two packages
(`Spaarke.SmartTodo.Components`, `Spaarke.Compose.Components`) had no `node_modules` installed in this fresh
worktree; ran `npm install --legacy-peer-deps --no-audit --no-fund` per root CLAUDE.md §12 before testing.

No `.cs`/BFF files were touched by this task, so no `dotnet build`/`dotnet test` was run (the task's `<tools>`
section lists `dotnet` but none of the six hazards' fixes landed in `Sprk.Bff.Api` or any other .NET project).
