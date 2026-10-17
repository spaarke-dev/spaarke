# Task 102 - Ontology admin slice (b), UI: the rule-authoring wizard

Branch `task/ontology-102`, based on `origin/docs/ontology-platform-design` at `7c5b4628c` (task 102a merged as #2054 `a9ed6f41a`). Rigor FULL (multi-step authoring UI over admin write routes). Model Sonnet, effort xhigh (directional steps).

UI only. No BFF, Dataverse or deployment change. Contract: [`102a-authoring-contract.md`](102a-authoring-contract.md) (D-149 option A: every list comes from the BFF; on "New version" the wizard rebuilds the condition from the catalog) over task 101's routes ([`101-ontology-admin-authoring.md`](101-ontology-admin-authoring.md)).

## Pre-start check (B.6)

Checked against prototype `HANDOFF.md` @ `ae1cc9f` (`projects/2026-10-spaarke-console/HANDOFF.md`), as reconciliation B.6 requires for 100 / 102 / 103:

- **Section 1.3, `RuleWizard` row (L97):** "Write a rule = the same shell (section 8). Never a raw form over the rule body; never saved untested; a saved version is never edited." Built that way: the condition is composed from catalog controls (no free-text JSON; the only body text on screen is a read-only preview), Save and Publish are shut until the Test was run on exactly this draft, and "New version" always writes a new version (the page never edits in place).
- **Section 8 (L302), the rule-wizard row:** Basics, Condition, Wording, Actions, Scope and noise, Test, Save, in that order, on the same shell as the decision wizard (D-26). Each step refuses what the spec forbids: an invalid body cannot pass Condition (FR-08); a sentence naming a field the condition does not read cannot pass Wording (section 0.3, NFR-02); Save needs a test run; a new rule saves Off (FR-10); publishing supersedes the open items and re-raises those that still hold (FR-16), and the count is shown before it happens.
- **Where the real solution cannot do what v4 shows**, the difference is a deviation below. None needed an owner decision beyond what D-149 and 102a already recorded.

## What was built

`Spaarke.UI.Components/src/components/OntologyAdmin/RuleWizard/` (new):

| File | What it holds |
|---|---|
| `authoringService.ts` | The five routes (catalog, validate, dry run, save, publish) over an injected fetch; wire types; a failure is a result, never a throw, whether the fetch throws an `ApiError` or returns a non-2xx; both refusal shapes (validate's 200 body, the 400 ValidationProblem) become one `Record<field, messages[]>`, and a reason code the BFF names always puts its message on its field |
| `ruleDraft.ts` | The draft and the pure functions over it: the condition builder (the closed grammar, from controls only), the plan, the requests, `draftKey` (the Test is bound to it) |
| `wizardSteps.ts`, `stepCommon.tsx`, `ruleWizardTypes.ts` | Step list and field-to-step map; shared field wrapper that shows a BFF refusal; the wizard's two modes (new rule, new version) |
| `steps/*.tsx` | `BasicsStep`, `ConditionStep` (+ `FilterRowsEditor`), `WordingStep`, `ActionsStep`, `ScopeNoiseStep`, `TestStep`, `SaveStep` |
| `RuleWizard.tsx` | Loads the catalog, then `WizardShell` (modal, in-app, `dismiss` explicit, `uiScale`), the Next-asks-the-BFF footer, the test gate, the publish confirmation, the discard check |

Wired in: `Rules/RulesArea.tsx` (opens the wizard, reloads the list or page after a write), `Rules/RuleList.tsx` ("New rule"), `Rules/RulePage.tsx` ("New version", hidden for a retired or inactive rule), `OntologyAdmin/index.ts` and `RuleWizard/index.ts` (exports), `Spaarke.AI.Widgets/.../OntologyAdminWorkspaceWidget.tsx` (builds the authoring service from the session fetch and BFF base URL). The wizard is offered only when the host passes `authoringService`.

## How the behaviour is enforced

- **Test gates the end.** `draftKey(draft)` is the JSON of every field the admin chose (row ids stripped). A test result carries the key it ran for; it counts only while the key is unchanged. ANY later edit changes the key, so the counts are hidden, Next on the Test step is shut and Save / Publish are unreachable until the test is run again. A test the BFF could not record (`recorded: false`) does not pass either.
- **One serialisation.** The body, template and plan text are produced once per draft by `ruleDraft` and sent unchanged to validate, the dry run, save and publish. The dry run's proof is a hash of those exact strings, so a second serialiser would break it. A test asserts the four requests carry identical text.
- **Next asks the BFF.** Leaving Condition, Wording, Actions or Scope calls `validate` for the draft as it stands. The step is left only if the BFF accepted that step's own fields. A step shows only the refusals that were asked of it, so a later step's empty required field is not shown early. A refusal from the dry run, save or publish (400) is shown on the field and step it concerns, with a banner whose buttons lead to the step.
- **Wording offers only `templateEligibleFields`.** They come from the last `validate` answer for the current body (keyed by the body, so editing the message never discards them). A `{{token}}` typed by hand that is not on the list is flagged, and the BFF refuses it as well. The headline is not templated (D-23).
- **No code in both Actions and Next steps.** A code is shown once, with a role (Action, Next step, Not used) taken from the lists the catalog gives it; `assign-work`-style codes that appear in both lists get one row with both roles. The catalog's `excludes` are shown on each row and enforced (a code that conflicts with a chosen one cannot be newly chosen). The order of the actions is the admin's; the first is the recommendation.
- **Publish asks first.** The confirmation (`ConfirmModal`) shows `wouldSupersede` and `wouldReraise` from the current dry run. "Not yet" changes nothing.
- **In-app, explicit dismiss.** `WizardShell` modal mode (SprkModal). Escape and the backdrop never close it. Cancel or the close control with unsaved work asks first ("Discard this rule?"). No `navigateTo`, no `WizardModal`.
- **After a write whose outcome is unknown** (5xx, no answer, or a policy created Off whose first version could not be made current) the wizard sends nothing more and says so.

## Deviations and decisions (for the main session to confirm)

1. **Scope is shown, not authored.** The save route (task 101) takes no tenant or matter, so a new rule is saved without a scope limit and a new version keeps the rule's. The Scope step says so and shows the existing scope read-only. HANDOFF section 8 lists "applies to" as authored. This is a contract gap, not something the closed acceptance criteria need, so it is not an escalation; a follow-up needs a route change first (BFF).
2. **No confidence threshold or calibration suggestion** (HANDOFF section 8, Scope & noise). Nothing in the 102a contract or task 101 carries it.
3. **Condition is previewed in formal form only.** HANDOFF shows it "in words and as the formal filter". The BFF describes a stored rule (the page does), but exposes no describer for a draft, and the wizard must not invent one. The read-only preview is the body as it will be written.
4. **No live preview of the decision wizard's sidebar** on the Actions step (HANDOFF section 8). It is a rendering of the plan the admin has chosen; not needed for any acceptance criterion.
5. **New version: wording and plan are prefilled, the condition is empty.** The BFF returns no raw rule body (D-149), so the condition is rebuilt from the catalog. Wording, plan and the quiet window come from the rule's page; the admin can change them. Priority and "counts toward muting" belong to the rule and are shown read-only.
6. **Names and shapes the client holds, because the catalog has no field for them.** Each is a closed list the wizard cannot read from the catalog:
   - the clause kinds `exists` / `notExists` (`ConditionStep.tsx`, the "has records" / "has none" choice);
   - the shapes of a test on a column and their value types: "is one of", "is within a window", the null tests, and the value types text / number / yes or no / date (`FilterRowsEditor.tsx`). The operators themselves, the null-test members and the relative-date forms come from the catalog;
   - the operator label maps (`OPERATOR_WORDS`, `NULL_TEST_WORDS` in `FilterRowsEditor.tsx`): words for the catalog's symbols and members, falling back to the member itself;
   - the rule type `Threshold` (its body has a different shape: one comparison and no clauses; every other catalog rule type is composed as Existence);
   - the lane `Decide` (the lane whose dismissed items wait out a quiet window; the other lane re-raises when the item's date changes, FR-17a / D-13). Consequence to know: if the lane were ever renamed in the catalog, a Decide rule's quiet window would silently stop being carried onto a new version (`ruleWizardTypes.ts`, `initialDraftOf`) and written into the body (`QUIET_WINDOW_LANE`).

   None of these is a list the admin chooses from. **Follow-up: a catalog flag for each (BFF) removes the client-side names; the main session files the issue.**
7. **Next is enabled on Wording and Actions and the BFF explains a refusal**, rather than disabling Next on locally-guessed rules. Local gating is limited to inputs that cannot be written at all (no column chosen, empty value, not a number), the Basics identity (code, name, lane) and the priority / quiet-window number format.
8. **The Test step is the only place a test is run**; the Save step shows a one-line summary of the last test (counts), not the full table.

## Tests

`RuleWizard/__tests__/` (5 files):

- `authoringService.test.ts`: routes, verbs and bodies; thrown and returned failures read the same; both refusal shapes give the same field errors; a reason code with no listed message still puts its message on its field; 409 messages; unknown outcome.
- `ruleDraft.test.ts`: the body for Existence and Threshold; `=` as a bare scalar; lists, windows, null tests, triage ids; quiet window for the Decide lane only; one serialisation for all four requests; `draftKey` changes on every kind of edit.
- `RuleWizard.test.tsx` (35): catalog-only lists; no free-text JSON; refusals on the field and step (200 and 400 shapes); wording offers only the eligible fields; actions / excludes / no code in both; the test gate and staleness; save, publish and the confirmation counts; unknown outcome lock; Escape, backdrop and discard; new version prefill and empty condition.
- `RulesAreaWizard.test.tsx`: New rule / New version only with an authoring service; not for a retired or inactive rule; the list is reloaded after a write.
- `ruleWizardSources.test.ts`: tokens only; no `navigateTo` / Xrm / `WizardModal`; no action, dismissal-reason, severity, work-type or column literal in the sources (each guard has a control that proves it fires).

Fail-before / pass-after was shown by mutation: each of the following broken one at a time made the named tests fail, then restored: staleness ignoring the draft key, the Test step not gated, `dismiss="light"`, wording offering a non-eligible field, a reason code's message dropped, a code kept in both lists, publish without asking, later-step refusals shown early, a 400's field errors not placed. The widget wiring test fails without the widget change (2 of 4).

## Not verified here

The task's three browser UI tests could not be run in this environment (no browser): **Test gates publish** (seen in a real page), **Invalid body cannot pass**, **Dark mode (ADR-021)**. The logic behind the first two is covered by jest (above); the wizard renders under `webDarkTheme` in jest as a smoke check only. They stay for UAT (task 105).

## Found, not fixed (D-106)

- `Spaarke.UI.Components` `tsc` reports TS2307 for `@spaarke/auth` and `@spaarke/sdap-client` (and TS18046 in `FileUploadService.ts`) in this worktree: the workspace packages are not built. Files are untouched by this task.
- `client-lint check --package ai-widgets` fails on `PinnedMemoryEditDialog.tsx` (463:10, no-unnecessary-condition) and reports stale suppressions in this environment; neither file is touched here. `ui-components` passes.
- jsdom: Fluent's focus manager marks a SprkModal that opens over a mounted page `aria-hidden`; the wizard's tests that mount the page behind it query with `hidden: true`.

## Fix round 1 (PR #2066 review)

| Finding | Fix | Pinned by |
|---|---|---|
| F1-a Close during or after a write | `handleClose` ignores the close while a save or publish is out (a `writing` flag around `onFinish`). With a write lock it closes without the discard question and calls `onClose({ kind: 'unknown', policyId, versionNumber: null })`; `RuleWizardResult` gained the `unknown` kind, so `RulesArea` reloads the list or page for it. | `RuleWizard.test.tsx`: "closes without 'nothing has been saved' after a write lock...", "hands the host the policy a failed save left Off", "ignores the close control while a save is out"; `RulesAreaWizard.test.tsx`: "a save whose outcome is unknown reloads the list..." |
| F1-b Stale hidden filter values | A row's `value`, `listText` and `ids` are reset when its column changes (`FilterRowsEditor.tsx`); `ruleDraft.filterOf` uses `ids` only for the triage column of the triage table. | `ruleDraft.test.ts`: "sends chosen ids only for the triage column..."; `RuleWizard.test.tsx`: "forgets the values chosen for a column...", "starts the value and the list empty when the column changes" |
| F1-c A refusal shown two or three times | `failWrite` returns when it has set the banner or the write lock; it throws (the shell's error bar) only when neither is set. | `RuleWizard.test.tsx`: "shows a refused save (400) once...", control "shows a refusal that has no field once..." |
| K3 Scoped-rule publish counts | The publish confirmation adds "These counts are worked out without the rule's scope, so once it is published they may be lower." when the rule has a tenant or matter. The BFF dry run ignoring scope is filed separately. | `RuleWizard.test.tsx`: "says on the confirmation that a scoped rule's counts...", control "does not say so for a rule with no scope" |
| K1 Expired dry-run proof | A 409 `dry_run_required` from save or publish makes the test not current and flags it `expired`; the Test step says "The proof of the last test has expired. Run the test again." and Next / Save stay shut. | `RuleWizard.test.tsx`: "marks the test stale when the BFF no longer holds its proof..." |
| K2 Notes | Deviation 6 lists every client-side code; follow-up named. | (this file) |

Fail-before / pass-after: with the four changed source files (`RuleWizard.tsx`, `ruleDraft.ts`, `FilterRowsEditor.tsx`, `TestStep.tsx`) put back to the head before this round and the new tests kept, 9 tests failed (every new test above except "starts the value and the list empty when the column changes", added afterwards and shown failing on its own below, and the controls, which pass either way); restored, all 183 OntologyAdmin tests pass. The two halves of F1-b were also shown apart: with only `FilterRowsEditor.tsx` reverted, "starts the value and the list empty when the column changes" fails and the others pass; with only `ruleDraft.ts` reverted, the `ruleDraft.test.ts` case fails.

## For UAT (task 105)

- After a confirmation dialog closes (the discard check, the publish check), check keyboard and screen-reader access to the wizard behind it: focus returns into the wizard and nothing in it stays hidden from a screen reader. In jsdom an ancestor of the wizard stayed `aria-hidden` after a nested confirmation closed (the tests query with `hidden: true`); that may be a jsdom artefact of Fluent's focus manager, which only a browser can settle.
