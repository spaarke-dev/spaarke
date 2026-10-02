# Task 146 — server child writers own their rows through the one resolver (C10 part 2, #1034)

> Branch `task/uac-r2-146` (base `integ/uac-r2-batch2` @ `2a9b4b26c`). Status: **code complete, not deployed**.
> Ships together with task 149. Deploy order with task 152 accepted as B2. Live steps are manual gates (§7).

## 1. Outcome

Every BFF create of a child or content row decides `ownerid` through `IRecordOwnershipResolver`. So does every
re-file (an update that files a child under a new parent). The rules:

- **Secure if any parent is secure.** The resolver checks every parent lookup the row carries, for example a
  document's `sprk_project` and its `sprk_relatedproject`. If any parent is owned in the Secure Record business
  unit, the row goes to the named `Secure Record Owners` team (task 144). Otherwise it goes to the primary parent's
  business-unit default team.
- **Flagged but not isolated refuses.** A root with `sprk_issecure = true` that is not owned in the Secure business
  unit refuses. This is a failed or interrupted provisioning, C11.
- **Missing or unreadable parents refuse.** So do an ambiguous Secure business unit and a missing team.
- **Faults are not refusals.** A Dataverse fault propagates as the caller's 5xx.
- **Unfiled rows.** A row with no parent at all falls back to the acting user's team, or keeps its creator for
  communications (E1).
- **Re-files.** A re-file applies the change, then assigns the new owner in a separate write and reads it back.

Each writer refuses in its own existing error contract:

| Writer kind | What a refusal does |
|---|---|
| HTTP routes | 409 ProblemDetails with `reasonCode`, from the one helper `ProblemDetailsHelper.RecordOwnerRefused` |
| Background and best-effort writers | Log and skip |
| AI task core | Degraded success, `Guid.Empty` |
| Inbound email | **Held** (owner amendment R3): retry, then dead-letter, then alert administrators. Nothing is created. |

`RecordOwnerAssignmentCensusTests` fails the build on any unlisted create of a child table. Each guard was proven to
bite by seeding (§8).

## 2. Resolver changes (`Services/Dataverse/RecordOwnershipResolver.cs`, extended in place, no second resolver)

- **Context.** `RecordOwnershipContext` gains three members:
  - `Parents`, with `ForChild` (every ownership-parent lookup on the row), `ForParents` and `ContentOf`.
  - `WhenUnfiled = KeepCreator` (E1).
  - `KeepCreatorUnlessTargetIsTeamOwned`, for content rows of a creator-owned unfiled parent.
- **Resolution.** `ResolveOwnerAsync` returns `RecordOwnerResolution`: Owned, Refused (with a stable
  `RecordOwnerRefusal` code) or Unchanged.
  - `ResolveOwningTeamAsync` is kept and delegates to it.
  - `ApplyTo(Entity)` writes the owner. It refuses to apply a refusal.
- **Reparent.** `ReparentAsync(RecordReparent, applyChange)` runs these steps:
  1. Read the row with all its columns.
  2. Overlay the parent changes and the `InheritedParents`.
  3. Resolve. A refusal stops here and writes nothing.
  4. Apply the change.
  5. Assign the owner in a separate write.
  6. Read the owner back.
- **Ownership parents.** These come from live metadata (§4): the three roots, `sprk_servicerequest` and the user-owned
  children. A row that is already a child of a child follows its parent, which is how a grandchild of a secure root
  becomes secure. `IsReparentableChild` excludes the roots, because a root's own ownership belongs to provisioning
  (owner S6).
- **Exception type.** `RecordOwnerUnresolvedException` carries `EntityLogicalName`, `RefusalCode` and `Reason`, for
  writers whose contract is to throw.

## 3. Census (Sprk.Bff.Api, Spaarke.Dataverse, Spaarke.Core; 2026-10-02)

### 3a. Create sites the ArchTest scans (`RecordOwnerAssignmentCensusTests.Census`)

