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

## Triage of the section A failures (diagnosis at origin/master `ce6c241964`, 2026-11-16)
- **A2 Matter follow-ons:** no code cause found. Matter and Project use the same host (`InAppWizardHost.tsx:317/331`), the same `navigationService`/`emailComposeHandlers` and the same follow-on steps (`CreateRecordWizard.tsx:422-545`); `SendEmailFollowOnStep` always renders `EmailComposer`. Suspected stale Console bundle, or an empty side-pane field read as an inline search (`DataverseLookupField.tsx:316-328`). Owner to retest after a hard reload, clicking the field.
- **A7 rule wizard:** defect (task 102b, not 187). `buildRuleBody` (`ruleDraft.ts:321`) reports no problem for an Existence rule with no tests, so Next sends a body the BFF schema refuses at the root; `ConditionStep.tsx:267-270` shows it raw. Fix: a "add at least one test" problem in `buildRuleBody`, strip the leading ": ". Size S, ontology-owned.
- **A5 locked record:** defect in eBilling's `CreateInvoiceWizard.tsx:139`: it passes `initialRecord` but not `lockedRecord`, so a record launch shows a one-item radio list. Size S.
- **A4 Assigned To:** the Console To Do widget "+" opens the in-app wizard with the side pane. The failing paths are not the wizard: Smart To Do "+ New Task" opens the Dataverse To Do form (`newTaskLauncher.ts:276-300`, by design since smart-todo-r5 030) and the Assistant `create-todo` is `kind: 'oob-form'`. Not a 185 defect.
- **New requests (owner decides):** Project "Draft with AI" (M, BFF); Smart To Do + Assistant open the To Do wizard and "New Task" → "New To Do" (`Header.tsx:306`) (S-M); invoice Quick Start entry + in-app launch (M), lookup for unlocked launches (M), firm choice when the record has no linked firm (M-L, server requires a linked firm, needs a decision; no external-SPA code to reuse).

## ui-test results — owner, 2026-11-16 (sections B–D)
- **B1–B4 Create Project Send Email:** PASS.
- **B5 Create Matter opened as a code page:** FAIL, same as A2 (the follow-on is not the new email composer). This weakens the stale-Console-bundle explanation for A2: the code page was deployed 2026-11-15 from `acc96e1927`.
- **C1 Semantic Search → Email documents** (PCF 1.1.86, white-header dialog): the compose step opens, but **To/Cc/Bcc open the Dataverse lookup BEHIND the modal** (visible top-right as "Lookup Records"). Applying a Dataverse email template puts **raw XSL (`<?xml … xsl:stylesheet …`) in Subject** and leaves **`]]>` CDATA debris in the body** ("Dear ]]>Valued Customer"); the sent email carried that debris. The Summary step shows the playbook's **raw JSON, twice**. "AI drafting is unavailable right now." Owner: the Semantic Search wizard does not use the new modal.
- **C2 Document Relationship Viewer:** same as C1.
- **C3:** not testable yet.
- **D1 Console Quick Start → Send Email ("New Email", Send split button, External/Internal Only):** To/Cc/Bcc do not open the side pane; **Related to has no lookup**. Template, AI draft and attachments work.

## Triage of the B–D failures (diagnosis at origin/master, dev web resources and App Insights, 2026-11-16)
- **A2/B5 Matter follow-ons:** still unexplained. The deployed `sprk_creatematterwizard` and `sprk_createprojectwizard` (both modified 2026-11-15 14:48:42Z) carry the same composer/lookup markers, and the Matter config passes `navigationService` and `emailComposeHandlers` (`CreateMatterWizard.tsx:487-492`). Next: a DevTools check during the owner's repro (`[data-testid="send-email-followon-step"]` present? "Search matter types…" a button or an input? `[DataverseLookupField] openLookup failed` in the console?).
- **C1/C2 (a) lookup behind the modal:** defect in 183/188 scope. In the PCF host `WizardShell.tsx:548-560` wraps `DocumentEmailWizard` in a blocking `SprkModal`, so `Xrm.Utility.lookupObjects` opens underneath it; `SendEmailDialog`'s `wrapLookup` + `yieldToSidePane` (`SendEmailDialog.tsx:314-361`, `sidePaneLayering.ts`) was not applied. DRV is an iframe inside the PCF's `FindSimilarViewerDialog` (`SemanticSearchControl.tsx:731-736`), so its outer dialog must yield too. M (+ M–L for DRV).
- **C1 (b) template XSL subject and `]]>` debris:** pre-existing BFF defect: `/api/communications/template/render` → `EmailTemplateService.cs:141` uses the XSL `subject`/`body` columns and only rewrites `{!x}` (`:182-232`). Fix: `InstantiateTemplate` with a regarding record, else `subjectsafehtml`/`safehtml` with placeholder fallbacks. M. Affects every composer host, the Console Send Email included (owner saw it work there; unverified).
- **C1 (c) summary raw JSON twice:** pre-existing: `DocumentEmailWizard.tsx:818-835` concatenates every chunk and shows it verbatim; `/api/ai/analysis/execute` (`AnalysisEndpoints.cs:652-658`) sends the streamed tokens and then the final node text. S–M.
- **C1 (d) "AI drafting is unavailable":** live dev BFF `f451e0874d` (master `ce6c241964` + mcp spike, deployed 2026-11-16 00:44Z; the older `5a9b687a0b` had the same defect) has `ModelContextProtocol.AspNetCore` 2.3.1 (needs `Microsoft.Extensions.AI.Abstractions` 10.11) with `Microsoft.Extensions.AI`/`.OpenAI` 10.3.0. App Insights 2026-11-16 03:03:35Z: `POST /api/communications/draft` 503, TypeLoadException `FunctionApprovalRequestContent`. Not ontology code; reported to uac-r2 (dev BFF owner) and mcp-r1 (their task 020 moves the family to 10.11.0) on 2026-11-16. uac-r2 confirmed: one TypeLoadException in 7 days; other IChatClient routes (briefing narrate, chat suggest, analysis execute) returned 200, so the break is narrower than all IChatClient. The next dev BFF build carries the full task-020 package set or drops MCP (uac-r2 + mcp-r1, owner chooses with the SHA).
- **D1 Console Send Email:** gap no task picked up (186 did D-219 Q3 only): `ConversationPane.tsx:3597-3612` mounts `SendEmailDialog` without `onLookupRecipients`, `recordLookupCatalog`, `onLookupRecord` and `onAddRelationship`, which are already built at `:923-926`. S.
