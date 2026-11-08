# Task 032b-1: shared mapping write gates, the invoice display copies, and the inventory of creators with no calling user

Branch `task/fm-032b-1` (from `work/field-mapping-server-write-path-r1` plus the merged `task/fm-032b-design`).
Binding input: `notes/task-032b-design.md` round 2 (owner: O1 inline, O2 gate move, U7), the 2026-11-08 entries in
`notes/decisions.md`, and the task 032 round-2 note (T-M1, G5).

## 1. The gate move (O2), pure move

Repair's own gates now live in `Services/RecordCreation/MappingWriteGates.cs`. Repair keeps thin wrappers with the
same log lines, so its behaviour does not change.

| Moved | From (Repair) | To |
|---|---|---|
| Copy gate | `SourceMayBeCopiedAsync`, `SourceGateColumns` | `MappingSourceCopyGate.DecideAsync`, `.SourceGateColumns` |
| Fit check | `TargetColumnValue`, `IsNumberType`, `AsNumber`, `InRange` | `MappedTargetFit.Check` |
| 052a metadata read | `ColumnMetadataForRepairAsync`, `SourceColumnsRepairMayCopyAsync` | `MappingColumnMetadata.ReadAsync`, `.CopyableSourceColumns` |

- The Repair wrapper reads the source row's owner inside its own try. An owner value of the wrong type still ends in
  "could not be told", as before the move.
- The gate turns a non-cancellation fault into `Unknown(gate_fault)` and rethrows cancellation. Before the move, Repair's
  catch did the same.
- **Deferred to 032b-2** (coordinator Q1): the owner gate, the No Access gate and `Gated(display_copy_confers)`. Repair
  has none of them, so moving them is not a pure move.

**Move proof.**
- The Repair suites pass unchanged (they are in the verify-suites filter).
- T-M1 (`tests/unit/domain/RecordCreation/MappingWriteGatesTests.cs`) pins each moved gate directly: every copy-gate
  verdict and reason, the missing and ambiguous team, fault, cancellation, every fit-check code, and the metadata read
  (read, empty, fault, cancellation, copyable split).

**G5** (`tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs`).
- `InvoiceReadIsNeverWiderThanCoreRead` checks every role file. A role's invoice Read depth must not be greater than its
  shallowest Read on matter, project and work assignment. A missing core privilege counts as none, and an unknown depth
  is reported.
- `InvoiceReadCheckFires` is the must-fire control. It seeds five roles (wider, no core, unknown depth, equal, no
  invoice). Exactly the first three are reported.

## 2. The invoice display copies (U3 (c), coordinator Q2: option P plus a display-copy class)

**The registry.** `MembershipOptionsDefaults.CanonicalAccessConferringRegistry` no longer has a `sprk_invoice` entry.
The invoice's attorney and paralegal columns are display copies of the matter's people. They do not confer access.

**One source.** `MembershipOptionsDefaults.DisplayCopyColumns` is an internal static set kept next to the registry. It
holds `sprk_invoice`: `sprk_assignedtoattorney1`, `sprk_assignedtoattorney2`, `sprk_assignedtoparalegal1` and
`sprk_assignedtoparalegal2`.

**The push side effect (for the PR text).**
- `PushRuleSelection.Select` skips a rule whose target is a display copy, whatever its sync mode. It does so in an
  ordinary push and with `includeAssignedTo`, and counts the rule as skipped, so the preview shows it.
- Before this change, these four columns were Assigned-To. A push with `includeAssignedTo` wrote them.
- After this change, no push writes them. Create-time mapping is unchanged by this task; filling them at create is
  032b-2's inline path.
- Other columns of the same invoice profile push as before.

**Tests.**
- Must-fire: `PushRuleSelectionTests.ADisplayCopyRule_IsNeverPushed_WhateverItsSyncMode_AndIsCounted` covers four
  columns, with and without `includeAssignedTo`. The contract test `Push_ADisplayCopyRule_IsNeverWritten_MatterToInvoice`
  checks that only the plain column is written and that `skippedRuleCount` is 1.
- Must-not-fire: `APlainColumnOfTheSameInvoiceProfile_StillPushes`, and
  `TheSameColumnNameOnAnotherTable_IsNotADisplayCopy`.

**CI guard** (`tests/unit/domain/Membership/InvoiceDisplayCopyRegistryGuardTests.cs`). It checks that the canonical
registry has no invoice entry, and that no display copy is an Assigned-To column of the canonical or the bound registry
(display copies ∩ registry = ∅).
- Must-fire: a registry seeded with the old invoice entry yields four overlaps.
- Must-not-fire: the root tables' assigned columns are still Assigned-To, and none of them is a display copy.

