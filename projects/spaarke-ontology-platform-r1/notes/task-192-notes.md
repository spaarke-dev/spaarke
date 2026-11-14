# Task 192 notes (untracked; do not commit)

Rigor FULL, sonnet @ high, directional steps. Branch task/ontology-192-budget-inquiry-default-text from origin/master 13a30da6d9.

## Escalation trigger
Not fired: the inquiry code already reads the matter's name and number (`InquiryRecordFacts.LabelColumns`: sprk_mattername, sprk_matternumber). Same columns reused, read as the caller through the existing `IDataverseUserClient` in `DecisionParameterSources` (no new service, endpoint, DI or package).

## Deviation from the POML (one file)
`DecisionWizard/templatedDrafts.ts` is not in the POML file list but acceptance criterion 6 needs it: the wizard already fills Subject and Message from a generic client template (D-53, `TEMPLATES.subject/body`), and `defaultsFor` only honoured a server `defaultValue` for Choice/Boolean. Without the change the server default never reaches the composer and the client template ("Question: <core>") shows instead. Change: Text/TextArea join the kinds that take the server value, ahead of the template. EmailMessageFields.tsx needed no change (line breaks and compose-only behaviour are pinned by new tests).
Also: D-224's finding "Subject and Message start empty" was not quite true in the UI (they started with the generic template); the new default replaces it.

## Design
- Sources `budget-inquiry-subject` / `budget-inquiry-body` in DecisionParameterSources; resolved in ForPlanAsync for a matter core only, as the caller (matter GET, WhoAmI(), systemusers(id) fullname).
- send-budget-inquiry's own subject/body declare the DefaultSource via `with` on the shared MessageParameters; send-reminder unchanged.
- Failure: matter unreadable/denied/faulted/nameless -> no default, plan 200. Caller name unreadable -> body ends "Thank you,". Warning logs carry ids and error codes, never names.
- One-line normalisation of name/number/sender (whitespace runs -> one space) so a line break in a name cannot break the Subject or a message line. Only one of name/number present -> that one shown.
- Not reusing `InquiryRecordFacts.RecordLabel`: it truncates to 100 chars with an ellipsis (a footer column limit), which is wrong for an email body.
- D-96 comments reworded (SignalReadService ~1673, DecisionRouteCores ~103): comments only.

## Review (Step 9.5)
code-review and adr-check run on the diff. No F-class findings.
K-class (known limits):
- K4: plan read now costs up to three extra caller reads for the inquiry (matter name/number, WhoAmI, systemuser); WhoAmI repeats the one the access check makes. Small, sequential, never fails the read; no measured problem.
- K4: the default text names the matter regardless of its inherited access permission (Restricted/Limited), unlike the footer label (task 190), which withholds it from external recipients. Owner-approved wording, read as the caller, editable before send; flagged to the owner in the report.
- K4: pre-existing unused eslint-disable warning at templatedDrafts.ts:78 (not ours).
- Class grep (rule 6): no other `DefaultSource` consumers needed changing (only DecisionPlanEndpoints.cs:306 and DecisionParameterSources).

## Tests
Server (tests/unit/domain/Signals, run via tests/unit/Sprk.Bff.Api.Tests): new D224 tests in DecisionParameterSourcesTests (8 + theory x3), DecisionActionCatalogTests (3 catalog + 4 route-level); existing "no other action declares a default source" test narrowed, "plan with no sourced parameters" now uses send-reminder.
Fail-before: with the catalog declaration reverted, 9 of 12 D224 tests fail (3 are must-not-fire / failure-path tests that pass trivially).
Client (DecisionWizardEmailComposer.test.tsx): 6 new; with templatedDrafts.ts reverted, 2 fail (opens with default; commit untouched default), the rest are guard tests.

## Out-of-scope
None found. (`npm install` modifies tracked `.husky/_/*` files in the worktree; not committed.)

## BFF hygiene (CLAUDE.md section 10 + NFR-01)
- Placement: in the BFF, extending the existing DecisionParameterSources and catalog (task 167 DefaultSource mechanism). No new endpoint, DI registration, package or background work; no AI dependency. Component justification: Existing = MatterCurrencyDefault source mechanism; Extension = used (two more source names); Cost of doing nothing = D-224 default text cannot reach the wizard.
- Publish size, fresh builds from short-path worktrees: master 13a30da6d9 at C:\wt192m vs branch 6732812070 at C:\wt192b.
  Command `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish`, zip `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included (4).
  master 40.195 MB, branch 40.198 MB, delta +0.003 MB. File counts 191 / 191 (match).
- CVE: no csproj or package change; `dotnet list package --vulnerable` not re-run (nothing it could change).

## Verification runs
- BFF build: 0 warnings, 0 errors.
- Scoped dotnet test (tests/unit/Sprk.Bff.Api.Tests): DecisionParameterSourcesTests + DecisionActionCatalogTests 217 passed; plus related Signals classes (script lists, composer-and-link, DoLanePrivacy, executors, commit service, authoring endpoint, gate tier, plan service) 745 passed, 0 failed. Never the unfiltered suite.
- Spaarke.ArchTests (full): 2544 passed, 0 failed.
- Jest DecisionWizard folder: 11 suites, 218 passed (35 in the email composer suite, 6 new).
- tsc for Spaarke.UI.Components: no error in DecisionWizard or EmailComposer files (remaining errors are unresolved @spaarke/auth and @spaarke/sdap-client workspace packages in this checkout, unrelated).
- eslint on changed client files: 0 errors. client-lint ratchet: ui-components ok; the other packages report failures in files this task does not touch (checkout has only the ui-components deps installed, so types do not resolve).

rework: none task 192
