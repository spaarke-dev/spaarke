# Create wizards: side-pane lookups and the shared email composer (review)

Read-only review, 2026-11-12, for UAT round 4 findings F-R4-2 and F-R4-3 (`uat-v4-log.md:145-146`). No source was changed.
Paths below are relative to `src/client/shared/Spaarke.UI.Components/src/` (written `UI/`) unless they start with `src/`.

## Summary

- Both patterns exist and every create-wizard host already has what they need. Nothing new has to be built; the work is wiring.
- **Lookups:** the field component is `DataverseLookupField` (side pane through `INavigationService.openLookup`). The Matter and Project wizards already use it for Matter Type, Project Type and Practice Area. 30 other wizard lookup fields still use the inline `LookupField`.
- **Email:** the old step (`EmailStep/SendEmailStep`) and the composer post to the same BFF route. Swapping the step to the composer in compose-only mode is a UI change; the server behaviour changes only if we also change sent-as, body format or Cc.
- Four defects found in passing (section 4, items 1-4). Six owner questions (section 6).

## 1. The two canonical patterns

### 1.1 Side-pane lookup

The contract is `INavigationService.openLookup(options: LookupOptions): Promise<LookupResult[]>` (`UI/types/serviceInterfaces.ts:381`; options at `:204-242`, including `filters` at `:241`). The Dataverse implementation is `createXrmNavigationService().openLookup`, which calls `Xrm.Utility.lookupObjects` and returns bare lower-case GUIDs (`UI/utils/adapters/xrmNavigationServiceAdapter.ts:222-267`). The shared library has no Xrm in its components (ADR-012), so the host builds the service and passes it down as a prop.

Two things sit on top of that contract today:

