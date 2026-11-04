# Task 089d (option R) - the Billing Rule Reader role, assigned directly

Branch `task/ebilling-089d-r`, from `work/ebilling-r1` at `9a5fa1bf51`. Not merged into work. **Nothing was written to Dataverse**: the only live call was the read-only `-WhatIf` below. `-Apply` (the role, the org setting if it is off, and any assignment) is the main session's, after the owner's go and the uac-r2 notices.

This file replaces the team design recorded in `089-assignment-override-service.md` ("089d - Billing Rule Readers team"). That file is not edited here.

## Decision

- **Owner, 2026-11-04:** "089d - use direct role approach".
- **Owner:** Billing Admin is a security role, explicitly assigned to a limited set of users.
- **Owner:** "the security roles should be part of the provisioning deployment package - and each customer needs it so however that is accomplished". So provisioning **creates** the role and **sets** `EnableOwnershipAcrossBusinessUnits` during stamp setup. Checking the setting alone is not enough.

### Why the team was replaced

- The team existed only to place the Deep grant in the customer's top unit. With matrix business units (`EnableOwnershipAcrossBusinessUnits`), a user can hold the top unit's **copy** of a role directly, and Dataverse measures the depth from that copy's unit. That gives the same reach without a team. It also removes a membership surface that needed its own checks: members, member units, and taking the creator off the team.
- The isolation argument is unchanged. Deep from the customer top unit covers that unit and its children. Secure Record is a sibling of that unit (both are direct children of the root), so Deep never covers it.
- The team was never applied anywhere: `-Apply` on dev never ran. Nothing has to be removed from an environment.
- The Read is **not** added to Billing Administrator (uac-r2). Assigning that role's top-unit copy would move all its other privileges' depth with it.

### Shape (config/billing-rule-readers-role.json)

- `roleName` "Billing Rule Reader".
- `assignment: users`. There is no `teamName`, no `membership` and no `memberPrivilege`.
- `businessUnit: customer`: the copy assigned is the customer top unit's.
- `access: Read`, `depth: Deep`, `isinherited: 0`.
- `holderPrivilege: prvWritesprk_ruleset`: the billing-admin marker, `BillingAdminAuthorization.WriteRuleSetPrivilege`.
- `requiresOrgSetting: EnableOwnershipAcrossBusinessUnits`.
- One table: `sprk_policy` (`prvReadsprk_Policy`).
- **`sprk_policyversion` is still not listed.** The evidence is unchanged (see the 089 notes, "Does Exclude read `sprk_policyversion`?"). The Exclude route checks the caller's Read on `sprk_policies` only, and reads the version as the application in `ProcedureAssignmentService.IsSystemRuleAsync`.

## uac-r2's five conditions and where each is enforced

