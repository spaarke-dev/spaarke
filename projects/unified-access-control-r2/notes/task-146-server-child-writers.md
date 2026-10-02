# Task 146 — server child writers own their rows through the one resolver (C10 part 2, #1034)

> Branch `task/uac-r2-146` (base `integ/uac-r2-batch2` @ `2a9b4b26c`). Status: **code complete, not deployed**.
> Ships together with task 149. Deploy order with task 152 accepted as B2. Live steps are manual gates (§7, §11d).
> Verifier round 1 fixes (branch `task/uac-r2-146-r1`): §11.

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

`RecordOwnerAssignmentCensusTests` fails the build on any unlisted LITERAL create of a child table (`new Entity("t")`, a
POST to a literal entity set), on any routed create SITE whose row gets no owner write in the member that builds it (r1),
and on any unclassified run-as-user POST (r1); creates through computed URLs, upserts and seams are a hand-kept list that
must call the resolver. Each guard was proven to bite by seeding (§8, §11b).

## 2. Resolver changes (`Services/Dataverse/RecordOwnershipResolver.cs`, extended in place, no second resolver)

- **Context.** `RecordOwnershipContext` gains three members:
  - `Parents`, with `ForChild` (every ownership-parent lookup on the row), `ForParents` and `ContentOf`.
  - `WhenUnfiled = KeepCreator` (E1).
  - `KeepCreatorUnlessTargetIsTeamOwned`, for content rows of a creator-owned unfiled parent (r1: a creator-owned
    parent that IS filed hands its content rows the owner of what it is filed under — §11a item 3).
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

r1: both are now NAMED in the ArchTest (`RecordOwnerAssignmentCensusTests.EscalatedWriters`), and
`EveryRunAsUserPostIsClassified` fails on any run-as-user (`IDataverseUserClient`) POST that is not listed as an
escalated writer, a routed/unscanned writer, or a non-create (`DataverseSearchDataHandler`: a search READ). An entry goes
stale (fails) when its create disappears or its file starts routing its owner.

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

## 11. Verifier round 1 (r1, 2026-10-02, branch `task/uac-r2-146-r1`)

An adversarial verifier reported 23 items. Each is closed below, or stated as not closed with the reason.

### 11a. Code fixes

| # | Finding | Fix |
|---|---|---|
| 1 | `associate-record` re-filed a document (and changed its owner) with no per-record check | The route now carries the body-declared per-record filter (`AddFinanceAuthorizationFilter(ResolveAssociateTargets)` — the one filter that authorizes several BODY ids; reused, not a new one): Write on the document (the same document path `PUT /api/v1/documents/{id}` uses) and AppendTo (`entity.associate_document`) on the target's entity set, resolved through `EntityAccessFilter`'s one type→set table. Unreadable id / unsupported type = 400 before any rights query. `RecordMatchEndpoints.cs` is now IN `RouteAuthorizationGuardTests` (RouteLevelGate); `match-records` is a **Pending "UNOWNED" waiver** — a pre-existing tenant-wide AI-index read, surfaced by bringing the file into the census, not task 146's scope. The report's earlier claim that "only the document is authorized" on this route was wrong: nothing was. |
| 2 | Event re-file re-owned from a lookup the PATCH never wrote | ONE derivation, `Spaarke.Dataverse.UpdateEventRequest.RegardingLookupWrites()`: the new regarding (null id = a clear) plus a clear of the PREVIOUS type's lookup when the type changes. `DataverseWebApiService.BuildEventUpdatePayload` writes exactly those lookups (live-verified nav props `sprk_RegardingProject` … and entity sets incl. `sprk_analysises`, spaarkedev1 2026-10-02), and `EventEndpoints.ParentChangesFor(update)` re-derives the owner from exactly those. The previous type comes from the denormalized type or the ONE populated typed lookup (`CurrentRegardingRecordType`). A re-file is now authorized AS THE CALLER (Write on the event, AppendTo on the target; deny/fault = 403). |
| 3 | `ContentOf` kept the creator for any parent that was not team-owned, even a FILED one | The resolver now reads the non-team-owned parent's own ownership-parent lookups (`ReadOwnershipParentsOfAsync`, every column — the read only happens on this branch): filed → resolves from what the parent is filed under (secure-if-any; a flagged-not-isolated grandparent refuses); unfiled → Unchanged (E1). Docs corrected to match. |
| 4 | Inbound / upload-capture owners ignored the FR-26 stamps; an evaluation fault produced an unfiled creator-owned email | `IncomingAssociationResolver.OwnershipContextForNewRecordAsync(decision)` derives the stamps with the SAME derivation the apply writes (`DeriveCoreAncestorStampsAsync`, factored out of `ApplyCoreAncestorStampsAsync`) and adds them as parents. Inbound: an evaluation or stamp-derivation failure is now a HOLD (`RecordOwnerUnresolvedException`, new code `record_owner_parent_undetermined` → retry → dead-letter + admin alert, R3); the filing step is `IncomingCommunicationProcessor.ResolveInboundFilingAsync` (internal, testable without Graph). Upload capture: the same failure SKIPS the capture (its refusal contract; the save proceeds as an archive). Note: the engine auto-writes only the tenant's `AutoFileOptions.CoreWritableEntities` (default matter/project/service request, whose stamps are themselves), so the stamp case arises for tenants that make a child type auto-writable. |
| 6 | Census proved ownership per FILE; the two run-as-user writers were absent | `EveryRoutedCreateSiteWritesItsOwnRowsOwner`: each routed create SITE's row must get an owner write in the member that builds it (by variable; inline constructions in their initializer or handed to an owner-writing call; builder members are checked at every caller; POST sites at member level; `row[CONST]` where the const is `"ownerid"`; a same-file helper that owns its first parameter counts). `EscalatedWriters` + `EveryRunAsUserPostIsClassified` (see §3c). Negative controls for both. `DATAVERSE-WRITE-PATH` row I-2 now states the guard's real scope (literal shapes; computed URLs are hand-listed). |
| 8 | A cleared lookup (null) was not a re-file | `RecordReparent.ParentChangesWithClearsIn` passes every null as a candidate clear (`DataverseUpdateHandler`); `UpdateRecordActionCore` passes every null payload value (`X@odata.bind: null` → column `x`). `ReparentAsync` treats a null for a column that holds no parent as no change and, when nothing a row is filed under changes, writes the change and decides no owner (Unchanged). |
| 9 | `PUT /events` answered 409 after the update had landed | The status log row's owner is resolved BEFORE any write (a refusal = 409, nothing written); a re-file's log row takes the event's resolved owner, so no second resolution follows the write. |
| 10 | Outbound send: owner decided after the email had gone | `BuildDataverseRecordAsync` / `BuildDataverseRecordForUserAsync` / `BuildMessageDataverseRecordAsync` build the record and resolve its owner BEFORE the send (`ResolveOutboundOwnerAsync`): refusal = 409 `SdapProblemException` with the refusal code, nothing sent or created; a fault propagates (5xx), nothing sent. After the send, `CreatePreparedRecordAsync` creates it (still best-effort — the email has gone). For chat, a refused message is no longer sent, so no echo can persist it unfiled. |

