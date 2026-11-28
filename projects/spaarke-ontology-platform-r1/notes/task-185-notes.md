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

## Recheck after 193-195, owner, 2026-11-18
- **C1 Semantic Search → Email documents:** PASS (lookup above the wizard, Summary once, template finished text).
- **C2 Document Relationship Viewer:** PASS.
- **D1 Console Send Email:** PASS (recipients and Related to use the side pane). Two new owner requests, task **197**: the composer opens in plain text and must open in rich text in every host (owner scope "every composer"); the attach menu moves from the toolbar paperclip to a button beside the Attachments heading, like "Link a record".
- **A7 rule wizard:** PASS.
- Still open in 185: the A2/B5 Create Matter follow-on (needs the owner's F12 check during a repro) and the owner questions (invoice `lockedRecord`, section A requests, AI draft retry).


## Browser check by the main session (built-in browser, owner signed in), 2026-11-22
Nothing sent, nothing created: every draft and wizard was cancelled.
- **197 Console Send Email:** opens in rich text; Attach button beside Attachments in rich AND plain mode, menu = Add files, Link documents; the email toolbar has no paperclip. Plain → rich toggle keeps typed `<b>` literal and the line break. PASS.
- **195 template render (Console):** "Thank you for registering with us" applied: subject clean, body finished text with fallbacks ("Valued Customer", "No Address Provided"), no XSL, `]]>` or `{!…}`. PASS. Cosmetic: "Dear Valued Customer ," (space before the comma, from the template itself).
- **AI draft:** Draft with AI inserted a sentence into the body (no "unavailable"). PASS.
- **A2/B5 Create Matter (Console):** Matter Type / Practice Area / Assigned Attorney / Paralegal / Outside Counsel / Event Type / Assigned To are all side-pane buttons (`BUTTON`, also after clearing a value); the lookup opens above the wizard. Send Email follow-on is the new composer (`[data-testid="send-email-followon-step"]` present), rich text, Attach button, no toolbar paperclip, pre-fill intact. No wizard errors in the console. PASS (closes the A2/B5 F12 question).
- **Matter form PCFs:** Documents shows v1.1.89, Tracking v1.0.65.
- **Email documents from the matter form (Documents → open file preview → ⋮ → Email):** Confirm → Summary (readable, once) → Compose: rich text, pre-fill intact, Attach beside "Attachments (1)", no paperclip. PASS for 197.
- **NEW DEFECT (this path only):** clicking To opens the Dataverse lookup pane, but the page cannot be used: every point over the pane hits a Fluent `DialogSurface__backdrop` in a `FluentProvider` portal at z-index 1000000 (`document.elementFromPoint`). Search, Add, Cancel and ✕ do nothing; the user is stuck until the wizard is closed some other way. The file preview dialog stays open underneath the Email wizard; its backdrop is the only one in the DOM, so the wizard yields (193) but the preview's backdrop does not. The C1 path the owner passed (Semantic Search toolbar → Email documents) has no preview underneath. Not yet diagnosed in code.
- **Not tested:** budget inquiry composer (160/190/192): "Ask outside counsel" has 0 items and the 176 Signal is closed, so it needs new test data (owner). C3. A7 (owner passed 11-18).
- Console noise (not ours): UCI `[storage]` "Entity 'sprk_invoice' With Id … Does Not Exist" for several deleted invoices.

## Re-test after task 198 and remaining checks, main session, 2026-11-22 (afternoon)
Nothing sent or saved: every composer and wizard was cancelled or discarded.
- **198 preview → Email → To (SemanticSearchControl 1.1.90, deployed on owner go):** PASS. The pane opens on top. `elementFromPoint` over the pane hits the pane, no backdrop is present while it is open, and the preview and wizard are both `inert`. Picking CIAM Test User → Add fills To. Afterwards the preview's backdrop is back (`aria-modal=true`).
- **NEW (a11y), task 199:** after the pick, focus is on the Documents grid link behind both dialogs. Email Members (Tracking) shows the same after a cancelled To lookup: focus is on the "Email members" button behind the dialog.
- **D2/D4 Email Members (TrackingFieldTrio 1.0.65):** rich text, Attach beside Attachments, Related to = the matter with "Link another record", template and AI icons present. To opens the side pane on top. PASS, apart from the 199 focus issue. Recipients are not pre-filled; the banner explains that org-wide-only access is not added.
- **D3 Communication Actions:** not testable on dev. No form on spaarkedev1 hosts the control (systemforms formxml search). The Message form hosts "Respond" (v1.0.6), a different control.
- **A8 Ontology admin → New rule → Scope and noise → Limit to one matter:** side pane on top of the rule wizard; MAT-000113 picked → "Applies to: MAT-000113". PASS. Focus afterwards is on `<body>`, so the same class extends to the shared `DataverseLookupField`: **task 200**. The 102b "Add at least one test" message shows (A7 fix holds).
  - Cosmetic: the Condition step lists tables by logical name (`sprk_matter`).
  - Cosmetic: Actions with nothing chosen shows "The decision plan is refused (plan_missing): …", which shows an internal code.
- **C3 send failure (Console Send Email):** cross-origin POSTs from the Console frame were blocked in the page, and the address was `c3-test@example.invalid`. The block caught the send (`POST …/api/communications/send`). The dialog reads "Email not sent — Something went wrong on this page and the email was not sent. Try sending again; if it keeps happening, contact support. Your draft is still here — nothing was lost." PASS.
  - Wording nit: a network failure is described as "on this page".
  - Closing the composer with a typed draft discards it without asking. The rule wizard asks first.
- Viewport note: at the pane's 918 px width the Console's New rule modal is cramped. That is below the 1280 floor, so the tests ran at 1440×900.

### Worklist decision review and Ontology admin, main session, 2026-11-22 (evening)

Console on dev (master `fe150ac35a`), 1440x900. Nothing recorded, saved or sent: every wizard was cancelled or discarded.

| Check | Result |
|---|---|
| Worklist → Start review opens the decision wizard at "1 of 192" | PASS |
| What was found: sentence, evidence ("No evidence was recorded"), "How this was determined" plain + formal lines | PASS, wording defect DI-159 #2950 ("where state is 0") |
| Create budget: amount validation (letters, negative), end before start, Next disabled until required fields | PASS |
| Create budget Category choices | Placeholder labels "Budget Category 0..4" in the shipped option set: DI-160 #2951 |
| Assign Task: "What it does" text | Says "the person above" for a field below: DI-159 #2950 |
| Next steps: Add To Do card adds a step; Confirm lists Taken / Skipped / Next steps, "Recorded as: a judgement" | PASS |
| Add To Do "Assigned to" lookup: side pane opens above the dimmed wizard, pick returns | PASS; focus then on body (task 200 class) |
| Cancel with choices → "Discard unrecorded choices?"; Keep deciding keeps the draft | PASS |
| Next record / Previous record keep each item's draft (by design, DecisionWizard.tsx:413-415) | PASS |
| Focus after Keep deciding / Cancel / Discard (decision wizard and rule wizard) | FAIL: focus on body, not the opener. Task 202 |

Further checks the same evening:

| Check | Result |
|---|---|
| Worklist narrative ("241 notifications across 3 categories") | FAIL (wording/truth): past-due sent as a third category; DI-161 #2952 |
| Do lane: Show items, open an Overdue To Do ("3 of 49"): What was found, Mark complete (excludes Reschedule/Reassign), Reschedule (date + reason required; blank or spaces reason blocks Next) | PASS |
| Reschedule to a past date (2020-01-01) | Accepted, no message, by UI and BFF (`RescheduleExecutor.PreflightAsync` checks format only). Owner 2026-11-22: allow (D-226) |
| Cancel with choices → discard check; Discard closes, nothing written | PASS |
| Ontology admin → Classification table at 1440x900 | Guidance column ~120 px, rows ~220 px tall: added to task 201 |
| Classification → Edit (inline) focus | Focus stays on Edit; Cancel drops it to body: added to task 202 |

## Dev checks after the 199 + 200 deploy, main session, 2026-11-23

Deploy: `notes/deploy-log.md` 2026-11-23 (master `973d72b67e`). Nothing was saved or sent: the rule wizard was discarded and the composer closed.

| Check | Result |
|---|---|
| Ontology admin → Rules at 1280x720 and 1440x900 (task 201) | PASS: rows 57 px, nothing clipped, no horizontal overflow |
| Ontology admin → Classification at both sizes (task 201) | PASS: rows 44-139 px, nothing clipped; Guidance preview wraps to 2 lines; "POL-COMMIT-BUDGET" in Read by wraps at its hyphens (readable) |
| Console → New rule → Scope → "Limit to one matter": focus the button, open, Cancel the pane (task 200) | PASS: focus back on "Select Limit to one matter"; top document focus in the Console frame |
| Same, pick MAT-000113 → Add (task 200) | PASS: focus on "Clear Limit to one matter" (the control that replaced Select) |
| Matter MAT-000113 → Tracking email icon → composer → To lookup → Cancel the pane (task 199, TrackingFieldTrio 1.0.67) | PASS: focus back on "Look up To recipients". The platform then focused "Email members" behind the dialog about 0.2 s later; the retry reclaimed it 4 ms after (the late hand-back known limit of 199 does not bite here) |
| Composer Close (task 202 scope) | Focus falls to body, as expected until 202 ships |
| Not checked | Find Similar → DRV → Email → To, and preview → Email → To (iframe and yielding-modal composer paths); a create-wizard code page lookup |

Gotcha found: a Console tab that stayed open across the deploy kept the web resource URL token from 04:01Z and served the OLD bundle even after Ctrl+Shift+R and a frame reload (the server returns the old content for the old token). A new tab got the new token and the new code. Testers: open a new tab after a Console deploy.

## Dev checks after task 202 reached dev (main session, 2026-11-23)

Console auto-deployed from master `6311cd0a69` (17:56Z). Checked in the owner's Chrome (the built-in browser's dev sign-in failed). Nothing saved: the Decision wizard's typed budget amount was discarded.

