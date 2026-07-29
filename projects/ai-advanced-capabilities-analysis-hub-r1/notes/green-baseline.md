# Green-Baseline Record — Task 001 (Fix 12 pre-existing e2e failures)

> Phase 0 Foundation gate (W0). Per spec FR-01 / PLAN Phase 0: nothing downstream (Phase 1+ three-pane
> work) starts until this record shows all 12 named failures fixed with no new regressions.
> Date: 2026-07-28.

## Scope

Three suites, run via `npx jest` from `src/solutions/SpaarkeAi/`:

- `src/components/conversation/__tests__/ConversationPane.compose-revise-document-session-routing.e2e.test.tsx`
- `src/components/conversation/__tests__/ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`
- `src/components/conversation/__tests__/ConversationPane.compose-edit-controls.test.tsx`
- `src/components/shell/__tests__/three-pane-compose-coordination.e2e.test.tsx`

(4 files map to the 3 named suites in the task POML — "compose-session-routing" spans the first two files.)

## Before (captured prior to any fix)

Command: `npx jest --testPathPatterns="ConversationPane.compose-(revise-document-session-routing|draft-alternative-session-routing|edit-controls)|three-pane-compose-coordination"`

```
Test Suites: 4 failed, 4 total
Tests:       12 failed, 3 passed, 15 total
```

12 failing tests, exactly matching the task's stated count:

| # | File | Test | Error |
|---|---|---|---|
| 1 | ConversationPane.compose-revise-document-session-routing.e2e.test.tsx | `DEF-11 ... dispatches the multi-change edits[] output ...` | `toContain` expected exact `COMPOSE_WHOLE_DOCUMENT_EDIT_CONFIRMATION`; actual content had `\n\n**What I changed:** ...` appended |
| 2 | ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx | `DEF-09 ... dispatches to the DOCUMENT session ...` | same shape — exact-match on `COMPOSE_EDIT_CONFIRMATION` failed due to appended explanation |
| 3 | ConversationPane.compose-edit-controls.test.tsx | `DEF-12 ... injects a summary-only confirmation ...` | same shape; additionally asserted `not.toContain(RATIONALE)`, which is now false |
| 4–12 | three-pane-compose-coordination.e2e.test.tsx | all 9 tests (Flow 1, 6, 2, 4, 3, 5, 7, Flow-6-citation, cross-pane-isolation) | `useAiSession must be used within an AiSessionProvider` thrown by `ComposeTraceHost` on mount |

## Root-cause classification (per test, per task step 1)

### Cluster A — 3 tests: stale assertion vs. current, correct product contract (classification b)

`ConversationPane.tsx` `dispatchComposeAction` (lines ~941-947) appends a `**What I changed:** {explanation}`
suffix to the base compose-edit confirmation, sourced via `extractComposeEditExplanation(dispatched.result)`
(`composeResultFormat.ts:172`). The code comment cites the origin: **"UAT round-8 #7 — the reviewer asked for
a Copilot-style explanation of WHAT/WHY changed (the summary-only confirmation gave no detail)."** This is a
deliberate, reviewer-approved product change, independently unit-tested in
`composeResultFormat.test.ts` (`describe('extractComposeEditExplanation (UAT round-8 #7 ...)')`, 3 passing
assertions pre-dating this task). It is NOT a regression — it is a shipped feature the 3 failing tests
pre-date.

The three tests still asserted the pre-UAT-round-8-#7 contract (bare confirmation string, and — in the
DEF-12 test — an explicit `not.toContain(RATIONALE)` that the reviewed feature now contradicts by design).

**Fix (test-only)**: updated the 3 test files to assert the current, correct contract — the confirmation
equals `${BASE_CONFIRMATION}\n\n**What I changed:** ${rationale}` when the dispatched ledger payload carries
a `rationale`/`summary` field — while KEEPING the still-valid invariant that the raw proposed redline text
(`NEW_TEXT`/`EDIT_TEXTS`) is never duplicated into the message. No test was weakened: the DEF-12 test now
explicitly asserts the explanation text is present (`toContain('**What I changed:**')` +
`toContain(RATIONALE)`) rather than silently dropping the assertion.

Files: `ConversationPane.compose-edit-controls.test.tsx`, `ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`, `ConversationPane.compose-revise-document-session-routing.e2e.test.tsx`.

### Cluster B — 9 tests: test-harness/mock drift vs. an accepted architecture default change (classification a)