**Registry consumers (U7).**
- The people-targeting filter (`MembershipResolverService.FilterToPeopleTargetingTermsAsync`, which Daily Briefing reads
  through `PeopleTargetedSet`) no longer matches invoices by these columns. It matches them by owner and creator only.
- Pinned by `tests/unit/domain/Membership/InvoicePeopleTargetingAfterDisplayCopyTests.cs`. The invoice condition names
  `ownerid` and `createdby` and none of the six former columns. The control is that the matter's
  `sprk_assignedattorney1` still binds.
- The other consumers (push classifier, sync-mode script, seeder class test, Repair `ColumnsRepairNeverMaps`) are covered
  below and in section 4. `AssignedAccessMaterializer` reads root tables only, so it is unaffected. No configuration
  file binds `AccessConferringRoles`, so the bound registry is the canonical one everywhere.

**Script and seeder.**
- `scripts/Set-FieldMappingRuleSyncMode.ps1` drops the invoice line from its assigned-to block and adds a `display-copy`
  class-data block, kept at One-time (0). `SyncModeMigrationClassDataTests.TheDisplayCopyTable_IsTheDisplayCopySet` holds
  that block to the set, and `-SelfTest` passes.
- The seeder's Matter→Invoice profile stays One-time. `FieldMappingSeederSyncModeClassTests` treats a display copy as
  One-time. Manual Refresh on one is caught.

## 3. §11 answers and placement

**Placement.** BFF, in-process, in `Services/RecordCreation` next to Repair, its only caller today, with 032b-2 to follow.
The change adds no endpoint, DI registration, package or background work.

| New member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `MappingSourceCopyGate`, `MappedTargetFit`, `MappingColumnMetadata` and their result records | Repair's private methods, which are the same code | Extending in place keeps the gates private to one partial class. The inline invoice path (032b-2) cannot call them. | 032b-2 would copy the gates, and the two copies would drift on secure and unknown sources (O2). |
| `MembershipOptionsDefaults.DisplayCopyColumns` | The access-conferring registry | It cannot hold these columns: anything in the registry confers access, which U3 (c) removes. | The push, script and seeder test would each hard-code the four columns, and the class would drift. |
| `PushRuleSelection.IsDisplayCopy` | `AssignedToColumnsOf`, the one classifier | That classifier reads the registry, and display copies are no longer in it. | A Manual Refresh rule onto a display copy would be pushed, overwriting the copy outside the create path. |
| The G5 helper `InvoiceReaderProblems` | `FeatureRolePrivilegeGuardTests`' role-file reader, which it reuses | It is an added check in the same guard class. | A role granting invoice Read wider than core Read would ship unnoticed. |
| The registry entry removal | None | Pure modification | Display copies would keep conferring access (U3 (c)). |

## 4. Known limits (K-class, owner to note)