| Path | Focus ends on | Result |
|---|---|---|
| Worklist → Start review → Cancel (nothing chosen) | Start review | PASS |
| Decision wizard, amount typed → Cancel → Keep deciding | the wizard's Cancel | PASS |
| → Cancel → Discard and close | Start review | PASS |
| Ontology admin → Classification → Edit | the form's Name field | PASS |
| → Cancel | that row's Edit button | PASS |
| Ontology admin → Rules → New rule → Cancel | body | **FAIL** |

New rule cause (focusin + MutationObserver trace): `RuleWizard` shows a loading SprkModal, then replaces it with RuleWizardBody's modal about 550 ms later. The loading modal correctly restores to New rule; the new modal then captures focus from New rule but records the loading modal's Close button (still active during its render) as its opener. At close that button is gone, so nothing is restored. Fix: follow-up PR to master (SprkModal hands the opener over when one modal replaces another). The composer Close check waits for the 202 PCF deploy.

### Recheck after the 202 follow-up (#3004 `738648c7fb`, Console auto-deployed 19:44Z)

Main session, owner's Chrome, 2026-11-23. A fresh tab still carried the 17:59Z cache token, so the Console frame was loaded from `/webresources/sprk_spaarkeai` directly; its bundle contains the follow-up code.

| Path | Focus ends on | Result |
|---|---|---|
| Ontology admin → New rule → Cancel (nothing typed) | New rule | PASS (was FAIL) |
| New rule → name typed → Cancel → Discard | New rule | PASS |
| Decision wizard: amount typed → Cancel → Keep deciding | the wizard's Cancel | PASS (no regression) |
| → Cancel → Discard and close | Start review | PASS |
| Classification → Edit / Cancel | Name field / that row's Edit | PASS |

