# Task 016 notes: Repair mode, fill-only and idempotent (FR-14, FR-25, NFR-03)

## Rigor declaration

FULL (POML). Repair can change `ownerid` on existing rows, which is security-adjacent; the task adds tests at KEEP paths.
Executed on opus at high effort. The implementing agent could not launch its own adversarial reviewer (nested-agent
limit), so it stopped at the review step. The main session runs the review over the commit range below.

Commits on `task/fm-016` (cut from `c9cca37a16`):

| Commit | Subject |
|---|---|
| `80d94166df` | refactor: share step 7 and step 8 logic for Repair (Create unchanged) |
| `79302346c3` | feat: Repair mode (FR-14), fill-only and idempotent |
| `ac8103c793` | fix: census the Repair owner write |
| (this commit) | docs: task 016 notes |

## Conflict check

`gh pr list` (open PRs, file lists) shows no open PR that touches `RecordCreation/RecordCreationService*.cs`,
`CoreAncestorRestamper.cs` or `RecordOwnerAssignmentCensusTests.cs`. #2191 changes `OfficeRecordCreationService.cs`,
`MappingSourceCallerMask.cs` and `SecureRootFilingGateFixtures.cs`:

- This task does not edit `OfficeRecordCreationService.cs`, and it does not touch the source-read path in
  `RecordCreationService.Mapping.cs` (that file is unchanged).
- The new test uses `SecureRootFilingGateFixtures.Over(entities)` only as a constructor argument that Repair never calls.
  If #2191 changes its signature, adjust that one line.

## Placement Justification (CLAUDE.md §10)

