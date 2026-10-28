# Task 168 — Assign Task email and bell notification (D-194)

> Implementer notes, 2026-10-28. Branch `task/168-assign-task-notify`, rebased onto `docs/ontology-platform-design` at 58daec8c2b.
> Rigor FULL. The POML and TASK-INDEX status are left to the main session.

## What changed

| Area | Change |
|---|---|
| Catalog | New `DecisionParameterKind.Boolean`. New trailing `DecisionActionParameter.DefaultValue`. Assign Task gains `notify` ("Send email notification", Boolean, optional, `DefaultValue: "true"`) and a second effect line. |
| Plan DTO | `DecisionActionDto.From` sends `DefaultValue`, a resolved `DefaultSource` first, then the fixed default. |
| Commit route | `ReadValues` refuses a Boolean that is not true/false (`InvalidParameter`, before any write). After the record, the Signal close and `AfterRecordedAsync` (new step 7), `AssignTaskNotices` builds one notice per **Done** assign-task step with notify on (an omitted value is on). The route then calls `DecisionCommitFollowUps.NotifyAssigneesAsync` inside a try/catch. A fault adds `assignee_email_not_sent` and never changes the result. |
| `AssignTaskNotifier` (new, scoped) | **Email:** reads the contact's `emailaddress1` app-only (regex-checked). It sends as the assigner (`SendMode.User`, 10 s linked timeout). On any failure it queues the `AssignTaskEmail` ADR-004 job, with ids only and a 5 s enqueue bound. **Bell:** `PersonalToDoAssignee.SingleEnabledUserAsync` → `NotificationService.CreateNotificationAsync` (category `assignment`, relative `/main.aspx?etn=…&id=…&pagetype=entityrecord`, "Open matter"). Never throws. |
| `AssignTaskEmailJobHandler` (new, scoped) | Idempotency entry → processing lock → durable check (`sprk_communication.sprk_correlationid = assign-task-{taskId:N}`). It then sends from `Communication:DefaultMailbox` via `RegistrationEmailService.ResolveFromMailbox`; the send path validates the approved sender. Poisoned on an unreadable payload; Failure (retried) on a fault. |
| Template | `Services/Signals/EmailTemplates/AssignTaskNotificationTemplate.html` is an embedded resource (new csproj line). It copies the Spaarke branded layout. Every value is HTML-encoded and the template is filled in one pass (`RegistrationEmailService.FillTemplate`). |
| DI | `SignalsModule`: `AddScoped<AssignTaskNotifier>()` and `AddScoped<IJobHandler, AssignTaskEmailJobHandler>()`. Both are unconditional, because all their dependencies are. |
| `DecisionCommitFollowUps` | New 5-argument constructor (DI takes the longest). The 4-argument constructor is kept as an overload so existing rigs and Moq proxies are unchanged; Castle ignores optional parameters. |
| Wizard (`Spaarke.UI.Components`) | A Boolean renders as a Fluent `Checkbox`, ticked unless the value is `"false"`; its starting value is the plan's `defaultValue`. It is always posted as a boolean. Replay shows Yes/No. `WARNING_TEXT` gains the two warning codes, which show as the quiet warning on Confirm. |

**Tracked communication (POML "state it in the PR"):** yes. Both paths go through `CommunicationService.SendAsync`. Each writes a `sprk_communication` best-effort after the send, filed under the matter through `Associations`, with `sprk_correlationid = assign-task-{taskId:N}`. The job reads that id back before sending.

## Acceptance criteria → proving tests

