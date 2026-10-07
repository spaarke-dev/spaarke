# sprk_accessevent — Access Event Log

> **Entity Purpose**: The **append-only log of access GRANT and DENY state changes** (spec FR-32,
> design.md §7). Every explicit grant, revoke, POA share/unshare, deny-list add/remove and
> secure/restricted/standing-grant flag flip that passes through a BFF writer lands here as one
> immutable row. It is the *explicit-event backbone* of attestation; the *derived* half of
> "who could see record X on date D" is answered by **evaluator replay** (task 088), never by rows
> in this table.
>
> **Schema Version**: 1.0
> **Created**: 2026-09-09
> **Project**: `unified-access-control-r2` (task 086 — FR-32 schema half)
> **Status**: 🟡 **AUTHORED, NOT DEPLOYED.** As of **2026-09-09** this table does **not** exist in
> `spaarkedev1.crm.dynamics.com` — verified by
> `GET EntityDefinitions(LogicalName='sprk_accessevent')` → `404 NotFound`. Deployment is an
> explicit **operator** step: run
> [`scripts/Deploy-AccessEventEntity.ps1`](../../scripts/Deploy-AccessEventEntity.ps1)
> (start with `-DryRun`). This document is the *contract* the script writes and task 087's appender
> reads — not a record of a live environment. Re-verify against live metadata after deployment and
> flip this banner.

**Related tables** (documented alongside their solution, not here — see *Where the sibling docs live* at the end):

| Table | Role | Doc |
|---|---|---|
| `sprk_externalrecordaccess` | Current-state grants (no history) | [`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md`](../../src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md) |
| `sprk_noaccessentry` | Current-state deny list (no history) | [`src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md`](../../src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md) |
| **`sprk_accessevent`** | **History of changes to both** | this document |

---

## 1. Entity definition

| Property | Value |
|---|---|
| **Logical Name** | `sprk_accessevent` |
| **Schema Name** | `sprk_AccessEvent` |
| **Collection Name** (Web API entity set) | `sprk_accessevents` |
| **Display Name** | Access Event |
| **Plural Display Name** | Access Events |
| **Primary Name Field** | `sprk_name` |
| **Ownership Type** | **Organization**-owned |
| **Audit** | **Enabled on the table itself** (see §5) |
| **Notes / Activities** | Disabled |
| **Solution** | `SpaarkeCore` (unmanaged), publisher **Spaarke**, prefix `sprk` |

**Why Organization-owned**: mirrors `sprk_noaccessentry` and matches what the row *is* — an
institutional record of a governance action, not something a user "owns". Organization ownership
also removes owner-reassignment as a way to mutate a log row.

---

## 2. Columns

Eighteen columns: one primary name, three choices, thirteen scalars, one memo. "Smallest possible
table" (design §3) means every column below is required by at least one event kind — the coverage
matrix in §4 is the proof.

### 2.1 Identity

| Logical Name | Display | Type | Required | Len | Notes |
|---|---|---|---|---|---|
| `sprk_accesseventid` | Access Event | Uniqueidentifier | Auto | — | Primary key |
| `sprk_name` | Name | String | ApplicationRequired | 300 | One-line human summary composed by the appender, e.g. `Grant Created - Jane Doe -> Acme Litigation (Collaborate)`. **No auto-naming plugin exists** — task 087's appender writes it. |

### 2.2 What happened

| Logical Name | Display | Type | Required | Notes |
|---|---|---|---|---|
| `sprk_eventtype` | Event Type | Choice (local) | ApplicationRequired | The closed 9-value vocabulary — §3.1. The appender may not invent values. |
| `sprk_occurredon` | Occurred On | DateTime (`DateAndTime`, **UTC** behavior) | ApplicationRequired | The instant the **state change** happened (domain time). Deliberately distinct from `createdon` (row-insert time): they diverge on retried writes and on batched cascades. **Replay orders on this column**, not on `createdon`. |

### 2.3 Who (subject principal — the party whose access changed)

| Logical Name | Display | Type | Required | Len | Notes |
|---|---|---|---|---|---|
| `sprk_subjectkind` | Subject Kind | Choice (local) | ApplicationRequired | — | Contact / Organization / System User / Team — §3.2 |
| `sprk_subjectid` | Subject Id | String | ApplicationRequired | 50 | GUID **as text** — see §2.7 for why this is not a lookup |
| `sprk_subjectname` | Subject Name | String | None | 200 | **Snapshot** of the display name at event time |

