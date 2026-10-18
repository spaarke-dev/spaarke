# Task 042b: gate Decision Record visible to the communication's sharees (D-169, #1876)

## Line references (checked 2026-10-18 at 3deb8c4a3)

| POML reference | Found | Drift |
|---|---|---|
| `DecisionRecordWriter.ResolveOwnerCoreAsync` ~1518-1588 | 1518-1588 | none |
| `ReadCommunicationOwnerAsync` ~1602-1650 | 1602-1651 | none |
| `requireTeam` enforced ~1579 | 1579 | none |
| owner stamped ~1449-1451 ("no team, no share") | 1449-1452 | none |
| `GateDecisionRecordJob.cs` ~227 | 227 (`WriteGateDecisionAsync`) | none |
| `CommunicationRuleGate.cs` ~226-229/286/373 | 228-229 is the doc comment; the enqueue is `EnqueueDecisionRecordAsync` 345-376 (called at 324); the failure metering is 375 | small: 286 is not an enqueue site |
| `DirectThreadAccessService.GrantMessageAccessAsync` ~191, ~201, ~269-273 | 191, 201, 267-274 | none |
| Grant callers `CommunicationService.cs` ~1029/~1083, `ThreadResolver.cs` ~1107, `MessagingIngestor.cs` ~129, `EmailUploadCaptureService.cs` ~283 | same lines | none |
| `ReconciledEmailFiling.FileIfUnfiledAsync` ~103-175; association ~135; re-stamp ~147; DR report-only ~155/~165; `AfterRefileAsync` ~170 | 103-196; 135; 147; 155/165; 170 | the method now ends at 196 (signal trigger after the refile step) |
| `SecureChildReconciler.AfterRefileAsync` ~741-766 | remarks 735-766, method 767 | none |
| `SecureChildLineage` `sprk_decisionrecord` ~100-102 | 100-104 | none |
| `DecisionRecordWriterTests.GateDecision.cs` ~240 | the test at 239-266 | none |
| Census entry `RecordOwnerAssignmentCensusTests.cs` ~218 | **drift**: line 218 is the `MessagingIngestor` waiver. The Decision Record create-census entry is at 277-286 and its owner-write entry at 557-560 | both updated |

## Escalation triggers: none fired

- **Append-only / immutability guards.** `DecisionRecordContextImmutabilityTests` pins (1) the four context columns to the create builders, (2) the writer's
  own client calls to Create/Retrieve/WhoAmI, and (3) no string-literal update of a Decision Record row. Its header says the owner and the shares
  are LIVE ("Its filing, stamps, owner and shares are LIVE"). This task writes no context column, adds no call to the writer's client inside
  `DecisionRecordWriter.cs`, and addresses the row through a constant in an `AssignRequest`. No guard forbids it; no exemption was added.
- **Assign identity.** `OntologyWriterDataverseClient` (the Spaarke Ontology Service user, `prvAssignsprk_DecisionRecord` Global, D-33c) is an
  existing DI singleton; the new helper injects it. No new client or credential. The re-own uses `AssignRequest`, which needs Assign only (the
  writer holds no Write on the table, and none is added).
- **Roles / privileges / schema.** None changed. Shares are written by the BFF app identity through `IDataverseRecordShareService` (no Share
  privilege added to any role).

## Decisions taken inside the brief

- **Where the logic lives.** One new singleton, `Services/Signals/GateDecisionRecordSharing.cs`, owned by the Decision Record side. The writer,
  `DirectThreadAccessService` and `ReconciledEmailFiling` each take it as an optional last constructor parameter (the `ReconciledEmailFiling`
  `signalTrigger` idiom), so no existing construction site changes and the `DirectThreadAccessService` diff is a field, a parameter and one call.
- **Both sides read current state.** Write time and mirror time run the same routine: read the communication (unfiled AND user-owned), read its
  current POA shares (strict read), keep the systemusers holding Read, drop the externals, grant Read on each user-owned gate Decision Record,
  then read the record's owner again and revoke what this pass granted when it is no longer a user. The order of the DR write and a
  communication share therefore does not matter.
- **The communication must be user-owned, not only "not Secure-team-owned".** An unfiled communication keeps its creator (E1); a team-owned one
  (Secure team, or FR-E7 category routing) is never shared through this path. This is stricter than `DirectThreadAccessService` ~253-276 and
  covers the D-123 case (a Secure-team-owned communication) by construction.