| Table | File:line | Disposition | Owner rule / reason |
|---|---|---|---|
| sprk_document | DataverseServiceClientImpl.cs:276 | Seam | `CreateDocumentAsync` throws without `OwningTeamId`; callers resolve it (Office, worker, email attachments, POST /documents) |
| sprk_analysis | DataverseServiceClientImpl.cs:454 | Seam | `CreateAnalysisAsync` throws without a team; AnalysisEndpoints:189/1223/1438, AnalysisResultPersistence:124, AppOnlyAnalysisService:117 |
| sprk_analysisoutput | DataverseServiceClientImpl.cs:626 | Seam | throws without `OwningTeamId`; owned like its analysis (AnalysisResultPersistence:349, AppOnlyAnalysisService:787) |
| sprk_emailartifact | DataverseServiceClientImpl.cs:1984 | Seam | `RequireArtifactOwner`; UploadFinalizationWorker passes its document's team |
| sprk_attachmentartifact | DataverseServiceClientImpl.cs:2086 | Seam | same |
| sprk_event | DataverseWebApiService.cs:461 | Seam | `CreateEventAsync` throws without `OwningTeamId`; EventEndpoints:388 |
| sprk_eventlog | DataverseWebApiService.cs:597 | Routed (EventEndpoints) | content of the event (`ContentOf`), EventEndpoints:804 |
| sprk_analysis | ObservationMirrorMapper.cs:141 | Routed (DataverseObservationMirror:185) | `ForChild`; refusal skips the mirror |
| sprk_communication | CommunicationService.cs:871, 1770, 1948 | Routed | `ForChild` + KeepCreator (E1); refusal throws before the create |
| sprk_document | CommunicationService.cs:507, 2260, 2459 | Routed | content of the communication, resolved before the SPE upload |
| sprk_communicationattachment | CommunicationService.cs:2405 | Routed | content of the communication |
| sprk_communication | EmailUploadCaptureService.cs:254 | Routed | association evaluated BEFORE the create; refusal skips the capture |
| sprk_communication | IncomingCommunicationProcessor.cs:684 | Routed | evaluated before the create; refusal → HOLD (§5 R3) |
| sprk_document | IncomingCommunicationProcessor.cs:984, 1169 | Routed | owned like the email |
| sprk_communicationattachment | IncomingCommunicationProcessor.cs:1010 | Routed | owned like the email |
| sprk_document / sprk_communicationattachment | MessageAttachmentMaterializer.cs:274 / 286 | Routed | content of the message; resolved before the upload; refusal = 409 rejection |
| sprk_communicationparticipant | CommunicationParticipantIndexer.cs:212 | Routed | content of the communication; refusal writes no rows |
| sprk_emailreviewlog | CommunicationEnrichmentService.cs:1168, 1454, 1510, 1634, 1922, 1978 | Routed | content of the communication; refusal throws inside the best-effort step |
| sprk_emailreviewlog | CommunicationProposalApplyService.cs:720, 759, 825 | Routed | resolved BEFORE the target write; 409 |
| sprk_emailreviewlog | CommunicationCreateTaskApplyService.cs:627, 787, 834 | Routed | resolved BEFORE the task create; 409 |
| sprk_communicationthread | ThreadResolver.cs:153, 585, 715 | Routed | record thread → its record's team (S6); Direct / master keep creator (E2) |
| sprk_document | ComposeCreateOnSavePromoter.cs:184 | Routed | inherited filing links (not the canonical dedup link) or the caller; SPE alternate key not relaxed |
| sprk_event | TaskActionCore.cs:107 | Routed | `ForChild` over regarding + core stamps; supplied OwnerId no longer owns a filed task (B2) |
| sprk_fileversion | DocumentCheckoutService.cs:968 | Routed | content of the document; 409 on refusal |
| sprk_todo | OfficeService.cs:1937 | Routed | now `ForChild` over regarding, stamps AND carriers (secure-if-any; was first-target only) |
| sprk_todo | TodoGenerationService.cs:832 | Routed | owned from the source event; refusal counts the rule failed |
| sprk_communication | Channels/MessagingIngestor.cs:199 | Waived — Pending (E1) | names no parent at create; filed only by the thread JOIN, which re-files it |
| sprk_communicationthread | Access/DirectThreadAccessService.cs:82 | Waived — Permanent (E2) | Direct two-party thread, per-participant privacy |
| sprk_communicationchannelref | Threads/MessagingThreadKeyStrategy.cs:63 | Waived — Permanent | ACS transport key row, no content, app-only reads |
| sprk_processingjob | DataverseServiceClientImpl.cs:1679 | Waived — Permanent | Office job tracking row, authorized by `sprk_initiatedby`; not a child in live metadata |
| sprk_workassignment | Api/WorkAssignmentEndpoints.cs:71 | Waived — Pending (§6 deviation 1) | a ROOT; and it cannot file under a matter today (see §6) |

