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
   refuses a record still owned by the default team (`sdap.provision.owned_by_other_secure_team`) before any write —
   moving it is the migration's job (step 3), not a side effect of provisioning.
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

**Extending the set** (a new `child` entry, or a batch such as task 146's 9 → 26 tables, approved by the owner on
2026-10-02, round 7 item 4) runs in this order, and **before** any code that assigns those tables' rows to the team
is deployed — Dataverse refuses the team as an owner without `Read`:

1. **Negative control** — for each NEW table, create a probe row owned by the named team (`ownerid@odata.bind` →
   `/teams(<Secure Record Owners>)`) and record the refusal verbatim (3 polls, ~25 s apart). It must name only that
   table's `Read` privilege, and its `privilegeCount` must equal the role's current count (not a cached reading).
2. **Write the evidence** — replace the entry's `VERBATIM REFUSAL PENDING …` text in the file with the date, poll
   count and refusal, and commit that BEFORE step 3. A table that is NOT refused is removed from the file instead.
3. **Dry run → `-Apply` → §5.4 strip → `-Verify`** with the script above (expect exit 0 and nothing "outside the
   file").
4. **Positive probes** — the same create succeeds on 3 consecutive polls; read back `owningteam`; delete each probe and
   record the deletion. A control create on a table NOT in the file must still be refused with the new
   `privilegeCount`.

The exact commands for task 146's 17 tables are in
`projects/unified-access-control-r2/notes/task-146-server-child-writers.md` §13 (gate G146-1).

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
| 12 | The BFF's `secure-child-share-reconciliation` job (`/api/admin/jobs/secure-child-share-reconciliation/status`, task 149) | **enabled**, last run `Success = true`, heartbeat `[SECURE-CHILD-SHARES] heartbeat status=Completed`. `held > 0` names a child whose secure roots cannot be determined (see §7a); `notUpdated > 0` is retried every two minutes |
| 13 | **🔴 Sharees see the children, nobody else does** (task 149) — with existing non-admin test users: share a secure project with user A, create a child under it, wait one reconcile tick (≤ 2 min) | A reads the child in MDA; a user NOT shared on the project is DENIED it; unsharing A (Manage Access, or the MDA Share dialog + one tick) removes A from the child. Delete the probes |
| 14 | The BFF's `secure-child-reconciliation` job (`/api/admin/jobs/secure-child-reconciliation/status`, task 148) | **enabled every 2 minutes** (task 147, owner round 28 item 2): its recent-changes pass WRITES (`recentChanges.mode = write`; only `SecureChild__Reconciliation__RecentChangesWritesEnabled=false` stops it). The sweep stays REPORT-ONLY with `SecureChild__Reconciliation__WritesEnabled` unset — heartbeat `[SECURE-CHILD-RECONCILE] heartbeat mode=ReportOnly`, and `wouldChange` is the backfill still owed (0 once §7c.1 has run); a scheduled tick does not run (or move) the report-only sweep |
| 15 | The BFF's `secure-root-inheritance` job (`/api/admin/jobs/secure-root-inheritance/status`, task 158) | **enabled** (writes on, every 5 minutes), last run `Success = true`, heartbeat `[SECURE-INHERIT] heartbeat`. It makes every work assignment / project FILED UNDER a secure matter or project secure itself (provisioning's own steps, for its recorded creator — refused when that person is on the No Access list of the record or of a secure parent) and gives it the parent's sharees, recording where each came from on the `sprk_assignedaccess` ledger (a parent's unshare ends only an unmodified inherited share). `unverifiable > 0` names a record whose parent's flag cannot be read or reads EMPTY (`emptyFlagParents` counts those parents — run `scripts/Repair-SecureFlagNulls.ps1`); `refused > 0` a record with no usable or a walled creator; `deferred > 0` means the run hit its bound of 25 provisionings — `Success = false`, `resumeAfter` names where the next run continues. `unfiledProvenance.notDone > 0` names inherited shares on records re-filed away from their parent that could not be checked (the run is not a success). Prerequisite: `scripts/Set-AssignedAccessLedgerSchema.ps1 -Apply` then `-Verify` (the `sprk_subjectteam` column) |

### 7a. The children of a secure record — who can see them (task 149)

A secure record's children (documents, events, to-dos, communications, memos, analyses and the rest of the codified
set in `config/secure-record-owner-role.json`) are owned by the memberless `Secure Record Owners` team (task 146), so
**the only way to reach one is a share on the child row itself**. Dataverse does not provide that share: every
root→child relationship has Share, Unshare, Reparent and Assign set to **NoCascade** (read 2026-10-02). The BFF does:

- **The rule.** Every child carries exactly its secure root's internal sharees — users and teams — at the root's rights
  restricted to Read, Write, Append, AppendTo and Delete. **Never Share** (share the ROOT; the children follow) and
  never Assign — owner decision, round 11 item 4. A child filed under two secure records gets only the people shared on
  BOTH, at the lower rights (the INTERSECTION, fail closed — owner decision, round 11 item 4). Any other share on a
  child is removed.