| AC | Proof |
|---|---|
| 1. `notify` checkbox on Assign Task only, ticked by default | `DecisionActionCatalogTests.AssignTask_IsADecideLaneActionNotANextStep_WithTheD178Shape` (updated)<br>`.OnlyAssignTask_HasSendEmailNotification_AYesNoTickedByDefault_D194`<br>`.AssignTask_ThePlanDtoSendsSendEmailNotificationTickedByDefault_AsABoolean_D194`<br>jest `DecisionWizard.assignTaskNotify`: "shows a checkbox, ticked by default", and must-not-fire "Create budget's step has no checkbox" |
| 2. One email to the contact (delegated first, then the system mailbox), with link, What to do and Due encoded; one bell with the link | `AssignTaskNotifierTests.NotifyOn_SendsOneEmailAsTheAssigner_ToTheBoundContact_WithTheMatterLink_WhatToDo_AndDue_AllEncoded`<br>`.TheAssignersSendFails_QueuesOneSystemMailboxJob_WithIdsOnly_AndTheJobSendsItOnce(graph\|consent\|timeout)`<br>`.TheSingleLinkedUser_GetsOneBell_WithTheMattersLink_AndNoDecisionContent`<br>`.NoDueDate_SaysSo`<br>`DecisionCommitServiceTests.AssignTask_NotifyOn_HandsOneNoticeAfterTheRecordAndTheClose_D17_D194`<br>`.AssignTask_NotifyOmitted_IsOn_D194` |
| 3a. notify false sends nothing | `DecisionCommitServiceTests.AssignTask_NotifyOff_HandsNothing_MustNotFire(false\|"false"\|"FALSE")`<br>jest "posts notify: false when it is unticked" |
| 3b. Contact with no email gets no email | `AssignTaskNotifierTests.AContactWithNoUsableEmail_GetsNoEmail_AndTheWarningSaysSo_MustNotFire(null\|""\|"not-an-address")`<br>`.AContactThatNoLongerExists_GetsNoEmail_MustNotFire` |
| 3c. Zero or several linked users get no bell | `AssignTaskNotifierTests.NoLinkedUser_NoBell_ButTheEmailGoes_MustNotFire`<br>`.SeveralLinkedUsers_NoBell_MustNotFire`<br>`.OnlyADisabledLinkedUser_NoBell_MustNotFire` |
| 3d. A failure never changes the result, the event or the DR | `DecisionCommitServiceTests.AssignTask_ANotificationFault_LeavesTheDecisionRecorded_TheRecordAndTheSignalUnchanged_MustNotFire`<br>`AssignTaskNotifierTests.ABellOrLinkReadFault_NeverThrows_AndTheEmailIsUnaffected_MustNotFire`<br>`.TheJobCannotBeQueued_ReturnsTheNotSentWarning` |
| 4. The DR is still the last write; existing commit-order tests unchanged | `AssignTask_NotifyOn_…` asserts `Writes == execute, record, close`, with notify after both. The existing `DecisionCommitServiceTests` pass unchanged (the only edit is `sealed` → `sealed partial`). |

**AC5 — tests beyond the stated contract, one line each:**
- `AValueThatLooksLikeAPlaceholder_IsText`: the one-pass fill is what keeps user text from injecting template content.
- `TheTemplate_IsEmbedded_AndBuildEmailSaysNothingButTheRecord_WhatToDo_AndDue`: pins the exact body shape, so no decision content creeps in.
- `TheJob_SendsNothing_WhenAnEmailForTheTaskIsAlreadyRecorded` and the redelivery half of the fallback theory: "never send both" on the job side.
- `TheJob_ASendFault_IsAFailureToRetry_AndAnUnreadablePayloadIsPoisoned`: the ADR-004 outcome contract.
- `TheLogs_CarryRecordIds_NeverTheAddress`: the POML security constraint "log record ids only".
- `AssignTask_NotifyNotAYesNo_IsRefusedBeforeAnyWrite`, `AssignTask_TheNotifiersWarnings_ReachTheResult`, `AssignTask_AStepThatDidNotFinish_IsNotNotified_MustNotFire`: the new value rule and the warning hand-off at the route.

## Test runs

