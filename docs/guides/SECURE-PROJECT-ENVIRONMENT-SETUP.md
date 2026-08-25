# Secure Project — Environment Setup Runbook

> **Created**: 2026-08-25 by `unified-access-control-r2` task 046
> **Applies to**: every environment that will hold secure projects (`sprk_project.sprk_issecure = true`)
> **Scope**: the Dataverse *security* configuration only — business unit, owner team, security role.
> Container/SPE provisioning is code (`ProvisionProjectEndpoint`), not setup, and is out of scope here.
> **Verified against**: `spaarkedev1`, 2026-08-25. Every privilege below was determined by experiment
> against live Dataverse, not copied from a design document.

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

A secure project is isolated by **ownership**, not by a per-record rule (Dataverse has no per-record
deny). One business unit holds secure records; that BU's **default owner team** owns them; the team has
**no human members**, so nobody gains access by owning or by business unit. All human access is by
explicit share. The team needs a security role only because **Dataverse refuses to assign a record to a
principal that lacks Read on that entity** — the role exists to make the team a legal assignment target,
nothing more.

---

## 2. Prerequisites

| | |
|---|---|
| Rights | A System Administrator in the target environment |
| Auth | `az login` to the tenant, then a token for the environment (below). `pac` is **not** used — its *active profile* may point at a different environment, which is an easy way to configure the wrong org |
| Config | `SecureProject:BusinessUnitName` in BFF app settings, **or** accept the compiled default |

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

- Default name if `SecureProject:BusinessUnitName` is unset: **`Secure Project`** — **singular**.
  Multiple design documents said `Secure Projects`; that was wrong and would have failed every
  provisioning call closed with "business unit not found". A test pins the value
  (`DefaultSecureBusinessUnitName_IsTheNameActuallyDeployed`).
- Parent: see §6 before choosing. In dev today it is a child of the root BU, which is part of the
  problem, not the design.
- Leave `businessunit.sprk_containerid` **null** on this BU. The BU-cascade container is *shared*
  storage; a secure project must use its own `sprk_containerid`. A non-null value here is a
  misconfiguration.

```powershell
$buName = 'Secure Project'
$bu = (Invoke-RestMethod "$Api/businessunits?`$select=businessunitid,name,sprk_containerid&`$filter=name eq '$buName'" -Headers $H).value
$buId = $bu[0].businessunitid
"BU $buName = $buId ; sprk_containerid = $($bu[0].sprk_containerid)"   # containerid MUST be null
```

If the BU does not exist, create it (parent per §6). **Do not** create one BU per project — that
mechanism was retired.

---

## 4. Step 2 — the owner team (already exists; do not create one)

Every business unit is created with a **default owner team** named after the BU. It requires no
provisioning. Verify rather than create:

```powershell
$team = (Invoke-RestMethod "$Api/teams?`$select=teamid,name,teamtype,isdefault&`$filter=_businessunitid_value eq $buId and isdefault eq true and teamtype eq 0" -Headers $H).value
$teamId = $team[0].teamid
"owner team = $($team[0].name) / $teamId (teamtype=$($team[0].teamtype), isdefault=$($team[0].isdefault))"

# MUST be zero, now and forever.
(Invoke-RestMethod "$Api/teams($teamId)/teammembership_association?`$select=systemuserid" -Headers $H).value.Count
```

`teamtype` must be `0` (Owner). An Access team (`1`) cannot own records.

---

## 5. Step 3 — the `Secure Project Owner` role

### 5.1 The privilege set, and why each entry survived

**`Read` at User (`Basic`) depth, on every entity that carries an `sprk_issecure` column — and nothing
else.** As of 2026-08-25 that is **three** entities, confirmed from live attribute metadata:

| Privilege | Depth | Why it is here |
|---|---|---|
| `prvReadsprk_Project` | **User (`Basic`)** | **Forced by a recorded failure.** With the role reduced to `Write`-only, Dataverse refused the assignment and named it: *"Principal team (Id=…, teamType=0, privilegeCount=5) is missing prvReadsprk_Project privilege"*. |
| `prvReadsprk_Matter` | **User (`Basic`)** | Same rule, different entity. **Found the hard way**: a first cut of this role covered only `sprk_project`, and assigning a *Matter* to the team failed — the workaround was to pile broad roles (`Spaarke Basic User` etc.) onto the owner team, which is exactly the over-grant this role exists to eliminate. |
| `prvReadsprk_WorkAssignment` | **User (`Basic`)** | Same rule. Included pre-emptively because `sprk_workassignment` also carries `sprk_issecure`; verified by assigning a probe record. |

**Derive this list from metadata, not from this table** — if a fourth entity gains an `sprk_issecure`
column, the role must gain the matching `Read`, or assignment for that type fails and someone will
"fix" it by over-granting the team:

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
| Anything on **child** entities | Nothing assigns children to this team (see design §5.1d). Granting here would be privilege without a purpose. |
| **Any broad role on the owner team** (`Spaarke Basic User`, `Spaarke AI Analysis User`, …) | If assignment fails, the cause is a **missing `Read` on that entity** — add the one privilege, never a whole role. A broad role on the owner team recreates the System-Administrator posture this runbook removes. |
| **Business Unit / `Deep` / `Global` depth** | `User` depth suffices. Any wider depth lets a future team member read beyond what the team owns, and re-opens the exact hole §6 is about. |

> **`User` depth is not a weaker version of `Business Unit` depth here — it is the correct one.** The
> team owns exactly the secure records, so "records this principal owns" already covers 100% of the
> intended scope. Wider depth adds reach without adding capability.

### 5.2 Create it

```powershell
$body = @{
  name = 'Secure Project Owner'
  'businessunitid@odata.bind' = "/businessunits($buId)"
  description = 'Least-privilege role for the Secure Project default OWNER team. Exists only so that team can be an assignment target for secure sprk_project records. MUST NOT be granted to any user or any other team.'
} | ConvertTo-Json
Invoke-RestMethod -Method Post "$Api/roles" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($body))
$roleId = (Invoke-RestMethod "$Api/roles?`$select=roleid&`$filter=name eq 'Secure Project Owner' and _businessunitid_value eq $buId" -Headers $H).value[0].roleid
```

**Create the role in the secure BU, not the root BU.** A role created in the root BU is auto-copied into
every child BU; a role created in a child BU exists only there and can only ever be assigned to
principals in that BU. That containment is a feature — keep it.

### 5.3 Grant the one privilege

Resolve each entity's `Read` privilege from **entity metadata** rather than hard-coding names — the
casing follows the schema name (`prvReadsprk_WorkAssignment`, not `prvReadsprk_workassignment`), which
is easy to get wrong by hand:

```powershell
$securable = @('sprk_project','sprk_matter','sprk_workassignment')   # re-derive per §5.1
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