### 3b. Writers the scanner cannot see. Each is still routed (`UnscannedWriters`).

| Writer | Shape | Rule |
|---|---|---|
| Finance/InvoiceReviewService.cs:630, 658 | create-by-upsert PATCH (sprk_invoice) + document link | invoice owned by the matter's team (task 130); **linking the document is now a reparent** (inherited parent = matter; resolved before the invoice is created; a resumed confirmation completes it after the status write) |
| Finance/SignalEvaluationService.cs:154 | create-by-upsert PATCH (sprk_spendsignal) | matter's team; refusal skips |
| Finance/SpendSnapshotService.cs:138 | keyed UpsertRequest (sprk_spendsnapshot) | parent's team; refusal skips |
| Api/ExternalAccess/ExternalProjectDataEndpoints.cs:499 | POST to computed URL (document, event, to-do) | root's team, resolved before the SPE upload; ExternalDataService `RequireOwner` refuses an empty team |
| Api/Events/EventEndpoints.cs:388, 541, 804 | seam caller + event re-file + log rows | regarding change = reparent; log rows `ContentOf` |
| Api/DataverseDocumentsEndpoints.cs:53, 247 | POST (task 080) + **PUT re-file** | an update setting a parent lookup = reparent; 409 |
| Api/Ai/RecordMatchEndpoints.cs:134 | associate = re-file | reparent; 409 |
| Services/Dataverse/DataverseUpdateHandler.cs:53 | generic update (EntityReference values) | reparent for reparentable children |
| Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs:158 | generic update (lookups) | reparent; ActionSeam returns a typed failure on refusal |
| Services/Communication/IncomingAssociationResolver.cs:388 | filing a communication (regarding writes) | reparent, owner re-derived before the regarding is written |
| Services/Communication/CommunicationService.cs (AssignExplicitThreadAsync) + ThreadResolver.cs:263 | joining a record thread | the one shared step `AssignToThreadReconcilingOwnerAsync`; refusal leaves the message unthreaded |
| Services/Email/EmailAttachmentProcessor.cs:281 | seam caller | parent document + association |
| Workers/Office/UploadFinalizationWorker.cs:137 | seam caller + artifacts | carried team wins |
| Office (task 080, verified not redone): OfficeService.cs:405, 1788; RecordCreationService.cs:223 | — | invoice quick-create names no parent → acting user's team |

### 3c. Not routed. Escalated, see §5.

| Writer | Why |
|---|---|
| Services/Ai/Handlers/DataverseCreateRecordHandler.cs:245 | run-as-user (OBO) create, "User-OBO ONLY" spec rule |
| Services/Ai/Handlers/EmailDraftToolHandler.cs:352 | run-as-user (OBO) sprk_communication create |

Per-user by design, and not children: `appnotification`, `sprk_notificationoutbox`, `sprk_workspacelayout`,
`sprk_aichatmessage`/`summary`. Organization-owned: `sprk_affinity`, `sprk_precedent`, `sprk_userentityassociation`.

