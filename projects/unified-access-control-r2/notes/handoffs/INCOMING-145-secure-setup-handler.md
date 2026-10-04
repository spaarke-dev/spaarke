# INCOMING (to customer-provisioning-orchestration-r1) — Secure Record setup handler

> **From**: unified-access-control-r2 task 145 (GitHub #1046), 2026-10-01
> **To deliver**: the UAC-r2 main session, to the customer-provisioning-orchestration-r1 session (owner of the L2
> control plane and its handler DAG). UAC-r2 does **not** edit control-plane code.
> **Owner decision F10 = (a)** (session 27): the per-environment Secure Record setup is a **control-plane handler step**,
> not a hand-edited runbook. A runbook step is the hand-edited path that produced the 32-privilege drift the owner had
> removed on 2026-10-01 (F1).

## 1. Why a handler, and why the solution cannot do it

Every environment that may hold secure projects, matters or work assignments needs five things that **no solution
import can create**:

| # | Thing | Why a solution cannot ship it |
|---|---|---|
| 1 | The **Secure Record** business unit (no users, ever) | Business units are data, not solution components |
| 2 | The **named, non-default Owner team** `Secure Record Owners`, memberless (task 144, owner F9) | Teams are data |
| 3 | The **`Secure Record Owner`** role, created **inside** the Secure Record BU | A solution-shipped role is created in the root BU and replicated into every child BU, so it is assignable everywhere. The guide (§5.2) requires it to be created IN the secure BU, where it exists only there |
| 4 | That role holding **Read at User (`Basic`) depth on exactly the tables in `config/secure-record-owner-role.json`**, and nothing else | Role privileges for a BU-local role are set by API |
| 5 | The role assigned to the named team **alone** (not to the BU's default team, no System Administrator on either team) | Team-role assignments are data |

Today none of this is in the pipeline: `grep "Secure Record"` over `src/server/services` returns nothing, and
`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` never mentions it. A new customer environment therefore has no secure
isolation anchor at all, and every BFF secure path refuses there (`sdap.provision.secure_owner_team_not_found`).

## 2. Proposed handler

- **Id**: `H7b` — "Secure Record setup" (name is the provisioning project's call; `H7b` is a proposal).
- **Folder**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SecureRecordSetup/`.
- **DAG**: `[H7b] = { H6 }` — it needs the solution's tables (each table's Read privilege comes from entity metadata,
  which exists only after H6). It is independent of H7, so it may run beside it.
  **Recommendation:** also make acceptance wait for it — `[H13] = { H14, H7b }` — so a run cannot pass E2E with no
  secure anchor. The 3-file registration dance applies (`HandlerIds.cs`, `HandlerDispatchRegistrationModule.cs`,
  `Worker/Program.cs`); `HandlerRegistrationCompletenessTests` moves to N+1.
- **Auth**: the SAME identity H6/H7 already use against the new environment's Dataverse (no new secret, ADR-028 A4;
  I1 explicit tenantId). It needs System Administrator in the target environment (creating a BU, a team and a role,
  and editing role privileges all require it).

### 2.1 Inputs

| Input | Source | Notes |
|---|---|---|
| `tenantId` | envelope (I1) | Explicit. Reject when missing (MissingUpstreamState, Resumable) |
| `customerId`, `runId` | envelope | Cosmos partition key `/customerId` (I3) |
| `dataverseEnvUrl` | InterStepState (H5) | Pin it explicitly; never an ambient/active profile (guide §2) |
| `secureBusinessUnitName` | run parameter, default `Secure Record` | **MUST equal the BFF app setting `SecureRecord:BusinessUnitName`** (compiled default `Secure Record`). Feed both from ONE run parameter. Today neither key is in `scripts/canonical-secret-catalog/manifest.yaml` `per_env_settings` (the BFF uses its compiled defaults); if the parameter is ever non-default, add both keys there so H4b sets them |
| `secureOwnerTeamName` | run parameter, default `Secure Record Owners` | Same rule, BFF key `SecureRecord:OwnerTeamName` (fail-closed lookup key, task 144) |
| The codified set | `config/secure-record-owner-role.json` | **Linked as an `EmbeddedResource`** into `Sprk.Provisioning.ControlPlane.Core.csproj`, exactly as that project already links `scripts/canonical-secret-catalog/manifest.yaml` and the seed-data JSON. ONE file, never copied. The control plane has no project reference to the BFF, so it needs its own small reader: copy the validation rules of `Sprk.Bff.Api/Infrastructure/Dataverse/SecureRecordOwnerRoleSet.cs` (schemaVersion 1; access `Read` and depth `Basic` only; ≥1 table; no duplicates; every entry has logicalName, privilegeName, reason, evidence). Add a parity test that both readers accept the live file and refuse the same seeded bad documents |
| `dryRun` | run parameter, default false | Dry run reads everything and returns the plan; zero writes |

### 2.2 Steps — each READS first and writes only what is missing

| Step | Read | Write only when needed | Refuse (Failure) when |
|---|---|---|---|
| S0 org setting | `organizations?$select=sharetopreviousowneronassign` | — | `true`: every assignment into the team would share the record back to its previous owner. Org-wide setting, operator decision; do not flip it silently |
| S1 business unit | `businessunits?$filter=name eq '<bu>'` | 0 found → create under the ROOT BU (`_parentbusinessunitid_value eq null`) | >1 found (ambiguous, task 144 rule) |
| S2 no users | `systemusers?$filter=_businessunitid_value eq <bu>` count | — | >0. Moving users is an owner decision (task 144); never move anyone |
| S3 named team | `teams?$filter=_businessunitid_value eq <bu> and name eq '<team>' and teamtype eq 0 and isdefault eq false` | 0 → create (`teamtype` 0, Owner) | >1; or members (`teammemberships`) >0 of ANY kind |
| S4 role | `roles?$filter=name eq 'Secure Record Owner' and _businessunitid_value eq <bu>` | 0 → create in the BU (guide §5.2) | >1; or the match is a REPLICA (`_parentrootroleid_value` ≠ `roleid`, i.e. created in an ancestor BU) — same refusal as the script |
| S5 grant | per table: `EntityDefinitions(LogicalName='<t>')/Privileges`, `PrivilegeType eq 'Read'`; then `RetrieveRolePrivilegesRole` | each MISSING table's Read → `AddPrivilegesRole` at `Basic` | metadata name ≠ the file's `privilegeName` (ordinal — the file is wrong or the table was renamed); a listed privilege held at a depth other than `Basic` (never widen, never narrow — owner decision) |
| **S6 strip (§5.4)** | `roles(<id>)/roleprivileges_association` | each privilege NOT in the file → `RemovePrivilegeRole` (parameter is an entity reference named `Privilege`, not a GUID `PrivilegeId`) | — |
| S7 assign | `roles(<id>)/teamroles_association` | role not on the named team → associate | — |
| S8 contain | default team's and named team's `teamroles_association` | remove `Secure Record Owner` from the BU's DEFAULT team; remove `System Administrator` from both teams (a new BU's default team can arrive holding it — guide §5.5) | — |
| S9 verify | re-read all of the above | — | role privileges ≠ the file exactly (name AND count, all `Basic`); holders ≠ {named team}; team members ≠ 0; BU users ≠ 0 |

**S6 MUST run after S5 every time, including when S5 added nothing.** Two verified platform behaviours (guide §5.4):
creating a role silently adds ~9 privileges at Global (SDK-message/plugin reads and the SharePoint four), and
**`AddPrivilegesRole` RE-INJECTS the SharePoint four every time it runs**. `ReplacePrivilegesRole` does NOT remove the
SharePoint four — do not use it. The strip's keep-list is the file's `tables[].privilegeName`, never a literal list
(the old literal three-name list would have removed `prvReadsprk_Document`).

### 2.3 Idempotency, dry run and "second apply changes nothing"

- Level-3 key: `secure-setup-{customerId}-{setHash}`, where `setHash` is a SHA-256 of the file's sorted
  `tables[].privilegeName`. Extending the codified set (tasks 145/146) changes the hash, so the next run re-applies.
- Every step is read-then-write; a second run against a configured environment performs **zero writes**. Pin it with a
  `HandlerIdempotencyTests` case that counts writer calls on the second run (expected 0), and a dry-run case that
  asserts zero writer calls on the first.
- §4C classes: every failure is **Resumable** except S2 (users in the BU) and S3 (team has members), which are
  **QuarantineRequired** — they need an owner decision, not a retry.

### 2.4 Rejection codes (proposal)

`secure_setup.org_sharetopreviousowner_on`, `secure_setup.bu_ambiguous`, `secure_setup.bu_has_users`,
`secure_setup.owner_team_ambiguous`, `secure_setup.owner_team_has_members`, `secure_setup.role_ambiguous`,
`secure_setup.role_is_replica`, `secure_setup.privilege_name_mismatch`, `secure_setup.privilege_wrong_depth`,
`secure_setup.verify_failed`, plus the shared auth/rate-limit/unknown codes H7 uses.

### 2.5 Seam and tests

- One seam `ISecureRecordSetupDataverse` (production Web API impl + test fake), mirroring
  `IEnvVarValuesWriter` / `DataverseWebApiEnvVarValuesWriter` (ADR-010: two implementations).
- Tests in `tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests/Handlers/SecureRecordSetup/`: empty env → full plan;
  configured env → no writes; each refusal row above; the S6 strip removes injected privileges and keeps every file
  entry; dry run writes nothing. No `Mock<HttpMessageHandler>` (ADR-038).

## 3. After the handler: acceptance

H13 (or the operator) triggers the BFF's read-only census, `POST /api/admin/jobs/secure-record-isolation-census/trigger`
then `GET …/status`, and requires `isolated`. That census is the NFR-05 evaluator (`SecureBuRoleDepthAssertion`); since
task 145 its clause 5 also fails, naming the table, when the role lacks any table in the codified set. Reusing it means
the pipeline and the BFF cannot disagree about what "set up" means.

**What the handler cannot fix**: NFR-05 clause 1 (no ordinary role may reach the secure BU by Deep/Global depth from
an ancestor BU — guide §6). That is the environment's user/role topology; the census reports it, and moving users or
narrowing roles is an owner decision.

## 4. Open items to carry into the design

1. 🔶 **Cascade-Assign and the SharePoint four.** `sprk_project` and `sprk_matter` cascade **Assign** to `team`,
   `sharepointdocumentlocation` and `sharepointdocument` (live metadata, spaarkedev1, 2026-10-01). The 2026-10-01
   strip removed the SharePoint four (`prvReadSharePointData` etc.) from the role. Whether Dataverse's owner Read
   check then refuses assigning a root that HAS SharePoint location children is **untested**: dev has 0
   `sharepointdocumentlocation` rows, so the F1 probes (fresh creates) could not exercise it. Before the handler's S6
   strip is unconditional in an environment with SharePoint integration, run the negative probe (assign a probe
   project with one location row to the team, with and without `prvReadSharePointData`). If it is refused, the
   SharePoint Reads join the codified set with that refusal as evidence — they do NOT get kept by a literal exception.
2. **Task 150 extends this step (2026-10-02)** — the field-level security of the columns only the BFF writes
   (`sprk_issecure`, and task 133's `sprk_createdbyperson`), on `sprk_project`, `sprk_matter`, `sprk_workassignment`.
   ONE mechanism, the profiles task 133 created; never a second set:

   | Step | Read | Write only when needed | Refuse (Failure) when |
   |---|---|---|---|
   | S10 profiles | `fieldsecurityprofiles?$filter=name eq 'Spaarke BFF-Managed Field Readers'` / `… Writers'` | 0 → create (in SpaarkeCore) | >1 of either |
   | S11 reader members | every `teams?$filter=isdefault eq true` vs the reader profile's `teamprofiles_association` | each missing default team → associate. **A business unit created later is a re-run of this step** (the new-BU step) | — |
   | S12 writer members | `systemusers?$filter=applicationid eq <bff app id>` (the BFF identity H-step that creates the application user — D-13: per-customer registration, never hard-coded) vs `systemuserprofiles_association` | missing BFF app user → associate | any OTHER member (human, other application user, any team) — **QuarantineRequired**: the membership is the lock |
   | S13 NULL flags | `<set>?$filter=sprk_issecure eq null&$top=1` per table | each NULL → `false` (a new environment has none; an upgraded one has the pre-column rows — `scripts/Repair-SecureFlagNulls.ps1`) | — |
   | S14 lock | `EntityDefinitions(…)/Attributes(LogicalName='sprk_issecure')?$select=IsSecured` per table | not secured → `IsSecured = true`, then AT ONCE the reader (read=4) and writer (read/create/update=4) `fieldpermissions` (retry 0x8004f508 — securing propagates asynchronously) | any other profile with create/update=4 on the column (besides System Administrator — owner decision F4) |
   | S15 verify | the standing assertion's census (`SecureFlagFieldSecurityAssertion`) | — | any finding |

   **Order constraint, binding**: S14 runs only after the BFF and the client that no longer write `sprk_issecure` are
   deployed to the environment (in a NEW environment there is no older client, so this is automatic; on an upgrade it
   is task 150 step 6). S11/S12 before S14, always: securing first masks the column for every reader. S13 runs
   BEFORE the environment receives any BFF build containing task 150 (that BFF refuses an EMPTY flag), so on an upgrade
   the handler — or the operator, until it exists — runs S13 ahead of the BFF deploy, not with the lock; no deploy
   script checks it (task 150 r1, verifier item 11).
   Reference implementation: `scripts/Set-RecordCreatorPersonSchema.ps1` (S10–S12) + `scripts/Set-SecureFlagFieldSecurity.ps1`
   (S14, preconditions = S10–S13) + `scripts/Repair-SecureFlagNulls.ps1` (S13).
3. The dev environment itself is not yet in the target shape (see `../task-145-secure-owner-role-privileges.md` §2):
   the named team does not exist and the role is on the default team (task 144's §4.3 cutover is pending), so the
   handler's S8 would be the cutover's step 4 there. Run the cutover by hand in dev first; the handler is for new
   environments.

## 5. Until the handler ships — the interim operator runbook

`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md` §3 (BU) → §4.2 (named team) → §5.2 (role) → §5.3
(`scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl <url>` dry run, then `-Apply`) → **§5.4 strip** →
§5.3 `-Verify` (exit 0) → §5.5 (assign to the named team; remove System Administrator) → §7 checks 1–11 → **§7b**
(`Set-RecordCreatorPersonSchema.ps1`: the column plus both BFF-managed field profiles and their members) → **§7c**
(task 150: `Repair-SecureFlagNulls.ps1`, then `Set-SecureFlagFieldSecurity.ps1`, then the field-security assertion). The live
NFR-05 run needs `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` on a workstation where only `az login` is available
(plain `DefaultAzureCredential` resolved only `EnvironmentCredential` there).

## 6. Production business-unit topology (owner, 2026-10-02): binding, with one gap in the control plane today

The owner stated the production model on 2026-10-02 (`notes/session27-owner-decisions-and-research.md`, round 5):
- **Users** are assigned to the customer's NAMED CHILD business unit, never to the root BU.
- **New records** are assigned to the creating user's business unit/team. That includes records created server-side by the BFF, whose Dataverse application user is assigned to the CUSTOMER business unit.
- `docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md` §6 "Fix A" is the same design and was validated in dev on 2026-08-25.

**Why it matters.** `Deep` read held at a business unit reaches every unit beneath it. If any person, or any team they belong to, holds `Deep`/`Global` read at the root BU or at any ancestor of the Secure Record BU, secure isolation is silently void. Dev showed this on 2026-10-02: the root BU's default team was given the full "Spaarke Basic User" role, which exposed every secure record to all 171 root-BU members. Server-side creates also break when the BFF application user sits in the root BU: `RecordOwnershipResolver` falls back to the caller's BU default team, and the ROOT default team holds no privileges, so Dataverse refuses it as an owner (#1081).

**Requested handler behaviour** (H7b, or wherever the owner places it):

| Step | Rule |
|---|---|
| T1 customer BU | Create the customer's named business unit as a child of the ROOT BU if it is absent. Its name comes from the run (for example the customer display name). Refuse if more than one unit matches. |
| T2 Secure BU parent | The Secure Record BU (S1) is a **direct child of the ROOT BU**, a sibling of the customer BU. **Refuse** if an existing Secure Record BU has any other parent, especially the customer BU, because users there would reach it at `Deep`. |
| T3 BFF application user | ⚠️ **Gap today:** `DataverseWebApiAppUserCreator` (`src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/DataverseAppUserGraphParity/DataverseWebApiAppUserCreator.cs`, step "(2) Resolve root business unit") creates the BFF application user in the **ROOT** BU. It must be created in, or moved to, the **customer BU**. Re-runs must be idempotent and must verify the application user's `businessunitid`. |
| T4 root default team | Verify that the ROOT BU's default team holds **no role with `Deep` or `Global` read** on `sprk_project` / `sprk_matter` / `sprk_workassignment` (or on any `config/secure-record-owner-role.json` table). Refuse (or at least report CRITICAL) otherwise. |
| T5 humans | Operator runbook: assign people to the customer BU, never the root BU. The UAC census job (`secure-record-isolation-census`, task 144) already reports any human who reaches the Secure BU. |

**Dev differs today, and that is accepted:** all four BFF application users and the test users sit in the root BU, and the root default team holds "Spaarke Basic User". The owner classed this as a dev data artifact (round 5).