- **`DataverseLookupField`** (`UI/components/LookupField/DataverseLookupField.tsx:184-303`): a general field. Props: `entityType`, `value`, `onChange`, `navigationService?`, `onSearch?` (`:50-81`). With a `navigationService` it shows a "Select" button that opens the pane, and a chip with a clear button once picked (`:262-301`). Without one it falls back to the inline `LookupField` (`:224-238`). This is what the Matter wizard uses for Matter Type and Practice Area (`UI/components/CreateMatterWizard/CreateRecordStep.tsx:592-613`), the Project wizard for Project Type and Practice Area (`UI/components/CreateProjectWizard/CreateProjectStep.tsx:437-457`), and the Work Assignment wizard's record picker (`UI/components/CreateWorkAssignmentWizard/SelectWorkStep.tsx:166-171`).
- **`DecisionOpenLookup`** (D-182, task 158): a decision-wizard-specific wrapper. The type is at `UI/components/DecisionWizard/decisionWizardTypes.ts:351-373`; the host builds it with `createWorklistOpenLookup(navigation, webApi)` (`src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/worklistLookup.ts:101-139`), which adds the row filter (active contacts, the item's own budgets) and a `supportedEntities` list. Hosts: `WorklistWorkspaceWidget.tsx:78-98` and `src/solutions/LegalWorkspace/src/sections/worklist.registration.ts:101-135`. The field itself is drawn in `UI/components/DecisionWizard/steps/ParameterFields.tsx:96-200`. Its request shape (`kind`, `parameterCode`, `core`) is tied to the decision catalog, so the create wizards cannot use it as it is.

**For the create wizards the reuse path is `DataverseLookupField` + the `navigationService` they already receive.** What the host must supply: an `INavigationService` built where Xrm exists. Every host already does (section 2.3).

`src/client/pcf/TrackingFieldTrio/index.ts` and `UI/components/RecordHeader/fields/LookupField.tsx:322` call `lookupObjects` for form-hosted PCFs; they are not wizard patterns.

### 1.2 Email composer

The engine is `EmailComposer` (`UI/components/EmailComposer/EmailComposer.tsx`), with wrappers per mount (`UI/components/EmailComposer/index.ts:13-31`). It sends through `sendCommunication()` (`EmailComposer.tsx:1153-1158`), which posts to `POST /api/communications/send` (`UI/services/communicationApi.ts:307, 349-399`). By default it sends with `archiveToSpe: true` (`EmailComposer.reducer.ts:187`), the host's `sendMode` or `defaultSendMode` (`EmailComposer.types.ts:582-589`), the `associations` it was given (regarding records), and the Internal Only flag (`EmailComposer.reducer.ts:409-431`).

Two wizard uses exist, and they differ in who sends:

- **Composer sends, record already exists**: `src/solutions/DocumentUploadWizard/src/components/DocumentEmailStep.tsx:294-330`. Inline mount, `sendMode="sharedMailbox"`, `associations` = the parent record, lookups from `createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl })` (`UI/components/EmailComposer/createXrmEmailComposeHandlers.ts:68-109`; the recipient pane opens `contact` + `systemuser` at `:214`).
- **Compose-only, host sends later** (D-179, task 160): `UI/components/DecisionWizard/steps/EmailMessageFields.tsx:126-141`. `composeOnly` hides Send, the From row and the Internal Only tags, and the composer's own `send()` rejects (`EmailComposer.types.ts:666-673`; `EmailComposer.tsx:1113, 1197, 1281, 1339`). The step reads the fields through `onStateChange` and the BFF sends at commit.

**The create wizards need the compose-only form**: the record does not exist while the step is shown, so the email can only be sent at Finish, after the create. What the host must supply: `authenticatedFetch` and `bffBaseUrl` (already passed to every wizard) and, for the recipient pane, `onLookupRecipients` from `createXrmEmailComposeHandlers` (needs Xrm).

## 2. Inventory

### 2.1 Lookup fields that do not use the side pane

"Reuse path" is `DataverseLookupField` with the wizard's `navigationService` in every "yes" row, keeping the existing search function as `onSearch` (the fallback).

| Surface | Step / field | File:line | Now | Change? | Notes |
|---|---|---|---|---|---|
| Assign Work follow-on (Matter, Project, Event, Invoice, Report Card wizards) | Assigned Attorney, Assigned Paralegal (contact), Assigned Outside Counsel (`sprk_organization`) | `UI/components/WizardFollowOns/steps/AssignWorkFollowOnStep.tsx:307, 328, 336` | inline | **yes** (F-R4-2) | The step has no `navigationService` prop today (`:48-100`). Keep "Assign to me" (`:316-326`). |
| same | Matter Type, Practice Area (reference tables) | same file `:249, 257` | inline | owner question 1 | The Matter and Project Enter Info steps already use the pane for these same tables. |
| Work Assignment wizard | Assign Work: Assigned Attorney, Assigned Paralegal, Law Firm, Law Firm Attorney | `UI/components/CreateWorkAssignmentWizard/AssignWorkStep.tsx:202, 209, 224, 231` | inline | yes | Law Firm Attorney is filtered by the chosen firm (`:156-159`); the pane needs a `filters` entry, which `DataverseLookupField` cannot pass today. |
| Work Assignment wizard | Enter Info: Assigned To (contact) | `UI/components/CreateWorkAssignmentWizard/EnterInfoStep.tsx:365` | inline | yes | |
| Work Assignment wizard | Enter Info: Matter Type, Practice Area | same file `:330, 337` | inline | owner question 1 | |
| Work Assignment wizard | Follow-on event: Assigned To (contact) | `UI/components/CreateWorkAssignmentWizard/CreateFollowOnEventStep.tsx:185` | inline | yes | |
| Event wizard, and the Create Event follow-on (wraps the same step, `WizardFollowOns/steps/CreateEventFollowOnStep.tsx:87`) | Assigned To (contact) | `UI/components/CreateEventWizard/CreateEventStep.tsx:250` | inline | yes | |
| same | Event Type (reference table) | same file `:221` | inline | owner question 1 | |
| To Do wizard, and the Add To Do follow-on (`WizardFollowOns/steps/AddTodoFollowOnStep.tsx:85`) | Assigned To (contact; `TodoWizardDialog.tsx:218`) | `UI/components/CreateTodoWizard/CreateTodoStep.tsx:192` | inline | yes | |
| Invoice wizard | Vendor Organization (`sprk_organization`) | `UI/components/CreateInvoiceWizard/CreateInvoiceStep.tsx:241` | inline | yes | |
| Report Card wizard | 8 fields: Attorney 1-2, Paralegal 1-2, Law Firm 1-2, Assigned External, Assigned Internal | `UI/components/CreateReportCardWizard/CreateReportCardStep.tsx:264-326` | inline | yes | |
| Old email step | To (systemuser) | `UI/components/EmailStep/SendEmailStep.tsx:197` | inline, single recipient | replaced by section 2.2 | |
| Ontology rule wizard | "Limit to one matter" | `UI/components/OntologyAdmin/RuleWizard/steps/ScopeNoiseStep.tsx:80` | inline | owner question 5 | Admin surface; not traced to its host. |
| Rule Review | Firm | `src/solutions/RuleReview/src/components/NewRuleSetModal.tsx:109`, `AssignmentPanel.tsx:77` | inline | owner question 5 | |
| SPE Admin | Business Unit, Environment, Container Type Config | `src/solutions/SpeAdminApp/src/components/layout/BuContextPicker.tsx:646, 687`; `settings/ContainerTypeConfig.tsx:605, 620` | inline | not determined | I did not check whether these are Dataverse tables the pane can open. |
| Record header PCFs | lookup cells | `src/client/pcf/RecordHeader/control/RecordHeaderView.tsx:517-537` | inline + "Advanced" footer that opens the pane | **no** | Deliberate: reproduces the form's own inline lookup (`UI/components/LookupField/LookupField.tsx:19-31, 91-108`). |

**Does D-182 cover reference tables?** Its text is "every Person and Lookup field in the decision wizard (assignee, budget, outside firm, email recipients)" (`decisions.md:187`). The decision catalog has no reference-table lookup, so D-182 does not answer it. The code points both ways: the Matter and Project wizards put Matter Type, Project Type and Practice Area in the side pane (commit `72ff34234f`, 2026-03-23), while the owner decision of 2026-08-27 recorded in `LookupField.tsx:101-107` is about the record header. Hence owner question 1.

### 2.2 Email steps that do not use the composer

| Surface | File:line | Step UI | Send at finish | Change? |
|---|---|---|---|---|
| Send Email follow-on wrapper | `UI/components/WizardFollowOns/steps/SendEmailFollowOnStep.tsx:136-151` | old `EmailStep` | (collects only) | **yes** (F-R4-3); one change here reaches all rows below except the last two |
| Matter, Project, Event wizards (via `CreateRecordWizard.tsx:529-538`) | Project `CreateProjectWizard.tsx:898-909`; Event `CreateEventWizard.tsx:486-495`; Matter `CreateMatterWizard.tsx:544-546` then `matterService.ts:395-400` | wrapper | `EntityCreationService.sendEmail` | via wrapper |
| Invoice, Report Card wizards | `CreateInvoiceWizard.tsx:352-359, 578-582`; `CreateReportCardWizard.tsx:351-358, 570-574` | wrapper | same | via wrapper |
| To Do wizard | `TodoWizardDialog.tsx:267-296` | wrapper (through `CreateRecordWizard`) | same | via wrapper |
| Work Assignment wizard (has Cc) | `WorkAssignmentWizardDialog.tsx:436-446, 624-631`; `workAssignmentService.ts:700-715` | wrapper | same, `bodyFormat: 'PlainText'` | via wrapper |
| Summarize Files wizard | `SummarizeFilesDialog.tsx:394-404, 488-506` | wrapper + "short summary" toggle | `sendCommunication`, shared mailbox, text | via wrapper; check the toggle slot |
| Document email wizard (hosted by the SemanticSearchControl PCF `SemanticSearchControl.tsx:1911` and DocumentRelationshipViewer `App.tsx:830`) | `UI/components/DocumentEmailWizard/DocumentEmailWizard.tsx:44, 818-824, 486-510` | old `EmailStep` directly | `sendCommunication`, shared mailbox, text | yes, own task: record exists, so it can copy `DocumentEmailStep.tsx`; one host is a React 16/17 PCF |

Already on the composer: Document Upload wizard (`DocumentEmailStep.tsx`), decision wizard (`EmailMessageFields.tsx`).

### 2.3 Hosts

| Host | How it renders the wizards | `navigationService` |
|---|---|---|
| Spaarke Console (`src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx:960`) | `InAppWizardHost` | `createXrmNavigationService()` once, passed to every wizard (`UI/components/Wizard/InAppWizardHost.tsx:208, 299-384`) |
| Code pages `src/solutions/Create{Matter,Project,Todo,WorkAssignment}Wizard/src/main.tsx`, `SummarizeFilesWizard` | dialog iframe | `createXrmNavigationService()` (`main.tsx:55 / 54 / 70 / 51`; Summarize `:71`) |
| Code pages `Create{Event,Invoice,ReportCard}Wizard` | dialog iframe | `useWizardPageBootstrap` (`UI/utils/useWizardPageBootstrap.ts:140`) |
| LegalWorkspace | opens the same wizards by name (`src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx:317-322`, `sections/getStarted.registration.ts:63-65`) | as above |
| SmartTodo | `CreateTodoWizard` (`src/solutions/SmartTodo/src/SmartTodoApp.tsx:635-669`) | Xrm adapter |
| External SPA (`src/client/external-spa`) | hosts none of these wizards (only `intake/NdaAssessmentWizard.tsx` matches) | n/a. Its adapter's `openLookup` returns `[]` (`UI/utils/adapters/bffNavigationServiceAdapter.ts:214-219`) |

So no host is a "host can't" case for the create wizards. The gap is inside the shared library: `CreateRecordWizard`'s config has a `navigationService` only inside `associateToStep` and `existingRecordPicker` (`UI/components/CreateRecordWizard/types.ts:46, 311`), and none of the follow-on or Enter Info steps listed in 2.1 take one.

## 3. Send Email step: old step vs composer

| | Old step (`EmailStep/SendEmailStep.tsx`) | Composer, compose-only |
|---|---|---|
| To | one systemuser, inline search; the address is parsed out of the display name (`:155-166`) | many; typed addresses, type-ahead, and the side-pane picker over contacts and users |
| Cc / Bcc | Cc only in the Work Assignment wizard, as free text (`SendEmailFollowOnStep.tsx:122-131`) | both built in |
| Body | plain textarea | plain or rich text |
| Attachments, templates, AI draft | none | available when the host passes the handlers |
| Route | `POST /api/communications/send` (`UI/services/EntityCreationService.ts:889`) | the same route (`communicationApi.ts:307`) |
| Filed under the new record | yes: `associations` = the created record (e.g. `CreateProjectWizard.tsx:906`) | yes, if the finish code keeps passing the association |
| Sent as | `sendMode` omitted, so the BFF default, shared mailbox (`SendCommunicationRequest.cs:71`) | whatever the finish code sends (owner question 2) |
| `.eml` archive | flag omitted (false), but a filed email is archived regardless (`SendCommunicationRequest.cs:73-78`, D-217) | same |
| Internal Only | omitted, so written false (`SendCommunicationRequest.cs:125-136`) | hidden in compose-only; same result |

**Server behaviour does not change if** the step keeps its current prop contract (`emailTo` as addresses joined with "; ", plain-text subject and body) and the wizards' finish code is untouched. `EntityCreationService.sendEmail` already splits `to` on `;` and `,` (`EntityCreationService.ts:877-883`), so several recipients work with no finish change. This is the same shape `EmailMessageFields` commits (`EmailMessageFields.tsx:6-8`). It changes only where we choose to: sent-as, Cc/Bcc, body format, attachments.

## 4. Risks and correctness concerns (ranked)

1. **Existing defect: the step's text says the email is a draft, but it is sent.** "It will be created as an email activity ... saved as a draft activity on the record" (`SendEmailFollowOnStep.tsx:92-94`; `CreateRecordWizard.tsx:537`; `SendEmailStep.tsx:147`), while Finish posts to `/api/communications/send`. The matter service's warning text also says "create email activity" (`matterService.ts:402`). Fix the wording with the composer swap.
2. **Likely existing defect: line breaks.** Matter, Project, Event, Invoice, Report Card and To Do send the textarea body with no `bodyFormat`, and `sendEmail` defaults it to `'HTML'` (`EntityCreationService.ts:897`). Only the Work Assignment wizard sends `'PlainText'` (`workAssignmentService.ts:712`). I did not confirm how the BFF renders an HTML body with bare newlines; check one sent email on dev.
3. **`DataverseLookupField` gaps, which matter once it is used 30 more times:**
   - A pane that throws is an unhandled rejection: `handleOpenLookup` has `try/finally` and no `catch` (`DataverseLookupField.tsx:200-216`), and the adapter throws when Xrm is missing (`xrmNavigationServiceAdapter.ts:224-228`). `ParameterFields.tsx:113-121` treats a failure as a cancel; copy that.
   - Its header says it falls back to inline search when the lookup returns empty (`:9-11`); the code does not (`:205-212`). A host whose `openLookup` returns `[]` gets a dead field. Not reachable from today's hosts (2.3), but the comment is wrong.
   - No `filters` prop, so no active-only filter and no dependent filter (Law Firm Attorney). The decision wizard's pane filters to active contacts (`worklistLookup.ts:108`); the create wizards' inline contact search should be compared before choosing.
   - No tests: `UI/components/LookupField/__tests__/` holds only `LookupField.test.tsx`.
4. **Bcc is lost on Back/Next.** The wizard shell renders only the current step (`UI/components/Wizard/WizardShell.tsx:435`), so the composer unmounts when the user leaves the step and is rebuilt from `initialTo` / `initialCc` / `initialSubject` / `initialBody`. There is no `initialBcc` (`EmailComposer.types.ts:398-402`). Also `EntityCreationService.sendEmail` has no Bcc. Either warn as the decision wizard does for Cc/Bcc (`EmailMessageFields.tsx:152-156`) or decide Cc/Bcc support (owner question 4).
5. **Stacking of the pane over the wizard modal.** The wizard shell is a blocking `SprkModal` and does not pass `nonBlocking` / `yieldToSidePane` (`WizardShell.tsx:548-561`; `SprkModal.tsx:194`). Task 158 recorded this as untested (`task-158-notes.md`, "Known limits"), and the owner then passed 158 in round 3 (`uat-v4-log.md:126`), with the same shell in the Console. The Matter wizard's Matter Type pane also runs in that shell. So it works on dev, but nothing in the code guarantees it. Each task's dev check must include: pane opens above the wizard, wizard is usable and keeps its values after pick and after cancel, in the Console and in a code-page dialog.
6. **Two field renderings of the same pattern.** `ParameterFields.tsx:160-200` (read-only input + search button) and `DataverseLookupField` (button, then chip). Do not add a third. Converging the decision wizard onto `DataverseLookupField` is optional and out of scope here.
7. **Duplicates and dead code (CLAUDE.md section 11):**
   - `UI/components/EmailStep/LookupField.tsx` (380 lines) is a second copy of the shared `LookupField`, with its own `ILookupItem` (`:34-37`).
   - Two exports named `SendEmailStep`: the old one (`UI/components/EmailStep/index.ts:7`) and the composer wrapper (`UI/components/EmailComposer/index.ts:19`). The wrapper has no consumer outside its own test.
   - No JSX consumer anywhere in `src`: `CreateRecordWizard/steps/AssignResourcesStep.tsx` (inline lookups at `:121, 129, 145`), `CreateMatterWizard/AssignResourcesStep.tsx`, `CreateMatterWizard/AssignCounselStep.tsx`, `CreateMatterWizard/DraftSummaryStep.tsx` and its `CreateMatterWizard/RecipientField.tsx`. They are exported from barrels (`CreateRecordWizard/index.ts:24`, `CreateMatterWizard/index.ts:26`), so confirm no external importer before retiring them.
   - `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` repeats the shared `matterService` send code (`:318, 369`); not traced further.
   - `EmailMessageFields.toPlainText` (`:45-52`) will be needed by the create wizards too; move it to a shared helper, do not copy it.
8. **React 16/17 PCF host.** Only `DocumentEmailWizard` is affected (SemanticSearchControl). The composer already runs in the CommunicationActions PCF, so it is feasible, but that PCF keeps its own lookup handlers (`createXrmEmailComposeHandlers.ts:11-12`). Keep it a separate task.

## 5. Proposed task split

Each task is one PR. Common negative criteria for all: no new lookup component, no new email form, no change to `LookupField.tsx` behaviour, no Xrm import added to a component file.

**T1. Harden `DataverseLookupField` (prerequisite).**
Files: `UI/components/LookupField/DataverseLookupField.tsx`, new `__tests__/DataverseLookupField.test.tsx`.
Copy from: `ParameterFields.tsx:99-123` (single flight, failure = cancel).
Do: optional `filters` (passed to `openLookup`), optional `hint`; catch a failed pane and log it; single flight; correct the header comment.
Accept: pick fills; cancel keeps the prior value; a throwing `openLookup` leaves the value unchanged with no unhandled rejection; second click while open opens nothing; no `navigationService` renders the inline field exactly as before; existing Matter and Project step tests pass unchanged.

**T2. Assign Work follow-on uses the side pane (F-R4-2).**
Files: `WizardFollowOns/steps/AssignWorkFollowOnStep.tsx`; `CreateRecordWizard/types.ts` and `CreateRecordWizard.tsx:441-466` (one optional top-level `navigationService` on the config); `CreateMatterWizard.tsx`, `CreateProjectWizard.tsx`, `CreateEventWizard.tsx` (pass it); `CreateInvoiceWizard.tsx:403-427`, `CreateReportCardWizard.tsx:402-426` (pass it to the step directly).
Copy from: `CreateProjectStep.tsx:437-457`.
Accept: with a `navigationService`, Assigned Attorney, Paralegal and Outside Counsel render no inline search input and open `contact` / `contact` / `sprk_organization`; "Assign to me" still fills the attorney; the picked ids reach `onFinish` unchanged; without a `navigationService` the step renders exactly as today (existing `AssignWorkFollowOnStep.assignToMe.test.tsx` passes unchanged). Matter Type and Practice Area per owner question 1.
Dev check: risk 5.

**T3. Enter Info steps of the Event, To Do, Invoice and Report Card wizards.**
Files: `CreateEventStep.tsx`, `CreateTodoStep.tsx`, `CreateInvoiceStep.tsx`, `CreateReportCardStep.tsx` and their wizards (pass `navigationService`; all four already receive it). The Create Event and Add To Do follow-ons inherit the change; pass the service through `CreateEventFollowOnStep.tsx` and `AddTodoFollowOnStep.tsx`.
Accept: as T2, per field in section 2.1.

**T4. Work Assignment wizard.**
Files: `AssignWorkStep.tsx`, `EnterInfoStep.tsx`, `CreateFollowOnEventStep.tsx`, `WorkAssignmentWizardDialog.tsx`.
Accept: as T2; Law Firm Attorney's pane is filtered to the chosen firm and is disabled or unfiltered exactly as the inline field behaves today when no firm is chosen (read `:156-159` first); existing `EnterInfoStep.assignedTo` and `WorkAssignmentWizardDialog.*` tests pass.

**T5. Send Email follow-on uses the composer, compose-only (F-R4-3).**
Files: `WizardFollowOns/steps/SendEmailFollowOnStep.tsx` (replace the inner `EmailStep` with `EmailComposer`); a shared plain-text helper moved out of `EmailMessageFields.tsx`; the wording in `CreateRecordWizard.tsx:520-537`; `bodyFormat: 'PlainText'` at the six finish sites if risk 2 is confirmed.
Copy from: `EmailMessageFields.tsx:54-158` (compose-only, `onStateChange` to the wizard's state, refuse-fetch, warnings) and `DocumentEmailStep.tsx:247-250` (`createXrmEmailComposeHandlers` for `onLookupRecipients`).
Keep: the step's existing props, so the eight consuming wizards and their finish code do not change; the `headerContent` slot (Summarize Files toggle).
Accept: no Send button, no From row; several typed or picked recipients arrive at Finish as "a; b"; Back then Next restores To, subject and body; the composer makes no network call (test with a rejecting fetch, as `EmailMessageFields.tsx:41-42`); the request body at Finish is byte-identical to today's for a single recipient, except the agreed changes; the step text no longer says "draft"; Cc/Bcc per owner question 4; `SendEmailFollowOnStep` no longer imports `../../EmailStep`.

**T6. `DocumentEmailWizard` on the composer.** Own task because of the PCF host (risk 8). Copy from `DocumentEmailStep.tsx`. After it, `UI/components/EmailStep/` has no consumer.

**T7. Retire the old code and add the guard.**
Retire (per the owner's move-never-delete rule, with an importer audit): `UI/components/EmailStep/`, and the unused steps in risk 7.
Guard (CLAUDE.md section 16, source-scan test in `UI/src/__tests__/`, as `noAccessEntryForm.test.ts` does): fails when a file under `components/Create*Wizard/` or `components/WizardFollowOns/` renders `<LookupField` or imports `EmailStep`. Ships with a must-fire and a must-not-fire control and a ratchet baseline listing whatever T2-T6 have not yet converted (and the reference-table fields, if owner question 1 keeps them inline).

Out of scope: record header PCFs (deliberate inline pattern); the external SPA (hosts none of these); Rule Review, the ontology rule wizard and SPE Admin until owner question 5; converging `ParameterFields` onto `DataverseLookupField`.

## 6. Open questions for the owner

1. **Reference tables** (Matter Type, Practice Area, Event Type) in the Assign Work, Work Assignment and Event steps: side pane, as the Matter and Project Enter Info steps already do, or stay inline? Recommendation: side pane, so one wizard does not mix both.
2. **Sent as**: the create wizards send from the shared mailbox today; D-179 made the budget inquiry send from the user's mailbox. Keep shared mailbox, or switch?
3. **Attachments, templates, AI draft** in the create wizards' compose step: off (as in the decision wizard), or on? Attaching the wizard's own uploaded files is not possible at that step, because they are uploaded at Finish.
4. **Cc / Bcc**: send Cc everywhere (needs `emailCc` in the follow-on state; `sendEmail` already accepts `cc`), and warn that Bcc is not sent? Or hide both?
5. **Other surfaces**: should the Firm pickers in Rule Review, the matter picker in the ontology rule wizard, and the SPE Admin pickers move to the side pane too?
6. **Contact filter**: should the Assigned pickers show active contacts only, as the decision wizard's pane does?
