# sprk_project — Secure Project Fields Schema

> **Purpose**: Documents the fields added to sprk_project to support the Secure Project & External Access Platform.
> **Schema Version**: 2.0
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module
> **Corrected 2026-09-08 (`unified-access-control-r2` task 026, review findings C4/C5/M4)**: this document
> is the ROOT CAUSE of the C4/C5 provisioning defects. It documented `sprk_securitybuid` and
> `sprk_externalaccountid` as live columns; **neither exists on `sprk_project`**. Provisioning code
> (`Api/ExternalAccess/ProvisionProjectEndpoint.cs`) was implemented faithfully from this doc, and its
> stamping `$select`/PATCH 400'd silently for **five months** as a result (fixed by tasks 021/026). The
> correct names below are quoted directly from `ProvisionProjectEndpoint.cs`'s `ProjectProvisioningSelect`
> comment, marked in that file as *"verified against live `sprk_project` metadata (2026-08-25)"* — treat
> that file, not this one, as the durable source until this correction has itself been re-verified against
> live metadata by a session with a working Dataverse MCP connection (this correction pass could not query
> Dataverse directly — see the note at the foot of this file).

---

## New Fields (Phase 1 — Secure Project Module)

| Logical Name | Display Name | Type | Required | Default | Description |
|--------------|--------------|------|----------|---------|-------------|
| sprk_issecure | Is Secure Project | Boolean | No | false | Designates this record secure — on `sprk_project`, `sprk_matter` and `sprk_workassignment`. **To be field-secured by `unified-access-control-r2` task 150 — NOT YET LIVE (pending live gates G-3/G-4; spaarkedev1 on 2026-10-03: `IsSecured=false`, 0 `fieldpermission` rows)**. The BFF code is already the only intended writer: inside `/provision-project` (sets it, first write) and `/unsecure-project` (clears it, last write); every user READS it. Drives secure ownership, SPE container placement and the external plane's Secure suppression. See the field specification below. |
| sprk_securitybu | Security Business Unit | Lookup → businessunit | No | — | ⚠️ **CORRECTED** from `sprk_securitybuid`, which does not exist and 400'd every stamping PATCH for five months. Read form: `_sprk_securitybu_value`. **Not actually written by provisioning today** — task 021 re-scoped provisioning onto ONE canonical `Secure Project` business unit resolved by name at request time (see `ProvisionProjectEndpoint.SecureBusinessUnitNameConfigKey`), not a per-project BU, so this field is read only to detect the RETIRED legacy per-project-BU mechanism (`ProjectRow.HasLegacyPerProjectBusinessUnit`) and refuse to re-provision such a project. |
| sprk_externalaccount | External Access Account | Lookup → account | No | — | ⚠️ **CORRECTED** from `sprk_externalaccountid`, which does not exist. This is the **CLIENT** lookup, not an "external access" bookkeeping field — `ProjectLiveFactResolver.cs` and `MatterLiveFactResolver.cs` both map the `client` predicate to it. **Provisioning code (task 021) deliberately never writes this column**: the retired code created a synthetic "External Access — {project}" `account` record and aimed the broken stamping PATCH at this field: had the name been correct, provisioning would have silently overwritten every secure project's real client. A test now fails if this field reappears in any provisioning payload. |
| sprk_containerid | SPE Container ID | Text (NVARCHAR 100) | No | — | 🆕 **ADDED to this doc** — not a lookup, a plain string holding the SPE container id created for this project. Verified live via `ProvisionProjectEndpoint.ProjectProvisioningSelect` and written by `RecordContainerAsync`. This is the field the old code's broken `sprk_specontainerid` name was presumably trying to reach; it was never in this document at all, which is part of why the wrong stamping PATCH went unnoticed — nothing was checking the field actually used for the container reference. **History:** until task 076 (2026-09-03) it was ALSO stamped at project create time from the creating user's business-unit defaults, which is why a non-empty value was once not evidence of provisioning. Task 076 made provisioning's Step 7 the only writer, so task 133 (2026-10-01) uses it as half of the idempotency marker: owned by the secure owner team AND a container recorded = provisioned (409); owned with no container = resumable. See `ProvisionProjectEndpoint.RootRow.IsProvisioned`. |
| sprk_createdbyperson | Created By (Person) | Lookup → systemuser | No | — | 🆕 **ADDED 2026-10-02 (`unified-access-control-r2` task 133, owner round 7 item 2)** — on `sprk_project`, `sprk_matter` and `sprk_workassignment`. The PERSON who created the record, stamped by the BFF on every BFF create path (field-secured: only the BFF writes it). Secure provisioning's resume shares to it when `createdby` is not a usable person (an app-created row). Full spec: [`created-by-person-schema.md`](created-by-person-schema.md). Not yet applied live — `scripts/Set-RecordCreatorPersonSchema.ps1`. |