- **Gate records are identified** as filed under the communication (`sprk_regardingcommunication`), no `sprk_confirmedby`, created by the
  ontology writer (`createdby` = WhoAmI on its connection, task 153 F1), and owned by a user. WhoAmI is asked only when a candidate row exists,
  so a messaging grant on a communication with no gate record costs one query and never touches the writer's connection.
- **Internal only.** A sharee is skipped when it is a team (no flag to read), when its `systemuser` row is missing or the read fails (unreadable),
  when `sprk_isexternal = true`, or when its `domainname` carries `#EXT#` (a B2B guest, `ContactBindingDecision.IsGuestDomainName`). A **blank**
  flag is read as internal: owner round 67 (2026-10-06, "sprk_isexternal means they're external"; `ISystemUserIdentityResolver.IsExternalAsync`
  and `InternalShareEndpoints.ClassifyEligibility` apply the same rule), and most dev person users carry no value. "Cannot be read" is taken to
  mean the read failed or the row is missing. **Owner to confirm** if a blank flag should also be skipped (that would share with almost nobody
  on dev today).
- **Re-own on filing** asks `IRecordOwnershipResolver` with the communication first and the record's live core-ancestor stamps
  (`CoreAncestorResolver.CoreAncestorLookups`), as the content of the communication (`KeepCreatorUnlessTargetIsTeamOwned`) — the gate writer's
  own question. Only an ordinary owned answer is applied; a Secure answer is left to `SecureChildReconciler` (nothing assigned or revoked); an
  Unchanged or refused answer keeps the user owner and the shares. After the Assign the owner-change cache eviction runs once
  (`IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync`, "every owner-changing writer MUST call it"), then the owner is read back;
  only a read-back that names the resolver's team revokes every POA share on the record.
- **Telemetry.** Two new failure reasons on `ontology.writer.failures`: `decision_participant_share_failed` and `decision_filing_reown_failed`;
  two EventIds, 50316 and 50317.

## Test scope beyond the stated contract

None beyond the acceptance criteria; each test maps to one criterion (see the task's report).

## Out of scope, noticed

- (Superseded by round 2 below: the re-own now runs from `SecureChildReconciler.AfterRefileAsync` on every communication re-file that
  reaches it.)

## Seeding proofs (2026-10-18)

| Seed | Break | Named test(s) | Seeded | Restored |
|---|---|---|---|---|
| A | `ShareWithCommunicationShareesAsync`: the "communication not owned by a user" guard replaced by a never-true condition | `ARecordWhoseCommunicationIsSecureTeamOwned_GetsNoParticipantShare(AtWrite, Mirror)` | 2 failed ("D-123 … but found {a1}") | pass |
| A2 | `GrantAndConfirmAsync`: the post-grant owner re-check always reads "still a user" | `Race_FilingReownsAndRevokesBetweenTheMirrorsReadAndItsGrant_…` | 1 failed (found a1, a2 on the team-owned record) | pass |
| B | `ReownAsync`: the read-back check removed, so the revoke runs whatever the read-back says | `Filing_AnAssignThatLandedButCannotBeReadBack_KeepsTheShares`, `Filing_AFailedReown_…`, `FileIfUnfiled_AFailedGateRecordReown_…` | 3 failed (shares found empty) | pass |

## Round 2 (uac-r2 review, 2026-10-18)

1. **No optional dependency.** `GateDecisionRecordSharing` stays registered unconditionally in `SignalsModule` (singleton over singletons:
   `IGenericEntityService`, the POA seam, the writer client, the resolver, `IMembershipCacheInvalidator`, all singletons), so the scoped
   `SecureChildReconciler` and the singleton `DirectThreadAccessService` take it as a REQUIRED parameter with no captive scope. Proven on the
   real host composition by `GateDecisionRecordSharingRegistrationTests` (the second test resolves with `ValidateScopes = true`). Every test
   construction site passes a double (`new GateSharingWorld().Sharing`, an empty world). `DecisionRecordWriter`'s parameter is still optional
   (not in the round-2 ask; seven test sites in four suites).
2. **The mirror needs the communication unfiled.** Already so: `ShareWithCommunicationShareesAsync` reads the communication and returns on
   `IsFiled` (`GateDecisionRecordSharing.cs:189`), and on a missing row (`:183`); a read fault propagates to the mirror's catch, so nothing is
   shared (fail closed). Tests: `AGrantOnAFiledCommunicationWhoseRecordStayedUserOwned_AddsNoShare` and (new)
   `AGrantOnACommunicationWhoseFilingCannotBeRead_AddsNoShare_FailClosed`.
3. **Re-own on every filing path through one call** in `SecureChildReconciler.AfterRefileAsync`: after UAC's own steps (the body moved
   unchanged into `RefileStepsAsync`), for `sprk_communication` only, every re-file (not gated on the descendant pass), fault-isolated with an
   Error log (ids only) and only the caller's cancellation rethrown. `ReconciledEmailFiling`'s direct call is removed (the file is back to its
   base). The re-own is idempotent: a team-owned record is not found again (`Filing_ASecondCall_OnARecordAlreadyTeamOwned_DoesNothing`,
   `AfterRefile_Twice_ReownsOnce`).

