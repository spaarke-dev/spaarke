# Task 058 — The decision wizard: pre-start check, decisions, dependencies and known limits

> Date: 2026-10-14 · Branch `task/ontology-058-decision-wizard` (worktree `C:\wts-058`) · Base `origin/docs/ontology-platform-design`

## Step 0 declaration

- Rigor: FULL. The POML says FULL (primary user surface, carries most of v4's binding behaviour); the task modifies `.ts`/`.tsx` and has 6+ steps.
- Model tier: sonnet at effort xhigh (POML `<model-tier>` / `<effort>`). Step mode: directional.
- Hot-path: the task touches `src/solutions/SpaarkeAi/**` (one mount in `ThreePaneShell.tsx`). The registry row in `projects/INDEX.md` is main-session work and was not touched here.

## B.6 pre-start check (reconciliation section B.6; HANDOFF @ ae1cc9f)

Read before writing code: HANDOFF section 1.1 (all), section 1.4 L127-144, section 4.1 L218-226. Each checklist line was mapped to a place in the code and a test:

| HANDOFF line | Where it is built | Test |
|---|---|---|
| Steps come from data, the UI never switches on rule, lane or signal shape | `DecisionItemWizard` builds steps from `plan.actions` / `plan.nextSteps`; `ParameterFields` switches only on parameter `kind` | source-scan guard in `DecisionWizard.test.tsx` |
| Each action its own step, skippable; invalid required fields can be skipped, not taken | `ActionStepView`, `stepValid`, `isSkippable` | "the steps come from the plan" |
| Mutually excluding actions never both run: taking the earlier removes the later | `excludedActionCodes` (plan order) and `ShellSync` | "taking Approve variance removes Revise budget" |
| Skipping every action makes Confirm a dismissal, reason required, Other needs detail | `isDismissal`, `dismissalValid`, `ConfirmStep` | "skipping every action turns Confirm into a dismissal" |
| Next steps created only when recorded; not gated | `FollowOnGrid` collects values only; the commit route creates the records | "Path B review records once" |
| ‹ N of M › browses, no wrap, loses nothing typed | `DecisionWizard` per-item drafts in parent state; `nav` | "browse" |
| Decided item opens read-only with a status bar and the fact snapshot | `resolution` prop, `initialStepId`, `StatusBar` | "a decided item is read-only (replay)" |
| After recording the item flips in place, focus on the status bar | local `resolutionFromRecord`, `statusBarRef.focus()` | "Path B review records once" |
| Escape and backdrop never close; close asks when anything on ANY item is unrecorded | `dismiss="explicit"`, `hasUnrecordedChoices`, nested `ConfirmModal` | "Escape and the backdrop never close" |
| One review writes one Decision Record listing every offered action | `buildReview` posts every plan action once | "Path B review records once" |
| Record class derived, never chosen; shown on Confirm | `deriveRecordClass` | "Path B review records once", "Do item" |
| Also resolve only on Decide items, only ticked | `ConfirmStep` | "Also resolve is offered on a Decide item and not on a Do item" |
| Append-only record: no edit or delete affordance | read-only replay has no edit control | "a decided item is read-only (replay)" |
| Section 4.1: `nav`, `'skipped'`, `statusBar`, footer override, `stayOpenOnFinish`, `dismiss`, `onBeforeNavigate`, `initialStepId`, dynamic steps with `canonicalOrder` | all taken from task 056's props; nothing was added to `WizardShell` | |

No escalation trigger fired: every templated draft needs only data the plan and item already carry, and every v4 behaviour is built on `WizardShell` plus 056's props.

## Contracts read from code (not invented)

- Plan: `GET /api/v1/signals/{id}/decision-plan` (036). Suppression: `GET /api/v1/signals/{id}/suppression` (034). Commit: `POST /api/v1/signals/{id}/decision` (043).
- Commit is idempotent per `reviewId`. `retry: true` only resumes a first attempt that ended `Failed`. A failure carries ProblemDetails extensions (`reasonCode`, `correlationId`, `reviewId`, `actionCode`; for a failed commit also `failedStep`, `landed`, `possiblyLanded`, `steps`, `recordMayExist`, `sentButNotRecorded`, `resumable`, `replayed`). The 200 body's `status` is numeric.
- Lookup and Person values are GUID strings (Person is a contact). Money is a number.
- Injected fetch may THROW an `ApiError` (matched structurally by name, status and problemDetails) or RETURN a non-2xx `Response`; both become one `DecisionCommitError`. No answer at all is `httpStatus` 0, not resumable, and offers Try again with the same review id.

## Decisions and deviations

1. **Mount.** `DecisionWizardHost` is mounted once in `ThreePaneShell.tsx` after `InAppWizardHost`. The worklist widget (059 / 050) calls `launchDecisionWizard({ ...OpenItemEvent, loadItem, lookup })`. Nothing opens the wizard with `navigateTo` or `WizardModal`.
2. **Item reader and lookup are host-supplied.** The Signal read route (038) and the record read (045) do not exist yet, so the wizard takes a `loadItem` and a `lookup` from the caller. Without them it says the item cannot be read (it does not fail silently). The widget lane wires them.
3. **Step model on the stock engine.** All possible steps are passed as base steps; `ShellSync` (rendered inside every step) reads step statuses (completed = taken, skipped = skipped) and adds or removes steps to match the draft. Dynamic step configs keep the closure they were added with, so step contents read a React context and `canAdvance` reads refs.
4. **Removing the step just landed on.** Next on an action that excludes the step after it has already moved the shell onto that step, and the stock reducer steps back one when the current step is removed. `ShellSync` therefore calls `handle.nextStep()` after removing the current step. Found by the "taking Approve variance removes Revise budget" test, which fails on the earlier reconciler.
5. **Mutual exclusion is applied in plan order.** In Path B, taking Revise budget (earlier) removes Approve variance, so a review cannot both revise the budget and skip a visible Approve variance step; Confirm lists Approve variance as skipped, "Cannot be combined with Revise budget". The POML ui-test "take Send budget inquiry and Revise budget, Approve variance skipped" is satisfied this way: the record lists all three offered actions.
6. **Closed `ConfirmModal` is not left mounted.** The nested Discard check is rendered only while it is being asked. A closed Fluent dialog left mounted marked the open wizard surface aria-hidden under jsdom.
7. **`StatusBar` extended** (task 057's kit) with `recorded`, `reason`, `supersededBy` and `openItemsLeft`, as the POML notes ask. Four tests added in `ConsoleKit.test.tsx`.
8. **Preselect only for required Lookup parameters** (D-18: one budget is preselected). An optional lookup (outside firm) is never filled in for the user.
9. **Templated drafts (D-53)** are keyed by parameter code (`subject`, `body`, `title`, `description`) from the sentence, headline, rule short name and core record name. No AI call; a source-scan test asserts it.
10. **Dismissal keeps nothing else.** A Not filed item has no Also resolve and no other open items (D-34/35).

## Why the tests drive DOM events, not user-event

`DecisionWizard.test.tsx` uses `fireEvent` for clicks, typing and selects, like the other Wizard tests (`WizardShell.characterization.test.tsx`). With user-event, focus moves, and Fluent's focus manager (tabster) in jsdom blurs the focused field on a 100 ms timer (dropping keystrokes) and marks the dialog `aria-hidden`. A control with a plain `WizardShell` and one `Input` shows the same blur, so it is jsdom + tabster behaviour, not the wizard's. One query uses `hidden: true` (the re-opened nested Discard dialog) for the same reason; the first opening of that dialog is checked accessibly.

## Dependencies

- 038 (Signal read route) and 045 (record read): the wizard takes `loadItem`; the widget lane supplies it.
- 059 / 050 (worklist widget): will call `launchDecisionWizard`.
- Live wiring to 043 is the injected `authenticatedFetch` against the real route; a live seam run is the widget lane's step (no Dataverse writes here).

## Known limits

- Browsing to another item remounts the shell (a loading shell, then the item shell), so the modal flashes. Typed values are kept (they live in the parent's drafts).
- A replay needs the plan route as well as the record (it walks the plan's steps with the values sent).
- Picked Lookup and Person names are not stored in the record, so a replay after a reload shows "(selected record)". Filed as #1849.
- The catalog hint for `revise-budget.budget` reads "(D-18)" and "the user picks"; shown as the plan returns it. Filed as #1848.
- Tests log React act() warnings from the fake loader and lookup resolving after the assertion; they do not fail anything.

## Verification

- `npx jest src/components/DecisionWizard src/components/ConsoleKit`: 97 passed (31 draft/service tests, 31 wizard tests, ConsoleKit incl. 4 new StatusBar tests).
- Mutation check: the earlier `DecisionItemWizard.tsx` (without the current-step removal fix) fails "taking Approve variance removes Revise budget".
- `tsc --noEmit` in Spaarke.UI.Components: 9 errors, all pre-existing (unbuilt `@spaarke/auth` and `@spaarke/sdap-client`, `FileUploadService` implicit unknown); none in `DecisionWizard/` or `ConsoleKit/`. ESLint clean on both folders.
- `tsc --noEmit` in `src/solutions/SpaarkeAi`: no error mentions `DecisionWizard`; the output is dominated by unresolved modules in this worktree (for example `@fluentui/react-icons`), which predate this change. A production `vite build` of SpaarkeAi was not run on this loaded machine.
