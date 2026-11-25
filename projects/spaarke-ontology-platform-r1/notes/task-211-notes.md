# Task 211 notes - Decision wizard person picker and Confirm summary show the email (#3062, DI-170)

Branch `work/ontology-211-picker-email` from origin/master `e70ff38894`. Rigor FULL, sonnet @ high, directional steps.

## Findings (step 1)

- `ILookupItem` already had a first-class `email?` (recipient pickers resolve to it) - it is not display text for `LookupField`,
  so showing it there would change every other consumer. A new optional `secondaryText?` carries the display line instead.
- Where each host's Person candidate gets its email:
  - MCP widget inline type-ahead (`src/client/widgets/spaarke-decisions/src/wizard/lookupTypeAhead.ts`): bare `name` + `email` (the
    server's `DecisionLookupCandidate.Email`). This is the case the issue reports. No change needed there.
  - Console inline fallback (`searchContactsAsLookup`): `name` is already "Full Name (email)" and `email` is set. A second line
    would show the email twice, so `withEmailLine` skips a candidate whose name already contains its email.
  - Console side pane (`createWorklistOpenLookup`, Person pick): `{ id, name, email }` when the host passes a Web API source
    (`WorklistWorkspaceWidget` does). The pick previously dropped the email from the draft label; it now reads "Name (email)".
- Console side-pane person lookup: it is the platform's own "Lookup Records" pane (`Xrm.Utility.lookupObjects` via
  `INavigationService.openLookup`, scoped by `filters`, no `viewId`). What columns it lists is the contact lookup view's setting in
  Dataverse, not code in this repo; I did not open a browser, so whether the dev view lists Email is NOT verified here. The wizard
  side of it is covered: after a pane pick the field and the Confirm summary read "Name (email)" from the email the host returns.
  Main session: check the pane's columns on dev; if Email is missing from the contact lookup view that is a Dataverse view edit.
- Labels (`draft.labels`) are display-only: the commit builds from `values` (ids), so the payload is unchanged (asserted).
- Open PRs touching the same files: none found with `gh pr list` (LookupField.tsx, LookupTypes.ts, ParameterFields.tsx, decisionDraft.ts).

## Change

- `types/LookupTypes.ts`: optional `secondaryText`.
- `components/LookupField/LookupField.tsx`: option renders name + secondary line (Fluent tokens, `colorNeutralForeground3`) only when
  `secondaryText` is set; otherwise the original single `<Text>` (same DOM).
- `components/DecisionWizard/decisionDraft.ts`: `pickLabel` and `withLookup` store "Name (secondary)" as the draft label.
- `components/DecisionWizard/steps/ParameterFields.tsx`: Person search results get their email as the second line
  (`withEmailLine`); the side-pane pick passes the host's email.

## Tests

- New `LookupField/__tests__/LookupField.secondaryText.test.tsx` (5) and `DecisionWizard/__tests__/DecisionWizard.personEmail.test.tsx`
  (7). On origin/master source (tests only): 1 of 5 and 4 of 7 fail (the secondary line, the field/Confirm "Name (email)", the pane
  pick); the rest are must-not-fire controls (no email, name already has email, no secondary text, keyboard/mouse pick) and pass
  both before and after, as intended.
- After: DecisionWizard + LookupField suites 25 suites / 341 tests pass. Whole `Spaarke.UI.Components` jest: 480 of 482 suites pass;
  `InquiryDispositionPicker` and `ConversationWorkspace.messages` failed in the full parallel run only (timeouts under load) and
  pass alone (54/54); neither uses LookupField.

## Known limits

- K4: a Person whose name contains the email as a substring but is a different string gets no second line; no failure scenario.
- Visual check (light/dark) is not done here: the main session checks the picker on dev after the Console auto-deploy.

rework: none task 211
