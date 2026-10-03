# Task 146 — server child writers own their rows through the one resolver (C10 part 2, #1034)

> Branch `task/uac-r2-146` (base `integ/uac-r2-batch2` @ `2a9b4b26c`). Status: **code complete, not deployed**.
> Ships together with task 149. Deploy order with task 152 accepted as B2. Live steps are manual gates (§7, §11d).
> Verifier round 1 fixes (branch `task/uac-r2-146-r1`): §11. Verifier round 2 fixes (branch `task/uac-r2-146-r1-r2`): §12.
> Fix round b2 (branch `task/uac-r2-146-b2`, base = `work/unified-access-control-r2` merged at `c8ede843e`): §13 —
> the round-2 verifier's open items, owner round 7 items 3 (G5 for the AI create tools, §6.5 path B) and 4 (role
> 9 → 26 readiness). **G146-1 is a HARD pre-deploy gate** (§13c).

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

### 3c. Not routed in r0/r1. Escalated, see §5. **Superseded by r2 (§12a items 2 and 6): all three chat tools now route.**

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
- **Run-as-user AI handlers — CLOSED in r2 (§12a item 6): owner S1 option (1) was already answered (round 3) and G5
  (round 3b) defines the shape; implemented.** The r0/r1 record follows for history.
  The handlers are DataverseCreateRecordHandler and EmailDraftToolHandler (trigger 5).
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

## 12. Verifier round 2 (r2, 2026-10-02, branch `task/uac-r2-146-r1-r2`)

The adversarial verifier reported 20 items (4, 5 and 12 informational). Every code item is closed below; what stays open
is a live gate, the publish size, #1034, or an escalation the owner has not answered (§12e).

### 12a. Code fixes