### Every path that sets an existing communication's filing

| Path | Write | Ends in `AfterRefileAsync`? | Test |
|---|---|---|---|
| Office save (`ReconciledEmailFiling.FileIfUnfiledAsync`) | `IncomingAssociationResolver.ApplyDecisionAsync` :502/:511 | YES, `ReconciledEmailFiling.cs:170` | `FileIfUnfiled_ReownsTheGateDecisionRecord_ThenRevokesItsShares` (real reconciler) |
| `PATCH /api/communications/{id}/filing` (and `PATCH /api/v1/child-records/{table}/{id}`) | `ChildRecordEndpoints.cs:497` (`OwnedChildWrite.RefileAsync`) | YES, `ChildRecordEndpoints.cs:547`, only when written | `CommunicationFiling_UnderAnOrdinaryMatter_ReownsTheGateDecisionRecord_…_D169` (the route's re-file core, real reconciler) |
| Chat update tool `dataverse.update_record` (`DataverseUpdateRecordHandler`) | `:293` → `OwnedChildWrite.RefileAsync` | YES, `:350`, when written and `children` resolved | `UpdateTool_ReFilingACommunication_ReownsTheGateDecisionRecord_…_D169` (real handler, real reconciler) |
| Playbook update node (`UpdateRecordActionCore`) | `:176` `ReparentAsync` / `:170` PATCH | **NO** (comment :206; calls `ReconcileThreadForFilingAsync` itself) | none: the path cannot reach the hook |
| Generic update handler (`DataverseUpdateHandler`, caller `OutputOrchestratorService.cs:134`) | `:75` `ReparentAsync` / `:71` | **NO** (comment :102) | none |
| Thread JOIN / respond-into-thread (`ThreadResolver.cs:323`, from `:278` and `CommunicationService.AssignExplicitThreadAsync` :1060) | `ReparentAsync` with the thread stamp | **NO** | none |
| `IncomingAssociationResolver.ResolveAsync` | `:132` → `ApplyDecisionAsync` | **NO** (no BFF caller found) | none |
| Create-time `ApplyToNewRecordAsync` (`IncomingCommunicationProcessor.cs:366`, `EmailUploadCaptureService.cs:208`) | create, owner already resolved | NO (create, not a re-file) | none |

The round-2 brief named `UpdateRecordActionCore` as "the chat update tool"; the chat tool is `DataverseUpdateRecordHandler` (ends in the
hook, tested). `UpdateRecordActionCore` (the playbook node), `DataverseUpdateHandler` and the thread JOIN reparent do not end in
`AfterRefileAsync`, so their re-file leaves a user-owned gate record with its participant shares (the mirror then stops: the communication is
filed). Reported, not fixed.

### Round 2 seeding proofs

| Seed | Break | Named test(s) | Seeded | Restored |
|---|---|---|---|---|
| C | `ShareWithCommunicationShareesAsync`: the `IsFiled` return made never-true | `AGrantOnAFiledCommunicationWhoseRecordStayedUserOwned_AddsNoShare` | 1 failed (found a1) | pass |
| D | `AfterRefileAsync`: the communication condition made never-true (no hook call) | the hook, route, tool and Office re-own tests | 6 failed (5 non-call controls passed) | pass |
| E | `AfterRefileAsync`: the hook's catch filter made never-true | `AfterRefile_AGateHelperFault_…`, `FileIfUnfiled_AGateHelperFault_…` | 2 failed | pass |
