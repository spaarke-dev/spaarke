# Task 117: Core-record delete and Procedures (the O-17 sweep)

Branch `task/ebilling-117`, base `b8e6e686c0` (origin/work/ebilling-r1). Rigor FULL. Written 2026-11-23.

## 1. What was built

After a core record is deleted, the platform clears the record lookups of its record-scoped rule set and of its Procedure assignment rows (RemoveLink, task 080). A scheduled sweep then cleans up both. It is report-only by default.

| Part | Where | What it does |
|---|---|---|
| Rule-set retire | `Services/Finance/RuleSetService.cs:2957` `RetireUnlinkedRecordSetsAsync` (result record `:2939`) | Selects Billing sets with scope Record or Firm record, named as 089 names a record set, no Procedure, every core lookup null, not Retired, and older than the age floor. Pages in id order. Re-reads and re-checks each set before writing. Switches each live Billing policy Off (`sprk_enabled` = No), then sets the set to statecode Inactive (1) and statuscode Retired (2). If any policy write faults, the set stays Active. |
| Unlinked predicate | `Services/Finance/Procedures/ProcedureAssignmentService.cs:221` `RecordRuleSet.IsUnlinked`, `:229` `AddUnlinkedFilter`, `:242` `UnlinkedColumns` | The scope values, 089's record-set name (`RecordRuleSet.Name`, fix round 1) and the core-lookup list (`InvoiceFamilyCoreRecordGuard.FilingCoreTables`), in the class that already holds them. |
| Orphan assignment delete | `ProcedureAssignmentService.cs:2661` `DeleteOrphanAssignmentsAsync` (result record `:2648`) | Selects `sprk_procedureassignment` rows with every core lookup null and older than the age floor. Pages in id order. Re-reads and re-checks each row before the delete. |
| Job | `Services/Finance/Procedures/RecordRuleSetRetireJob.cs` (`ExecuteAsync` `:122`, kill switch `:128`, heartbeat `:189`) | One `IScheduledJob`, one scope per run. Reads `Ebilling:Enabled` first (ADR-018): off means no read and no write. Runs the two parts and writes one heartbeat with all counts, the Apply flag and the skip reason. A page read fault makes the run FAILED; a per-row fault makes it partial. |
| Registration | `Infrastructure/DI/FinanceModule.cs:146` | `AddScheduledJob<RecordRuleSetRetireJob>(CronOf(configuration), enabled: false)`. It is registered disabled, like `CommunicationThreadFilingSweepJob`. There is no BackgroundService. |
| Config | `appsettings.template.json:498` section `RecordRuleSetRetireSweep` | `Apply` (default false), `Cron` (blank = `45 3 * * *`, read at start-up), `PageSize` (100, max 5000), `MaxPages` (10), `MinAgeMinutes` (15, max 525600 = one year). The Communication sweep's keys are not in the template, so this section sits after `Ebilling`. |
| Delete guard list | `tests/Spaarke.ArchTests/WriterOnlyTablesDeleteGuardTests.cs:60` | Adds `ProcedureAssignmentService.DeleteOrphanAssignmentsAsync` to `FixedTableDeletes`, citing ADR-002 and uac-r2 design §14. **uac-r2 must agree this entry.** |
| Census waiver | `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs:136` `RuleSetServicePolicyWaiver` | Names the task 117 exception: the retire writes statecode / statuscode and `sprk_enabled` only on record-scoped rows. `RuleSetServiceRecordSetRetireWritesStatusOnly` enforces it, with controls. |

What the sweep never does:

- read or write anything while `Ebilling:Enabled` is false (fix round 1);
- delete anything except an orphan assignment row;
- send an Assign, or write the owner, `sprk_createdbyperson` or any lookup;
- write `sprk_policyversion`;
- touch a set that has a Procedure, a ClientDefault or Firm set, or a set or row with any core lookup;
- send any request to `sprk_procedure`, `sprk_procedureversion`, `sprk_storedfile`, `sprk_ratecard`, `sprk_ratecardrate`, documents or communications;
- close Signals. D-170's nightly sweep closes the Signals of Off policies.