| # | Condition | Enforced by |
|---|---|---|
| 1 | The org setting is a provisioning precondition, and `-Verify` checks it. | **Script.** `-Verify` reads `organization.orgdborgsettings` and reports `ORG SETTING` drift (exit 1) when `EnableOwnershipAcrossBusinessUnits` is off or absent. `-Apply` sets it as its own step: it prints `ORG SETTING : writing organization '<name>' (<id>).orgdborgsettings: EnableOwnershipAcrossBusinessUnits '<old>' -> 'true'`, changes that one element, writes every other element back as read, reads back and fails if anything else changed. The dry-run plan lists it first, as "ORG-WIDE WRITE, its own step". **Pester:** off, absent and empty make `-Verify` fail; the dry run plans the step; `-Apply` makes exactly one PATCH to `organizations(<id>)` and the other element survives. **Config and guard:** `requiresOrgSetting` is pinned. |
| 2 | `-Verify` checks five things and fails on any break: only the top unit's copy is assigned; every holder has the marker; every holder is homed in the subtree; no team holds the role; and the grant is Deep Read on `sprk_policy` only. | **Script**, `Get-RoleDrift`. Each break below is an owner decision, so `-Apply` refuses: <br>- `ROLE ASSIGNED ELSEWHERE`: a user holds the root copy, the Secure Record copy (or a copy below it), or a copy below a customer top unit. <br>- `HOLDER PRIVILEGE`: a holder of this unit's copy lacks `prvWritesprk_ruleset` (checked with `RetrieveUserPrivileges`). <br>- `HOLDER UNIT`: a holder is homed outside the customer top unit and its children. That covers the root, Secure Record and other customers. <br>- `ROLE HELD BY TEAM`: a team holds any copy. <br>- `PRIVILEGE DEPTH` / `PRIVILEGE MISSING` / `PRIVILEGE OUTSIDE THE FILE`: these `-Apply` fixes. <br>**Pester:** each break has its own must-fire case, plus a must-not-fire case for a correct setup. **Guard:** the config's depth, table and privilege are pinned. |
| 3 | List the BFF code that judges reach by `systemuser.businessunitid`. | Below ("Condition 3"). No code was changed. |
| 4 | The live proof stays a precondition. | Deployment guide §7.11 ("Live-proof gate") and §12.2 Phase 7a (a `LIVE-PROOF GATE` comment block). This file, under "Gates". The provisioning notice. It was not run: see "Live proof on dev". |
| 5 | Pin `isinherited` = 0. Keep the "no team holds the role" guard. | **Script:** `-Apply` creates the role with `isinherited` 0, or PATCHes it to 0. `-Verify` reports `ROLE INHERITED` when it is not 0, on the root role, or on a copy that differs from it. `Read-Config` refuses a config whose `isinherited` is not 0. **Guard:** must-fire for `isinherited` 1, for it missing, and for it as text. **Pester:** `-Verify` fails, and the dry run plans "set isinherited to 0". **No-team guard:** the config must-fire covers `teamName`, `assignment: team` and `membership`. A script scan (`ScriptTeamHolderFindings`) adds four must-fire cases: `Invoke-ApplyRole` writes a team role, `Invoke-ApplyRole` creates a team, a team-role write outside `Invoke-ApplyTeam`, and `Invoke-ApplyTeam` running without the owner-team filter. A must-not-fire case covers the live script. This also covers the team version's pass-1 F4 finding. |

## Condition 3: BFF code that reads a user's home business unit

Grep of `src/server` (test projects and the provisioning control plane excluded) for `businessunitid` / `_businessunitid_value` / `BusinessUnitId` on systemuser, `WhoAmI`, and comparisons with a record's owning unit. Paths are under `src/server/api/Sprk.Bff.Api/`. Lines are at `9a5fa1bf51`.

**Summary:** none of the code below judges Read reach on `sprk_policy` from the home unit. R does not move the home unit; it only adds a Read grant anchored at the top unit. So R breaks none of it. Nothing was changed.

| Location | What it does with the home unit | Does R break it? |
|---|---|---|
| `Services/Dataverse/RecordOwnershipResolver.cs:4296` | Reads the acting user's `businessunitid` and picks the owner: Secure Record's named team, otherwise the unit's default team. This is **write placement**. | No. The home unit stays the child unit, so the answer is unchanged. |
| `Infrastructure/Dataverse/RecordContainerResolver.cs:2404` | Reads the user's home unit, then its `sprk_containerid`, to choose the SPE container for content that has no record. | No. It reads the home unit only. |
| `Services/Documents/OfficeEditAccessService.cs:198` | Reads the home unit's `sprk_containerid` and compares it with the document's drive for "standing" Office edit. | No. It concerns the SPE container, not `sprk_policy`. |
| `Services/Access/SpeContainerMembershipSync.cs:264`, `:274` | Keeps or removes a user's standing SPE container membership by home unit. Queries users by `businessunitid` In a unit set. | No. A role assignment does not change the home unit. |
| `Services/SpeAdmin/SpeAdminTenantScope.cs:869-881` (used at `:98-111`) | The caller's home unit and its subtree make up the SPE admin scope; the root unit means a platform operator. | No. The scope stays the child subtree. |
| `Api/Reporting/ReportingEndpoints.cs:1013` (`ReadCallerBusinessUnitAsync`, used at `:240`, `:326`, `:729`) | WhoAmI `BusinessUnitId` as the caller decides two things: the catalog subtree check (`:932-953`) and the Power BI RLS identity. | No change in its answer. A report built on `sprk_policy` would still be RLS-keyed to the child unit. That is a reporting design note, not a break. |
| `Services/Ai/Membership/IdentityNormalizationService.cs:271`, `MembershipResolverService.cs:1164` | The home unit goes into `PersonIdentity.BusinessUnitId`, and from there into the AI search ACL filter for unit-typed descriptors. | No. It filters document search, not rule Read. |
| `Services/Ai/Context/UserOrgContextReader.cs:168` | The home unit's name, used for prompt context only. | No. Nothing is decided from it. |
| `Services/ExternalAccess/SecureRecordIsolationCensus.cs:115-125` | Reads `systemuser.businessunitid` for clause 4 ("no user homed in Secure Record"). | No. Grants are anchored at the role copy's unit, so R's grant is anchored at the top unit and does not reach Secure Record (ArchTest below). |

