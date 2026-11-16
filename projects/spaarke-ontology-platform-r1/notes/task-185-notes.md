# Task 185 notes: deploy the create-wizard lookup and composer set to dev

**Status (2026-11-15):** deployed and read back. The owner's dev checks (`notes/dev-test-checklist-185.md`) are pending. The task closes when they pass.

## What was deployed
The deploy log has the entry, the hashes and the D-126 clobber check (`notes/deploy-log.md`, 2026-11-15).
- **Source:** master `acc96e1927` in detached worktree `C:\wtdep185`.
- **Console:** auto-deploy `f7174f6fc1`, which succeeded.
- **Code pages, 13 web resources, read back by SHA-256:**
  - the 9 create and document wizards plus `sprk_wizard_commands` (unchanged);
  - SmartTodo;
  - SPE Admin;
  - the Document Relationship Viewer.
- **PCFs:** SemanticSearch 1.1.86, CommunicationActions 1.3.22, TrackingFieldTrio 1.0.64. Imported with `Import-SolutionScoped.ps1` (scoped publish), and each served bundle hash matches its build.

## Deviations from the POML
- **More hosts than the POML named.** The POML's host list was the review's §2.3 list. CreateInvoice and CreateReportCard (task 180's Enter Info steps), DocumentUploadWizard, SmartTodo (CreateTodoWizard) and the CommunicationActions and TrackingFieldTrio PCFs (task 188) also bundle the changed code, so they were rebuilt and deployed.
- **Not deployed:**
  - CommunicationPage (`sprk_communicationpage`, task 188) and the RuleReview standalone page (`sprk_rulereviewpage.html`) do not exist on dev. Creating new web resources is outside 185.
  - RuleReview's New rule set runs inside the Console, which is deployed.
- **The PCF bundles were rebuilt, not taken from master's commits.** The bundles committed by 183/188 were older than master's shared library: they lacked `composeOnlyShowsFrom` and the 183 send-failure wording. The rebuilt ones were deployed, and PR #2836 records them on master (same pattern as #2828).
- **Build gotchas** (fresh worktree):
  - `Spaarke.UI.Components` needs `Spaarke.SdapClient` built first (file: dependency).
  - CommunicationActions needs `npm install` in `Spaarke.Communication.Components`.
  - Running PCF builds in parallel with a shared-lib build races `ensure-dist-fresh`.
- **`customcontrols.version` still reads the old version** after the scoped import. pcf-deploy documents this as harmless; the served bundles are new.

## ui-test results — owner, 2026-11-15 (section A; B–D not yet run; A8 later)
- **A1 Create Project:** Associate To, Add file(s) PASS. Enter Info: Project Type + Practice Area side pane PASS; **Description has no "Draft with AI"** (the Matter wizard's Enter Info has it) — gap. Assign Work: all lookups side pane PASS. Create Event: side pane PASS. Send Email: new composer PASS.
- **A2 Create Matter (Console, dark header):** Associate To, Add file(s), Enter Info (Matter Type + Practice Area side pane) PASS. **FAIL:** Assign Work — Matter Type / Practice Area use an inline search dropdown, not the side pane (seen after clearing the value), and the Assigned fields are not side pane. **FAIL:** Create Event follow-on — Event Type and Assigned To not side pane. **FAIL:** Send Email follow-on is not the new EmailComposer (Project's is).
- **A3 Create Event:** side pane PASS.
- **A4 Create To Do:** **FAIL** Assigned To not side pane; opens in the old dialog chrome (white header) rather than the in-app wizard; the Smart To Do kanban also opens in the white-header dialog; owner asks to rename "New Task" → "New To Do" (avoid confusion with Event Task).
- **A5 Create Invoice:** **FAIL/requests:** no Console Quick Start entry (only New Invoice on a matter/project); the record step uses an option list, not a lookup — and in record context it should default to that record and not be changeable; white-header dialog; when the record has no associated firm the required Firm step is not shown — must allow choosing a firm (owner thinks the SPA project did this).
- **A6 Create Work Assignment:** side pane PASS.
- **A7 Ontology admin → New rule:** **FAIL** — Condition step (Existence, about sprk_communication, no tests yet) shows the raw error `: Required properties ["when"] are not present`.
- **A8:** later. **A9 SPE Admin:** uses a dropdown; owner accepts. **A10:** picks cleared on reopen PASS.