All reads and writes are app-only, through `IGenericEntityService`.

### Placement and scope justifications

- **Billing domain only (rule sets).** Ontology owns the rule sets of other domains, and 089 creates only Billing record sets (`ProcedureAssignmentService.RecordRuleSetCreateAsync`, `:3018`). The sweep filters on `RuleDomains.AddBillingFilter` and re-checks each row with `RuleDomains.IsBillingRow`.
- **Where the orphan delete lives.** `ProcedureAssignmentService` already writes every assignment row, including the removal delete. `RuleSetService` owns rule sets only, so a delete of assignment rows does not belong there.
- **The predicate is outside `RuleSetService`.** `RecordOwnerAssignmentCensusTests` (089a K4) fails when the waived `RuleSetService.cs` names a record scope (`RecordRuleSet.ScopeRecord`/`ScopeFirmRecord`). So the "unlinked" predicate and filter sit in `RecordRuleSet`, beside `IsRecordScoped` and `RequireOneHome`. `RuleSetService` stays the one rule-set lifecycle writer and gains one method.

### Age floor (uac-r2 addition a)

Rule sets and assignment rows are taken only when `modifiedon` is earlier than now minus `MinAgeMinutes` (default 15). The selection query applies this, and so does the in-memory re-check after the re-read. Supporting evidence: both 089 creators write the core lookup in the create request itself.

- Assignment rows: `AssignmentCreateAsync` builds the row with `[spec.CoreTable] = new EntityReference(...)` (`ProcedureAssignmentService.cs:2962`).
- The record-scoped set: `RecordRuleSetCreateAsync` builds it with `[coreTable] = new EntityReference(coreTable, recordId)` (`:3026`) and checks it with `RequireOneHome` before the create.
- The census lists `RecordRuleSetCreateAsync` as the one record-scoped `sprk_ruleset` create (`RecordOwnerAssignmentCensusTests.cs:314`).
- No other BFF file creates `sprk_procedureassignment` rows. A grep of `src/server` finds only metadata lists (lineage, resolver, role checks).

### What deleting an assignment removes (uac-r2 addition b)

`sprk_excludedpolicy` is a lookup ON `sprk_procedureassignment` to `sprk_policy`. The policy is the parent, so deleting an assignment deletes nothing else. Evidence:

- `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_Policy.xml:34`, relationship `sprk_procedureassignment_excludedpolicy_policy`: referencing entity `sprk_procedureassignment`, referencing attribute `sprk_ExcludedPolicy`, CascadeDelete `Cascade`. Deleting the **policy** deletes its override rows.
- `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1`, the `sprk_excludedpolicy` lookup spec ("a deleted rule deletes its override"; line 593 on this branch, its base and master) and `:70` (the only parental relationship of the table).

A delete of an orphan assignment row removes that row only. There is nothing cascade-deleted to count, so the report-only output has no cascade count.

### Restrict check (uac-r2 addition c, design §15)

Every relationship in the solution XML that involves `sprk_procedureassignment` has it as the **referencing** (child) table. None has it as the referenced table (a grep for `ReferencedEntityName>sprk_procedureassignment` over `src/dataverse` finds no hits). The table is not an activity, has no notes relationship and has connections off (`Entities/sprk_procedureassignment/Entity.xml:1183,1196`).

