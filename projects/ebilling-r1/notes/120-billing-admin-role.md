# Task 120: Spaarke Billing Administrator role; D-185 (only billing roles read rule sets)

Status: source steps 1-5 done on branch `task/ebill-120-billing-admin-role`, with uac-r2's review changes applied (2026-10-23). **Step 6 (the live dev run) has NOT been run.** An escalation trigger fired on the -WhatIf; see "Escalation" below.

## What changed in source

| File | Change |
|---|---|
| `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml` | New role with the fixed id `810926e0-a97c-44ea-9891-e80dadef73f0`. On `sprk_ruleset`, `sprk_Policy` and `sprk_PolicyVersion` it holds **Read at Global** and **Create, Write, Append and AppendTo at Basic**, plus the 9 platform defaults every Spaarke role export carries. The description says to assign it to users directly, never to a team. |
| `src/dataverse/solutions/SpaarkeMaster/Other/Solution.xml` | One `RootComponent type="20"` for the new role id. This file is not in the POML's list, but every role file has one; without it the role is not part of the SpaarkeMaster package. |
| `Spaarke Basic User.xml`, `Spaarke Core User.xml` | `prvReadsprk_ruleset` (Deep) removed. No other line changed: no `sprk_Policy` or `sprk_PolicyVersion` change (ontology #2298 owns those). |
| `scripts/ebilling/Deploy-EbillingRolePrivileges.ps1` | `sprk_ruleset` dropped from `$ConfigTables`. The header says why (D-185) and that it must never be re-added. |
| `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1` | New live script with -WhatIf, plain and -Verify modes. It sets each privilege's depth from a per-verb table. -Verify and the read-backs compare depths. A privilege held deeper than wanted stops a plain run (exit 2); the script never lowers one. |
| `tests/Spaarke.ArchTests/RuleSetRolePrivilegeGuardTests.cs` | New guard with 3 facts. Its exact set is now name -> depth. |
| `src/server/api/Sprk.Bff.Api/Api/Finance/BillingAdminAuthorization.cs` | Remarks only. The D-185 state replaces "Core and Basic User: Read, Deep", and the depth paragraph names the Basic markers. |
| `projects/ebilling-r1/tasks/120-billing-admin-role-d185.poml` | The D-76/D-108 constraint and its acceptance criterion now carry uac-r2's depths and the reason. |

### uac-r2 review (2026-10-23): markers at Basic, Read at Global

Create, Write, Append and AppendTo on `sprk_ruleset`, `sprk_Policy` and `sprk_PolicyVersion` are held at **Basic**; Read stays at **Global**. The reasons:
- `BillingAdminAuthorization` uses Create and Write only as markers, at any depth (D-108; `EbillingOverridePrivilege.cs:17`).
- `RuleSetService` writes these tables app-only (D-173).
- At Global, a billing admin could create or edit any rule set or policy natively in Dataverse, including Secure-matter record-scoped children, bypassing `RuleSetService`'s invariants (ADR-002).

Seeding proof for the changed guard fact (b): setting `prvWritesprk_ruleset` to Global made fact (b) fail with "missing or at another depth: prvWritesprk_ruleset at Basic (held at Global)" (1 failed, 2 passed). After restoring it, all 3 pass. Fact (a)'s logic did not change, and its seeding proof from the first round still holds.

### Decisions inside the task

1. **Platform defaults on the role.** Dataverse puts 5 plugin-metadata reads on every role it creates: `prvReadPluginAssembly`, `prvReadPluginType`, `prvReadSdkMessage`, `prvReadSdkMessageProcessingStep` and `prvReadSdkMessageProcessingStepImage`. `AddPrivilegesRole` also re-injects the SharePoint four: `prvCreate/Read/WriteSharePointData` and `prvReadSharePointDocument` (docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4). Every Spaarke role export carries these 9, including Spaarke Access Administrator, which was created the same way. The role source includes them so a later export matches. The guard's fact (b) allows exactly those 9 beyond the billing-admin set, and the script's -Verify reports anything else as drift.
2. **Fixed role id.** The script creates the role with `roleid` set to the source id, in the root business unit, under the `MSCRM.SolutionUniqueName: SpaarkeMaster` header. The run stops (exit 2) if a role has that name but another id, or that id but another name or business unit. A risk to watch on the live run: if Dataverse refused a client-supplied `roleid`, the create would fail (exit 1) with nothing written. No such refusal is known for `role`.

## uac check 1: no BFF path reads sprk_ruleset as the caller

The question: after Core User loses Read, would a non-admin get a 403 or an empty result anywhere? The commands below ran on this branch on 2026-10-23, at commit `c49b02b29a`.

**A. Every reference to the table in `src/server`**
```
grep -rnE '"sprk_ruleset"|"sprk_rulesets"|"sprk_rulesetid"|RuleSetEntity\b|sprk_ruleset_|_sprk_ruleset_value' src/server --include=*.cs
```
The hits fall into four groups:
- **`BillingAdminAuthorization.cs`:** only the `"sprk_rulesets"` entity-set argument of `Check(<privilege>, ...)`. These are privilege-held checks (`FinanceAuthorizationCheck.CallerPrivilege`), not row reads, and a billing admin holds the markers.
- **`RecordCreatorPerson.cs:124`:** a table-name list, with no read.
- **`RuleSetService.cs`** (lines 238, 343, 480, 552, 566, 724, 946, 1040, 1102, 1860, 2071, 2104, 2133, 2152, 2343, 2495, 2520) and **`ProcedureService.cs:419`:** all go through `_entities`.
- **`BillingFactLoader.cs`** (lines 204, 285, 336, 341, 349, 381): also through `_entities`.

**B. The client behind those reads is app-only**
- `RuleSetService.cs:358`, `BillingFactLoader.cs:223` and `ProcedureService.cs:193` all declare `private readonly IGenericEntityService _entities`.
- `Infrastructure/DI/GraphModule.cs:145` registers it as `services.AddSingleton<IGenericEntityService>(sp => sp.GetRequiredService<IDataverseService>())`. It is a singleton bound to the service identity, with no per-request caller.

**C. Files that use a caller-identity client AND mention a rule set**
```
for f in $(grep -rlE 'IDataverseUserClient|CallerReadCheck|CallerRecordAccessProbe|MSCRMCallerID' src/server --include=*.cs); do grep -qiE 'sprk_ruleset|ruleset' "$f" && echo "$f"; done
```
Five files match. None reads `sprk_ruleset` as the caller:
- **`BillingAdminAuthorization.cs:193`:** the `CallerReadCheck` there checks `sprk_procedures`. It is the I-P5 Procedure filter.
- **`RuleSetEndpoints.cs:373`:** the `CallerReadCheck` checks `sprk_procedures` (`BillingAdminAuthorization.ProcedureEntitySet`). It hides the Procedure-owned sets the caller cannot Read.
- **`EbillingOverridePrivilege.cs`:** its rule-set mentions are only in comments. Its caller probe asks about the `sprk_ebillingoverride` privilege.
- **`FinanceModule.cs`** and **`EndpointMappingExtensions.cs`:** DI and route mapping only.

**D. OData paths naming the table**
```
grep -rnE "sprk_rulesets\(|expand=[^\"]*sprk_ruleset|LinkEntity\([^)]*sprk_ruleset|AddLink\(\"sprk_ruleset" src/server --include=*.cs
```
No hits: no `$expand` or link-entity into `sprk_ruleset` exists anywhere in the BFF.

**The named paths**

| Path | Evidence | Result |
|---|---|---|
| Review and guidelines DTOs (`RuleSetSummary`, `RuleSetDetail`, `ProposalView`, guideline fields) | `grep -rlnE 'Guideline' src/server/api/Sprk.Bff.Api --include=*.cs` -> `RuleSetEndpoints.cs`, `RuleSetService.cs`, `ProcedureService.cs` (billing); the rest are Scorecard / AI chat / web search, which are unrelated. All three billing files build these DTOs from `_entities` (B). The routes are billing-admin only. | app-only |
| Signal sentence building | `SignalWriter.RenderSentence` (`SignalWriter.cs:1813`) renders `sprk_sentence` at detection time from the facts, called by `BillingFindingSignalMapper.cs:446`. The facts come from `BillingFactLoader` (app-only, B). `SignalReadService.cs:1967` returns the stored `sprk_sentence` and reads no rule set. | stored on the Signal; no ruleset read when shown |
| Record-scoped rule names shown on a record | `grep -nEi 'sprk_ruleset\|RuleSet\|"sprk_policy\|"sprk_policyversion' SignalReadService.cs EarlierInvoiceReadability.cs InvoiceReviewReadService.cs` -> no hits. The rule identity a record shows (`sprk_policycode`, sentence) is written onto the Signal row by `SignalWriter.cs:1779-1789`. The caller-identity files that read the policy tables (`OntologyAdminAuthoringService`, `OntologyAdminClassificationService`, `OntologyAdminRulesService`, `SignalSuppressionService`) are ontology-domain. None of them references `sprk_ruleset` (they are absent from A), and none links to it (D). | no ruleset read |
| `InvoiceReviewReadService` | Caller client `IDataverseUserClient` (`:370`) and `CallerReadCheck` (`:412`). Grep for `sprk_ruleset\|RuleSet` gives no hits. | no ruleset read |
| #2294 generic-sentence path (`SignalReadService`, `EarlierInvoiceReadability`) | Both use `IDataverseUserClient` (`SignalReadService.cs:267`; `EarlierInvoiceReadability.cs:98, 128, 208, 263`). Grep for `sprk_ruleset\|ruleset` gives no hits. The same grep on ontology PR #2303's versions of `SignalReadService.cs`, `EarlierInvoiceReadability.cs`, `SignalCoreRecordAccess.cs` and `SignalReadEndpoints.cs` (test-merged tree, check 2) also gives no hits. | no ruleset read |

**Client side**
```
grep -rnE "sprk_ruleset" src/client src/solutions --include=*.ts --include=*.tsx --include=*.js
```
No hits. `src/solutions/RuleReview` calls the BFF's `/api/finance/rulesets` routes through `authenticatedFetch`.

Conclusion: no BFF or client path reads `sprk_ruleset` as the caller. Core User losing Read changes nothing a non-admin sees through the product. The only visible effect is the Spaarke Administration MDA (below).

## uac check 2: test-merge with ontology PR #2303

- **PR state:** #2303 head `7cb3c2d4b7de5e8c904cbe14b2df6aec35bcedf2`, base master.
- **Role-XML changes in #2303:**
  - Basic User: `-prvReadsprk_Policy` (Deep).
  - Console User: `-prvReadsprk_Policy` and `-prvReadsprk_PolicyVersion` (Deep).
  - Core User: `+prvAppendTosprk_EventType_Ref` (Global).
  - Nothing on `sprk_ruleset`.
- **Method:** I created a throwaway branch from `c49b02b29a` and ran `git merge --no-ff --no-commit` of the PR head. Afterwards I aborted the merge and deleted both temporary branches; nothing was merged into the task branch.
- **Result:**
  - Basic and Console User merged cleanly.
  - One conflict, in `Spaarke Core User.xml:143`. Two adjacent additions collide: `prvAppendTosprk_externalidentifier` (Deep), already on `work/ebilling-r1` from D-90 and not a task-120 line, and #2303's `prvAppendTosprk_EventType_Ref` (Global). The resolution keeps both lines. The same conflict will appear when #2303 meets `work/ebilling-r1` with or without task 120.
  - Task 120's own lines (both `prvReadsprk_ruleset` removals and the new role file) applied without conflict.
- **Neither side re-adds what the other removed:**
  - On the combined tree, grep finds no `sprk_ruleset` privilege in Basic, Core or Console User.
  - It finds no `prvReadsprk_Policy` in Basic.
  - It finds no `prvReadsprk_Policy` or `prvReadsprk_PolicyVersion` in Console.
  - The Billing Administrator file is unchanged (24 privileges).
- **Tests on the combined tree:** `RuleSetRolePrivilegeGuardTests` and `ProcedureRolePrivilegeGuardTests` gave 7 passed, 0 failed (ArchTests built with 0 errors).

## The MDA (Spaarke Administration)

After D-185 is applied live, only System Administrators and holders of Spaarke Billing Administrator see rows in the `sprk_ruleset` views and forms. Core and Basic User lose them. Who gets the role is the owner's decision; the script assigns it to nobody.

## Tests

- `RuleSetRolePrivilegeGuardTests` (ArchTests, KEEP path `tests/Spaarke.ArchTests/**`) has three facts:
  - (a) only Spaarke Billing Administrator holds any `prv*sprk_ruleset`;
  - (b) Billing Administrator holds exactly the D-76/D-108 set at uac-r2's depths, plus only the platform defaults;
  - (c) a must-fire on an inline Core User fixture. The same fixture carries a look-alike `prvReadsprk_rulesetx`, which must not be reported, so there is no separate must-not-fire fact.
  
  The guard reuses `ProcedureRolePrivilegeGuardTests.RoleFiles`.
- **Seeding proofs:**
  - (a), first round: with `prvReadsprk_ruleset` (Deep) re-added to Core User, it failed naming `Spaarke Core User: prvReadsprk_ruleset (Deep)`.
  - (b), this round: see the uac-r2 section.
- `ProcedureRolePrivilegeGuardTests` passes unchanged, with `UntilTask191` still true.
- On 2026-10-23, at commit `c49b02b29a`:
  - ArchTests: 1542 passed, 0 failed.
  - Unit filter `FullyQualifiedName~Finance|FullyQualifiedName~RuleSet|FullyQualifiedName~Procedure`: 3078 passed, 3 skipped (pre-existing), 0 failed.

## Dev runs before the live step (read-only, 2026-10-23, at commit `c49b02b29a`)

### `Deploy-EbillingBillingAdminRole.ps1 -WhatIf` (exit 0)

```
1. Role Spaarke Billing Administrator (810926e0-a97c-44ea-9891-e80dadef73f0)
  WOULD CREATE role 'Spaarke Billing Administrator' with id 810926e0-a97c-44ea-9891-e80dadef73f0 in the root business unit 06fbf21c-1872-f011-b4cb-7c1e52671ad0, in solution SpaarkeMaster
  WhatIf: the role is created under the SpaarkeMaster solution header

3. Spaarke Billing Administrator privileges (Read Global; Create / Write / Append / AppendTo Basic)
  WOULD ADD prvCreatesprk_ruleset          Basic
  WOULD ADD prvReadsprk_ruleset            Global
  WOULD ADD prvWritesprk_ruleset           Basic
  WOULD ADD prvAppendsprk_ruleset          Basic
  WOULD ADD prvAppendTosprk_ruleset        Basic
  WOULD ADD prvCreatesprk_Policy           Basic
  WOULD ADD prvReadsprk_Policy             Global
  WOULD ADD prvWritesprk_Policy            Basic
  WOULD ADD prvAppendsprk_Policy           Basic
  WOULD ADD prvAppendTosprk_Policy         Basic
  WOULD ADD prvCreatesprk_PolicyVersion    Basic
  WOULD ADD prvReadsprk_PolicyVersion      Global
  WOULD ADD prvWritesprk_PolicyVersion     Basic
  WOULD ADD prvAppendsprk_PolicyVersion    Basic
  WOULD ADD prvAppendTosprk_PolicyVersion  Basic

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
  Spaarke Core User      Read=Deep    [loses Read in this run (step 4)]
  Service Reader, Service Writer, Service Deleter, Support User, System Administrator, System Customizer   [platform roles, not Spaarke roles]

would change: 17 (create the role; 15 privileges at the depths above; Core User - prvReadsprk_ruleset)
held / skipped: 1
WhatIf run: nothing was written.
```

The raw output is in the session scratchpad: `t120-whatif-v2.txt`. The first-round output, from before the depth change, is `t120-whatif.txt`.

### `Deploy-EbillingBillingAdminRole.ps1 -Verify` (exit **1**, before the live run)

There are 17 gaps:
- the role does not exist;
- it lacks each of the 15 privileges at its depth;
- `Spaarke Core User still holds prvReadsprk_ruleset (Deep) (D-185)`.

This is the acceptance check "-Verify exits non-zero when Core User still holds prvReadsprk_ruleset". The raw output is `t120-verify-before-v2.txt`.

### `Deploy-EbillingRolePrivileges.ps1 -WhatIf` (exit 0, first round)

It reports `would add: 0` for Core and Basic User. No `sprk_ruleset` line appears anywhere in the output, because the script no longer lists the table.

## Escalation (trigger 2): live Basic User already differs from the source

I compared the live roles on spaarkedev1 (2026-10-22) with the role source at the branch base:

| Role | Live | Source (before task 120) | Differences |
|---|---|---|---|
| Spaarke Basic User | 669 | 671 | Only in the source: `prvReadsprk_ruleset` (Deep) and `prvReadsprk_Policy` (Deep). |
| Spaarke Core User | 785 | 783 | Only live: `prvWritesprk_ebillingoverride` (Global) and `prvAppendTosprk_EventType_Ref` (Global). The first comes from D-142 and is known; it lands with the task 112 export. The second matches #2303's one Core User line, so it was most likely applied live ahead of #2303's merge. |

What this means:
- Live Basic User no longer holds `prvReadsprk_ruleset` or `prvReadsprk_Policy`.
- The Policy removal matches #2303 (ontology task 163), so it was most likely applied live ahead of #2303's merge. That is the POML's STOP condition: "#2298 has already changed the same roles live". The `sprk_ruleset` Read removal on Basic is not in #2303's diff, so someone else removed it live.
- Console User still holds its Policy and PolicyVersion Reads live (#2303 not applied there yet).

Effect on the plan: the live run would remove only Core User's Read, because Basic User's step is a skip. It would also create the role and add its 15 privileges. Nothing here touches the Policy privileges of existing roles.

## Step 6 (not run): exact commands

```
pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1
pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1            # second plain run: expect "changed: 0"
pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1 -Verify    # expect exit 0
```

Defaults: `-EnvironmentUrl` is `$env:DATAVERSE_URL`, else `https://spaarkedev1.crm.dynamics.com`; `-SolutionUniqueName` is `SpaarkeMaster`. Auth is the operator's `az` login.

## Live run on dev (2026-10-23, owner's direct OK in the main session)

Pre-notices went to ontology-r1 and uac-r2. Ontology confirmed it did not change Basic User live, and said go ahead; uac-r2's depth change was taken first.

1. **Plain run** at `8f7aa122a7` (exit 0, `changed: 17`):
   - created `Spaarke Billing Administrator` (810926e0-a97c-44ea-9891-e80dadef73f0) in the root BU, in SpaarkeMaster;
   - added 15 privileges: Read at Global; Create, Write, Append and AppendTo at Basic, on sprk_ruleset, sprk_Policy and sprk_PolicyVersion;
   - removed `prvReadsprk_ruleset` (Deep) from Spaarke Core User;
   - skipped Basic User, which already lacked it;
   - the role is held by 0 users and 0 teams.
2. **Second plain run:** `changed: 0` (idempotent).
3. **-Verify:** the first attempt exited 1 because of a **script bug**, not a live gap.
   - PowerShell variable names are case-insensitive, so step 4's loop variable `$roleName` overwrote the script-level `$RoleName`. Step 6 then flagged the Billing Administrator itself as "NOT ALLOWED (D-185)".
   - The plain run's steps 1–3 run before step 4, so the live privileges were applied correctly.
   - Fixed by renaming the loop variables (`$stripRoleName`, `$watchRoleName`). The re-run of -Verify exits 0: `VERIFY PASS: the task 120 / D-185 state holds.`

**Open, not ebilling's:** who removed `prvReadsprk_ruleset` and `prvReadsprk_Policy` from live Basic User before this run. Ontology didn't, and no ebilling session made a privilege-removal call on that role. The result matches the agreed end state.