```powershell
# Run this AFTER every AddPrivilegesRole call.
$keep = @('prvReadsprk_Project','prvReadsprk_Matter','prvReadsprk_WorkAssignment')   # per §5.1
foreach ($p in (Invoke-RestMethod "$Api/roles($roleId)/roleprivileges_association?`$select=privilegeid,name" -Headers $H).value) {
    if ($keep -contains $p.name) { continue }
    $rb = @{ Privilege = @{ '@odata.type'='Microsoft.Dynamics.CRM.privilege'; privilegeid=$p.privilegeid } } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Post "$Api/roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" -Headers $H -Body ([Text.Encoding]::UTF8.GetBytes($rb))
}
```

### 5.5 Assign to the team, then remove System Administrator — in that order

Keep the working configuration until the new one is proven.

```powershell
# a) assign the new role
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
> | Read the same Matter after reassigning it to the `Secure Project` BU/team | ✅ **DENIED** |
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
| 1 | Role privileges: `roles(<id>)/roleprivileges_association` | **`Read` on every `sprk_issecure` entity and nothing else** — as of 2026-08-25 exactly 3 (`prvReadsprk_Project`, `prvReadsprk_Matter`, `prvReadsprk_WorkAssignment`), each at depth mask `1` |
| 2 | Team roles: `teams(<id>)/teamroles_association` | **exactly 1** — `Secure Project Owner`. **No `System Administrator`, and no broad role** (`Spaarke Basic User` etc.) |
| 2b | Assignment works for **each** securable type, not just projects | assign a probe `sprk_project`, `sprk_matter` **and** `sprk_workassignment`. A role covering only one type fails silently on the others until someone over-grants the team |
| 3 | Team members: `teams(<id>)/teammembership_association` | **0** |
| 4 | Role holders: `roles(<id>)/systemuserroles_association` | **0 users** |
| 5 | Role holders: `roles(<id>)/teamroles_association` | **exactly 1 team** — the secure BU's default owner team |
| 6 | **Assignment works** — create a probe `sprk_project` with `sprk_issecure=true`, `PATCH ownerid@odata.bind → /teams(<teamId>)` | succeeds, **and** `owningbusinessunit` flips to the secure BU. Must be re-run **after** removing System Administrator |
| 7 | **🔴 Isolation works** — impersonate a known non-admin user (`MSCRMCallerID: <userid>`) and `GET` the probe record | **DENIED.** A successful read means §6 has not been satisfied |
| 8 | Delete the probe record | no `sprk_issecure=true` rows remain |

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

## 8. What must NOT be done

| ❌ | Why |
|---|---|
| Add a human to the secure owner team | The team owns every secure project; a member reads all of them by membership. The whole safety argument is that it is memberless |
| Grant `Secure Project Owner` to a user or any other team | Same reason. Items 4–5 of §7 assert this |
| Add privileges "for completeness" | Every privilege must be forced by a recorded failure. Nothing beyond `Read` was |
| Widen the depth beyond `User` | Adds reach without adding capability, and re-opens §6 |
| Remove `sprk_project` Read from ordinary user roles | Silently disables all sharing (§6) |
| Create a service account to own secure records | Not needed — the default owner team costs no licence, no credential, no identity to audit |
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
| Container isolation (separate, unresolved) | design §5.1c → project `spaarke-secure-project-r1` |
| NFR-05 assertion wording | `projects/unified-access-control-r2/spec.md` |
| Provisioning code | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs` |