---

## Field Specifications

### sprk_issecure (Is Secure Project)

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_issecure |
| **Display Name** | Is Secure Project |
| **Type** | Two Options (Boolean) |
| **True Label** | Yes |
| **False Label** | No |
| **Default Value** | No (false) — verified live 2026-10-02 on all three tables |
| **Required Level** | None |
| **Tables** | `sprk_project`, `sprk_matter`, `sprk_workassignment` |
| **Field Security** | **Target configuration — pending live gate G-4, NOT applied yet** (spaarkedev1 re-checked read-only 2026-10-03: `IsSecured=false`, 0 `fieldpermission` rows for the column). Applied by `scripts/Set-SecureFlagFieldSecurity.ps1 -Apply` (task 150), after which it is: Secured; reader profile `Spaarke BFF-Managed Field Readers` (Read) on every business unit's default team; writer profile `Spaarke BFF-Managed Field Writers` (Read/Create/Update) = the BFF application user(s) only; plus the platform's System Administrator profile (full, not narrowable — owner decision F4 accepts it) |
| **Who writes it** | Only the BFF: `ProvisionProjectEndpoint` sets it `true` as its first write (read back); `UnsecureProjectEndpoint` clears it as its last write. No client, form, business rule, workflow or flow writes it |
| **Who may remove it** | Owner round 3b F3: a Full Access holder on the record (Write + Delete — an administrator qualifies through their role) or the record's creator (`createdby`, or `sprk_createdbyperson` for an app-created row) — enforced in the unsecure endpoint. Securing is open to any Write holder |
| **NULL** | Not a legitimate value: every NULL row was created before the column existed (live 2026-10-02: 9 projects, 18 matters, 11 work assignments, newest 2026-03-15) and is set to No by `scripts/Repair-SecureFlagNulls.ps1` (owner decision Q1). After it, an EMPTY value means the reader cannot read the secured column, and the BFF refuses on it (`secure_flag_unreadable`, `sdap.unsecure.secure_flag_unreadable`) |

**Corrected 2026-10-02 (task 150).** This section used to say the flag is "immutable after creation", enforced by a
business rule "Lock Secure Project Flag After Creation". Neither was true: the designation is reversible
(`UnsecureProjectEndpoint`, design.md §5.1), and a read-only inventory of spaarkedev1 on 2026-10-02 found **no**
business rule, classic workflow, cloud flow, form or view on the three tables that references `sprk_issecure`. Until
task 150 any user with Write could change it through the Web API — and until live gate G-4 applies the field-level
security above, that is STILL true in spaarkedev1. Once applied, that field-level security is the enforcement.

**Readers must never be masked.** A field-secured column a caller cannot read comes back EMPTY rather than refused.
Readers that map empty to "not secure" — the external plane, the client `RecordContainerResolver.ts`, the
TrackingFieldTrio access gate — would then treat a secure record as an ordinary one. That is why the reader profile sits
on every business unit's default team, why a new business unit's default team must be added (re-run
`Set-RecordCreatorPersonSchema.ps1 -Apply`), and why the standing assertion
`tests/integration/auth/UnifiedAccessControl/SecureFlagFieldSecurityAssertion.cs` checks it.

---
### sprk_securitybu (Security Business Unit)

> ⚠️ Corrected from `sprk_securitybuid` — see the top-of-file correction note.

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_securitybu |
| **Display Name** | Security Business Unit |
| **Type** | Lookup |
| **Target Entity** | businessunit |
| **Read form** | `_sprk_securitybu_value` |
| **Required Level** | None (nullable — not all projects are secure) |
| **Relationship Name** | UNVERIFIED — do not assume a name derived from the old `sprk_securitybuid` convention; confirm against live metadata before citing one |

**Notes**:
- **Not populated by current provisioning** (task 021 re-scope, 2026-08-25): provisioning now resolves and
  assigns ownership to ONE canonical `Secure Project` business unit by NAME, and does not stamp a
  per-project BU reference onto this field at all. This field is read-only in the current code path, to
  detect and refuse re-provisioning of a project created under the RETIRED per-project-BU mechanism.
