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

`tests/integration/data-mutation/RecordOwnership/RecordRepairModeTests.cs` has 27 tests (29 cases with the two theories), `[Trait("status","new")]`; 17 from the first round, 10 from the review-pass-1 fix round.

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

**Why a leaf table may move business units:** the trigger-2 guard protects rows that inherit access THROUGH the row
being moved. A leaf table (`sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision`: not in
`RecordOwnershipResolver.OwnershipParentEntities`) has no rows filed under it, so its re-own changes only its own access, exactly as
Create would have owned it. Ownership-parent tables keep the fail-closed skip until the owner decides.

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

rework: review pass 1 (F1-a, F1-b, F1-c, F3, K2) fixed; F1-d stopped (above). Escalation trigger 2 still awaits the owner.
