# Task 089 - Assignment and override service; secure-child registration; K-2 pin

Split 2026-11-03 (see the POML "split" constraint): **089a** the secure-child registration as one change, **089b** `ProcedureAssignmentService` and its endpoints, **089c** change rule and remove. This file holds 089a; 089b and 089c add their own sections.

## 089a - registration (done 2026-11-03)

Branch `worktree-agent-a542b98a7be5593bf`, from `2d84774d57`. Not pushed. Nothing was written to Dataverse; the only live call was `Set-SecureRecordOwnerRolePrivileges.ps1 -WhatIf` (reads only).

| Commit | Subject |
|---|---|
| `f901b2dbe5` | register Procedure assignments and record-scoped rule sets as secure children |
| `4df57ce4af` | NFR-05 depth list for the hard-deny tables |
| `c5a7713669` | K-2 pin - billing lookups confer no access |
| (this commit) | notes, role-file evidence, TASK-INDEX row |

### What landed

- **Lineage** (`SecureChildLineage.cs`): `sprk_procedureassignment` and `sprk_ruleset` are children of matter / project / work assignment; `sprk_policy` gains `sprk_ruleset` as a parent. The secure family is set → rule → version and the assignment; a Procedure's rule set has no core lookup and is never reached.
- **Ownership** (`RecordOwnershipResolver.cs`): both tables in `OwnershipParentEntities`; the assignment's `sprk_procedure`, `sprk_pinnedversion` and `sprk_excludedpolicy` are `CitationLookups` (constraint 5.2: citing a Procedure or rule confers nothing).
- **Client** (`bffChildWriteAdapter.ts`): both tables in `OWNERSHIP_CHILD_TABLES` (pinned by `SecureChildNewCommandAgreementTests`).
- **Role set** (`config/secure-record-owner-role.json`): `prvReadsprk_procedureassignment` and `prvReadsprk_ruleset` as child entries (39 tables).
- **RecordCreatorPerson**: both tables are now codified children, so they leave `StampedOutsideCodifiedChildren`. `scripts/Set-ChildRecordCreatorPersonSchema.ps1` and its Pester test already covered all four tables; no change (Pester 5.7.1: 43/43; Pester 6.2.0 fails these script tests, pre-existing).
- **Census** (`RecordOwnerAssignmentCensusTests.cs`): both tables are child tables. The `RuleSetService` policy waiver is rewritten to cover Procedure (and legacy) rule sets only and is **Permanent** from 089a; `RuleSetService.cs sprk_ruleset 1` is Waived Permanent; `ProcedureService.cs` is an unscanned writer (its creates go through a `CreateRequest` batch built by `NewProcedureRuleSet`).
- **New guard** `WaivedRuleFamilyWritersNameNoRecordScope`: a waived or unscanned rule-family writer file must name no core-record lookup, no record scope (`RuleSetScope.Record` / `FirmRecord`, option 100000002 / 100000003) and no record-rule code or key (`"BRR-"`, `ebilling-record-rule-`). A Routed entry without a resolver call fires; a missing file fires. Controls: one must-fire per marker kind (8), the routed/unscanned case, and a must-not-fire for Procedure creates and routed record creates.
- **RecordFamilyEdges**: 7 lineage edges and 2 `OwnershipParentEntities` entries are baselined in `KnownDisagreements` (the assignment is never family, owner 2026-10-27; rule-set family membership is uac-r2's call).
- **NFR-05** (`SecureBuRoleDepthAssertion.cs`): `RestrictedContentPrivileges` lists the five hard-deny tables (assignment, rule set, rule, rule version, stored file) by live privilege name. Clause 1 grades every entry the codified set does not already cover; an entry that does not resolve in the environment raises the new verdict `RestrictedContentPrivilegeNotResolved` (severity 7, logged at Error by `SecureRecordIsolationCensusJob`). `GuardedPrivileges` (the three roots) is unchanged.
- **K-2** (`BillingLookupsConferNoAccessTests.cs`, 7 tests; `MembershipResolverServiceTests` theory over matter, project and work assignment): `sprk_billingfirm` and `sprk_billingresponsible` are in no access-conferring registry or appsettings, no grant / share / membership path reads them, and no external module serves a procedure-model table.

### Deviations

1. `CitationLookups` was extended; the POML named `OwnershipParentEntities` only, but constraint 5.2 needs the Procedure, version and rule lookups to be citations.
2. RecordFamilyEdges: baselined, not added as family rows (owner direction and uac-r2's open call above).
3. The real-reconciler test (`SecureChildRecordRuleSetLineageTests`) is in `tests/integration/data-mutation/ExternalAccess`, where the `SecureChildShareWorld` fixture lives, not in the unit path.
4. The K-2 behavioural test is in the unit suite, because ArchTests has no Moq.
5. The waiver is Permanent: after 089a a Procedure rule set is never owned by a secure team, so the waiver no longer waits on anything.
6. NFR-05 adds a new verdict and its job log level.
7. The role-file evidence for both privileges is the refusal recorded on 2026-10-27 by the earlier 089 attempt (commit `8f4920ce0c`, branch `task/ebill-089-assignments`), not a new probe; see the probe plan.

### Seeding proofs (each restored afterwards)

| Change | Result |
|---|---|
| Old lineage and resolver | 7 of 30 lineage unit tests fail; the must-not-fire passes |
| `sprk_ruleset` out of the client list | `SecureChildNewCommandAgreementTests` fails |
| `sprk_ruleset` out of census `ChildTables` | 4 census tests fail |
| `sprk_procedureassignment` out of `OwnershipParentEntities` | client agreement and RecordFamilyEdges parity fail |
| Role entry renamed | 2 SecureFamilyConsistency tests and `TheLineageMapCoversExactly` fail |
| `["sprk_project"] = null,` planted in `RuleSetService.NewProcedureRuleSet`; `"ebilling-record-rule-"` planted in `ProcedureService` | the new guard names both files and lines |
| Empty `RestrictedContentPrivileges` | 3 depth tests fail |
| `sprk_billingfirm` added to the matter registry | 2 ArchTests and the unit theory (matter) fail |

### Gates

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Spaarke.ArchTests (full, final tip) | 1947/1947 |
| Sprk.Bff.Api.Tests filtered (depth assertion, census job, SecureChild*, membership resolver, ownership resolver, owner role, secure family) | 1397/1397 |
| Pester 5.7.1, `Set-ChildRecordCreatorPersonSchema.Tests.ps1` | 43/43 |

No package was added, so no new CVE and no measurable publish-size change.

### `-WhatIf` (2026-11-03, reads only, exit 0)

```
Environment : https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
Config      : ...\config\secure-record-owner-role.json (39 tables)
Mode        : DRY RUN (no writes)
Role        : Secure Record Owner (e4ebabd9-b4a0-f111-aaac-000d3a99d1d7) in BU Secure Record (d9ec0b6f-80a0-f111-aaac-000d3a99d1d7)

Held now    : 44 privileges
Present     : 39 of 39  (... sprk_policy, sprk_policyversion, sprk_procedureassignment, sprk_ruleset, sprk_externalrecordaccess)
Outside the file (5) — reported, never removed by this script:
  prvCreateSharePointData                  Global
  prvReadSharePointData                    Global
  prvReadSharePointDocument                Global
  prvReadsprk_StoredFile                   Basic
  prvWriteSharePointData                   Global

Nothing to add.
```

No removal, no deeper depth, no table outside the four. `prvReadsprk_StoredFile` is task 086's entry (this project), applied live by the 086 branch and not yet in this file.

### Probe plan (main session)

Both privileges are **already held** on dev: the earlier 089 attempt ran the refusal probes on 2026-10-27 (both refused with 0x80040299, no row created; commit `8f4920ce0c`) and then `-Apply`. So:

- **Do not run the refusal probes.** A create owned by the team would now succeed and leave a row.
- **Do not run `-Apply`**: there is nothing to add.
- Run `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify` (reads only) after uac-r2 has seen the `-WhatIf`. **Done 2026-11-03: `-Verify` passed on dev, exit 0, Read at Basic on all 39 tables.**
- Run the NFR-05 census (`SecureRecordIsolationCensusJob`) once on dev: clause 1 now grades the stored-file privilege, so a human role holding it above Basic is a new finding. **Not run yet: it waits for the next dev BFF deploy, because the dev BFF predates 089a** (the deployed census does not know the depth list).
- The 2026-10-27 `-Apply` (the earlier 089 attempt, `8f4920ce0c`) **predates the rule that uac-r2 sees the `-WhatIf` first**. It was not re-run; the 2026-11-03 `-WhatIf` above is the one uac-r2 reviews, and `-Verify` confirms the state the old `-Apply` left.

### Overlaps

- `task/ebill-086-storedfile-secure-child` contains the earlier 089 attempt (`task/ebill-089-assignments`, `b9a80f9bd2` and `8f4920ce0c`). A trial merge conflicts in the role file, `SecureBuRoleDepthAssertion.cs`, `SecureChildLineage.cs`, `RecordOwnershipResolver.cs` and `BillingLookupsConferNoAccessTests.cs` (add/add). 086 must be rebased onto 089a, dropping the earlier 089 commits.
- `work/ebilling-r1`: a trial merge is clean.
- `work/unified-access-control-r2`, `task/ebill-081-procedure-service`, master and PR #2153: no shared file.

### K findings and open items

- Only `RuleSetServiceNoMatterScopeGuardTests` keeps `sprk_matter` off policies; it is unchanged.
- ~~Restrict on the core lookups (uac Q10) -> task 117.~~ **Superseded.** Model 5.2 says no Restrict is reachable from a core-record delete: the set's core lookups are RemoveLink, so deleting a matter unlinks its record-scoped sets (task 117 retires the unlinked ones) and nothing blocks the delete.
- `sprk_ruleset.sprk_sourcedocument` -> `sprk_document` (an ownership parent) is a citation. **Fixed in the 089a review (K7)**: a legacy uploaded set carries it (`RuleSetService.cs:497`), and read as a parent it would have filed the set under its document. Added to `CitationLookups`; the must-not-fire in `SecureChildLineageTests` now covers the legacy shape, with and without a core lookup.
- No pin yet that no non-BFF role holds Create or Write on `sprk_procedureversion` or `sprk_procedureassignment` (089b).
- 089c: record-scoped writers must live in their own Routed file, because the census guard works per file. 089c also decides whether evaluation closes stale base-code Signals.
- The client list has no npm unit test here (no `node_modules`); the ArchTest pins it.

### 089c must

Found in the 089a review; none is fixed in 089a. 089c is not done until each holds:

- **A RecordScoped refusal (409) on every `RuleSetService` mutation and on the list** (K1; `RuleSetService.cs:23`, `:1105`, `:2006`). A record-scoped set is a secure child; the legacy service must not edit, list or delete it.
- **Record-scope census markers that match write shapes only**, with the scope enum moved out of the waived file or exempted (K4; `RecordOwnerAssignmentCensusTests.cs:2304-2312`). The `RuleSetScope` marker also matches a read or a declaration, so `RuleSetService.cs` cannot carry the enum and 089c's reads cannot live in a waived file.
- **089c's record-rule writer is listed Routed or as an UnscannedWriter** (K1). `RuleFamilyWaiverProblems` sees only the files the census lists or names; a writer in neither is not caught. The doc sentence that said otherwise (`:2314`) is corrected in this review.
- **A write validator so a set never carries both `sprk_procedure` and a core lookup** (K1; `SecureChildLineage.cs:237`). The lineage reaches a set by its core lookup alone, so a set carrying both would be a secure child and a Procedure's set at once.

### 089a review fixes (2026-11-03)

| Item | Handling |
|---|---|
| F3: census Error branch untested | `Run_WhenADepthListPrivilegeDoesNotExistInTheEnvironment_LogsOneErrorNamingIt_NotCritical`: the fake directory lacks `prvReadsprk_StoredFile`; one Error naming it, no Critical, `status=findings`. Seeded: with the `or RestrictedContentPrivilegeNotResolved` clause dropped the test fails (the line goes out at Critical). |
| F3: comment claimed a protection the test did not give | `AnAssignment_ExcludingAnotherSecureMattersRecordRule_FollowsItsOwnMatterOutOfIsolation` (reconciler, Unsecure pass): an assignment on a mid-unsecure matter excludes another Secure matter's record rule and still moves to the content team. Seeded: with `("sprk_procedureassignment","sprk_excludedpolicy")` out of `CitationLookups` it fails. The first test's comment now says what it proves and points here and at the unit pin. (The Make Secure shape cannot express this: the reconciler never reads `sprk_excludedpolicy`, so the citation only matters to the parent derivation, i.e. secure-if-any on the Unsecure pass.) |
| K7: legacy set filed under its source document | `("sprk_ruleset","sprk_sourcedocument")` in `CitationLookups`; must-not-fire extended (legacy set alone: no parent; with a matter: the matter only). Seeded: fails without the entry. |
| K6: a missing table can go unnamed | The builder now reports every depth-list privilege the environment did not return, codified or not. The evaluator drops a codified one only where clause 5 can grade (tables present, exactly one non-replica owner role) and names it, so each missing privilege yields exactly one finding. Test: two owner roles and `prvReadsprk_ruleset` not installed give the role-count finding plus one `RestrictedContentPrivilegeNotResolved` naming it. Seeded: fails on the old builder. The existing one-finding theory (clause 5 able to grade) still passes. |

## 089b - assignment and rule-exclude service and routes (done 2026-11-03)

Branch `task/ebilling-089b`, from `work/ebilling-r1` at `53f3f6b724`. Not pushed. Nothing was written to Dataverse; no live call was made.

| Commit | Subject |
|---|---|
| `d7f863e5d1` | Procedure assignment and rule-exclude service and routes |
| (this commit) | notes, TASK-INDEX row |

### What landed

- **`ProcedureAssignmentService`** (`Services/Finance/Procedures/ProcedureAssignmentService.cs`) has two methods, `AssignProcedureAsync` and `ExcludeRuleAsync`. Each writes one `sprk_procedureassignment` row in one `ExecuteTransactionAsync`, all or nothing, with `CancellationToken.None`.
  - `AssignProcedureAsync` writes a FollowLatest, Pinned or Excluded row.
  - `ExcludeRuleAsync` excludes one rule, optionally for one firm.
- **Assign and pin checks:**
  - A pinned version must belong to that Procedure and be Active or Superseded (EB-D-155). Only a pinned row names a version.
  - A retired Procedure is 409 `assignment.procedure_retired`. A missing one is 404 `assignment.procedure_not_found`.
- **Exclude-rule checks:**
  - The rule must be an active Billing policy in a Billing set that is not record-scoped, or the call is 404 `assignment.rule_not_found`. A set is record-scoped if it has a Record or Firm-record scope or any core lookup; either one alone counts.
  - The system rule (a `SubmissionIntegrity` current version) is 409 `assignment.system_rule`.
  - The reason is required, at most 2000 characters. A named firm must exist.
- **Ownership (EB-D-168):**
  - The service asks `RecordOwnershipContext.ForChild(row)`, with the caller as `CallerObjectId` and `RequestedBy`. It calls `owner.ApplyTo(row)` in the same member that builds the row.
  - 089a drops the citations, so the core record is the row's one parent. A refusal writes nothing.
  - Real-resolver tests: an ordinary matter's row goes to the restricted-content owner team and a Secure matter's row to its Secure team. The creator person is the caller in both.
- **I-A1:** one row per (record, Procedure) and one per (record, rule), with or without a firm. Two guards:
  - a read for an existing row;
  - a derived v5 row id, `AssignmentIdFor(coreTable, recordId, subjectColumn, subjectId)`, so a racing create collides on the primary key (0x80040237).

  Both answer 409 `assignment.duplicate`.
- **Send results:**
  - a duplicate key: 409 `assignment.duplicate`;
  - a row-version conflict: 409 `assignment.concurrent_edit`;
  - no answer, or a timeout: 500 `assignment.write_failed`, "may or may not have been saved (id ...)";
  - any other fault: 500 `assignment.write_failed`, "nothing was written".
- **Core table:** it must be in `InvoiceFamilyCoreRecordGuard.FilingCoreTables` (a new internal accessor), and the record id must be non-empty. Anything else throws: it is a bug, never input.
- **Routes** (`Api/Finance/ProcedureAssignmentEndpoints.cs`): six POSTs, one literal chain per core type.
  - `/api/finance/{matters|projects|workassignments}/{recordId}/procedure-assignments` and `.../rule-overrides/exclude`.
  - Success is 201 with `Location` and the `OverrideView`.
  - Checks, in order, all as the caller and fail closed:
    1. `RequireAuthorization`.
    2. Step 0 on the group (`RequireInternalFinanceCaller`), which answers in the uniform-404 shape.
    3. The record filter `ebilling.record.override`: Write and AppendTo on that record, refused as the uniform 404.
    4. A second filter, refused as 403: Read on the Procedure (`sprk_procedures`) or on the rule (`sprk_policies`), plus `BillingAdminAuthorization.Admin()` (prvWritesprk_ruleset). They are composed in one filter, with no second marker.
    5. `RequireEbillingEnabled` (503).
  - A missing or empty body id is 400 before any subject check.
  - Errors are ProblemDetails with a stable `errorCode` (ADR-019). An owner refusal goes through `ProblemDetailsHelper.RecordOwnerRefused(..., "override", ...)`.
- **`OperationAccessPolicy`**: `ebilling.record.override` = Write | AppendTo, plus its two completeness rows.
- **DI**: `services.AddScoped<ProcedureAssignmentService>()` in `FinanceModule` (ADR-010: a concrete type, one registration), and `MapProcedureAssignmentEndpoints()` in `EndpointMappingExtensions`.
- **Census**: `ProcedureAssignmentService.cs sprk_procedureassignment 1` is Routed. A `PersonBearingWriter` is added for `CreateRowAsync`.
- **Route ledger**: `GovernedFile("Api/Finance/ProcedureAssignmentEndpoints.cs", Scope.RouteLevelGate, ...)`. `ExpectedEndpointFileCount` goes from 150 to 151.

### Placement and section 11 (CLAUDE.md §10, §11)

- **Placement:** the BFF, in the existing Finance module (`Services/Finance/Procedures`, `Api/Finance`).
  - The rows are secure children of a core record, owned through the BFF's resolver.
  - The caller checks need the BFF's OBO record path; no other host has it.
  - No package and no background work are added.
- **Existing:** `RuleSetService` writes rule sets without record scope, behind admin routes, and `ProcedureService` writes Procedures. Neither writes a row under a core record, and neither is caller-checked against one.
- **Extension:** neither service can take these writes. Their routes check only a Procedure or the admin marker, and these rows are secure children of a core record. The ownership question, the outcome type (`RuleSetOutcome`) and the transaction seam are reused, not copied.
- **Cost of doing nothing:** no record could be pinned to an earlier Procedure Version, opt out of a Procedure or waive a rule (O-4, O-10), and the resolver (082) would have no rows to read.
- **New operation key** `ebilling.record.override`: no existing key asks Write and AppendTo on a core record for an override. The closest, `ebilling.submission.decide`, is a different action with a different audit.

### Decisions and deviations

1. **One service with two methods; 089c extends it.** The constructor is minimal: `IGenericEntityService`, `IRecordOwnershipResolver` and the logger. 089c adds its own dependencies.
2. **The single create goes through `ExecuteTransactionAsync`.** 089c's change rule (set, rule, version and Exclude row) then uses the same seam and the same duplicate mapping.
3. **The record check comes first and answers 404; the subject and role checks come second and answer 403.** A caller who cannot write the record learns nothing about it. A caller who can learns only that the subject is not theirs to cite.
4. **Each route uses the literal operation string, not a const,** so the source scan in `OperationAccessPolicyCompletenessTests` discovers it.
5. **POML step 0 (a live OwnershipType read) was not run**, because this brief allows no live calls. The 080 notes record the table as user-owned, 089a registered it, and the real-resolver tests use that shape.
6. **The 089a open item about a missing role pin is stale.** That item says "No pin yet that no non-BFF role holds Create or Write on `sprk_procedureversion` or `sprk_procedureassignment` (089b)". `ProcedureRolePrivilegeGuardTests` (ArchTests) already pins this with `^prv(Create|Write)(sprk_procedureversion|sprk_procedureassignment)$`, so no new pin was added.

### Seeding proofs

Each change was restored afterwards, and the file content was compared to confirm it. Suite: `FullyQualifiedName~ProcedureAssignment`, 116 tests.

| Change | Result |
|---|---|
| S1: record filter dropped on both matter routes | 12 failed: the step-0 must-fire on matter rows (the refusal is no longer the uniform 404), the uniform-404 theory (3) and the record-check must-not-fire |
| S2: Read on the Procedure dropped (assign targets become admin only) | 3 failed: "no Read on the Procedure is 403" (matters, projects, work assignments) |
| S3: Read on the rule dropped (exclude targets become admin only) | 3 failed: "no Read on the rule is 403" (3) |
| S4: admin marker dropped (subject only) | 12 failed: "without the billing-admin privilege is 403" (6) and "a privilege probe fault fails closed" (6) |
| S5: step 0 dropped from the group | 24 failed: the step-0 must-fire theory |
| S6: the duplicate read never matches | 1 failed: "a second row for the same Procedure is 409 and writes nothing" (the derived id still refuses it, but only after a transaction is sent) |
| S7: the derived id replaced by a random id | 4 failed: the race test and the three tests that pin the row id |
| S8: `RequestedBy` dropped | 2 failed: the assign and exclude row tests (the resolver substitute sees no requester) |
| S9: the duplicate-key fault not mapped | 1 failed: the race test (500, not 409) |

### Gates

| Run | Result |
|---|---|
| Build: Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spaarke.Core.Tests, Spe.Integration.Tests | each 0 warnings, 0 errors |
| Spaarke.ArchTests (full) | 1951/1951 |
| Sprk.Bff.Api.Tests `FullyQualifiedName~Procedure\|~Finance\|~Assignment` | 5510 passed, 3 skipped, 0 failed |
| Sprk.Bff.Api.Tests, adjacent: OperationAccessPolicy, RecordOwnership, SecureChild, BillingLookups, StepZero, EndpointRegistration, Startup | 2207/2207 |
| Spaarke.Core.Tests | 64/64 |
| New tests (`~ProcedureAssignment`): 23 service, 93 endpoint cases | 116/116 |
| `dotnet list package --vulnerable --include-transitive` (BFF) | none |
| Publish size: `git archive` of `53f3f6b724` vs `d7f863e5d1`, `dotnet publish -c Release -r linux-x64 --self-contained false`, `Compress-Archive -CompressionLevel Optimal` | 41,249,100 B to 41,264,209 B = **+15,109 B**; 192 files each; about 41.3 MB in total. No package added |

### Known limits and questions

- **K: Read on the rule's policy may refuse a billing admin in a child business unit (fail closed). Question for uac-r2 and the owner.**
  - Billing policies are owned by the restricted-content team in the top business unit, and billing-admin Read on `sprk_policy` is Deep.
  - EB-D-162 says "no route depends on it"; this route now does.
  - So an admin in a unit below the policy's owner may get 403 for a rule they can see in the UI through the BFF.
  - The alternative is to ask the caller's Read on the rule's Procedure: resolve the policy, its rule set and its Procedure as the app, then check the caller on that Procedure. That needs a custom filter.
  - Not changed here, because the current check fails closed.
- **K: an Exclude can name a firm the rule does not apply to.** For a firm-named base rule and another firm, the row is accepted but is a no-op: the resolver (082) never matches it. 089c's change rule refuses the same shape with `assignment.firm_mismatch`, and the exclude path could reuse that check later.
- **K: a missing firm is a 400** ("The firm was not found"), so a billing admin can test whether an organization id exists. Billing admins already read organizations, so no caller Read on the firm is asked.
- **K: an Exclude does not check that the rule belongs to a Procedure assigned to, or in force on, the record.** An Exclude of an unrelated rule is a no-op, and the caller still needs Read on the rule.
- **The duplicate read does not filter `statecode`.** An inactive row still counts, and its derived id would collide anyway. So **089c's remove must delete the row, not deactivate it**, or that subject can never be overridden again.

### 089c must (in addition to the 089a list above)

- **Add change rule and remove (with preview).**
  - The methods go in `ProcedureAssignmentService`; the routes go in `ProcedureAssignmentEndpoints`, one literal chain per core type.
  - Extend the constructor with `RuleSetService`, `SignalClosureService` and `AffectedInvoiceCounter` as needed, and update the constructor arguments of the endpoint tests' `Mock<ProcedureAssignmentService>`.
- **Reuse `AssignmentIdFor(coreTable, recordId, ExcludedPolicyColumn, policyId)` for change rule's Exclude row.** An Exclude and a change of the same rule are then one row per (record, rule) and collide on the key.
- **Make the record filter (`ebilling.record.override`) each new route's first filter.** Add the new privileges to the second filter: Create on rule set, policy and policy version, and Write on policy for a removal.
- **Keep the route-ledger entry** (same file, so `ExpectedEndpointFileCount` does not change).
- **Add census entries:**
  - the record-rule writer as Routed, in its own file if the census needs it (see the 089a list);
  - a `PersonBearingWriter` for each new member that builds a row.
- **Check Read on what the stored row overrides before removing it.** The request body does not name that subject, so this needs a custom filter. If one is added, give it a credited form for the route ledger.
- **Move the record-scope constants if the new writer needs them.** `ScopeRecord` and `ScopeFirmRecord` are private to this service; a shared place must respect the census marker rule (089a K4).
- **Remove by deleting the row, not deactivating it** (see the limit above).

### 089b: uac-r2 decisions (2026-11-03)
- **Exclude route:** keep the caller's Read check on `sprk_policies` and its fail-closed 403. Do **not** add a custom filter that infers rule rights from Procedure rights; that would be a second, softer authorization path to the same rows.
- **Child-unit admins:** a billing admin in a child business unit may get 403 for a rule held in the top unit. If the owner wants such admins to be able to exclude top-unit rules, the fix is in the role: Billing Administrator gets Read on `sprk_policy` at Global (rules are org-wide configuration), set in the role config and checked by the census. This is an open owner question. Until the owner answers, the 403 is correct.
- **UI constraint (084 and any UI that offers Exclude):** offer Exclude only on rules the caller can Read. Otherwise the 403 looks like a bug. On 2026-11-03 no client code calls these routes.

### 089b review fixes (2026-11-03, branch `fix/ebilling-089b-review`)

**F1 (fixed): the system rule got 404 `assignment.rule_not_found` instead of 409 `assignment.system_rule`.**
- **Cause:** `LoadRuleAsync` required a rule set before it looked at the rule's type. The system policy `BILL-SYSTEM-SUBMISSION` belongs to no rule set (`scripts/ebilling/Seed-BillingSystemPolicy.ps1`: "the system policy belongs to none"), so the 409 branch could not be reached on real data. The old test seeded the system rule inside a published set, a shape production never has.
- **Fix:** `ProcedureAssignmentService.IsSystemRuleAsync` runs right after the Billing policy is found and before the rule set is required. It matches `sprk_policycode == BillingRuleCatalogue.SystemPolicyCode` (trimmed, ordinal, as `InvoiceSubmissionAfterSteps` finds it) or a version in force of type `SubmissionIntegrity` (the check that was already here, as `RuleSetService` ~2635 applies it). No rule was duplicated. The route's Read check on the rule still runs first, and the order of the other checks is unchanged.
- **Tests (`ProcedureAssignmentServiceTests`):**
  - `ExcludeRule_RefusesTheSystemRule_AndWritesNothing` re-seeded with the system rule holding no `sprk_ruleset` (`World.RuleWithoutSet`): 409 `assignment.system_rule`, nothing written.
  - New: a `SubmissionIntegrity` rule inside a set stays 409.
  - New: an ordinary rule with no rule set stays 404 `assignment.rule_not_found`.
- **Seed proof:** with `ProcedureAssignmentService.cs` reverted to the parent commit, the re-seeded test fails ("Expected outcome.StatusCode to be 409, but found 404"; 1 failed, 9 passed of the `ExcludeRule` cases). The fixed file was restored and compared byte for byte (`cmp`: identical).
- **Gate:**
  - All 5 test projects build with 0 warnings, 0 errors (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests).
  - Unit filter `ProcedureAssignment|OperationAccessPolicy|RuleSet`: 624 passed, 0 failed, 0 skipped.
  - ArchTests filter `RecordOwnerAssignmentCensus|RouteAuthorizationGuard|ProcedureRolePrivilege`: 154 passed, 0 failed.

**Known limits recorded from the review (not changed):**
- **K2: Read on `sprk_policies` is Deep.**
  - uac-r2 decided (2026-11-03) to keep the check, with no Procedure-inferred filter (a second, softer authorization path).
  - **Owner question:** does Billing Administrator get Global Read on `sprk_policy`? Rules are org-wide configuration, so Global would let a child-unit admin exclude top-unit rules. The change would be in the role config and checked by the census.
  - **Tension:** EB-D-162 premises "no route depends on the depth" (`notes/decisions-log.md` ~169). This route now does. The owner resolves it: either amend that premise or raise the role's Read depth.
  - Until then the 403 is correct and fails closed.
- **K3: the race-duplicate relies on fault code `0x80040237` in `Detail.ErrorCode`.** It has never been verified against live Dataverse. A live probe needs owner OK; none was run.
- **K4: a missing firm returns 400** ("The firm was not found"), which reveals whether an organization id exists. An **inactive firm is accepted** (the firm lookup does not filter `statecode`).
- **K5: unrelated rules and firms are accepted as no-op rows.** An Exclude of a rule that no Procedure in force on the record carries, or for a firm the rule does not apply to, is written and never matched by the resolver (082).
- **K6: the 201 `Location` points at a GET that does not exist until 084.**
- **K7: step 0 runs twice per request** (a cost, not a defect; not changed).

### 089b review 2 fixes (2026-11-03, branch `fix/ebilling-089b-review2`)

**F1 + F3 (fixed): every exclude that named a firm returned 500.**
- **Cause:** `ExcludeRuleAsync` checked the firm with a `$select` of `sprk_name` on `sprk_organization`. That column does not exist; the primary name is `sprk_organizationname` (`RegardingNameFields.cs:29`; `AssignedAccessStore.cs:876`). The test harness answered any column set on `sprk_organization`, so the tests passed against a query production cannot run.
- **Fix:** the check selects `sprk_organizationid` only (const `FirmIdColumn`), as `RuleSetService.ExistsAsync` does.
- **Other columns:** every `ColumnSet` and `$select` in `ProcedureAssignmentService` was checked against `Entities/sprk_Policy`, `sprk_PolicyVersion`, `sprk_ruleset`, `sprk_Organization` and `notes/080-procedure-schema.md` (2.2, 2.3). **No other wrong name.** Checked: policy `sprk_name`, `sprk_policycode`, `sprk_ruleset`, `sprk_currentversion`, `sprk_domain`; policy version `sprk_policy`, `sprk_ruletype`; rule set `sprk_scope`, `sprk_domain` and the three core lookups; Procedure and version columns (`ProcedureEntity`); the assignment's own subject and core lookups.
- **Tests (`ProcedureAssignmentServiceTests`):**
  - `World` now sets `H.AfterQuery` so the fake refuses a column `sprk_organization` does not have (`sprk_organizationid`, `sprk_organizationname`). It is local to this class; `RuleSetServiceTests.Harness` is unchanged.
  - New `ExcludeRule_TheFirmCheck_SelectsOnlyTheKey_NeverSprkName`: the exclude succeeds and the firm query's columns are exactly `sprk_organizationid`.
- **Seed proof:** with `sprk_name` restored in the firm check, 4 fail: the new test, plus 3 existing tests that had been passing against the bad query (`ExcludeRule_WritesOneRow...ForOneFirm...`, `ExcludeRule_NeedsAReason_AndAFirmThatExists`, `...SecondExcludeOfTheSameRule...`). Restored; `cmp` identical.

**K2 (fixed): a Dataverse read fault before the write was a generic 500.**
- All pre-write reads (Procedure, version, firm, rule, rule set, system-rule version, duplicate) go through `ReadAsync`. A fault (other than the caller's own cancellation) becomes a private `ReadFailedException`; `AssignProcedureAsync` and `ExcludeRuleAsync` catch it and return `Refused(503, "assignment.read_failed")` (ADR-019), logged, nothing written. `SendAsync` (the transaction write) is not wrapped and keeps its own mapping.
- **Test:** `APreWriteRead_ThatFaults_Is503ReadFailed_AndWritesNothing` (assign and exclude).
- **Seed proof:** with the service at the parent commit, both cases fail (the exception escapes). Restored; `cmp` identical.

**K3 (fixed): a caller whose `oid` is not a GUID ran the service with no requester.**
- `ProcedureAssignmentEndpoints.Created` now takes the caller first and returns 401 (the shape `FinanceAuthorizationFilter` uses) when `CallerOid` does not parse; the service is never called. The six routes pass `oid => service...`.
- In production step 0 already refuses such a caller (it resolves GUID oids only), so this is defence in depth; the test host reaches the route through `StatedCaller.Register(normaliseOid: true)`.
- **Test:** `EveryRoute_ACallerWhoseOidIsNotAGuid_Is401_AndNeverReachesTheService` (six routes).
- **Seed proof:** with the endpoints file at the parent commit, all 6 fail. Restored; `cmp` identical.

**K1 (recorded, not changed): the retired-Procedure check is time-of-check vs time-of-use.**
- `AssignProcedureAsync` reads the Procedure's status, then creates the row in a separate transaction. A Procedure retired in between still gets an assignment.
- The window is small and the row is harmless. **Task 082's resolver must ignore assignments to retired Procedures**, so a row that slips through has no effect.

**Gate:**
- All 5 test projects build with 0 warnings, 0 errors (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests).
- Unit filter `ProcedureAssignment|OperationAccessPolicy|RuleSet`: 633 passed, 0 failed, 0 skipped (`~ProcedureAssignment` alone: 127).
- ArchTests filter `RouteAuthorizationGuard|RecordOwnerAssignmentCensus|FinanceStepZero|ProcedureRolePrivilege`: 166 passed, 0 failed.

### 089d design (uac-r2, 2026-11-03)

- **Owner's decision:** "yes Billing Administrator can have read on rules across whole organization".
- **Shapes ruled out (D-123):** Global Read on `sprk_policy`, and Deep Read held in the root, both reach the Secure Record business unit, which is a direct child of the root (INCOMING-145 T2). Deep from a child unit doesn't reach up to the customer's top unit, so it doesn't meet the owner's decision.
- **Shape chosen:** an owner team **"Billing Rule Readers"** in the customer's top unit (the unit `RestrictedContentOwnerTeam` resolves; never the root).
  - The team holds one role: Read at Deep on `sprk_policy`. It gets Read on `sprk_policyversion` too only if Exclude reads that table.
  - Child-unit billing admins become members, so they read every rule owned in the customer unit and its children.
  - Secure-owned rules sit in Secure Record, a sibling of the customer unit, so the team never reaches them.
- **Guards:**
  - Add the role to the role guard's scan.
  - A verify step checks that the team is not in the root, that no Global depth is granted, and that the role holds no extra privilege.
  - The Secure census reports any principal that reaches Secure Record.
- **Setup:** creating the team and role is a per-customer setup step. It goes in the customer deployment guide and is handed to customer-provisioning-orchestration-r1.
- **Dev check (read-only, 2026-11-04):**
  - The only Billing rule is BILL-SYSTEM-SUBMISSION, team-owned in Spaarke Business Unit 1, the customer's top unit.
  - The 6 policies owned in the root "Spaarke" are ontology product rules (POL-TASK-OVERDUE, POL-TASK-DUESOON, POL-WA-RESPONSE, POL-TODO-OVERDUE, POL-MATTER-NOBUDGET, POL-COMMIT-BUDGET), not Billing rules, and Exclude refuses them. No re-owning is needed for Billing rules on dev.
- **Until the team exists,** a child-unit admin's 403 is correct.
- **Task:** `tasks/089d-billing-rule-readers-team.poml`.

## 089d - Billing Rule Readers team (code done 2026-11-04; -Apply pending the owner's go)

Branch `task/ebilling-089d`, from `work/ebilling-r1` at `012d5dfa68`. Not pushed, not merged. **Nothing was written to Dataverse**: the dev reads below and the `-WhatIf` run are read-only. `-Apply` is the main session's, after the owner's go and the uac-r2 notices.

### What landed

- **`config/billing-rule-readers-role.json`** (new): team `Billing Rule Readers`, role `Billing Rule Reader`, `businessUnit: customer`, Read at **Deep** on `sprk_policy` only, `membership: members`, `memberPrivilege: prvWritesprk_ruleset` (the billing-admin marker, `BillingAdminAuthorization.WriteRuleSetPrivilege`).
- **`scripts/Set-CustomerOwnerTeams.ps1`** (extended, no parallel script): the file joins `$DeclaredRoleFiles`.
  - A config with no `membership` (or `none`) is a memberless owner team at Basic, as before. `members` is a member team at Deep. Every other combination is refused before any read, and so is Global.
  - Members are allowed on a member team. `-Apply` never adds or removes one. Each member must hold `memberPrivilege` (read with `RetrieveUserPrivileges`); a member without it is drift (`MEMBER PRIVILEGE`) and an owner decision.
  - Each member must be homed in the customer's top unit or below it. A member homed in another customer's unit (dev has four customer top units), the root or Secure Record is drift (`MEMBER UNIT`) and an owner decision: such a member would read this customer's rules through the team.
  - New drift for **every** declared team: `TEAM PLACE`, a team of the name in the root, in Secure Record or below it. It is an owner decision (`-Apply` never moves or deletes a team). On dev no declared team is in either place, so the two uac-r2 teams are unchanged.
  - Depth is the config's (`PRIVILEGE DEPTH ... expected Deep`). `-Apply` adds at that depth.
  - The output now prints each team's unit by name, its member count, and each privilege its role holds with the depth.
  - The duplicate-table refusal applies to owner configs only. `sprk_policy` is in the restricted-content file too, and a member team reads; it never owns.
- **`RestrictedContentOwnerRoleGuardTests`** (ArchTests): a third `DeclaredRole` with `Depth` and `MemberPrivilege`. `Findings` checks the depth, the membership and the member privilege per role. 18 must-fire documents, a named-break control, the member-privilege pin, a script must-fire (the entry commented out), and the negative-case reach test (below). The owner-table check is now owner roles only.
- **`tests/scripts/Set-CustomerOwnerTeams.Tests.ps1`** (Pester): a second Describe over a mocked Dataverse (every write fails the test), run with `-ConfigPaths` for this file alone.
- **Docs:** `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.11 (the step), §12.2 Phase 7a (item 3 and the by-hand member step), the change log. `UAC-ACCESS-MODEL-GUIDE.md`, one paragraph after "Hard-deny tables".

### Does Exclude read `sprk_policyversion`? No, not as the caller

- The caller checks on the Exclude routes are the second filter, `ProcedureAssignmentEndpoints.ExcludeTargets`: `ReadOn(PolicyEntitySet = "sprk_policies", body.policyId)` plus `BillingAdminAuthorization.Admin()`.
  - The Read check is `FinanceCheckPath.Record`, which runs `FinanceAuthorizationFilter.CallerHoldsRightsAsync` → `AuthorizationService.GetCallerRecordAccessAsync` on that one policy record.
  - `Admin()` is a caller **privilege** check (`prvWritesprk_ruleset`), not a record read.
- The service reads `sprk_policyversion` once, in `ProcedureAssignmentService.IsSystemRuleAsync` (the current version's `sprk_policy` and `sprk_ruletype`). It reads through `IGenericEntityService`, the application identity; `LoadRuleAsync`'s summary says "Read as the application; the route has already checked the caller's Read on the policy".
- So the role holds **no** `sprk_policyversion` Read. A future caller-checked route that reads versions (for example 084's read models) must bring its own evidence before the file gains a table; the guard fails the build until both change.

### Dev check (read-only, 2026-11-04)

- Business units: root `Spaarke` (06fbf21c...); direct children `Secure Record` (d9ec0b6f...), `Spaarke Business Unit 1` (cb15f587..., the customer top unit), `Spaarke Demo`, `Spaarke Dev 1`, `Spaarke Test 1`. **`Spaarke Business Unit 1` has no child unit**, so dev has no child-unit billing admin yet: after `-Apply` the team has 0 members.
- Billing rules (`sprk_domain` 100000000): one, `BILL-SYSTEM-SUBMISSION`, owned by `Spaarke Restricted Content Owners` in `Spaarke Business Unit 1`. The six root-owned policies (POL-TASK-OVERDUE, POL-TASK-DUESOON, POL-WA-RESPONSE, POL-TODO-OVERDUE, POL-MATTER-NOBUDGET, POL-COMMIT-BUDGET) have an empty domain (Ontology). **No Billing rule is owned in the root**: no finding, nothing to re-own.
- No team named `Billing Rule Readers` and no role named `Billing Rule Reader` exist.
- `RetrieveRolePrivilegesRole` spells the depth `Deep`, and `RetrieveUserPrivileges` returns `PrivilegeName` per row (checked on the operator's own user), which the member check relies on.

### `-WhatIf` on dev (2026-11-04, read-only, exit 0)

`scripts/Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitName 'Spaarke Business Unit 1' -WhatIf`:

```
Environment : https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
Config      : ...\config\restricted-content-owner-role.json ('Spaarke Restricted Content Owners', 5 tables)
Config      : ...\config\customer-configuration-owner-role.json ('Spaarke Customer Configuration Owners', 3 tables)
Config      : ...\config\billing-rule-readers-role.json ('Billing Rule Readers', 1 tables)
Mode        : DRY RUN (no writes)
Root BU     : Spaarke (06fbf21c-1872-f011-b4cb-7c1e52671ad0)
Secure BU   : Secure Record (d9ec0b6f-80a0-f111-aaac-000d3a99d1d7)
Customer BU : Spaarke Business Unit 1 (cb15f587-baa0-f111-aaac-000d3a99d1d7)

== Spaarke Restricted Content Owners (restricted-content-owner-role.json)
Declared    : Read at Basic on sprk_policy, sprk_policyversion, sprk_storedfile, sprk_ruleset, sprk_procedureassignment; memberless
Team        : Spaarke Restricted Content Owners (16685363-43d3-f111-84e8-00224820fd20) teamtype=0 isdefault=False bu=Spaarke Business Unit 1 (cb15f587-baa0-f111-aaac-000d3a99d1d7) members=0 roles=1
Role        : Restricted Content Owner (ba9bf15f-43d3-f111-84e8-7ced8dda31f8) customer-unit copy=0d4e0675-fce9-4e39-baf5-310b0ead22f3 copies=6 assignments=1 privileges=5
Privilege   : prvReadsprk_Policy at Basic
Privilege   : prvReadsprk_PolicyVersion at Basic
Privilege   : prvReadsprk_procedureassignment at Basic
Privilege   : prvReadsprk_ruleset at Basic
Privilege   : prvReadsprk_StoredFile at Basic
INFO        : customer top unit 'Spaarke Demo' (9271b764-952f-f111-88b5-7c1e520aa4df) has no 'Spaarke Restricted Content Owners' team (not this run's unit; not a failure).
INFO        : customer top unit 'Spaarke Dev 1' (7bdb15ee-e39e-f011-bbd3-7c1e5215b8b5) has no 'Spaarke Restricted Content Owners' team (not this run's unit; not a failure).
INFO        : customer top unit 'Spaarke Test 1' (1a75377a-e29e-f011-bbd3-7c1e5217cd7c) has no 'Spaarke Restricted Content Owners' team (not this run's unit; not a failure).
Drift       : none

== Spaarke Customer Configuration Owners (customer-configuration-owner-role.json)
Declared    : Read at Basic on sprk_aisearchindex, sprk_fieldmappingprofile, sprk_fieldmappingrule; memberless
Team        : Spaarke Customer Configuration Owners (2dae7e6e-43d3-f111-84e8-00224820fd20) teamtype=0 isdefault=False bu=Spaarke Business Unit 1 (cb15f587-baa0-f111-aaac-000d3a99d1d7) members=0 roles=1
Role        : Customer Configuration Owner (31ae7e6e-43d3-f111-84e8-00224820fd20) customer-unit copy=fca8fb47-24f2-43d9-bc27-8f21de147a22 copies=6 assignments=1 privileges=3
Privilege   : prvReadsprk_AISearchIndex at Basic
Privilege   : prvReadsprk_FieldMappingProfile at Basic
Privilege   : prvReadsprk_FieldMappingRule at Basic
INFO        : (the same three customer top units, no team; not a failure)
Drift       : none

== Billing Rule Readers (billing-rule-readers-role.json)
Declared    : Read at Deep on sprk_policy; members allowed, each holding prvWritesprk_ruleset
Team        : (none named 'Billing Rule Readers' in Spaarke Business Unit 1)
Role        : (none named 'Billing Rule Reader' in the root business unit)
INFO        : (the same three customer top units, no team; not a failure)
DRIFT       : TEAM MISSING: no team named 'Billing Rule Readers' in Spaarke Business Unit 1.
DRIFT       : ROLE MISSING: no role named 'Billing Rule Reader' in the root business unit.

PLAN (dry run, nothing written):
  1. create the Owner team 'Billing Rule Readers' in Spaarke Business Unit 1 (cb15f587-baa0-f111-aaac-000d3a99d1d7) (teamtype 0), with no members
  2. create the role 'Billing Rule Reader' in the root business unit (Dataverse copies it into every child unit; the root copy is assigned to nobody)
  3. add Read at Deep to 'Billing Rule Reader': prvReadsprk_Policy
  4. take off every privilege of 'Billing Rule Reader' outside billing-rule-readers-role.json, including any the platform injects (setup guide 5.4)
  5. assign the Spaarke Business Unit 1 copy of 'Billing Rule Reader' to 'Billing Rule Readers'
  6. read 'Billing Rule Readers' back and check it as -Verify does
  7. (not this script) an operator adds each of Spaarke Business Unit 1's child-unit billing admins (holding prvWritesprk_ruleset) to 'Billing Rule Readers', then re-runs -Verify

DRY RUN: no writes. Re-run with -Apply (owner go), then -Verify.
exit=0
```

Against the first acceptance criterion: the team goes in Spaarke Business Unit 1; the role gets Read at Deep on `sprk_policy` only; nothing changes in the root or Secure Record. The only write in the root is the role definition itself (Dataverse requires a role to be created there and copies it down). Its root copy is assigned to nobody, and `-Verify` reports any assignment outside a customer top unit's team of that name (`ROLE ASSIGNED ELSEWHERE`). The two uac-r2 teams show no drift and their plan is empty.

### Negative case (documented reasoning plus a test; the live probe needs a write)

- **Claim:** a member reads a rule owned in a child unit of the customer unit, and cannot read a rule owned by the Secure Record Owners team.
- **Why it holds:** a privilege a role grants to an owner team is evaluated against the team's business unit, not the member's. Deep (Parent: Child Business Units) covers that unit and every unit below it. The team sits in the customer's top unit, a direct child of the root. Secure Record is another direct child of the root, so it is a sibling, never a descendant, and Deep from the customer unit never covers it. The census (`SecureBuRoleDepthAssertion.Reaches`) uses the same model: its remarks say a team-held grant is anchored at the team's business unit.
- **Test:** `RestrictedContentOwnerRoleGuardTests.TheBillingRuleReadersTeam_DeepFromTheCustomerTopUnit_ReachesItsChildUnits_AndNeverSecureRecord` reads the live file's depth and checks it with `Reaches` over a tree of root, Secure Record, the customer unit with a child and a grandchild, and a second customer unit:
  - Deep from the customer unit reaches the customer unit, its child and its grandchild.
  - It does not reach Secure Record, the root or the other customer.
  - Deep from the child unit does not reach the customer unit (the original 403).
  - Deep from the root, and Global, do reach Secure Record. These are the two shapes ruled out.
- **Belt:** if the team were ever put in the root, the 15-minute Secure census (clause 1) would report it as a team-held grant reaching Secure Record, and `-Verify` reports `TEAM PLACE`.
- **Live probe (not run; it needs writes):** dev has no child unit under Spaarke Business Unit 1. A real probe needs a child unit, a billing admin homed in it and added as a member, then an Exclude on `BILL-SYSTEM-SUBMISSION` that gets 409 `assignment.system_rule` instead of 403. A Secure-owned rule must stay 403. Each of those steps is a live write and the owner's call. The escalation trigger did not fire: nothing read so far contradicts the reach model.

### Decisions

1. **Members are added by hand, not by the script.** Membership is a per-person, ongoing decision (who is a billing admin, in which unit); a script that takes a list would need its own source of truth. The guide says how, and `-Verify` checks every member.
2. **Every member must hold the billing-admin marker and be homed in this customer's units.** Membership gives direct Dataverse Read on every rule in the customer, which D-185 keeps from end users. An operator who adds the wrong person, or another customer's admin, is the realistic failure, and the checks cost one read per member.
3. **`TEAM PLACE` applies to all declared teams.** It fails closed and changes nothing on dev; a team of a declared name in the root or Secure Record is wrong for the owner teams too.
4. **Membership lives in the config, not in the script,** so the guard pins it per file and a memberless file cannot silently gain members (must-fire control).

### Seeding proofs

| Change | Result |
|---|---|
| Guard: the per-role depth check disabled (`if (false && Text(file, "depth") != role.Depth)`) | 7 of 74 `RestrictedContentOwnerRoleGuard` tests fail: the billing must-fire for Global, Local and Basic; the restricted and configuration raised-depth must-fire; both named-break controls. Restored; file hash equal to the backup; rebuilt and 74/74 again |
| Script: the `TEAM PLACE` drift disabled (`$false -and` in its filter) | 2 of 12 Pester tests fail: the team in the root and the team in Secure Record. Restored; file hash equal to the backup |

The Pester must-fire cases (Global, an extra privilege, a member without the marker, a member homed in another customer's unit, a team in the root, a team in Secure Record, a bad config depth) each fail closed against the shipped script; the must-not-fire case (two billing-admin members, one homed in a child unit) passes `-Verify` with exit 0.

### Gates

| Run | Result |
|---|---|
| ArchTests `FullyQualifiedName~RestrictedContentOwnerRoleGuard` | 74/74 (23 new: 18 must-fire documents, 1 script must-fire, the live-file must-not-fire, the named-break control, the member-privilege pin, the reach test) |
| ArchTests (full) | 1974/1974 |
| Pester 5.7.1 `tests/scripts/Set-CustomerOwnerTeams.Tests.ps1` | 12/12 (4 existing, 8 new) |
| Pester 6.2.0 (local) | does not load any container on this machine (`Export-ModuleMember can only be called from inside a module`, also for the untouched `Set-ProductConfigurationReadDepth.Tests.ps1`): the known 6.x problem `ci-standalone-checks.yml` names; CI's job `owner-teams-script-tests` pins 5.7.1, the version run here |
| `-WhatIf` on dev | exit 0, plan above (re-run after the member-unit check: identical output) |
| BFF code | not touched (no publish-size or CVE delta to report) |

### Known limits

- **K: the member check is point in time.** A member who later loses Billing Administrator keeps Read on the rules until someone runs `-Verify` (or H13 re-checks, once provisioning's H7c ships). The owner teams' membership checks work the same way. Not changed here.
- **K: `RetrieveUserPrivileges` counts the marker at any depth and through any team.** A user who holds `prvWritesprk_ruleset` some other way passes the member check. The BFF's own billing-admin check (`CallerHoldsPrivilegeAsync`) answers the same way, so the two agree.
- **K: the root copy of the role exists** (Dataverse creates roles in the root). It is assigned to nobody and `-Verify` reports any assignment of it.

### 089d: uac-r2 review (2026-11-04)

uac-r2 approved 089d (43a5b37051) with two binding notes. Both are to be done in the 089d fix round.

1. **Pin the role's member-privilege inheritance.** Set "Member's privilege inheritance" to Team privileges only (`isinherited` = 0).
   - `-Apply` creates or sets the role with `isinherited` = 0.
   - `-Verify` fails if it is 1.
   - Add a guard test or a Pester case for each of these.
   - Why: the isolation argument depends on the team's Deep being evaluated from the team's business unit.
2. **A live proof is required before the first customer with a child unit relies on this.** A member homed in a child unit must be able to read a top-unit Billing rule, and must NOT be able to read a rule owned by Secure Record Owners.
   - Add this to Phase 7a of the deployment guide and to the provisioning notice.
   - For a proof on dev earlier, a test child unit under BU1 is needed, which is a live change and needs the owner's go.

**Who adds members:** this is an owner question. The proposed default is that an operator adds each child-unit billing admin following guide Phase 7a, and `-Verify` catches mistakes.

### 089d: owner answers and an alternative (2026-11-04)

- **Members (owner):** Billing Admin is a security role explicitly assigned to a limited set of users. An operator adds billing admins by hand; the default stands.
- **Owner's question:** why use a team rather than the role assignment? On dev, `OrgDBOrgSettings.EnableOwnershipAcrossBusinessUnits` is true, so a role can be assigned to a user in another business unit's context.
- **Option R (no team):** assign "Billing Rule Reader" (Read at Deep on `sprk_policy`, `isinherited`=0) directly to each child-unit billing admin, in the customer top unit's context.
  - The isolation argument is the same as for the team.
  - R depends on two things the team does not: the org setting must be on in every customer environment, and the uac-r2 census must model role assignments made in another unit's context.
  - Do not add the Read to the existing Billing Admin role. Re-homing that role's assignment would move all its other privileges as well.
- **Status:** sent to uac-r2. The 089d fix round (adversarial pass 1: the `isinherited` pin, the live-proof gate in the guide, removing the operator straight after the team is created, the file-pattern wording) is held until uac-r2 chooses between the team and R.
- **If R is chosen (provisioning-09 asked):** should provisioning only CHECK `EnableOwnershipAcrossBusinessUnits` (an H0/H13 gate that fails when it is off), or SET it during stamp setup? Setting it is an org-wide Dataverse write on every stamp, so the owner must approve that design. It is not yet known whether the setting is on in ACME.

## 089c - change rule and remove with preview (2026-11-04)

Branch `task/ebilling-089c`, from `work/ebilling-r1` at `1848d67bdb`. Not merged, not pushed. No Dataverse write, no live call, no deploy.

### What landed

- **`ProcedureAssignmentService`** gets four new `public virtual` methods. In each, a read fault before the write is 503 `assignment.read_failed`.
  - **`ChangeRuleAsync(core, record, RuleChangeRequest, callerOid)`**
    - Requires a policy id, a reason and at least one edit (`ruleBody`, `messageTemplate`, `shortHeadline`, `consequence`).
    - Loads the base rule the way Exclude does: an active Billing rule that is not record-scoped and not the system rule.
    - The base is the rule's `sprk_currentversion`. If that is missing, or belongs to another policy, the call is 409 `assignment.rule_not_in_force`.
    - Merges the edits over the base and validates the result with `RuleSetService.ValidateRule` (now `internal`), so errors come back as `errors[]`.
    - Writes in three stages:
      1. The record's record-scoped set. Its id comes from `RecordRuleSetIdFor`: a v5 id per core, record, and firm or all firms.
      2. The record rule, created Off. Its code is `BRR-{id:N}` and its id is `IdOf("ebilling-record-rule-{set:D}-{base:D}")`.
      3. One transaction with three writes:
         - the Exclude row, `AssignmentIdFor(..., ExcludedPolicyColumn, policyId)`, so an Exclude and a change of the same rule collide on the key;
         - the rule's next version: `sprk_basedonversion` = the base version, Accepted, Published, in force now;
         - the switch-on: `sprk_enabled` and `sprk_currentversion`, as a conditional update.
    - An existing set or rule is reused only if it is provably this record's. For the set, `IsTheRecordSet` must hold. For the rule: the same set, a `BRR-` code, active, and every version based on a version of the same base policy. Otherwise the call is 409 `assignment.record_rule_conflict`.
  - **`SubjectOfOverrideAsync`** returns the stored row's Procedure (`sprk_procedures`) or rule (`sprk_policies`), and only for this route's record.
  - **`PreviewRemovalAsync`**
    - A row that is not this record's is 404 `assignment.override_not_found`.
    - For a changed rule it returns:
      - the warning;
      - the number of invoices the caller can see that have an open Signal of the record rule (`SignalClosureService.SelectOpenSignalsOfPolicyAsync` + `AffectedInvoiceCounter`, as the caller);
      - `ProcedureService.AffectedText`.
    - A rights fault gives no count.
  - **`RemoveAsync`**
    - One transaction: a `DeleteRequest` on the row (never a deactivation, per the 089b limit). For a changed rule it also switches the record rule Off, as a conditional update.
    - After the commit, the record rule's open Signals are closed through `ClosePolicySignalsRetiredAsync`.
    - If that close fails, the answer is still 200, with `closeComplete=false` and a message. The rule is Off, so the nightly run closes the rest.
- **`RecordRuleSet`** (public static, in the service file):
  - `ScopeRecord` and `ScopeFirmRecord` moved here (089a K4).
  - `IsRecordScoped`, shared with `RuleSetService`.
  - `RequireOneHome`, the 089a K1 write validator. It refuses:
    - a Procedure and a core lookup together;
    - two core lookups;
    - a record scope without a core lookup, or a core lookup without a record scope;
    - a firm without Firm-record scope, or Firm-record scope without a firm.
- **`RuleSetService`** (089a K1):
  - It now reads the core lookups.
  - `ListAsync` hides record-scoped sets.
  - `LoadRuleSetAsync` treats a record-scoped set as not found, so every mutation and every read by id is 404.
  - `RequireOneHome` runs on both of its set creates.
- **Routes**: one literal chain per core, 15 routes in the file.
  - POST `.../{recordId}/rule-overrides/change` (201), GET `.../overrides/{overrideId}/removal-preview` (200) and DELETE `.../overrides/{overrideId}` (200).
  - Order on each route:
    1. step 0, on the group;
    2. the record filter `ebilling.record.override` (uniform 404);
    3. the privilege filter (403);
    4. `RequireEbillingEnabled`;
    5. on the two removal routes only, `RequireReadOnTheOverridesSubject` (after the kill switch since the pass-1 review, K2).
  - Privilege filter for change:
    - Read on the body's rule (`sprk_policies`), asked as the caller; rule rights are never inferred from Procedure rights;
    - `CreateRuleSet() ∪ DraftRules() ∪ EditRule()`;
    - Write on `sprk_policy`.
  - Privilege filter for preview: `Admin()`. For delete: `Admin()` plus Write on `sprk_policy`.
  - `RequireReadOnTheOverridesSubject` reads the row app-only through `SubjectOfOverrideAsync`, then runs `CallerReadCheck.CheckAsync` as the caller:
    - Refused: 404 `assignment.override_not_found`, the answer of a missing row (pass-1 review K1; it was 403 `insufficient_rights`).
    - A read fault, an unparseable id or no answer: 403 `system_failure`.
    - A row that is not this record's passes through to the service, which answers 404.
  - A caller whose oid is not a GUID gets 401 on every route.
- **Census**:
  - `ProcedureAssignmentService.cs` is listed Routed for `sprk_ruleset`, `sprk_policy` and `sprk_policyversion`, one create each.
  - `PersonBearingWriter`s:
    - `AssignmentCreateAsync` (renamed from `CreateRowAsync`);
    - `RecordRuleSetCreateAsync`;
    - `RecordRuleCreateAsync`;
    - `RecordRuleVersionCreateAsync`.
  - Every builder asks `ForChild(row)` with `CallerObjectId` and `RequestedBy`, and applies the answer in the same member. A refusal writes nothing.
  - New record-scope marker `RecordRuleSet.Scope(Record|FirmRecord)`, with two must-fire cases and one must-not-fire case (`RequireOneHome`, `IsRecordScoped`).
- **Route ledger**: a credited form for `RequireReadOnTheOverridesSubject`. The `GovernedFile` reason covers the new routes. `ExpectedEndpointFileCount` stays 151.
- **Columns**:
  - Every new `ColumnSet` and write was checked against `Entities/sprk_Policy`, `sprk_PolicyVersion`, `sprk_ruleset` and the 080 schema script.
  - `sprk_basedonversion` and `sprk_publishstate` are not in `sprk_ruleset/Entity.xml` yet; neither are `sprk_matter`, `sprk_project` and `sprk_workassignment` on `sprk_ruleset`. They come from `Deploy-EbillingProcedureSchema.ps1` (P5/P6, task 080) and are live on dev, per `080-dev-run-2026-10-21.md`. The core lookups come from 080 P5, so every rule-set read now needs P5 deployed (089c review K5).
  - The firm check still selects `sprk_organizationid` only.

### Section 11

- **Existing:**
  - `RuleSetService` writes rule sets, rules and versions.
  - `SignalClosureService` and `AffectedInvoiceCounter` close and count Signals.
  - The 089b service writes the Exclude row.
- **Extension:** the change and removal reuse all of these: the validator, the closure, the counter, `AssignmentIdFor` and `SendAsync`. The record-scoped writes cannot live in `RuleSetService`, for two reasons:
  - its census waiver covers Procedure sets only (089a K4);
  - a record's rows are secure children, routed through the resolver.
- **Cost of doing nothing:** no record could change a rule (O-4) or undo an override, and 082 would have no record rules to resolve.
- **New surface:**
  - 9 routes, 3 per core;
  - one custom filter, needed because the request does not name the subject;
  - `RecordRuleSet`: the shared constants and the write validator.
  - No package, no background work and no DI registration change; the three new dependencies were already registered.

### Seeding proofs

Each change was restored afterwards, and `cmp` confirmed the file was identical. Suite: `~ProcedureAssignmentEndpoints`, 243 tests.

| Change | Result |
|---|---|
| S1: record filter dropped on the matter change route | 10 failed: the step-0 and uniform-404 must-fires |
| S2: Read on the rule dropped from `ChangeTargets` | 3 failed: "no Read on the rule cannot change it" (3 cores) |
| S3: `Admin()` dropped from `RemoveTargets` | 3 failed: "without the billing-admin privilege a removal is 403" (DELETE, 3 cores) |
| S4: `RequireReadOnTheOverridesSubject` dropped from the matter DELETE | 3 failed: "without Read on what the row overrides, 403 insufficient_rights" (Procedure and rule subjects), plus the fail-closed case |
| S5: step 0 dropped from the group | 60 failed: the step-0 must-fire theory |

### Gates

| Run | Result |
|---|---|
| Build: BFF and Sprk.Bff.Api.Tests | 0 warnings, 0 errors |
| Unit filter `ProcedureAssignment\|OperationAccessPolicy\|RuleSet\|Procedure` | 1008 passed, 0 failed |
| ArchTests filter `RouteAuthorizationGuard\|RecordOwnerAssignmentCensus\|FinanceStepZero\|ProcedureRolePrivilege\|ProcedureRetireSelection` | 172 passed |
| Spaarke.ArchTests (full) | 1953/1953 |
| `dotnet list package --vulnerable --include-transitive` (BFF) | none |
| Publish size (`-c Release`, uncompressed, 192 files each) | See below |

Publish size, no package added:
- **Against a fresh master** (`aef86b89f7`, 136,945,460 B): this branch is 137,238,892 B, **+293,432 B**. Most of that is ebilling-r1 work not yet merged to master.
- **This task alone**, against the base `1848d67bdb` (137,147,136 B): **+91,756 B** (dll +72,704, pdb +18,420).

### 089c pass-1 review (2026-11-04)

Branch `task/ebilling-089c-fix1`, from `work/ebilling-r1` at `0b3abd3e38`. No Dataverse write.

**Fixed**

- **F1: a change of a retired or unpublished rule.** `ChangeRuleCoreAsync` (`ProcedureAssignmentService.cs:503-512`) treated a rule as in force when its current version belonged to it. Retiring a Procedure sets each rule's `sprk_enabled=false` (`ProcedureService.cs:937`) but keeps `statecode` and the version, so a change answered 201 and created an enabled record rule copied from a retired rule. `LoadRuleAsync` (`:942-985`) now also reads `sprk_enabled` and the set's `statuscode` (against `RuleSetService.RuleSetPublished`); the change answers 409 `assignment.rule_not_in_force` unless the rule is enabled, its set is Published and its version is in force. Assign and Exclude accept what they accepted before (pinned by `Exclude_StillAcceptsARuleThatIsNotInForce`).
- **K1: an existence oracle on the removal routes.** Override ids are derived (`AssignmentIdFor`), so 403 for an unreadable subject against 404 for a missing row showed whether an override exists. `RequireReadOnTheOverridesSubject` (`ProcedureAssignmentEndpoints.cs:370-373`) now answers a refused Read with the service's own `OverrideNotFound` through `Map`: the same status, `errorCode` and body shape. A fault or an unanswered question stays 403 `system_failure`.
- **K2: filter order.** On the six removal routes `RequireEbillingEnabled` now runs before `RequireReadOnTheOverridesSubject`, so a switched-off eBilling answers 503 before the row is read. The record and privilege filters keep their order.
- **K5:** the columns bullet above now lists the three core lookups on `sprk_ruleset`.

**Left for 082 (defect and location only; not changed here)**

- **K3 (082 must).** `sprk_excludedpolicy` is Cascade (`scripts/ebilling/Deploy-EbillingProcedureSchema.ps1:589`). Deleting the base rule deletes the Exclude row, which strands an enabled record rule that no API can switch off. Retiring a Procedure also leaves the record rules made from its rules enabled. 082's resolver and the retire count must handle both.
- **K4 (082 must).** The record set's "visible to firms" flag is taken from the first rule changed (`ProcedureAssignmentService.cs:544`, `:1173`). Nothing reads it today, because `LoadRuleSetAsync` hides record-scoped sets. Any later firm-facing reader must not skip that hide.

### Decisions and deviations

1. **A record-scoped set is 404 in `RuleSetService`, not the 409 the 089a list named.** A 409 would tell a caller that a set id exists; the uniform not-found leaks nothing.
   - **Built in the 089e fix round (owner 2026-11-04: "Approve (A)+(B) on all 9 routes"; see "089e fix round" below).** Before that:
   - **089e (owner 2026-11-04: "a more descriptive error is important/useful to the user; also so can be tracked"): STOPPED, unchanged.** The owner's 409 `ruleset.record_scoped` was to be given only to a caller who passes the route's existing Read check on the set or on its core record. No by-id rule-set route has such a check. Each one checks table privileges only (`BillingAdminAuthorization.Admin()` and similar, any depth) and, for a Procedure's set, Read or Write on that Procedure (`RequireRead/WriteOnTheRuleSetsProcedure`). A record-scoped set has no Procedure, so that filter passes it through. A 409 behind the privilege checks alone would tell every billing admin, including one who cannot Read the record, that the record has a changed rule. See "089e" below for the options.
2. **Change also requires Write on `sprk_policy`.** The 089b list named it for removal only, but the change switches the record rule on, just as removal switches it off. This is stricter.
   - **Changed in the 089e fix round (owner 2026-11-04: "Add to role file + apply on dev"):** the role source gains Read on `sprk_procedure` at Deep, so Procedure override removal and preview work for billing admins. See "089e fix round" below. Before that:
   - **089e (owner: "provide edit rights as required"): no role change is needed.** The role source "Spaarke Billing Administrator" already holds `prvWritesprk_Policy` (Basic), and every other privilege the change and removal routes check. The check accepts any depth. The dev root role matches the source exactly (24 privileges, read-only query 2026-11-04). The privilege list is under "089e" below.
3. **The change filter always requires Create on rule set**, because the filter cannot know whether the record's set already exists.
4. **The base is the policy's `sprk_currentversion`, even for a record pinned to an earlier Procedure Version.** 082 resolves pins; the record rule copies the rule in force now.
5. **A failure after stage 1 or 2 can leave an empty set, or a rule that is Off and has no version.** Nothing applies either one, and the next change reuses it. Stage 3 is all or nothing.
6. **An inactive (statecode 1) record-rule policy is 409, not reactivated.** A removal only switches the rule Off, so this case arises only after a manual deactivation.
7. **Record rules have no firm clause text.** A plain removal's preview has no warning and no count.

### Known limits

- **K1: an assignment to a retired Procedure** (089b review 2, unchanged). 082 must ignore it.
- **K2: a change that names the wrong firm is 400 `firmId`.** This applies when the base rule names one firm and the change names another firm or none. 089b's Exclude still lacks this check.
- **K3: the preview count is a snapshot.** Signals raised between the preview and the delete are closed too.
- **K4: the close after a removal is best effort**, and the response body says whether it completed. The nightly run finishes it, because the rule is Off.

### STOP: stale base-code Signals (POML)

The stop fires, as expected. No evaluation closes the open Signals that the base rule raised on the record's invoices:
- `SignalReevaluationJob` evaluates Ontology policies only.
- Billing Signals are written only at submission and by the corrections re-evaluation.

**No close path was added.**
- **Count:** 0 on every record today, because nothing is deployed and no client calls the change route yet. In general it is the number of open base-rule Signals on the record's In Review invoices at the time of the change. The service does not count them.
- **Stricter or looser:** neither in general. It depends on each edit: a body that matches more lines is stricter, and a softer consequence or template is looser. The service does not compare them.
- **Owner choice:**
  - (a) leave them for the reviewer, with a "rule changed" note in the UI; or
  - (b) close them at publish as Superseded, through the existing SignalWriter close, naming the record rule's first Policy Version.
- **Decided 2026-11-04: (b). Built in 089e** (see "089e" below). The change closes them after its commit, best effort, as Superseded through `SignalClosureService.SupersedeInPassAsync`, naming the record rule's version the change put in force. The answer says whether the close completed (`closeComplete`) and how many of those invoices the caller can see. One difference from the removal: **nothing finishes a close that fails.** The nightly run does not evaluate Billing rules, and the base rule stays on. So any finding left open stays open for the reviewer, which is option (a) for those findings, and the message says so.

### Answers to spaarke-prototype questions h-j (`notes/084-prototype-questions.md`)

- **h (removal preview):** `GET /api/finance/{matters|projects|workassignments}/{recordId}/overrides/{overrideId}/removal-preview` returns `{ overrideId, kind, closesRule, warning, affectedText, visibleOpenInvoices }`. The count covers only invoices the caller can see, and is `null` when it could not be counted.
- **i (parameter schema per rule type):** there is no schema route. The editor starts from the base rule's current version (its body, template, headline and consequence) and sends only what it changes. The server validates the merged version with the rule-type validator. An invalid version is 400 with `errors[]` (field and message).
- **j (request shapes):**
  - assign or pin: `POST .../procedure-assignments` `{ procedureId, mode: "FollowLatest"|"Pinned"|"Excluded", pinnedVersionId?, reason? }`;
  - exclude: `POST .../rule-overrides/exclude` `{ policyId, reason, firmId? }`;
  - change: `POST .../rule-overrides/change` `{ policyId, reason, firmId?, ruleBody?, messageTemplate?, shortHeadline?, consequence? }`, answered 201 `{ override, recordRule, closeComplete, affectedText, visibleOpenInvoices, message }` (the last four added by 089e: the base rule's findings closed Superseded);
  - remove: `DELETE .../overrides/{overrideId}`, answered 200 `{ overrideId, kind, ruleSwitchedOff, closeComplete, affectedText, visibleOpenInvoices, message }`.

### Questions for the owner or uac-r2

- The STOP above: choose (a) or (b). **Answered 2026-11-04: (b), built in 089e.**
- Confirm deviation 1 (404, not 409) and deviation 2 (Write on policy for change). **Answered 2026-11-04: 1 wants a tracked 409 for readers (089e stopped it; see below); 2 confirmed, no role change needed.**

### 089c pass-2 review (2026-11-04)

Branch `task/ebilling-089c-fix2`, from `work/ebilling-r1` at `916e2f80ba`. No Dataverse write.

| # | Finding | Disposition |
|---|---|---|
| 1 | **Partly retired Procedure (fixed).** The change's in-force check never read the base set's `sprk_procedure` or that Procedure's status. `ProcedureService.RetireAsync` writes in chunks (`ProcedureService.cs:938-950`), so a failed later chunk leaves the Procedure retired while some of its rules stay `sprk_enabled=true`. | `LoadRuleAsync` (`ProcedureAssignmentService.cs`) takes `readProcedure`, which only the change passes. It reads the set's `sprk_procedure` and that Procedure's `sprk_status`, against `ProcedureEntity.StatusRetired`, the constant `AssignProcedureAsync` refuses a retired Procedure with. `BaseRule.ProcedureRetired` ends the in-force check, whatever the rule's flags say: 409 `assignment.rule_not_in_force`. A fault reading the Procedure throws `ReadFailedException` and is the existing 503 `assignment.read_failed`. A set with no Procedure reads none and behaves as before. Assign and Exclude are unchanged (`Exclude_StillAcceptsARuleOfARetiredProcedure`). |
| 2 | **Removal routes read the row twice (fixed).** `RequireReadOnTheOverridesSubject` passed the request on when its subject lookup found no row, and `PreviewRemovalAsync` / `RemoveAsync` read the row again and acted on whatever was there. | The filter answers a null subject itself with `Map(ProcedureAssignmentService.OverrideNotFound<object>(), http)`, the answer an unreadable subject already gets, so the bytes are identical. The request never reaches the service. Override ids are derived from the subject, so a row created later at that id has the subject that would have been checked. The ledger citations for the filter (`RouteAuthorizationGuardTests.Ledger.cs`) moved to `:364`, `:375`, `:376`. |
| 3 | **The notes record was wrong (fixed).** Merge `85c835dad0` mixed 089d's text into 089c's section. | Reorganised: each item is back under its own task. 089d's gate rows, known limits, uac-r2 review and owner answers follow 089d's Seeding proofs. 089c's gate table has its header, and its Decisions, Known limits, STOP, answers and questions follow it. No text was lost (checked against the merge's two parents) and none was added. |
| 4 | **K1: a rule dropped by a newer Procedure Version (082 must-item and owner question; not changed here).** Such a rule stays enabled and keeps its old current version (`RuleSetService.cs` about `:2195-2196`), so the change still accepts it (`ProcedureAssignmentService.cs` about `:505`). For a record pinned to the old version the rule is still in force. Whether to refuse the change for records that are not pinned depends on 082's pin resolution. | Left for 082. Owner question: should a change of a rule that the Procedure's latest version no longer carries be refused for a record that is not pinned to an older version? |
| 5 | **K2: the Location header of a create route (follow-up).** The change route answers `Location: .../rule-overrides/change/{id}` (`ProcedureAssignmentEndpoints.cs` about `:119`, from `Created`); the resource is `.../overrides/{id}`. 089b's exclude route does the same. | Not changed here. One later change should fix every create route together (assign, exclude and change, three cores each) and pin it with a test. |

**Seeding and pre-fix proofs (each restored; `cmp` confirmed the file identical)**

| Change | Result |
|---|---|
| Item 1: `ProcedureRetired: false` removed from the in-force check | `Change_ARuleOfARetiredProcedure_Is409_WhateverTheRuleFlagsSay`: the retired case failed (status 0, Done instead of 409); the active-Procedure case still passed |
| Item 2: the filter's null subject passes on again (`return await next(context)`), the old behaviour | `Removal_OfAnUnknownRow_IsTheFilters404_AndTheServiceNeverRuns`: 6 of 6 cases failed. The byte-identical test (`Removal_OfAnUnreadableSubject_IsIdenticalToAMissingRow`) passed |

**Tests added or changed**

- `ProcedureAssignmentServiceTests`: a theory (retired Procedure and enabled rule: 409 and no writes; active Procedure: changed); a set with no Procedure is changed and no `sprk_procedure` row is read; a Procedure read fault is 503 with no write; Exclude still accepts a rule of a retired Procedure. The existing `Change_WhenAReadFaults_Is503` now names the record-set read only, because the base rule's set read also selects `sprk_procedure` now.
- `ProcedureAssignmentEndpointsTests`: `Removal_OfAnUnknownRow_IsTheServices404` became `Removal_OfAnUnknownRow_IsTheFilters404_AndTheServiceNeverRuns` (404 `assignment.override_not_found`, and neither `RemoveAsync` nor `PreviewRemovalAsync` is called).

### 089c fix rounds

| Round | Build | Unit (`ProcedureAssignment\|RuleSetService`) | ArchTests (`RecordOwnerAssignmentCensus\|RouteAuthorizationGuard\|ProcedureRolePrivilege\|FinanceStepZero`) | Publish size |
|---|---|---|---|---|
| Fix round 1 (pass-1 review, `task/ebilling-089c-fix1`) | 0 errors | 578/578 | 168/168 | Not measured: no package or registration changed |
| Fix round 2 (pass-2 review, `task/ebilling-089c-fix2`) | 0 warnings, 0 errors | 583/583 (5 new cases) | 168/168 | Not measured: no package or registration changed (`LoadRuleAsync` gained one optional parameter and a Procedure read) |

### 089c fix-2 re-check and close (2026-11-04)

A scoped re-check (Sonnet) of merge 593c70f2b5 found no fix-now finding. Unit tests 583/583, ArchTests `RouteAuthorizationGuard|RecordOwnerAssignmentCensus` 152/152. Known limits, accepted:

- **K2:** a request with no oid to an unknown row now gets 404 from the filter, where it used to get the handler's 401 (`ProcedureAssignmentEndpoints.cs:354-360`). In production this cannot happen: `FinanceAuthorizationFilter` (`:415-423`) answers 401 to a missing oid before any record read, and step 0 refuses a non-GUID oid. The 404 is what every caller with no row gets.
- **K1:** a dangling Procedure reference on a set counts as "not retired" (`ProcedureAssignmentService.cs:977-980`). Dataverse referential integrity makes this unreachable.
- **K1 (test):** the byte-identity test (`ProcedureAssignmentEndpointsTests.cs:708-729`) now compares the filter's 404 with itself. Both answers come from the same `Map(OverrideNotFound<object>())` call, so they stay identical by construction.

**089c is complete:** two adversarial passes, two fix rounds and a scoped re-check. Still open for the owner: the stale-Signals STOP, (a) or (b), and confirmation of deviations 1 and 2. (Answered 2026-11-04; see 089e.)

## 089e - the owner's three follow-ups to 089c (2026-11-04)

Branch `task/ebilling-089e`, from `work/ebilling-r1` at `4a5f78ece0`. POML `tasks/089e-override-followups.poml`. No Dataverse write, no live call, no deploy. The dev reads are listed under item 3.

### Item 1: a change closes the base rule's stale findings as Superseded (built)

- **`ChangeRuleAsync`** now takes the caller's bearer token (before `ct`). All three change routes pass `TokenHelper.ExtractBearerTokenOrNull(http)`.
- **After the stage-3 commit**, `SupersedeBaseFindingsAsync`:
  1. selects the base rule's open findings through the close's own selection (`SignalClosureService.SelectOpenSignalsOfPolicyAsync(base, withBillingSubject: true)`; nothing under Procedures queries `sprk_signal`);
  2. maps each finding to its invoice (directly, or through its line's `sprk_billingevent.sprk_invoice`);
  3. keeps the invoices filed under this record (the core lookup from `InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"]`) that are Active and In Review and, for a firm-scoped change, of that firm (`sprk_vendororg`);
  4. closes them through the existing close, `SignalClosureService.SupersedeInPassAsync(baseCode, recordRuleVersionId, …, outsideScope: _ => true)`. The record is outside the base rule's scope now. **Corrected (pass-1 F3):** the close does not name or record the record rule's version. The version is passed as the close's "current version", but with every target outside scope it decides nothing, and the writer stores the resolution only.
- **The answer** (`RuleChangedView`, additive) carries `closeComplete`, `affectedText`, `visibleOpenInvoices` and `message`. The count is `AffectedInvoiceCounter` over the closed findings, as the caller, so it covers only invoices the caller can Read. Any fault in the selection, the invoice reads or the close is caught: the change stays 201 and `closeComplete=false`, and the message is `ChangeCloseIncompleteMessage`.
- **Shape, and why it differs from the removal.** It is best effort after the commit, as `RemoveAsync` is. Closing first would leave closed findings behind a change that then failed. But **the nightly run does not finish this close.** `SignalReevaluationJob` evaluates Ontology policies only (`SignalReevaluationJob.cs:170`, `:1237`), and the base rule stays on, so its Off/Retired sweep never reaches it. A finding left open stays open until a reviewer resolves it, and the message says so. A guaranteed close needs a stricter shape: either a sweep step that closes base-rule findings on In Review invoices of records with a changed-rule row, or a retry route, because a repeated change is 409 `assignment.duplicate`. Neither is built; this is an owner question.
- **`SignalClosureService.SupersedeInPassAsync`** became `virtual`, so the tests can observe the close, as with the two members 081 made virtual. `SignalClosureServiceDerivationGuardTests` still forbids a BFF subclass.
- **Section 11:** no new public surface beyond four additive response members. The new private helpers reuse the counter's line → invoice and In Review rules. The cost of doing nothing: stale base-rule findings stay open on every changed record's invoices.
- **Tests** (`ProcedureAssignmentServiceTests`, under "Task 089e"):
  - must-fire: findings on the invoice and on a line, including an invoice the caller cannot see, all close Superseded; the count is 2 of 3 (the assertion on the version argument was dropped in the fix round, pass-1 F3);
  - must-not-fire: another record's invoice, another rule, an Approved invoice, an inactive invoice, and a missing invoice stay open;
  - firm-scoped: only that firm's invoices close;
  - a write fault, and a selection fault: 201 with `closeComplete=false` and the message, the change committed.
  - `ProcedureAssignmentEndpointsTests`: the change route hands the service the caller's token and returns `closeComplete`.
- **Seeding proofs** (`~ProcedureAssignmentServiceTests.Change_`, 23 tests; each restored, and the file byte-identical afterwards):

  | Seed | Result |
  |---|---|
  | S1: the close call replaced by "nothing closed" | 4 failed |
  | S2: the record condition dropped (query and in-memory check) | 1 failed (must-not-fire) |
  | S3: the firm condition dropped | 1 failed (firm-scoped) |
  | S4: the In Review condition dropped | 1 failed (must-not-fire) |
- **Known limit (K4), fixed in the fix round:** the selection read all of the base rule's open findings org-wide, then filtered to the record. It now reads the record's invoices first (see "089e fix round").

### Item 1: quality gates

- **code-review** (one pass, no adversarial reviewer, per the brief):
  - **F2, fixed:** `SignalClosureServiceDerivationGuardTests`' doc listed only two virtual members.
  - **K4:** the org-wide selection; see the known limit above.
  - **K4:** with nothing to close, the answer reads "0 open invoices you can see are affected", as the removal's does.
  - **K3:** the test fake mirrors `SupersedeInPassAsync`'s selection rule. The real rule stays pinned by `SignalReevaluationJobTests`.
  - No F1. The catch blocks are deliberate best effort after a commit, not log-and-rethrow. `ProcedureAssignmentService.cs` is large (about 1,660 lines) but cohesive: one record's overrides, and the new helpers serve only the change's close.
- **adr-check:**
  - ADR-001 (Minimal API, no Functions), ADR-002 (no plugin; the close is the server-side owner), ADR-008 (filters unchanged), ADR-010 (no DI or interface change), ADR-013 (no AI dependency), ADR-019 (no new error shape), ADR-028 (the caller token is only passed on to the existing OBO-based counter) and ADR-052 (no background work): all compliant.
  - ADR-038: the tests are unit tests in the existing maintain-class files, and nothing is deleted under a KEEP path.
  - The diff has no Graph, `IMemoryCache`, AI, DI or interface hits.
- **Placement (CLAUDE.md §10):** in the BFF, inside the existing `ProcedureAssignmentService`, the owner of the change route. There is no new endpoint, registration, package or background work.
- **Gates:**

  | Run | Result |
  |---|---|
  | Build (BFF) | 0 warnings, 0 errors |
  | Unit filter `ProcedureAssignment\|RuleSetService\|SignalClosure\|Signal` | 4760 passed, 3 skipped, 0 failed |
  | ArchTests `RouteAuthorizationGuard\|RecordOwnerAssignmentCensus\|ProcedureRolePrivilege\|FinanceStepZero\|SignalClosureServiceDerivation\|ProcedureRetireSelection\|RuleSetRolePrivilege` | 180 passed |
  | Publish size (`-c Release`, `Compress-Archive`, 192 files each, short paths `C:\wt089e{m,a,b}`) | See below |

- **Publish size, no package added:**
  - **Against a fresh master** (`0d4aa87ee0`: 136,945,320 B raw, 41,208,838 B zip): the branch is 137,268,940 B raw and 41,310,785 B zip, **+323,620 B raw (+101,947 B zip)**. Most of that is ebilling-r1 work not yet on master.
  - **This task alone**, against the base `4a5f78ece0` (137,254,820 B raw, 41,304,287 B zip): **+14,120 B raw (+6,498 B zip)**; the dll grows 12,288 B.

### Item 2: a descriptive error for record-scoped rule sets (STOPPED, nothing changed; built in the fix round below)

The brief's condition is that a reader gets the 409 only when they pass "the Read check on that set (or on its core record), whichever the route already checks". No by-id route has either check:

| Route (`RuleSetEndpoints.cs`) | Checks | Record Read on the set or its core? |
|---|---|---|
| GET `/{id}` (`:113-116`) | `Admin()` (privilege, any depth); `RequireReadOnTheRuleSetsProcedure` | No. A record-scoped set has no Procedure, so the filter passes it through (`BillingAdminAuthorization.cs:174`, `:183`) |
| draft, PATCH proposal, accept, reject (`:84-87`, `:126-157`) | privileges; `RequireWriteOnTheRuleSetsProcedure` | No (same pass-through) |
| publish, assignment, visibility (`:168-198`) | privileges only | No |

`LoadRuleSetAsync` (`RuleSetService.cs:2513-2528`) hides the set before anything else. A 409 placed there would answer every billing admin, including one who cannot Read the record, which is the existence leak deviation 1 avoids. **Options for the owner and uac-r2:**
- **(A)** Extend the shared `OnTheRuleSetsProcedure` filter: for a record-scoped set, ask the caller's own Read on the set (`sprk_rulesets`, through `CallerReadCheck`). Readable gets 409 `ruleset.record_scoped`, logged under the code, with a message naming `/api/finance/{matters|projects|workassignments}/{recordId}/overrides` and the record kind. Refused or no answer gets the uniform 404. The filter would also go on publish, assignment and visibility, and the route ledger would change. This is a new auth check on nine routes.
- **(B)** The same, but asking Read on the core record (the record filter's shape).
- **(C)** Keep the 404 and add tracking only: a structured log or metric under a `ruleset.record_scoped` reason when the hide fires, with no change to the answer.
- Recommendation: (A), because the set is the record's secure child and its own Read is the narrowest question. It needs uac-r2's sign-off as an auth change.

### Item 3: Billing Administrator edit rights (no change needed)

Role source `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml`. Every Privilege-path check asks `RetrieveUserSetOfPrivilegesByNames` and accepts **any depth** (`FinanceAuthorizationFilter.cs:248`, `CallerRecordAccessProbe.cs:544`).

| Route and check | Where it is checked | Privilege or right | Depth needed | Source holds |
|---|---|---|---|---|
| change: Read on the body's rule | `ProcedureAssignmentEndpoints.cs:299` (Record path, as the caller) | `prvReadsprk_Policy` | must reach the rule's owner (customer unit; child-unit admins via the 089d team) | Deep (`:24`) |
| change: `CreateRuleSet()` | `:294`; `BillingAdminAuthorization.cs:53-56` | `prvCreatesprk_ruleset`, `prvWritesprk_ruleset` | any | Basic (`:16`, `:30`) |
| change: `DraftRules()` | `:295`; `BillingAdminAuthorization.cs:75-79` | `prvWritesprk_ruleset`, `prvCreatesprk_Policy`, `prvCreatesprk_PolicyVersion` | any | Basic (`:30`, `:14`, `:15`) |
| change: `EditRule()` | `:296`; `BillingAdminAuthorization.cs:59-63` | `prvWritesprk_ruleset`, `prvCreatesprk_PolicyVersion`, `prvWritesprk_PolicyVersion` | any | Basic (`:30`, `:15`, `:29`) |
| change, remove: Write on policy | `:297`, `:308` | `prvWritesprk_Policy` | any | Basic (`:28`) |
| preview, remove: `Admin()` | `:304`, `:306-309`; `BillingAdminAuthorization.cs:49-50` | `prvWritesprk_ruleset` | any | Basic (`:30`) |
| remove, preview: Read on the row's subject, a rule | `:365` (`RequireReadOnTheOverridesSubject`) | `prvReadsprk_Policy` | as for change | Deep |
| remove, preview: Read on the row's subject, a Procedure | `:365`; subject from `ProcedureAssignmentService.cs:870` | Read on `sprk_procedure` | must reach the Procedure | **none**, by design: `ProcedureRolePrivilegeGuardTests` forbids any role a privilege on `sprk_procedure` until uac-r2 task 191 |
| every route: the record (`ebilling.record.override`) | `ProcedureAssignmentEndpoints.cs:87` etc.; `OperationAccessPolicy.cs:224` | Write and AppendTo on the core record | must reach the record | not this role's job: from the user's record roles |

- **Nothing is missing from the billing-admin role for the change or for a changed rule's removal.** No source edit, no guard change, no live role change.
- **Finding (not changed):** removing or previewing a **Procedure** override needs Read on `sprk_procedure`. Until uac-r2 191 no Spaarke role may hold it, so only a System Administrator can remove one. This is consistent with 089b's assign route, which needs the same Read.
- **D-123:** nothing here needs Global, or Deep held in the root. The privilege checks accept Basic, and the one Read that needs reach is the existing Deep Read (EB-D-162). For child-unit admins that reach comes from the 089d team in the customer unit.
- **Dev (read-only, `role` and `roleprivileges` through Dataverse SQL, 2026-11-04):** the root role `810926e0-a97c-44ea-9891-e80dadef73f0`, the source's id, holds 24 privileges. They are identical to the source in name and depth: Write, Create, Append and AppendTo at Basic (mask 1); Read at Deep (mask 4) on the three rule tables; the nine platform defaults at Global. The role also exists in five child units (Spaarke Demo, Spaarke Test 1, Spaarke Dev 1, Secure Record, Spaarke Business Unit 1), the inherited copies Dataverse makes. Nothing differs from the source.

## 089e fix round and item 2 (2026-11-04)

### Owner decisions (verbatim, typed in the main session)

- Item 2: "Approve (A)+(B) on all 9 routes".
- The role: "Add to role file + apply on dev" (the owner applies it on dev; this branch changes the source only).
- Earlier owner wording on item 2: "a more descriptive error is important/useful to the user; also so can be tracked; and (2) yes provide edit rights as required".

### Item 2: rule-set by-id routes authorise the set and its record (built)

The uac-r2 design, binding. `RuleSetEndpoints.RequireAccessToTheRuleSet` runs on every rule-set route that takes a set id, after the privilege filter and the eBilling flag and before the Procedure filter.

- **Routes.** `RuleSetEndpoints.cs` has eight such routes, not nine: draft, GET, PATCH proposal, accept, reject, publish, assignment and visibility. No other endpoint file takes a rule set id. All eight carry the filter.
- **Order** (nothing that differs between "exists" and "does not exist" is read before the set is authorised):
  1. **(A)** The caller's own Read on the `sprk_rulesets` row, through `CallerReadCheck` as the caller, for every set. Anything but Readable is the uniform 404. A set id that does not exist may answer Refused or no answer, so both are 404.
  2. The set's record home, read app-only (`RuleSetService.RecordHomeOfRuleSetAsync`, through `RecordRuleSet.HomeOf`). If the set is not record-scoped, or there is no set, the route continues unchanged. A fault here, after (A), is 403 `system_failure`, as in the Procedure filter.
  3. **(B)** For a record-scoped set, the caller's Read on its core record, through `FinanceAuthorizationFilter.CallerHoldsRightsAsync`, the record routes' own check. No new evaluator. A refusal, a fault, or a set with no single core lookup is the uniform 404. (A) alone is not enough: the Billing Administrator holds Read at Deep on `sprk_ruleset`.
  4. Only then: **409 `ruleset.record_scoped`**. The detail names the record kind and its override routes (`/api/finance/{matters|projects|workassignments}/{recordId}/rule-overrides/change`, `/exclude` and `/overrides/{overrideId}`), with `recordKind`, `recordId` and `overridesRoute` members. It is tracked as a Warning log line carrying the code, the set, the record, the method and the path.
- **The uniform 404** is `RuleSetService.RuleSetNotFound<T>()` rendered by the routes' own `Map`: `ruleset.not_found`, the same bytes the service answers for a missing set. It is byte-identical to a nonexistent set id on each route. The 089c removal filter's 404 has the same shape, but its own code (`assignment.override_not_found`).
- **Procedure-scoped and legacy sets** keep today's behaviour, plus (A). Every existing Procedure-filter test is unchanged and green.
- **The service is unchanged in behaviour.** `LoadRuleSetAsync` still hides a record-scoped set (the backstop for plans and the firm clause read). It now reads through `LoadBillingRuleSetAsync`, which `RecordHomeOfRuleSetAsync` shares.
- **Deviations, for review:**
  - (B) asks Read on the record (`"read"`), as the brief's tests state ("caller can't read the record"). It does not ask the override routes' `ebilling.record.override` (Write and AppendTo). A reader of the record who cannot override it gets the 409, and then the override routes' uniform 404.
  - The vetoes. On `/api/finance` every caller is internal (step 0). The record routes' evaluator for an internal caller is `GetCallerRecordAccessAsync`, Dataverse's answer as the caller, so a Restricted or Secure record the caller cannot see answers no Read. The task 037 vetoes in `AccessibleRecordSetService` serve the external plane and are not on this path. The Restricted test models a record that answers no Read while the set is readable.
- **Section 11.**
  - `RequireAccessToTheRuleSet` (new filter). Existing: the Procedure filter, which sees only a set's Procedure, and the override-subject filter's uniform-404 shape. Extension: not possible, because the Procedure filter reads through `LoadRuleSetAsync`, which hides record-scoped sets by design, and keeping it separate keeps its 403 answers. Cost of doing nothing: the owner's descriptive, tracked error cannot be given without telling a billing admin who cannot see the record that the record has a changed rule.
  - `RecordHomeOfRuleSetAsync`, `RecordRuleSetHome` and `RecordRuleSet.HomeOf`. They are the filter's only read. They live in `RecordRuleSet`, because the census forbids record-scope markers in the waived `RuleSetService.cs`.
  - `RuleSetNotFound<T>`. One definition of the 404 bytes.
- **Tests** (`RuleSetEndpointsTests`, theory over the eight routes, 49 new cases; `RuleSetServiceTests`, one):
  - (A) must-fire: the set is unreadable and its record readable: 404, byte-identical to a missing set; the home is never read; no record question is asked.
  - (B) must-fire: the record is unreadable: 404, identical.
  - Restricted: Read on the set, no Read on the record: 404, identical, and no `ruleset.record_scoped` log line.
  - Fail closed: no answer on the record, or no single core: 404, identical. A fault reading the home: 403 `system_failure`.
  - Readable: 409 `ruleset.record_scoped` with the routes and the kind, logged; the Procedure filter is never asked. The kind is checked for each of the three core tables.
  - Must-not-fire: a set that is not record-scoped reaches the service, and its 404 is the same bytes. A Procedure's set without Read on the set is 404, and the Procedure check is never asked.
  - Service: the home of a record-scoped set, a lookup-only set, no core, two cores, a legacy set, another domain's set, none and an empty id; the reads still hide all four record-scoped shapes.
- **Seeding proofs** (`RuleSetEndpoints.cs` restored byte-identical after each, sha1 `0e68651f…`):

  | Seed | Result |
  |---|---|
  | (B) removed (`if (false && !recordReadable)`) | Restricted theory: 8 of 8 failed |
  | (A) removed (`if (false && setRead != …)`) | unreadable-set theory: 8 of 8 failed |

### Item B: Billing Administrator gains Read on `sprk_procedure` (source only)

- **Added:** `prvReadsprk_procedure` at **Deep** in `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml`. That is the depth of its `sprk_ruleset` Read (EB-D-162).
- **Why:** removing or previewing a Procedure override asks the caller's own Read on that Procedure (`RequireReadOnTheOverridesSubject`). Before this, only a System Administrator passed.
- **`Deploy-EbillingBillingAdminRole.ps1`:** the privilege is now in the wanted set. A plain run adds it at Deep, and lowers it from Global like the other Reads. `-Verify` reports it as a gap when it is missing, at another depth, or missing from a business-unit copy. It is out of the forbidden set; any other privilege on the Procedure tables still stops the run. The script parses with no error. It was not run: no Dataverse write.
- **Guards:**
  - `RuleSetRolePrivilegeGuardTests`: the exact set includes it, with controls for missing, Global and an extra Write.
  - `ProcedureRolePrivilegeGuardTests`: the until-191 rule now allows exactly this one grant (this role, this privilege, Deep). Any other role, depth, privilege or Procedure table is still reported, with must-fire and must-not-fire controls.
  - Seed: the role-source line alone, before the guard edits, failed both guards (2 failed).
- **Not done: the field-mapping task 046 permissions-vs-roles matrix.** It does not exist on this branch or on master. 046 is a POML on `work/field-mapping-server-write-path-r1` (`5450e1a5af`), not built. When 046 builds its matrix, it should list this privilege.
- **For the owner and uac-r2:**
  - `ProcedureRolePrivilegeGuardTests` said the until-191 rule changes only "with uac-r2's agreement". The owner's decision is recorded here; uac-r2 and ontology-r1 still need the notice the POML constraint names.
  - D-123 ("never Deep held in the root") is the existing EB-D-162 position for the role's Reads. This Read follows it. Procedures are internal configuration, not records in the Secure Record unit.

### Item C: adversarial pass-1 findings (all fixed)

- **F2, the close covers distance 0 only.** Kept at distance 0, because 082's `CoreRecordChain` is not on this branch and nothing evaluates record rules yet. The answer is now honest:
  - `message` is always set. On a complete close it is `ChangeCloseCompleteMessage` ("…closed on invoices filed directly on this record. Findings on invoices of the records under it are not closed by this change…"). On an incomplete one, `ChangeCloseIncompleteMessage` also says so.
  - The `RuleChangedView` xmldoc says `closeComplete` covers the record's own invoices.
  - **Hand-off for 082:** supersede-close must select through `CoreRecordChain` and respect nearer rows.
- **F3, "names the record rule's new version" was not true.** The writer stores no version. Corrected in this file, the POML and the xmldoc (`ChangeRuleAsync`, `SupersedeBaseFindingsAsync`, `SupersedeInPassAsync`). The assertion on the no-op version argument was dropped. The writer is unchanged.
- **F3, the write-fault test.** `w.Readable.Add(invoice)` before the act. Seed: counting targets instead of closed findings fails it.
- **F3, the probe mock.** It is constrained to the caller's token (`It.Is<string?>(t => t == Token)`). The assertion inside the callback was removed, because the counter's fault handling would swallow it.
- **K2, no answer from stage 3.** The Exclude row is read back at its derived id. If it is there, the change committed: the close runs and the answer is 201. If it is not there, or the read faults, the 500 stays. Tests cover both (harness: `TransactionCommitsButAnswersUnknown`, `TransactionGivesNoAnswer`, per transaction).
- **K4, the close's reads are now bounded by the record:**
  1. this record's In Review invoices: one query on the core lookup, the state and the firm (more than one page is incomplete);
  2. only those invoices' lines (paged, at most 20 pages a batch);
  3. the base rule's open Signals on those invoices or lines, through the new `SignalClosureService.SelectOpenSignalsOfPolicyOnSubjectsAsync`. It is `OpenSignalsOfPolicyQuery` with an `In` on the subject, so the rows are exactly the org-wide selection's for those subjects. It is virtual for the tests; the derivation guard's doc is updated.

  The selection semantics are unchanged. A finding's invoice is its own when it names one, else its line's. The existing tests are green. New tests: no org-wide Signal read (every `sprk_signal` query carries the subject `In`, and every invoice and line read is bounded), and a record with no In Review invoice reads no Signal.
- **Seeding proofs** (`~ProcedureAssignmentServiceTests.Change_`; `ProcedureAssignmentService.cs` restored byte-identical after each):

  | Seed | Result |
  |---|---|
  | S5: count the targets, not the closed findings | write-fault test: 1 failed |
  | S6: no read-back on no answer | committed-but-no-answer test: 1 failed |
  | S7: the org-wide selection | K4 test and must-fire: 2 failed |
  | S8: no message on a complete close | must-fire (message): 1 failed |

### Fix round: gates

| Run | Result |
|---|---|
| Build (BFF) | 0 warnings, 0 errors |
| Unit filter `ProcedureAssignment\|RuleSet\|SignalClosure\|Signal\|BillingAdmin` (includes the rule-set endpoint tests and `SignalReevaluationJobTests`) | 5043 passed, 3 skipped, 0 failed (first run: 1 failed, `RuleSetServiceConsequenceTests` publish via the endpoint host, which needed Read on its own set; fixed in `61fb52b76b`) |
| ArchTests `RouteAuthorizationGuard\|RuleSetRolePrivilege\|ProcedureRolePrivilege\|SignalClosureServiceDerivation\|ProcedureRetireSelection\|RecordOwnerAssignmentCensus\|FinanceStepZero` | 181 passed |
| Permissions-vs-roles matrix guard (field-mapping 046) | not on this branch (see item B) |
| Publish size (`git archive` short-path trees, `-c Release -r linux-x64`, `Compress-Archive Optimal`, 192 files each) | vs fresh master `0d4aa87ee0` (136,945,320 B raw, 41,208,838 B zip): 137,285,264 B raw, 41,314,415 B zip, **+339,944 B raw (+105,577 B zip)**, mostly ebilling-r1 work not on master. This round alone, vs the branch before it (`49896175a0`: 137,268,940 B raw, 41,310,785 B zip): **+16,324 B raw (+3,630 B zip)**. No package. |

- **ADR notes:** ADR-008 (an endpoint filter for resource authorization); ADR-019 (ProblemDetails with a stable `errorCode`); ADR-028 (every new check is asked as the caller, never app-only; the one app-only read, the set's home, runs only after the caller's Read on the set); ADR-010 (no DI or interface change; the new members are virtual seams in existing concrete classes). No Dataverse write, no live call, no deploy, no role change on dev.
- **Not reviewed yet:** this round has had no adversarial pass of its own. The auth change on eight routes warrants the two passes the auth rule asks for.