- **Repair.** `ColumnsRepairNeverMaps` no longer lists the four invoice columns, because they left the registry. The path
  is unreachable today: invoice-family rows are refused (`InvoiceFamilyGuardUnavailable`, #1911). Once #1911 lands,
  Repair would fill an empty display copy, fill-only. That is the §8.5 named exception against §7 "no re-map". Decide it
  in 032b-2.
- **The invoice's `sprk_assignedto1` and `sprk_assignedto2`** are now plain data and are not display copies. A Manual
  Refresh rule onto them would push. No seeded profile targets them.

## 5. Inventory: creators with no calling user

"Mappable" means the table is a mappable target in `RecordTableRules`. No creator below runs a field-mapping profile;
only `RecordCreationService` does. Line numbers are on this branch.

| Creator (file:line) | Trigger | Table(s) | Mappable | Maps today | Would need the shared gates |
|---|---|---|---|---|---|
| `Services/Workspace/TodoGenerationService.cs:1081` | `IScheduledJob` | `sprk_todo` | yes | no | yes, if it maps |
| `Services/Signals/Actions/InquiryReplyTodoCreator.cs:160` | signal action (job) | `sprk_todo` | yes | no | yes, if it maps |
| `Services/Ai/Nodes/ActionCore/TaskActionCore.cs:349` (via `CreateTaskNodeExecutor`, `CommunicationCreateTaskApplyService.cs:346,501`) | playbook node; communication apply | `sprk_event` | yes | no | yes, if it maps |
| `Services/Insights/Observations/DataverseObservationMirror.cs:202` | insights job | `sprk_analysis` | yes | no | yes, if it maps |
| `Services/Communication/IncomingCommunicationProcessor.cs:1212` (and its communication create) | Graph webhook → job handler | `sprk_document`, `sprk_communication` | yes | no | yes, if it maps |
| `Services/Communication/Channels/MessagingIngestor.cs:265` | ACS Event Grid webhook → job handler | `sprk_communication` | yes | no | yes, if it maps |
| `Services/Communication/MessageAttachmentMaterializer.cs:285,298` | job handler | `sprk_document`, `sprk_communication` | yes | no | yes, if it maps |
| `Workers/Office/UploadFinalizationWorker.cs:670,1292` (`CreateDocumentAsync`) | BackgroundService (Office upload) | `sprk_document` | yes | no | yes, if it maps |
| `Services/Finance/Submission/TrackingNumberAllocator.cs:122` | e-billing submission | `sprk_invoicesubmission` | yes | no | out of scope (ebilling-r1) |
| `Services/Communication/ThreadResolver.cs:234` | job handler | `sprk_communication` thread and participant rows | thread row: yes | no | no: a structural row |
| `Services/Communication/CommunicationEnrichmentService.cs:1288`, `CommunicationProposalApplyService.cs:751`, `CommunicationCreateTaskApplyService.cs:864,907` | job and apply | `sprk_emailreviewlog` | no | — | no |
| `Services/Insights/Precedents/DataversePrecedentBoard.cs:105` | insights job | `sprk_precedent` | no | — | no |
| `Services/Signals/SignalWriter.cs:653`, `DecisionRecordWriter.cs:620,697,848` | signals | `sprk_signal`, `sprk_decisionrecord` | no (ontology, excluded) | — | no |
| `Services/Ai/Membership/MembershipJunctionUpdater.cs:182` | membership job | `sprk_userentityassociation` | no | — | no |
| `Services/ExternalAccess/AssignedAccessStore.cs:430,512,644` | access jobs | access ledger rows | no | — | no |
| `Services/NotificationService.cs:118`, `Services/Notifications/OutboxService.cs:155`, `Services/Ai/PlaybookSchedulerJob.cs` (notification playbooks) | jobs | notification and outbox rows | no | — | no |
| `Services/Ai/Chat/ChatDataverseRepository.cs:76,280,317` | chat | `sprk_aichatsummary`, `sprk_aichatmessage` | no | — | no |
| `Services/Finance/RateCardImportService.cs:204,235,291`, `RuleSetService.cs:405,466,508` | import and admin (DI-021) | rate card and rule set rows | no | — | no |

The two Dataverse-ingress webhooks (`Api/AcsEventGridEndpoints.cs` and the `Api/CommunicationEndpoints.cs`
incoming-webhook) create no rows themselves. They enqueue the jobs listed above. The Compose sync endpoints were not
traced for this inventory.

**Should map but does not (described, not fixed).** The owner decides whether a profile from the parent record should
apply to these creates:
- system-generated to-dos (`TodoGenerationService`, `InquiryReplyTodoCreator`);
- events from playbooks and from communication apply (`TaskActionCore`);
- analyses mirrored from insights (`DataverseObservationMirror`);
- documents and communications from inbound email, chat and Office upload (`IncomingCommunicationProcessor`,
  `MessagingIngestor`, `MessageAttachmentMaterializer`, `UploadFinalizationWorker`).

Each would need the shared gates of section 1, because the creator runs with no calling user.

## 6. Evidence

| Check | Before | After |
|---|---|---|
| New registry, guard and push tests | 6 failed, 35 passed (the controls passed) | all pass |
| Filtered unit and contract run (invoice, push selection, sync-mode migration, write gates, push contract, Repair, Membership, Daily Briefing, people-targeting) | | 1126 passed, 0 failed |
| ArchTests (role privilege guard, seeder class test) | | 43 passed |
| Script class data with the old script | 3 failed, 3 passed | 6 passed |
| `-SelfTest` | | passes |

verify-suites.sh and the BFF build: see the commit.

**Step 9.5 (code-review, adr-check, run in session).**
- No F-class finding.
- No plugin, endpoint, DI registration, package or AI dependency is added (ADR-002, ADR-010, ADR-013 clean).
- The new guards carry must-fire and must-not-fire controls.
- K4: the `PushRuleSelection` remark "only the create maps them" states the U3 (c) intent. Filling the copies at create
  is 032b-2's work.

Rework: none.
