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
- Run `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify` (reads only) after uac-r2 has seen the `-WhatIf`.
- Run the NFR-05 census (`SecureRecordIsolationCensusJob`) once on dev: clause 1 now grades the stored-file privilege, so a human role holding it above Basic is a new finding.

### Overlaps

- `task/ebill-086-storedfile-secure-child` contains the earlier 089 attempt (`task/ebill-089-assignments`, `b9a80f9bd2` and `8f4920ce0c`). A trial merge conflicts in the role file, `SecureBuRoleDepthAssertion.cs`, `SecureChildLineage.cs`, `RecordOwnershipResolver.cs` and `BillingLookupsConferNoAccessTests.cs` (add/add). 086 must be rebased onto 089a, dropping the earlier 089 commits.
- `work/ebilling-r1`: a trial merge is clean.
- `work/unified-access-control-r2`, `task/ebill-081-procedure-service`, master and PR #2153: no shared file.

### K findings and open items

- Only `RuleSetServiceNoMatterScopeGuardTests` keeps `sprk_matter` off policies; it is unchanged.
- The Restrict delete behaviour on the core lookups (uac Q10) is a schema change and is not done here (task 117).
- `sprk_ruleset.sprk_sourcedocument` is not a citation: a record rule set has no source document.
- No pin yet that no non-BFF role holds Create or Write on `sprk_procedureversion` or `sprk_procedureassignment` (089b).
- 089c: record-scoped writers must live in their own Routed file, because the census guard works per file. 089c also decides whether evaluation closes stale base-code Signals.
- The client list has no npm unit test here (no `node_modules`); the ArchTest pins it.