## 4. Live metadata: parent lookups for the three roots (spaarkedev1, 2026-10-01, read-only GETs)

| Child | Root lookups |
|---|---|
| sprk_agreement | matter.sprk_regardingmatter, project.sprk_regardingproject |
| sprk_analysis | regardingmatter, regardingproject, regardingworkassignment |
| sprk_billingevent / sprk_budget / sprk_invoice / sprk_kpiassessment / sprk_spendsignal / sprk_spendsnapshot | matter.sprk_matter, project.sprk_project |
| sprk_communication / sprk_communicationthread / sprk_event / sprk_memo / sprk_todo / sprk_servicerequest | regardingmatter, regardingproject, regardingworkassignment |
| **sprk_document** | **sprk_matter, sprk_relatedmatter, sprk_project, sprk_relatedproject, sprk_workassignment, sprk_relatedworkassignment** (six) |
| sprk_reportcard | regardingmatter, regardingproject |
| sprk_workassignment | regardingmatter, regardingproject (a ROOT, see §6) |

All of these tables are UserOwned. Every relationship has `CascadeAssign = NoCascade`, so assigning a root never moves
its children. This is the reason this task exists.

## 5. Escalations. Each is a first-class stop for that part.

- **E1: unfiled communications keep their creator.** Inbound, chat and outbound messages that name no record keep
  the creating identity instead of taking the acting user's team. There are three reasons:
  - the "inbound never dropped" contract;
  - the per-user master thread (Tier 3), which keys on the message's owning user;
  - Direct-thread privacy, which rests on per-participant shares of the row.

  Filed communications are routed (secure-if-any). **Owner to confirm.** MessagingIngestor is waived as Pending on
  this decision.
- **E2: Direct and master threads stay per-participant or per-user** (constraint "per-user artifacts"). Record
  threads follow S6.
- **E3: BU default teams cannot own review logs.** The live default teams `Spaarke` and `Spaarke Business Unit 1`
  hold no `prvReadsprk_EmailReviewLog`. Read-only check, 2026-10-01: both `Spaarke Basic User` copies lack it. So
  Dataverse will refuse a review-log row owned by an ordinary communication's team. **Deploy gate G146-2** (§7) must
  run first. Without it, enrichment and the apply endpoints fail to write audit rows for ordinary mail. The data is
  not exposed.
- **Run-as-user AI handlers.** The handlers are DataverseCreateRecordHandler and EmailDraftToolHandler (trigger 5).
  - Owner S1 chose app-only creates owned by the Secure team, with the person recorded in a "for" column.
  - Both handlers carry a spec **"User-OBO ONLY" MUST** rule.
  - Switching them needs two things: an as-user authorization pre-check, because an app-only create bypasses the
    caller's Create privilege, and a named "for" column per table (S1: a new column needs a §11 justification).
  - Both are security-sensitive (CLAUDE.md §6). **Not changed.** Today these rows are user-owned in the user's
    business unit. They are not isolated when filed to a secure record.
  - Recommendation: a follow-up task that implements S1 with an `IsAllowedAsUser` pre-check (RetrievePrincipalAccess
    / `CallerRecordAccessProbe`) plus `sprk_assignedto` where the table has it.
- **Trigger 3 (two secure roots with different sharee sets):** secure-if-any is implemented. The question of which
  sharee set to mirror goes to task 149 and was not chosen here.
- **R3 inbound hold (answered, implemented).** `IncomingCommunicationJobHandler.HoldAsync` handles a refusal:
  - Before the last attempt: Failure (retry).
  - At the last attempt: Poisoned (dead-letter, "left unprocessed in the ingestion queue"), a Critical log, and one
    `appnotification` per `Communication:OwnershipHoldAlertUserIds` with the refusal code and the correlation id.

## 6. Deviations

