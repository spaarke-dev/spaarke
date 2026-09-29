# Task 024 (toolbar slot auto-hide + sprk_agreement) — deviations from the POML plan

> Task: `projects/record-header-and-notepad-r2/tasks/024-toolbar-slot-autohide-and-agreement.poml`
> Status: implementation complete, targeted quality gates clean; see caveat in §2 on the shared-lib-wide test run.

None of these are scope changes — both are within-step engineering decisions the directional `<steps>` mode
permits, documented here per step 7.

## 1. Two pre-existing test rewrites beyond the POML's literal ask

The POML's step 4 said "extend `useRecordHeaderToolbarActions.test.ts` with the closed slot matrix." Doing so
surfaced that two *existing* tests asserted the pre-FR-16 behavior (null filter → badge idles at 0, slot still
renders) for entities that the FR-16 change now omits entirely:

- `'checkmark badge = 0 for an UNSUPPORTED parent (playbook is not in SUPPORTED_TODO_PARENTS)'` — `sprk_playbook`
  is unsupported in both maps, so post-change the checkmark slot doesn't exist at all; `checkmark?.badge` would
  be `undefined`, not `0`. Rewrote to assert `checkmark` itself is `undefined` (renamed to reflect FR-16).
- `'annotation badge = 0 for an UNSUPPORTED parent (sprk_document not in SUPPORTED_MEMO_PARENTS)'` — same
  issue for the annotation slot. `sprk_document` remains supported for To Do, so this is also now the FR-16
  auto-hide worked example (To-Do-but-not-Memo) alongside the new `contact` case; rewrote to assert `annotation`
  is `undefined` and that the slot list is exactly `['checkmark']`.

Leaving these two unmodified would have made them fail against the new (correct, spec-mandated) behavior — they
were testing the exact trap FR-16 closes. No other existing test needed adjustment.

## 2. Full shared-lib test suite has pre-existing failures unrelated to this task's scope

The acceptance criteria ask for "shared-lib build and full test suite green." `npm run build` is clean (0
errors). The two target test files are fully green (`toolbarLaunchDefaults.test.ts` and
`useRecordHeaderToolbarActions.test.ts`, run in isolation: 37/37 passing after this task's additions, modulo two
already-failing, already-scoped-out cases below).

Running the *entire* shared-lib suite (`npx jest`, no path filter) reports **12 failed suites / 24 failed tests
/ 2759 passed / 2783 total**. Spot-checked two of the failing suites
(`ConversationView.emailInFlow.test.tsx`, `AccessGrantModal.test.tsx`) — both fail on `waitFor`/timeout
assertions unrelated to toolbar/regarding-filter logic, and neither imports `toolbarLaunchDefaults` or
`useRecordHeaderToolbarActions` (confirmed by grep). Multiple other agents (015, 021, 040) are editing this same
shared-lib worktree concurrently per the orchestrator's brief, which is the most likely source. Not something
this task's file scope (`hooks/useRecordHeaderToolbarActions*`, `hooks/toolbarLaunchDefaults.ts`) can fix or is
responsible for — flagged rather than silently claimed clean.

Separately, `toolbarLaunchDefaults.test.ts` (in isolation) has **two pre-existing failures that predate this
task**, confirmed by running the suite before any edits: `NOTEPAD_MODAL` asserts stale 70%×80% sizing (actual
constant is 25%×35%, per the v1.0.7 comment already in the source) and `NOTEPAD_WEBRESOURCE_NAME` asserts the
stale `sprk_notepad_page` name (actual constant is `sprk_notepad`). Both assertions are inside the code this
task's constraints (NFR-07) explicitly forbid touching (`buildNotepadLaunchData`, `NOTEPAD_MODAL`,
`NOTEPAD_WEBRESOURCE_NAME`) — left untouched, not counted against this task's acceptance criterion 7 (which
scopes "byte-identical" to those exact symbols, which they are — `git diff` shows zero changed lines touching
them).