`ContextPaneController.tsx` (`decision-1`, 2026-07-19 code comment): the Context pane's **resting default
view** changed from the retired "quick-start" `GetStartedCardsWidget` to `execution-trace`
(`<ComposeTraceHost/>`). `ComposeTraceHost` calls `useAiSession()` unconditionally on mount (its own docblock:
*"Must be rendered inside an `AiSessionProvider`... In the SpaarkeAi shell that is always true — the whole
three-pane tree is wrapped by `AiSessionProvider` (see `ThreePaneShell`)."*). Verified in
`ThreePaneShell.tsx`'s provider-tree docblock and JSX: `PaneEventBusProvider` → `AiSessionProvider` →
`ShellStageManager` → panes — the real running shell always supplies this provider.

`three-pane-compose-coordination.e2e.test.tsx`'s harness mounts `<ContextPaneController />` directly under
only a `PaneEventBusProvider` (no `AiSessionProvider`) — this was safe when the pane's default view had no
session dependency, but became stale once `decision-1` changed the default. This is a test-harness gap, not
a product regression: the shipped app is never in this state.

**Fix (test-only)**: added the SAME `jest.mock('@spaarke/ai-widgets', ...)` override-only-`useAiSession`
pattern already used by the 3 `ConversationPane.compose-*-session-routing` sibling suites (`...actual` spread
keeps `PaneEventBus`/`PaneEventBusProvider`/everything else REAL — the file's stated forcing-function
invariant, "a REAL PaneEventBus, not a mocked bus," is unaffected; none of the 9 tests assert anything about
session state). `bffBaseUrl` is set falsy so `ComposeTraceHost`'s `restoreTrace` soft-fails to an empty trace
via its own documented fallback, with no fetch mock required.

File: `three-pane-compose-coordination.e2e.test.tsx`.

### No genuine product regressions found

All 12 failures traced to test-infra drift (stale assertions or a missing harness provider), each against a
documented, deliberate, already-reviewed product decision (UAT round-8 #7; decision-1 2026-07-19). The
escalation trigger in task 001's POML ("if any failure traces to a genuine product regression... STOP and
escalate") did NOT fire — no escalation was raised.

## Independence confirmation (task step / constraint: "confirm independence")

- None of the 4 touched test files, nor `ConversationPane.tsx`, `ContextPaneController.tsx`,
  `ComposeTraceHost.tsx`, `ThreePaneShell.tsx`, or `composeResultFormat.ts`, were modified by, or reference,
  any not-yet-written `ai-advanced-capabilities-analysis-hub-r1` work (no `sprk_analysis`, no fork endpoint,
  no hub widget, no wizard code exists yet on this branch per TASK-INDEX — tasks 010+ are all still 🔲).
- Both root causes predate this project: UAT round-8 #7 and decision-1 (2026-07-19) are dated before this
  project's Step 3 task decomposition (2026-07-28) and are unrelated in subject matter (Compose UAT
  copy-polish; Context-pane default-view change) to the Analysis-hub spine/session/widget/wizard work this
  project adds.
- Confirmed the failures pre-exist independent of this project by running the full SpaarkeAi suite (see
  After section) — no other suite depends on or was touched to produce this fix.

## After (post-fix)

Command (targeted 4 files):
```
Test Suites: 4 passed, 4 total
Tests:       15 passed, 15 total
```
All 12 previously-failing tests now pass; the 3 previously-passing tests in the same files still pass (no
regression within scope).

Full-repo regression check — command: `npx jest` (all 76 suites, `src/solutions/SpaarkeAi/`):
```
Test Suites: 76 passed, 76 total
Tests:       673 passed, 673 total
```
No previously-green test anywhere in the SpaarkeAi Jest portfolio regressed to failing.

## Diff scope verification (negative acceptance criterion)

Only test files were modified — no product behavior changed:

- `src/components/conversation/__tests__/ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`
- `src/components/conversation/__tests__/ConversationPane.compose-edit-controls.test.tsx`
- `src/components/conversation/__tests__/ConversationPane.compose-revise-document-session-routing.e2e.test.tsx`
- `src/components/shell/__tests__/three-pane-compose-coordination.e2e.test.tsx`

No file under `ConversationPane.tsx`, `ContextPaneController.tsx`, `ComposeTraceHost.tsx`,
`ThreePaneShell.tsx`, or `composeResultFormat.ts` was edited. No test was deleted or `.skip`/`xit`-marked.
No assertion was weakened to a no-op (`not.toThrow()`, removed expectation, etc.) — every changed assertion
was replaced with an equally- or more-specific check of the current, correct contract.

(Note: `src/solutions/SpaarkeAi/package-lock.json` also shows as modified in `git status` — this is the
side effect of `npm install --legacy-peer-deps --no-audit --no-fund` needed to materialize `node_modules`/
the `jest` binary before tests could run at all in this worktree; per root CLAUDE.md §12 this is the
prescribed install command for Vite solutions. No dependency version was deliberately changed.)

## Escalation

None raised. All 12 failures classified as test-infra drift (a/b), not genuine product regressions (c).
