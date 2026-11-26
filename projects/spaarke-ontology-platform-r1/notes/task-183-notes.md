# Task 183 notes: DocumentEmailWizard uses the shared EmailComposer

Branch `task/ontology-183-document-email-wizard-composer` (from origin/master 13a30da6d9). Rigor FULL, sonnet @ high, directional.

## What changed
- `DocumentEmailWizard.tsx`: Compose step = `SendEmailFollowOnStep` (182, compose-only, From shown). No `EmailStep` import. New prop
  `emailComposeHandlers` (host-injected `createXrmEmailComposeHandlers`). Email state is an `IComposedEmail`; Finish sends with
  `sendComposedEmail` (`archiveToSpe: false`, as before; association = parent record; `onResolveShareLink` from the handlers).
- Kept documents are the composer's attachment rows (`doc:{documentId}`, Attach/Link ticks). Step-1 toggles set those ticks; Remove drops the
  row and rewrites subject/body only while the user has not written their own (fixes a body listing a removed document).
- A failed send throws the composer's wording from `handleFinish`, so the wizard stays open with the composer filled in. No authenticatedFetch:
  visible warning in the step, Finish off, `handleFinish` throws.
- Hosts: SemanticSearchControl PCF (1.1.85 -> 1.1.86 in all 5 locations, bundle rebuilt with build:prod, copied to Solution) and
  DocumentRelationshipViewer App.tsx pass the handlers.

## Deviations
- `SemanticSearchControl.bulkDownload.test.tsx`: added one `jest.mock` for `dist/components/EmailComposer` (the host now imports the handler
  factory; unmocked it made ts-jest type-check the composer sources and the suite failed to run). No assertion changed.
- Body links: the wizard used to append a "Links:" block with Dataverse record links; now the composer appends "Linked documents:" with the
  Spaarke record link (`appname=sprk_MatterManagement`). A link the host cannot build refuses the send with the document name.
- With no client URL the link tick starts off (a relative link would be dead).
- PCF bundle 792 KB -> 1.07 MB: the composer (lexical) is now in the control. The skill's ~770 KB reference predates this; the build is minified.
- Dev check (both hosts) NOT run: needs the dev environment. Owner check listed in the report.

## Review (Step 9.5)
code-review + adr-check on the diff: no F-class finding. Known limits:
- K2: the 25 MB warning reads the step-1 toggle, not per-row ticks changed in the composer.
- K2: a document the user re-adds in the composer from another source can appear twice.
- K2: an unconfirmed send leaves Finish enabled; the message tells the user to check Sent Items first.
- K4: the composer's 401 wording says "send it from the record"; correct for the create wizards, slightly off here (wizard still open). Wording
  is the composer's, kept per the brief.
Class grep (old EmailStep importers): `grep -rn "EmailStep/" src --include=*.tsx` still lists other wizards; task 184 retires them.
client-lint ratchet: ui-components group ok; other groups FAILED on files this task does not touch (partial node_modules in this worktree,
"suppressions no longer occur"); no error names a file changed here.

## Verification
- composer test: 17 failed against the old wizard, 17 pass after. Touched folders: DocumentEmailWizard + EmailComposer + WizardFollowOns 391
  pass (before the wizard test file: 374); PCF jest 143 pass; tsc clean; ESLint clean on DocumentEmailWizard folder; DRV webpack build ok.

rework: none task 183

## Fix round 1 (independent review)
- F1: `SemanticSearchControl/webpack.config.js` now aliases `react/jsx-runtime$` and `react/jsx-dev-runtime$` to the PCF's own React 16.14 (copied from
  CommunicationActions). Rebuilt with build:prod; Solution `bundle.js` committed. Version stays 1.1.86 (never shipped). Bundle: 0 x
  `react.transitional.element`, 1 x `Symbol.for("react.element")`.
- F3: `Spaarke.UI.Components/src/utils/__tests__/pcfBundleReactRuntime.guard.test.ts` scans committed `src/client/pcf/*/Solution/**/bundle.js`
  (17 bundles); fails when the ControlManifest.xml beside it declares the React platform library and the bundle has the React 19 marker.
  Runs in CI as a step of the `xrm-capability-guard` job (ci-tier1-blocking.yml; its `xrm_guard` filter already covers src/client/**).
  Controls: fires on a seeded bundle, quiet on a clean one, quiet without a React platform-library declaration. Against the OLD committed
  SemanticSearchControl bundle it failed (2 tests); against the rebuilt one it passes. Baseline: empty (no other PCF bundle has the marker,
  TrackingFieldTrio included: 0 hits, `react.element` present).
- F2: `sendComposedEmail` option `retryLabel` (composer still open; label of the button that sends again). `hasRecord` follows `associations`.
  401: open -> "select {label} to send it"; closed with record -> "send it from the record" (unchanged); closed, no record -> "send the email
  again". "the record's Messages" / "may not appear on the record" only with a record. Callers: the create wizards, Work Assignment, Create
  Analysis, Summarize Files close after Finish (no retryLabel; Summarize has no record); DocumentEmailWizard stays open (retryLabel "Send",
  its Finish label). 7 new tests failed before, pass after.

## Known limits (owner-listed)
- An unconfirmed send leaves Finish enabled, so a double send is possible; the warning says to check Sent Items first.
- The 35 MB attachment total is checked only by the BFF.