### 2.4 What on (target record)

| Logical Name | Display | Type | Required | Len | Notes |
|---|---|---|---|---|---|
| `sprk_targetentityname` | Target Entity Name | String | ApplicationRequired | 100 | Logical name, e.g. `sprk_project`, `sprk_matter`, `sprk_workassignment`, `sprk_communication`; `contact` for standing-grant events (the flag lives on the contact) |
| `sprk_targetrecordid` | Target Record Id | String | ApplicationRequired | 50 | GUID **as text** |
| `sprk_targetrecordname` | Target Record Name | String | None | 400 | **Snapshot** of the display name at event time. 400 because matter/project names run long |

### 2.5 How much (level / mask — populated only where the event kind has one)

| Logical Name | Display | Type | Required | Len | Notes |
|---|---|---|---|---|---|
| `sprk_accesslevel` | Access Level | Choice (local) | None | — | The **literal** `sprk_externalrecordaccess.sprk_accesslevel` on the grant row created or revoked. Mirrors that column's values exactly (§3.3). Empty for share / deny / flag events. |
| `sprk_sharedrights` | Shared Rights | String | None | 100 | The **literal** Dataverse `AccessRights` mask handed to the POA call, e.g. `ReadAccess,WriteAccess`. Empty for non-share events. |

> 🔴 **Neither column is a derived-access column.** Both record *what the operation asked for*, at
> the moment it was asked. Neither is an evaluator output, and nothing reads them as effective
> access. See §6.

### 2.6 Before/after (flag-flip events only)

| Logical Name | Display | Type | Required | Len | Notes |
|---|---|---|---|---|---|
| `sprk_previousvalue` | Previous Value | String | None | 100 | Before-value as text: `true`/`false` for the two booleans; `100000002 (Restricted)` for `sprk_accesspermission` |
| `sprk_newvalue` | New Value | String | None | 100 | After-value, same encoding |