| # | Finding | Fix |
|---|---|---|
| 1 (HIGH) | `CommunicationEnrichmentService.AssignOwningTeamAsync` (FR-E7 category routing) set `ownerid` to a team found by NAME in any business unit, re-owning a secure record's email out of isolation; the census could not see an owner OVERRIDE | Routing now applies only to a communication filed under NOTHING: `IsFiledOrUnreadableAsync` reads the row (every column, through the resolver's own `RecordOwnershipContext.ParentsOf` — no per-table list) and skips routing when the row is filed or cannot be read (fail closed). A row filed AFTER routing is re-owned by the resolver when it is filed (the `IncomingAssociationResolver` reparent). **New owner-write census** in `RecordOwnerAssignmentCensusTests`: every server member that WRITES an owner (`["ownerid"] =`, `ownerid@odata.bind`, or a const holding either) is listed by member with its count and kind — Resolver / Seam / Routed (the file must call the resolver) / Root / PerUser / UnfiledOnly (the member must call the filing gate before its write); 41 members. Behaviour change, recorded: FR-E7 no longer routes FILED communications (it would contradict I-2/I-6 for every filed child, not only secure ones). |
| 2 (HIGH) | `DataverseUpdateRecordHandler` (AI `dataverse.update_record`, run-as-user PATCH of any table, lookups allowed) re-filed children with no owner re-derivation; the run-as-user classification scanned POST only | For a CHILD table (`IsReparentableChild`), a set or cleared lookup is a re-file. As the caller, the row must be visible (`GET`) and AppendTo must be held on every record it is moved under — asked BEFORE any owner decision, so a refusal never describes a record the caller cannot see. Then `ReparentAsync` decides the owner, runs the caller's OWN PATCH (Dataverse authorizes it), assigns the owner separately and reads it back. A refusal writes nothing (tool error with the stable code); a Dataverse refusal of the PATCH assigns no owner. A table outside the ownership set moved under a SECURE record is refused (it cannot be re-owned here). Roots re-own nothing (S6). Census: `EveryRunAsUserWriteIsClassified` now covers POST **and PATCH**; `WorkProductRecordPersister` is classified (it patches one text column). |
| 3 (MEDIUM) | Dropping `body[OwnerBindKey] = …` from an external payload builder passed every test | `SecureChildOwnershipExternalTests`' recorder now records the owner the REAL builder BOUND (`ownerid@odata.bind`), not the argument it was handed; a new test pins each of the three builders' bind; the census seam guard for the builders now requires `RequireOwner(owningTeamId…)` AND `[OwnerBindKey] = $"/teams({owningTeamId})"`; the owner-write census lists each builder's write. |
| 6 | The two run-as-user CREATE tools were escalated although owner S1 option (1) (round 3) and G5 (round 3b) had answered | Implemented **S1 option (1), G5-refined**, in one shared path `Services/Ai/Handlers/Dataverse/OwnedChildWrite.cs`. For a CHILD-table row filed under an ownership parent, AS THE CALLER (through `IDataverseUserClient`: the identity is the credential): `WhoAmI`; the table's Create privilege (and Append when it sets a lookup) by the names the table's metadata declares (activity tables share `prvCreateActivity`); AppendTo on every record a lookup names (`RetrievePrincipalAccess`); no field-secured column (`IsSecured` — an app-only write would pass column security); no owner/audit column (`ownerid`, `createdby`, `overriddencreatedon`, …). Then the owner from the ONE resolver (secure-if-any; a refusal creates nothing), then the APPLICATION creates the row owned by that team by PATCHing a fresh id (`IFieldMappingDataverseService.UpdateRecordFieldsAsync` — the create-by-upsert `InvoiceReviewService` already uses for the G5 invoice). `email.draft` records the drafter as `sprk_sentby` (S1's "for" person; Created By is then the application). Unfiled rows, non-child tables and roots are unchanged (run as the user); a non-child table filed under a SECURE record is refused. **CLAUDE.md §6.5 path A**: §12c. |
| 7 / 8 (LOW) | `PUT /api/v1/documents/{id}` checked Write on the document only: its 409 detail was an oracle about a record the caller may not see, and a caller could pull their document under a secure record they cannot see | Before any owner decision the route asks AS THE CALLER for AppendTo (`entity.associate_document`) on every record the update files the document under — the same check the event re-file and `associate-record` make. Denied → a uniform 403 naming no record. The 409 is now reachable only by a caller authorized on every target. No client PUTs a lookup on this route today (grep of `src/`), so the tightening breaks no caller. |
| 9 (LOW) | Inbound mail was created owned by the (memberless) Secure team, then filed by a separate NON-FATAL write: a failure left a record nobody can see, filed under nothing | An email (inbound or upload capture) OWNED from its filing is now CREATED WITH that filing. `IncomingAssociationResolver.BuildNewRecordFieldsAsync` returns exactly the fields the apply would write (regarding, status, provenance, resolver fields, FR-26 stamps — the builder was extracted from the apply, not copied) and they are merged into the create. A failure to build them HOLDS inbound mail (`record_owner_parent_undetermined`, R3) and SKIPS an upload capture. An unfiled (creator-owned, E1) email keeps the old create-then-apply. |
| 10 (LOW) | The transitive look-through applied only to content rows (`ContentOf`): an analysis of a user-owned document filed to a secure matter took an ordinary team | In the resolver, for EVERY context: a parent that is a CHILD table and NOT team-owned is followed to the records it is filed under, transitively (at most `MaxLineageDepth` = 4 levels, cycle-safe). Those ancestors take part in the SECURE decision and the flagged-not-isolated refusal only; an ordinary row still takes the primary parent's own unit (no change for ordinary data). An unreadable ancestor refuses. A team-owned parent is not followed: its owner already is the resolver's answer. |
| 11 (LOW) | A failed delivery is abandoned and redelivered as the SAME message, so `Attempt` never advances; the processor dead-letters at `DeliveryCount >= 5` while the hold answered "retry" — no administrator alert | `JobContract.DeliveryCount` (set by `ServiceBusJobProcessor`, `[JsonIgnore]`, not on the wire) and `JobContract.IsFinalDelivery` (attempts exhausted OR the delivery count reached) — the ONE definition the processor's dead-letter branch and the hold now share. The hold alerts on the final delivery. Processor behaviour is unchanged (the same condition, now named). |

### 12b. Tests (r2)

New (all over the REAL resolver and `OwnershipDirectory` unless stated):

- `SecureChildOwnershipAiToolTests` (19):
  - create_record: a filed to-do is app-created, named team, never as the user; an ordinary matter → its unit's team;
    flagged → refused, nothing created; no Create privilege → denied; no AppendTo → denied before any owner decision (no
    oracle); an owner column → refused; a field-secured column → refused; unfiled → still as the user; a `task` filed to a
    secure matter → refused, to an ordinary matter → as the user; a resolver fault → an error, nothing created.
  - email.draft: regarding a secure matter → app-created, named team, sender = the drafter, still a draft.
  - update_record: a re-file → named team, read back; flagged → refused, the caller's PATCH never sent; no AppendTo →
    denied; Dataverse refuses the PATCH → no owner assigned; an invisible row → the caller's 404, no oracle; a non-child
    table under a secure matter → refused; a change that files nothing → an ordinary update.
- `SecureChildOwnershipComposeTests` (3): a Compose promote inheriting a secure matter → upserted owned by the named team
  (alternate key kept); inheriting a flagged project → refused before the upsert; unfiled → the saving user's unit team.
  Closes "Compose promoter not driven".
- `SecureChildOwnershipAnalysisForkTests` / `SecureChildOwnershipAnalysisPromoteTests` (2 + 2): fork and promote ask the
  resolver with the document and create owned by its answer; a refusal → 409 + stable code, nothing created, archived or
  bound. Closes "fork and promote not tested".
- `SecureChildOwnershipWriterTests` (+4): the inbound CREATE itself (`CreateCommunicationRecordAsync`, now `internal`,
  driven from a Graph `Message` POCO) carries owner + regarding + stamp; an inbound filing that cannot be built → HOLD;
  upload capture created with its filing; an unbuildable upload-capture filing → skipped.
- `SecureChildOwnershipDocumentRefileTests` (+2): PUT without AppendTo → 403, nothing written or reassigned; onto a
  flagged project without AppendTo → 403 naming neither the record nor its state.
- `SecureChildOwnershipExternalTests` (+1, plus the recorder change above).
- `SecureChildOwnershipTests` (+2): the hold on the broker's last delivery (attempt 1) → dead-letter + alert; one
  delivery earlier → retry, no alert.
- `RecordOwnershipResolverTests` (+6): a child of a user-owned document filed to a secure matter → named team; two levels
  through un-team-owned children → named team; ordinary filing → the primary parent's unit (unchanged); a flagged
  ancestor → refused; a missing ancestor → refused; a team-owned parent is not followed.
- `EmailTriageSeamTests` (+2): filed to a secure matter → never routed; filing unreadable → never routed.
- `RecordOwnerAssignmentCensusTests` (14 in all): the owner-write census, its kind check and its negative control (+3);
  run-as-user POST/PATCH classification and its negative control (replacing the POST-only pair); the builder bind guard.

Updated where the contract changed: the upload-capture seam test reads the filing from the create (no separate update);
the `DataverseCreateRecordHandlerTests` / `EmailDraftToolHandlerTests` lookup tests were retargeted to a NON-ownership
lookup (a reference table / an organization) so they still pin the run-as-user mapping (the filed case is the owned path,
above); the handler constructions gained the resolver (and the app-only seam).

**Still not driven end to end, reason unchanged:** the inbound `.eml`/attachment ROWS (behind the Graph message fetch),
`SpendSnapshotService` (unwrapped `ServiceClient`) and `DocumentCheckoutService` (raw `HttpClient`). Their only offline
doubles are transport doubles that ADR-038 B1 rules out ("transport-level mock — encodes wire format"). Each is pinned by
the per-site census AND the owner-write census.

### 12c. CLAUDE.md §6.5 path A — S1 against the AI tool plane's "user-OBO" MUST

- **Spec rule in question**: spaarke-ai-architecture-redesign-r1, MUST "run user-OBO for all Dataverse tool access"
  (FR-P0-10: "no app-only Dataverse path reachable from AI").
- **Conflict**: a run-as-user create of a child of a secure record leaves it owned by the caller in an ordinary business
  unit, readable by every colleague with ordinary depth (C10, CRITICAL). The owner decided S1 option (1) on exactly these
  tools ("AI email draft, AI create-record"): app-only creation owned by the team, the person in the "for" column. G5
  (round 3b) fixed the shape: as-the-user checks, then the APP creates the row owned by the team.
- **Path**: A (project-scoped exception), decided by the owner (S1, G5). Scope: ONLY a row of a child table filed under
  an ownership parent (the create), and the owner ASSIGNMENT of a child the caller re-files (the update — the PATCH itself
  still runs as the caller). Everything else in the tool plane stays user-OBO.
- **Why it is not an escalation**: the four as-the-caller questions in §12a item 6 (Create/Append privileges, AppendTo on
  every record named, no field-secured column, no owner/audit column) — everything Dataverse would have checked for a
  run-as-user create except the owner itself, which is the point.
- **Alternatives rejected** (S1's own): create-then-assign (option 2) leaves the row readable in an ordinary unit for the
  gap; widening user roles with prvAssign (option 3) is forbidden.

**S1 "for" column per table** (S1: "name it per table in the task note"):

| Table | "for" column | Status |
|---|---|---|
| `sprk_communication` (email.draft) | `sprk_sentby` (systemuser) | Set to the drafter on the owned path (r2). |
| `sprk_todo` | `sprk_assignedto` (CONTACT) | Kept when the model supplies it. The default "the caller's contact" needs task 141's user↔contact link (not merged), so it is not defaulted. |
| `sprk_event`, `sprk_memo`, `sprk_analysis`, `sprk_agreement`, `sprk_budget`, `sprk_invoice`, others | none found in code | Created By is the application. A NEW column needs a §11 justification and a live schema change — an owner / task 152 decision (§12e). Owner B2 already accepted the interim drop-out of team-owned to-dos/tasks from `owninguser = caller` surfaces. |

### 12d. Seed-and-bite (r2)

Every plant was made in a backed-up copy, built, run, and restored from the backup (files touched). Each failed the tests
named below and passed again once restored.

| Plant | Item | What failed |
|---|---|---|
| A1: category routing's filing gate removed | 1 | 2 `EmailTriageSeamTests` (filed / unreadable); census `EveryOwnerWriteKeepsItsKind` (UnfiledOnly) |
| A2: `body[OwnerBindKey] = …` deleted from `BuildTodoCreatePayload` (the verifier's plant) | 3 | 4 external to-do tests + `ExternalCreatePayloads_EachBindTheResolvedOwnerTeam`; census `EverySeamRefusesAnOwnerlessCreate` and `EveryOwnerWriteIsCensused` |
| A3: PUT re-file AppendTo check removed | 7/8 | 2 `SecureChildOwnershipDocumentRefileTests` (403 cases) |
| A4: inbound create without its filing | 9 | `Inbound_OwnedFromItsFiling_IsCreatedWithThatFiling_NeverFiledUnderNothing` |
| A5: upload capture without its filing | 9 | `EmailUploadCaptureSeamTests` save-pane test, and — after fixing a vacuous assertion this plant exposed (`?.Id.Should()` short-circuits when the column is missing; now asserted present first; the same pattern fixed in three `reasonCode` reads) — `UploadCapture_FiledToAnInvoiceUnderASecureMatter…` |
| A6: resolver lineage disabled | 10 | 4 `RecordOwnershipResolverTests` (secure, two levels, flagged, missing) |
| A7: hold keyed on `IsAtMaxAttempts` again | 11 | `InboundHold_OnTheBrokersLastDelivery…` |
| A8: AppendTo check skipped | 2/6 | create + update AppendTo-denied tests |
| A9: update-tool re-file disabled | 2 | update re-file, flagged, invisible-row, non-child tests |
| B1: privilege check skipped | 6 | `CreateRecord_WhenTheCallerLacksCreateOnTheTable…` |
| B2: owner-column check skipped | 6 | `CreateRecord_SettingTheOwnerColumn…` |
| B3: field-security check skipped | 6 | `CreateRecord_SettingAFieldSecuredColumn…` |
| B4: draft sender not recorded | 6 | `EmailDraft_RegardingASecureMatter…` |
| B5: non-child secure refusal skipped | 6 | `CreateRecord_ATableOutsideTheOwnershipSetFiledToASecureMatter…` |
| B6: an extra (unreachable) owner write in `PersistTriageResultAsync` | 1/15 | census `EveryOwnerWriteIsCensused` ("UNLISTED owner write") |
| B7: `DataverseUpdateRecordHandler` removed from the census lists | 2 | census `EveryRunAsUserWriteIsClassified` (PATCH) |
| C1: owned path switched off (`AppliesTo` false) | 6 | 5 owned-path tests (secure / ordinary to-do, draft, privilege, field security); the remaining filed cases are still refused by the fail-closed fallback — defence in depth, by design |

### 12e. Still not closed (and why)

- **AC1 — work assignment S6 b** (verifier item 5, correctly stopped): inline secure provisioning at creation needs task
  144's provisioning as a service, and the endpoint writes `sprk_matterid`, which the table lacks. Unchanged.
- **E1, E2, E3/G146-2, trigger 3** (verifier item 5): unchanged, awaiting the owner / task 149.
- **S1 "for" column** for tables other than communications and to-dos (§12c): owner / task 152; the to-do default needs
  task 141.
- **AC11 (G146-4)**: the live gate after deploy with task 149 — not run (no live writes from this session).
- **AC13**: publish size is measured by the main session (task brief); #1034 closes after merge (main session).
- **AC14 (G146-1, G146-2, G146-5)**: the live role / default-team gates — not run (live writes are the main session's;
  round 4 approved live steps for 141/144/145 only).
- **AC16**: no PR may be opened from this session; the PR description is ready in §12f.

### 12f. PR description (ready to paste)

> **Task 146 — server child writers own their rows through the one resolver (C10 part 2; closes #1034 with
> word-add-in-r1).**
>
> Every BFF create and re-file of a child of a project, matter or work assignment decides `ownerid` through
> `IRecordOwnershipResolver`: the named `Secure Record Owners` team when ANY parent is secure, else the primary parent's
> business-unit default team; refuse (in each writer's own contract) when unresolved; a Dataverse fault propagates.
> Census: `RecordOwnerAssignmentCensusTests` (creates per site, owner writes per member, run-as-user POST/PATCH).
>
> **SHIP-TOGETHER with task 149** (sharee mirror): once a child is owned by the memberless Secure team, internal sharees
> of the parent lose sight of it until 149 mirrors them. **Do not deploy to spaarke-bff-dev or any shared environment
> before 149 merges.**
>
> **Ordering with task 152** (Assigned To / human Created By targeting): filed AI, external and Office to-dos, tasks and
> drafts become team-owned and drop out of `owninguser = caller` surfaces (Daily Briefing) until 152 deploys — **owner B2
> accepted this interim drop-out.**
>
> **§6.5 path A (owner S1 / G5):** the chat tools `dataverse.create_record` and `email.draft` create a FILED child
> app-only, owned by the team, after an as-the-caller check (Create/Append, AppendTo, no field-secured or owner column);
> `dataverse.update_record` re-files through `ReparentAsync`. An exception to the AI tool plane's user-OBO MUST, scoped to
> filed children (task note §12c).
>
> **Placement (bff-extensions.md):** no new service, endpoint, DI registration, package, option or job in r2; one new
> internal static helper (`OwnedChildWrite`, §11 justification in its remarks and the POML) beside the existing write-item
> mapper.
>
> **Deploy gates (main session):** G146-1 (Secure Record Owner role: the census tables), G146-2 + G146-5 (BU default
> teams' Read on `sprk_emailreviewlog`; the Dev 1 / Test 1 default teams' roles), G146-3 (hold-alert recipients), G146-4
> (the live check after deploy with 149). Publish size: measured by the main session against a fresh master build.


## 13. Fix round b2 (2026-10-02, branch `task/uac-r2-146-b2`)

**Base.** `task/uac-r2-146-b1` (the b1 WIP commit `6edf97c58`: compensation + depth refusal, unverified) with
`work/unified-access-control-r2` merged in (origin/master `93634db58` + integrated batch 3: 138, 139, 141, 152, 155
and the Office save fix). The merge commit is `c8ede843e`.

The merge conflicted in 17 files. Every resolution keeps BOTH sides: 146's owner from the resolver, and 152's
Assigned To / acting-user contact. The details:

- **AI tasks.** `TaskActionCore`, `ActionSeam` and `CreateTaskNodeExecutor` take both the resolver and
  `IIdentityNormalizationService`. An unparented task's owner user is `OwnerId`, else 152's `ActingUserId`.
- **Generated to-dos.** `TodoGenerationService` is now 152's `IScheduledJob`. Its lazy dependency step resolves the
  resolver too. The removed `ownerId` / `ownerEntityName` parameters stay removed.
- **Event create.** It resolves the owner AND the acting user's linked contact. The membership owner event now names
  the team the create WROTE (`knownOwner`), not a read-back.
- **Event payload.** 152 extracted `DataverseWebApiService.BuildCreateEventPayload`. The owner refusal moved into it,
  so no payload without an owner is ever built. The census seam and owner-write entries were updated to match.
- **External to-dos.** They take both `owningTeamId` and `callerContactId`.

Several 152 tests needed fixes the merge could not see:

- Two payload tests passed a contact id where 146 now takes the owner. They compiled, and would have bound the
  contact as the owner. Fixed.
- `EventEndpointsMembershipPublishingTests` was rewritten to the merged contract.

### 13a. The round-2 verifier's open items

| # | Finding | Disposition |
|---|---|---|
| GAP S6a | A hard-coded owner in `ThreadResolver.FindOrCreateDefaultThreadAsync` survived every test | **Closed.** `SecureChildOwnershipTests` drives the REAL resolver through `ResolveAndAssignThreadAsync`'s FR-09 ladder. A secure matter's default thread goes to the named team; an ordinary one to its BU team (Theory, 2 cases). A flagged-not-isolated project is refused: nothing is created and the message is left unthreaded. The Tier-3 master keeps its creator (E2). The verifier's exact seed (P1) now fails both the tests and the census. |
| GAP per-member | `EveryOwnerWriteKeepsItsKind` asked only whether the FILE calls the resolver | **Closed.** See below. |
| GAP AC10 | An owner-less create-by-upsert, `Attributes.Add("ownerid", …)` and `{ { "ownerid", … } }` were all invisible | **Closed.** See below. |
| RESIDUAL FAIL-OPEN | `ReparentAsync` applied the change, and a failed assignment left the row filed under the secure parent but owned elsewhere | **Closed in code** (b1's compensation, reworked). See below. |
| LOW depth | The lineage stopped at 4 levels and answered "ordinary" | **Closed.** b1's change was verified: a frontier left at the limit REFUSES (`ParentUnresolved`, reason names the levels). A 6-deep chain ending at a secure matter is refused; a 3-deep one resolves (control). Seed P6 bites. |
| LOW race | FR-E7 routing read the filing, then wrote the owner later | **Closed.** See below. |
| OBSERVATION | A Write + AppendTo holder can re-file a secure root's CHILD out of it, which un-secures it | **Not changed: owner question** (§13f). |
| Pending live / main-session items | G146-1..5, publish size, #1034, the PR | Unchanged by design (§13f). |

**GAP per-member — what now runs:**

- `EveryRoutedOwnerWriteTakesItsValueFromAResolution`. Every owner write in a Routed member must take its VALUE from a
  resolution. Accepted sources:
  - a resolution made in that member: a resolver call, or a call to a same-file member that reaches one;
  - a `RecordOwnerResolution` parameter;
  - for a builder: a parameter every same-file caller of which resolves. **Superseded in b2-r1 (§14a):** this let a
    member that resolves ITSELF write any of its parameters, because its callers reached the resolver through it. The
    rule is now per ARGUMENT: every same-file call must pass a resolution in that parameter's position.

  The check is a crude taint: assignments, `is { } x` bindings and fixpoint. A GUID literal or `Guid.Parse` is always
  rejected.
- `NoServerCodeForgesAnOwnedResolution`. Only the resolver may make an Owned `RecordOwnerResolution`. Without this, a
  forged one would satisfy the value check.
- Negative controls cover: a sibling-resolved seed (the verifier's shape), a hard-coded owner, a resolution parameter,
  a fed builder, and an unfed builder.

**GAP AC10 — what the census now sees:**

- **Owner writes**, in three forms:
  - `x["ownerid"] =`, as before;
  - `.Add` / `.TryAdd(KEY, …)`, including `Attributes.Add`;
  - collection-initializer elements `{ KEY, v }`. Only an INNER brace counts (one preceded by `{` or `,`), so a column
    list such as `new() { "ownerid", "owninguser" }` is not an owner write.
- **Creates**, two new shapes:
  - a create-by-upsert: `UpdateRecordFieldsAsync(<table>, <fresh id>, …)`, where the fresh id is `Guid.NewGuid()` or a
    variable the member assigns from it or from a `Generate…Id(…)`;
  - a two-argument construction with a fresh id.
- **New census entries.** Two real sites are now listed as Routed: `InvoiceReviewService` (sprk_invoice) and
  `SignalEvaluationService` (sprk_spendsignal). `SignalEvaluationService` left `UnscannedWriters`.
- **The per-site check** now requires an upsert's field map to carry the owner: in its initializer, by a write, or
  through the same-file builder it comes from.
- **Seed proofs.** The verifier's three seeds (P2, P3, P4) and a real owner-bind removal (P15) each fail.

**RESIDUAL FAIL-OPEN — how a failed assignment now recovers.** When the assignment (or its read-back) fails after the
change, the owner is read again:

| Owner reads back as | What happens | Logged |
|---|---|---|
| the resolved team | The assignment landed (a lost response). The re-file stands. | — |
| anything else | The filing is put back. Restored columns: the parent lookups the change moved, plus `RecordReparent.AttachColumns`. | `ReparentOwnerAssignmentFailed` |
| unreadable | The SAFE direction is taken. A move INTO a secure record is put back (at worst over-restricted); otherwise the new filing is kept (at worst a stricter old owner). | `ReparentLeftInconsistent` |

A restore that itself fails is also logged `ReparentLeftInconsistent`. The original failure always propagates to the
writer's own contract.

Further changes in this area:

- **The "already owned" check reads the owner AFTER the change.** A concurrent owner write is never mistaken for the
  resolved owner (seed P7).
- **Thread JOIN.** It passes `AttachColumns = sprk_communicationthread`, so a failed assignment takes the message back
  out of the secure record's thread (P12).
- **Invoice confirm.** Its change is "create the invoice + link the document". On a post-change failure it deletes
  the invoice it created. Because `sprk_document_Invoice_n1` cascades `RemoveLink`, that also removes the link, and no
  orphan remains. A lost-race link is left to the winning confirmation. Seed P9 bites.
- **G146-1 is now a HARD pre-deploy gate** (§13c). The expected cause of an assignment failure is a role still missing
  Read on the table.

**LOW race — the routing write.** FR-E7 routing's owner write is now its own CONDITIONAL write, made after the triage
fields:

- It goes through `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync`, with `If-Match` on the
  `versionnumber` read alongside the filing.
- A filing landing in between makes it fail (412). The email is then "not routed" and keeps its resolver owner.
- A missing version is treated as unreadable, so the email is never routed.
- A filing landing AFTER the routed write re-derives the owner itself, because the re-file reads the owner after its
  change.
- `CommunicationEnrichmentService` gained the `IFieldMappingDataverseService` dependency. It is unconditionally
  registered (GraphModule), so there is no asymmetric registration.
- Tests: mapped → conditional write with the read version; 412 → not routed and nothing escapes; no version → not
  routed; filed / unreadable / unmapped → no write. Seed P8 bites.

### 13b. Owner round 7 item 3: G5 for the AI create tools (CLAUDE.md §6.5 path B)

**`dataverse.create_record` now takes the G5 path for EVERY create** (`OwnedChildWrite.PathFor`):

1. Checks AS THE CALLER:
   - Create on the table, plus Append when the row sets a lookup, by the privilege names the table's metadata
     declares;
   - AppendTo on every record a lookup names (`RetrievePrincipalAccess`);
   - no field-secured column;
   - no owner/audit column.
2. The owner from `IRecordOwnershipResolver`: the named Secure team under a secure parent, otherwise the parent's BU
   team, or the caller's BU team for an unfiled row.
3. The APPLICATION creates the row by a fresh-id PATCH that carries the owner.
4. The caller is named in the table's "for" column.

For the two create tools, this supersedes r2's narrower path A (filed child rows only). **It does not supersede §12c's
other scope** (b2-r1, verifier b2 item 7): `dataverse.update_record`'s re-file owner assignment is still app-only under
§12c's path A, which stays in force for it (§14c).

**The creator is kept** (a run-as-user create) only where the resolver's own rules keep it:

- tables whose metadata ownership is not `UserOwned` / `TeamOwned`;
- per-user tables (`appnotification`, notification outbox, workspace layout, nav item, user preferences / profile, AI
  chat message / summary);
- unfiled communications and threads (E1 / E2).

Such a create filed under a SECURE record is refused, as before.

**Two more refusals.** Each says why, and nothing is created:

- A ROOT that the resolver would hand the Secure team. A work assignment or project under a secure record is secured
  by provisioning: task 158.
- Any table outside the ownership set that the resolver would hand the Secure team. The role holds no Read on it.

**`email.draft`: no code change.** A filed draft was already G5 with `sprk_sentby` = the drafter. An UNFILED draft
keeps its creator, which is the resolver's E1 answer: a team-owned draft would show one person's unsent email to the
whole BU. Its XML doc now records path B.

**"For" columns.** Each follows its shipped precedent. A supplied value is never overwritten. The column is server-set,
so no AppendTo is asked of the caller's own contact.

| Table | Column | Value | Precedent |
|---|---|---|---|
| `sprk_todo`, `sprk_event` | `sprk_assignedto` (contact) | the caller's LINKED contact (task 141); none → blank + `assigned_unset` log | task 152, #1044 |
| `sprk_matter`, `sprk_project` | `sprk_assignedtointernal` (contact) | same | owner A7 (Office quick-create) |
| `sprk_communication` | `sprk_sentby` (systemuser) | the caller | S1 (the email draft) |
| `sprk_workassignment` | ~~— not defaulted~~ **b2-r2: `sprk_assignedtointernal` (contact)** | same as matter/project | **Superseded by §15b.** b2 left it out ("no shipped precedent"); the b2-r1 verifier showed the matter/project precedent applies (owner A7, and task 152 already reads this column as a work assignment's responsible person). Its separate `sprk_assignedto` stays model-supplied. |

This supersedes §12c's "for" table for the tools.

**Where the supersession is recorded:**

- `projects/spaarke-ai-architecture-redesign-r1/spec.md`: MUST-rule marker, plus "Amendment A-UAC146" under ADR
  Tensions;
- `projects/spaarke-ai-architecture-redesign-r1/notes/user-obo-audit.md`: header amendment;
- both handlers' XML docs;
- `OwnedChildWrite`'s remarks;
- this note;
- the POML `<execution>` b2 outcome;
- the PR description (§13g).

**Not changed: the model-facing tool description.** It still says "Records you create belong to the calling user
automatically". Changing it needs three things at once:

1. `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json`;
2. the compiled `Metadata.Description` (`CatalogToolDescriptionParityContractTests`);
3. a live re-push (`scripts/Seed-TypedHandlers.ps1`).

Without step 3, `RoutingConsumerTypeHealthCheck` reports the BFF Unhealthy on description drift. **Follow-up for the
main session (with the deploy):** replace that sentence in all three with "Records you create are owned by the team of
the record they are filed under (or your own team) and name you as their Assigned To where the table has one."

**b2-r1 addition: a fourth place carries the same false claim.** The `create-matter` Binding row's `toolDescription`
(`CREATE-MATTER@v1`, `infra/dataverse/sprk_playbookconsumer-rows.json`) ends its `dataverse.create_record`
instructions with "Records you create belong to the calling user automatically". Its live row is pushed by
`scripts/dataverse/Seed-PlaybookConsumers.ps1`. Change it in the same follow-up. `.claude/catalogs/scope-model-index.json`
is a generated snapshot: refresh it with `/jps-scope-refresh` after the re-push.

### 13c. Owner round 7 item 4: the live role extension, 9 → 26 tables (READY, not applied)

**Read-only checks on spaarkedev1, 2026-10-02:**

- The live `Secure Record Owner` role (`e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`, BU `Secure Record`) holds exactly
  **9** privileges, all Read at Basic (`privilegedepthmask` 1): Communication, Document, Event, Invoice, Matter, Memo,
  Project, Todo, WorkAssignment.
- `config/secure-record-owner-role.json` lists **26** tables: those 9 plus 17 marked `VERBATIM REFUSAL PENDING`.
- **All 17 new `privilegeName`s match live metadata exactly**, including casing (a `privilege` query). The script's
  `-cne` name check will therefore not stop the run:
  `prvReadsprk_analysis`, `prvReadsprk_analysisoutput`, `prvReadsprk_CommunicationThread`,
  `prvReadsprk_CommunicationAttachment`, `prvReadsprk_CommunicationParticipant`, `prvReadsprk_EmailReviewLog`,
  `prvReadsprk_SpendSignal`, `prvReadsprk_SpendSnapshot`, `prvReadsprk_FileVersion`, `prvReadsprk_EmailArtifact`,
  `prvReadsprk_AttachmentArtifact`, `prvReadsprk_EventLog`, `prvReadsprk_Agreement`, `prvReadsprk_BillingEvent`,
  `prvReadsprk_Budget`, `prvReadsprk_KPIAssessment`, `prvReadsprk_ReportCard`.

**Other readiness:**

- **Script.** `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` needs no change. Its behavior:
  - it accepts the pending-evidence text (non-empty);
  - `-Apply` adds all missing privileges in one `AddPrivilegesRole` call and reads them back;
  - `-Verify` exits 0 at 26 of 26.
- **Guide.** `SECURE-PROJECT-ENVIRONMENT-SETUP.md` §5.3 gains the "Extending the set" order (negative control →
  evidence write-back → dry run → `-Apply` → §5.4 strip → `-Verify` → positive probes). The §5.4 strip reads `$keep`
  from the file, so it keeps all 26.
- **New guard (in code).** `CodifiedSecureOwnerRoleSet_CoversEveryTableTheServerCanHandTheSecureTeam` fails the build
  when a table the resolver or a Routed/Seam create can hand the Secure team is missing from the file. Seed P14 bites.

**G146-1: HARD pre-deploy gate (main session, live writes).** Run it BEFORE 146's code reaches dev. Dataverse refuses
team ownership without Read, and `ReparentAsync` would then roll back every secure re-file of those tables.

```powershell
# From the repository root. Pin the environment (guide §2).
$DvUrl = 'https://spaarkedev1.crm.dynamics.com'; $Api = "$DvUrl/api/data/v9.2"
$tok = az account get-access-token --resource $DvUrl --query accessToken -o tsv
$H = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'
        'Content-Type' = 'application/json; charset=utf-8'; Prefer = 'return=representation' }
$teamId = '6eabc7f9-13be-f111-a05b-0022482913fc'   # 'Secure Record Owners' (task-145 note §11) — confirm with guide §4.1
$pending = (Get-Content config/secure-record-owner-role.json -Raw | ConvertFrom-Json).tables |
           Where-Object { $_.evidence -like 'VERBATIM REFUSAL PENDING*' }

# 1) NEGATIVE CONTROL — 3 polls ~25 s apart per table; expect "... is missing prvRead<table> privilege ...".
foreach ($t in $pending) {
  $m = Invoke-RestMethod "$Api/EntityDefinitions(LogicalName='$($t.logicalName)')?`$select=EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute" -Headers $H
  $b = @{ 'ownerid@odata.bind' = "/teams($teamId)" }
  if ($m.PrimaryNameAttribute) { $b[$m.PrimaryNameAttribute] = "task146-probe-$($t.logicalName)" }
  try {
    $row = Invoke-RestMethod -Method Post "$Api/$($m.EntitySetName)" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes(($b | ConvertTo-Json)))
    "NOT REFUSED: $($t.logicalName) $($row.($m.PrimaryIdAttribute)) — DELETE it and remove the entry (revert commit)"
  } catch { "$($t.logicalName): $($_.ErrorDetails.Message)" }
}
# A refusal that names a missing REQUIRED column instead of the Read privilege: add that column (a lookup to an existing
# row) and re-probe; ESCALATE if it names anything other than a Read privilege (task 145 trigger 3).
# 2) WRITE BACK: replace each entry's "VERBATIM REFUSAL PENDING …" evidence with date + poll count + refusal; commit FIRST.
# 3) Role:
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl            # dry run: MISSING ×17, would add 17
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl -Apply     # expect the SharePoint-four WARNING
#    guide §5.4 strip with $roleId = 'e4ebabd9-b4a0-f111-aaac-000d3a99d1d7' ($keep read from the file — 26 names)
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl -Verify    # exit 0: "26 of 26"
# 4) POSITIVE: re-run step 1 until each create SUCCEEDS on 3 consecutive polls; read back owningteam (= $teamId);
#    DELETE every probe and record each 404. Control: a table NOT in the file (e.g. sprk_invoicelineitem) must still be
#    refused with privilegeCount=26.
# 5) NFR-05 census live run (clause 5: 26 of 26 at Basic):
$env:SPAARKE_NFR05_DATAVERSE_URL = $DvUrl; $env:SPAARKE_NFR05_REQUIRED = 'true'; $env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "Category=LiveDataverseRoleDepth" --logger "console;verbosity=detailed"
#    Reading step 5 in dev (b2-r1, #1081): the test asserts EVERY clause and headlines a clause-1 exposure over a
#    clause-5 gap. The root team 'Spaarke' now holds Spaarke Basic User (#1081, decided 2026-10-02) — an accepted dev
#    finding under owner round 5. A red verdict naming that root reach is therefore NOT a G146-1 failure. G146-1 passes
#    on the summary line "... covering 26 of 26 codified table(s) at Basic" with no clause-5 table named in the message.
```

Before/after for the record: before = the 9 above; after = 26, all Basic, diff +17, nothing removed.

### 13d. Tests (b2)

**New tests:**

- `RecordOwnershipResolverTests` (+10):
  - the assignment fails after a move IN, so the filing is put back and the failure rethrown;
  - the assignment fails after a move OUT, so the cleared secure lookup is restored;
  - a lost response that landed, so the re-file stands;
  - the owner is unreadable after a move IN, so the filing is put back;
  - the owner is unreadable after a move to an ordinary parent, so the filing is kept;
  - the restore also fails, so the ORIGINAL failure propagates;
  - a thread-JOIN attach column is put back;
  - a concurrent re-owner between the read and the change still ends with the resolved team;
  - the depth limit refuses, with a within-limit control.
- `SecureChildOwnershipTests` (+5): the default record thread, secure and ordinary (Theory, 2 cases); the flagged
  default thread is refused and left unthreaded; the master thread keeps its creator; a JOIN through `ThreadResolver`
  is undone when its assignment fails.
- `InvoiceReviewWritePathTests` (+1): the document owner cannot be moved after the link, so the created invoice is
  deleted and nothing is queued.
- `EmailTriageSeamTests` (+2, 4 reworked): routing is a conditional write with the read version; 412 means not routed;
  no version means not routed.
- `SecureChildOwnershipAiToolTests` (+9, 2 flipped):
  - flipped: an unfiled to-do and a task under an ordinary matter are now app-created and team-owned;
  - new: a filed to-do names the caller in Assigned To without asking AppendTo of their contact; a supplied assignee is
    kept; no linked contact leaves it blank but still creates; an unfiled matter is owned by the caller's BU team with
    Assigned To (internal); a work assignment under a secure matter is refused (158); a per-user table and an
    organization-owned table run as the user (Theory); an unfiled communication runs as the user (E1); an unfiled
    `email.draft` runs as the user (E1).
- `RecordOwnerAssignmentCensusTests` (+7, now 21):
  - the per-member value check, the forging guard and the codified-set guard;
  - negative controls for the value check, upsert / fresh-id creates, `Add` / initializer owner writes, and the upsert
    per-site check.
- `EventEndpointsMembershipPublishingTests` reworked for the merge (§13 base).

**Seed-and-bite (b2).** Each plant was made in the real source and checked with the tests named. Each plant was
restored byte-for-byte and touched; a grep confirmed none remained.

| Plant | What failed |
|---|---|
| P1: hard-coded team in the default-thread owner write (the verifier's seed) | 2 default-thread tests and the per-member value check |
| P2: owner-less `UpdateRecordFieldsAsync("sprk_todo", Guid.NewGuid(), …)` | the create census (UNLISTED) |
| P3: `entity.Attributes.Add("ownerid", …)` | the owner-write census |
| P4: `{ { "ownerid", … } }` | the owner-write census |
| P5: recovery removed | 6 resolver / JOIN recovery tests |
| P6: depth limit answers ordinary | the depth test |
| P7: stale pre-change "already owned" | the concurrent re-owner test. First attempt did NOT bite: the harness handed back the live row. The test now swaps in a new row version, and the plant bites. |
| P8: unconditional routing write | 2 routing tests |
| P9: invoice undo removed | the invoice test |
| P10: G5 only for filed children (the r2 scope) | 3 tool tests |
| P11: no "for" column | 3 tool tests |
| P12: JOIN without `AttachColumns` | the JOIN test |
| P13: a forged `RecordOwnerResolution.Owned(…)` | the forging guard |
| P14: `sprk_emailreviewlog` dropped from the JSON | the codified-set guard |
| P15: the spend signal's owner bind removed | the per-site check and the owner-write census |

**Test harness.** `OwnershipDirectory` gained fault hooks: assignment fault (optionally after applying), owner-read
fault and restore fault. Non-owner updates now WRITE their columns, with `DBNull` clearing.

**Test scope.** These are the items named above, plus the merge's contract updates. No other test was changed.

### 13e. Placement and component justification (CLAUDE.md §10 / §11)

There is no new service, endpoint, DI registration, option, job or package. The changes:

- **Two new constructor dependencies,** each on an unconditionally registered singleton (no asymmetric registration,
  §10 F.1):
  - `IFieldMappingDataverseService` on `CommunicationEnrichmentService`. Existing: the only conditional (`If-Match`)
    write in the server. Extension: reused, not a new method. Cost of doing nothing: the routing race stays open.
  - `IIdentityNormalizationService` on `DataverseCreateRecordHandler`. Existing: task 141's linked-contact read, used
    by 152's writers. Cost of doing nothing: round 7's "record the person" cannot name a contact.
- **New members on existing types:**
  - `RecordReparent.AttachColumns` (b1);
  - in `RecordOwnershipResolver`: `ReadOwningTeamAsync`, `RecoverFromFailedAssignmentAsync`, `RestoreFilingAsync`
    (b1's `CompensateAsync` reworked), and the event ids `ReparentOwnerAssignmentFailed` / `ReparentLeftInconsistent`;
  - `ThreadResolver.ThreadLookupOnCommunication` (a const);
  - in `OwnedChildWrite`: `PathFor`, `WritePath`, `PerUserTables`, `KeepsItsCreatorWhenUnfiled`, `ForPersonColumns`,
    `ForPersonColumn`, `WithLookup`, `Sets`, and `Outcome.SecureFilingRefused`;
  - in `DataverseCreateRecordHandler`: `WithForPersonAsync` and `LinkedContactAsync`.

  None duplicates an existing owner rule. Every owner still comes from the one resolver; the new code decides only
  WHEN to ask it and what to do on failure.
- **Publish size.** Not measured here; the main session measures it.

### 13f. Not closed (and why)

- **Owner question: a CHILD re-filed OUT of a secure root** (verifier OBSERVATION). F3 limits un-securing a ROOT to
  Full Access holders plus the creator. Round 6 says un-securing a related record is "an explicit act by the people F3
  allows", but it addresses secured work assignments and projects (roots).
  - **Question:** must the same F3 limit apply when a document, event or to-do is moved out of a secure root? Today
    Write on the child plus AppendTo on the target is enough.
  - **Recommendation:** yes. Every re-file route would ask F3 rights on the secure root being left: the PUT documents
    route, the event PUT, `associate-record`, `dataverse.update_record`, and inbound filing (which only adds).
  - **Not implemented:** it is a new authorization rule the owner has not stated for children.
- **E1 vs owner round 5** (for the main session to reconcile, as the verifier asked). Round 5 says BFF-created rows go
  to the creating identity's BU team. E1 keeps UNFILED communications with their creator. Recommendation:
  - unfiled inbound / outbound EMAIL → the creating identity's BU team;
  - unfiled CHAT messages and DRAFTS stay with their creator. Direct-thread privacy rests on per-participant shares,
    the master thread keys on the owning user, and a team-owned draft exposes unsent mail.

  This needs the owner's yes, because it changes who can read unfiled mail. Nothing changed here.
- **S6 b** (a work assignment under a secure matter becomes secure) is **task 158** (owner round 6). It is not
  implemented here. The chat tool refuses that create (§13b). `POST /api/v1/work-assignments` still writes
  `sprk_matterid`, which the table lacks: a pre-existing bug, recorded for 158.
- **E2, E3 / G146-2, trigger 3 → task 149:** unchanged.
- **Live gates, all main session:**
  - G146-1: the 9 → 26 role extension, a HARD pre-deploy gate, approved by owner round 7 item 4 (§13c);
  - G146-2 / G146-5: default-team Read on `sprk_emailreviewlog`, and roles for the Dev 1 / Test 1 default teams;
  - G146-3: hold-alert recipients;
  - G146-4: the step-8 live check after deploy with 149. It now also covers the AI create path: a chat-created unfiled
    to-do is owned by the caller's BU team with Assigned To = their contact.
- **Main session:** publish size; closing #1034 after merge; the PR (§13g); the tool-description follow-up (§13b).

### 13g. PR description addendum (append to §12f)

> **b2 (owner round 7):**
>
> - `dataverse.create_record` applies the G5 pattern to EVERY create: an as-the-caller rights check, an app-only
>   create owned by the resolver's team, and the caller in the table's Assigned-To / "for" column. This is a §6.5
>   **path B** amendment of spaarke-ai-architecture-redesign-r1's "user-OBO for all Dataverse tool access" MUST, and it
>   supersedes r2's path A for the two create tools. (b2-r1: `dataverse.update_record`'s re-file owner assignment stays
>   under r2's path A, §12c — see §14e for the consolidated PR text.)
> - The creator is kept only for per-user tables, unfiled communications (E1) and non-user-owned tables.
> - `email.draft` is unchanged: filed drafts are G5, unfiled drafts follow E1.
>
> **G146-1 (Secure Record Owner role, 9 → 26 tables) must run BEFORE this deploys.** A re-file whose owner assignment
> fails is now rolled back (or left in the safe direction) and logged CRITICAL. The FR-E7 routing owner write is
> conditional on the version its filing was read at.

## 14. Fix round b2-r1 (2026-10-02, branch `task/uac-r2-146-b2-r1`)

**Base.** `task/uac-r2-146-b2` @ `aa512cbda`, with `work/unified-access-control-r2` @ `ea6484102` merged in (merge
commit `b5e55cf9d`, no conflicts). The merge brings `8531711d6`, the integration fixtures for batch 3, and `ea6484102`,
the project hard gate "run both integration suites in full before a PR".

### 14a. The b2 verifier's items

| # | Finding | Disposition |
|---|---|---|
| 1 | Re-run of the committed tree | Informational. Nothing to do. |
| 2 | On the branch HEAD, the integration suites failed 3 tests inherited from the base | **Closed.** The work tip is merged (`b5e55cf9d`). Both suites were run in full on the merged tree, with the counts in §14d. |
| 3, 4 | Behaviour and census seed-and-bite: every plant bit | Informational. |
| 5 | The per-member owner-value check passed an owner taken from ANY parameter of a Routed member that calls the resolver itself | **Closed** (§14b). The builder clause is now decided per ARGUMENT, and the member under check never counts as reaching the resolver. Negative control added. The verifier's two seeds bite (S1, S2), and so do two seeds the old rule also passed (S3, S4). |
| 6 | Two blind spots: `row.SetAttributeValue("ownerid", …)`, and an owner-less `new Entity { LogicalName = "sprk_todo" }` create | **Closed** (§14b). Both shapes are seen, plus `e.LogicalName = …` after `new Entity()`, with negative controls. Seeds S5, S6 and S7 bite. The shapes still unseen are listed in the census's MAINTENANCE PROCEDURE item 4 ("KNOWN LIMITS"). |
| 7 | The update tool's app-only owner assignment was recorded nowhere current | **Closed** (§14c). §12c's path A is kept in force for that step and recorded wherever the amendment is. |
| 8 | The model-facing description of `dataverse.create_record` is false for owned creates | **Deferred to the main session, unchanged.** It needs a live re-push. b2-r1 found a fourth place carrying the same sentence, the `create-matter` Binding row, and added it to the follow-up in §13b. |
| 9, 10 | Merge resolution; round 7 items 3 and 4 | Informational: confirmed by the verifier. |
| 11 | AC10 | **Closed** by items 5 and 6. |
| 12 | AC13 | **Partly closed.** The merge is done, and the unit, arch and both integration suites are green (§14d). Publish size and #1034 belong to the main session. |
| 13 | AC11 | **Not closed.** G146-4 is a live gate, run after deploy together with task 149. |
| 14 | AC14 | **Not closed.** G146-1, the live grant of 17 new tables, is a hard pre-deploy gate for the main session. Its evidence entries stay `VERBATIM REFUSAL PENDING` until the live negative control writes them. |
| 15 | AC16 | **Not closed.** This session may not open a PR. The consolidated PR text is in §14e. It cites the pairing with task 149, the ordering with task 152, path B (Amendment A-UAC146) and path A (§12c). |

### 14b. Census changes (`tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`)

**Item 5: the per-member value check.**
- **The b2 defect.** The builder clause asked whether every same-file CALLER of a member reached the resolver.
  A member that calls the resolver itself makes each of its callers "reach" it through that very call. So for any such
  member the clause passed trivially, and every parameter counted as a resolution.
- **The new rule** (`ParametersFedByResolution`). A parameter counts as a resolution only when EVERY same-file call
  passes a resolution in that argument:
  - the argument is matched by name (`teamId: …`) or by position;
  - it must be a resolving call, or name a value the CALLER's own taint holds;
  - it must never be a GUID literal.
  The caller's taint is computed without the member in the reaching set and without a builder clause of its own, so a
  builder fed by another builder fails closed.
- **No self-credit.** `MembersReachingTheResolver(code, exclude: member)` keeps the member under check out of the
  reaching set for its own value.
- **No change on the real tree.** The two real builders pass under the new rule, because each caller hands in its
  resolution: `InvoiceReviewService.BuildInvoiceCreateFields` gets `ownerTeamId.Value`, and
  `SignalEvaluationService.UpsertSignalAsync` gets `owner.OwningTeamId!.Value`.
- **Negative control** `RoutedValueCheck_NegativeControl_ParameterSourcedOwner`. Each case and its result:

  | Case | Result |
  |---|---|
  | A self-resolving member writes `anchor!.RecordId` (the verifier's first seed) | flagged |
  | The same member writes `ParseKey(keyId)` (the verifier's second seed) | flagged |
  | The same member writes its own resolution | passes |
  | A builder whose caller resolves but hands it another team | flagged |
  | A builder fed its resolution by named argument | passes |

**Item 6: shapes the census now sees.**
- **Owner writes.** `x.SetAttributeValue(KEY, v)` is now an owner write. It is matched in `OwnerWritesIn` (the
  owner-write census and the value check) and in `WritesOwnerOn` (the per-site check).
- **The filing-gate check.** `UnfiledOnlyGateComesFirst` now checks the gate against the first owner write of ANY shape.
  Before, it checked only `["ownerid"] =`.
- **Creates.** `ScanSiteIndexes` counts two new shapes:
  - `new Entity { LogicalName = T }` / `new Entity() { LogicalName = T, … }`;
  - `e.LogicalName = T;` where the same member constructs `e = new Entity()`.

  Neither counts when the row is given an EXISTING id (`Id = existing` / `e.Id = existing`). A fresh id still counts.
  A nested `new EntityReference { LogicalName = …, Id = … }` is ignored, because only the initializer's top level is
  read. For `e.LogicalName =`, the per-site check looks for the owner on `e`.
- **Negative controls:**
  - `Detector_NegativeControl_LogicalNameCreates`: 6 creates are found and 3 non-creates are not. As Routed sites, the
    4 owner-less creates are flagged and the 2 owned ones are not (one owned by `ApplyTo`, one by `SetAttributeValue`).
  - `OwnerWriteDetector_NegativeControl_AddAndInitializerForms` adds a `SetAttributeValue("ownerid", …)` write, which is
    found, and an `XElement.SetAttributeValue("paging-cookie", …)`, which is not.
  - `OwnerWriteDetector_NegativeControl` adds a `SetAttributeValue` owner write placed before the filing gate, which is
    flagged.
- **Known limits.** These are now written in the census's MAINTENANCE PROCEDURE item 4:
  - creates:
    - a table name that is neither a literal nor a const;
    - keyed constructions;
    - `UpsertRequest`, `CreateRequest` or `ExecuteMultiple` built elsewhere;
    - a POST to a computed URL;
    - string-embedded JSON;
  - owner writes:
    - `new KeyValuePair<string, object>("ownerid", …)`;
    - `AddRange`;
    - a `[JsonPropertyName("ownerid@odata.bind")]` DTO property;
    - an owner key held in a non-const field;
    - an `AssignRequest`;
  - the value check follows a builder's arguments only within its own file, and its taint is name-based.

  Grep on 2026-10-02: none of these shapes creates a listed child table or writes an owner in `src/server`, except
  `SpendSnapshotService`'s keyed `UpsertRequest`, which is already in `UnscannedWriters`.

### 14c. Item 7: the update tool's app-only owner assignment

**What the code does.** `dataverse.update_record` re-files a CHILD row through `ReparentAsync`:
1. The checks run as the caller: the row must be visible to them, and they must hold AppendTo on each record it moves
   under.
2. The resolver's reads run app-only.
3. The caller's own PATCH runs user-OBO.
4. The owner ASSIGNMENT and its read-back run app-only.
5. If that assignment fails, the filing columns the PATCH moved are restored app-only.

**Why §12c stays in force.** Before b2-r1, those app-only steps were covered only by §12c's path A. §13b, §13g, the
spec amendment and the audit header all said they superseded §12c, while the amendment also said "updates stay
user-OBO". Round 7 item 3 names the two CREATE tools, so b2-r1 does not claim that path B covers the update step. §12c's
path A stays in force for it, and every place now says so:
- `projects/spaarke-ai-architecture-redesign-r1/spec.md`: the MUST-rule marker; the amendment's "Path"; a new "Not
  user-OBO, and not covered by this amendment" bullet; "Still user-OBO" narrowed to reads, deletes and every update's
  PATCH;
- `projects/spaarke-ai-architecture-redesign-r1/notes/user-obo-audit.md`: the header;
- `DataverseUpdateRecordHandler`'s XML doc and `OwnedChildWrite`'s remarks;
- `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` row I-2;
- this note: §13b, §13g and §14e.

**Owner option, offered at PR review.** Folding the update step into path B is the owner's call.

### 14d. Tests

**Counts on the merged tree** (`b5e55cf9d` plus this round's changes, Debug, 2026-10-02), each suite run once in full:

| Suite | Result |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests` | 14,373 passed / 0 failed / 54 skipped (14,427) |
| `tests/Spaarke.ArchTests` | 368 / 368. The census has 23 tests: +2, and 2 negative controls extended. |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | 104 / 104 |
| `tests/integration/Spe.Integration.Tests` | 403 passed / 0 failed / 25 skipped |

The integration figures match the verifier's run on a no-commit merge of the work tip, so the 3 base failures are gone
and this round adds none.

**New and changed tests.** All are in `RecordOwnerAssignmentCensusTests`; no other test changed.
- New: `RoutedValueCheck_NegativeControl_ParameterSourcedOwner` and `Detector_NegativeControl_LogicalNameCreates`.
- Extended: `OwnerWriteDetector_NegativeControl_AddAndInitializerForms` (`SetAttributeValue`, plus a non-owner
  `SetAttributeValue`) and `OwnerWriteDetector_NegativeControl` (a `SetAttributeValue` write before the filing gate).
- ADR-038: no `Mock<HttpMessageHandler>`, no DI-registration test, no constructor null-check test.

**Seed-and-bite (b2-r1).** Every plant was made in the real source and checked with a `--no-build` census run (the
scanner reads source at run time). Each was then restored from a byte copy and touched, and `git status` showed no
`src/` change.

| Plant | Item | What failed |
|---|---|---|
| S1: `ThreadResolver.FindOrCreateDefaultThreadAsync` writes `new EntityReference("team", anchor!.RecordId)` (the verifier's first seed) | 5 | `EveryRoutedOwnerWriteTakesItsValueFromAResolution` |
| S2: the same write as `ParseKey(keyId)` (the verifier's second seed) | 5 | the same |
| S3: `InvoiceReviewService` hands `BuildInvoiceCreateFields` `request.MatterId` instead of `ownerTeamId.Value` (its caller still resolves, so the b2 rule passed it) | 5 | the same, naming `BuildInvoiceCreateFields` |
| S4: `SignalEvaluationService` hands `UpsertSignalAsync` `matterId` instead of the resolved team (same) | 5 | the same, naming `UpsertSignalAsync` |
| S5: `entity.SetAttributeValue("ownerid", new EntityReference("team", Guid.NewGuid()))` in `TodoGenerationService.CreateTodoAsync` | 6 | `EveryOwnerWriteIsCensused` (COUNT CHANGED 1 → 2) and the value check |
| S6: `var seeded = new Entity { LogicalName = "sprk_todo" }; await _dataverse!.CreateAsync(seeded, ct);` in `TodoGenerationService` (the verifier's seed) | 6 | `EveryChildCreateSiteIsCensused` (COUNT CHANGED 1 → 2) and `EveryRoutedCreateSiteWritesItsOwnRowsOwner` |
| S7: `var seeded = new Entity(); seeded.LogicalName = EntityTodo;` + `CreateAsync` | 6 | the same two |

### 14e. PR description (consolidated, ready to paste; replaces §12f + §13g)

> **Task 146: server child writers own their rows through the one resolver** (C10 part 2; closes #1034 with
> word-add-in-r1).
>
> Every BFF create and re-file of a child of a project, matter or work assignment decides `ownerid` through
> `IRecordOwnershipResolver`:
> - the named `Secure Record Owners` team when ANY parent is secure;
> - otherwise, the primary parent's business-unit default team.
>
> When no owner resolves, the writer refuses in its own contract. A Dataverse fault propagates.
>
> A re-file whose owner assignment fails after the change is rolled back, or left in the safe direction when the owner
> cannot be read, and logged CRITICAL.
>
> The census `RecordOwnerAssignmentCensusTests` pins this:
> - creates, per site;
> - owner writes, per member, with each routed write's VALUE traced to a resolution (per argument for builders);
> - run-as-user POST/PATCH.
>
> Its known blind spots are listed in its maintenance procedure.
>
> **SHIP-TOGETHER with task 149** (the sharee mirror). Once a child is owned by the memberless Secure team, internal
> sharees of the parent lose sight of it until 149 mirrors them. **Do not deploy to spaarke-bff-dev or any shared
> environment before 149 merges.**
>
> **Ordering with task 152** (Assigned To / human targeting). Filed AI, external and Office to-dos, tasks and drafts
> become team-owned and drop out of `owninguser = caller` surfaces such as the Daily Briefing until 152 deploys.
> **Owner B2 accepted this interim drop-out.** 152 is already on `work/unified-access-control-r2`, and this branch
> merged it (§13).
>
> **CLAUDE.md §6.5 — the AI tool plane's "user-OBO for all Dataverse tool access" MUST:**
> - **Path B: spaarke-ai-architecture-redesign-r1 spec Amendment A-UAC146, owner round 7 item 3.** It covers
>   `dataverse.create_record` and `email.draft`. Each create follows the G5 pattern:
>   1. an as-the-caller check: Create/Append, AppendTo on every record named, no field-secured or owner column;
>   2. an app-only create owned by the resolver's team;
>   3. the caller recorded in the table's Assigned-To / "for" column.
>
>   The creator is kept for per-user tables, unfiled communications (E1) and non-user-owned tables. A root, or another
>   table the Secure team would own, is refused; a root under a secure parent is task 158.
> - **Path A: task note §12c, owner S1 / G5, kept in force for this one step.** `dataverse.update_record`'s re-file of a
>   child runs its PATCH as the caller. The resolver's reads, the owner assignment and its failure restore run app-only.
>   The owner may fold this step into path B at review.
>
> **Placement (bff-extensions.md):** no new service, endpoint, DI registration, package or job in r1–b2-r1. The task
> as a whole added one option key, `CommunicationOptions.OwnershipHoldAlertUserIds`. The details, with the §11
> justifications, are in task note §9 and §13e and the POML `<execution>` blocks.
>
> **Deploy gates (main session):**
> - **G146-1, a HARD pre-deploy gate:** the Secure Record Owner role goes from 9 to 26 tables, owner round 7 item 4.
>   Run it before this code deploys.
> - G146-2 and G146-5: the business-unit default teams' Read on `sprk_emailreviewlog`, and the Dev 1 / Test 1 default
>   teams' roles.
> - G146-3: the hold-alert recipients.
> - G146-4: the live check after deploy with task 149.
> - The tool-description follow-up (task note §13b): four places, then a re-push.
>
> Publish size is measured by the main session against a fresh master build.

### 14f. Not closed (and why)

- **Owner questions, unchanged from §13f:**
  - must F3's limit govern moving a CHILD out of a secure root;
  - the E1 vs owner round 5 reconciliation.
- **S6 b is task 158**, as before.
- **Live gates, all main session:**
  - G146-1 (AC14), a hard pre-deploy gate;
  - G146-2, G146-3 and G146-5;
  - G146-4 (AC11), after deploy with task 149.
- **Main session:**
  - publish size and #1034 (AC13);
  - the PR (AC16, text in §14e);
  - the tool-description follow-up (item 8, §13b).
- **The #1081 relay (owner, 2026-10-02) changes nothing in 146's code or census.** That census grades OWNER writes,
  not role reach. Its only bearing is on reading G146-1 step 5, which §13c now explains.

## 15. Fix round b2-r2 (2026-10-02, branch `task/uac-r2-146-b2-r2`)

**Base.** `task/uac-r2-146-b2-r1` @ `02e79b879`. The work tip was not merged this round. The b2-r1 verifier merged
`62ea6a8ee` with `--no-commit` and reported it clean, with ArchTests 369/369.

### 15a. The b2-r1 verifier's items

| # | Finding | Disposition |
|---|---|---|
| 1 | Independent re-run: unit 14,373 / 0 / 54, ArchTests 368, both integration suites green; the work-tip merge is clean | Informational. |
| 2 | The verifier's own process disclosure: relative-path seeds briefly wrote three lines into the MAIN worktree. The verifier restored them and nothing was committed. | Informational. Nothing on this branch changed. This round's seed runner uses absolute paths only and writes only inside this worktree. Each plant is restored from a byte copy, checked byte-identical, then touched (§15c). |
| 3 | Prior items (a)–(f) and item 7 re-checked | Informational. Confirmed closed. |
| 4, 9 | Owner round 7 item 3 ("record the person in the table's Assigned-To / 'for' column where one exists") is not applied to `sprk_workassignment`, and this is not raised as an owner question | **Closed in code** (§15b). `sprk_workassignment` → `sprk_assignedtointernal`, the matter/project precedent. Two tests added; seed S6 bites. |
| 5, 10 | Census seeds (1)–(4) pass all 23 census tests | **Closed** (§15c). (1) Prefixed owner keys and (4) creates with a caller-chosen id are now DETECTED. (2) The verifier's exact seed (`Guid.TryParse` in a conditional) is refused; the general shape (a conditional whose other branch is not a resolution) is written into KNOWN LIMITS. (3) is written into KNOWN LIMITS. Seeds S1–S5 bite. |
| 6 | The model-facing `create_record` description is still false for owned creates | **Deferred to the main session, unchanged.** It needs live re-pushes (§13b). |
| 7 | The #1081 relay | Informational. Handled in b2-r1 (§13c, §14f). |
| 8 | No owner decision contradicted beyond item 4 | Informational. |
| 11 | AC11 | **Not closed.** G146-4 is a live gate, run after deploy together with task 149. |
| 12 | AC13 | **Partly closed.** The unit and arch suites are green (§15d), and no package changed. Publish size and #1034 belong to the main session. |
| 13 | AC14 | **Not closed.** G146-1 is a hard pre-deploy gate for the main session. The 17 evidence entries stay `VERBATIM REFUSAL PENDING`. |
| 14 | AC16 | **Not closed.** This session may not open a PR. The text is §14e plus the §15e addendum. |
| 15 | AC1, S6 b | **Task 158** (owner round 6). Not implemented here. The chat tool still refuses a work assignment filed under a secure record (`CreateRecord_AWorkAssignmentFiledToASecureMatter_IsRefused_…`). |

### 15b. Item 4: the work assignment's "for" column

**Change.** `OwnedChildWrite.ForPersonColumns` gains `["sprk_workassignment"] = ("sprk_assignedtointernal", "contact")`.
Nothing else in the handler changed. The existing `WithForPersonAsync` path does the rest:
- a value the request supplies is never overwritten;
- a caller with no linked contact (task 141) leaves the column blank and logs `assigned_unset … caller_has_no_linked_contact`;
- if the table's metadata does not map the column as a contact lookup, the row is created without it and
  `assigned_unset … column_not_mapped` is logged.

**Why this column and not `sprk_assignedto`.** `sprk_workassignment` carries both. Read-only check of live metadata
(spaarkedev1, 2026-10-02): both are contact lookups. The choice follows two shipped precedents:
- **Owner A7.** The other two roots, `sprk_matter` and `sprk_project`, name their maker in `sprk_assignedtointernal`.
  The work assignment carries the same Assigned To (Internal) / (External) pair (`TrackingFieldTrio`: "Project / Matter
  / Work Assignment carry the SAME `sprk_assigned*` lookups").
- **Task 152.** `AssignedToDefaults.ResponsibleContactColumns` already reads a work assignment's
  `sprk_assignedtointernal` (then `sprk_assignedattorney1`) as its responsible internal person. That is the person a
  to-do filed under the work assignment defaults to.

`sprk_assignedto` is in neither precedent, so it stays model-supplied. One table gets one "for" column.

**Effect.** A chat-created work assignment under an ORDINARY parent, or under none, is:
- created by the application;
- owned by the resolver's team (the parent's business-unit team, or the caller's when unfiled);
- for the caller, through their linked contact.

Under a SECURE parent it is still refused (task 158).

**Consequence, the same one A7 accepted for matter/project.** `sprk_assignedtointernal` is an access-conferring
column (`MembershipOptions.CanonicalAccessConferringRegistry` lists it for all three roots). Task 142's Assigned-To
auto-grant (Collaborate) therefore applies to the caller's own contact, exactly as for a matter or project.

**Not raised as an owner question.** Round 7 item 3 is explicit ("where one exists"), and the column follows two shipped
precedents. Reversing it means deleting one dictionary entry. It is listed for the owner's information in §15e.

**Tests** (`SecureChildOwnershipAiToolTests`, real handler + real resolver):
- `CreateRecord_AWorkAssignmentFiledToAnOrdinaryMatter_IsOwnedByThatMattersTeam_AndNamesTheCallerAsAssignedToInternal`
  checks three things: owner = the matter's BU team; `sprk_AssignedToInternal` = the caller's contact; no
  `sprk_AssignedTo` bind.
- `CreateRecord_AWorkAssignmentWithASuppliedAssignedToInternal_KeepsIt`: an unfiled work assignment is owned by the
  caller's BU team, and a supplied value is kept.

The scripted metadata now gives `sprk_workassignment` its live `sprk_assignedto` and `sprk_assignedtointernal` lookups.

### 15c. Item 5: census (`tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`)

**(1) Prefixed owner keys: detected.**
- New `OwnerKeyLiteral` matches `"ownerid"` / `"ownerid@odata.bind"` plain, verbatim (`@"…"`) or interpolated with no
  holes (`$"…"`, `$@"…"`, `@$"…"`).
- It is used in three places, so every check that recognises the key accepts the same spellings:
  - `OwnerWritesIn`: the owner-write census, the value check and the filing gate;
  - `SiteOwnerProblems`: the per-site check;
  - `OwnerKeyConst`: a const holding a prefixed key.
- An interpolation WITH holes is a key built at runtime. It stays in KNOWN LIMITS, now spelled out.

**(2) A conditional owner value.**
- `HardCodedId` now also refuses `Guid.TryParse` / `ParseExact`. The verifier's exact seed is therefore refused even
  though its other branch is the resolution.
- The general shape (`c ? other : team`, `other ?? team`) is written into KNOWN LIMITS: an owner value is accepted when
  it MENTIONS one resolved name.

**(3) Reassigning a tainted name.** Written into KNOWN LIMITS: a later `team = request.MatterId;` does not clear the
taint.

**(4) Creates with a caller-chosen id: detected.**
- New `HandedToACreate`: a row given a non-fresh id counts as a create when the same member hands it to
  `Create(…)`, `Create…Async(…)` or `new CreateRequest { Target = … }`. It can be handed inline or through the
  variable it is assigned to.
- It applies to all three construction shapes:
  - `new Entity(T, id)`;
  - `new Entity { LogicalName = T, Id = id }`;
  - `e.LogicalName = T; e.Id = id;`.
- `FreshEntityCreate` now accepts a member-access id (`request!.RecordId`). Before, such a construction was not
  matched at all.
- An UPSERT of a caller-chosen id is not counted, nor a create in another member or file. Both are written into KNOWN
  LIMITS. Dataverse creates on an upsert of an absent id.
- Also noted while reading, and written into KNOWN LIMITS: a target-typed construction (`Entity row = new("t")`).
  Grep finds none in `src/server`.

**The real tree is unchanged by the new detection.** The census passes with the same counts:
- no server create hands a caller-chosen id to a create;
- no server code spells the owner key with a prefix (Grep `[$@]+"ownerid` in `src/server`: no matches).

**New negative controls** (census 23 → 26):
- `OwnerWriteDetector_NegativeControl_PrefixedKeyLiterals`: `$"ownerid"`, `@"ownerid"`, `$@"ownerid@odata.bind"` and a
  `@"ownerid"` const are found; `$"owneridname"` and `@"sprk_name"` are not.
- `RoutedValueCheck_NegativeControl_PrefixedKeyAndParsedBranch`:
  - a `@"ownerid"` write of the member's own resolution is seen AND accepted (an unseen write would report "writes no
    owner");
  - a `$"ownerid"` write of `Guid.NewGuid()` is flagged;
  - the `Guid.TryParse` conditional is flagged as hard-coded.
- `Detector_NegativeControl_CallerChosenIdCreates`:
  - 7 creates are found: the two-argument form, the initializer, the `LogicalName` assignment, `CreateRequest`,
    `Create(row)`, a member-access id, and an owned one;
  - an update, an upsert (KNOWN LIMITS), and an update beside another row's create are not;
  - as Routed sites, the 6 owner-less creates are flagged and the `ApplyTo`-owned one is not.

**Seed-and-bite (b2-r2).**
- The runner script sits in the session scratchpad and uses absolute paths only.
- Census plants are checked with a `--no-build` run, because the scanner reads source at run time. S6 rebuilds the unit
  project.
- After each plant, the file is restored from a byte copy, asserted byte-identical, and touched. `git status` afterwards
  showed only this round's intended edits.

| Plant | Item | What failed |
|---|---|---|
| S1: `entity[$"ownerid"] = new EntityReference("team", Guid.NewGuid());` in `TodoGenerationService.CreateTodoAsync` (the verifier's seed 1) | 5 | `EveryOwnerWriteIsCensused` (count 1 → 2) and `EveryRoutedOwnerWriteTakesItsValueFromAResolution` |
| S2: the same with `@"ownerid"` | 5 | the same two |
| S3: `ThreadResolver.FindOrCreateDefaultThreadAsync` writes `ownerTeamId != Guid.Empty && Guid.TryParse(keyId, out var k) ? k : ownerTeamId` (the verifier's seed 2) | 5 | `EveryRoutedOwnerWriteTakesItsValueFromAResolution` |
| S4: `var seeded = new Entity(EntityTodo, seededId); await _dataverse!.CreateAsync(seeded, ct);` in `TodoGenerationService` (the verifier's seed 4) | 5 | `EveryChildCreateSiteIsCensused` (count 1 → 2) and `EveryRoutedCreateSiteWritesItsOwnRowsOwner` |
| S5: `await _dataverse!.CreateAsync(new Entity { LogicalName = EntityTodo, Id = entity.Id }, ct);` (seed 4, initializer form) | 5 | the same two |
| S6: the `sprk_workassignment` entry removed from `OwnedChildWrite.ForPersonColumns` | 4 | `CreateRecord_AWorkAssignmentFiledToAnOrdinaryMatter_…_AndNamesTheCallerAsAssignedToInternal` |

The verifier's seed (3), the reassigned tainted local, is a documented limit and was not re-planted.

### 15d. Tests

**Counts on this branch** (`02e79b879` plus this round's changes, Debug, 2026-10-02). Each suite was run once in full
after the seed-and-bite runs had restored every plant.

| Suite | Result |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests` | 14,375 passed / 0 failed / 54 skipped (14,429). That is +2, the two work-assignment tests. |
| `tests/Spaarke.ArchTests` | 371 / 371. That is +3; the census now has 26 tests. |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | 104 / 104 |
| `tests/integration/Spe.Integration.Tests` | 403 passed / 0 failed / 25 skipped |

No failure needed a re-run, so there was no contention to report. One change landed after the unit run: the section
reference in `OwnedChildWrite`'s XML doc comment, §15a → §15b. It is comment-only, and the ArchTests and both
integration runs rebuilt over it.

**Affected tests, run first:**
- the census filter: 26 / 26;
- `SecureChildOwnershipAiToolTests`, `DataverseCreateRecordHandler*` and `EmailDraftToolHandler*`: 93 / 93.

**New tests:**
- `SecureChildOwnershipAiToolTests` +2;
- `RecordOwnerAssignmentCensusTests` +3: `OwnerWriteDetector_NegativeControl_PrefixedKeyLiterals`,
  `RoutedValueCheck_NegativeControl_PrefixedKeyAndParsedBranch` and `Detector_NegativeControl_CallerChosenIdCreates`.

No existing test changed. The only fixture edit gives the scripted `sprk_workassignment` metadata its two live contact
lookups.

**ADR-038.** No `Mock<HttpMessageHandler>`, no DI-registration test, no constructor null-check test.

### 15e. PR description addendum (append to §14e)

> **b2-r2:**
> - A chat-created work assignment names the caller in `sprk_assignedtointernal`. This is the matter/project
>   precedent (owner A7), and task 152 already reads that column as a work assignment's responsible person. It is one
>   dictionary entry, and the owner may reverse it.
> - The census also sees owner keys spelled `$"ownerid"` / `@"ownerid"`, and creates of a row given a caller-chosen id.
>   Its remaining blind spots are listed in its maintenance procedure.

### 15f. Placement and component justification (CLAUDE.md §10 / §11)

- No new service, endpoint, DI registration, option, job, column or package.
- One new entry in an existing static dictionary (`OwnedChildWrite.ForPersonColumns`). It writes an EXISTING column
  through the existing `WithForPersonAsync` path.
- The census changes are test-only.
- Publish size is not measured here; the main session measures it.

### 15g. Not closed (and why)

- **Owner questions, unchanged from §13f / §14f:**
  - must F3's limit govern moving a CHILD out of a secure root;
  - the E1 vs owner round 5 reconciliation.
- **For the owner's information (no question):** the work assignment's "for" column choice (§15b), which is reversible.
- **S6 b is task 158** (owner round 6).
- **Live gates, all main session:**
  - G146-1 (AC14), a hard pre-deploy gate;
  - G146-2, G146-3 and G146-5;
  - G146-4 (AC11), after deploy with task 149. G146-4 can also confirm one chat-created work assignment: owned by the
    BU team, Assigned To (Internal) = the maker's contact.
- **Main session:**
  - publish size and #1034 (AC13);
  - the PR (AC16, text in §14e + §15e);
  - the tool-description follow-up (item 6 here, item 8 in b2-r1; §13b).
- **Census limits that remain** are listed in its MAINTENANCE PROCEDURE item 4. Behaviour tests pin every existing
  writer's owner, so these limits bite only on new code.