Related, not keyed on the home unit:

| Location | What it does | Does R break it? |
|---|---|---|
| `Services/Dataverse/CallerWriteAuthorization.cs:418-506` | Judges append-to on a `systemuser`/`team`/`businessunit` row from the caller's privilege anchors (`RetrieveUserSetOfPrivilegesByNames`, `UnderOrEqual` at `:495`). | No. It evaluates only the append privilege it is asked about. |
| `Services/Ontology/OntologyAdminClassificationService.cs:448-500` | Reads the caller's `prvReadsprk_Policy` and version grants with each anchor. Coverage needs Global, or Deep anchored at the ROOT. | No. R adds Deep anchored at the top unit, which satisfies neither. |

Correction to the mapper's first pass: it said the census "would flag" a top-unit Deep grant. It does not, because Secure Record is a sibling of the top unit, not below it.

**Doc note for uac-r2 (not edited; it is uac-r2's file):** the remark on `SecureBuRoleDepthAssertion.Reaches` (`Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs:712-714`) says "Dataverse only lets a user hold the copy that lives in their own BU". With matrix business units that is no longer true. The code is right: `AddGrants` anchors at `role.BusinessUnitId`, the copy's unit, not the user's home. Only the remark is stale. The new ArchTest `TheSecureCensus_AnchorsTheCrossUnitAssignmentAtTheCopysUnit_AndReportsOnlyTheRuledOutShapes` pins the behaviour.

## Census

- `SecureBuRoleDepthAssertion` needed **no entry and no edit**. `SecureBuRoleDepthCensusBuilder.Build` already anchors a user's direct grant at the business unit of the role copy (`AddGrants`, `anchorOverride ?? role.BusinessUnitId`). `SecureRecordIsolationCensus` builds each `CensusRole` with the copy's own `businessunitid`.
- `prvReadsprk_Policy` is already in `RestrictedContentPrivileges`, so clause 1 grades it.
- The new ArchTest runs the real `Build` + `Evaluate`, with a human homed in a child unit:
  - holding the top unit's copy at Deep: no clause-1 finding, and the grant is anchored at the top unit;
  - holding the root copy at Deep: a finding;
  - holding the top unit's copy at Global: a finding;
  - holding the child unit's copy: no finding.
- No census or person-writer ArchTest referenced the old team (`git grep` for "Billing Rule Readers" / `BillingRuleReaders` outside this guard found none).

## -WhatIf on dev (2026-11-04, read-only, exit 0)

`scripts/Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitName 'Spaarke Business Unit 1' -WhatIf`:

```
Environment : https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
Config      : ...\config\restricted-content-owner-role.json ('Spaarke Restricted Content Owners', 5 tables)
Config      : ...\config\customer-configuration-owner-role.json ('Spaarke Customer Configuration Owners', 3 tables)
Config      : ...\config\billing-rule-readers-role.json (role 'Billing Rule Reader', assigned to users, 1 tables)
Mode        : DRY RUN (no writes)
Root BU     : Spaarke (06fbf21c-1872-f011-b4cb-7c1e52671ad0)
Secure BU   : Secure Record (d9ec0b6f-80a0-f111-aaac-000d3a99d1d7)
Customer BU : Spaarke Business Unit 1 (cb15f587-baa0-f111-aaac-000d3a99d1d7)
Org setting : EnableOwnershipAcrossBusinessUnits = 'true' on organization 'spaarkedev1' (0c3e6ad9-ae73-f011-8587-00224820bd31)

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

== Billing Rule Reader (billing-rule-readers-role.json)
Declared    : Read at Deep on sprk_policy; isinherited 0; assigned to users only (no team), the Spaarke Business Unit 1 copy, each holder holding prvWritesprk_ruleset and homed in Spaarke Business Unit 1 or below
Role        : (none named 'Billing Rule Reader' in the root business unit)
Holders     : none (no copy of the role exists, so no user and no team holds one)
DRIFT       : ROLE MISSING: no role named 'Billing Rule Reader' in the root business unit.

PLAN (dry run, nothing written):
  1. create the role 'Billing Rule Reader' in the root business unit with isinherited 0 (Dataverse copies it into every child unit; the root copy is assigned to nobody)
  2. add Read at Deep to 'Billing Rule Reader': prvReadsprk_Policy
  3. take off every privilege of 'Billing Rule Reader' outside billing-rule-readers-role.json, including any the platform injects (setup guide 5.4)
  4. read 'Billing Rule Reader' back and check it as -Verify does
  5. (per billing admin, later) for each of Spaarke Business Unit 1's child-unit billing admins, re-run with -AssignBillingRuleReader <systemuserid> (dry run, then -Apply), then -Verify. Do not assign it in the admin UI: that offers the copy in the user's own unit
  Not changed: nothing is written in the root business unit except a role definition the plan creates there (Dataverse creates roles in the root; its root copy is assigned to nobody), and nothing in Secure Record.

DRY RUN: no writes. Re-run with -Apply (owner go), then -Verify.
exit=0
```

Read against the brief:

- **The org setting** is already `true` on dev. `-Apply` would not write it, and the plan has no org-wide step.
- **The role and its privileges:** the role is not there yet. The plan creates it with `isinherited` 0, Read at Deep on `prvReadsprk_Policy`, and nothing else.
- **The root and Secure Record:** the only write in the root is the role definition, which Dataverse requires to be created there. Its root copy is assigned to nobody, and `-Verify` reports any assignment of it. Nothing is written in Secure Record.
- **Current holders:** none. There is no role, so no copy and no holder. The two uac-r2 owner teams show no drift and have an empty plan.
- **No child unit on dev:** `Spaarke Business Unit 1` has no child unit (the 089 notes' dev check), so no billing admin needs the role yet.

## Gates

| Gate | Result |
|---|---|
| ArchTests filter `RestrictedContentOwnerRoleGuard\|RecordOwnerAssignmentCensus\|ProcedureRolePrivilege` | 150 passed, 0 failed |
| `RestrictedContentOwnerRoleGuardTests` alone | 87 passed |
| `tests/Spaarke.ArchTests` (full) | 1998 passed, 0 failed |
| Pester 5.7.1, `tests/scripts/Set-CustomerOwnerTeams.Tests.ps1` | 24 passed, 0 failed |
| BFF code | none changed, so the publish-size check is skipped |

### Before and after proofs

| Change | Result |
|---|---|
| New Pester file against the team-version script (`origin/work/ebilling-r1`) | 0 of 24 pass (the old script refuses the new config and has no direct-role path). The new script was restored, and its hash matches the copy saved before the swap. |
| New guard against the team-version config | 2 of 87 fail: the live-file check and the holder-privilege pin. The config was restored, and its hash matches. |
| **Seed proof:** the live config with `"isinherited": 1` | 1 of 87 fails: `TheLiveBillingRuleReaderFile_HoldsReadAtDeep_OnSprkPolicyOnly_AssignedToBillingAdminsWithNoTeam`. The config was restored, and its hash matches. |

### Live-proof gate (uac-r2 condition 4; kept from the team version's pass-1 F4)

- **The gate.** Before the first customer with a child unit relies on R, prove it live once. A billing admin homed in a **child** unit, holding the **top unit's** copy:
  - **can** Read a Billing rule owned in the top unit. The rule-exclude on `BILL-SYSTEM-SUBMISSION` is no longer 403; that rule is a system rule, so the expected answer is 409 `assignment.system_rule`.
  - **cannot** Read a rule owned by `Secure Record Owners` (still 403).
- **Where it is recorded:** deployment guide §7.11 and Phase 7a, this file, and the provisioning notice.

#### Live proof on dev (described, not run: every step is a live write and needs the owner's go)

1. Create a test child unit under `Spaarke Business Unit 1`, for example `Spaarke Business Unit 1 / 089d probe`.
2. Create or move a test billing admin homed in it, holding Billing Administrator from its own unit's copy.
   - Note: a business-unit move strips roles by default (`DoNotRemoveRolesOnChangeBusinessUnit` is absent on dev), so assign Billing Administrator after the move.
3. Run the script with `-AssignBillingRuleReader <that user> -WhatIf`, then with `-Apply`, then `-Verify`. Expected: `Holders : 1`, homed in the probe unit, marker yes.
4. As that user, call the Exclude route on `BILL-SYSTEM-SUBMISSION`. Expected: 409 `assignment.system_rule`, not 403.
5. As that user, read a rule owned by `Secure Record Owners`. Expected: 403, or 404 from a direct Dataverse read.
   - Dev may have no Secure-owned rule. Making one is a further live write: a Secure record's record rule (the 089c change route).
6. Clean up: take the role off the user, then remove the user and the probe unit. Run `-Verify` again.

Notices to uac-r2 before and after, as for every live change.

### The team version's pass-1 findings

- **F4 (the live-proof gate in the guide):** kept and adapted to R (above).
- **K2 (removing the member straight after the team was created):** no longer applies, because there is no team. `Invoke-ApplyTeam`'s creator removal stays for the two memberless owner teams, as it was before 089d.
- **K3 (the file-pattern wording):** the deployment guide's Phase 7a said "from the same config/*-role.json files ($DeclaredRoleFiles)". That glob also matches `secure-record-owner-role.json` and `product-configuration-read-role.json`. It now names the three files `$DeclaredRoleFiles` lists. `git grep` found the pattern nowhere else.

## Decisions made in this rework

1. **`TEAM PLACE` stays for the two owner teams.** It came with the team version, applies to every declared team, and is not membership logic. Its Pester cases moved to the owner-team Describe.
2. **Another customer top unit's copy, held by users, is INFO, not drift, in this run.** `-Verify` runs once per customer top unit, and dev has four. Failing customer one's run because customer two has billing admins would make `-Verify` unusable in a shared environment. Each top unit's own run checks its holders, so a holder of customer two's copy homed in customer one's units fails customer two's `-Verify` (`HOLDER UNIT`). A **team** holding any copy, and a user holding the root, Secure Record or a lower unit's copy, are always drift.
3. **The assignment helper is all or nothing.** If any user it names fails a check, the run is an owner decision. The dry run exits 1, and `-Apply` writes nothing, not even the org setting or the role. A user who already holds the copy is reported as "nothing to do".
4. **The org setting is written back as the whole `orgdborgsettings` document with one element changed,** then read back and compared element by element. If anything else differs, the run stops.
5. **`Read-Config` refuses `membership` and `memberPrivilege` in any file,** so the team design cannot come back by config alone.
6. **`Set-RolePrivileges` is shared.** The add-then-strip privilege code moved out of `Invoke-ApplyTeam` so the owner roles and the direct role use the same one. No behaviour changed for the owner roles; the Pester owner cases are unchanged and pass.

## Known limits

- **K: the holder check is a point-in-time check.** A holder who later loses Billing Administrator keeps Read on the rules until someone runs `-Verify`, or until H13 re-checks once provisioning's H7c ships. The owner teams' membership checks work the same way.
- **K: `RetrieveUserPrivileges` counts the marker at any depth and through any role or team.** A user who holds `prvWritesprk_ruleset` some other way passes the holder check. The BFF's own billing-admin check (`CallerHoldsPrivilegeAsync`) answers the same way, so the two agree.
- **K: the root copy exists.** Dataverse creates every role in the root and copies it down. The root copy is assigned to nobody, and `-Verify` reports any user who holds it (`ROLE ASSIGNED ELSEWHERE ... ROOT copy`) and any team that holds it (`ROLE HELD BY TEAM`). The Secure census would also report a human holding it, because Deep from the root reaches Secure Record.
- **K: a holder moved to another business unit.** With the defaults (`DoNotRemoveRolesOnChangeBusinessUnit` absent, so false), Learn says a business-unit change removes the user's role assignments ("can remove all security role assignments"). Whether a copy from a **third** unit is removed, as the top unit's copy is for a child-unit user, is not documented (`.claude/agent-memory/researcher/dataverse-user-bu-change-semantics-2026-10-28.md`). The two outcomes:
  - **The assignment is removed.** The user loses the cross-unit Read and gets 403 again, which fails closed. An operator re-runs the helper if the user is still a child-unit billing admin. Billing Administrator is likely gone too, so the marker must be reassigned first.
  - **The assignment is kept.**
    - Moved within the customer's subtree (child A to child B): still correct; `-Verify` passes.
    - Moved out of it (another customer, the root, Secure Record): `-Verify` fails with `HOLDER UNIT`.
    - Billing Administrator removed and not reassigned: `-Verify` fails with `HOLDER PRIVILEGE`.
    - Until someone runs `-Verify`, a user moved out keeps Read on this customer's rules. That is the point-in-time limit above.
    - A user moved **into** Secure Record is also reported by the Secure census (clause 4: no user in Secure Record).
- **K: the org setting's reversibility was not checked.** Learn documents how to turn it on. Whether turning it off later is supported, and what it does to existing cross-unit assignments, is outside this task. Provisioning sets it once; nothing here turns it off.
- **K: `orgdborgsettings` is written as a whole document.** A concurrent change by someone else between the read and the PATCH would be overwritten. The read-back comparison catches a difference only against this run's own read. The window is one call.

## Other things noticed (not changed)

- The guard's `Assert.NotEqual(LiveJson(...), json)` in the must-fire theories cannot fail: `Mutated` re-serialises with `ToJsonString()`, which never equals the file's own text. The same pattern was in the team version and is in the owner-team theories. A real check would compare parsed nodes. Not changed (out of scope).
- The `SecureBuRoleDepthAssertion.Reaches` remark is stale (see Condition 3, "Doc note for uac-r2").

## Draft notice for customer-provisioning-orchestration-r1 (the main session sends it)

> **ebilling-r1 089d, option R: a per-customer role and an org setting, for H7c (#2429).** This replaces the earlier
> "Billing Rule Readers team" notice. There is no team.
>
> 1. **The role.** Create `Billing Rule Reader` once, in the root, with `isinherited` = 0 ("Team privileges only"). Give it
>    Read at **Deep** on `sprk_policy` and nothing else; strip anything the platform injects. The one definition is
>    `config/billing-rule-readers-role.json` (`assignment: users`, `holderPrivilege`, `requiresOrgSetting`). It is the third
>    file in `$DeclaredRoleFiles` in `scripts/Set-CustomerOwnerTeams.ps1`, beside the two owner-team files.
> 2. **The org setting.** Set `EnableOwnershipAcrossBusinessUnits` = true in `organization.orgdborgsettings` during stamp setup
>    (owner 2026-11-04: provisioning sets it; checking it alone is not enough). It is an org-wide write: log it as its own
>    step, change only that element, and read it back. The script's `-Apply` does exactly this.
> 3. **The assignment step.** For each billing admin homed in a CHILD unit of the customer's top unit, assign the **customer
>    top unit's copy** of the role. Do not use the admin UI's default, which is the copy in the user's own unit. First check
>    that the user holds `prvWritesprk_ruleset` and is homed in the top unit or below it. The script does this:
>    `-AssignBillingRuleReader <systemuserid>[,...]` (a dry run, then `-Apply`). Billing admins homed in the top unit itself
>    need nothing. Who is a billing admin is the customer's decision, made per person, so this step may come after the
>    provisioning run.
> 4. **The check (H13, or your gate).** `scripts/Set-CustomerOwnerTeams.ps1 -CustomerBusinessUnitId <id> -Verify` must exit 0.
>    It fails on any of these:
>    - the org setting off;
>    - `isinherited` not 0;
>    - any privilege or depth outside the file;
>    - any team holding any copy;
>    - a user holding the root copy, the Secure Record copy, or a copy below a customer top unit;
>    - a holder of the top unit's copy without the marker, or homed outside the customer's units.
> 5. **The live-proof gate.** Before the first customer with a child unit relies on this, prove it live once. A child-unit
>    billing admin holding the top unit's copy must be able to read a top-unit Billing rule (the Exclude route is no longer
>    403), and must NOT be able to read a rule owned by `Secure Record Owners`.
>
> The customer deployment guide has the interim steps: §7.11 and §12.2 Phase 7a.