### 11b. Tests (writer families, real resolver) — verifier items 5, 7, 15, 16

New files under `tests/integration/data-mutation/RecordOwnership/`:

- `SecureChildOwnershipEndpointTests` (15): event re-file (lookup written = lookup re-owned from; type change clears the old lookup; 403 without Write on the event; 403 without AppendTo on the target; 409 flagged target, nothing written), event status 409 before any write, event create (named team + log row; 409), the ParentChanges ≡ PATCH pin, associate-record (403 no rights — the verifier's repro; 403 no AppendTo; authorized → named team, read back; 400 before any rights query), operation key pin.
- `SecureChildOwnershipExternalTests` (8): external to-do on a secure project / matter / work assignment → named team; ordinary project → its BU team; external event → named team (the verifier's `sprk_matter` plant now fails it); 409 flagged; document upload → named team; 409 flagged with NO bytes uploaded.
- `SecureChildOwnershipWriterTests` (23): shared-mailbox send → named team; 409 before the send, nothing sent; fault → propagates, nothing sent; chat send 409, nothing sent; inbound filing → named team THROUGH the stamp; inbound undeterminable → HOLD; inbound flagged → hold with the refusal; upload capture through the stamp; upload capture undeterminable → skipped; spend signals (secure / ordinary / missing → skipped); generated to-dos (regarding secure matter; source event; flagged → refused); analysis create → document's named team; 409 missing document; enrichment review log (secure communication; user-owned communication FILED to a secure matter — item 3 end to end); apply audit row (named team; 409 BEFORE the target write); inbound attachment documents (`EmailAttachmentProcessor`: named team; flagged → none created).
- `SecureChildOwnershipDocumentRefileTests` (2): `PUT /api/v1/documents/{id}` → named team (read back); 409 flagged, not written.
- `SecureChildOwnershipTests` (+2): a generic update that clears the only secure lookup → reassigned out; clearing only non-lookup columns → no reassignment.
- `RecordOwnershipResolverTests` (+5): content rows of a user-owned FILED communication (secure → named team; ordinary → the matter's team; flagged → refused); reparent by clearing with null (read back); null on non-parent columns → Unchanged, no assignment.
- `ExternalTodoScopeTests` fixture comment (item 7) now points at `SecureChildOwnershipExternalTests`.

**Seed-and-bite (r1), every plant failed its tests and passed again once restored (files touched):**
P1 inbound attachment `ApplyOwner` removed → per-site census (`IncomingCommunicationProcessor.cs:1008`);
P2 shared-mailbox send `owner.ApplyTo` removed → per-site census (`CommunicationService.cs:1983`) + send test;
P3 external event owner from `sprk_matter` → 2 external event tests; P4 signal evaluation targeting `sprk_project` →
2 signal tests; P5 event create dropping the regarding parent → 2 event create tests; P6 event PATCH writing no lookup →
2 re-file tests + the pin; P7 associate-record filter removed → Rule A (`associate-record` UNGATED) + 2 associate tests;
P8 content rows ignoring the parent's filing → 3 resolver tests + enrichment test + event status test; P9 update handler
ignoring nulls → the clear test; P10 inbound fault swallowed → hold test; P12 stamps not derived for the owner →
2 inbound + 2 upload tests; P13 status log owner not pre-resolved → event status test; P14 re-file authorization
removed → 2 re-file 403 tests; P15 outbound refusal not thrown → 2 send tests.

**Not driven end to end, with the reason (item 5 remainder):** the inbound `.eml`/attachment rows and the inbound create
itself (they sit behind the Graph message fetch — the Graph SDK request builders cannot be doubled; `InboundPipelineTests`
skips the same path), `SpendSnapshotService` (writes through an unwrapped `ServiceClient`, like `FinanceRollupService`)
and `DocumentCheckoutService` (raw `HttpClient`) — their only offline doubles are the transport mocks ADR-038 B1 rules
out — and the Compose promoter (no real-resolver harness without the full Compose save fixture). Each of these sites is
pinned by the PER-SITE census (proven by P1/P2), and their owner decision is one resolver call already covered by the
resolver tests. Analysis fork/promote resolve the owner exactly as create does (the document); create is tested.

### 11c. Proved by design, not changed

- **AC6 for best-effort writers (item 17).** The task's own constraint binds best-effort writers to stay best-effort ("a
  refusal is a skipped row, never a failed profile or a failed job"); the enrichment, participant index and post-send
  record step therefore log a fault and write nothing — nothing is created, which is the AC's substance. The one place a
  fault could have produced a success response for a filed record (the outbound send) now resolves the owner BEFORE the
  send and propagates a fault (item 10).
- **Documents PUT target authorization.** `PUT /api/v1/documents/{id}` authorizes Write on the document but not AppendTo on
  the record it files to (pre-existing; the verifier named only associate-record and the event PUT). Not changed here
  (a live route with clients; tightening it could break the wizard's association step for callers without AppendTo) —
  recorded for the owner.

### 11d. Gates (r1) — G146-2 extended (item 11)

Read-only check, spaarkedev1 2026-10-02 (`RetrieveRolePrivilegesRole` over each default team's roles, Read privilege of
every census "fix" table):

| BU default team | Roles | Read on the child tables |
|---|---|---|
| Spaarke (root) | Spaarke Basic User `11f93c04…` | all **except `sprk_emailreviewlog`** (G146-2) |
| Spaarke Business Unit 1 | Basic User `dc44312f…`, Reporting Viewer, AI Analysis, Office Add In | all **except `sprk_emailreviewlog`** (G146-2) |
| Spaarke Dev 1 `7cdb15ee-e39e-f011-bbd3-7c1e5215b8b5` | **none** | none — every child owned here is refused by Dataverse |
| Spaarke Test 1 `1c75377a-e29e-f011-bbd3-7c1e5217cd7c` | **none** | none |
| Spaarke Demo | System Administrator | all |
| Secure Record (default) | none | none — by design (task 144: never the owner) |

Today Dev 1, Test 1 and Demo own **no** projects, matters, work assignments or documents and hold **no** active users, so
no child is currently filed there; the moment a user or record is placed in Dev 1 / Test 1, every child write for it is
refused (fail closed, not a leak). **G146-5** (before placing any user or record in those business units):

```
POST {org}/api/data/v9.2/teams(7cdb15ee-e39e-f011-bbd3-7c1e5215b8b5)/teamroles_association/$ref
{ "@odata.id": "{org}/api/data/v9.2/roles(fe4b2a83-f18c-44e7-a30b-ea7391bdcbec)" }      # Spaarke Basic User, Dev 1 copy
POST {org}/api/data/v9.2/teams(1c75377a-e29e-f011-bbd3-7c1e5217cd7c)/teamroles_association/$ref
{ "@odata.id": "{org}/api/data/v9.2/roles(b5aff661-8494-4756-a8a9-ae13368fce25)" }      # Spaarke Basic User, Test 1 copy
```

…then G146-2's `prvReadsprk_EmailReviewLog` (Basic) on those copies too. Rule for every future BU: its default team must
hold a role with Read on every census "fix" table before it owns children (re-run the read-only check above).

### 11e. Still not closed (and why)

- **AC1 / items 12, 14:** the run-as-user AI handlers and work-assignment S6 b remain escalated (security-sensitive / needs
  task 144's endpoint as a service). They are now NAMED in the census. E1, E2, trigger 3, E3/G146-2 unchanged.
- **AC13 / item 21:** publish size is measured by the main session; #1034 closes after merge.
- **AC14 / items 11, 22:** G146-1 (role set) and G146-2/G146-5 (default-team Read) are manual live gates.
- **AC11 / item 23:** G146-4 (live gate after deploy with task 149).
- Pre-existing, observed while fixing item 2: the event create and update write `sprk_regardingrecordtype` as an int, but
  live metadata types it as a lookup (`sprk_recordtype_ref`) — the events API write path may fail live. Not task 146's
  scope; recorded.