1. **WorkAssignmentEndpoints (S6 b) was not changed.**
   - A read-only metadata check on 2026-10-02 showed `sprk_workassignment` has no `sprk_matterid`. Its matter lookup
     is `sprk_regardingmatter`.
   - So `POST /api/v1/work-assignments` with a MatterId **already fails** at Dataverse. It can never create a work
     assignment under a secure matter: no leak, and a pre-existing bug.
   - S6 b, inline secure provisioning at creation, needs task 144's `ProvisionProjectEndpoint` flow as a reusable
     service. That refactor is out of this task's file set. **Handoff:** fix the lookup name and provision inline,
     in a follow-up.
2. **Event log rows.** `IEventDataverseService.CreateEventLogAsync` gained a positional `Guid? owningTeamId`. `null`
   is allowed only when the resolver answered Unchanged, for an event that is not team-owned.
3. **Compose:** the canonical (dedup) link is not treated as a parent. Only inherited filing links are.
4. **ADR-038 cleanup:** the three constructor null-check tests in `DataverseObservationMirrorTests` were deleted
   when the constructor gained the resolver.
5. **Shared harness:** `RecordOwnershipResolverTests.FakeDirectory` was moved to
   `TestInfrastructure/OwnershipDirectory.cs` so the integration tests drive the same resolver over the same
   directory (§11 reuse, no second harness).
6. **Office to-do:** the owner is now secure-if-any over the regarding, its core stamps and its document/email
   carriers. It was first-target-only (task 080). Single-parent behaviour is unchanged.

## 7. Manual gates (live writes; the main session runs them)

- **G146-1: codified role set.** `config/secure-record-owner-role.json` gains 17 child tables, all with "VERBATIM
  REFUSAL PENDING" evidence: analysis, analysisoutput, communicationthread, communicationattachment,
  communicationparticipant, emailreviewlog, spendsignal, spendsnapshot, fileversion, emailartifact,
  attachmentartifact, eventlog, agreement, billingevent, budget, kpiassessment, reportcard. Follow task 145's
  procedure (notes/task-145 §4) for each table:
  1. Create a probe row owned by `Secure Record Owners`, record the verbatim refusal, and put it into `evidence`.
  2. Then run:

     ```
     pwsh scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com            # dry run
     pwsh scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
     # SECURE-PROJECT-ENVIRONMENT-SETUP.md §5.4: re-strip the SharePoint four that AddPrivilegesRole re-injects
     pwsh scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify   # exit 0
     ```

  3. If a probe is NOT refused, remove that entry with a revert commit. The census job reports any table still
     missing.
- **G146-2 (E3): BU default teams need Read on review logs.** Add `prvReadsprk_EmailReviewLog`
  (`9a9066a5-650c-49f4-a5c6-311ab1882635`) at **Basic** depth to both `Spaarke Basic User` copies:
  `11f93c04-ddf6-f011-8406-7c1e520aa4df` (BU Spaarke) and `dc44312f-f586-4d84-7c32-66f868fd8e2f`
  (BU Spaarke Business Unit 1).

  ```
  POST {org}/api/data/v9.2/roles(<roleid>)/Microsoft.Dynamics.CRM.AddPrivilegesRole
  { "Privileges": [ { "Depth": "Basic", "PrivilegeId": "9a9066a5-650c-49f4-a5c6-311ab1882635",
                      "BusinessUnitId": "<the role's businessunitid>" } ] }
  ```

  Basic depth adds no cross-user reach.
- **G146-3: alert recipients.** Set the App Service setting `Communication__OwnershipHoldAlertUserIds__0=<admin
  systemuserid>`, plus `__1` and so on as needed. Without it the hold still dead-letters and logs Critical.
- **G146-4 (step 8): live check after deploy with task 149.**
  1. Using a non-admin test user created by the owner (round 4 item 1), create a child of a secure project, matter
     and work assignment through each writer family.
  2. Record the owner from a read-back.
  3. Confirm a non-sharee test user cannot open the row in MDA.
  4. Delete the probes and record the deletion.

