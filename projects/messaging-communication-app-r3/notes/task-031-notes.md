# Task 031 notes — SpaarkeAI workspace widget (FR-14a)

## Summary

Upgraded `CommunicationsWorkspaceWidget` (`@spaarke/communication-components`) IN PLACE: its body now
mounts the shared two-pane conversation shell (`<ConversationWorkspace>` + `<ConversationView>`, tasks
011/012) in record-less/all-mode (no `regarding` prop), replacing the Pattern D filter-chip/card-strip/
`<DataGrid>` body shipped by `messaging-communication-app-r2` task 030. The registered identity is
UNCHANGED: widget type string `communications-list`, section id `communications` (NFR-06) — verified by
new registry tests, not just asserted in a comment.

## SpaarkeAi hot-path coordination

- **Dual-use consequence (read before the deploy PR, task 034):** `CommunicationsWorkspaceWidget` is
  consumed from TWO places (Pattern D dual-use):
  1. `Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` — Direct workspace widget,
     type `communications-list` (untouched — still does `import('@spaarke/communication-components')`).
  2. `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` — Dashboard-wrapper
     section, id `communications`, which calls
     `React.createElement(CommunicationsWorkspaceWidget, { configId: effectiveConfigId })`.

  Because both call sites mount the SAME component, upgrading the widget body upgrades BOTH surfaces —
  the LegalWorkspace "Communications" dashboard section will now ALSO render the two-pane conversation
  instead of the channel-filter/DataGrid view it showed before this task. This is the correct read of the
  task's goal ("the workspace surfaces conversations through the same shared component as the other
  surfaces") but is a real behavior change on a file OUTSIDE this task's boundary
  (`communications.registration.ts`), which I did NOT edit per the assigned scope ("touch ONLY the
  CommunicationsWorkspaceWidget files + the WorkspaceWidgetRegistry test/entry"). No code change was
  needed there for it to keep compiling (see next point), but the **behavioral** consequence should be
  called out explicitly in the task 034 deploy PR description and/or `plan.md`.

- **`configId` prop kept, now unused.** `communications.registration.ts` passes
  `{ configId: effectiveConfigId }` to the widget. Rather than touch that file, `CommunicationsWorkspaceWidgetProps.configId?`
  was RETAINED (marked `@deprecated` in JSDoc) purely so that call site keeps compiling/rendering
  unchanged. The GUID it carries (`e1826c4c-9575-f111-ab0e-7ced8ddc4a05`, "Active Communications
  (Workspace)") no longer configures anything — the DataGrid it fed was removed. A future cleanup task
  should remove `configId` from BOTH `CommunicationsWorkspaceWidgetProps` and the
  `communications.registration.ts` call site together (filed as a candidate for
  `/project-defer-issue-tracking`, not done here — out of this task's boundary).

- **`onCreateThread` intentionally NOT wired.** The widget mounts `<ConversationWorkspace>` without an
  `onCreateThread` callback, so the "+ New" affordance renders disabled. Wiring `NewThreadModal` (task
  024) into this workspace mount site was not in this task's acceptance criteria (which scoped the swap
  to "thread list + ConversationView", auth, and dark mode) — flagging this as a likely near-term
  follow-up rather than silently shipping a disabled affordance without a paper trail.

- **Shared-lib republish:** `@spaarke/ui-components`'s `dist/` was rebuilt (`npm run build`) as part of
  this task — it was stale (predated tasks 011/012, so it had no `ConversationWorkspace`/`ConversationView`
  exports). `dist/` is gitignored so this isn't a tracked change, but any other in-flight worktree/agent
  consuming `@spaarke/ui-components` via its pre-built `dist/` (not source-aliased) should rebuild locally
  too. `@spaarke/auth`'s `dist/` was also rebuilt (was fresh already; rebuilt defensively).

## Test infrastructure added (net-new for this package)

`@spaarke/communication-components` had NO test runner configured before this task (its one test file's
own docblock said "checked in for the future test runner setup"). Added, mirroring
`@spaarke/daily-briefing-components`'s established Jest pattern:
- `package.json`: `@spaarke/auth` dependency + Jest/testing-library devDependencies + `test`/`test:watch`/
  `test:coverage`/`test:ci` scripts.
- `tsconfig.json`: added a `@spaarke/auth` → `../Spaarke.Auth/dist/index.d.ts` paths mapping (mirrors the
  existing `@spaarke/ui-components` mapping) — no `node_modules` install needed for type-checking.
- `tsconfig.test.json` (new) — ts-jest config variant (non-declaration-only, `jsx: react-jsx`, jest types).
- `jest.config.cjs` (new) — `testEnvironment: jsdom`, `@spaarke/ui-components` mapped to SOURCE (its
  `dist/` ships pure ESM that ts-jest's CJS transform can't consume from `node_modules`), plus the same
  `d3-force`/`marked`/`@spaarke/sdap-client` transitive-dep stubs and React-dedupe mappings
  `@spaarke/ai-widgets/jest.config.ts` already uses for the identical `@spaarke/ui-components` barrel
  problem (`src/__mocks__/d3-force.ts`, `marked.ts`, `sdap-client.ts` copied verbatim from that package).
  `@spaarke/auth` is intentionally NOT globally mapped — the one test file mocks it inline via
  `jest.mock('@spaarke/auth', ...)`.
- Ran `npm install` in both `Spaarke.Communication.Components` and `Spaarke.AI.Widgets` (neither had
  `node_modules` present before this task).

## Registry test cross-package rendering limitation (documented, not a defect)

The new `communications-list` identity describe block in `Spaarke.AI.Widgets`'s
`register-workspace-widgets.test.ts` asserts registration presence/uniqueness/metadata and resolves the
REAL widget export (identity equality + `displayName` check), but does NOT do a full DOM `render()` of
the resolved component from within `Spaarke.AI.Widgets`'s own test run. Attempting that trips React's
single-instance invariant ("Invalid hook call") because `@spaarke/communication-components` is mapped to
SOURCE there too, and that source pulls in `@griffel/react`/Fluent from `Spaarke.Communication.Components`'s
OWN (separately installed) `node_modules`, producing two physical React instances across the package
boundary. Full render-mount coverage of the upgraded body (asserting `<ConversationWorkspace>`'s thread-list
chrome actually mounts, incl. record-less/all-mode FR-16 endpoint call, dark theme, deprecated `configId`
back-compat) lives in `CommunicationsWorkspaceWidget.test.ts` itself, against that package's own React
instance — this is the authoritative "the shared widget mounts" evidence for acceptance criterion 1.

## Escalation / deviation

None fired. No change to the registered type string, section id, or the widget's exported
name/importability was required — `configId` was retained (not removed) specifically to avoid the
"changing props contract" trigger in the task's `<escalation>` block.