- **Fail before / pass after:**
  - Server: with the original `DecisionCommitService.cs` and `DecisionPlanEndpoints.cs` swapped back in, 6 of the 10 new route and DTO tests fail. The 4 must-not-fire cases pass by construction.
  - `AssignTaskNotifierTests` and the catalog shape tests do not compile before the change, since they test new types and members.
  - Client: with the 4 original wizard source files swapped back in, 4 of 5 jest tests fail. The must-not-fire test passes.
- **Sprk.Bff.Api.Tests, filtered** (`Signals | OntologyAdmin | KnowPromotion | Decision | Notification | Jobs | AssignTask`): 3531 passed, 3 skipped, 0 failed (after the rebase).
- **Spaarke.ArchTests, full** (built explicitly first): 1609/1609 passed after the rebase (1593/1593 before it).
- **jest `src/components/DecisionWizard`:** 11 suites, 178 tests, all pass.
- **tsc (`Spaarke.UI.Components`):** 9 errors, all pre-existing and none in changed files. They are unresolved `@spaarke/auth` / `@spaarke/sdap-client` modules in AccessGrantModal, FileUploadService and others, because sibling packages are not built in this worktree.
- **eslint (changed files):** 0 errors. The 2 warnings are pre-existing unused-disable lines that this change did not touch.
- **`dotnet format whitespace`:** applied to the changed C# files.

## Publish size (bff-hygiene item 4)

Measured from fresh worktrees `C:\w168m` (origin/docs/ontology-platform-design 58daec8c2b) and `C:\w168b` (this branch, rebased). Both were built with `dotnet publish -c Release … -o deploy/api-publish/` and zipped with `Compress-Archive -CompressionLevel Optimal`, PDBs included.

| | Size | Files |
|---|---|---|
| Base | 39,752,256 B (39.75 MB) | 192 |
| Branch | 39,766,895 B (39.77 MB) | 192 |
| **Delta** | **+14,639 B (+0.015 MB)** | — |

No packages were added, so there is no new CVE surface.

## Escalation trigger (POML)

| Trigger | Status |
|---|---|
| Delegated send needs a new Graph permission or consent | **No.** It is the same `SendMode.User` OBO `/me/sendMail` path the budget inquiry (`InquiryEmailSender`) already uses. |
| The system mailbox is not in ApprovedSenders on dev | **Not verifiable offline** (no Dataverse or config reads). It is a live check below. If it is not approved, the job fails and retries to dead-letter, the commit is unaffected, and no warning reaches the wizard. |
| The notification has to be sent before the DR | **No.** It runs after the record and the close. |

## Known limits (K-class, not defects on a real path)

- **Timeout overlap.** A delegated send that Graph accepts after our 10 s cancel may also arrive from the fallback job. The job's correlation-id check closes this only when the delegated path's tracked record landed.
- **Process death after the record and before notify.** No email goes: the notify is at most once, in-request. A replay takes the "finishing" path, which does not notify, by design, so it does not double-send.
- **Tracked-record failures.** The delegated path's tracked record is best-effort (`CommunicationService` behaviour). If it fails, the email still went, but there is no `sprk_communication`.

## Live checks for the main session (after deploy)

1. `Communication:DefaultMailbox` on dev is in `Communication:ApprovedSenders` (App Service config), so the fallback can send.
2. **UI test.** On a no-budget item, Assign Task to an internal contact with an email and one linked user. With the box ticked:
   - the contact receives one email from the assigner's mailbox, with the matter link, What to do and Due;
   - a `sprk_communication` exists with `sprk_correlationid = assign-task-{eventId:N}`, filed under the matter;
   - the linked user sees one bell, "A task was assigned to you", whose "Open matter" link opens the matter.

   Repeat with the box unticked: no email and no bell.
3. Optional, for the fallback path: an assigner without a mailbox or OBO consent. Expect one email from the system mailbox, through the `AssignTaskEmail` job.

## Out-of-scope observations (not fixed, D-106)

- None that affect this task.
