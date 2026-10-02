# Secure Project — Environment Setup Runbook

> **Created**: 2026-08-25 by `unified-access-control-r2` task 046
> **Applies to**: every environment that will hold secure projects (`sprk_project.sprk_issecure = true`)
> **Scope**: the Dataverse *security* configuration only — business unit, owner team, security role.
> Container/SPE provisioning is code (`ProvisionProjectEndpoint`), not setup, and is out of scope here.
> **Verified against**: `spaarkedev1`, 2026-08-25. Every privilege below was determined by experiment
> against live Dataverse, not copied from a design document.
> **Updated 2026-09-30** by `spaarkeai-word-add-in-r1` task 082. The role now also covers CHILD tables, because
> write-path invariant I-6 (task 080) assigns a secure record's children to the owner team. The privilege list
> moved to ONE file, [`config/secure-record-owner-role.json`](../../config/secure-record-owner-role.json), which
> §5, §7 and the NFR-05 census read.
> **Updated 2026-10-01** by `unified-access-control-r2` task 144 (C10 part 1, GitHub #967). Secure records are owned by
> a **NAMED, non-default owner team** (`Secure Record Owners`), no longer by the business unit's **default** team — whose
> membership Dataverse maintains from each user's business unit and which cannot be curated. The business unit must
> also hold **no users**. Provisioning refuses unless both hold; a scheduled census job reports any drift. §4 is the
> cutover; §7 the checks.
> **Updated 2026-10-01** by `unified-access-control-r2` task 133 (C11). Provisioning can no longer lock the creator
> out: share before the move, undo the move if the share cannot be proven, resume a record left team-owned without a
> container. §7a lists every failure state and who recovers it.

---

## 0. Read this first — the configuration below is NOT sufficient on its own

This runbook produces a correctly scoped owner team. It does **not** by itself isolate secure projects,
because isolation also depends on a property this runbook cannot set: **no ordinary user role may hold
`Deep` or `Global` depth on `sprk_project`.**

In `spaarkedev1` as of 2026-08-25 that condition is **NOT met**, and secure projects are therefore
readable by ordinary users. See [§6 Blocking prerequisite](#6-blocking-prerequisite--role-depth) —
**do not report an environment as "secure-project ready" until §6 passes.**

---

## 1. What this configuration is, in one paragraph

A secure record (project, matter or work assignment) is isolated by **ownership**, not by a per-record rule
(Dataverse has no per-record deny). One business unit holds secure records and **holds no users**; a **named,
non-default owner team** in it (`Secure Record Owners`) owns them; the team has **no members of any kind**, so nobody
gains access by owning or by business unit. All human access is by explicit share. The team needs a security role
only because **Dataverse refuses to assign a record to a principal that lacks Read on that entity** — the role exists
to make the team a legal assignment target, nothing more. It is the target for secure records and, since task 080,
for the children filed to them.

**Why a named team and not the business unit's default team (task 144).** Every business unit has a default team, and
Dataverse keeps its membership equal to the users whose business unit it is — it cannot be curated. When the default
team owned secure records, moving any user into the business unit (a Change-BU, or registration given that BU's
name) silently made them a reader of every secure record, and nothing checked. A named team changes membership only
when someone adds a member on purpose. **Why the business unit must hold no users:** a record owned by ANY team in the
business unit sits in that business unit, so a user placed there whose roles carry Business Unit or Deep depth reads
every secure record by depth whoever owns it. Provisioning checks both before it moves anything; the read-only
`secure-record-isolation-census` job re-checks them every 15 minutes and logs CRITICAL on drift.

---

## 2. Prerequisites

| | |
|---|---|
| Rights | A System Administrator in the target environment |
| Auth | `az login` to the tenant, then a token for the environment (below). `pac` is **not** used — its *active profile* may point at a different environment, which is an easy way to configure the wrong org |
| Config | `SecureRecord:BusinessUnitName` and `SecureRecord:OwnerTeamName` in BFF app settings, **or** accept the compiled defaults (`Secure Record` / `Secure Record Owners`). Both are fail-closed lookup keys |

```powershell
# Pin the environment explicitly. Never rely on an ambient/active profile.
$DvUrl = 'https://<org>.crm.dynamics.com'
$tok = az account get-access-token --resource $DvUrl --query accessToken -o tsv
$H = @{ Authorization = "Bearer $tok"; Accept = 'application/json'
        'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'
        'Content-Type' = 'application/json; charset=utf-8' }
$Api = "$DvUrl/api/data/v9.2"

# Confirm you are where you think you are, BEFORE changing anything.
(Invoke-RestMethod "$Api/organizations?`$select=name" -Headers $H).value.name
```

---

## 3. Step 1 — the business unit

The BU is resolved **by name from configuration**, never by GUID (GUIDs differ per environment).

- Default name if `SecureRecord:BusinessUnitName` is unset: **`Secure Record`** — **singular**.
  🔴 **Renamed from `Secure Project` on 2026-09-29** (task 121, D-12 §2). See §3a for the cutover.
  Multiple design documents once said `Secure Projects`; that was wrong and would have failed every
  provisioning call closed with "business unit not found". A test pins the value
  (`DefaultSecureBusinessUnitName_IsTheNameActuallyDeployed`).
- Parent: see §6 before choosing. In dev today it is a child of the root BU, which is part of the
  problem, not the design.
- Leave `businessunit.sprk_containerid` **null** on this BU. The BU-cascade container is *shared*
  storage; a secure project must use its own `sprk_containerid`. A non-null value here is a
  misconfiguration.

```powershell
$buName = 'Secure Record'
$bu = (Invoke-RestMethod "$Api/businessunits?`$select=businessunitid,name,sprk_containerid&`$filter=name eq '$buName'" -Headers $H).value
$buId = $bu[0].businessunitid
"BU $buName = $buId ; sprk_containerid = $($bu[0].sprk_containerid)"   # containerid MUST be null
```

If the BU does not exist, create it (parent per §6). **Do not** create one BU per project — that
mechanism was retired.

---

## 3a. 🔴 The 2026-09-29 rename — `Secure Project` → `Secure Record`

**Why**: the BU holds secure rows of **three** entity types — `sprk_project`, `sprk_matter` and
`sprk_workassignment` all carry `sprk_issecure` — so naming it after one of them described the topology
wrongly. D-12 §2, owner-approved. It happened at that moment because **no customer deployments existed**;
there is no later cheap moment.

**What renamed** (all four, together):

| Artifact | From | To |
|---|---|---|
| Business unit (live Dataverse) | `Secure Project` | `Secure Record` |
| Security role (live Dataverse) | `Secure Project Owner` | `Secure Record Owner` |
| Code default `DefaultSecureBusinessUnitName` | `Secure Project` | `Secure Record` |
| Config section | `SecureProject:` | `SecureRecord:` |

**What did NOT rename, deliberately** — these contain the words "Secure Project" for other reasons:

- **`Secure Project Workspace`** — the external SPA's product name (`src/client/external-spa`). It is
  user-facing and was not part of the decision.
- **"a Secure Project"** as a domain concept — a `sprk_project` with `sprk_issecure = true`. The BU is
  named for the general case; an individual secure project is still a secure project. This is why
  `ProvisionSecureProject`, `CloseSecureProject`, the *Secure Project* toggle and the SPE container
  display name `Secure Project — {name}` are unchanged.
- **`Secure Project Participant`** — a Power Pages web role. Not decided; left alone.

### The cutover is HARD, and here is the order

There is **no transitional dual-accept**. That was considered and rejected: a tolerant name list that
nobody ever prunes is exactly how the earlier `Secure Project` / `Secure Projects` ambiguity arrived.

1. **Rename the live BU** to `Secure Record` (and the role to `Secure Record Owner`).
2. **Deploy the code** carrying the new default.

⚠️ **Between those two steps, provisioning fails closed** with *"business unit not found"* — the correct
direction, but an error that reads like a **missing environment** rather than a rename in flight. If you
see that error and the environment is otherwise healthy, check which half of the rename has landed before
investigating anything else.

If you must eliminate the window, set `SecureRecord:BusinessUnitName` to the *current* live name before
step 1 and clear it after step 2 — the config value always wins over the default, which is why the key
exists.

**Verifying which half you are on:**

```powershell
# Live side: does the new BU exist?
(Invoke-RestMethod "$Api/businessunits?`$select=name&`$filter=name eq 'Secure Record'" -Headers $H).value.Count   # 1 = renamed
# Code side: what does the deployed build default to?
#   -> DefaultSecureBusinessUnitName, pinned by DefaultSecureBusinessUnitName_IsTheNameActuallyDeployed
```

---

## 4. Step 2 — the NAMED owner team (create it; never use the default team)

> **Changed 2026-10-01 (task 144, #967).** This step used to say "the owner team already exists; do not create one"
> and pointed at the business unit's **default** team. That was the defect: the default team's membership is every
> user in the business unit, maintained by Dataverse and impossible to curate. Secure records are now owned by a
> **named, non-default Owner team**, and the default team is retired as an owner.

**The name** is `Secure Record Owners` (owner decision F9), the compiled default of `SecureRecord:OwnerTeamName`. It
is deliberately different from the business unit's own name, which its default team carries — so the two cannot be
confused in MDA or in a census. 🔴 It is a **fail-closed lookup key**: the live team, the config value, the compiled
default (`SecureRecordOwnerTeam.DefaultOwnerTeamName`, pinned by
`DefaultSecureOwnerTeamName_IsTheOwnersChoice_AndNotTheBusinessUnitsOwnName`) and this guide move together, or
provisioning stops with `sdap.provision.secure_owner_team_not_found`.

### 4.1 Read the current state first (read-only)

```powershell
# Must be FALSE. If true, every reassignment shares the record back to its previous owner — the default team.
(Invoke-RestMethod "$Api/organizations?`$select=sharetopreviousowneronassign" -Headers $H).value[0].sharetopreviousowneronassign

# The business unit must hold NO systemusers — enabled or disabled, human or application. Do not "fix" a non-zero
# answer by moving people: relocating users is an owner decision.
(Invoke-RestMethod "$Api/systemusers?`$select=fullname&`$filter=_businessunitid_value eq $buId" -Headers $H).value.Count   # MUST be 0

# The default team, for reference (its membership mirrors the business unit's users, so it must also be 0).
$default = (Invoke-RestMethod "$Api/teams?`$select=teamid,name&`$filter=_businessunitid_value eq $buId and isdefault eq true and teamtype eq 0" -Headers $H).value
$defaultTeamId = $default[0].teamid
```

### 4.2 Create the named team

```powershell
$teamName = 'Secure Record Owners'
$exists = (Invoke-RestMethod "$Api/teams?`$select=teamid&`$filter=_businessunitid_value eq $buId and name eq '$teamName' and teamtype eq 0 and isdefault eq false" -Headers $H).value
if ($exists.Count -eq 0) {
  $body = @{ name = $teamName; teamtype = 0
             'businessunitid@odata.bind' = "/businessunits($buId)"
             description = 'Owns every secure project, matter and work assignment (and the children filed to them). MUST have no members. Not the business unit''s default team.' } | ConvertTo-Json
  Invoke-RestMethod -Method Post "$Api/teams" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
$teamId = (Invoke-RestMethod "$Api/teams?`$select=teamid&`$filter=_businessunitid_value eq $buId and name eq '$teamName' and teamtype eq 0 and isdefault eq false" -Headers $H).value[0].teamid

# MUST be zero, now and forever — of ANY kind, human or application user.
(Invoke-RestMethod "$Api/teammemberships?`$select=systemuserid&`$filter=teamid eq $teamId" -Headers $H).value.Count
```

`teamtype` must be `0` (Owner). An Access team (`1`) cannot own records. Do not add any member — a member reads every
secure record by ownership, and provisioning refuses (`secure_owner_team_has_members`) while one exists.

### 4.3 The cutover, in this order

1. **Assign the `Secure Record Owner` role to the named team** (§5.5 a, with `$teamId` = the named team). Prove an
   assignment works (§7 item 6) before going further.
2. **Deploy the BFF build carrying task 144.** From here provisioning assigns new secure records to the named team, and
   refuses a record still owned by the default team (`sdap.provision.owned_by_other_secure_team`) instead of giving
   it a second container.
3. **Migrate the existing secure rows** with the one-time script. Dry run first — it prints every check and the plan,
   and writes nothing:

   ```powershell
   .\scripts\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl $DvUrl                    # dry run (default)
   .\scripts\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl $DvUrl -Apply `
       -AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument                 # only after the owner accepts the cascade list
   .\scripts\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl $DvUrl                    # second run: the plan must be EMPTY
   ```

   It moves only rows already inside the Secure Record business unit, reads every owner back, and compares each row's
   share count before and after. It refuses `-Apply` if `sharetopreviousowneronassign` is true, if the named team or the
   default team has members, if any user sits in the business unit, if the named team lacks the role, or if a root
   relationship cascades Assign to a child table the owner has not accepted. Secure rows owned OUTSIDE the business unit
   are reported as **not isolated** and never touched — provision them, unsecure them or delete them (an owner decision).
4. **Remove the role from the default team** — once nothing is owned by it, it must not remain a legal owner:

   ```powershell
   $roleId = (Invoke-RestMethod "$Api/roles?`$select=roleid&`$filter=name eq 'Secure Record Owner' and _businessunitid_value eq $buId" -Headers $H).value[0].roleid
   Invoke-RestMethod -Method Delete "$Api/teams($defaultTeamId)/teamroles_association($roleId)/`$ref" -Headers $H
   ```
5. **Verify**: `.\scripts\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl $DvUrl -Verify` (exit 0), the NFR-05
   assertion (§7 item 9), and the impersonated isolation probe (§7 item 7) on a secure project, a secure matter and a
   secure work assignment.

---

## 5. Step 3 — the `Secure Record Owner` role

### 5.1 The privilege set, and why each entry survived

**`Read` at User (`Basic`) depth, on every table listed in
[`config/secure-record-owner-role.json`](../../config/secure-record-owner-role.json), and nothing else.**
That file is the one list. This guide, `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` and the NFR-05
census all read it; do not copy it here. Each entry names its reason and quotes the refusal that forced it.
There are two kinds:

- **`root`**: tables that carry `sprk_issecure` (project, matter, work assignment). Provisioning assigns the
  secure record itself to the team.
- **`child`**: tables whose rows write-path invariant **I-6** (`RecordOwnershipResolver`, record-first) assigns
  to the team when they are filed to a secure record. Examples are a document saved to a secure project, or a
  To Do regarding one. Owner decision C10 part 2 adds communications, events and memos. Added 2026-09-30 by
  `spaarkeai-word-add-in-r1` task 082, because the earlier premise that "nothing assigns children to this
  team" stopped being true with task 080.

The refusal for a missing child privilege reads exactly like the root one: *"Read Privilege Check For Owner
failed … Principal team (…) is missing prvReadsprk_Todo privilege"*.

How the three `root` entries were found (history; the file is the list):

| Privilege | Depth | Why it is here |
|---|---|---|
| `prvReadsprk_Project` | **User (`Basic`)** | **Forced by a recorded failure.** With the role reduced to `Write`-only, Dataverse refused the assignment and named it: *"Principal team (Id=…, teamType=0, privilegeCount=5) is missing prvReadsprk_Project privilege"*. |
| `prvReadsprk_Matter` | **User (`Basic`)** | Same rule, different entity. **Found the hard way**: a first cut of this role covered only `sprk_project`, and assigning a *Matter* to the team failed — the workaround was to pile broad roles (`Spaarke Basic User` etc.) onto the owner team, which is exactly the over-grant this role exists to eliminate. |
| `prvReadsprk_WorkAssignment` | **User (`Basic`)** | Same rule. Included pre-emptively because `sprk_workassignment` also carries `sprk_issecure`; verified by assigning a probe record. |

**Keep the `root` entries in step with metadata.** If a fourth entity gains an `sprk_issecure` column, add it
to the file, or assignment for that type fails and someone will "fix" it by over-granting the team. A new
`child` entry joins the file when a writer starts assigning that table's rows to the team; the file's
`howToExtend` block has the procedure.

```sql
-- entities that can be secured => entities this role must cover
SELECT LogicalName FROM EntityDefinitions WHERE Attributes ANY (LogicalName = 'sprk_issecure')
```
```powershell
foreach ($e in @('sprk_project','sprk_matter','sprk_workassignment','sprk_servicerequest','sprk_document')) {
  $has = (Invoke-RestMethod "$Api/EntityDefinitions(LogicalName='$e')/Attributes?`$select=LogicalName&`$filter=LogicalName eq 'sprk_issecure'" -Headers $H).value.Count -gt 0
  "{0,-22} issecure={1}" -f $e, $has
}
# 2026-08-25: project=True  matter=True  workassignment=True  servicerequest=False  document=False
```

**Everything else was tested and is NOT required** — do not add any of it:

| Not granted | Evidence |
|---|---|
| `Write` | Assignment succeeds without it, stable across 3 consecutive polls. The team is an ownership anchor and never an actor; the BFF application user performs every mutation. |
| `Create` | The team never creates. The BFF app user creates, then assigns. |
| `Delete`, `Append`, `AppendTo`, `Share`, `Assign` | Never exercised by the assignment path. |
| A **child** table that is not in the file | A child table joins the file only when a writer assigns its rows to this team and the refusal has been recorded. ~~Nothing assigns children to this team (see design §5.1d).~~ That stopped being true on 2026-09-30: task 080's I-6 does, for the child tables now in the file. |
| **Any broad role on the owner team** (`Spaarke Basic User`, `Spaarke AI Analysis User`, …) | If assignment fails, the cause is a **missing `Read` on that entity** — add the one privilege, never a whole role. A broad role on the owner team recreates the System-Administrator posture this runbook removes. |
| **Business Unit / `Deep` / `Global` depth** | `User` depth suffices. Any wider depth lets a future team member read beyond what the team owns, and re-opens the exact hole §6 is about. |

> **`User` depth is not a weaker version of `Business Unit` depth here — it is the correct one.** The
> team owns exactly the secure records, so "records this principal owns" already covers 100% of the
> intended scope. Wider depth adds reach without adding capability.

### 5.2 Create it

```powershell
$body = @{
  name = 'Secure Record Owner'
  'businessunitid@odata.bind' = "/businessunits($buId)"
  description = 'Least-privilege role for the Secure Record OWNER team. Exists only so that team can be an assignment target for secure records and the children filed to them (config/secure-record-owner-role.json). MUST NOT be granted to any user or any other team.'
} | ConvertTo-Json
Invoke-RestMethod -Method Post "$Api/roles" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($body))
$roleId = (Invoke-RestMethod "$Api/roles?`$select=roleid&`$filter=name eq 'Secure Record Owner' and _businessunitid_value eq $buId" -Headers $H).value[0].roleid
```

**Create the role in the secure BU, not the root BU.** A role created in the root BU is auto-copied into
every child BU; a role created in a child BU exists only there and can only ever be assigned to
principals in that BU. That containment is a feature — keep it.

### 5.3 Grant the privileges

**Use the script.** It reads the file, resolves the business unit and the role to exactly one each, takes each
table's `Read` privilege from entity metadata, and refuses a name mismatch. It adds only, and reads the role
back afterwards:

```powershell
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl            # dry run: present / missing / outside the file
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl -Apply     # add the missing ones, read back
.\scripts\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl $DvUrl -Verify    # exit 0 = covered; exit 1 names each gap
```

The manual equivalent is below. Resolve each entity's `Read` privilege from **entity metadata** rather than
hard-coding names. The casing follows the schema name (`prvReadsprk_WorkAssignment`, not
`prvReadsprk_workassignment`), which is easy to get wrong by hand:

```powershell
# Run from the repository root. The ONE list:
$securable = (Get-Content config/secure-record-owner-role.json -Raw | ConvertFrom-Json).tables.logicalName
$privList = foreach ($e in $securable) {
    $read = (Invoke-RestMethod "$Api/EntityDefinitions(LogicalName='$e')/Privileges" -Headers $H).value |
            Where-Object { $_.PrivilegeType -eq 'Read' }
    @{ '@odata.type'='Microsoft.Dynamics.CRM.RolePrivilege'; Depth='Basic'
       PrivilegeId=$read.PrivilegeId; PrivilegeName=$read.Name; BusinessUnitId=$buId }
}
$b = @{ Privileges = @($privList) } | ConvertTo-Json -Depth 10
Invoke-RestMethod -Method Post "$Api/roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($b))
```

### 5.4 ⚠️ Strip the privileges the platform injects behind you

**Creating a role silently adds ~9 privileges you did not ask for**, at `Global` depth: SDK-message and
plugin-metadata reads (`prvReadSdkMessage`, `prvReadSdkMessageProcessingStep`,
`prvReadSdkMessageProcessingStepImage`, `prvReadPluginAssembly`, `prvReadPluginType`) and legacy
SharePoint integration (`prvReadSharePointData`, `prvWriteSharePointData`, `prvCreateSharePointData`,
`prvReadSharePointDocument`). `AddPrivilegesRole` **re-injects the SharePoint four** every time it runs.

Two behaviours to know, both verified:

- **`ReplacePrivilegesRole` does NOT remove the SharePoint four.** It clears the SDK/plugin ones and
  leaves those. Do not rely on it for a clean set.
- **`RemovePrivilegeRole` does** — one privilege per call, and the parameter is an **entity reference**
  named `Privilege`, *not* a GUID named `PrivilegeId` (that returns an OData parameter error).

> ✅ **`spaarkedev1` was cleaned on 2026-10-01** by the owner's decision (issue #1046). The role had held **32
> privileges outside the file**: Create/Write/Delete/Assign/Share/Append/AppendTo on project, matter, work assignment
> and document, plus the SharePoint four at Global. Their origin was unrecorded. This strip removed them, leaving
> the 8 Read privileges in the file. Afterwards, team-owned creates on all four of those tables still succeeded
> across 4 polls, against a control refusal that reported `privilegeCount=8`. Record:
> `projects/spaarkeai-word-add-in-r1/notes/082-secure-owner-role.md` §4.1. If the script's dry run ever lists
> anything under "Outside the file" again, run this strip.

```powershell
# Run this AFTER every AddPrivilegesRole call. $keep comes from the ONE list (§5.1), so it never strips a child
# privilege I-6 depends on. The literal three-name list that used to be here would have removed prvReadsprk_Document.
$keep = (Get-Content config/secure-record-owner-role.json -Raw | ConvertFrom-Json).tables.privilegeName   # from the repository root
foreach ($p in (Invoke-RestMethod "$Api/roles($roleId)/roleprivileges_association?`$select=privilegeid,name" -Headers $H).value) {
    if ($keep -contains $p.name) { continue }
    $rb = @{ Privilege = @{ '@odata.type'='Microsoft.Dynamics.CRM.privilege'; privilegeid=$p.privilegeid } } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Post "$Api/roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($rb))
}
```

### 5.5 Assign to the team, then remove System Administrator — in that order

Keep the working configuration until the new one is proven. `$teamId` is the **named** owner team from §4.2 — never
the business unit's default team (task 144).

```powershell
# a) assign the new role (to the NAMED team)
$ref = @{ '@odata.id' = "$Api/roles($roleId)" } | ConvertTo-Json
Invoke-RestMethod -Method Post "$Api/teams($teamId)/teamroles_association/`$ref" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($ref))

# b) prove assignment works (see §7), THEN remove System Administrator
$sysAdmin = (Invoke-RestMethod "$Api/roles?`$select=roleid&`$filter=name eq 'System Administrator' and _businessunitid_value eq $buId" -Headers $H).value[0].roleid
Invoke-RestMethod -Method Delete "$Api/teams($teamId)/teamroles_association($sysAdmin)/`$ref" -Headers $H

# c) re-prove assignment AFTER the removal. (b) proves nothing on its own —
#    a System Administrator team can never be denied.
```

> A newly created BU's default owner team may arrive holding **`System Administrator`** (it did in
> `spaarkedev1`). A memberless team hides this, but it is one membership row from full administrative
> rights over the organisation. Removing it is the point of this runbook.

---

## 6. Blocking prerequisite — role depth

**This is the part that is easy to miss and it is the part that matters most.**

A child business unit isolates nothing from a principal holding **`Deep`** depth at an **ancestor** BU.
`Deep` ("Parent: Child Business Units") reaches every descendant. So if ordinary users sit in the root
BU with `Deep` on `sprk_project`, and the secure BU is a child of root, **every secure project is
readable by every ordinary user** — silently, with no error and nothing in the role that mentions the
secure BU.

Census the depths (`privilegedepthmask`: `1` Basic/User · `2` Local/BU · `4` **Deep** · `8` **Global**):

```sql
SELECT role.name, role.businessunitid, roleprivileges.privilegedepthmask
FROM roleprivileges
JOIN privilege ON roleprivileges.privilegeid = privilege.privilegeid
JOIN role      ON roleprivileges.roleid      = role.roleid
WHERE privilege.name = 'prvReadsprk_Project'
ORDER BY roleprivileges.privilegedepthmask DESC
```

**Pass condition**: no role held by a **non-administrator human** shows `4` or `8`. `Global` on
service/application-user roles (`Service Reader`, `Service Writer`, `System Customizer`) is acceptable
provided no human holds them — check with `systemuserroles`.

**In `spaarkedev1` this FAILED while ordinary users sat in the root BU**: `Spaarke Basic User` holds
`prvReadsprk_Project` at `Deep`, and `Test User 1` — an ordinary user then in root — read a real secure
record. Two fixes, either of which closes it (design §5.1a-2 has measured blast radius):

- **A — get users out of the root BU** (the decided direction, design §5.2): put users in a BU that is a
  **sibling** of the secure BU, not an ancestor of it. `Deep` then covers the user's own subtree and
  cannot reach the secure BU.
- **B — narrow the depth**: `Spaarke Basic User` `prvReadsprk_Project` `Deep` (4) → `Local` (2).
  Cheap and reversible, but a role guarantee, so a later role edit can silently undo it.

> ✅ **Fix A is VALIDATED in `spaarkedev1` (operator test, 2026-08-25).** `Spaarke Business Unit 1` was
> created as a child of root, `testuser1@spaarke.com` was moved into it, and the user **kept** `Deep`
> depth. Observed:
>
> | Test | Result |
> |---|---|
> | Read a Matter owned by `Spaarke Business Unit 1` | ✅ visible |
> | Read anything in **other** BUs | ✅ not visible |
> | Read the same Matter after reassigning it to the secure BU/team (named `Secure Project` when this was observed; `Secure Record` since 2026-09-29) | ✅ **DENIED** |
> | Read it after an explicit **share** | ✅ visible |
>
> That is the whole intended model working: `Deep` at a sibling BU cannot reach the secure BU, and
> access returns only via explicit share. **It also makes FR-28's share→read assertion testable** —
> previously impossible, because every human with `sprk_project` Read held `Deep` at the root.
>
> ⚠️ **Migration caveat, and it is not cosmetic.** Existing records (projects, matters, documents…)
> were **left in the root BU**, so the relocated user can no longer see any of them. Moving users out of
> root is therefore not a pure security change — it is a **data-migration** decision: either reassign
> existing records into the new BUs, or place users to match where their records already live. Plan this
> before doing it in a populated environment.

**Do NOT "fix" this by removing `sprk_project` Read from ordinary roles.** A share confers nothing
unless the user also holds the entity privilege at some depth — stripping it disables all sharing,
including the explicit shares secure projects depend on. **Narrow the depth; never remove the
privilege.**

---

## 7. Verification checklist

Run all of it. Items 6–7 are the ones that actually test the security property; 1–5 only confirm the
configuration is shaped correctly.

| # | Check | Expected |
|---|---|---|
| 1 | Role privileges: `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Verify` | **Exits 0**: `Read` at depth `1` on every table in `config/secure-record-owner-role.json` (9 as of 2026-10-01: task 145 added `sprk_invoice`, so `spaarkedev1` exits **1** naming it until the §5.3 `-Apply` + §5.4 strip are run). Its "Outside the file" list should be **empty** — it is in `spaarkedev1` since the 2026-10-01 strip (§5.4); after any `-Apply` it shows the re-injected SharePoint four until the strip runs again |
| 2 | Team roles of the **named** team: `teams(<id>)/teamroles_association` | **exactly 1** — `Secure Record Owner`. **No `System Administrator`, and no broad role** (`Spaarke Basic User` etc.) |
| 2b | Assignment works for **each** table in the file, not just projects | assign a probe of each: the `root` tables **and** the `child` tables (e.g. a `sprk_todo` created with `ownerid@odata.bind → /teams(<teamId>)`). A role covering only some types fails silently on the others until someone over-grants the team. Delete the probes |
| 3 | Named team members: `teammemberships?$filter=teamid eq <id>` | **0 — of any kind**, human or application user (task 144). Provisioning refuses otherwise |
| 3b | **Users in the Secure Record BU**: `systemusers?$filter=_businessunitid_value eq <buId>` | **0** — enabled or disabled, human or application (task 144). A user there reads every secure record by depth; provisioning refuses otherwise. Moving anyone out is an owner decision |
| 4 | Role holders: `roles(<id>)/systemuserroles_association` | **0 users** |
| 5 | Role holders: `roles(<id>)/teamroles_association` | **exactly 1 team — the NAMED owner team**. The business unit's **default** team must NOT hold it (task 144; §4.3 step 4) |
| 5b | `organization.sharetopreviousowneronassign` | **false** — otherwise every assignment into the team shares the record back to its previous owner |
| 6 | **Assignment works** — create a probe `sprk_project` with `sprk_issecure=true`, `PATCH ownerid@odata.bind → /teams(<teamId>)` | succeeds, **and** `owningbusinessunit` flips to the secure BU. Must be re-run **after** removing System Administrator |
| 7 | **🔴 Isolation works** — impersonate a known non-admin user (`MSCRMCallerID: <userid>`) and `GET` the probe record. Since task 144: run it on a secure **project, matter AND work assignment** | **DENIED** on each. A successful read means §6 has not been satisfied |
| 8 | Delete the probe record | no `sprk_issecure=true` rows remain |
| 9 | **NFR-05 assertion** — `SecureBuRoleDepthAssertionTests` live test with `SPAARKE_NFR05_DATAVERSE_URL` set (and `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` when only `az login` is available) | **passes**: no non-administrator human reaches the BU by depth on project/matter/work assignment, the BU holds no users, the named team resolves with no members, it alone holds the role, and the role holds Read at User depth on every table in `config/secure-record-owner-role.json` (clause 5, task 145 — a gap names the table and means that table's secure rows cannot be owned by the team) |
| 10 | **Cutover complete**: `scripts\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl $DvUrl -Verify` | **exit 0** — every secure row owned by the named team, none by the default team and none outside the BU |
| 11 | The BFF's `secure-record-isolation-census` job (`/api/admin/jobs/secure-record-isolation-census/status`) | last run `isolated`. Each exposure is a CRITICAL log line `[SECURE-CENSUS]` naming the principal; a clause-5 coverage gap is an ERROR line naming the table (fail-closed, not an exposure); it writes nothing |

### ⚠️ Privilege caching will lie to you

**Dataverse's principal-privilege cache lags role edits by roughly one operation.** A single probe
immediately after a privilege change can report the *previous* configuration. During task 046 this
produced a false "assignment allowed with zero privileges" — a result that, taken at face value, would
have justified shipping a role that grants nothing.

Two defences, use both:

1. **Re-probe until the outcome is stable across ≥3 polls** (~20 s apart).
2. **Cross-check the denial message.** It reports `privilegeCount=N` for the principal; compare it to
   the role's real privilege count. Matching means current, mismatched means stale.

A control run is worth the minute it costs: strip the privilege entirely and confirm you get a
**denial**. If a role with no privileges still allows the assignment, every other reading in that
session is void.

---

## 7a. Provisioning failure states and recovery (task 133)

Provisioning (`POST /api/v1/external-access/provision-project`) is the only thing that moves a secure record into the
team. The owner's rule (session 27, S5): **a secure record must always keep at least one person who can open it.** So
the creator's share is issued BEFORE the owner move, proven after it, and if it cannot be proven the move is undone
(read back). A record owned by the team with **no container recorded** is not refused — the next call **resumes** it:
it shares to the person who created the record — its `createdby` user when that is a usable person, otherwise the
person the BFF recorded in `sprk_createdbyperson` (§7b; an Office quick-created record's `createdby` is the BFF app user)
— never to the caller instead, then creates and records the container. A record owned by the team **with** a container
recorded is provisioned: 409, nothing written.

**A container already on a not-yet-secured record is never orphaned** (task 133 b2; live 2026-10-02 provisioning
`65a3fab2` created a second container and left its own referenced by nothing). Before any write the recorded
`sprk_containerid` is classified: a business unit's shared container, or one this BFF is configured to use for many
records (`Communication:ArchiveContainerId`, `EmailProcessing:DefaultContainerId`, `Email:DefaultContainerId`,
`SharePointEmbedded:StagingContainerId`), is **replaced** by the record's own — its owner keeps pointing at it; one that
another project, matter or work assignment also records is **refused**; anything else is the record's **own** and is
**kept** — no container is created and the value is not rewritten.

A resume never widens the record's access list on the caller's say-so: named colleagues (`sharePrincipalIds`) are
accepted only when the caller IS the record's creator — anyone else is refused before any write and adds people through
Manage Access. And when neither `createdby` nor `sprk_createdbyperson` names a usable person (absent, disabled, an
application user — or one of them cannot be read), the resume is refused — no share, no container — whoever else may hold
a share; the row below names the recoveries that work.

`sprk_issecure` is never written by provisioning. A secure-requested record that failed stays flagged, so uploads to it
are refused until it has its own container (fail closed).

| `reasonCode` (`sdap.provision.*`) | State the record is left in | Who recovers, and how |
|---|---|---|
| `secure_bu_not_found`, `secure_bu_ambiguous`, `secure_owner_team_not_found`, `secure_owner_team_ambiguous`, `secure_owner_team_has_members`, `secure_owner_team_membership_unreadable`, `secure_bu_has_users`, `secure_bu_users_unreadable`, `container_type_not_configured` | Unchanged — refused before any write | Administrator fixes the environment (§3–§5, `SharePointEmbedded:ContainerTypeId`), then provisioning is called again |
| `legacy_per_project_bu` | Unchanged | Administrator migrates the record off its per-project BU (manual) |
| `already_provisioned` | Provisioned: owned by the team, container recorded | Nothing to do |
| `owned_by_other_secure_team` | Owned by the retired default team | Administrator runs `scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1` (§4.3) |
| `creator_unresolved` | Unchanged — refused before any write | **The same caller** calls again (the wizard's "Try securing again") |
| `record_owner_unreadable` | Unchanged — refused before any write. The row was read with no owning user or team, which is deterministic for that row | **An administrator** checks the record's owner in Dataverse; calling again before that repeats the refusal (not offered as a retry) |
| `resume_colleagues_not_permitted` | Unchanged — refused before any write. A resume request named colleagues (`sharePrincipalIds`) and its caller is not the record's creator | The caller calls again **without** `sharePrincipalIds` (the resume then completes, sharing only to the creator), and adds people through Manage Access |
| `container_shared_with_another_record` (HTTP 409; `otherRecordType` — the other record's id is in the `[PROVISION]` log line under the response's `traceId`, not in the response) | Unchanged — refused before any write. The container already recorded on the record is also recorded on another project, matter or work assignment, and is not shared storage | **An administrator** finds the other record (the log line, or a query on `sprk_containerid`), decides which record the container belongs to and clears `sprk_containerid` on the other one; then provisioning is called again (it keeps the container for this record). Calling again before that repeats the refusal |
| `container_ownership_unreadable` (HTTP 500) | Unchanged — refused before any write. Whether the container already on the record is shared storage could not be read | **The same caller** calls again once Dataverse is reachable (the wizard's "Try securing again") |
| `creator_share_failed` | The move was undone or never made: owner read back as before the call. Shares: `sharesRestored: true` — the creator's shares are as before the call (read back). `sharesRestored: false` with `creatorShareRemoved: false` — a share this call issued could not be confirmed removed (check Manage Access). `sharesRestored: false` with `creatorShareRemoved: true` — the shares could not be read before the call, so the creator's explicit share was removed entirely, **including one they held before the call** (an administrator re-adds it through Manage Access if it is still needed). On a resume: ownership unchanged by the call | **The same caller** calls again — they still pass the Write check |
| `owner_assignment_failed`, `owner_assignment_not_applied` | Owner read back unchanged; nothing moved | Administrator checks the `Secure Record Owner` role (§5) and §7 item 6; calling again repeats the same refusal |
| `owner_assignment_unverified` | Unknown whether the team owns it. A share to the creator was issued: `creatorShareConfirmed: true` — read back on the record; `false` — issued but not confirmed by a read | **The same caller** calls again: a record the team now owns is resumed, one it does not is provisioned from the start. If `creatorShareConfirmed` was false and the creator cannot open the record, **an administrator** calls provisioning again: it resumes and ensures the share for `createdby` (the wizard's message names the administrator whenever the share is not confirmed; the creator's own retry is then refused at the Write gate) |
| `container_creation_failed` | Owned by the team, shared to its creator, no container | **The same caller** calls again: it resumes |
| `container_not_recorded` | Owned by the team, shared to its creator, no container recorded; the error names a container that holds nothing | **The same caller** calls again: it resumes and records a NEW container. Delete the named empty container |
| `creator_share_failed_resumable` | 🔴 Owned by the memberless team **without a confirmed creator share** — possibly nobody can open it (logged CRITICAL `[PROVISION]`). `ownershipVerified: false` when the move back could not be read back at all. The creator no longer passes the Write check | **An administrator** (who holds Write through their role) calls provisioning again for the record: it resumes and shares to the person who created it (`createdby`, or `sprk_createdbyperson`) |
| `resume_creator_unavailable` (`creatorState`: `absent` / `disabled` / `application-user` — HTTP 409; `unreadable` — HTTP 500; `creatorColumn`: `createdby` or `sprk_createdbyperson` — which column the state describes; `createdByState` / `creatorPersonState` for the operator) | Unchanged — refused before any write: owned by the team, no container. Neither `createdby` nor `sprk_createdbyperson` names a usable person, and it is never shared to the caller or anyone else instead — a share another person already holds does not change that | `unreadable` (a read failed — `createdby`, the recorded person, or the column itself in an environment §7b has not reached): **the same caller** calls again once the read works (the wizard offers "Try securing again"). `disabled`: **an administrator** re-enables that user, if they should keep the record, and calls again — the resume shares it to them. Any state: **an administrator** assigns the record (Dataverse Assign) to the person who should hold it — it leaves the owner team and is back in the state of a secure record never provisioned (flagged, no container, uploads refused, readable within that person's business unit as before provisioning) — and **that person** calls provisioning: it runs from the start and shares it to them. Sharing through Manage Access and calling again does NOT complete it (owner decision F8). Rows that land here now: records created by an application BEFORE `sprk_createdbyperson` existed, or outside the BFF, with no person recorded |

**Calling provisioning as an administrator** (the resume path): `POST {bff}/api/v1/external-access/provision-project`
with body `{ "recordType": "project" | "matter" | "workassignment", "recordId": "<guid>" }` and a user token for the BFF
API. The caller must hold Write on the record (the delegation filter); a System Administrator does through their role.

---

## 7b. The record's creator: `sprk_createdbyperson` (task 133, owner round 7 item 2)

`createdby` names the identity that SENT a create. The BFF creates matters and projects (Office quick-create) and work
assignments (`POST /api/v1/work-assignments`) **app-only**, so on those rows `createdby` is the BFF application user and
`createdonbehalfby` is empty. The BFF therefore records the PERSON on every create path of the three tables in
`sprk_createdbyperson` (lookup → `systemuser`), and the resume above shares to it when `createdby` is not a usable person.
The column is **field-secured: only the BFF writes it** (writer profile = the BFF application user(s); every user reads it
through the reader profile on each business unit's default team). Full spec:
`src/solutions/SpaarkeCore/entities/sprk_project/created-by-person-schema.md`.

⚠️ **Apply the schema BEFORE deploying a BFF that carries task 133** — that BFF writes the column on every Office
quick-create and work-assignment create, and Dataverse refuses a create naming a column it does not have:

```powershell
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>]            # dry run: read-only
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Apply
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Verify   # must exit 0
```

A business unit created later needs `-Apply` re-run (its default team joins the reader profile).

---

## 8. What must NOT be done

| ❌ | Why |
|---|---|
| Add anyone — human or application user — to the secure owner team | The team owns every secure record; a member reads all of them by membership. The whole safety argument is that it is memberless. Provisioning refuses while it has a member |
| Place any user in the Secure Record business unit (Change-BU, or a registration environment configured with that BU or a team in it) | A user there reads every secure record by business-unit depth, whoever owns them. Registration refuses it by ID (task 144); a maker-portal Change-BU cannot be blocked and is caught by provisioning and the census job |
| Use the business unit's **default** team as the owner, or leave the owner role on it | Its membership is every user placed in the business unit, maintained by Dataverse and impossible to curate (#967). §4.3 retires it |
| Grant `Secure Record Owner` to a user or any other team | Same reason. Items 4–5 of §7 assert this |
| Add privileges "for completeness" | Every privilege must be forced by a recorded failure. Nothing beyond `Read` was |
| Widen the depth beyond `User` | Adds reach without adding capability, and re-opens §6 |
| Remove `sprk_project` Read from ordinary user roles | Silently disables all sharing (§6) |
| Create a service account to own secure records | Not needed — an owner team costs no licence, no credential, no identity to audit |
| Create one BU per project | Retired mechanism. No `SP-*` BUs should exist |
| Set `sprk_containerid` on the secure BU | That is the *shared* cascade container. A secure project uses its own |
| Use `pac` without checking the active profile | `pac auth list` may be pointed at production. Mint a token against an explicit URL instead |

---

## 9. Related

| Topic | Where |
|---|---|
| Ownership model + the empirical privilege determination | `projects/unified-access-control-r2/design.md` §5.1a |
| The depth defect, proof, and candidate fixes | design §5.1a-2 and §5.2 |
| Child-entity ownership (18 entities, unresolved) | design §5.1d |
| The role's privilege list (the ONE list) + the script that applies and verifies it | [`config/secure-record-owner-role.json`](../../config/secure-record-owner-role.json), [`scripts/Set-SecureRecordOwnerRolePrivileges.ps1`](../../scripts/Set-SecureRecordOwnerRolePrivileges.ps1); why children are in it: `projects/spaarkeai-word-add-in-r1/notes/082-secure-owner-role.md` |
| Container isolation (separate, unresolved) | design §5.1c → project `spaarke-secure-project-r1` |
| NFR-05 assertion wording | `projects/unified-access-control-r2/spec.md` |
| Provisioning code | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs` (projects, matters, work assignments; `recordType` + `recordId`) |
| Named owner team + the two invariants | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/SecureRecordOwnerTeam.cs`; census job `Services/ExternalAccess/SecureRecordIsolationCensusJob.cs`; evaluator `Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs` |
| One-time migration off the default team | [`scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1`](../../scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1); record `projects/unified-access-control-r2/notes/task-144-named-secure-owner-team.md` |
