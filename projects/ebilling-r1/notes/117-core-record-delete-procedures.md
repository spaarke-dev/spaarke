# Task 117: Core-record delete and Procedures (the O-17 sweep)

Branch `task/ebilling-117`, base `b8e6e686c0` (origin/work/ebilling-r1). Rigor FULL. Written 2026-11-23.

## 1. What was built

After a core record is deleted, the platform clears the record lookups of its record-scoped rule set and of its Procedure assignment rows (RemoveLink, task 080). A scheduled sweep then cleans up both. It is report-only by default.

| Part | Where | What it does |
|---|---|---|
| Rule-set retire | `Services/Finance/RuleSetService.cs:2956` `RetireUnlinkedRecordSetsAsync` (result record `:2939`) | Selects Billing sets with scope Record or Firm record, no Procedure, every core lookup null, not Retired, and older than the age floor. Pages in id order. Re-reads and re-checks each set before writing. Switches each live Billing policy Off (`sprk_enabled` = No), then sets the set to statecode Inactive (1) and statuscode Retired (2). If any policy write faults, the set stays Active. |
| Unlinked predicate | `Services/Finance/Procedures/ProcedureAssignmentService.cs:216` `RecordRuleSet.IsUnlinked`, `:223` `AddUnlinkedFilter`, `:235` `UnlinkedColumns` | The scope values and the core-lookup list (`InvoiceFamilyCoreRecordGuard.FilingCoreTables`), in the class that already holds them. |
| Orphan assignment delete | `ProcedureAssignmentService.cs:2654` `DeleteOrphanAssignmentsAsync` (result record `:2641`) | Selects `sprk_procedureassignment` rows with every core lookup null and older than the age floor. Pages in id order. Re-reads and re-checks each row before the delete. |
| Job | `Services/Finance/Procedures/RecordRuleSetRetireJob.cs` (`ExecuteAsync` `:107`, heartbeat `:169`) | One `IScheduledJob`, one scope per run. Runs the two parts and writes one heartbeat with all counts and the Apply flag. A page read fault makes the run FAILED; a per-row fault makes it partial. |
| Registration | `Infrastructure/DI/FinanceModule.cs:146` | `AddScheduledJob<RecordRuleSetRetireJob>(CronOf(configuration), enabled: false)`. It is registered disabled, like `CommunicationThreadFilingSweepJob`. There is no BackgroundService. |
| Config | `appsettings.template.json:498` section `RecordRuleSetRetireSweep` | `Apply` (default false), `Cron` (blank = `45 3 * * *`, read at start-up), `PageSize` (100, max 5000), `MaxPages` (10), `MinAgeMinutes` (15). The Communication sweep's keys are not in the template, so this section sits after `Ebilling`. |
| Delete guard list | `tests/Spaarke.ArchTests/WriterOnlyTablesDeleteGuardTests.cs:60` | Adds `ProcedureAssignmentService.DeleteOrphanAssignmentsAsync` to `FixedTableDeletes`, citing uac-r2 design §14. **uac-r2 must agree this entry.** |

What the sweep never does:

- delete anything except an orphan assignment row;
- send an Assign, or write the owner, `sprk_createdbyperson` or any lookup;
- write `sprk_policyversion`;
- touch a set that has a Procedure, a ClientDefault or Firm set, or a set or row with any core lookup;
- send any request to `sprk_procedure`, `sprk_procedureversion`, `sprk_storedfile`, `sprk_ratecard`, `sprk_ratecardrate`, documents or communications;
- close Signals. D-170's nightly sweep closes the Signals of Off policies.

All reads and writes are app-only, through `IGenericEntityService`.

### Placement and scope justifications