This task adds no endpoint, DI registration, package or background work. It adds one method on the existing scoped
`RecordCreationService` (ADR-010) and one internal method on the existing `CoreAncestorRestamper`. The repair pass job
and the admin trigger belong to task 052.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService.RepairAsync` and its report types (`RecordRepairOutcome`, `RecordRepairField`, the enums, `RecordRepairReason`) | `CoreAncestorStampReconciliationJob` repairs I-1 only. `SecureChildReconciliationJob` repairs secure children only. Neither handles mapping, owner or search index | Extends the task 013 service and shares the create's step logic (below). The report types are new because no existing outcome type describes an update of an existing row | Natively created, imported and agent-written rows never get mapping, a search index or the standard owner. Task 052 has nothing safe to call |
| `ResolveOwnerOverAsync`, `OwnerDecided`, `TeamBusinessUnitAsync`, `BusinessUnitSearchDefaultsAsync` (private) | `DecideOwnerAsync` and `SearchIndexDefaultAsync` | Extracted from those two methods, not copied (the POML says "share step logic, do not copy it"). Create calls them with identical reads and texts | Repair would duplicate the owner and BU-default logic |
| `CoreAncestorRestamper.PlanMissingStampAsync` (internal) | `RestampChildAsync` plans the same stamp, but re-reads the row, overwrites, clears and cascades | Reuses its probe, its classification, the root read and `PlanStamp`. It adds only the fill-only filter | Repair cannot add a missing I-1 stamp inside its one fill-only update, or else it overwrites a present stamp |
| `RecordCreationService.Repair.cs` (partial) | `RecordCreationService.Mapping.cs` is the task 014 precedent for a partial | Same class. Kept separate from the hot `RecordCreationService.cs` to limit merge overlap. `RepairOwnerAsync` stays in `RecordCreationService.cs` (see "Owner-write census") | — |

## How it works

`RepairAsync(table, id, ct)` makes one whole-row read (`ColumnSet(true)`, `NoLock = false`, the reparent's read). Steps 7,
3, 8 and 10 then each add to one patch. Since review pass 1 (F1-b) the owner runs FIRST, because it can block the
mapping; see "Review pass 1 fix round".

- **Step 3, mapping.**
  - The source is resolved by `MappingSourceResolver` from the row's filing. When the row is filed under an
    intermediate, its I-1 stamp columns are left out, because they are copies of that intermediate's filing, not
    filings of the row.
  - The pair's record type is read app-only.
  - The profile is fetched with `GetFieldMappingProfileWithRulesAsync`. The source is read with
    `MappingSourceReadProbe.ReadAsync(MappingSourceReader.System, ...)`, which is app-only and has no probe.
  - `CreateTimeFieldMapping.ApplyFillOnly` runs over a copy of the row. This is the create's own FR-02 fill decision, so
    blank strings are not supplied, and option sets and booleans count as supplied when present.
  - A mapped value that would file the row is not written: a filing lookup, an ownership-parent lookup, or an I-1 stamp
    column (`record_repair.mapped_value_would_refile`).
- **Step 7, owner** (`RepairOwnerAsync`).
  - Applies only to a row whose `ownerid` is a systemuser.
  - Parents come from `RecordOwnershipContext.ParentsOf(row)` plus the record-thread filing (`RecordThreadFiling`), as
    the reconciler and the reparent read them.
  - If the table's ownership exception applies and the row is unfiled, the owner is kept.
  - For an unfiled row, the acting user is the owning person (I-6). An application user, or one whose status is
    unknown, counts as nobody, so the owner is refused with `record_owner_no_source`.
  - `ResolveOwnerOverAsync` and `OwnerDecided` then decide, the same as for Create.
  - A refusal or a fault leaves `ownerid` unchanged and is reported as a typed `RecordCreationRefusal`.
- **Step 8, search index.** On a search-indexed table, each column the row does not hold is filled from the OWNING
  business unit: the repaired team's unit, or else the row's `owningbusinessunit`. Values are written as SDK types
  (string, and `EntityReference("sprk_aisearchindex", id)`).
- **Step 10, I-1 stamp.** `PlanMissingStampAsync` fills only stamp columns that are empty. A held stamp, stale or not,
  and the row's children are left to the reconciliation job.

The write:

- An empty patch means zero writes (`NothingToRepair`).
- Otherwise the invoice-family seam runs, then exactly one `UpdateIfRowVersionMatchesAsync` conditioned on the version
  that was read.
- `DBConcurrencyException` means the row changed: nothing was written and the repair is not retried (`RowChanged`).
- Any other fault returns `Failed` (`record_repair.write_failed`) with no retry, because the write may have landed.
- An owner write, or a write attempt that included the owner, triggers `IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync`. Its contract says
  "every owner-changing writer MUST call it". The invalidator is resolved through the existing `IServiceScopeFactory`,
  so no registration changes.

## Decisions and deviations

1. **Whole-row read, not "needed columns only"** (POML step 3). The filing lookups decide both the mapping source and
   the owner, and they are not a fixed list. This is the reparent's and the reconciler's read. It is still ONE read of
   the row.
2. **Repair's write is SDK typed** (`IGenericEntityService.UpdateIfRowVersionMatchesAsync`), not the create's Web API
   item. The system principal has no caller metadata reader; the app-only metadata reader is task 033's. This brings
   one known limit, K-1 below.
3. **Owner and fields in the same update.** Dataverse Update accepts `ownerid`, and the resolver's #2143 conditional
   owner write uses the same call. The reconciler's read-back is not repeated: a conditional update that returns
   success has applied.
4. **New partial file `RecordCreationService.Repair.cs`.** The POML justification says "no new production file". This
   is the same class, split the way task 014 split `Mapping.cs`.
5. **Pre-write rules are not run** (w2-coordination: they are not widened to updates).
6. **The creator person is not stamped by Repair.** There is no person who asked.

## Escalations

- **Trigger 2: FIRED as a design condition; not decided.** In the test world, and on real data, a user-owned row
  filed under a record in another business unit resolves to that unit's team. Almost every table this service creates
  is an ownership parent (`RecordOwnershipResolver.OwnershipParentEntities`): matter, project, work assignment, todo,
  event, memo, invoice, report card, analysis, document, budget, KPI assessment, billing event and communication. So
  re-owning such a row moves it, and the rows filed under it, to another business unit.
  - Implemented default (fail closed, pending the owner): when the resolved team's business unit differs from the row's
    `owningbusinessunit` on an ownership-parent table, the owner is NOT written
    (`record_repair.owner_moves_business_unit`). A warning line is logged, prefixed `ESCALATION (task 016 trigger 2)`.
    The other fields are still repaired.
  - This also covers a user-owned row under a SECURE parent (Secure Record business unit). Those rows remain
    `SecureChildReconciliationJob`'s.
  - **Decision needed:** allow the cross-BU re-own, for all tables or only for leaf tables with no children rows? Or
    keep the skip, and have task 052 report the count?
- **Trigger 1: not judged a hidden defect.** A row filed under nothing has no mapping source. It is reported as
  `record_repair.mapping_source_none`, the FR-03 silent no-op, and task 052 counts it from that code. Nothing was found
  in this task that turns it into a data defect.

## Known limits (K-class)

- **K-1. No target metadata on the system path.**
  - A Default or Concat Number rule into a **Money** column is written as a decimal, and a rule naming a column that
    does not exist is written as is. Dataverse then refuses the whole update: `Failed`, `record_repair.write_failed`,
    nothing written, no retry. Every later pass reports the same failure until the rule is fixed.
  - Copy rules from a Money source copy the `Money` value and are fine.
  - Closes with task 033's app-only metadata reader. Until then the failure is visible per row, by code.
- **K-2. Children's stamps and secure isolation are not cascaded.** These belong to the jobs:
  `CoreAncestorStampReconciliationJob` and `SecureChildReconciliationJob`.
- **K-3. I-16 inherited Access Permission is not refreshed after a stamp fill.** The create does this after a create.
  It is not in the POML's step list, and adding it would mean a second write. **Flagged for task 052**: a row whose
  missing I-1 stamp Repair fills keeps its old inherited Access Permission until the I-16 refresh runs for it.

## ebilling-r1 criterion: PARTLY met

`InvoiceFamilyCoreRecordGuard` is not on master (PR #1911 is a draft), so the criterion "calls
`CheckGenericUpdateAsync` before writing" cannot be met yet. In its place:

- The ONE seam `InvoiceFamilyUpdateRefusalAsync` sits just before the write. For `sprk_invoice` and `sprk_billingevent`
  it currently refuses: the row is skipped, nothing is written, and the reason is
  `record_repair.invoice_family_guard_unavailable`, with a log line.
- A `TODO(#1911)` names the guard call that replaces the refusal.
- Must-fire test: `AnInvoiceFamilyRow_IsNotWritten_UntilTheInvoiceGuardIsOnThisBuild`. Must-not-fire test:
  `ARowOutsideTheInvoiceFamily_IsWritten_TheGuardDoesNotFire`.
- When #1911 lands, both tests are rewritten against the real guard: guard refuses means skipped, guard admits means
  written.

## Owner-write census

`RecordOwnerAssignmentCensusTests` failed on the new owner write. That is correct: a Routed write must sit in a file
whose members reach the resolver. `RepairOwnerAsync` was moved beside the create's step 7 in `RecordCreationService.cs`,
where it reaches the resolver through `ResolveOwnerOverAsync`. It is listed as `Routed`. The taint check passes, so the
value written is the resolver's team.

## Tests

`tests/integration/data-mutation/RecordOwnership/RecordRepairModeTests.cs` has 44 tests (49 cases with the theories), `[Trait("status","new")]`: 17 from the first round, 10 from review pass 1 and 17 from review pass 2.

- The row store is `OwnershipDirectory`: whole-row reads, and a conditional update applied with a version check.
- The stamp source is `StampWorld`.
- The real `RecordOwnershipResolver`, `CoreAncestorRestamper` and service are used.
- The app-only `IGenericEntityService` and `IFieldMappingDataverseService` mocks are STRICT.

### Fail-before evidence (mutants; each applied alone, built, run, restored; tree clean after)

| Mutant | Killed by |
|---|---|
| M1 an empty patch is still written | TwoConsecutiveRepairs, TeamOwnedRow, UnfiledCommunication, OwnerInAnotherBusinessUnit |
| M2 mapping ignores the row's current values (overwrites) | APresentValue_IsNeverOverwritten (+3) |
| M3 ownership exception removed | AnUnfiledCommunication_KeepsItsCreatorOwner |
| M4 owner resolved with a caller identity | Repair_RunsAsTheSystemPrincipal_NoCallerIdentity_NoWhoAmI (+1) |
| M5 business-unit guard removed | AnOwnerInAnotherBusinessUnit_IsNotWritten_EscalationTrigger2 |
| M6 invoice seam never fires | AnInvoiceFamilyRow_IsNotWritten (must-fire) |
| M6b invoice seam fires on every table | ARowOutsideTheInvoiceFamily_IsWritten (must-not-fire) (+7) |
| M7 held stamp overwritten | AHeldI1Stamp_IsNeverOverwritten |
| M8 search-index name filled although held | TheSearchIndexDefault_IsFilledOnlyWhereUnset |
| M9 team-owned rows re-owned | ATeamOwnedRow_KeepsItsOwner (+1) |
| M10 refused owner loses the row's business unit | AnUnresolvableOwner_..._OtherFieldsAreStillRepaired |

### Runs

- `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- ArchTests: 1232 passed, 0 failed (after the census entry; before it, 1 failed, which is the census firing as it
  should).
- `Sprk.Bff.Api.Tests` with the filter RecordCreation | Office | FieldMapping | SecureRoot | SecureChildOwnershipAiToolTests |
  RefileWriterAfterRefile | RouteAuthorizationGuardTests | CrossSurface | SearchIndex | RecordOwnership | RecordRepair |
  CoreAncestor: 2651 passed, 0 failed, 7 skipped (pre-existing skips), on the final code (`ac8103c793`).

## Build, publish, CVE

- Fresh master `9b6963d737`, Release publish under `C:\tmp`, `Compress-Archive -CompressionLevel Optimal`:
  - master: 192 files, 39,388,423 bytes (37.56 MB);
  - task branch: 192 files, 39,425,267 bytes (37.60 MB);
  - delta: **+36,844 bytes (+0.04 MB)**, same file count.
  - The branch is cut from `c9cca37a16`, behind master, so the delta also includes master's own drift.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

## POML acceptance criteria

| # | Criterion | Proof |
|---|---|---|
| 1 | Unmapped column filled; blank string not supplied | `AnUnmappedColumn_IsFilledFromTheMapping_AndABlankStringCountsAsNotSupplied` |
| 2 | Second repair: zero writes | `TwoConsecutiveRepairs_WriteNothingTheSecondTime` (M1) |
| 3 | Present value never overwritten | `APresentValue_IsNeverOverwritten_EvenWhenItDiffersFromTheMapping` (M2) |
| 4 | Owner only when systemuser-owned and not an exception; team-owned untouched | `AUserOwnedRow_TakesTheOwnerItsParentGives`, `ATeamOwnedRow_KeepsItsOwner_EvenOneTheRuleWouldNotGive` (M9) |
| 5 | Search index only when unset; I-1 stamp only when missing | `TheSearchIndexDefault_IsFilledOnlyWhereUnset` (M8), `TheI1Stamp_IsAddedWhenMissing`, `AHeldI1Stamp_IsNeverOverwritten_EvenAStaleOne` (M7) |
| 6 | Unresolvable owner: unchanged, typed refusal, others repaired; unfiled communication keeps creator | `AnUnresolvableOwner_IsLeftUnchanged_WithATypedRefusal_AndTheOtherFieldsAreStillRepaired` (M10), `AnUnfiledCommunication_KeepsItsCreatorOwner_TheRegistryException` (M3) |
| 7 | System principal, no WhoAmI | `Repair_RunsAsTheSystemPrincipal_NoCallerIdentity_NoWhoAmI` (M4) |
| 8 | Build; 013-015 tests, SecureChildOwnershipAiToolTests*, RouteAuthorizationGuardTests unchanged; publish and CVE | Runs above; no existing test file edited except the census list |
| 9 | ebilling invoice-family guard | PARTLY met: the seam plus must-fire and must-not-fire tests (see above) |

Also covered: `ARowChangedAfterItWasRead_IsNotWritten_AndNotRetried` (NFR-03),
`AMappedLookupThatWouldFileTheRowUnderARecord_IsNotWritten`, and
`ATableThisServiceDoesNotCreate_IsSkipped_WithNothingRead`.

## Review pass 1 fix round

| Item | Fix | Test (fail-before proof) |
|---|---|---|
| F1-a | On a secure-flagged root (matter, project, work assignment) the owner is kept unless `sprk_issecure` reads an explicit `false`; empty counts as flagged. Reason `record_repair.owner_root_secure`. The other fields are still repaired | `AUserOwnedSecureFlaggedRoot_KeepsItsOwner_AndItsOtherFieldsAreStillRepaired(true / null)`; must-not-fire `AUserOwnedRootFlaggedExplicitlyNotSecure_IsReOwnedToItsUnitsTeam` |
| F1-b | `RepairOwnerAsync` runs before the mapping and resolves a FILED row's filing whatever happens to the owner (kept, refused, BU-guarded). If the resolution is a Secure owner and that team does not own the row, nothing is mapped (`record_repair.mapping_source_secure`). If the filing cannot be decided (refusal or fault), nothing is mapped (`record_repair.mapping_filing_undecided`), fail closed. A mapping fault now removes only the columns the mapping added, because the patch may already hold `ownerid` | `AUserOwnedEventUnderASecureMatter_GetsNothingMapped_FromTheSecureMatter`; must-not-fire `AnEventAlreadyOwnedByTheSecureTeam_IsStillMapped_TheGateDoesNotFire` |
| F1-c | A blank or whitespace mapped string is `MappingNoValue` and never written | `ATemplateThatYieldsABlankString_IsNeverWritten_TwoRepairsWriteNothing` |
| F1-d | **STOPPED, not implemented.** See below | — |
| F3 | Tests added | `AStampedChild_IsMappedFromItsSource_ItsStampIsACopyNotASecondFiling`; `AWriteThatFaults_WithTheOwnerInIt_IsEvictedFor_AndReportsNoExceptionText`; `AnOwnerResolverFault_LeavesTheOwner_AndMapsNothing_FailClosed`; `AMappingFault_MapsNothing_AndTheOwnerIsStillRepaired(profile unreadable / rule faults)`; `ALeafRowInAnotherBusinessUnit_IsReOwned_NoRowInheritsAccessThroughIt` |
| K2 | Census wording: the BU clause holds on ownership-parent tables only; secure-flagged roots named | `RecordOwnerAssignmentCensusTests` entry text |

The F1-b gate reads the OWNER resolution of the row's filing, not the mapping source's flag directly. Both come from the
row's own filing lookups; a filing under a secure root, or under an intermediate whose root is secure, resolves to the
Secure team (I-2). A row the resolver cannot decide maps nothing.

**Eviction on a faulted write (the contract the test pins):** when the patch held `ownerid`, a write that throws (other
than `DBConcurrencyException`) is evicted for anyway, because the write may have landed (a lost response). The reason is
`record_repair.write_failed`; no exception text reaches the outcome (ADR-019).

**Why a non-ownership-parent table may move business units** (corrected in review pass 2, K2): the trigger-2 guard
protects rows whose access follows the OWNER of the row being moved. `sprk_signal` and `sprk_budgetrevision` are not in
`RecordOwnershipResolver.OwnershipParentEntities`, so no row's owner is derived from theirs. That is not the same as "no
rows filed under it": a Decision Record is a lineage parent of Signals (`sprk_signal.sprk_decisionrecord`). Even so, no
access is inherited through its owner, because the share mirror walks lookups up to the roots, not owners. Re-owning
such a row changes only its own access, as Create would have owned it. Ownership-parent tables keep the fail-closed skip
until the owner decides. Decision Records are not re-owned at all now: Repair skips them entirely (review pass 2).

### F1-d: STOPPED (no app-only metadata source without new surface)

Create's metadata filter (`ValidForUpdate`, `MaxLength`, Money) reads `EntityDefinitions` through the CALLER's
`IDataverseUserClient`. Repair has no caller, and the brief forbids reading as one. The only app-only reader,
`MetadataService`, does not return `IsValidForUpdate` or `MaxLength`. Using it needs either a DTO extension (the DataGrid
metadata contract) or a new dependency / scope-resolved service in `RecordCreationService`: new surface either way. K-1
stands; per the coordinator, task 052 becomes hard-blocked on task 033.

### Owner questions (for the owner, not decided here)

1. **Should Repair re-own roots at all?** It now keeps every secure-flagged root, but still re-owns a user-owned root
   explicitly flagged not secure to the owner's business-unit default team.
2. **Built-in SYSTEM and INTEGRATION users.** They have no `applicationid`, so `ApplicationUserCheck` treats them as
   people. Their unfiled rows (I-6) move to the default team of the root business unit, where those users sit. Should
   they be treated like application users (refused, `record_owner_no_source`)?
3. Trigger 2 (above) is still open for ownership-parent tables.

### Fail-before evidence, fix round (mutants; each applied alone, built, run, restored)

| Mutant | Killed by |
|---|---|
| F1-M1 root secure-flag check removed | `AUserOwnedSecureFlaggedRoot_...(true)`, `(null)` |
| F1-M2 mapping gate removed (RepairAsync) | `AUserOwnedEventUnderASecureMatter_...`, `AnOwnerResolverFault_...` |
| F1-M3 secure-owner condition removed (RepairOwnerAsync) | `AUserOwnedEventUnderASecureMatter_...` |
| F1-M4 blank-string skip removed | `ATemplateThatYieldsABlankString_...` |
| F3-M5 I-1 copy-column exclusion removed | `AStampedChild_IsMappedFromItsSource_...` |
| F3-M6 mapping fault removes every patch key (drops the owner) | `AMappingFault_...(rule faults)` |
| F3-M7 no eviction on a faulted write | `AWriteThatFaults_WithTheOwnerInIt_...` |
| F3-M8 BU guard on every table (leaf move refused) | `ALeafRowInAnotherBusinessUnit_...` |

The full old files could not be copied back, because the new tests name the new reason codes. Each fix was instead
removed alone, with the result above. Create is unchanged: the fix round edits only the Repair members.

### Runs, fix round

- `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- ArchTests: 1232 passed, 0 failed.
- `RecordRepairModeTests`: 29 passed.
- The same filtered `Sprk.Bff.Api.Tests` set as above: 2663 passed, 0 failed, 7 skipped (the same pre-existing skips).

## Review pass 2 fix round (with the UAC-r2 conditions)

| Item | Fix | Tests |
|---|---|---|
| F1-e (i) | The mapping source must be one of the records the owner step resolved over: the row's parents and thread parents, plus the pair's record only where the pair files the row on its own (a project; `PairParentAsync`, now read once in `RepairOwnerAsync` and handed to the resolution as a parent). Otherwise nothing is mapped (`record_repair.mapping_source_undetermined`) | `APairOnlyEventNamingASecureMatter_CopiesNothingFromIt_ProbeP1`, `ATypedOrdinaryMatterPlusAPairNamingASecureMatter_CopiesNothing_ProbeP2`, `APairNamingAnotherOrdinaryRecord_IsNotCopiedFrom_...`; must-not-fire `ATypedOrdinaryMatterThePairAgreesWith_IsStillTheSource_AndMaps` |
| F1-e (ii) | The source's owner (and, on a root, `sprk_issecure`) is read in the same single source read. A flag that is not an explicit `false`, or a source owned by the Secure Record owner team (`SecureChildShareSynchronizer.IsSecureOwnerTeamAsync`), maps only into a row owned by that Secure team. Otherwise, and when the source's owner does not come back or deciding the team faults, the reason is `record_repair.mapping_source_secure`. A source read that faults maps nothing, with the existing `record_repair.mapping_source_unreadable` | `ASecureFlaggedMatterNotYetIsolated_...`, `AMatterOwnedByTheSecureTeam_FlaggedFalse_...`, `ASourceReadThatFaults_CopiesNothing_FailClosed`, `ASourceWhoseOwnerDidNotComeBack_CopiesNothing_FailClosed`; must-not-fire `AnOrdinaryMatterFlaggedFalse_OwnedByItsUnitsTeam_StillMaps`, `AnEventAlreadyOwnedByTheSecureTeam_IsStillMapped_...` |
| F1-f + UAC-r2 1 | A Decision Record is append-only: `RepairAsync` returns `Skipped` / `record_repair.table_append_only` before any read, with no update call. This is Repair's own set, NOT the registry's `OwnershipException`: Create's `DecideOwnerAsync` reads that flag and refuses an unfiled create with it, so a registry entry would change Create | `ADecisionRecord_IsNeverRepaired_NoReadNoWrite` (P3; and a filed one with a missing stamp and a profile) |
| UAC-r2 document columns | On `sprk_document`, a rule targeting `sprk_accesspermission` or `sprk_accessinheritance` is Skipped (`InheritedAccessPermission.RefuseAppOnlyWrite`, the push's call; reason `sdap.access.document_access_server_only`). A rule targeting `sprk_parentdocument` or `sprk_relatedcommunication` is Skipped (`record_repair.document_link_server_owned`). `RefuseAppOnlyWrite` covers only the two access columns, so the D7 links are Repair's own set; extending it would change its other callers | `ADocumentsAccessRecordAndD7Links_AreNeverMapped`; must-not-fire `TheSameColumnsOnAnotherTable_AreNotRefusedAsADocuments(event / todo)`: the access columns are written there. The links are not written on those tables either, but because they would re-file the row (`mapped_value_would_refile`), not because of the document gate |
| F3 | Tests for the six pass-1 survivors | see the mutant table |
| K2 | Leaf-table sentence corrected (above) | — |

**Repair's write path:** `IGenericEntityService.UpdateIfRowVersionMatchesAsync`, not `UpdateRecordFieldsAsync`,
`UpdateExistingRecordFieldsAsync` or `UpdateRecordFieldsIfUnchangedAsync`. Whether the pending
`EveryGenericUpdateWriter_AsksTheAppOnlyRefusal` ArchTest (#2170) covers this method is for that task. Repair asks the
refusal per rule in its mapping step, and no exemption was added.

**Not added (owner decision pending):** the field-security skip, and a Roslyn-guard allow-list entry.

**Owner outcome of P1 (observation, not changed):** a pair-only event is still re-owned to its person's team, because
its pair files nothing on its own on an event (the same answer Create gives). Only the mapping was the leak, and it is
closed.

**Residual (closed in pass 3):** in pass 2, a missing or ambiguous Secure Record owner team made `IsSecureOwnerTeamAsync`
answer "no", so gate (ii) failed open. See "Pass 3" below.

### Reachability (UAC-r2 condition 5)

Nothing in production reaches `RecordCreationService.RepairAsync` today:
- it is `internal`, on an `internal` class;
- no endpoint, job or service calls it (the other `RepairAsync` names are private methods of
  `CoreAncestorStampReconciliationJob` and `TodoAssigneeOwnerReconciliationJob`);
- the class is the existing scoped registration, and no registration or endpoint was added.

Pinned by `tests/Spaarke.ArchTests/RecordRepairReachabilityTests.cs`: no `x.RepairAsync(` call under `src/server` outside
an empty allow list, with must-fire and must-not-fire controls. Task 052 must list itself there.

### Fail-before evidence, pass 2 (mutants; each applied alone, built, run, restored)

| Mutant | Killed by |
|---|---|
| MA team-id check → `true` (`RepairOwnerAsync`) | `AnEventOwnedByAnOrdinaryTeam_FiledUnderASecureRecord_GetsNoMapping` |
| MB not-owned and filed → `null` | `AFiledRowTheResolverCannotOwn_GetsNoMapping` |
| MC `IsNullOrWhiteSpace` → `IsNullOrEmpty` | `AConcatThatYieldsOnlyASpace_IsNeverWritten_TwoRepairsWriteNothing` |
| MD kept row plus refusal → `null` | **Survives: equivalent since F1-e (i).** `ResolveOwnerOverAsync` refuses only a project's pair it cannot tell. That pair is then not among the owner's parents, and a project has no typed filing lookup, so (i) (or `MappingSourceResolver` itself) maps nothing either way |
| ME Root narrowed to `sprk_matter` | `EverySecureFlaggedRootTable_KeepsAUserOwner(project / work assignment)` |
| MF `&& !filed` removed | `AnEventOwnedByAnOrdinaryTeam_FiledUnderASecureRecord_GetsNoMapping` |
| N1 F1-e (i) source-in-parents check removed | P1, P2, `APairNamingAnotherOrdinaryRecord_...` |
| N2 F1-e (ii) source gate removed | `ASourceWhoseOwnerDidNotComeBack_CopiesNothing_FailClosed` |
| N3 F1-e (ii) Secure-team owner ignored | `ASourceOwnedByTheSecureTeam_ThatIsNoRoot_IsNotCopiedIntoARowItDoesNotIsolate`. On consistent data the owner gate catches this case first: the resolver reads a parent held in the Secure Record unit as secure. So this test seeds the source's unit as ordinary, to pin (ii) independently of the resolver |
| N4 append-only skip removed | `ADecisionRecord_IsNeverRepaired_NoReadNoWrite` (both cases) |
| N5 document server-owned gate removed | `ADocumentsAccessRecordAndD7Links_AreNeverMapped` |

### Runs, pass 2

- `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- ArchTests (built and run from its own project; it is not in `Spaarke.sln`): 1237 passed, 0 failed, including the 5 new
  reachability cases.
- `RecordRepairModeTests`: 49 passed.
- The same filtered `Sprk.Bff.Api.Tests` set: 2683 passed, 0 failed, 7 skipped (the same pre-existing skips).
- Create is unchanged: the diff to `RecordCreationService.cs` lies inside `RepairOwnerAsync` only.

## Pass 3: the residual, and the merge of the work branch

**Residual closed.** When a team owns the mapping source (or the source is a flagged root and a team owns the row),
Repair's gate now asks for the Secure Record owner team itself (`SecureChildShareSynchronizer.SecureOwnerTeamAsync`, a
new internal wrapper over the existing `ResolveSecureOwnerTeamAsync`). If that team is missing or ambiguous, the
source's owner cannot be told, so nothing is mapped (`record_repair.mapping_source_secure`). `IsSecureOwnerTeamAsync`
and its other callers, and Create, are unchanged. A user-owned, unflagged source needs no team lookup and still maps.

- Consequence: in an environment with no Secure Record business unit, Repair maps nothing from a team-owned source.
- §11: (1) Existing: `IsSecureOwnerTeamAsync` collapses "missing" into "no". (2) Extension: an instance wrapper of the
  existing static resolution, so the gate needs no new dependency. (3) Cost of doing nothing: gate (ii) fails open.
- Test: `ATeamOwnedSource_WhenTheSecureTeamIsMissing_CopiesNothing_FailClosed` (must-fire). Must-not-fire:
  `AnOrdinaryMatterFlaggedFalse_OwnedByItsUnitsTeam_StillMaps` (Secure team present, ordinary team owner).
- Mutant (the missing-team branch returns `!flagged`, i.e. "not the Secure team"): killed by the must-fire test.

**Merges** of `origin/work/field-mapping-server-write-path-r1`, at `4b3efc3ac1` and again at `3583ac04cb` (master
`446c83d927`, UAC #2170): both clean, no conflicts. Against the work branch, `RecordCreationService.Mapping.cs` and
`RecordTableRules.cs` are identical. `RecordCreationService.cs` differs only by the reviewed step-7/8 helper extraction
and the Repair members.

**#2170 alignment:** `RefuseAppOnlyWrite` now also refuses a document's D7 links. Repair's own link set and
`record_repair.document_link_server_owned` were dropped. All four columns are skipped per rule with the refusal's code
(`sdap.access.document_access_server_only` or `sdap.access.document_link_server_only`); the tests are kept.

**`EveryGenericUpdateWriter_AsksTheAppOnlyRefusal` (#2170): FAILS. Stopped; not decided here.** The guard works per
type (IL). `RecordCreationService` is listed in `GenericWritersThatUpdateNoDocument` for Create's step-11
`UpdateRecordFieldsAsync` ("Creates only"). Repair's per-rule `RefuseAppOnlyWrite` call is in the same type, so the
ratchet reports the entry as STALE ("it now asks the refusal — remove it"). Options:
- (a) Remove the entry, as the ratchet says. The type then passes because of Repair's call, not Create's.
- (b) Move Repair's refusal call to another type, so the entry stays accurate. That adds a type to satisfy a scanner.

This is a decision on UAC's guard semantics, so it is left for the coordinator. Repair writes through
`UpdateIfRowVersionMatchesAsync`, which the guard does not name.

**`MappingSourceCallerMaskGuard` (#2188): fails on Repair only, as expected.** Repair has no caller to mask against,
and the field-security approach is the owner's decision. No allow-list entry and no field-security skip were added:

```
A source entity reaches CreateTimeFieldMapping.Apply/ApplyFillOnly without MappingSourceCallerMask (#2188). ...
src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Repair.cs:307: the source is the expression
'read.Record', not a masked local
Failed!  - Failed: 1, Passed: 71, Total: 72
```

**Runs, pass 3** (after the merge at `3583ac04cb`):
- `dotnet build Spaarke.sln`: 0 errors, 0 warnings. `dotnet build tests/Spaarke.ArchTests`: 0 errors, 0 warnings.
- ArchTests: 1340 passed, 2 failed. One is #2188 on Repair's `ApplyFillOnly`, as expected. The other is #2170's stale
  entry above (stopped).
- `RecordRepairModeTests`: 50 passed.
- The filtered `Sprk.Bff.Api.Tests` set, now with MappingSourceCallerMask: 2815 passed, 0 failed, 7 skipped.

rework: review pass 1 (F1-a, F1-b, F1-c, F3, K2) fixed; F1-d stopped. Review pass 2 (F1-e, F1-f, the UAC-r2
conditions, F3, K2) fixed. Pass 3: residual closed, work branch merged (twice), #2170 alignment done; #2170's
stale-entry ratchet stopped for the coordinator. Open: escalation trigger 2, and the
field-security approach for Repair (the #2188 guard fails on Repair by design until the owner decides).

## Pass 4: the re-check's test gaps, and #2170 option (c)

**Part A (commit `aa8e7a668f`).** New tests:
- `ADecisionRecord_IsNeverRepaired_NoReadNoWrite` now asserts that nothing is read (`RecordQueries` is empty).
- `DecidingTheSecureTeamFaults_CopiesNothing_FailClosed`: the fail-closed catch in `SourceMayBeCopiedAsync`.
- With an ordinary owner, `ResolverAnswer = Owned(ChildTeam)`, two tests pin the gate alone:
  - `ASourceWhoseFlagIsEmpty_IsNotCopiedIntoAnOrdinaryTeamsRow_TheGateAlone`;
  - `ASecureTeamOwnedSource_IsNotCopiedIntoAnOrdinaryTeamsRow_TheGateAlone`.

K4: the warning for an unreadable source now reads "The related record could not be read, so no fields were copied
from it." (`RepairSourceUnreadableWarning`). A test asserts the text and that no warning says "you". Each mutant was
applied alone, built, run and restored:

| Mutant | Killed by |
|---|---|
| A1 the append-only skip runs after a row read | `ADecisionRecord_IsNeverRepaired_NoReadNoWrite` (both cases) |
| A2 the gate's fault catch returns true | `DecidingTheSecureTeamFaults_CopiesNothing_FailClosed` |
| M1 `!= false` becomes `== true` | `ASourceWhoseFlagIsEmpty_..._TheGateAlone` |
| M3 the early return ignores the flag | `ASourceWhoseFlagIsEmpty_..._TheGateAlone` |
| M4 a flagged source counts as isolated for any team-owned row | `ASourceWhoseFlagIsEmpty_..._TheGateAlone` |
| M14 `isolated = rowTeamOwned` | `ASecureTeamOwnedSource_..._TheGateAlone`, `ASourceWhoseFlagIsEmpty_..._TheGateAlone` |

**Part B: `EveryGenericUpdateWriter_AsksTheAppOnlyRefusal`, per member (UAC-r2 option c).**
- The guard now keys by member (`SourceKey`): lambdas, local functions, state machines and `<>c` closures count for the
  member they were written in.
- `IGenericEntityService.UpdateIfRowVersionMatchesAsync` is scanned, matched on `IGenericEntityService`. `UpdateAsync`
  is not scanned (#2213).
- Exemptions are `Type::Member` keys with reasons.
- `RepairAsync` now calls `RefuseAppOnlyWrite(table, patch.Keys)` itself, just before its write. If refused, nothing is
  written and the outcome is Skipped with the refusal code. That cannot happen today, because the mapping already
  refuses per rule.
- Controls:
  - must-fire: `GenericWriterControlTwoMembers::WritesWithoutRefusingAsync`, and a stale member entry
    (`GenericWriterControlListed::ReadAsync`);
  - must-not-fire: the refusing member, the lambda refusal, and the non-vacuity writers. Those are
    `UpdateRecordActionCore::UpdateAsync`, `FieldMappingEndpoints::ApplyMappingsToChildRecordsAsync`,
    `RecordCreationService::RepairAsync`, and `DataverseUpdateHandler` (`UpdateAsync` refuses, `WriteAsync` writes).

Payload evidence for each member entry:
- InvoiceReviewService:
  - `CreateInvoiceRecordAsync`: a fresh-id create of `sprk_invoice`;
  - `LinkDocumentOrCompensateAsync`: `sprk_document`, only `sprk_Invoice@odata.bind`;
  - `MarkDocumentConfirmedAsync` and `RejectInvoiceAsync`: `sprk_document`, `BuildReviewStatusFields` (review status,
    reviewed-on, notes).
  - The old type reason ("sprk_invoice only") was inaccurate. These members do update documents, but only with literal
    keys outside the refusal's set.
- SignalEvaluationService `UpsertSignalAsync`: `sprk_spendsignal`.
- FinanceRollupService `RecalculateForEntityAsync` and ScorecardCalculatorService `UpdateEntityGradesAsync`: rollup and
  grade literals on a matter or project.
- CommunicationEnrichmentService `AssignOwningTeamAsync`: `ownerid@odata.bind` only.
- OwnedChildWrite:
  - `CreateAsync` and `CreateIntoIsolationAsync`: fresh-Guid creates;
  - `PatchWithBffManagedPairAsync`: only the `PairColumns` (regarding record id and type), picked out of the body by
    name.
- DataverseUpdateHandler `WriteAsync` and `UpdateWithOptimisticConcurrencyAsync`: private. They are reached only from
  `UpdateAsync` (lines 104 and 115, then 156), which refuses the same `fields` at :59. Nothing changes the payload in
  between.
- RecordOwnershipResolver:
  - `SettleOwnerAsync` (:1916) and `ReleaseOwnSecureWriteAsync` (:2121): only `ownerid` (a team reference).
  - **`PutBackFilingAsync` (:2811): STOPPED, not exempted.** It writes `restore`, which `RecordOwnershipResolver` builds
    with `Remember(column)` (:1422-1480). It holds every parent column the re-file moved, plus `AttachColumns`, and
    `sprk_document` is an ownership parent. On a `sprk_document` re-file, `restore` can therefore hold
    `sprk_parentdocument` (`ParentChangesOf` sets it, :833) and `sprk_relatedcommunication`, and writes back their prior
    values. The guard reports this member, and it is left for the coordinator and UAC.
- Signal writers: `DecisionCommitService` :2027, `SignalSuppressionService` :1305 and `SignalWriter`
  :907/:985/:1038/:1079.
  - They call `OntologyWriterDataverseClient.UpdateIfRowVersionMatchesAsync`, a concrete class that does not implement
    `IGenericEntityService`. The scan, matched on `IGenericEntityService` as briefed, does not see them, so they have no
    entry; an entry would be reported stale.
  - Their payloads are all `sprk_signal` (the first argument is the literal `"sprk_signal"` or `SignalEntity`), so
    seeing them would add exemptions, not findings.

Guard mutants (each applied alone, built, run, restored):
- B1 (no attribution of compiler-generated code; the key is `MoveNext`): both facts fail.
- B1b (the per-type key): both facts fail.
- B2 (the row-version update is not scanned): both facts fail.
- B3 (`RepairAsync` does not refuse): the main fact fails ("RepairAsync should be seen calling RefuseAppOnlyWrite").
  This is also the fail-before for the Repair change.

**Runs, pass 4:**
- `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- ArchTests (built separately): 1340 passed, 2 failed.
  - #2188 on Repair's `ApplyFillOnly` (now `Repair.cs:323`), as expected.
  - #2170 on `RecordOwnershipResolver::PutBackFilingAsync`, the STOP above.
- `RecordRepairModeTests`: 53 passed.
- The filtered set: 2818 passed, 0 failed, 7 skipped.

Part B is committed locally and NOT pushed, pending the coordinator's call on `PutBackFilingAsync`.

**Pass 4 resolution (UAC-r2's answers to the three open items):**
1. `RecordOwnershipResolver::PutBackFilingAsync` is now a member exemption, with UAC's reason verbatim: it is a
   row-version-conditional rollback of the re-file's own failed write, back to the row's prior values.
2. Signal writers: the scan is not widened and they get no entries. Their payloads are fixed `sprk_signal`, written
   through `OntologyWriterDataverseClient`.
3. `DataverseUpdateHandler` `WriteAsync` and `UpdateWithOptimisticConcurrencyAsync` are accepted, with the reason
   "private; sole caller UpdateAsync, which refuses first".

ArchTests after this: 1341 passed, 1 failed. The failure is #2188 at `Repair.cs:323`, as expected. Part B is pushed.

## Owner decision 2026-10-21: field security, option 1

Repair runs as the system principal and has no caller to mask a source against, so it skips field-secured source
columns itself. It uses task 033's app-only metadata reader for this. UAC-r2 concurs, on five conditions:
1. **Fail closed on metadata.** A metadata fault, or an unknown `IsSecured`, maps nothing for the row, with a distinct
   reason (`record_repair.mapping_source_metadata_unavailable`). Metadata is read per pass, or cached briefly.
2. **The document access and D7 link columns are skipped.** Done: `RefuseAppOnlyWrite` per rule, and again on the write
   member.
3. **A per-occurrence allow-list entry** in the #2188 guard for Repair's single `ApplyFillOnly` call.
4. **Tests.**
   - Must-fire: a secured column is not copied; a metadata fault maps nothing; the document columns are skipped.
   - Must-not-fire: unsecured columns are copied, and the same columns on a to-do or an event are copied.
5. **052 stays blocked until 033 lands with the skip.** `RecordRepairReachabilityTests` enforces this in the meantime.

**Seam for 033 (done).** `SourceColumnsRepairMayCopy(sourceTable, mappedSourceColumns)` in `Repair.cs` is where the
`IsSecured` skip goes. It is marked `TODO(task 033)` with the fail-closed rule. Today it returns the columns unchanged,
so there is no behaviour change.

**Condition 3: STOPPED.** The #2188 guard has no per-occurrence allow-list. Its only exemption mechanism is
`MappingSourceCallerMaskGuardTests.Allowlist`, keyed by FILE path, and an entry skips the whole file (:83). The "allow-list
per occurrence" in its remarks (:27, :921) describes how a masked local's uses are judged, not an exemption list.
- A file entry for `Repair.cs` would let a second `Apply`/`ApplyFillOnly` call there pass, which the decision rules out.
- A per-occurrence key would be a new mechanism, which the brief rules out.
- Left for the coordinator. `KnownCallSites` is still 2. The scan already sees 3 call sites (Office, step 3, Repair),
  because `Repair.cs` is scanned today, so it should go to 3 together with the entry.

**Condition 3: done (the coordinator authorized a per-occurrence exemption).** The file `Allowlist` is unchanged. A new
`UnmaskedCallExemptions` (`(Uses, Reason)`, the `CreateOwnerResolutionGuardTests` shape) is keyed
`path|Type.Member`. The entry is
`src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.Repair.cs|RecordCreationService.RepairMappingAsync`,
with Uses = 1 and the agreed reason verbatim.

How the guard now works:
- Calls are attributed to the declared member around them (`ScopeOf`/`MemberKey`). A call in a local function, lambda
  or anonymous method counts for that member.
- Each call is judged on its own; the old per-name de-duplication is gone.
- Past `Uses`, every further unmasked call fails. Unmasked calls in other members always fail.
- `EveryUnmaskedCallExemption_IsStillUsed` reports an entry as stale when the reason is empty, the key is malformed, the
  file or member is gone, or the member has fewer unmasked calls than `Uses`.
- `KnownCallSites` is 3.

Controls, run through the same walker:
- must-fire: a second unmasked call in the exempt member; a call in another member; a lambda call in another member;
- must-not-fire: exactly one unmasked call in the exempt member, directly or in a local function;
- stale: a member with zero unmasked calls, a gone member, a gone file, an empty reason.

Mutants (each applied alone, built, run, restored):
- E1 (no allowance): the real fact and both must-not-fire controls fail.
- E2 (unlimited allowance): the second-call control fails.
- E3 (keyed by file, not member): the other-member and lambda controls fail.
- E4 (local functions not attributed to their member): the local-function control fails.
- E5 (stale ignores the count): the stale control fails.
- E6 (stale ignores a gone member) did not compile as written. It is equivalent anyway: a gone member has zero calls, so
  the count check reports it too.

Before the entry, the real fact failed on `Repair.cs:323`; with it, the fact passes.

## UAC-r2 condition (b) review: fix round

The Create path is unchanged: `git diff` shows no change to `RecordCreationService.cs` or `RecordCreationService.Mapping.cs`
in this round. The F3-2 mutant edits `RecordCreationService.cs` temporarily and then restores it.

**F1-1: columns Repair never maps, on any table** (`ColumnsRepairNeverMaps`, Repair.cs).
- They are added on top of Create's protected set, which is left unchanged. The engine is handed the union, and each
  column is reported with its own reason.
- `sprk_issecure`, `sprk_accesspermission`, `sprk_accessinheritance`, `sprk_securitybu` and `sprk_containerid` get
  **`record_repair.mapped_value_confers_access`**.
- The regarding pair (`sprk_regardingrecordid`, `sprk_regardingrecordtype`) gets
  **`record_repair.mapped_value_would_refile_pair`**.
- The table's Assigned / access-conferring columns get **`record_repair.mapped_value_assigns_access`**. They come from
  BOTH `MembershipOptionsDefaults.CanonicalAccessConferringRegistry` and the bound `MembershipOptions.AccessConferringRoles`.
  The bound one is read through the existing `IServiceScopeFactory`, so there is no new dependency; it is the same
  registry `AssignedAccessMaterializer.RegistryFor` reads. A fault reading it fails the mapping step, which then maps
  nothing.
- A column the row already holds is reported Present.
- A document's access columns keep their `sdap.access.*` codes, because the document refusal is checked first.
- Tests:
  - must-fire: `AMattersBlankSecureFlag_IsNeverFilledFromItsProject_ScenarioA` and
    `AMattersEmptyAssignedLawFirm_IsNeverFilledFromItsProject_ScenarioB`;
  - must-not-fire: `AnOrdinaryColumn_IsStillFilledFromTheProject_F1_1MustNotFire`.
- `TheSameColumnsOnAnotherTable_AreNotRefusedAsADocuments` (event, to-do) now expects the access columns skipped with
  `mapped_value_confers_access`. Its must-not-fire on the document codes is kept.
- Task 189's text guard (`DocumentAccessPermissionWriterGuardTests`) flagged the first form of the map, a dictionary
  initializer `[InheritedAccessPermission.Column] = reason`, as a write of `sprk_accesspermission`. It is not a write:
  the map lists columns that are NOT written. It is now built from column lists, a shape the guard's controls treat as
  not a write. The guard and its lists are unchanged.

**F2-1: step 8 only once the owner is settled on a team.** The row must be team-owned already, or re-owned in this
patch. Otherwise each missing column is skipped with **`record_repair.search_index_owner_unsettled`**.
- Tests:
  - the BU skip: `AnOwnerKeptInItsUnitByTheBusinessUnitSkip_GetsNoSearchIndex`;
  - the owner refused: `AnUnresolvableOwner_IsLeftUnchanged_WithATypedRefusal_AndNoSearchIndexIsFilled`;
  - a kept secure root: `AUserOwnedSecureFlaggedRoot_KeepsItsOwner_AndGetsNoSearchIndexUntilItsOwnerIsSettled`;
  - must-not-fire: `AUserOwnedRowReOwnedInThisPatch_GetsTheSearchIndexOfItsNewOwnersUnit` and the existing team-owned
    `TheSearchIndexDefault_IsFilledOnlyWhereUnset`.

**F3-1: reachability by IL.** `RecordRepairReachabilityTests` now references `IlCallScan.MethodReferences` over the BFF.
- Any reference to `RecordCreationService.RepairAsync` (a call, a method group or a token) is keyed `Type::Member`, with
  lambdas and state machines counted for their member. It must be listed in `AllowedReferences`, which is empty.
- Unlisted references fail, and so do stale entries.
- The control, `TheScan_FindsAWrapperAMethodGroupAndALambda_NotAnotherRepairAsync`:
  - must-fire: a wrapper, a method group and a lambda;
  - a caller of the wrapper is not reported itself, because the wrapper already is;
  - must-not-fire: another method named `RepairAsync`.

**F3-2: "status unknown".** `AnUnfiledRowWhoseOwnersStatusCannotBeTold_IsNotReOwned_NoOwnerSource` covers two cases:
the systemuser read faults, or no row comes back. Both expect `record_owner_no_source` and no `ownerid`.

**F4-1:** the `UnmaskedCallExemptions` reason now puts the 033 skip in the future ("will be skipped … (TODO task 033) …
no production caller until then (RecordRepairReachabilityTests; 052 hard-blocked on 033)"). The rest is unchanged.

**K1 (asserted by IL, cheaply):** `DataverseUpdateHandlersPrivateWriters_AreReachedOnlyFromUpdateAsync`.
`WriteAsync` is referenced only by `UpdateAsync`, and `UpdateWithOptimisticConcurrencyAsync` only by `WriteAsync`.

Mutants (each applied alone, built, run, restored):

| Mutant | Killed by |
|---|---|
| F1a no never-mapped set | Scenario A, Scenario B, the must-not-fire, the event/to-do pair |
| F1b registry columns not added | Scenario B |
| F1c the secure flag not in the set | Scenario A, the must-not-fire |
| F2a step 8 whatever the owner | the BU skip, the owner refused, the kept secure root (×2) |
| F2b re-owned in this patch not counted | the re-owned must-not-fire |
| F3-2 `== false` → `!= true` (RecordCreationService.cs:798) | both F3-2 cases |
| F3-1a the scan matches nothing | the reachability control |
| F3-1b keyed by type, not member | the reachability control |

## Condition (b) statement

**Owner and search index, per table, before and after Repair:**

| Table (kind) | Owner before → after | Search index (matter, project, document only) |
|---|---|---|
| any table, team-owned | kept (`owner_team_owned`) | filled from the row's unit where missing |
| child, user-owned, filed | re-owned to the team its parents resolve to (same unit; another unit only on a leaf table) | filled from the new team's unit |
| child, user-owned, filed, team in another unit (ownership-parent table) | kept (`owner_moves_business_unit`) | not filled (`search_index_owner_unsettled`) |
| child, user-owned, filed, owner refused or faulted | kept, typed refusal | not filled (`search_index_owner_unsettled`) |
| any table, user-owned, unfiled | the person's team (I-6) | filled from that team's unit |
| any table, application-owned or status unknown, unfiled | kept, `record_owner_no_source` | not filled (`search_index_owner_unsettled`) |
| matter / project / work assignment, user-owned, flag not explicit false | kept (`owner_root_secure`) | not filled (`search_index_owner_unsettled`) |
| matter / project / work assignment, user-owned, flag false | re-owned to the unit's team | filled from that team's unit |
| communication, unfiled | kept (`owner_kept_by_exception`) | no search index |
| decision record | never read or written (`table_append_only`) | no search index |

**Order:**
1. One read of the row.
2. Owner (step 7).
3. Mapping (step 3), gated by the owner decision.
4. Search index (step 8), gated by the owner settling on a team.
5. The missing I-1 stamp (step 10).
6. The pre-write checks: the version, the invoice-family guard, and `RefuseAppOnlyWrite` over the patch.
7. One write, conditional on the row version.
8. After the write, only the membership-cache owner eviction (`EvictOwnerChangeAsync`), when `ownerid` was in the patch.
   It also runs on an ambiguous write fault or a cancellation.

**Create's post-create steps that Repair does NOT run:**
- `SyncChildAsync` (the secure-child share sync);
- `RefreshInheritedAccessPermissionAsync`;
- the membership owner event (`IMembershipEventPublisher`);
- `AssignedAccessMaterializer`.

**Known limits:**
- K1: the two `DataverseUpdateHandler` member exemptions are reason-only entries. Their sole-referencer claim is now also
  asserted by IL (above).
- K2: Repair runs no post-create steps. Access converges through `SecureChildReconciliationJob` and the nightly
  membership reconciliation. Until then it is narrower, never wider: Repair writes no access-conferring column (F1-1).
