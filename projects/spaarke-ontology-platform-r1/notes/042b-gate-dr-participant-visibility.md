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

## Round 3 (adversarial passes, 2026-10-18)

- **F1-a, filed = the resolver's test.** `IsFiled` (`GateDecisionRecordSharing.cs:551`) is now `RecordOwnershipContext.ParentsOf(row)` minus the
  row itself, over the WHOLE communication row (`ColumnSet(true)`, as `RecordOwnershipResolver.ReadOwnershipParentsOfAsync` reads a content
  row's parent). A communication regarding only a contact, an account or an organization is unfiled and gets D-169's shares. The misleading
  "takes the team-owned path" log now says the filing re-owns the records. Test:
  `ACommunicationRegardingOnlyAContactAnAccountOrAnOrganization_IsUnfiled_ItsRecordGetsTheParticipantShares` (6 cases, at write and mirror).
- **F1-b, filing between the owner decision and the create.** `ShareAtWriteAsync` re-owns the record (`ReownAsync`, after re-reading it with
  its stamps) when the communication reads as filed (`:124`). Tests: `AtWrite_ACommunicationAlreadyFiled_…` and the writer-level interleave
  `Gate_AFilingThatLandsBetweenTheOwnerDecisionAndTheCreate_ReownsTheRecordAtWrite_D169_042bRound3`.
- **K1.** `DecisionRecordWriter`'s helper parameter is REQUIRED; its 7 test sites pass the empty-world double; the DI test asserts the real
  container's writer holds the host's helper.
- **K2, leftover shares.** `ReownAfterFilingAsync` now also takes this communication's writer-created, unconfirmed gate records owned by a
  team: when one still holds a user share and the resolver answers that SAME ordinary team, its user shares are revoked
  (`RevokeLeftoverSharesAsync`, `:445`). A Secure answer, a refusal or another team leaves the row alone (a Secure-team row's shares are
  the Secure mirror's). Tests: `Filing_ARevokeThatFailed_IsRetriedOnTheNextFiling_AndTheLeftoverSharesGo`,
  `Filing_ASecureTeamOwnedRecordWithUserShares_IsNeverTouched_NorOneOwnedByAnotherTeam`.
- **K3, Assign census.** `RecordOwnerAssignmentCensusTests.EveryAssignCallerIsCensused` scans `\.AssignAsync\s*\(|\bnew\s+AssignRequest\b`
  by member against an exact allow-list (grep 2026-10-18: `OntologyWriterDataverseClient.AssignAsync` (the AssignRequest) and
  `GateDecisionRecordSharing.ReownAsync`; `SecureChildReconciler`'s private `AssignAsync` and `ResolveAndAssignAsync` do not match).
  `AssignCallDetector_NegativeControl`: must fire on a qualified call and a raw AssignRequest (UNLISTED, STALE), must not fire on the
  declaration, `ResolveAndAssignAsync`, an unqualified private call or a comment.
- **K4, telemetry.** The caller's own cancellation in the mirror (`:153`) is logged at Information and not counted; any other fault is.
  An unreadable owner after the grants is counted (`:331`). Tests: `Mirror_TheCallersOwnCancellation_IsNotCountedAsAFailure_AnyOtherFaultIs`,
  `AnOwnerThatCannotBeReadAfterTheGrants_IsCounted_AndTheGrantsAreRevoked`.
- **K5, uac-r2's end-to-end.** `CommunicationFiling_IntoASecureMatter_RemovesTheGateDecisionRecordsParticipantShares_D169_042bRound3` (the
  route's re-file core, the REAL reconciler and synchronizer over the share world): the user-owned gate record with a participant share
  ends owned by the Secure team with no participant share. It passed first time; no defect behind it.
- **Not changed (coordinator files them):** Unshare is not mirrored; an ordinary A→B move.

### Round 3 seeding proofs

| Seed | Break | Named test(s) | Seeded | Restored |
|---|---|---|---|---|
| F1-a | `IsFiled` back to the round-2 test (every RegardingFieldMap column + the pair) | `ACommunicationRegardingOnly…` | 6 failed | pass |
| F1-b | at write, return on a filed communication | `AtWrite_ACommunicationAlreadyFiled_…`, `Gate_AFilingThatLandsBetween…` | 2 failed | pass |
| K2 | no leftover pass | `Filing_ARevokeThatFailed_…` | 1 failed | pass |
| K2 guard | leftover pass ignores the resolver's answer | `Filing_ASecureTeamOwnedRecordWithUserShares_…` | 1 failed | pass |
| K4 | the caller's cancellation counted again | `Mirror_TheCallersOwnCancellation_…(True)` | 1 failed | pass |
| K4 | an unreadable owner only logged | `AnOwnerThatCannotBeReadAfterTheGrants_…` | 1 failed | pass |

### Publish size (CLAUDE.md section 10)

Measured in round 1 (fresh worktrees, `dotnet publish -c Release`, zipped with PowerShell `Compress-Archive`):

| Build | Files | Raw bytes | Zip bytes | `Sprk.Bff.Api.dll` |
|---|---|---|---|---|
| origin/master `c690b0ac2` | 192 | 130,905,972 | 39,212,186 | 19,651,584 |
| branch base `3deb8c4a3` | 192 | 130,647,132 | 39,130,158 | 19,425,280 |
| 042b head `fbb1f70a6` | 192 | 130,679,356 | 39,142,835 | 19,452,928 |

042b vs its base: +32,224 bytes raw, +12,677 zipped, +27,648 on the DLL, no file added. (Head vs master is -226 KB only because the
branch is cut from the docs branch, not master.) Rounds 2 and 3 add C# methods to existing files and no package or file, so the delta stays
in the tens of kilobytes; not re-measured (the brief: re-measure only if the code delta is no longer trivial).

## Round 4 (re-check of round 3: four K items, 2026-10-18)

1. **Assign census** (`RecordOwnerAssignmentCensusTests.cs`). The call regex is now
   `\.\s*AssignAsync\b|\bnew\s+(?:global::)?(?:\w+\s*\.\s*)*AssignRequest\b`: a call, a method group, `this.`, `?.`, a line break after
   the dot or before `(`, and a `global::`- or namespace-qualified `new`. A regex over call shapes cannot see an alias (`new AssignAlias {…}`)
   or a target-typed `new()`, so the `AssignRequest` type token is censused too, by member, wherever it appears in code (the alias's
   declaration, the variable's type). Only `OntologyWriterDataverseClient.AssignAsync` names it today. The negative control seeds every
   shape (9 must-fire on the call regex; the alias and `new()` must-fire on the token) and 5 must-not-fire members.
   Known limit: `new OrganizationRequest("Assign")` (the untyped request) is caught by neither; no such code exists.
2. **Secure end to end** (`SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs`, `CommunicationFiling_IntoASecureMatter_…`). The gate
   helper is now built over THE SAME share world, share table and real `RecordOwnershipResolver` as the engine
   (`GateHelperOverTheShareWorld`). Its after-re-file step finds the record (now Secure-team-owned, carrying the mirror's share for the
   matter's sharee), gets the Secure answer and logs "left as they are"; no Assign, no warning, the mirror's share stays.
3. **IsFiled with the real resolver** (`GateRecordSharing_OverTheRealResolver_…`): contact-only communication shared at write and kept
   user-owned on a filing pass (resolver Unchanged); a matter-regarding one gets no participant share and is re-owned at write to the
   ordinary matter's team.
4. **Logging.** `ReadOwnerOrNullAsync` returns its fault instead of logging it; the caller's one `ShareFailed`/`ReownFailed` warning
   carries it (both the Assign's and the read's when both fail, as an `AggregateException`). The leftover log reports
   `{Count} of {Leftover}` with the count actually revoked. Not changed (outside the brief): `ReownAsync`'s closing "re-owned … {Count}
   share(s) revoked" still reports the attempted count.

### Round 4 seeding proofs

| Seed | Break | Named test(s) | Seeded | Restored |
|---|---|---|---|---|
| 1 | the round-3 call regex | `AssignCallDetector_NegativeControl` | 1 failed | pass |
| 2 | the leftover pass ignores the Secure answer | `CommunicationFiling_IntoASecureMatter_…` (the round-3 version, over an empty world, did not see it) | 1 failed | pass |
| 3 | `IsFiled` counts any lookup (a contact too) as filed | `GateRecordSharing_OverTheRealResolver_…` | 1 failed | pass |
| 4 | the round-3 helper | `AnOwnerThatCannotBeRead_IsLoggedOnce_…` (both), `Filing_ALeftoverRevokeThatFailsAgain_…` | 3 failed | pass |

## 042c: thread-filed communications at write (2026-10-19)

**Defect (F1, #2110 review).** `ShareAtWriteAsync` re-owns a just-created gate record only when `ShareWithCommunicationShareesAsync` returns
`true` ("filed"). A communication filed by a THREAD JOIN (uac-r2 `ThreadResolver.JoinCoreAsync`) carries only `sprk_communicationthread` plus a
team owner and never its own parent lookups, so `IsFiled` is false and the team-owned branch returned `false`. Race: the gate job decides owner
U while the email is unfiled; the join re-owns the email to the matter's team, and its `AfterRefileAsync` finds no record yet; the writer then
creates the record owned by U; `ShareAtWriteAsync` sees "not filed" and returns. The record stays U-owned with no shares, so the matter's team
reads the email but not its gate record.

**Fix.** In `ShareWithCommunicationShareesAsync` the combined "team-owned or not a systemuser" condition is split. (a) A team-owned
communication (`owningteam` set) logs that its gate records follow the communication's owner (no participant share, D-169) and returns `true`,
so the caller at write re-owns through the resolver. (b) Any other non-user owner keeps the old log and `false`. `IsFiled` and its
`ParentsOf(...)` call are untouched (master has a different overload there). `ReownAsync` is unchanged: it asks `RecordOwnershipResolver`, a
Secure answer is left to `SecureChildReconciler`, and Unchanged leaves the record as it is. `MirrorCommunicationSharesAsync` ignores the return
value; the only other caller of `ShareAtWriteAsync` is `DecisionRecordWriter`, which ignores it too.

**Tests.** `AtWrite_ACommunicationFiledByAThreadJoin_ReownsTheRecordToTheResolversTeam_WithNoParticipantShare` and
`AtWrite_ACommunicationFiledByAThreadJoin_UnderASecureTeam_LeavesTheRecordToTheReconciler`.

**Known limit (from the #2110 review).** uac-r2's same-parent skip (`ChildRecordEndpoints.cs:542`, `DataverseDocumentsEndpoints.cs:225`, on their
branch) removes one retry path for a gate record whose earlier re-own failed (EventId 50317). A real move still retries it.

**Known limits (review round, 2026-10-19).**
- (a) A thread join that lands AFTER the at-write read of the communication is covered only by uac-r2 PR #2110 (`JoinThreadAsync` -> `AfterRefileAsync` -> `ReownAfterFilingAsync`). That PR is not yet on master or on this branch. Until it merges, a late join leaves the record owned by the user who decided.
- (b) For a team-owned communication the resolver answers either Secure or the communication BU's default team (`RecordOwnershipResolver.cs` ~:1529, ~:1615); it never answers Unchanged. A communication reassigned to a queue team or to another BU's team inside the window therefore re-owns the record to that BU's default team, and the decider can lose read. The same applies when FR-E7 category routing (`CommunicationEnrichmentService` ~:724-765) makes the communication unfiled-but-team-owned while it races the gate job. This is not a regression: the writer already does the same when the communication is team-owned at decision time (or routing landed first). It is consistent with D-169.
- (e) uac-r2 #2113 review, 2026-10-19:
  - A later owner-only change of the communication to an ordinary team (no re-file) leaves the record stale: user-owned, keeping its participant shares, and unreadable by the new team. Nothing over-shares. This is pre-existing and tracked as #2114. A move to Secure is covered by the reconciler's recent-changes pass plus the synchronizer trim.
  - #2110 also closes a second window: an at-write read that falls between a join's lookup write and its owner write. That read grants participant shares, which the join's `AfterRefileAsync` → `ReownAfterFilingAsync` then revokes.
  - Between the write and the reconciler pass, a Secure-family record stays readable by its user owner. That window is unchanged and was accepted by the owner (uac-r2 round 28 item 2).
- (c) The Secure answer adds no participant share and keeps the record user-owned until the `SecureChildReconciler` recent-changes pass picks it up (`SecureChildLineage` covers `sprk_decisionrecord`). A Refused answer now logs `FilingReownFailed` (50317), where before it was skipped silently.
- (d) The team-owned check reads only `owningteam`. A row with an `ownerid` of type team and no `owningteam` would fall through to "not owned by a user". Dataverse always populates `owningteam`, so this is not reachable today.