- **Billing domain only (rule sets).** Ontology owns the rule sets of other domains, and 089 creates only Billing record sets (`ProcedureAssignmentService.RecordRuleSetCreateAsync`, `:3011`). The sweep filters on `RuleDomains.AddBillingFilter` and re-checks each row with `RuleDomains.IsBillingRow`.
- **Where the orphan delete lives.** `ProcedureAssignmentService` already writes every assignment row, including the removal delete. `RuleSetService` owns rule sets only, so a delete of assignment rows does not belong there.
- **The predicate is outside `RuleSetService`.** `RecordOwnerAssignmentCensusTests` (089a K4) fails when the waived `RuleSetService.cs` names a record scope (`RecordRuleSet.ScopeRecord`/`ScopeFirmRecord`). So the "unlinked" predicate and filter sit in `RecordRuleSet`, beside `IsRecordScoped` and `RequireOneHome`. `RuleSetService` stays the one rule-set lifecycle writer and gains one method.

### Age floor (uac-r2 addition a)

Rule sets and assignment rows are taken only when `modifiedon` is earlier than now minus `MinAgeMinutes` (default 15). The selection query applies this, and so does the in-memory re-check after the re-read. Supporting evidence: both 089 creators write the core lookup in the create request itself.

- Assignment rows: `AssignmentCreateAsync` builds the row with `[spec.CoreTable] = new EntityReference(...)` (`ProcedureAssignmentService.cs:2955`).
- The record-scoped set: `RecordRuleSetCreateAsync` builds it with `[coreTable] = new EntityReference(coreTable, recordId)` (`:3019`) and checks it with `RequireOneHome` before the create.
- The census lists `RecordRuleSetCreateAsync` as the one record-scoped `sprk_ruleset` create (`RecordOwnerAssignmentCensusTests.cs:309`).
- No other BFF file creates `sprk_procedureassignment` rows. A grep of `src/server` finds only metadata lists (lineage, resolver, role checks).

### What deleting an assignment removes (uac-r2 addition b)

`sprk_excludedpolicy` is a lookup ON `sprk_procedureassignment` to `sprk_policy`. The policy is the parent, so deleting an assignment deletes nothing else. Evidence:

- `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_Policy.xml:34`, relationship `sprk_procedureassignment_excludedpolicy_policy`: referencing entity `sprk_procedureassignment`, referencing attribute `sprk_ExcludedPolicy`, CascadeDelete `Cascade`. Deleting the **policy** deletes its override rows.
- `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1:593` (lookup spec, "a deleted rule deletes its override") and `:70` (the only parental relationship of the table).

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
| Apply off: no write, counts logged | `ApplyOff_WritesNothing_AndReportsTheCounts`; `AnythingButTrue_IsReportOnly` (theory); `RuleSetServiceTests.RetireUnlinked_ReportOnly_CountsAndWritesNothing`; `ProcedureAssignmentServiceTests.DeleteOrphans_ReportOnly_DeletesNothing` |
| Recorded requests: only reads, single-row updates (`sprk_enabled` / state+status) and orphan deletes; no Assign, lookup, owner or version write; no forbidden table | `AssertOnlyTheSweepsRequests`, called in the must-fire, report-only and fault tests |
| Re-read before write | `RuleSetServiceTests.RetireUnlinked_ASetReLinkedBeforeItsWrite_IsSkipped`; `ProcedureAssignmentServiceTests.DeleteOrphans_ARowReLinkedBeforeItsDelete_IsKept` |
| Age floor | `TheAgeFloor_LeavesRecentRows_AndTakesOlderOnes` (14 min kept, 16 min taken); `TheAgeFloor_IsConfigurable` |
| Policy fault leaves the set Active; the run goes on; the next run retires it; a third run writes nothing | `APolicyFault_LeavesTheSetActive_TheRunContinues_AndTheNextRunRetiresIt` |
| Second run writes nothing | `ASecondRun_WritesNothing` |
| Delete fault on one row continues | `ADeleteFault_OnOneRow_ContinuesTheRun` |
| Read fault means a failed run | `AReadFault_IsAFailedRun` |
| Paging and cap | `ThePageCap_IsHonoured_AndTheNextRunTakesTheRest` |
| Registered through AddScheduledJob, disabled, configurable cron, no hosted service | `TheJob_IsRegisteredThroughAddScheduledJob_Disabled` |

### Seeding proofs