| Relationship (file:line) | Lookup on the assignment | Delete |
|---|---|---|
| `business_unit_sprk_procedureassignment` (BusinessUnit.xml:1403) | OwningBusinessUnit | Restrict (blocks deleting the BU) |
| `owner_sprk_procedureassignment` (Owner.xml:1403) | OwnerId | NoCascade |
| `lk_sprk_procedureassignment_createdby` / `_modifiedby` (SystemUser.xml:4203, 4223) | CreatedBy / ModifiedBy | NoCascade |
| `sprk_systemuser_sprk_procedureassignment_createdbyperson` (SystemUser.xml:7284) | sprk_CreatedByPerson | RemoveLink |
| `user_sprk_procedureassignment` (SystemUser.xml:9139) | OwningUser | NoCascade |
| `team_sprk_procedureassignment` (Team.xml:1434) | OwningTeam | NoCascade |
| `sprk_matter_procedureassignment_matter` (sprk_Matter.xml:660) | sprk_Matter | RemoveLink |
| `sprk_project_procedureassignment_project` (sprk_Project.xml:563) | sprk_Project | RemoveLink |
| `sprk_workassignment_procedureassignment_workassignment` (sprk_WorkAssignment.xml:695) | sprk_WorkAssignment | RemoveLink |
| `sprk_procedureassignment_firm_organization` (sprk_Organization.xml:834) | sprk_Firm | Restrict (blocks deleting the firm) |
| `sprk_procedureassignment_excludedpolicy_policy` (sprk_Policy.xml:34) | sprk_ExcludedPolicy | Cascade (deleting the policy deletes the row) |
| `sprk_procedureassignment_procedure_procedure` (sprk_procedure.xml:3) | sprk_Procedure | Restrict (blocks deleting the Procedure) |
| `sprk_procedureassignment_pinnedversion_procedureversion` (sprk_procedureversion.xml:38) | sprk_PinnedVersion | Restrict (blocks deleting the version) |

Each Restrict here protects a **parent** from deletion while an assignment points at it. None can refuse deleting the assignment row itself. So no "refused" outcome is reachable, and the sweep does not add one. Any delete fault is counted as `assignmentsFaulted`, logged and retried on the next run. Known limit: a row that faults on every run is retried and logged on every run.

### Rate cards (uac-r2 addition d)