Nothing saved (the typed rule name and budget amount were discarded). Next: the 202 PCF + code-page deploy (owner go 2026-11-23), then the composer Close check.

### Composer Close after the 202 PCF deploy (main session, owner's Chrome, 2026-11-23 ~22:40Z)

TrackingFieldTrio on dev serves 1.0.68 (bundle SHA-256 prefix `63406a99c0dc`, matches the deploy); a tab open since before the deploy showed 1.0.67 until a hard reload. Nothing typed, nothing sent.

| Path | Focus ends on | Result |
|---|---|---|
| Matter MAT-000113 → Tracking "Email members" → composer → Close (X) | Email members | PASS (was body) |
| Same → composer → Cancel | Email members | PASS |
| Tracking "Grant access" → its dialog → Cancel (opened by a mis-aimed click) | Grant access | PASS (its own opener) |

## Remaining checks, main session, 2026-11-28 (owner's Chrome, own tab, 1288x945)

Nothing was saved, created or sent: every wizard was cancelled, and the composer was closed with a recipient in To but never sent.

| Check | Result |
|---|---|
| A4 Console → Corporate Workspace → Get Started → Create New To Do → To Do Details → Assigned To (cleared, then clicked) | PASS: it is a side-pane button, and the pane opens on top of the in-app wizard (dark chrome) |
| A4 Smart To Do "+ New Task" inside the Console | Now opens the same in-app Create New To Do wizard (it used to open the Dataverse form). The label still reads "New Task"; the rename to "New To Do" is an owner request |
| A5 Matter MAT-000113 → Billing → New Invoice | Opens with a Firm step (one radio per firm the account belongs to), then Record, File, Details. **Still open (eBilling's `CreateInvoiceWizard`):** the Record step is a searchable radio list with the matter preselected but changeable (no `lockedRecord`); the wizard sits inside a white-header outer dialog; Next needs an uploaded PDF, so Details was not reached. There is no Vendor Organization lookup any more: the firm comes from the Firm step |
| Checklist A7 Rule Review → New rule set → Firm | Not testable on dev: the Rule Review page (eBilling) is not deployed there, and the Console does not host it |
| Find Similar → Document Relationship Viewer → preview → ⋮ Email → Compose → To | PASS: the pane opens on top, nothing blocks it (no backdrop), and a contact found by search fills To ("Jane Smith (jane.smith@…)") |
| Same, after the pick | **Focus** ends on the viewer frame's `<body>`, not on the To button (the task 199 hand-back does not reach this iframe path). Minor a11y |
| Same, picking "Jane Smith" from the pane's **Recent records** | To stays empty with no message. The recent entry is a deleted system user (`lookupObjects` returned a systemuser id that `retrieveRecord` reports as not found); `createXrmEmailComposeHandlers.ts:221-240` logs `[EmailCompose] recipient email resolve failed` and drops the pick silently. Stale dev data exposed a silent-failure gap in the shared composer |
| Closing the composer with a recipient filled | Closes without asking (already noted 2026-11-22) |

### Status of task 185
Every host in the checklist has now been run, or recorded as not testable on dev (A7 Rule Review, D3 Communication Actions). What is left is not a deploy defect:
- **Owner requests (section A):**
  - "Draft with AI" in the Create Project description;
  - rename "New Task" to "New To Do";
  - a Console entry for Create Invoice, in-app launch, record locked in record context, and a firm choice when the record has no linked firm (eBilling's wizard).
- **New minor defects:**
  - the shared composer drops an unresolvable recipient pick silently;
  - focus is not handed back in the viewer's iframe composer.