- **The No Access list wins** (task 143). A user on the No Access list of ANY secure record a child is filed under is
  never given anything new on that child — no new share, no wider one — even when the child's other secure record does
  not wall them, and even while their share on the record itself is still there (the No Access enforcement has not run
  yet, or kept them as the record's last reader); a share they already hold can only narrow. When the enforcement
  removes their share on the record, its children follow in the same call. If the list cannot be checked for someone,
  they are given nothing and the child is retried. The list applies where the record's **Secure flag**
  (`sprk_issecure`) is set: a record owned by `Secure Record Owners` whose flag reads No or empty — possible only part
  way through an unsecure (see the ship gate below) or after a hand edit before task 150 locks the flag — is not
  checked for its children either. Its own share path (Manage Access and the No Access enforcement) skips it the same
  way, so a child is never wider than its record.
- **When.** Immediately when a share is added, changed or removed through Manage Access (the response says how many
  related records could not be updated yet, if any); immediately after secure provisioning (whether the record keeps
  its own container or gets a new one); immediately after the No
  Access enforcement removes a share on the record; and every two minutes for everything else — a new or re-filed
  child, a client-side create, and a Share/Unshare made in the model-driven app's own dialog on a secure record.
- **The two-minute window is accepted** (owner decision, round 11 item 2, 2026-10-03). For a model-driven-app
  Share/Unshare and for a NEW or RE-FILED child, the children catch up within at most two minutes: an unshared user can
  keep a child for that long, and a sharee may wait that long to open a child just created. The reconcile job is the
  mechanism, ships with **writes on**, and Dataverse's table-wide Share/Unshare/Reparent cascade is **not** enabled.
- **Held children.** A child whose secure roots cannot be determined from the data (a missing parent, a root marked
  secure whose provisioning did not complete, a filing chain deeper than six levels) is only ever narrowed — nobody is
  added to it — and is reported as `held` until the data is fixed. When none of its secure records can be found at all,
  every share on it is removed, so a stale share (a former sharee's) never survives on such a child.
- **Messages on a secure record.** A conversation message filed under a secure record is a child like any other: its
  readers are the record's sharees. Thread participants who are not shared on the record are NOT granted it (the
  messaging grant skips Secure-team-owned messages); share the record to give someone its conversation.
- **Not this mechanism.** Contacts (SPA/Teams) reach children through the external data plane's root scoping, not POA
  shares.
- **🔴 SHIP GATE — no record is unsecured in a shared environment until task 148 is deployed** (owner decision, round
  11 item 3, 2026-10-03). Tasks 146 and 149 may deploy. Before task 148, a record made ordinary again kept its children
  owned by `Secure Record Owners` and shared with its FORMER sharees. **Task 148 meets the gate in code**: unsecure now
  re-owns every child into the record's business unit and removes its mirrored shares BEFORE the record's own shares go
  (§7c). The gate lifts in an environment when the BFF carrying task 148 is deployed THERE; until then, **do not
  unsecure** a record in any environment other people use (`notes/task-149-secure-child-sharee-access.md` §12 decision 4,
  §13; `notes/task-148-secure-child-backfill.md`).

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
recorded is provisioned: 409, nothing written — unless its existing related records still need securing, which the call
then completes (200 `childrenOnly`, task 148 §7c). **Through the form's Make Secure command** (`transition:
"make-secure"`, task 150 round 40) the resume instead follows the forward Make Secure rules: the CALLER is shared (and
proven) and the creator beside them, so the same command finishes a Make Secure that failed after its first write (§7d.1).

**A container already on a not-yet-secured record is never orphaned** (task 133 b2; live 2026-10-02 provisioning
`65a3fab2` created a second container and left its own referenced by nothing). Before any write the recorded
`sprk_containerid` is classified: a business unit's shared container, or one this BFF is configured to use for many
records (`Communication:ArchiveContainerId`, `EmailProcessing:DefaultContainerId`, `Email:DefaultContainerId`,
`SharePointEmbedded:StagingContainerId`), is **replaced** by the record's own — its owner keeps pointing at it; one that
another project, matter or work assignment also records is **refused**; anything else is the record's **own** and is
**kept** — no container is created and the value is not rewritten.

**Why the marker shapes both of those** (task 133 r1). "Owned by the team **with** a container recorded" answers 409 to
every later call, whatever happened after the move. So:

- A **shared** container is **unlinked before the owner move** (the business unit or configuration keeps it). Every
  failure after the move then leaves "owned by the team, no container", which the next call resumes — never a record the
  409 would refuse forever while its uploads went to shared storage. A secure-flagged record with the link removed
  refuses uploads until it has its own container (fail closed).
- A record that **keeps its own** container cannot be resumed once the team owns it. It is therefore moved only once its
  creator's share can be set up FIRST (if the record's shares cannot be read, provisioning refuses before any write —
  the same caller retries), and every failure after its move says so: the response carries `containerKept: true`, says
  the next call answers `already_provisioned`, and names the recovery that works without it — **an administrator shares
  the record to its creator through Manage Access** (or Share in the model-driven app). The owner decided (round 10 item
  5, 2026-10-03) to keep this as shipped: no automatic resume for such a record (task 133 note §14.3).

**The rows an owner move cascades to are put back on their own owners** (task 133 c1, owner round 10 item 4). Moving a
project or matter re-owns its SharePoint document locations and documents with it (Dataverse's Assign cascade; a work
assignment cascades nothing; `team` is business-owned and has no owner to move). The owner accepted that for the move
INTO the team (round 4 item 3) — on success they stay with the team. An undo cascades the same way, to the RECORD's
pre-call owner, so before any write provisioning records each child's OWN owner (`AssignCascadeChildOwners`), and after
a verified undo puts each one back, read back. A snapshot that cannot be read completely refuses the run before any
write (`cascade_children_unreadable`); a child that cannot be put back is named with the call that puts it back
(`cascade_children_not_restored`). SharePoint documents are read only under a document location: in an environment
without SharePoint integration (dev — Spaarke stores documents in SharePoint Embedded) Dataverse refuses that read, and
there is no location for a document to come from.

A resume never widens the record's access list on the caller's say-so: named colleagues (`sharePrincipalIds`) are
accepted only when the caller IS the record's creator — anyone else is refused before any write and adds people through
Manage Access. And when neither `createdby` nor `sprk_createdbyperson` names a usable person (absent, disabled, an
application user — or one of them cannot be read), the resume is refused — no share, no container — whoever else may hold
a share; the row below names the recoveries that work.

**`sprk_issecure` is written by provisioning, once, as its FIRST write** (task 150 — the column is field-secured and
the client no longer writes it; §7d). Every refusal before that write leaves the record **not flagged**, and the
wizard then adds nothing to it — no file, child record or email — so nothing reaches shared storage. Every failure
after it leaves the record **flagged**: uploads to it are refused until it has its own container (fail closed). It
is never cleared on any provisioning path, compensation included. A record already flagged (an older client, or a
row created before task 150) is provisioned exactly like an unflagged one.

| `reasonCode` (`sdap.provision.*`) | State the record is left in | Who recovers, and how |
|---|---|---|
| `secure_bu_not_found`, `secure_bu_ambiguous`, `secure_owner_team_not_found`, `secure_owner_team_ambiguous`, `secure_owner_team_has_members`, `secure_owner_team_membership_unreadable`, `secure_bu_has_users`, `secure_bu_users_unreadable`, `container_type_not_configured` | Unchanged — refused before any write | Administrator fixes the environment (§3–§5, `SharePointEmbedded:ContainerTypeId`), then provisioning is called again |
| `legacy_per_project_bu` | Unchanged | Administrator migrates the record off its per-project BU (manual) |
| `already_provisioned` | Provisioned: owned by the team, container recorded, every existing related record already secured (task 148: when one is not, the call secures it and answers 200 `childrenOnly: true` instead). Nothing written | Nothing to provision. Who can open it is managed through Manage Access: an administrator shares it to anyone who should hold it but cannot open it (this is also the recovery for a record that kept its own container — `containerKept: true` below) |
| `owned_by_other_secure_team` | Owned by the retired default team (another team inside the Secure Record business unit) — already isolated. The Access ribbon hides Make Secure on such a record (task 150 round 53 item 2) | Administrator runs `scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1` (§4.3) — task 144's migration, never a provisioning call |
| `creator_unresolved` | Unchanged — refused before any write | **The same caller** calls again (the wizard's "Try securing again") |
| `caller_rights_unverifiable` (task 150 round 53 item 1, HTTP 500; the form's Make Secure command only) | Unchanged — refused before any write. Which access the caller holds on the record (their effective rights, the floor their share is kept at — round 46 item 1) could not be read | **The same caller** calls again (Make Secure stays offered on the unchanged record). The wizards' path makes no such read and never meets it |
| `secure_flag_not_set` (task 150, HTTP 500) | Nothing else changed — the flag write is the first write. The flag itself may or may not be set (the write failed, or did not read back `true`) | **The same caller** calls again. If it repeats: an administrator runs `scripts/Set-SecureFlagFieldSecurity.ps1 -Verify` (§7d) — a refused write means the BFF application user is not in the writer profile; a read-back that comes back EMPTY means the BFF lost its field-level Read |
| `record_owner_unreadable` | Unchanged — refused before any write. The row was read with no owning user or team, which is deterministic for that row | **An administrator** checks the record's owner in Dataverse; calling again before that repeats the refusal (not offered as a retry) |
| `resume_colleagues_not_permitted` | Unchanged — refused before any write. A resume request named colleagues (`sharePrincipalIds`) and its caller is not the record's creator | The caller calls again **without** `sharePrincipalIds` (the resume then completes, sharing only to the creator), and adds people through Manage Access |
| `container_shared_with_another_record` (HTTP 409; `otherRecordType` — the other record's id is in the `[PROVISION]` log line under the response's `traceId`, not in the response) | Unchanged — refused before any write. The container already recorded on the record is also recorded on another project, matter or work assignment, and is not shared storage | **An administrator** finds the other record (the log line, or a query on `sprk_containerid`), decides which record the container belongs to and clears `sprk_containerid` on the other one; then provisioning is called again (it keeps the container for this record). Calling again before that repeats the refusal |
| `container_ownership_unreadable` (HTTP 500; `containerOwnershipState`) | Unchanged — refused before any write. Whether the container already on the record is shared storage could not be read | `containerOwnershipState: unreadable` (a read failed): **the same caller** calls again once Dataverse is reachable (the wizard's "Try securing again"). `refused` (owner round 14: Dataverse refused the read — a 401/403, the BFF's sign-in or its application user's Read privilege on `businessunit`, `sprk_project`, `sprk_matter` or `sprk_workassignment` refused; or a 400): calling again repeats the refusal (no retry is offered); **an administrator** looks at that Read privilege first |
| `shared_container_not_cleared` (HTTP 500; `speContainerId` = the shared container) | Not moved, no share issued. The record recorded a shared container (a business unit's or a configured one), whose link is removed BEFORE the owner move; removing it failed, so the link may or may not still be there | **The same caller** calls again (the wizard's "Try securing again"): a record still linked is unlinked again, one already unlinked is provisioned as a record with no container |
| `cascade_children_unreadable` (HTTP 500; `childTable` — `sharepointdocumentlocation` or `sharepointdocument`; `cascadeChildState`) | Unchanged — refused before any write. The OWN owners of the rows an owner move of the record cascades to could not be read completely, so a failure after the move could not put each back | `cascadeChildState: unreadable` (a read failed): **the same caller** calls again once Dataverse is reachable (the wizard's "Try securing again"). `refused` (Dataverse refused the read — for `sharepointdocument`, SharePoint integration is off while the record HAS document locations; or a 401/403: the BFF's sign-in, or its application user's Read privilege on `childTable`, was refused — or answered a full page, or a row came back without an id or an owner): calling again repeats the refusal; **an administrator** looks at the record's rows in `childTable`, and the BFF application user's Read privilege on that table, first |
| `cascade_children_not_restored` (HTTP 500; `childOwnersNotRestored` — each child's table, id, own owner, outcome and `nextCall`) | The move was undone: owner read back as before the call; the creator's shares as `sharesRestored` / `creatorShareRemoved` describe (as for `creator_share_failed`). 🔴 The named related rows are NOT confirmed back on their own owners — the undo's cascade gave them the record's owner (logged CRITICAL `[PROVISION]`). `outcome` says why: `Refused` / `NotApplied` (the owner bind was refused, or accepted and not applied — read back on another owner); `Unreadable` (the row could not be read before the restore — nothing written to it); `Unverified` (the bind was sent and the row could not be read back — it may or may not be back) | **An administrator** puts each named row back first — the `nextCall` (`PATCH /api/data/v9.2/{set}({id})` with `{"ownerid@odata.bind":"/systemusers(…)"}` or `/teams(…)`), or Assign in the model-driven app — and only then is provisioning called again: another run would record the owner those rows have now as their own (not offered as a retry) |
| `creator_share_failed` | The move was undone or never made: owner read back as before the call. Shares: `sharesRestored: true` — the creator's shares are as before the call (read back). `sharesRestored: false` with `creatorShareRemoved: false` — a share this call issued could not be confirmed removed (check Manage Access). `sharesRestored: false` with `creatorShareRemoved: true` — the shares could not be read before the call, so the creator's explicit share was removed entirely, **including one they held before the call** (an administrator re-adds it through Manage Access if it is still needed). On a resume: ownership unchanged by the call. With `containerKept: true`: the record keeps its own container and its shares could not be read, so nothing was written at all. A replaced shared container's link stays removed (the detail says so). After an undo, `childOwnersRestored: true` — every row the move cascaded to is back on its own owner (read back) | **The same caller** calls again — they still pass the Write check |
| `owner_assignment_failed`, `owner_assignment_not_applied` | Owner read back unchanged; nothing moved | Administrator checks the `Secure Record Owner` role (§5) and §7 item 6; calling again repeats the same refusal |
| `owner_assignment_unverified` | Unknown whether the team owns it. A share to the creator was issued: `creatorShareConfirmed: true` — read back on the record; `false` — issued but not confirmed by a read. `containerKept: true` — the record keeps its own container | **The same caller** calls again: a record the team now owns is resumed, one it does not is provisioned from the start. If `creatorShareConfirmed` was false and the creator cannot open the record, **an administrator** calls provisioning again: it resumes and ensures the share for `createdby` (the wizard's message names the administrator whenever the share is not confirmed; the creator's own retry is then refused at the Write gate). **With `containerKept: true` there is no resume**: a record the team now owns answers `already_provisioned` (with the share confirmed, it is complete); if the creator cannot open it, **an administrator shares it to them through Manage Access** (or Share in the model-driven app) |
| `container_creation_failed` | Owned by the team, shared to its creator, no container (a replaced shared container was unlinked before the move, so it is not recorded either) | **The same caller** calls again: it resumes |
| `container_not_recorded` | Owned by the team, shared to its creator, no container recorded; the error names a container that holds nothing | **The same caller** calls again: it resumes and records a NEW container. Delete the named empty container |
| `creator_share_failed_resumable` | 🔴 Owned by the memberless team **without a confirmed creator share** — possibly nobody can open it (logged CRITICAL `[PROVISION]`). `ownershipVerified: false` when the move back could not be read back at all (the record may instead be back with its previous owner). The creator no longer passes the Write check. `containerKept: true` — the record keeps its own container; the detail says whether the creator's share was confirmed before the move (it then still stands unless the move itself dropped it) | `containerKept: false`: while the team owns it, **an administrator** (who holds Write through their role) calls provisioning again for the record: it resumes and shares to the person who created it (`createdby`, or `sprk_createdbyperson`). `containerKept: true`: **no resume** — a provisioning call answers `already_provisioned`; if the creator cannot open the record, **an administrator shares it to them through Manage Access** (or Share in the model-driven app). Either way: if the move back did take effect, the record is where it was and its creator calls provisioning again — when `childOwnersAtRisk` is present (the move back could not be verified, and some cascaded rows had owners of their own), an administrator first puts each listed row back with its `nextCall` |
| `children_incomplete` (HTTP 500; task 148; `childrenReowned`, `childrenRemaining`, `childTables` — per table: examined, already correct, re-owned, refused, failed; `containerKept`) | Provisioned — isolated, shared, its container recorded — but some of its EXISTING related records are not yet re-owned into the team or not yet shared with its sharees (each left as it was — never more exposed than before the call). The pass runs after the container (Step 8), so a related record never blocks the record's storage | **The same caller** calls again: the record is provisioned, so the call completes the related records and answers 200 `childrenOnly: true` (or 409 `already_provisioned` once there is nothing left to do). A child the ownership rule REFUSES (e.g. one also filed under another record flagged secure but not isolated) stays refused until that record is fixed; the `[PROVISION]` and `[SECURE-CHILD-RECONCILE]` log lines name it. A Dataverse refusal of the re-own naming a privilege is an escalation to the codified role set (§5), not something to work around |
| `resume_creator_unavailable` (`creatorState`: `absent` / `disabled` / `application-user` — HTTP 409; `unreadable` / `refused` / `column-missing` — HTTP 500; `creatorColumn`: `createdby` or `sprk_createdbyperson` — which column the state describes; `createdByState` / `creatorPersonState` for the operator) | Unchanged — refused before any write: owned by the team, no container. Neither `createdby` nor `sprk_createdbyperson` names a usable person, and it is never shared to the caller or anyone else instead — a share another person already holds does not change that | `unreadable` (a read failed — `createdby` or the recorded person, transiently): **the same caller** calls again once the read works (the wizard offers "Try securing again"). `refused` (owner round 14: Dataverse refused a read the decision needs — a 401/403, the BFF's sign-in or its application user's Read privilege on `systemuser` or on the record's table refused; or a 400 to a user read): calling again repeats the refusal (no retry is offered); **an administrator** restores that Read privilege first, then calls again. `column-missing` (Dataverse answered 400 to the query naming `sprk_createdbyperson` — the column is not in this environment, §7b has not run): calling again repeats the refusal (no retry is offered); **an administrator** applies §7b (`Set-RecordCreatorPersonSchema.ps1 -Apply`, then `-Verify`) and calls again. `disabled`: **an administrator** re-enables that user, if they should keep the record, and calls again — the resume shares it to them. Any state: **an administrator** assigns the record (Dataverse Assign) to the person who should hold it — it leaves the owner team and is back in the state of a secure record never provisioned (flagged, no container, uploads refused, readable within that person's business unit as before provisioning) — and **that person** calls provisioning: it runs from the start and shares it to them. Sharing through Manage Access and calling again does NOT complete it (owner decision F8). Rows that land here now: records created by an application BEFORE `sprk_createdbyperson` existed, or outside the BFF, with no person recorded |

**Calling provisioning as an administrator** (the resume path): `POST {bff}/api/v1/external-access/provision-project`
with body `{ "recordType": "project" | "matter" | "workassignment", "recordId": "<guid>" }` and a user token for the BFF
API. The caller must hold Write on the record (the delegation filter); a System Administrator does through their role.
Without a `transition` the resume shares to the record's creator only (F8: the administrator is not added to its access
list); the form's Make Secure command (`transition: "make-secure"`) shares to its caller as well (§7d.1).

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

## 7c. Existing children follow the record — provisioning, unsecure and the one-time backfill (task 148)

A record's children are created secure by task 146 and shared by task 149. The children it ALREADY HAS when it becomes
secure — and the children of a record that stops being secure, and of every record that was secure before task 148 — are
brought into line by ONE engine, `SecureChildReconciler`, called from three places:

| Trigger | When | What happens to the existing children |
|---|---|---|
| `/provision-project` | Step 8: after the record is isolated (Step 5), shared (Step 5.5 + colleagues) and has its container (Steps 6/7, or the one it keeps); also on a call for an ALREADY provisioned record, before it answers 409 | Each child is re-owned to `Secure Record Owners` (the ownership rule, task 146), read back; then the record's sharees are mirrored onto it (task 149 — its former Step 8). Incomplete → `children_incomplete` (§7a table); calling again completes it (200 `childrenOnly: true`); nothing left to do → 409 `already_provisioned` as before |
| `/unsecure-project` | Step 3.5: after the record moved to its new owner (Step 3), BEFORE its own shares are revoked (Step 4) and its flag cleared (Step 5) | Each child the team owns is re-owned to the owner the rule gives a child of an ordinary record (its business unit's team), THEN its mirrored shares are removed. The SharePoint document locations / documents the record's Assign cascaded to the new owner are placed by the same rule (owner round 13 item 1). Incomplete → 500 `sdap.unsecure.children_incomplete`: **the record's shares stay and `sprk_issecure` stays set**, so the people shared on it keep the children that are still isolated; calling again completes it. On a record already not secure, the call completes the children an earlier unsecure left behind. Before the move, the cascade rows are read (`sdap.unsecure.cascade_children_unreadable` refuses before any write, as provisioning does) |
| `secure-child-reconciliation` job | Every 2 minutes (task 147, owner round 28 item 2), and a manual trigger | Every record flagged `sprk_issecure = true` (projects, matters, work assignments), in a fixed order, at most `SecureChild:Reconciliation:MaxRootsPerRun` (default 50) per run; each run's `startPosition` is where it began and `resumeAfter` where the next continues (the place in the list is a per-instance cursor, and the run history is per instance — a restart starts the sweep over, harmlessly). The sweep is **report-only** unless `SecureChild__Reconciliation__WritesEnabled=true`, and a SCHEDULED tick runs it only then. **Recent changes first** (task 147), with writes ON by default (`SecureChild__Reconciliation__RecentChangesWritesEnabled=false` is the emergency stop): the records whose related rows changed since the last run (per-instance watermark, 60 minutes back after a restart) are reconciled. Those rows are children written outside the product (the product's own writers create and re-file through the BFF). A record whose pass came back incomplete, and a changed row that could not be placed, are carried to the next run and retried and reported every run until finished. **After a start** (restart, deploy, scale-out — the carried state is per instance and in memory) the instance first walks EVERY secure record once on its scheduled ticks (the **catch-up**, `MaxRootsPerRun` records a run, in the recent-changes mode; never on a manual trigger; it stands aside while the sweep writes), so nothing an earlier instance carried is lost. The run's `recentChanges` lists `mode`, `since`, `rowsChanged`, the records found, `retriedRoots` / `retriedRows` (more than 1,000 unplaced rows set `pendingOverflow` and hold the watermark, so the window is listed again), `droppedRoots` (carried records no longer flagged secure — not reconciled again), any row it could not place (`undetermined`, which fails the run), a listing `failure` (the watermark does not move) and `catchUp` (`ran`, `complete`, `rootsInRun`, `resumeAfter`); each correction is in `changes` with pass `recent` or `catch-up`. **Deploy order (G147-2):** apply and verify the backfill (§7c.1) BEFORE a task 147 BFF reaches the environment — the catch-up then only ever finds drift |

**Which rows.** A record's descendants through every lookup task 149's lineage map lists — a document's `sprk_project`
AND `sprk_relatedproject`, the `sprk_regarding{project|matter|workassignment}` links, and further hops (a to-do filed only
under a document of the record). **Never**: a row filed under nothing; a row of another record only; a per-user Direct or
master thread; a work assignment or project filed under the record — it is a ROOT of its own (owner round 6; task 158),
and neither it nor its own children are moved. A row is moved only across the isolation boundary: an ordinary row the
rule would give another ordinary team is left alone. A row is taken OUT of isolation only by `/unsecure-project` (an
F3 holder's act): provisioning and the sweep never release a child they find isolated — one the rule would hand an
ordinary team stays isolated and is reported `needsF3` (owner round 24 item 2; round 6, "never auto-unsecure"). A row
also filed under a SECOND secure record stays isolated
(secure-if-any). A row filed under the record AND under one of its other children (an analysis of a document that also
carries `sprk_regardingproject`; a communication regarding an event, stamped) is decided again after that child moves,
until nothing moves — it never stays isolated because it was looked at before its parent.

**Ordering and the invariant.** Into isolation the re-own comes first and the mirror second (task 149 mirrors only
team-owned rows; owner round 11 item 3): for the length of the pass the record's sharees may briefly not see a child they
saw through their business unit — never the reverse; no principal that could not read a child before gains it. Out of
isolation the re-own comes first, so a child is never readable by nobody, and then its mirrored shares are removed — ONLY
from a child the Secure team owned when the pass began (owner round 22): a share someone gave on an ordinary child that
was never isolated (for example a model-driven-app share of a user-owned document) is that user's intent and is kept. A
child whose mirrored shares cannot all be removed is put back on the Secure team (its sharees keep it; nobody new gains
it) and the call answers `children_incomplete`; calling again moves it out and finishes the removal.

### 7c.1 The one-time backfill — runbook

Run on each environment ONCE after the BFF carrying task 148 is deployed and the §5 role set is applied. The script only
triggers the job and reads its run history; the rule is the BFF's. It needs an Azure CLI login of a user holding the
BFF's `SystemAdmin` policy (the `/api/admin/jobs` routes), and for `-Apply` rights to change the App Service settings.

**Prerequisites — check before step 1, or the dry run, apply and verify are not about the same list:**

- **ONE App Service instance.** The job's place in the list of secure records (its cursor) and its run history are per
  instance, and the script polls the run history of whichever instance answers. With more than one instance, a trigger
  can run on one instance and its report be looked for on another, and two instances keep two places in the list. Scale
  the BFF's App Service plan to **1 instance** for the whole runbook, and back afterwards. Check:
  `az appservice plan show --ids (az webapp show -g <rg> -n <bff-app-service> --query appServicePlanId -o tsv) --query
  sku.capacity` → `1`; scale: `az webapp scale -g <rg> -n <bff-app-service> --instance-count 1`. A plan with autoscale
  rules needs its minimum and maximum set to 1 too (`az monitor autoscale show/update`).
- **The job's schedule disabled** (`POST /api/admin/jobs/secure-child-reconciliation/disable`, or confirm `enabled:
  false` on its `/status`). It is scheduled every 2 minutes (task 147); while the sweep is report-only a scheduled tick
  leaves its place in the list alone, but with `SecureChild__Reconciliation__WritesEnabled=true` a scheduled tick moves it
  mid-pass. The script refuses a pass whose runs are not contiguous from the first secure record (each run's
  `startPosition`), so a moved place makes it stop with an error rather than pass on part of the list — re-run it after
  disabling the schedule.

```powershell
# 1. DRY RUN (report-only; the default). Repeats until the pass completes, prints the planned owner changes with each
#    row's current owner, and saves each run's report to .\secure-child-backfill-<timestamp>\ .
#    The plan is the apply's: it is decided to the same fixpoint over PLANNED owners, so a grandchild reached only
#    through a row the pass would move (a to-do filed under an ordinary document of the record) is planned too. It
#    assumes each write lands — a write Dataverse refuses shows up in the apply's report, not here. A run's report LISTS
#    at most 200 changes (it COUNTS all of them, changesTotal); when a run planned more, the script warns, and the
#    complete list is the BFF log's '[SECURE-CHILD-RECONCILE] plan:' lines (or lower MaxRootsPerRun and run again).
#    The share changes follow the owner changes and are not listed per principal: a row moving in gets the record's
#    sharees mirrored.
.\scripts\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://<bff-host> -ApiScope api://<bff-app-id>/.default

# 2. Review the reports: every planned change is a related record of a secure record moving INTO the Secure team —
#    the sweep never moves a row OUT (owner round 24 item 2). A 'NeedsF3' row (counted in needsF3; the script warns)
#    is isolated but the rule would give it an ordinary team, because every record it is filed under is ordinary (for
#    example a to-do filed only under another, ordinary record's document): it stays isolated, and only Unsecure — a
#    Full Access holder or the record's creator — releases it. A 'refused' row names a parent flagged secure but not
#    isolated (fix that record first). A planned change of a document whose content sits in a SHARED SPE container is
#    an escalation (task 148 trigger 1) — present it to the owner.

# 3. APPLY (writes): turns SecureChild__Reconciliation__WritesEnabled on (an app-setting change restarts the app), runs the
#    pass to its end, then turns the setting OFF again.
.\scripts\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://<bff-host> -ApiScope api://<bff-app-id>/.default `
  -Apply -ResourceGroup <rg> -AppName <bff-app-service>

# 4. VERIFY (report-only): exit 0 only when a FULL pass — runs contiguous from the first secure record (startPosition 1),
#    adding up to all of them — plans ZERO changes and refuses / fails nothing (NeedsF3 rows are listed, and do not fail
#    it: no sweep may move them). A first run that begins mid-list (the tail
#    of an earlier pass) is printed but not counted; the script carries on to a pass that starts at the first record.
.\scripts\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://<bff-host> -ApiScope api://<bff-app-id>/.default -Verify
```

Every re-own is logged before it is written (`[SECURE-CHILD-RECONCILE] reassign: <table> <id> owner <previous> -> team
<target>`) — the reversal record: assign the row back to `<previous>` to undo it. A run's `ResultJson` lists the first
200 changes with the same fields and counts all of them (`changesTotal`, `changesListed`; `needsF3` for the held rows).

**Manual live gate (dev, after deploy; owner-approved round 11, run by the main session).** Seed probe children under
an ordinary project, matter and work assignment owned by an existing non-admin test user — a document via
`sprk_project`, a document via `sprk_relatedproject` (project), an event, a to-do regarding the document, a
communication, a memo; provision each secure; record each child's owner read-back, the creator's share on each child,
and that a non-sharee existing non-admin test user (`uac.child.user@demo.spaarke.com`) is denied; unsecure one of them
and record the reverse (children on the business unit's team, no child shares, the flag cleared last); run §7c.1 steps
1-4 over dev's secure records; delete the probes and record the deletion.

---

## 7d. Locking `sprk_issecure` — field-level security (task 150, owner round 2 item 2)

`sprk_issecure` on `sprk_project`, `sprk_matter` and `sprk_workassignment` (and `sprk_invoice` — below) is
**field-secured**: only the BFF, inside
the secure (`/provision-project`) and unsecure (`/unsecure-project`) endpoints, sets or clears it. A user with Write can
no longer clear it on a secure record (which would bypass the unsecure endpoint's ownership move and share sweep and
route new content to shared storage) or set it on a record that was never provisioned.

**The design is "readers never masked", not "writers locked".** A field-secured column a caller has no Read on comes
back EMPTY — no error. Most readers (the external plane's Secure suppression, the client container resolver, the
Access Permission pill) map empty to "not secure". So every user must keep Read:

| Profile (task 133's — reused, never forked) | Field permission on `sprk_issecure` | Members |
|---|---|---|
| `Spaarke BFF-Managed Field Readers` | Read | **every business unit's default team** — every current and future user of an existing BU is covered automatically |
| `Spaarke BFF-Managed Field Writers` | Read, Create, Update | **the BFF application user(s) only**, associated explicitly (never relying on System Administrator — a production app user may not hold it) |
| `System Administrator` (platform) | Read, Create, Update — created by the platform, cannot be narrowed | every holder of the System Administrator role. **Owner decision F4: accepted** as the administrator boundary (an administrator can already reassign, share or delete any secure record); the standing assertion LISTS every holder |

Who may remove the designation (owner round 3b, **F3**): the unsecure endpoint admits only a **Full Access holder**
(Write + Delete on the record, as Dataverse reports the caller's rights — an administrator qualifies through their role)
or **the record's creator** (`createdby`, or `sprk_createdbyperson` for an app-created row). Any other Write holder gets
403 `sdap.unsecure.not_permitted`. The check is the ONE F3 rule task 146 shares with moving a child out of a secure record
(`SecureDesignationRemoval`, owner round 13 item 6): what it cannot establish — the caller, their rights, the creator
person, or (a `sprk_createdbyperson` column this environment lacks) whether one is recorded — answers
`sdap.unsecure.permission_unverifiable` (500 when a read failed, else 403), never "allowed". Securing stays open to Write
holders — for a record already marked secure (an older client, a pre-task-150 row), and through the form's **Make
Secure** command for any record (§7d.1: the request names `transition: "make-secure"`, held to the Write gate — owner R3b,
round 33 item 1 — and the record's creator is shared to as well). On the wizards' create-then-secure path (no
transition), a record NOT yet marked secure is
secured through `/provision-project` only by **its creator** (owner round 10 item 10: `createdby` when a person, else
`sprk_createdbyperson`); anyone else gets 403 `sdap.provision.not_record_creator` before any write (a missing
`sprk_createdbyperson` column, where `createdby` names no person: 403 `sdap.provision.record_creator_unverifiable` with
`creatorState: column-missing` — round 17 item 1). That holds on a resume too (a record the Secure Record owner team
already owns with no container, but whose flag is not set): only its creator may finish it. Every documented recovery
meets a FLAGGED row, which stays on the Write gate; a System Administrator who must finish an unflagged one (an anomaly,
e.g. a manual Assign to the owner team) sets the flag first (F4), then calls provisioning.
Securing an existing record someone else created is task 148's transition, reached through Make Secure. `sprk_accesspermission` is NOT
field-secured (owner-accepted).

**`sprk_invoice` carries the column too, and is locked the same way** (owner round 10 item 11: invoices follow their
matter). Its value is **not a security input** anywhere in the BFF: the securable-entity registry leaves the invoice out
(`SecurableEntityRegistry.FlagIsNotASecurityInput`), so an invoice is secure exactly when the matter or project it is filed
under is, decided by the ancestor walk. Nothing writes it; the lock (same profiles, same script, same window) only stops a
user setting a value that looks meaningful. Its NULL rows are part of step 0.

**Order — every step dry-run first, then `-Apply`, then `-Verify` (each must exit 0):**

```powershell
# 0. One-time NULL cleanup (owner decision Q1). BEFORE the environment receives ANY BFF build containing task 150,
#    which reads an EMPTY flag as UNREADABLE everywhere - not only uploads: see "Shipping the task 150 BFF before
#    step 0" below for the full effect on every NULL-flag record. Where an environment is fed from master (dev: peers
#    deploy master), that means BEFORE task 150 merges to master. Harmless to the older BFF, which already routes NULL
#    and false the same. Nothing in Deploy-BffApi.ps1 or the release flow checks this: -Verify (exit 0) is the gate,
#    run by hand.
.\scripts\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com                # dry run: ids + counts
.\scripts\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -Apply         # writes a JSON report
.\scripts\Repair-SecureFlagNulls.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -Verify
# 1. Deploy the BFF (task 150). 2. Deploy the client (no create payload names sprk_issecure) and confirm no cached old
#    bundle is served — securing the column first would refuse EVERY secure project create made by a user.
# 3. The two profiles and their members (§7b's script — the ONE mechanism for every BFF-managed column).
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -BffApplicationIds <ids> -Apply
# 4. The lock. It REFUSES -Apply unless the profiles, every default team, the writer membership and the NULL cleanup are
#    in place, and -ClientNoLongerWritesFlag confirms step 2.
.\scripts\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -BffApplicationIds <ids>   # dry run
.\scripts\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -BffApplicationIds <ids> `
  -ClientNoLongerWritesFlag -Apply
.\scripts\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -BffApplicationIds <ids> -Verify
# 5. The standing assertion, by hand (read-only):
$env:SPAARKE_NFR05_DATAVERSE_URL = 'https://<org>.crm.dynamics.com'; $env:SPAARKE_BFF_APPLICATION_IDS = '<ids>'
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SecureFlagFieldSecurity_InTheTargetEnvironment"
```

**Shipping the task 150 BFF before step 0 — the full effect (round 26 item 2(c)).** The task 150 BFF reads an EMPTY
`sprk_issecure` as **unknown, and unknown fails closed** — in every reader, not only the upload path. Until step 0 has
run, every record whose flag is NULL (and every record below it) is treated as **Unreadable: secure AND Restricted AND
inactive at once**:

- **External participants lose every contact-sourced right on it** — direct grants, organization grants, standing-grant
  membership and organization expansion alike (the shared flag reader `ExternalParticipationService` answers
  `RootRecordFlags.Unreadable`; the read-time evaluator removes every contact-sourced contribution). Their projects
  disappear from the external SPA and the record-scoped routes refuse them.
- **Grants answer 503 `policy_unreadable`** — granting a contact or an organization on it is refused (the write-time
  grant policy cannot read the flag), for every caller.
- **The last-reader rule applies** — removing a user's share is refused when no other enabled user would keep Read, as
  on a secure record (an unreadable flag counts as secure).
- **Uploads answer 503** (`secure_flag_unreadable`) — the container resolver refuses the record and everything filed
  under it, rather than guess between its own and a shared container.
- The external SPA labels such a project **secure** (`ExternalDataService`); unsecure (`secure_flag_unreadable`) and the
  child-ownership pass (a NULL-flag root that is not isolated is refused, never given an ordinary team) refuse it
  rather than read "not secure". Provisioning is the exception: it treats NULL as "not yet marked" and writes the flag
  itself (its first write), under the creator rule (the wizards) or the Write gate (Make Secure, round 33 item 1).

Nothing is mis-routed or exposed — every effect is a refusal — but every one of those records is unusable for external
participants and for uploads until step 0's `-Apply` sets the flag. That is why step 0 comes first in EVERY environment.

**The masked window.** The Web API cannot create a field permission on a column that is not yet secured, so between
securing each column and granting the reader profile there are a few seconds in which non-administrators read the
flag EMPTY — and so does the BFF identity, unless it holds System Administrator, until the writer profile's grant
lands. The lock script grants immediately after securing, and prints the measured window per table. With the task 150
BFF deployed, every record read during the window has the **full effect above** for those seconds (external participants'
contact-sourced rights withheld, grants 503 `policy_unreadable`, the last-reader rule, uploads 503
`secure_flag_unreadable`, provisioning `secure_flag_not_set`) — it refuses, it never mis-routes.

**A new business unit** needs its default team added to the reader profile — re-run step 3 (`-Apply` adds every
default team), then step 4's `-Verify` and step 5. The standing assertion fails on any default team without it.

### 7d.1 The user surface — Make Secure / Remove Secure in the form's "Access" group (task 150, UX amendment)

Users secure and unsecure an existing project, matter or work assignment from the main form's **Access** flyout (task
142's ONE group, ONE ribbon source `infrastructure/dataverse/ribbon/AccessRibbons/`, ONE command script
`sprk_/scripts/access_ribbon.js` 1.5.0) — never by editing the field, which no form shows and FLS locks.

