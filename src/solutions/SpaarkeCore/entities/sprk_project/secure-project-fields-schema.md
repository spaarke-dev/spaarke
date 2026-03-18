# sprk_project — Secure Project Fields Schema

> **Purpose**: Documents the three new fields added to sprk_project to support the Secure Project & External Access Platform.
> **Schema Version**: 1.0
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module

---

## New Fields (Phase 1 — Secure Project Module)

| Logical Name | Display Name | Type | Required | Default | Description |
|--------------|--------------|------|----------|---------|-------------|
| sprk_issecure | Is Secure Project | Boolean | No | false | Designates this project as a secure project. Immutable after creation. Drives BU isolation, SPE container provisioning, and external access model. |
| sprk_securitybuid | Security Business Unit | Lookup → BusinessUnit | No | — | The isolated Business Unit created for this secure project. Set by wizard during creation. |
| sprk_externalaccountid | External Access Account | Lookup → Account | No | — | The Account record representing the external firm/organization that participates in this project. |

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
| **Default Value** | No (false) |
| **Required Level** | None |
| **Field Security** | None |
| **Behavior** | **Immutable after creation** — implemented via Power Apps business rule: lock field on existing records (statecode = Any, set field read-only when `{sprk_projectid} != null`) |

**Important**: This field is the immutability anchor. Once a project is created with sprk_issecure = true, it cannot be changed back. The entire UAC model (BU isolation, SPE container, external access grants) depends on this being permanently true.

**Business Rule Configuration**:
- Rule Name: "Lock Secure Project Flag After Creation"
- Condition: Record is NOT new (`{sprk_projectid}` attribute is set)
- Action: Set `sprk_issecure` field to Read-Only

---

### sprk_securitybuid (Security Business Unit)

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_securitybuid |
| **Display Name** | Security Business Unit |
| **Type** | Lookup |
| **Target Entity** | businessunit |
| **Required Level** | None (nullable — not all projects are secure) |
| **Relationship Name** | sprk_project_sprk_securitybuid_businessunit |

**Notes**:
- Populated by the BFF API during secure project creation wizard (task 061)
- The BU is created specifically for this project (isolated, prevents data crossover)
- Internal staff in this BU can manage the secure project's documents

---

### sprk_externalaccountid (External Access Account)

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_externalaccountid |
| **Display Name** | External Access Account |
| **Type** | Lookup |
| **Target Entity** | account |
| **Required Level** | None (nullable — set when external firm is invited) |
| **Relationship Name** | sprk_project_sprk_externalaccountid_account |

**Notes**:
- References the Account record for the external law firm / organization
- Used for tracking "which organization participates in this project"
- External Contacts linked to this Account are eligible for access grants

---

## Form Layout Changes

Add a new section to the sprk_project main form:

### Section: Secure Project Configuration

Position: After existing project details, before documents section.

| Field | Label | Notes |
|-------|-------|-------|
| sprk_issecure | Is Secure Project | Read-only on existing records (business rule) |
| sprk_securitybuid | Security Business Unit | Read-only (set by wizard) |
| sprk_externalaccountid | External Access Account | Editable by SDAP Admin |

**Section Visibility**: Always visible. Fields read-only if not SDAP Admin role.

---

## Impact on Existing Functionality

| Area | Impact |
|------|--------|
| Create Project Wizard | New "Secure Project" toggle step (task 060) |
| BFF API authorization | External access filter checks sprk_issecure before allowing external callers |
| SPE container provisioning | Triggered when sprk_issecure = true on project creation (task 061) |
| Power Pages SPA | Only shows projects where active sprk_externalrecordaccess exists for the current Contact |
| AI Search | Search filter includes project_id only for contacts with active participation records |

---

## Deployment Notes

These fields are added to the existing sprk_project table via solution update in SpaarkeCore. No data migration needed — existing projects default to sprk_issecure = false.

```bash
# Verify fields after import
pac data export --entity sprk_project --select sprk_projectid,sprk_name,sprk_issecure,sprk_securitybuid,sprk_externalaccountid --output-directory ./exports
```

---

*Schema version: 1.0 | Created: 2026-03-16 | Project: sdap-secure-project-module*