## 8. Tests

- **Resolver:** `tests/unit/domain/Dataverse/RecordOwnershipResolverTests.cs` (35 tests). Covers:
  - secure-if-any, including both project lookups;
  - flagged-not-isolated;
  - a second parent that is missing or faults;
  - KeepCreator and content rows;
  - reparent in and out, refused-not-applied, read-back mismatch and already-owned.
- **Writer families:** `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipTests.cs` (17 tests) uses
  the real resolver over `OwnershipDirectory`, with the Secure BU's default team present as a decoy. Covers:
  - re-file: document with an ordinary project plus a secure related project → named team; out of secure → BU team;
    flagged-not-isolated refused with nothing written; fault propagates; a root's lookups never reassign it;
  - AI task: secure or ordinary → matter's team; missing matter → nothing created; fault propagates;
  - record thread: secure → named team; flagged → 409 with stable code, nothing created;
  - participant rows: secure → named team; unfiled → creator; missing → none;
  - inbound hold: retry → no alert; last attempt → dead-letter plus an alert per admin; no admins configured → still
    dead-letters.
- **Census:** `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` (7 tests).
- **Seed-and-bite.** Each guard failed when seeded and passed again once restored (files touched):
  - unlisted `new Entity("sprk_memo")`;
  - a routed file losing its owner write;
  - a seam losing its refusal;
  - an unscanned writer losing its resolver call;
  - DataverseUpdateHandler skipping the reparent (4 failures);
  - the participant indexer dropping `ApplyTo` (1 failure);
  - the hold returning Success (2 failures).
- **Existing suites** were updated where the contract changed:
  - task owner is now the team (two seam tests, the RI seam);
  - record thread owner is now the team (contract test renamed);
  - thread JOINs read the thread row first;
  - hosts register `RecordOwnershipResolverDouble`.

  No external-access expectation changed. `ExternalTodoScopeTests` only gained the owner team in its payload
  builder calls.

## 9. Placement (CLAUDE.md §10 / §11)

- **No new service, endpoint, DI registration, package or job.** The resolver was extended in place. The writers
  became callers.
- **One new option key:** `CommunicationOptions.OwnershipHoldAlertUserIds`.
  - **Existing:** there is no administrator-recipient setting. Grep found only `DemoProvisioningOptions.AdminNotificationEmails` (registration email).
  - **Extension:** the existing `CommunicationOptions` section was extended rather than adding a new section.
  - **Cost of doing nothing:** amendment R3's "an administrator is alerted" has no recipient, so a held email would
    only be in logs.
- **New test infrastructure only:** `OwnershipDirectory` (moved, not new logic) and the two test files the POML
  names.
- **New public helpers on existing types:** `ProblemDetailsHelper.RecordOwnerRefused` replaces four copied 409
  blocks. `RecordReparent.ParentChangesOf(UpdateDocumentRequest)` and `RecordOwnershipContext.ContentOf` are thin
  factories on the resolver's own types.
- **Publish size:** not measured here. The main session measures it, per the task brief.

## 10. Ship-together and ordering

- **Task 149 (sharee mirror):** this task MUST NOT reach a shared environment before 149 merges. Once a child is
  owned by the memberless named team, internal sharees of the parent lose sight of it until 149 mirrors them. Put
  this in the PR description.
- **Task 152 (Assigned To):** filed AI and external to-dos and tasks are now team-owned. They drop out of
  `owninguser = caller` surfaces, such as the Daily Briefing, until 152 deploys. **Owner B2 accepted this interim
  drop-out.** Record it in the PR description.
- **Task 145:** G146-1 extends 145's codified set with its procedure. No second list.
- **#1034 (word-add-in-r1 ISS-007):** close it once, citing both projects, after merge. The main session coordinates.