These exist so the three flag-change kinds have **dedicated columns** for their core fact rather
than depending on the JSON column (acceptance criterion: "no free-form-JSON-only event kinds for
core fields").

### 2.7 Who did it, and provenance

| Logical Name | Display | Type | Required | Len | Notes |
|---|---|---|---|---|---|
| `sprk_actinguserid` | Acting User Id | String | None | 50 | `systemuser` GUID as text. **Empty** when the change came from the app-only identity with no resolvable calling user (background / cascade paths) — the same "omit rather than fail" posture `sprk_externalrecordaccess.sprk_grantedby` already takes |
| `sprk_actingusername` | Acting User Name | String | None | 200 | **Snapshot**; names the app registration for system-initiated changes |
| `sprk_evaluatorversion` | Evaluator Version | String | ApplicationRequired | 50 | The evaluator's version string current when the event was written. Task 088 owns the constant and its bump policy; replay uses this to disclose when it answers a historical date with a **newer** evaluator than the one live on that date |
| `sprk_correlationid` | Correlation Id | String | ApplicationRequired | 100 | BFF request correlation id (same source `AuditEnrichmentMiddleware` uses). Ties the N events of one cascade — e.g. the FR-09 revoke-all — into one operation |
| `sprk_details` | Details | Memo | None | 4000 | Free-form JSON **supplement**: endpoint route, admin-supplied reason, the grant row's id, the cascade parent. Supplement only — no event kind may depend on it for a core fact |

#### Why the subject and target are strings, not lookups

Two reasons, both load-bearing:

1. **Polymorphism.** A subject is a contact, an organization, a systemuser or a team; a target is
   *any* record type. Modelling that with lookups means one nullable lookup per possible type. The
   ADR-024 dual-field (`type` + `id`) strategy is designed for **small closed** parent sets; this is
   open-ended — the same reasoning `sprk_noaccessentry.sprk_objectrecordid` already applies.
2. **Immutability.** A lookup configured `RemoveLink`-on-delete **nulls itself out** when the
   referenced record is deleted — silently destroying the very history this table exists to hold.
   `Restrict` is worse: a log row would block deletion of an unrelated contact forever. A string id
   plus a name snapshot survives both rename *and* deletion. That is also why the name-snapshot
   columns exist at all.

> Consequence, stated honestly: **there is no referential integrity on `sprk_subjectid` /
> `sprk_targetrecordid`.** A malformed id is not rejected by the platform. The appender (087) is the
> only writer and its tests are the enforcement.

#### 🔴 GUID canonicalization (ADR-044) — binding on the appender

`sprk_subjectid`, `sprk_targetrecordid` and `sprk_actinguserid` are `Edm.String` columns holding
GUIDs, so they are exactly the boundary [ADR-044](../../.claude/adr/ADR-044-dataverse-guid-canonicalization.md)
governs. The appender **MUST** write them **bare and lowercase** — no braces, no uppercase — because
replay (task 088) and any attestation query find rows by string `eq` on these columns, and a
brace-wrapped or upper-cased id written once produces a row that later queries silently miss. There
is no platform-side normalization to fall back on: this is a plain string column, not a lookup.

The same rule applies to the target/subject ids that appear inside `sprk_details` JSON. A 087 unit
test should pin it (round-trip a registry-format `{ABC-…}` input and assert the persisted value is
bare-lowercase), because it is invisible until a query returns nothing.

### 2.8 System columns

| Logical Name | Type | Notes |
|---|---|---|
| `statecode` / `statuscode` | State / Status | Present because Dataverse creates them. **Rows are never deactivated** — see §5. Anything reading this table should ignore `statecode`, not filter on it |
| `createdon` / `createdby` | DateTime / Lookup | Row-insert time and inserting identity. `createdon` ≠ `sprk_occurredon` (§2.2) |
| `modifiedon` / `modifiedby` | DateTime / Lookup | **A row whose `modifiedon` ≠ `createdon` has been tampered with** — see §5 |

---

## 3. Choice values

All three option sets are **local** to this table, not global. Rationale (CLAUDE.md §11 — justify
new shared surface): each vocabulary has exactly one consumer, this table. A global option set
would add tenant-wide surface with no second reader. `sprk_accesslevel` deliberately **mirrors**
`sprk_externalrecordaccess.sprk_accesslevel`'s values rather than reusing its option set, so a
future change to the live grant vocabulary cannot silently rewrite the meaning of historical log
rows.

### 3.1 `sprk_eventtype`

| Value | Label | Written when |
|---|---|---|
| 100000000 | Grant Created | A `sprk_externalrecordaccess` row is created (contact grant or org-wide grant) |
| 100000001 | Grant Revoked | A `sprk_externalrecordaccess` row is deactivated — including each row of a closure cascade |
| 100000002 | Share Granted | A Dataverse POA share is granted (`GrantAccess` / `ModifyAccess`) |
| 100000003 | Share Revoked | A POA share is removed (`RevokeAccess`) |
| 100000004 | Deny Added | A `sprk_noaccessentry` veto is created |
| 100000005 | Deny Removed | A `sprk_noaccessentry` veto is deactivated |
| 100000006 | Secure Flag Changed | A root record's `sprk_issecure` is set or cleared **through a BFF writer** |
| 100000007 | Restricted Flag Changed | A root record's `sprk_accesspermission` changes **through a BFF writer** |
| 100000008 | Standing Grant Changed | `contact.sprk_standinggrant` changes **through a BFF writer** |

The "through a BFF writer" qualifier is not decorative — see §7.

### 3.2 `sprk_subjectkind`

| Value | Label | Id column holds |
|---|---|---|
| 100000000 | Contact | `contact.contactid` — an external CIAM principal |
| 100000001 | Organization | `sprk_organization.sprk_organizationid` — an org-wide grant or org-scoped deny |
| 100000002 | System User | `systemuser.systemuserid` — internal POA share |
| 100000003 | Team | `team.teamid` — POA share to a team |

### 3.3 `sprk_accesslevel`

Mirrors `sprk_externalrecordaccess.sprk_accesslevel` (values verified live 2026-09-09):

| Value | Label |
|---|---|
| 100000000 | View Only |
| 100000001 | Collaborate |
| 100000002 | Full Access |

---

## 4. Event-kind × column coverage matrix

The acceptance check for this schema: *every kind task 087 writes has the columns it needs, and no
kind depends on `sprk_details` for a core fact.* ● = always populated · ○ = populated when
resolvable · — = not applicable.

| Column | Grant Created | Grant Revoked | Share Granted | Share Revoked | Deny Added | Deny Removed | Secure Flag | Restricted Flag | Standing Grant |
|---|---|---|---|---|---|---|---|---|---|
| `sprk_name` | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `sprk_eventtype` | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `sprk_occurredon` | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `sprk_subjectkind` | ● | ● | ● | ● | ● | ● | ●¹ | ●¹ | ● |
| `sprk_subjectid` | ● | ● | ● | ● | ● | ● | ●¹ | ●¹ | ● |
| `sprk_subjectname` | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| `sprk_targetentityname` | ● | ● | ● | ● | ●² | ●² | ● | ● | ●³ |
| `sprk_targetrecordid` | ● | ● | ● | ● | ●² | ●² | ● | ● | ●³ |
| `sprk_targetrecordname` | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| `sprk_accesslevel` | ● | ● | — | — | — | — | — | — | — |
| `sprk_sharedrights` | — | — | ● | ●⁴ | — | — | — | — | — |
| `sprk_previousvalue` | — | — | — | — | — | — | ● | ● | ● |
| `sprk_newvalue` | — | — | — | — | — | — | ● | ● | ● |
| `sprk_actinguserid` | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| `sprk_actingusername` | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| `sprk_evaluatorversion` | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `sprk_correlationid` | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `sprk_details` | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ | ○ |

1. **Flag flips have no single subject.** A secure/restricted flip changes the answer for *everyone*
   — enumerating affected principals would be materializing derived access, which is banned (§6).
   The row therefore names the **record** as both target and subject scope: write
   `sprk_subjectkind = Organization` with the record's owning/primary organization when one exists,
   otherwise repeat the target as the subject (`sprk_subjectkind = Organization`,
   `sprk_subjectid` = the record id) and say so in `sprk_details`. Task 087 fixes the exact
   convention and its tests pin it; what this schema guarantees is that **the columns are never
   empty**, so a query can always group flag events by target.
2. **Deny events name the deny entry's OBJECT as the target.** An org-scoped (ethical-wall) deny has
   no single record: write `sprk_targetentityname = sprk_organization` and the organization's id.
   A per-record deny writes the denied record's type + id. Either way the `sprk_noaccessentry` row's
   own id goes in `sprk_details`.
3. **Standing-grant events target the contact**, because that is where the flag lives:
   `sprk_targetentityname = contact`, `sprk_targetrecordid` = the contact id — the same value as
   `sprk_subjectid`. That duplication is intentional and is what makes the row queryable from both
   the "what changed about this principal" and "what changed on this record" directions.
4. `sprk_sharedrights` on **Share Revoked** records the mask that was *removed*.

---

## 5. Append-only posture

**Application code only ever Creates.** There is no update path and no delete path in any Spaarke
component. Specifically:

- Task 087's `AccessEventAppender` exposes a single append operation and issues only `POST`s. It is
  the **only** writer.
- Nothing reads-then-updates a row. There is no correction workflow: a mistake is corrected by
  appending a compensating event, never by editing history.
- Rows are **never deactivated**. `statecode` exists because Dataverse creates it; a reader must
  ignore it rather than filter `statecode eq 0` (the convention `sprk_externalrecordaccess` and
  `sprk_noaccessentry` use *does not* apply here — those tables represent live state, this one
  represents history).

### The posture is documented + role-enforced, not plugin-enforced

There is **no** pre-operation plugin blocking Update/Delete. That is a deliberate scope decision,
not an oversight: a plugin that blocks System Administrator would also block legitimate emergency
remediation and GDPR erasure (§8), and one that does not block System Administrator adds ceremony
without a guarantee. The enforcement is layered instead:

| Layer | Mechanism |
|---|---|
| Code | One appender, create-only, no update/delete surface (087 + its tests) |
| Privileges | The create-only role posture below — the BFF app user *cannot* Write or Delete |
| Evidence | **Table-level auditing is ON.** Rows are never legitimately modified, so *any* audit entry against this table, and any row whose `modifiedon ≠ createdon`, is evidence of tampering. That is the cheap tamper-evidence a "legally defensible" log needs |

### Create-only privilege model (operator step — the deploy script does NOT apply this)

| Principal | Create | Read | Write | Delete | Append/AppendTo | Assign | Share |
|---|---|---|---|---|---|---|---|
| **BFF application user** (app-only identity that runs the appender) | **Organization** | **Organization** | ❌ **None** | ❌ **None** | None | None | None |
| Attestation reader role (compliance / privilege-log reviewer) | None | **Organization** | ❌ None | ❌ None | None | None | None |
| SDAP Admin | None | Organization | ❌ None | ❌ None | None | None | None |
| System Administrator | (inherent) | (inherent) | (inherent) | (inherent) | — | — | — |

The load-bearing cells are the ❌s. Granting Write or Delete to the BFF application user would make
the append-only claim unenforceable, because that identity executes every BFF Dataverse call.
External contacts never touch this table — they have no Dataverse identity (broker-only, ADR-028 A1).

---

## 6. What this table deliberately does NOT store

**No derived / effective / computed access.** Spec FR-32 forbids it and design.md §7 explains why:
materializing derived access reintroduces every staleness and reconciliation failure of a push model
(the critique in `notes/investigation/06-adversarial-critique.md` §F4).

Concretely, the following columns **must never** be added:

- an "effective level" or "effective rights" column,
- a "computed access" / "resolved access" column,
- any column enumerating *which principals* a flag flip affected,
- any per-(principal × record) row that was not itself an explicit state change.

Point-in-time derived access is **computed on demand** by replay (task 088) over this event log plus
Dataverse field audit. The deploy script enforces the rule mechanically: after creation it re-reads
the attribute list and **throws** if any column name matches `effective|computed|derived|resolvedaccess`.

**Also not stored**: read/evaluation events. Only *state changes* are logged; an access *check* is
never an event here (spec FR-32 MUST, restated as a 087 constraint and a negative test).

---

## 7. Replay coverage boundary — which source covers which change

Task 088's replay composes **two** sources. Task 087 owns finalizing this section once its hooks
land; what follows is the boundary as of 2026-09-09, with the live audit configuration verified.

| Change | Covered by | Note |
|---|---|---|
| Grant created / revoked **via the BFF** | This event log | Task 087 hooks `GrantExternalAccessEndpoint`, `RevokeExternalAccessEndpoint`, `InviteAndGrantExternalUserEndpoint`, `ProjectClosureEndpoint` |
| POA share granted / revoked **via the BFF** | This event log | Hooked once at the consolidated POA seam (task 060), not per endpoint |
| Deny entry added / removed **via the BFF** | This event log | Task 064's deny endpoints |
| Secure / restricted / standing-grant flag changed **via the BFF** | This event log | `ProvisionProjectEndpoint`, `UnsecureProjectEndpoint`, and the standing-grant writer |
| The same rows edited **directly in the MDA** (a maker deactivates a grant on the form; an admin flips `sprk_issecure`) | **Dataverse field audit** — *if enabled* | The BFF never sees these. This is the boundary, and §7.1 shows it currently has holes |
| Org membership (`sprk_contactorganization`) changes | Dataverse field audit | Entity audit **enabled** ✅ (verified 2026-09-09) |
| Record lookup/junction changes that move a record into or out of an org's scope | Dataverse field audit | Entity audit enabled on `sprk_project` / `sprk_matter` / `sprk_workassignment` ✅ |

### 7.1 🔴 Live audit configuration — verified 2026-09-09, three gaps found

Environment `spaarkedev1.crm.dynamics.com`; org-level `isauditenabled = True`,
`auditretentionperiodv2` unset (platform default).

| Object | Entity audit | Attribute audit | Verdict |
|---|---|---|---|
| `sprk_project.sprk_issecure` | ✅ True | ✅ True | OK |
| `sprk_project.sprk_accesspermission` | ✅ True | ✅ True | OK |
| `sprk_matter.sprk_issecure` | ✅ True | ✅ True | OK |
| `sprk_matter.sprk_accesspermission` | ✅ True | ✅ True | OK |
| `sprk_workassignment.sprk_issecure` | ✅ True | ✅ True | OK |
| `sprk_workassignment.sprk_accesspermission` | ✅ True | ❌ **False** | 🔴 **GAP 1** |
| `contact.sprk_standinggrant` | ✅ True | ✅ True | OK |
| `sprk_contactorganization` | ✅ True | — | OK |
| `sprk_externalrecordaccess` | ❌ **False** | (`sprk_accesslevel`, `statecode` True — inert) | 🔴 **GAP 2** |
| `sprk_noaccessentry` | ❌ **False** | (`statecode` True — inert) | 🔴 **GAP 3** |

**Attribute-level audit does nothing while entity-level audit is off** — which is why GAP 2 and
GAP 3 are real despite the ✅s in their attribute column.

**What each gap costs replay:**

- **GAP 1** — a Restricted flip on a *work assignment* leaves no trace on any path except a BFF
  hook. There is no BFF writer for that column today, so today it leaves **no trace at all**.
- **GAP 2** — a grant deactivated **directly in the MDA** (not through `RevokeExternalAccessEndpoint`)
  is invisible to both sources: the BFF never saw it, and there is no field-audit history. Replay
  would report the grant as still live after it was revoked — a **confidently wrong** answer, which
  is the one outcome the "honest completeness" constraint exists to prevent.
- **GAP 3** — same failure for deny entries deactivated in the MDA.

**These are prerequisites for task 088, and audit history does not backfill.** Enabling entity audit
on `sprk_externalrecordaccess` / `sprk_noaccessentry` records changes *from that moment forward*;
every change before it is permanently unrecoverable. The fix must land **before** UAT generates the
history 088 is meant to replay. Escalated to the owner; recorded in
[`projects/unified-access-control-r2/notes/task-086-access-event-schema.md`](../../projects/unified-access-control-r2/notes/task-086-access-event-schema.md).

---

## 8. Retention, and the GDPR known gap

**Retention inherits the environment's Dataverse audit/data retention configuration.** There is no
bespoke retention mechanism, no purge job, and no per-row TTL column — an owner decision recorded in
the task brief. Verified 2026-09-09: `organization.auditretentionperiodv2` is unset on
`spaarkedev1`, i.e. the platform default applies.

### GDPR / right-to-erasure — a KNOWN GAP, deliberately not solved here

This table stores **personal data** — `sprk_subjectname` and `sprk_actingusername` are name
snapshots, and `sprk_subjectid` identifies a natural person — and it is **append-only**, so nothing
in the design erases it. The tension is real and is recorded, not resolved:

- An erasure request for a data subject who appears in this log **cannot** be satisfied by the
  application. There is no erasure path, by design (§5).
- Deleting rows would destroy the attestation record that privilege-log and breach-inquiry
  obligations depend on — the two obligations genuinely conflict.
- The plausible resolutions — pseudonymizing the name snapshots on request while retaining the ids,
  or a supervised administrative erasure with its own audit trail — are **not built**. Either is a
  future decision with legal input, not an engineering default.

**Do not build erasure as part of this project** (explicit task-brief instruction). Do not let this
section be read as "handled": it is an accepted, documented gap.

---

## 9. Deployment

```powershell
# 1. Read-only reconnaissance — no writes at all. Always start here.
.\scripts\Deploy-AccessEventEntity.ps1 -DryRun

# 2. Apply (idempotent — safe to re-run; every create is existence-guarded)
.\scripts\Deploy-AccessEventEntity.ps1

# 3. Another environment
.\scripts\Deploy-AccessEventEntity.ps1 -EnvironmentUrl "https://contoso.crm.dynamics.com" -SolutionName "SpaarkeCore"
```

The script POSTs raw Dataverse Web API metadata with the `MSCRM.SolutionUniqueName` header, the same
pattern [`scripts/Deploy-PrecedentEntity.ps1`](../../scripts/Deploy-PrecedentEntity.ps1) uses.

> ⚠️ **Never create this table with `mcp__dataverse__create_table`.** That tool has no
> publisher/solution parameter and silently uses the environment's **default** publisher — which is
> how this repo acquired a stray `cr140_noaccessentry` table
> ([`.claude/FAILURE-MODES.md` AP-13](../../.claude/FAILURE-MODES.md)). **Dataverse prefixes are
> immutable**: a table created under the wrong prefix cannot be renamed, only dropped and rebuilt.
> The script guards this from both ends — it refuses to run unless the target solution's publisher
> carries prefix `sprk`, and it re-reads the created entity and throws unless the live logical name
> is exactly `sprk_accessevent`.

**Not automated, on purpose:**

1. **Security roles.** The create-only posture in §5 is an operator step. The script does not touch
   roles — an incorrect privilege grant here is a security defect, and it should be a deliberate,
   reviewed action.
2. **Views and forms.** None are needed for task 087 (the appender writes via the Web API). Add an
   admin view when a human needs to browse the log.
3. **The audit-configuration fixes** for GAPs 1–3 in §7.1. Those change auditing on *other* tables
   and are an owner decision, not a side effect of creating this one.

---

## Where the sibling docs live

`sprk_externalrecordaccess` and `sprk_noaccessentry` are documented under
`src/solutions/SpaarkeCore/entities/{name}/entity-schema.md`, next to their solution; this document
is in `docs/data-model/` per CLAUDE.md §14 ("Dataverse entity schemas, ERD") and the task's declared
output. The split is pre-existing and is called out here so a reader chasing the access-control
tables finds all three rather than assuming this one stands alone.

---

*Schema version 1.0 · Created 2026-09-09 · Project `unified-access-control-r2` task 086 (FR-32) ·
**Authored, not deployed** — live status verified `404 NotFound` on 2026-09-09.*