Rate-card row stands (uac-r2 design §12: sprk_ratecard unlink-only, never Delete; sprk_ratecardrate untouched; both in the planner's never-Delete set). Since 085 the unlink has no rate effect.

Evidence:

- **The columns remain on dev, as RemoveLink.** The solution XML has `sprk_matter_ratecard_matter` (sprk_Matter.xml:695, CascadeDelete RemoveLink, :703), `sprk_project_ratecard_project` (sprk_Project.xml:598, RemoveLink :606) and `sprk_workassignment_ratecard_workassignment` (sprk_WorkAssignment.xml:730, RemoveLink :738). DEF-027 records them. The coordinator's live dev read (2026-11-23) agrees.
- **No code reads or writes them.** `notes/085-ratecard-procedure-version.md:7` says no code reads or writes the card's core-record lookups. A grep of `RateCardImportService.cs`, `RateCardWorkbook.cs`, `Resolution/RateCardResolver.cs` and `Api/Finance/RateCardImportEndpoints.cs` for `sprk_matter|sprk_project|sprk_workassignment|FilingCoreTables` finds no hits.
- **Resolution keys on `sprk_procedureversion`.** See `RateCardResolver.cs:15` (`sprk_ratecard.sprk_procedureversion`) and `:129`.

The sweep sends no request to `sprk_ratecard` or `sprk_ratecardrate`. The forbidden-table assertion in `RecordRuleSetRetireJobTests.AssertOnlyTheSweepsRequests` includes both.

### The five hand-off points to uac-r2

1. Hard-deny adds `sprk_procedure`, `sprk_procedureversion` and `sprk_storedfile`.
2. Delete: `sprk_procedureassignment`, through the planner. The O-17 sweep removes orphans (`DeleteOrphanAssignmentsAsync`, listed in `WriterOnlyTablesDeleteGuardTests`).
3. Unlink-only: the record-scoped `sprk_ruleset` and its `sprk_policy` and `sprk_policyversion`. The delete leaves them untouched, and this sweep retires them, replacing §12's "kept" wording.
4. Rate cards: the wording above (no "moot").
5. A core record's invoices still Block the delete.

## 2. Tests

`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Procedures/RecordRuleSetRetireJobTests.cs` runs the production job over the production services. The fake store is `RuleSetServiceTests.Harness`, extended for this task:

- it also evaluates NotEqual, GreaterThan (the id cursor) and LessThan on `modifiedon`;
- `SetModifiedOn` sets a row's `modifiedon`;
- `FailUpdateOf` faults a single row's update.

| Criterion | Test |
|---|---|
| Must fire: Record and Firm-record sets Retired, every policy Off, owner, creator person and versions unchanged | `Apply_RetiresUnlinkedRecordAndFirmRecordSets_AndSwitchesTheirPoliciesOff` |
| Must fire: orphan assignment deleted | `Apply_DeletesAnOrphanAssignmentRow`; `ProcedureAssignmentServiceTests.DeleteOrphans_DeletesTheOrphan_KeepsEveryRowWithALiveLookup` |
| Must not fire: set with one lookup (each core table) | `ASetWithALookup_IsUntouched` (theory over `FilingCoreTables`, Record and Firm-record sets) |
| Must not fire: Procedure set, ClientDefault, Firm, Retired, non-Billing (Ontology and empty domain) | `OtherSets_AreUntouched` |
| Must not fire: assignment with a live lookup (each core table) | `AnAssignmentRowWithALiveLookup_IsKept` (theory) |
| Must not fire: a Record or Firm-record set with no lookup that 089 did not create (another name) | `ASetNotOf089sShape_IsUntouched` (theory: a legacy name, a case variant, empty) |
| Must not fire: eBilling off with Apply on, zero requests | `EbillingDisabled_WithApplyOn_SendsNoRequest_AndHeartbeatsTheSkip` |
| Apply off: no write, counts logged | `ApplyOff_WritesNothing_AndReportsTheCounts`; `AnythingButTrue_IsReportOnly` (theory); `RuleSetServiceTests.RetireUnlinked_ReportOnly_CountsAndWritesNothing`; `ProcedureAssignmentServiceTests.DeleteOrphans_ReportOnly_DeletesNothing` |
| Recorded requests: only reads, single-row updates (`sprk_enabled` / state+status) and orphan deletes; no Assign, lookup, owner or version write; no forbidden table; no Signal close, Decision Record, owner question or caller probe | `AssertOnlyTheSweepsRequests`, called in the must-fire, report-only and fault tests |
| Re-read before write | `RuleSetServiceTests.RetireUnlinked_ASetReLinkedBeforeItsWrite_IsSkipped`; `ProcedureAssignmentServiceTests.DeleteOrphans_ARowReLinkedBeforeItsDelete_IsKept` |
| Age floor | `TheAgeFloor_LeavesRecentRows_AndTakesOlderOnes` (14 min kept, 16 min taken); `TheAgeFloor_IsConfigurable`; `AHugeAgeFloor_IsClampedToOneYear_AndTheRunCompletes` |
| Policy fault leaves the set Active; the run goes on; the next run retires it; a third run writes nothing | `APolicyFault_LeavesTheSetActive_TheRunContinues_AndTheNextRunRetiresIt` |
| Second run writes nothing | `ASecondRun_WritesNothing` |
| Delete fault on one row continues | `ADeleteFault_OnOneRow_ContinuesTheRun` |
| Read fault means a failed run | `AReadFault_IsAFailedRun` (rule sets); `AnAssignmentReadFault_IsAFailedRun` |
| Paging and cap | `ThePageCap_IsHonoured_AndTheNextRunTakesTheRest` |
| The id cursor moves the pages on | `ReportOnly_PagesOnTheIdCursor_CountingEachRowOnce`; `AFirstRowThatAlwaysFaults_IsAttemptedOnce_AndTheRestAreProcessed` |
| Registered through AddScheduledJob, disabled, configurable cron | `TheJob_IsRegisteredThroughAddScheduledJob_Disabled` |
| The retire writes status and `sprk_enabled` only on record-scoped rows (waiver exception) | ArchTest `RuleSetServiceRecordSetRetireWritesStatusOnly`, controls `RecordSetRetire_Control_Fires`, `RecordSetRetire_Control_StatusOnlyPasses_VacuousFires` |

### Seeding proofs

Redone after fix round 1 (the filter now selects 34 tests).

- **(a) Drop "every lookup null".** Removed the `FilingCoreTables.All(... is null)` clause from `RecordRuleSet.IsUnlinked` and the per-core `Null` conditions from `AddUnlinkedFilter`. With the filter `RecordRuleSetRetireJobTests|DisplayName~117:`, 5 of 34 failed:
  - `ASetWithALookup_IsUntouched` (sprk_matter, sprk_project, sprk_workassignment);
  - `RuleSetServiceTests.RetireUnlinked_ASetReLinkedBeforeItsWrite_IsSkipped`;
  - `ApplyOff_WritesNothing_AndReportsTheCounts`.
  After restoring, 34 of 34 passed.
- **(b) Drop the assignment "all lookups null" condition.** Removed it from `DeleteOrphanAssignmentsAsync`'s `Matches` and `Selection`. With the same filter, 6 of 34 failed:
  - `AnAssignmentRowWithALiveLookup_IsKept` (×3);
  - `ProcedureAssignmentServiceTests.DeleteOrphans_DeletesTheOrphan_KeepsEveryRowWithALiveLookup`;
  - `DeleteOrphans_ARowReLinkedBeforeItsDelete_IsKept`;
  - `ApplyOff_WritesNothing_AndReportsTheCounts`.
  After restoring, 34 of 34 passed.

### Filters and counts

- **Build.** `dotnet build src/server/api/Sprk.Bff.Api/` gives 0 errors and 0 warnings.
- **Unit, brief filter:** `FullyQualifiedName~RecordRuleSetRetireJobTests|FullyQualifiedName~Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests|FullyQualifiedName~ProcedureAssignmentServiceTests|FullyQualifiedName~FinanceModule`: **438 passed, 0 failed**.
- **Unit, wider filter** (every user of the extended harness): `FullyQualifiedName~RecordRuleSetRetireJobTests|FullyQualifiedName~RuleSetService|FullyQualifiedName~Procedure|FullyQualifiedName~FinanceModule|FullyQualifiedName~CommunicationThreadFilingSweepJobTests`: **2628 passed, 0 failed**.
- **ArchTests filter 1:** `WriterOnlyTables|WorkloadPlacement|RouteAuthorizationGuard|SecureChild|Finance|ADR010_DI|LayerDependency|RecordOwnerAssignmentCensus|RuleSetService|DryRunNoWrite|ProcedureRetireSelection|HardDenyOwnershipQuestion|Procedure|PolicyVersion|DraftPolicyVersion|ChildRowOwnership|AccessTableReaderScan|IOptionsDrift|CurrentVersionReader|SecureFamilyConsistency|FeatureRolePrivilege|ADR013_AiBoundary|RuleSetVisibleToFirms|SpaarkeMasterApplicationDependency` (each as `FullyQualifiedName~`): **547 passed, 0 failed**. Before the guard-list entry, 1 failed: `WriterOnlyTablesDeleteGuardTests.EveryGenericDeletePath_...` on `DeleteOrphanAssignmentsAsync`, as expected.
- **ArchTests filter 2:** `Owner|Census|Job|Delete|Writer|Lineage|Billing|Rule|Scheduled|AppOnly|Signal`: **993 passed, 0 failed**.
- The full ArchTests and full unit suites were not run, as the brief instructs.
- **Fix round 1.** Build: 0 errors, 0 warnings. Unit filter `RecordRuleSetRetireJobTests|DisplayName~117:|RuleSetServiceTests|ProcedureAssignmentServiceTests|SignalReevaluationJob|StoredFileReconcileJob|EligibleApproverRefreshJob|FinanceModule` (each `FullyQualifiedName~`, except the `DisplayName~117:`): **789 passed, 0 failed**. ArchTests filter `WriterOnlyTablesDeleteGuard|RecordOwnerAssignmentCensus|WorkloadPlacement|RuleSetService|IOptionsDrift|DryRunNoWrite|Scheduled|FeatureFlag|Ebilling`: **229 passed, 0 failed**.

## 3. Publish size and CVE

- **Method:** 123 §8 (the 124 §12 procedure):
  - `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -r linux-x64 --self-contained false`, then `Compress-Archive -CompressionLevel Optimal`;
  - each tree is a `git archive` export (BFF, `src/server/shared`, `config`, `Directory.Build.props`, `global.json`) into the session scratchpad, not a worktree.
- **Trees:** `origin/master` `2dcbe35e34`, base `b8e6e686c0` and branch tip `3850ea458e`. Each has 191 files.
- **Sizes:**

  | Tree | Bytes | Delta |
  |---|---|---|
  | master | 42,581,541 | — |
  | base | 42,540,703 | — |
  | branch | 42,554,511 | **−27,030 against master (project + task); +13,808 against base (this task alone)** |

  `Sprk.Bff.Api.dll` grew by 30,720 bytes. Every figure is far under +5 MB, 55 MB and 60 MB.
- **Packages:** none changed (no csproj or props diff against the base).
- **CVE:** `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports "has no vulnerable packages given the current sources".
- **Cleanup:** the scratch exports and publishes were deleted.

## 4. Known limits and open points

- **Faulting rows can hold back the rest, only past the page cap.** Every run starts at the top, and within a run the id cursor moves past a faulted row (it is attempted once per run). Only when more than `PageSize × MaxPages` rows fault on every run do they hold back the rows after them. The `faulted` counts and the `moreRemain` flag show this.
- **Which policies the retire switches Off.** It uses `LoadPoliciesAsync`, which returns the set's live (statecode Active) Billing policies, the same read as the Procedure retire (081). An inactive or non-Billing policy of a record set is not written.
- **Age floor and RemoveLink.** The age floor assumes the platform's RemoveLink cascade updates `modifiedon`. If it does not, the row is still taken; the floor then measures from its last edit.
- **A restored record does not revive its set.** If a matter is restored with its original id after the sweep has run, its set stays Retired: re-linking does not revive it. A later "change rule" on that record gets 409, as for any unlinked set.
- **A planner delete between the re-read and the delete.** If uac-r2's planner deletes an assignment row after the sweep re-reads it and before the sweep deletes it, the delete faults. That run shows one `faulted` row and is partial; the next run no longer finds the row.
- **An app-created assignment row with no core lookup is deleted.** A `sprk_procedureassignment` row created in the model-driven app with no core lookup, older than the age floor, is deleted. Model 5.2: such a row applies to nothing.
- **Record-set shape.** The sweep takes a set only when its name is exactly `RecordRuleSet.Name`, which 089's creator always writes. A set renamed in the app after its record's delete is not retired. A read-only scope on the rule-set form is a further option, not taken here (no form or script edit).

- **Signals are closed by D-170, not by the sweep (precondition for Apply).** The sweep switches the sets policies Off and retires the set; it closes no Signal. Open Signals of those policies are closed by ontology-r1s nightly D-170 close in SignalClosureService, whose not-in-force policy read is unpaged and stops past 5,000 rows (#2982, ontology DI-162). Before task 079 turns RecordRuleSetRetireSweep:Apply on, #2982 is fixed, or the dev count of not-in-force Billing policies is read and recorded as well under 5,000 (ontology-r1 #2992 review, 2026-11-23).

## 5. Review passes A and B, fix round 1

Each fix-now item has a test that fails before the fix and passes after; the proof is named. Fix commits are on `task/ebilling-117`.

| # | Finding | Class | Fix commit | Test and proof |
|---|---|---|---|---|
| 1 | The job never read `Ebilling:Enabled` (ADR-018); the sibling Finance jobs do | F | `1214a0e509` | `EbillingDisabled_WithApplyOn_SendsNoRequest_AndHeartbeatsTheSkip`: eBilling off and Apply on send zero requests; heartbeat `skippedReason=ebilling-disabled`; `Success` true, `Skipped` false (the host alone sets it, `.claude/patterns/api/scheduled-jobs.md`). Failed with the flag read replaced by `null`, passes with it. The old claim that the Communication sweep justified skipping the switch is removed. |
| 2 | A set hand-edited in the app to a record scope with no lookup (for example a legacy ClientDefault set) would be retired | K2 | `ac585e5ce6` | `ASetNotOf089sShape_IsUntouched` (3 cases × Record and Firm record): all 3 failed on the old predicate, pass now. See the evidence below. |
| 3 | `MinAgeMinutes` became a date before the try, so a huge value threw out of `ExecuteAsync` | K1 | `ed2d6af9db` | `AHugeAgeFloor_IsClampedToOneYear_AndTheRunCompletes` (`int.MaxValue`): failed with `ArgumentOutOfRangeException` before; passes with the clamp to 525,600 minutes. |
| 4 | The census waiver said record-scoped sets are NOT covered by `RuleSetService`, but the retire now writes them | F2 | `f1bfa64e96` | The waiver names the task 117 exception (statecode / statuscode and `sprk_enabled` only; no create, owner, lookup, Assign or delete; ADR-002, ADR-036). New guard `RuleSetServiceRecordSetRetireWritesStatusOnly` with must-fire (8 cases) and must-not-fire / non-vacuous controls. Proved on the real file: adding an `ownerid` write to the retire fails it. |
| 5 | The id cursor was unproven: a mutant dropping both `GreaterThan cursor` conditions passed every test | F2 | `ad50562314` | `ReportOnly_PagesOnTheIdCursor_CountingEachRowOnce` (3 of each, PageSize 1, MaxPages 5 → selected 3 each) and `AFirstRowThatAlwaysFaults_IsAttemptedOnce_AndTheRestAreProcessed`. Both fail on that mutant, pass now. |
| 6 | An assignment read fault failing the run was untested | F2 | `ad50562314` | `AnAssignmentReadFault_IsAFailedRun`: fails when the job ignores `assignments.ReadFailed`, passes now. |
| 7 | The registration test asserted DI descriptor lifetimes (ADR-038 ban B3) | F2 | `ad50562314` | Those two assertions are removed; `Enabled == false` and the cron checks stay. Not a behaviour change, so no fail-before. |
| 8 | The new delete-guard entry cited no ADR (tests/CLAUDE.md) | F2 | `f1bfa64e96` | The reason cites ADR-002 and uac-r2 core-record-delete design §14. Text only; `WriterOnlyTablesDeleteGuardTests` passes. |
| 9 | `AssertOnlyTheSweepsRequests` covered `IGenericEntityService` only | K | `ad50562314` | It also asserts no Signal close, no Decision Record preflight, record or enqueue, no owner question and no caller probe. No production path reaches them, so there is no fail-before. |
| 10 | Known limits not recorded (restored record, faults past the cap, planner race, app-created orphan row) | K3 | this notes commit | §4. |
| 11 | The `Deploy-EbillingProcedureSchema.ps1` line reference | — | this notes commit | The `sprk_excludedpolicy` lookup spec is at line 593 on this branch, its base and master (checked; line 594 is the `sprk_firm` spec). The reference now names the spec and says where the line number holds. The script is not edited. |

**Evidence for item 2.** `RecordRuleSetCreateAsync` (`ProcedureAssignmentService.cs:3018`) always writes `["sprk_name"] = RecordRuleSet.Name` (`:3023`). It is the one record-scoped `sprk_ruleset` create (census `RecordOwnerAssignmentCensusTests.cs:314`), and `RuleSetService` reads a record-scoped set as no set on every mutation route (`LoadRuleSetAsync`, `RuleSetService.cs:3592-3593`), so no BFF path renames one. So the name condition holds for every set 089 makes; it is ordinal, so it fails closed. `sprk_createdbyperson` is **not** a condition: 089's creator stamps it through `owner.ApplyTo(set)` (`:3055`), and `RecordOwnerResolution.ApplyTo` stamps a person only when the caller resolves to one (`RecordOwnershipResolver.cs:1855-1860`, `:2264-2266`: an unknown person "simply records nobody"). A set 089 made can therefore lack it.

**Not re-measured.** Publish size and CVE (§3) were not re-measured in this round. The change adds no package and a few lines of BFF code.

## uac-r2 hand-off and review

**Hand-off (sent 2026-11-23, before the build):** the five points of model 5.2:
1. Hard-deny adds sprk_procedure, sprk_procedureversion, sprk_storedfile (the planner's never-Delete set).
2. Delete sprk_procedureassignment with R through the planner.
3. Record-scoped sprk_ruleset and its policies and policy versions are unlink-only, retired by this sweep.
4. The rate-card row stands (§12: sprk_ratecard unlink-only, never Delete; sprk_ratecardrate untouched; both in the planner's never-Delete set). Since 085 the unlink has no rate effect.
5. A core record with invoices still Blocks the delete.

uac-r2 CONFIRMED all five against core-record-delete-design §12–§15. It added four points, all built in:
- (a) the age floor;
- (b) the cascade direction of an assignment delete;
- (c) the Restrict check;
- (4) evidence for the rate-card wording.

The live metadata read of 2026-11-23 shows sprk_ratecard still has the three RemoveLink core lookups. The owner then decided to drop them as task 193.

**Review (2026-11-23): uac-r2 APPROVED task/ebilling-117 at becd5b983c.**
- uac-r2's own runs: build 0/0; unit tests 604/604; ArchTests WriterOnlyTablesDeleteGuard and RecordOwnerAssignmentCensus 73/73.
- Both guard changes AGREED:
  - (i) the WriterOnlyTablesDeleteGuardTests entry for DeleteOrphanAssignmentsAsync;
  - (ii) the RuleSetServicePolicyWaiver 117 exception, plus RuleSetServiceRecordSetRetireWritesStatusOnly.
- The helper-escape K limit on that guard is accepted.
- Notes, no change asked:
  - The platform's RemoveLink clear bumps modifiedon, so the floor delays a just-deleted record's set by 15 minutes (harmless).
  - Report-only logs each would-be row at Information (fine at the 1,000-per-part cap).

**Scoped re-check of fix round 1 (Sonnet):** no F-class findings. K limits:
- The new guard reads only the retire method's text.
- Name narrowing: a case or space variant of the name is skipped harmlessly.
- The kill switch reads IOptions (a config change needs a restart, as in the sibling job).
- The removed DI assertions are covered elsewhere.

**Merged** into work/ebilling-r1 as f4a272136c. Post-merge: build 0 warnings 0 errors; unit filter (117, RuleSetService, ProcedureAssignment, upload, commit gate, StoredFile, FinanceModule, dry-run parity) 1071/0; ArchTests filter (WriterOnlyTablesDeleteGuard, RecordOwnerAssignmentCensus, WorkloadPlacement, RouteAuthorizationGuard, SecureChild, IOptionsDrift, FinanceStepZero) 324/0. Apply stays off until task 079 switches it on with the owner's OK.

**Update 2026-11-23 (ontology-r1).** #2982 is fixed on master by PR #3011 (merge `33d7c6a6ad`). The D-170 close now reads not-in-force policies by id-cursor paging, with no 5,000 cap. A truncated or failed read is counted as a PolicyFailure, so the job fails loudly. The 079 precondition is met once the deployed BFF contains `33d7c6a6ad` (check by content). It reaches dev with the next uac-r2 dev BFF build from master. Related cost item: DI-164 #3014, one Signal query per not-in-force policy, which grows with the sweep's Off switches.