- **Make Secure** — shown on a record that is NOT secure, and on one flagged secure whose secure transition did not
  finish (round 40 item 1, round 46 item 4: no container recorded, or owned by a user, or owned by a team in ANOTHER
  business unit — a PROVISIONED secure record is owned by the Secure Record Owners team and records its own
  container, and hides it), to a caller with Write. **Hidden too on a flagged record with a container owned by ANOTHER
  team INSIDE the Secure Record business unit** (round 53 item 2 — in practice the retired default team, before task
  144's migration): it is secure and isolated already, so there is nothing to finish; moving it onto the named team is
  task 144's migration (`scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 -Apply` / `-Verify`, §4.3), and a direct API
  call still answers 409 `owned_by_other_secure_team`. Which team and business unit are the Secure Record ones is the
  server's configuration, so for a flagged, team-owned record with a container the ribbon asks `can-manage-access` with
  `includeOwner=true` (the owning team, whether it is the Secure Record Owners team, and whether that team owns the record
  inside the Secure Record business unit); an answer it cannot get keeps Make Secure hidden on that record. A record
  reassigned outside Spaarke to a team in another business unit, or a legacy one provisioned before task 133's owner move
  (still user-owned), is finished by the call: its forward path re-owns it to the Secure Record Owners team and keeps its
  own container. It confirms with the
  owner-authored copy (owner round 27), then calls `/provision-project` with `transition: "make-secure"` (round 33 item 1; the exact
  token — any other value is refused 400, and so is a Make Secure request naming `sharePrincipalIds`): the server holds
  that path to the Write gate (owner R3b) — the creator rule is the wizards' path only — and the access afterwards is
  exactly what the confirmation says: the record is shared to the caller (as on every forward run) and to **the person
  who created it** (`createdby` when a person, else `sprk_createdbyperson`; read before any write — a read that fails
  refuses 500 `record_creator_unverifiable`, a missing `sprk_createdbyperson` column where it is needed refuses 403 with
  `creatorState: column-missing`; a disabled creator is not shared to). A creator on the record's No Access list is not
  shared to (No Access wins, owner N6), and neither is one whose No Access check or share fails — each is NAMED in the
  response's `skippedPrincipals` (`principal_no_access`, `principal_no_access_unverifiable`, `principal_share_failed`)
  and the ribbon shows a per-person warning (never silent, round 33 item 5; a person whose name cannot be read is
  "Someone", round 40 item 3); the caller adds them through Manage Access. The caller keeps access on BOTH paths
  (round 40 item 2): when the call finishes an earlier run that stopped after the owner move, the caller is shared and
  the creator beside them exactly as on the forward path. **The caller's share is floored on their EFFECTIVE rights
  before the call** (round 46 item 1) — Dataverse's own answer, asked as the caller in the same step as WhoAmI, the
  answer owner F3 decides Full Access from: a caller who held Full Access (Write and Delete) through a share, through
  owning the record, or through a security role is shared at **Full Access** and keeps the right to remove the
  designation; anyone else is shared at **Collaborate**. Never less than Collaborate, never more than Full Access, never
  Assign. If those rights cannot be read, the call is refused before any change (500
  `sdap.provision.caller_rights_unverifiable` — provisioning's own code, round 53 item 1; the same caller may retry, and
  the ribbon shows "Which access you hold on this {record} could not be read, so securing it could not make sure you keep
  that access. Nothing was changed; you may try again."). **A Make Secure that fails after its first write
  can always be finished from the same command** (round 40 item 1): a failure the server answers as "the same caller may
  call again" offers that call in place (a confirm dialog with the server's message, Make Secure / Cancel), and the
  command stays offered on the unfinished record; the per-code closure table is task 150's note §23.2. A Make Secure
  that finishes an earlier run runs the full forward rule set before any write (round 46 item 3): WhoAmI and the
  effective rights, the caller's No Access check, the creator read, then the caller's proven share, the creator in the
  colleague step. An administrator who finishes a record through Make Secure is shared to like any caller; the API call
  without the transition (§7, F8) finishes it without adding them. Task 148's transition carries the existing
  children; round 26 item 3's relocation moves the files (task 166's `DocumentContainerRelocator`, wired into this path
  at integration). The scheduled backstop for a file relocation left pending (`files_incomplete`) is task 147's
  `SecureChildReconciliationJob` (every 2 minutes): each run also settles the PENDING Make Secure relocations recorded
  in the relocation ledger through the ONE `DocumentContainerRelocator`, capped per run and reported (round 46 item 2;
  wired at integration once 147, 150 and 166 are all on the integration branch — not 166's migration job, which is
  registered disabled). **Release rule (acceptance (b)): Make Secure is imported only into an environment whose BFF
  carries task 148's transition, the wired file relocation AND that backstop — `SecureChildReconciliationJob`
  registered with its writes on — in the same release** — `Set-AccessRibbon.ps1 -SecureTransitionDeployed`; without
  the switch the group ships without it and `-Verify` fails if it is present.
- **Remove Secure** — shown on a secure record, to a caller with Write. It confirms first (round 33 item 2: "Remove the
  secure designation from this {record}?"), then calls `/unsecure-project`; the SERVER decides who may (F3: Full Access
  holders and the creator) and the refusal shows the endpoint's message.
- Both read `sprk_issecure` — with `sprk_containerid` and `_owninguser_value`, in ONE `Xrm.WebApi.retrieveRecord` —
  (every user's reader-profile Read, step 3); a failed or masked read hides BOTH. Only a flagged, team-owned record
  with a container adds the server's owner answer (above).

Order (release order: round 60 item 2): the BFF (tasks 148 + 150) → the **default-team part** of task 144's migration
(`scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1` dry run, then `-Apply`, §4.3 — a live gate before the ribbon ships,
round 53 item 2; complete when the dry run's plan has **no MIGRATE rows** and the retired default team **no longer holds
the Secure Record Owner role**) → web resources (`access_ribbon.js` 1.5.0, `assignedaccess_postsave.js`, `bff_auth.js`)
→ `Set-AccessRibbon.ps1` dry run, `-Apply`, `-Verify` (its README has the exact commands) → the task 150 POML ui-tests on
the three forms → the migration's **full `-Verify` exit 0**, once Make Secure's "finish" has settled the **NOT-ISOLATED**
rows (user-owned legacy records, records owned by a team outside the Secure Record business unit, flagged records left
before the owner move). The full `-Verify` is not a precondition of the ribbon: the ribbon is the tool that settles those
rows, so gating it on their absence would mean settling them by hand first.

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
| Share an individual CHILD of a secure record (a document, a to-do) with someone | The reconcile removes any child share its root does not carry, within two minutes. Share the secure record itself; its children follow (§7a) |
| Turn on Share / Unshare / Reparent cascade on a project, matter or work-assignment relationship | The setting is TABLE-WIDE: every ordinary record's children would be shared too, a product-wide behaviour change. The owner decided against it (round 11 item 2): the two-minute reconcile is the mechanism (§7a) |
| Unsecure a record in a shared environment before task 148 is deployed there | Its children keep their former sharees and nobody in its business unit can read them (§7a ship gate, owner round 11 item 3). With 148 deployed, unsecure moves them first (§7c) |
| Turn on `SecureChild__Reconciliation__WritesEnabled` without reviewing a report-only run first | The sweep moves the ownership of existing rows in bulk; the report is the evidence (and the reversal record) the owner reviews before any write (§7c.1) |
| Clear `sprk_issecure` by hand on a record whose unsecure answered `children_incomplete` | The flag is what keeps the record's sharees on its still-isolated children and tells the next call to finish; call unsecure again instead (§7c) |
| Disable the `secure-child-share-reconciliation` job in a shared environment | It is the only mechanism for new children and for model-driven-app Share/Unshare of a secure record; disabled, a removed user keeps every child (§7a) |
| Secure `sprk_issecure` before every business unit's default team is on the reader profile, or before the client that stops writing it is live | A reader without Read sees the flag EMPTY and treats a secure record as an ordinary one; an old client's create naming the column is refused for every user (§7d) |
| Add a person or a team to the `Spaarke BFF-Managed Field Writers` profile | Its membership IS the lock: a member can set or clear `sprk_issecure` (and name anyone as a record's creator) outside the endpoints |
| Strip System Administrator from anyone to "close" the residual writer | Owner decision F4 accepts it; the BFF application users hold `prvActOnBehalfOfAnotherUser` through it |

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
| `sprk_issecure` lock (task 150) | [`scripts/Repair-SecureFlagNulls.ps1`](../../scripts/Repair-SecureFlagNulls.ps1), [`scripts/Set-SecureFlagFieldSecurity.ps1`](../../scripts/Set-SecureFlagFieldSecurity.ps1); standing assertion `tests/integration/auth/UnifiedAccessControl/SecureFlagFieldSecurityAssertion.cs`; schema doc `src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md`; note `projects/unified-access-control-r2/notes/task-150-issecure-lock.md` |
| Named owner team + the two invariants | `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/SecureRecordOwnerTeam.cs`; census job `Services/ExternalAccess/SecureRecordIsolationCensusJob.cs`; evaluator `Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs` |
| One-time migration off the default team | [`scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1`](../../scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1); record `projects/unified-access-control-r2/notes/task-144-named-secure-owner-team.md` |
| Sharees of a secure record see its children (§7a) | `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildShareSynchronizer.cs` (+ `SecureChildLineage.cs`, `SecureChildShareReconciliationJob.cs`); record `projects/unified-access-control-r2/notes/task-149-secure-child-sharee-access.md` |
| A work assignment or project filed under a secure record is itself secure (owner round 6) | `src/server/api/Sprk.Bff.Api/Services/Access/SecureRootInheritance.cs` (+ `SecureRootFilingGate.cs`, `SecureRootInheritanceJob.cs`, `SecureChildShareSynchronizer.SyncInheritedRootAsync`); record `projects/unified-access-control-r2/notes/task-158-secure-inherit-filed-records.md` |
