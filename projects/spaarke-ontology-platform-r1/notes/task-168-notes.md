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

## Fix round 1 — review findings and D-199 (2026-10-29)

> Merged `origin/docs/ontology-platform-design` first (`f2ff16d842`). The gate is D-199 Q1, and the fallback rule is D-199 Q2.

### One recipient gate (D-199)

`Services/Signals/AssignTaskRecipientGate.cs` is new and scoped. It runs in three places: `NotifyAsync`; `SendFromSystemMailboxAsync` (the request half); and the job's send time, where it re-reads `sprk_assignedto`, the `statecode` and the gate. Whoever it refuses gets no email and no bell. The task is still created, and the warning says why: `assignee_email_not_sent_no_access`, `_several_users` or `_unverified`. These are additive values in the existing warnings list, not a contract change. The wizard shows a sentence for each.

| Recipient | Gets the email when | Gets |
|---|---|---|
| Internal user (D-199 a) | The contact is active.<br>Exactly one enabled eligible user represents it (link plus oid confirmation, the `GrantAccessNotifier` rule; `ClassifyEligibility`).<br>A user flagged external is not on a Restricted root.<br>`SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(AsFlagged)` returns NotSecure or NotWalled.<br>The user has Read on the task (`RetrievePrincipalAccess`, app-only). | The record name (only via `InquiryRecordFacts.ResolveAsync`: Standard records and confirmed-internal addresses), the app link, "Assigned by {display name}", and a bell to that user |
| External contact (D-199 b) | `IsOnboardedWithEmail`.<br>The contact is not the assigner.<br>The core is a matter, project or work assignment.<br>`IAccessibleRecordSetService.ComposeForCiamContactAsync` (the portal's own grant term plus vetoes) gives Read. | The portal link (`GrantAccessNotifier.BuildPortalLink`) and the assigner's display name only. No record name, no app link, no bell |
| Anyone else | Never. Every check that cannot be answered refuses (fail closed) | — |

**Represents:** `ContactUserRepresentation` (uac-r2 task 193) is only on `origin/fix/uac-193-add-person`, not on master. The gate mirrors the `GrantAccessNotifier` logic instead: `AssignedLinkCandidate.Represents` plus `ContactBindingDecision.DecideBoundContact`. Swap it in when 193 merges. No new schema and no new privilege are needed: every read is an existing reader.

### Findings

| # | Class | Status | Change |
|---|---|---|---|
| 1 | F1 | **Fixed** | The record name and link come from the gate's recipient. Internal recipients use `InquiryRecordFacts.ResolveAsync` (the label only when Standard and confirmed internal). External recipients get the portal link and no name. The bell goes only to the gate's internal user. |
| 2 | F1 | **Fixed** | `IsInternalOnly = true` in `Request()`. Read back through the real `CommunicationService` send path: `sprk_isinternalonly` is true. |
| 3 | F3 | **Fixed** | `AssignTaskNotifierRegistrationTests` (real `CustomWebAppFactory` container, plus `ValidateScopes`): `DecisionCommitFollowUps` holds the resolved notifier, the notifier holds the resolved gate, and the `IJobHandler` set holds `AssignTaskEmailJobHandler`. There is also a direct test of `NotifyAssigneesAsync` (it hands each notice over, and a fault becomes the not-sent warning). |
| 4 | K2 | **Fixed** | The payload carries `AssignedByObjectId`. Both emails say "Assigned by {fullname}" (a name containing `@` is left out). The job sends with a sender context carrying the assigner's oid, so `sprk_sentby` is the assigner (read-back test). The name lookup excludes disabled users (ArchTest #1576). |
| 5 | K2 | **Fixed** | `AssignTaskEmailJob.IsPermanent`: an `SdapProblemException` 4xx or an `InvalidOperationException` (config) gives Poisoned. Other faults give Failure (retry). An unverified gate at send time gives Failure (retry). |
| 6 | K1 | **Known limit** | Timeout double-send. Graph `sendMail` has no idempotency key, and not falling back on timeout would trade a possible duplicate for a possible no-send (against D-194). |
| 7 | K3 | **Known limit** | Latency grows further: the gate adds `RetrievePrincipalAccess` (15 s bound) and `InquiryRecordFacts` (10 s deadline). Moving the send fully to a job would change D-194's in-request delegated send. That is an owner call. |
| 8 | K4 | **Known limit** | An old client shows a stray text box until the bundle updates. Transient. |

**Logging:** no log passes the exception object any more. Each log carries `ex.GetType().Name` and `ErrorCode(ex)` (the `SdapProblemException` code plus `graphErrorCode`, or "none"). A test asserts that the logs carry no address and no exception object.

### Tests

- **Must-fire:**
  - eligible internal (internal-only, "Assigned by", no UPN);
  - a record that isn't Standard is not named;
  - an onboarded external with a grant (portal link, no `main.aspx`, no name, no bell);
  - the job to an external contact gets the portal link.
- **Must-not-fire:**
  - walled;
  - secure non-member (no Read on the task);
  - flagged-external user on a Restricted root;
  - 7 checks that cannot be answered;
  - external with no grant;
  - external not onboarded (3 cases);
  - external who is the assigner;
  - several users;
  - disabled user only;
  - inactive or missing contact;
  - the fallback never sends to a refused recipient (at request time and at send time, internal and external);
  - the job re-reads a reassigned or closed task and an inactive contact.
- **Fail before / pass after:**
  - the wizard texts: 1 of 6 jest tests fail with the old `decisionDisplay.ts`;
  - the disabled-user lookup: 3 tests fail without `isdisabled = false`;
  - the gate tests do not compile against the old notifier, because they test new types.
- **Filtered `Sprk.Bff.Api.Tests`** (`Signals|OntologyAdmin|KnowPromotion|Decision|Notification|Jobs|AssignTask|GrantAccessNotifier|ChildRecordContainerResolution|GrantExternalContactEmail|Communication`): **5995 passed, 5 skipped, 0 failed**. `AssignTask` alone: 84/84 (also after the disabled-user fix).
- **`Spaarke.ArchTests`, full:** **1665/1665 passed**. The first run failed 1 of 1665, `SystemUserOidLookupDisabledGuardTests` (#1576), on the new assigner-name lookup. It was fixed in `aae28218ff`.
- **jest `DecisionWizard`:** 11 suites, 179 tests, all pass.
- **tsc `Spaarke.UI.Components`:** 9 errors, all pre-existing (the same set as round 0), none in `DecisionWizard`.

### Mutation check (every guard; each mutation applied alone, then reverted)

| Mutation | Tests failed |
|---|---|
| M1 walled passes | 2 |
| M2 no Read check on the task | 1 |
| M3 no Restricted bar | 2 |
| M4 an unverifiable wall passes | 2 |
| M5 an unanswered task Read passes | 2 |
| M6 external with no grant check | 2 |
| M7 external onboarding not checked | 3 |
| M8 external assigner not excluded | 1 |
| M9 several users allowed | 1 |
| M10 inactive contact allowed | 2 |
| M11 the job skips the gate | 3 |
| M12 the request ignores a refusal | 20 |
| M13 the job ignores reassignment | 1 |
| M14 not internal-only | 6 |
| M15 nothing poisoned | 3 |
| M16 external gets the app link and name | 2 |
| M17 the job's sender context is dropped | 4 |
| M18 the notifier is not registered | 2 |

All 18 mutations are caught. Re-running M17 exposed a vacuous null-conditional assertion (`?.Id.Should()`) in the read-back test, which was fixed before the final run.

### Publish size (bff-hygiene item 4)

Built with fresh detached worktrees: `C:\wt168m` (origin/master `5119519679`) and `C:\wt168b` (this branch `aae28218ff`). Each ran `dotnet publish -c Release -o pubout` and was zipped with `Compress-Archive`.

| | Zip | Raw | Files |
|---|---|---|---|
| Master | 39,791,548 B (39.79 MB) | 132,640,112 B | 192 |
| Branch | 39,814,980 B (39.81 MB) | 132,715,452 B | 192 |
| **Delta** | **+23,432 B (+0.023 MB)** | +75,340 B | 0 |

No packages were added, so there is no new CVE surface.

### Known limits and owner flags

- **K1, K3 and K4:** above.
- **K-1:** the gate's "represents" mirrors `GrantAccessNotifier`. Replace it with `ContactUserRepresentation` when uac-r2 193 merges.
- **From header (owner flag):** the delegated send goes from the assigner's mailbox, so an external assignee sees the assigner's address in From. This follows from D-194/D-199 Q2. The body names the assigner by display name only.
- **Portal task visibility:** verified in code only. The portal lists a matter's `sprk_event` rows through `sprk_regardingmatter` (`ExternalDataService.BuildEventsQuery`), and the executor files the task under `sprk_regarding{type}`. Live check: a granted external contact sees the task on the matter in the portal. There is no deep link to the task (DI-135, #2445).

### Live checks added

4. An internal assignee on a walled or secure matter where they are not a member: no email and no bell. The wizard says "no access".
5. An onboarded external contact with an active grant: one email with the portal link and no matter name. The task shows on the matter in the portal.

## Fix round 2 — review of the fix-round-1 diff (2026-10-29)

Two reviews of `f2ff16d842..e8b460a9ea`. Every F item is fixed, and so is the K item. The From behaviour is unchanged: the assigner's mailbox in the request, `DefaultMailbox` in the job. The owner decides that separately.

| # | Class | Change |
|---|---|---|
| 1 | F (uac-r2) | **External assignee: the portal's per-row rule for THIS task.** The gate checked Read on the core only, so a task also filed to a secure root the contact can't read was emailed with a link to a page that withholds it. After the matter Read, `DecideExternalAsync` now asks `PortalListsTaskAsync`. That calls `ExternalDataService.CheckLiveParentsAsync(DeletableKind.Event, task, principal)`, which is already public and is the single-row form of the events list's rule. It is the same `ReadLiveParentRootsAsync` + `ReadSecureRootFlagsAsync` + `ExternalMultiRootRule.ChildRowReadable`, reused unchanged. It runs on the principal the portal composes: `CiamContactPrincipalStrategy.ComposeAsync` is a new internal static, lifted out of `ResolveAsync` (the resolver calls it, same behaviour). Withheld gives no access. A failed read, or withheld while a composition is capped, gives Unverified. `ExternalDataService` is not changed, and no schema or privilege is needed. |
| 2 | F1 | `AssignTaskEmailJob.IsPermanent`: `>= 400 and < 500 and not 408 and not 429`. Graph throttling, `DAILY_SEND_LIMIT_REACHED` and a timeout are retried. |
| 3 | F1 | `RestrictedAsync`: a table that is not a matter, project or work assignment (`!IsFlagBearingRootType`) answers null, which means Unverified for a user flagged external. It is never read. An empty answer is no longer "not Restricted". The comment is corrected. No existing helper reads such a table's root through the filing walk for this purpose, so it fails closed. |
| 4 | F3 | New `AssignTaskRecipientGateSeamTests`: each seam runs over its real dependency. <br>• Wall: the REAL `SecureShareNoAccessGuard`. AsFlagged is pinned: an entry on a non-secure matter does not wall. <br>• Task Read: only the `sprk_events` set answers. <br>• Restricted: the flag stub returns Restricted, Standard, unreadable, absent or a throw, plus no-flag tables. <br>• Grants: a capped set with a missing record gives null. <br>• Portal: the fake Dataverse of the events-list tests. |
| 5 | K2 | **The job gates and links the CURRENT filing.** The send-time re-read selects `CoreAncestorLookups` (the `sprk_regarding{type}` the executor writes). `TryCurrentFiling` keeps the queued record while its lookup still names it. Otherwise it uses the one core the task is filed under (or none). Several others means no email. |

**Item 1, same verdict as the list:** a task under a readable matter and a secure project is refused. A test asserts that `GetEventsAsync(Matter)` lists only the control row for the same principal and the same fake Dataverse.

**`ContactUserRepresentation`:** the swap is filed as **#2457 (DI-136)**, with uac-r2's guidance: use `DecideOnLink`, and read Restricted up front.

### Tests

- **`AssignTask` filter:** 120/120. In round 1 it was 84; the new tests are 11 in the notifier and 25 seam tests.
- **Wider filtered `Sprk.Bff.Api.Tests`:** 6788 passed, 5 skipped, 0 failed. The filter is round 1's, plus `CallerPrincipal`, `Ciam`, `ExternalRootEventRead`, `ExternalLiveParentRead`, `ExternalDeleteOwn` and `ExternalTodoScope`.
- **`Spaarke.ArchTests`, full:** 1665/1665.
- **Intermediate commit `6789b73bd2`:** builds (test project).

### Mutation check (each alone, then reverted; `AssignTask` filter)

| Mutation | Failed |
|---|---|
| M1 portal-withheld task still emailed | 2 |
| M2 undecided portal rule passes | 1 |
| M3 capped composition ignored | 1 |
| M4 asks the to-do table | 4 |
| M5 Unavailable read as listed | 1 |
| M6 408/429 poisoned (the old rule) | 3 |
| M7 no-flag table read (type check removed) | 5 |
| M7b empty answer read as "not Restricted" (the old line) | 1 |
| M8 unreadable flag read as its value | 1 |
| M9 wall asked BeingSecured | 2 |
| M10 `sprk_event` instead of `sprk_events` | 1 |
| M11 capped grant set read as complete | 1 |
| M12 filing not re-read (the old select) | 10 |
| M13 queued record always used (the old line) | 4 |
| M14 several filings take the first | 1 |

All 15 are caught. M7 first survived, because the stub answered empty. The no-flag-table test now also covers a reader that answers, and it asserts the read is never made.

### Publish size (bff-hygiene item 4)

Fresh detached worktrees: `C:\wt168m` (origin/master `3d54ac845d`) and `C:\wt168b` (`04c9ae45e7`). Each was built with `dotnet publish -c Release`, then zipped with `Compress-Archive`. Both were removed with `Remove-WorktreeSafe.ps1`.

| | Zip | Raw | Files |
|---|---|---|---|
| Master | 39,791,796 B | 132,640,648 B | 192 |
| Branch | 39,817,423 B | 132,723,556 B | 192 |
| **Delta** | **+25,627 B (+0.026 MB)** | +82,908 B | 0 |

No packages were added, so there is no new CVE surface.

## Fix round 3 — the WorkforceHome plane (R3, 2026-10-29)

One finding from R3, the external-access owner. The gate under-notified, which fails closed and is not a leak. It always composed an external assignee's access as a CIAM sign-in. A WorkforceHome contact signs in with their home directory, and the portal resolves that sign-in as a ContactOnly workforce principal. Its composition (`ComposeContactPlaneAsync(…, includeDerivedMemberTerms: true)`) adds standing-grant membership and organization expansion. So a WorkforceHome assignee whose access came only through one of those terms saw the task in the portal but got no email. D-199 says an external contact is emailed exactly when the portal would show them the task. The From behaviour is unchanged.

| # | Change |
|---|---|
| 1 | **Composition by plane, composed once.** `DecideExternalAsync` composes ONE principal (`ComposePortalPrincipalAsync`). The matter Read (`ContactRights`, now over that composition's set for the grant table) and the events list's per-row rule (`PortalListsTaskAsync`) are both asked of it. Before, the gate made four compositions per decision; now it makes three. `PortalPlaneOf` picks the plane from `ContactBindingRow.Binding` (`ContactBindingDecision.ReadBinding`, the reader every binding path uses). A Bound binding with plane WorkforceHome gets `WorkforcePrincipalStrategy.ComposeAsync`, over the `WorkforcePrincipalResolution.ForContact` principal that the home resolver builds. Everything else gets `CiamContactPrincipalStrategy.ComposeAsync`, the smaller composition: External, a legacy oid with no marker, an unknown marker value, an unproven empty plane, unbound, and Spaarke-tenant Workforce. The round-2 mapping is kept: Readable means email; Withheld means NoAccess, or Unverified when capped; Unavailable or a throw means Unverified. |
| 2 | **Workforce composition lifted.** `WorkforcePrincipalStrategy.ComposeAsync` is a new internal static, next to the CIAM one. It makes the same three `ComposeAsync(principal, …)` calls in the same order, uses the same `FromReadBearing` mapping, and sets the same derived fields (`UndatedAccessTermEntityTypes`, `DataverseConfirmsWrites`, `TruncatedRootEntityTypes`). `ResolveAsync` calls it, and the `[WF-AUTH] … resolved … (project sources: …)` line stays in the strategy. `CiamContactComposition` became `ContactPrincipalComposition`, which carries the three sets (`Capped`, `SetFor`). `ComposeContactPlaneAsync` is unchanged, including the inactive-contact and removed-marker guards. |
| 3 | **A licensed user's home sign-in.** The portal asks the stored link FIRST (`WorkforcePrincipalResolver.ResolveHomeAsync`, `case HomeLinkOutcome.Enabled` → `ForSystemUser`). A WorkforceHome contact whose home pair a licensed user carries is therefore never this contact in the portal (Kind = SystemUser). The gate's "represents" step (`DecideCoreAsync`: `ReadLinkCandidatesAsync` + `Represents`) reads `sprk_primarycontact` and the Spaarke-tenant `azureactivedirectoryobjectid` only, NOT the field-secured home pair. Such a contact could therefore reach the external branch. The gate now makes the same read for a WorkforceHome contact (`HomeLinkAsync`: `IContactIdentityStore.FindHomeLinkedSystemUsersAsync` + `ContactBindingDecision.DecideHomeLink`) before composing. NotLinked goes on. Enabled is Unverified: the person's view is the user's, not the contact's. Disabled is NoAccess, because the portal denies that sign-in. Denied (ambiguous, failed, column missing, a throw, or an unparseable home tenant) is Unverified. `GrantAccessNotifier` has the same gap (filed below), so there was no existing behaviour to copy. Unverified is the fail-closed choice of the two the reviewer allowed. |

**Where a contact linked to a licensed user is caught:**
- **Linked by `sprk_primarycontact`, or by the Spaarke-tenant oid:** `DecideCoreAsync` (`AssignTaskRecipientGate.cs` ~223-237). If any candidate `Represents` the contact, the external branch is never entered and the decision goes to `DecideInternalAsync`. The test `AWorkforceHomeContact_LinkedToALicensedUserBySprkPrimaryContact_IsDecidedOnTheInternalPath_NeverTheExternalOne` pins this.
- **Bound on the Spaarke-tenant Workforce plane:** `IsOnboardedWithEmail` refuses it (`GrantAccessNotifier.cs:345`).
- **Linked by the home pair only:** item 3 above.

### Tests

- New `AssignTaskRecipientGatePlaneTests`, 22 cases. They run over the REAL `AccessibleRecordSetService` (doubles for membership, standing grants, participation reads and the removed marker only) and the REAL `ExternalDataService` over the events-list fake Dataverse.
  - **Must fire:** a WorkforceHome contact reached only by standing grant, or only by organization expansion, is emailed. The events list served to its home sign-in (the REAL `WorkforcePrincipalStrategy.ResolveAsync` principal → `GetEventsAsync`) lists the task. The CIAM composition gives None (control).
  - **Must not fire:**
    - A CIAM contact with the same access gets NoAccess, and its CIAM list is empty.
    - A removed contact composes an EMPTY principal and gets NoAccess on each plane. The control is the same contact, unmarked, with a grant: it gets an email.
    - Eight bindings → plane table. Every non-WorkforceHome binding uses the CIAM composition (Strict mock), with three compositions per decision.
    - Six home-link outcomes → refused, never composed.
    - An unreadable home tenant → Unverified.
    - Internal-path pinning (above).
- New `WorkforcePrincipalStrategyComposeTests`, 3 cases (resolver regression). For ContactOnly and for SystemUser: the three calls in order; every principal field; the Read-bearing mapping (a None entry and a Write-only entry dropped); the derived sets; and exactly one `[WF-AUTH]` resolved line with counts and sources. The lifted static matches.
- Round-2 tests adapted to the new seam signatures, assertions unchanged: the notifier harness stubs `ComposePortalPrincipalAsync`/`ContactRights`/`PortalListsTaskAsync`, and the seam tests compose once.
- **Filtered `Sprk.Bff.Api.Tests`** (`AssignTask|CallerPrincipal|Ciam|Workforce|AccessibleRecordSet|ExternalLiveParentRead|ExternalRootEventRead`): 1369/1369. The `AssignTask` filter alone is 142/142; it was 120 in round 2, and the 22 new cases account for the difference.
- **`Spaarke.ArchTests`, full:** 1665/1665 on the clean build. One earlier run on the same binaries failed 1 of 1665 while a publish was running alongside it; a rerun on the same binaries passed, so it was a load flake, not this change.

### Mutation check (each alone, then reverted; gate + notifier + resolver-regression filter, 115 tests)

| Mutation | Failed |
|---|---|
| Plane switch always CIAM (the old behaviour) | 10: must-fire, plane table, home-link and tenant cases |
| Plane switch always WorkforceHome | 19: CIAM must-not-fire, removed contact, plane table, and the round-2 external notifier/seam tests |
| Unknown or unreadable plane read as WorkforceHome | 5 (plane table) |
| Home-link read skipped (the old behaviour) | 7 |
| Enabled licensed-user link let through | 1 |
| Resolver: project and matter compositions swapped | 2 |
| Resolver: `[WF-AUTH]` resolved line dropped | 2 |

All 7 are caught. Each mutation was built fresh, because the mutated file was rewritten each time. The final restore needed a forced rebuild (`touch`): the restored file kept an older mtime, so the incremental build first reused the last mutated DLL. Every count above comes from the clean rebuild.

### Publish size (bff-hygiene item 4)

The publish was run from fresh detached worktrees: `C:\wt168m` (origin/master `7008180304`) and `C:\wt168b` (`bd32be448f`). Each ran `dotnet publish -c Release`, and the output was zipped with `Compress-Archive`. Both worktrees were removed with `Remove-WorktreeSafe.ps1 -Force`; the only untracked content was `pubout/`.

| | Zip | Raw | Files |
|---|---|---|---|
| Master | 39,814,110 B | 132,703,428 B | 192 |
| Branch | 39,819,704 B | 132,728,364 B | 192 |
| **Delta (whole task 168 vs master)** | **+5,594 B** | +24,936 B | 0 |

No package was added.

### To file

- `GrantAccessNotifier.NotifyContactGrantAsync` has the same home-link gap. A grant to a WorkforceHome contact whose home pair a licensed user carries emails the contact as external, although the portal resolves that person as the user. This is outside this task's scope. It is reported to the main session to file for the external-access owner (not filed from here).