- design.md §5.1 explicitly rules out BU-per-project proliferation — if this field is populated on a
  project, that project predates the 2026-08-25 re-scope and needs a deliberate migration, not an
  automatic one.

---

### sprk_externalaccount (External Access Account / Client)

> ⚠️ Corrected from `sprk_externalaccountid` — see the top-of-file correction note. **This is the CLIENT
> lookup**, consumed elsewhere in the codebase as the `client` predicate (`ProjectLiveFactResolver.cs`,
> `MatterLiveFactResolver.cs`) — it is not specific to the external-access/secure-project feature despite
> the field's original doc framing, and current provisioning code deliberately never writes it (see the
> top-of-file table).

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_externalaccount |
| **Display Name** | External Access Account (documented display name — the field's actual role is "client") |
| **Type** | Lookup |
| **Target Entity** | account |
| **Read form** | `_sprk_externalaccount_value` |
| **Required Level** | None (nullable — set when a client account is associated) |
| **Relationship Name** | UNVERIFIED — do not assume a name derived from the old `sprk_externalaccountid` convention; confirm against live metadata before citing one |

**Notes**:
- References the Account record for the project's client / external law firm.
- **Do not write this field from secure-project provisioning code.** The five-month C4/C5 defect existed
  precisely because the broken stamping PATCH targeted this field with the wrong name; had the name been
  right, provisioning would have silently overwritten every secure project's real client with a synthetic
  placeholder account. A guard test now fails if this field reappears in a provisioning payload.

---

## Form Layout Changes

Add a new section to the sprk_project main form:

### Section: Secure Project Configuration

Position: After existing project details, before documents section.

| Field | Label | Notes |
|-------|-------|-------|
| sprk_issecure | Is Secure Project | ⚠️ Not on any live form (inventory 2026-10-02). If it is ever placed on one, then once field security is applied (task 150, pending live gate G-4) it shows read-only to everyone but System Administrators; secure/unsecure goes through the Access ribbon commands (task 150 + 142) |
| sprk_securitybu | Security Business Unit | Read-only. ⚠️ Not currently written by provisioning — see the field note above |
| sprk_externalaccount | External Access Account | Editable by SDAP Admin. This is the CLIENT lookup — see the field note above before wiring any secure-project-specific logic to it |

**Section Visibility**: Always visible. Fields read-only if not SDAP Admin role.

---

## Impact on Existing Functionality

| Area | Impact |
|------|--------|
| Create Project Wizard | New "Secure Project" toggle step (task 060) |
| BFF API authorization | External access filter checks sprk_issecure before allowing external callers |
| SPE container provisioning | Triggered when sprk_issecure = true on project creation (task 061) |
| External Access SPA | ⚠️ Corrected 2026-09-08 — "Power Pages SPA" is stale; Power Pages is RETIRED (ADR-028 Amendment A1). The current external surface is the Static Web Apps SPA + Entra External ID (CIAM); see `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`. It shows only projects where an active `sprk_externalrecordaccess` exists for the current Contact |
| AI Search | NOT IMPLEMENTED (corrected 2026-09-08 — no external AI Search filter exists; see `docs/architecture/external-access-spa-architecture.md` Plane 3) |

---

## Deployment Notes

These fields are added to the existing sprk_project table via solution update in SpaarkeCore. No data migration needed — existing projects default to sprk_issecure = false.

```bash
# Verify fields after import
# NOTE: sprk_projectname (not sprk_name) is the project's name field -- sprk_name does not exist on
# sprk_project (see finding H5, review-2026-08-24-findings.md: ExternalDataService's sprk_name order-by
# 400'd). sprk_securitybu / sprk_externalaccount corrected per the top-of-file note (2026-09-08).
pac data export --entity sprk_project --select sprk_projectid,sprk_projectname,sprk_issecure,sprk_securitybu,sprk_externalaccount --output-directory ./exports
```

---

*Schema version: 2.0 | Created: 2026-03-16 | Project: sdap-secure-project-module | Corrected: 2026-09-08 by `unified-access-control-r2` task 026 (root-cause repair of the C4/C5 provisioning defect — `sprk_securitybuid`→`sprk_securitybu`, `sprk_externalaccountid`→`sprk_externalaccount`, `sprk_name`→`sprk_projectname`, added `sprk_containerid`, retired Power Pages / unbuilt-AI-Search references corrected). Names verified against `ProvisionProjectEndpoint.cs`'s live-metadata-verified (2026-08-25) column list — NOT independently re-verified against a live Dataverse `describe` in this pass (Dataverse MCP was unavailable). Re-verify before treating as gospel.*
