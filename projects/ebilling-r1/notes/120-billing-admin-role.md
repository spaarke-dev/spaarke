# Task 120: Spaarke Billing Administrator role; D-185 (only billing roles read rule sets)

Status: source steps 1-5 done (branch `task/ebill-120-billing-admin-role`). **Step 6 (live dev run) NOT run**, and an escalation trigger fired on the -WhatIf (see "Escalation" below).

## What changed in source

| File | Change |
|---|---|
| `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml` | New role, fixed id `810926e0-a97c-44ea-9891-e80dadef73f0`. Create / Read / Write / Append / AppendTo at Global on `sprk_ruleset`, `sprk_Policy`, `sprk_PolicyVersion`; plus the 9 platform defaults every Spaarke role export carries. Description says: assign to users directly, never to a team. |
| `src/dataverse/solutions/SpaarkeMaster/Other/Solution.xml` | One `RootComponent type="20"` for the new role id. Not in the POML's file list, but without it the role file is not part of the SpaarkeMaster package (every other role file has one). |
| `Spaarke Basic User.xml`, `Spaarke Core User.xml` | `prvReadsprk_ruleset` (Deep) removed. No other line changed (no `sprk_Policy` / `sprk_PolicyVersion` change; ontology #2298 owns those). |
| `scripts/ebilling/Deploy-EbillingRolePrivileges.ps1` | `sprk_ruleset` dropped from `$ConfigTables`; header says why (D-185) and that it must never be re-added. |
| `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1` | New live script: -WhatIf / plain / -Verify. |
| `tests/Spaarke.ArchTests/RuleSetRolePrivilegeGuardTests.cs` | New guard, 3 facts. |
| `src/server/api/Sprk.Bff.Api/Api/Finance/BillingAdminAuthorization.cs` | Remarks only: "Core and Basic User: Read, Deep" replaced by the D-185 state. |

### Decisions inside the task

1. **Platform defaults on the role.** Dataverse puts the plugin-metadata reads (`prvReadPluginAssembly`, `prvReadPluginType`, `prvReadSdkMessage`, `prvReadSdkMessageProcessingStep`, `prvReadSdkMessageProcessingStepImage`) on a role it creates, and `AddPrivilegesRole` re-injects the SharePoint four (`prvCreate/Read/WriteSharePointData`, `prvReadSharePointDocument`) (docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4). Every Spaarke role export, including Spaarke Access Administrator created the same way, carries those 9. The role source includes them so a later export matches; the guard's fact (b) allows exactly those 9 beyond the billing-admin set, and the script's -Verify reports anything else as drift.
2. **Fixed role id.** The script creates the role with `roleid` = the source id, in the root business unit, under the `MSCRM.SolutionUniqueName: SpaarkeMaster` header. A role with that name but another id, or that id with another name / business unit, stops the run (exit 2). Risk to watch on the live run: if Dataverse refused a client-supplied `roleid` the create would fail (exit 1) with nothing written; no such refusal is known for `role`.
3. **No caller-scoped read of sprk_ruleset in src/** (escalation trigger 1, checked 2026-10-22): the BFF reads `sprk_ruleset` only through `IGenericEntityService` (app-only) in `RuleSetService` and `BillingFactLoader`; the billing-admin check is a privilege-held check (`FinanceAuthorizationCheck.CallerPrivilege`), not a row read; the Procedure Record-path checks read `sprk_procedures`, not rule sets; the only client (`src/solutions/RuleReview`) calls BFF routes through `authenticatedFetch`. No PCF, code page or SPA reads the table.

## The MDA (Spaarke Administration)

After D-185 is applied live, only System Administrators and holders of Spaarke Billing Administrator see rows in the `sprk_ruleset` views and forms. Core and Basic User lose them. Who gets the role is the owner's decision; the script assigns it to nobody.

## Tests

- `RuleSetRolePrivilegeGuardTests` (ArchTests, KEEP path `tests/Spaarke.ArchTests/**`): (a) only Spaarke Billing Administrator holds any `prv*sprk_ruleset`; (b) Billing Administrator holds exactly the D-76 / D-108 set at Global plus only the platform defaults; (c) must-fire on an inline Core User fixture (the same fixture carries a look-alike `prvReadsprk_rulesetx`, which must not be reported; no separate must-not-fire fact). It reuses `ProcedureRolePrivilegeGuardTests.RoleFiles`.
- **Seeding proof:** with `prvReadsprk_ruleset` (Deep) re-added to `Spaarke Core User.xml`, fact (a) failed: `src\dataverse\solutions\SpaarkeMaster\Roles\Spaarke Core User.xml: Spaarke Core User: prvReadsprk_ruleset (Deep)` (1 failed, 2 passed). Restored: 3 passed.
- `ProcedureRolePrivilegeGuardTests` passes unchanged (`UntilTask191` still true).
- ArchTests: 1542 passed, 0 failed. Unit filter `FullyQualifiedName~Finance|FullyQualifiedName~RuleSet|FullyQualifiedName~Procedure`: 3078 passed, 3 skipped (pre-existing), 0 failed.

## Dev runs before the live step (read-only)

### `Deploy-EbillingBillingAdminRole.ps1 -WhatIf` (exit 0)

```
eBilling task 120 - Spaarke Billing Administrator; D-185: only billing roles read rule sets
Environment: https://spaarkedev1.crm.dynamics.com   Mode: WHATIF (no writes)

0. Tables
  sprk_ruleset               UserOwned  8 privileges
  sprk_policy                UserOwned  8 privileges
  sprk_policyversion         UserOwned  8 privileges
  sprk_procedure             UserOwned  8 privileges
  sprk_procedureversion      UserOwned  8 privileges
  sprk_procedureassignment   UserOwned  8 privileges

1. Role Spaarke Billing Administrator (810926e0-a97c-44ea-9891-e80dadef73f0)
  WOULD CREATE role 'Spaarke Billing Administrator' with id 810926e0-a97c-44ea-9891-e80dadef73f0 in the root business unit 06fbf21c-1872-f011-b4cb-7c1e52671ad0, in solution SpaarkeMaster
               description: Spaarke billing administrator (ebilling-r1 D-76, D-108, D-185): reviews, edits, drafts and publishes billing rule sets; the only Spaarke role that reads rule sets. Assign it to users directly, never to a team.
  WhatIf: the role is created under the SpaarkeMaster solution header

3. Spaarke Billing Administrator privileges (Global)
  WOULD ADD prvCreatesprk_ruleset          Global
  WOULD ADD prvReadsprk_ruleset            Global
  WOULD ADD prvWritesprk_ruleset           Global
  WOULD ADD prvAppendsprk_ruleset          Global
  WOULD ADD prvAppendTosprk_ruleset        Global
  WOULD ADD prvCreatesprk_Policy           Global
  WOULD ADD prvReadsprk_Policy             Global
  WOULD ADD prvWritesprk_Policy            Global
  WOULD ADD prvAppendsprk_Policy           Global
  WOULD ADD prvAppendTosprk_Policy         Global
  WOULD ADD prvCreatesprk_PolicyVersion    Global
  WOULD ADD prvReadsprk_PolicyVersion      Global
  WOULD ADD prvWritesprk_PolicyVersion     Global
  WOULD ADD prvAppendsprk_PolicyVersion    Global
  WOULD ADD prvAppendTosprk_PolicyVersion  Global

4. Spaarke Core User - prvReadsprk_ruleset
  root role 75e1b627-2ab6-f111-aaad-0022482913fc, 785 privileges; sprk_ruleset: Read=Deep
  WOULD REMOVE prvReadsprk_ruleset (Deep) from the root role (RemovePrivilegeRole)

4. Spaarke Basic User - prvReadsprk_ruleset
  root role 11f93c04-ddf6-f011-8406-7c1e520aa4df, 669 privileges; sprk_ruleset: (none)
  skipped  prvReadsprk_ruleset already absent
  business-unit copies (5) do not hold prvReadsprk_ruleset

5. sprk_Policy / sprk_PolicyVersion on the shared roles (ontology-r1 #2298 owns these; read only)
  Spaarke Core User      sprk_Policy: (none)   sprk_PolicyVersion: (none)
  Spaarke Basic User     sprk_Policy: (none)   sprk_PolicyVersion: (none)
  Spaarke Console User   sprk_Policy: Read=Deep AppendTo=Deep   sprk_PolicyVersion: Read=Deep AppendTo=Deep

6. Roles holding a privilege on sprk_ruleset (root roles; live, before any change)
  Spaarke Core User                  Read=Deep                                          [loses Read in this run (step 4)]
  Support User                       Read=Basic                                         [platform role, not a Spaarke role]
  Service Reader                     Read=Global                                        [platform role, not a Spaarke role]
  System Customizer                  Create=Global Read=Global Write=Global Delete=Global Append=Global AppendTo=Global Assign=Global Share=Global [platform role, not a Spaarke role]
  Service Deleter                    Delete=Global                                      [platform role, not a Spaarke role]
  Service Writer                     Create=Global Read=Global Write=Global Append=Global AppendTo=Global Assign=Global [platform role, not a Spaarke role]
  System Administrator               Create=Global Read=Global Write=Global Delete=Global Append=Global AppendTo=Global Assign=Global Share=Global [platform role, not a Spaarke role]

================ Summary ================
would change: 17
  create role Spaarke Billing Administrator (810926e0-a97c-44ea-9891-e80dadef73f0)
  Spaarke Billing Administrator + prv{Create,Read,Write,Append,AppendTo}sprk_ruleset Global        (5 lines in the raw output)
  Spaarke Billing Administrator + prv{Create,Read,Write,Append,AppendTo}sprk_Policy Global         (5)
  Spaarke Billing Administrator + prv{Create,Read,Write,Append,AppendTo}sprk_PolicyVersion Global  (5)
  Spaarke Core User - prvReadsprk_ruleset
held / skipped: 1
-Verify would report 17 gap(s) now: (the same 17 as the -Verify run below)
WhatIf run: nothing was written.
```

### `Deploy-EbillingBillingAdminRole.ps1 -Verify` (exit **1**, before the live run)

17 gaps: the role does not exist; it lacks each of the 15 privileges at Global; `Spaarke Core User still holds prvReadsprk_ruleset (Deep) (D-185)`. Step 6 lists Core User as `[NOT ALLOWED (D-185, step 4)]`. This is the acceptance check "-Verify exits non-zero when Core User still holds prvReadsprk_ruleset".

### `Deploy-EbillingRolePrivileges.ps1 -WhatIf` (exit 0)

`would add: 0` for Core and Basic User; no `sprk_ruleset` line anywhere in the output (it no longer lists the table).

## Escalation (trigger 2): live Basic User already differs from the source

Live `Spaarke Basic User` (root `11f93c04-...`) on spaarkedev1, 2026-10-22, compared with the role source at the branch base:

| Role | Live | Source (before task 120) | Differences |
|---|---|---|---|
| Spaarke Basic User | 669 | 671 | source only: `prvReadsprk_ruleset` (Deep), `prvReadsprk_Policy` (Deep) |
| Spaarke Core User | 785 | 783 | live only: `prvWritesprk_ebillingoverride` (Global; D-142, known, lands at the 112 export), `prvAppendTosprk_EventType_Ref` (Global; not from ebilling, owner unknown) |

Basic User no longer holds `prvReadsprk_ruleset` nor `prvReadsprk_Policy` live. The Policy removal is ontology-r1's #2298 (their task 163); the `sprk_ruleset` Read was removed by the same hand or another. Either way "#2298 has already changed the same roles live", which is the POML's STOP condition before writing. Console User still holds its Policy and PolicyVersion Reads live (#2298 not applied to it yet).

Effect on the plan: the live run would remove only Core User's Read (Basic User's step is a skip), create the role and add its 15 privileges. Nothing here touches the Policy privileges of existing roles. The operator decides whether to proceed after confirming with ontology-r1 what they changed on Basic User and whether their source change (#2298) will also drop `prvReadsprk_ruleset` from Basic User.xml (a textual overlap with this task's one-line removal there).

## Step 6 (not run): exact commands

```
pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1
pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1            # second plain run: expect "changed: 0"
pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1 -Verify    # expect exit 0
```

Defaults: `-EnvironmentUrl` = `$env:DATAVERSE_URL`, else `https://spaarkedev1.crm.dynamics.com`; `-SolutionUniqueName SpaarkeMaster`. Auth: the operator's `az` login.
