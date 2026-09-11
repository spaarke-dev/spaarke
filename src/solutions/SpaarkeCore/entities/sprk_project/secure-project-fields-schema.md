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
| sprk_issecure | Is Secure Project | Boolean | No | false | Designates this project as a secure project. Immutable after creation. Drives BU isolation, SPE container provisioning, and external access model. Name unchanged by this correction — confirmed live, used throughout `ProvisionProjectEndpoint.cs` (e.g. `projectRow.sprk_issecure != true`). |
| sprk_securitybu | Security Business Unit | Lookup → businessunit | No | — | ⚠️ **CORRECTED** from `sprk_securitybuid`, which does not exist and 400'd every stamping PATCH for five months. Read form: `_sprk_securitybu_value`. **Not actually written by provisioning today** — task 021 re-scoped provisioning onto ONE canonical `Secure Project` business unit resolved by name at request time (see `ProvisionProjectEndpoint.SecureBusinessUnitNameConfigKey`), not a per-project BU, so this field is read only to detect the RETIRED legacy per-project-BU mechanism (`ProjectRow.HasLegacyPerProjectBusinessUnit`) and refuse to re-provision such a project. |
| sprk_externalaccount | External Access Account | Lookup → account | No | — | ⚠️ **CORRECTED** from `sprk_externalaccountid`, which does not exist. This is the **CLIENT** lookup, not an "external access" bookkeeping field — `ProjectLiveFactResolver.cs` and `MatterLiveFactResolver.cs` both map the `client` predicate to it. **Provisioning code (task 021) deliberately never writes this column**: the retired code created a synthetic "External Access — {project}" `account` record and aimed the broken stamping PATCH at this field: had the name been correct, provisioning would have silently overwritten every secure project's real client. A test now fails if this field reappears in any provisioning payload. |
| sprk_containerid | SPE Container ID | Text (NVARCHAR 100) | No | — | 🆕 **ADDED to this doc** — not a lookup, a plain string holding the SPE container id created for this project. Verified live via `ProvisionProjectEndpoint.ProjectProvisioningSelect` and written by `RecordContainerOnProjectAsync`. This is the field the old code's broken `sprk_specontainerid` name was presumably trying to reach; it was never in this document at all, which is part of why the wrong stamping PATCH went unnoticed — nothing was checking the field actually used for the container reference. **Also cascaded at project CREATE time** from the creating user's business-unit defaults (`EntityCreationService.applyUserBuDefaults`), independent of secure-project provisioning — see the idempotency-marker note in `ProvisionProjectEndpoint.ProjectRow.IsOwnedBy` for why a non-empty value here must NOT be read as "already provisioned". |

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
| sprk_issecure | Is Secure Project | Read-only on existing records (business rule) |
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