- **(a) Drop "every lookup null".** Removed the `FilingCoreTables.All(... is null)` clause from `RecordRuleSet.IsUnlinked` and the per-core `Null` conditions from `AddUnlinkedFilter`. With the filter `RecordRuleSetRetireJobTests|DisplayName~117:`, 5 of 26 failed:
  - `ASetWithALookup_IsUntouched` (sprk_matter, sprk_project, sprk_workassignment);
  - `RuleSetServiceTests.RetireUnlinked_ASetReLinkedBeforeItsWrite_IsSkipped`;
  - `ApplyOff_WritesNothing_AndReportsTheCounts`.
  After restoring, 26 of 26 passed.
- **(b) Drop the assignment "all lookups null" condition.** Removed it from `DeleteOrphanAssignmentsAsync`'s `Matches` and `Selection`. With the same filter, 6 of 26 failed:
  - `AnAssignmentRowWithALiveLookup_IsKept` (×3);
  - `ProcedureAssignmentServiceTests.DeleteOrphans_DeletesTheOrphan_KeepsEveryRowWithALiveLookup`;
  - `DeleteOrphans_ARowReLinkedBeforeItsDelete_IsKept`;
  - `ApplyOff_WritesNothing_AndReportsTheCounts`.
  After restoring, 26 of 26 passed.

### Filters and counts

- **Build.** `dotnet build src/server/api/Sprk.Bff.Api/` gives 0 errors and 0 warnings.
- **Unit, brief filter:** `FullyQualifiedName~RecordRuleSetRetireJobTests|FullyQualifiedName~Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests|FullyQualifiedName~ProcedureAssignmentServiceTests|FullyQualifiedName~FinanceModule`: **438 passed, 0 failed**.
- **Unit, wider filter** (every user of the extended harness): `FullyQualifiedName~RecordRuleSetRetireJobTests|FullyQualifiedName~RuleSetService|FullyQualifiedName~Procedure|FullyQualifiedName~FinanceModule|FullyQualifiedName~CommunicationThreadFilingSweepJobTests`: **2628 passed, 0 failed**.
- **ArchTests filter 1:** `WriterOnlyTables|WorkloadPlacement|RouteAuthorizationGuard|SecureChild|Finance|ADR010_DI|LayerDependency|RecordOwnerAssignmentCensus|RuleSetService|DryRunNoWrite|ProcedureRetireSelection|HardDenyOwnershipQuestion|Procedure|PolicyVersion|DraftPolicyVersion|ChildRowOwnership|AccessTableReaderScan|IOptionsDrift|CurrentVersionReader|SecureFamilyConsistency|FeatureRolePrivilege|ADR013_AiBoundary|RuleSetVisibleToFirms|SpaarkeMasterApplicationDependency` (each as `FullyQualifiedName~`): **547 passed, 0 failed**. Before the guard-list entry, 1 failed: `WriterOnlyTablesDeleteGuardTests.EveryGenericDeletePath_...` on `DeleteOrphanAssignmentsAsync`, as expected.
- **ArchTests filter 2:** `Owner|Census|Job|Delete|Writer|Lineage|Billing|Rule|Scheduled|AppOnly|Signal`: **993 passed, 0 failed**.
- The full ArchTests and full unit suites were not run, as the brief instructs.

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

- **Every run starts at the top.** If more than `PageSize × MaxPages` rows fault on every run, they would hold back the rows after them. The counts and the `moreRemain` flag show this.
- **Which policies the retire switches Off.** It uses `LoadPoliciesAsync`, which returns the set's live (statecode Active) Billing policies, the same read as the Procedure retire (081). An inactive or non-Billing policy of a record set is not written.
- **No eBilling kill switch.** The job does not read `Ebilling:Enabled`, the same as the Communication sweep. Writes are gated by `Apply` alone. Main session to decide whether the kill switch should also gate it.
- **Age floor and RemoveLink.** The age floor assumes the platform's RemoveLink cascade updates `modifiedon`. If it does not, the row is still taken; the floor then measures from its last edit.

## uac-r2 hand-off and review
