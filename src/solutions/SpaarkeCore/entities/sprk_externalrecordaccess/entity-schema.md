# sprk_externalrecordaccess Entity Schema

> **Entity Purpose**: Junction table linking external Contacts (or Organizations — org-wide grants omit the Contact) to polymorphic grant roots (Project / Matter / Work Assignment) with a specific access level. This is the single source of truth for "who can access what" in the external access module, read by the BFF's external authorization layer (`ExternalParticipationService` / `CallerPrincipalResolver`). The Power Pages access model originally described here is RETIRED — external callers are Static Web Apps + Entra External ID (CIAM), broker-only through the BFF (ADR-028 A1).
>
> **Schema Version**: 1.1
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module
> **Corrected**: 2026-08-20 by the `unified-access-control-r2` investigation — field logical names, expiry field name, organization lookup, and never-built features (expiry worker, Power Pages chain, three-plane revocation) verified against `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` + `Infrastructure/ExternalAccess/ExternalParticipationService.cs`; unbuilt behavior is marked NOT IMPLEMENTED below

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_externalrecordaccess |
| **Display Name** | External Record Access |
| **Plural Display Name** | External Record Accesses |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | Organization |
| **Description** | Tracks external user (Contact) and Organization access grants to Spaarke records (Projects/Matters/Work Assignments). Drives BFF API external authorization. (Power Pages table permission chain: RETIRED.) |

---

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_externalrecordaccessid | External Record Access | Uniqueidentifier | Auto | — | Primary key (auto-generated GUID) |
| sprk_name | Name | String | Computed | 200 | Auto-generated display name (e.g., "Jane Smith → Acme Litigation") |

### Core Lookup Fields

> **Corrected 2026-08-20** — the original `*id`-suffixed logical names were wrong and 400'd every grant. Live names verified via `$metadata` (task 070): schema (write) names are PascalCase `@odata.bind` navigation properties (`GrantExternalAccessEndpoint.cs:290-334`); read form is `_sprk_{name}_value` (`ExternalParticipationService.cs:399-407`).

| Logical Name | Schema Name (write) | Display Name | Type | Required | Target Entity | Description |
|--------------|--------------------|--------------|------|----------|---------------|-------------|
| sprk_contact (read: `_sprk_contact_value`) | `sprk_Contact@odata.bind` | Contact | Lookup | No* | contact | External user granted access. *OMITTED for an Organization grant — a row with no Contact + a bound Organization grants every ACTIVE org member at check time (`GrantExternalAccessEndpoint.cs:303-311`) |
| sprk_project (read: `_sprk_project_value`) | `sprk_Project@odata.bind` | Project | Lookup | One-of | sprk_project | Grant root — exactly ONE typed root lookup is bound per record (project / matter / workassignment; `GrantExternalAccessEndpoint.cs:286-298`) |
| sprk_matter (read: `_sprk_matter_value`) | `sprk_Matter@odata.bind` | Matter | Lookup | One-of | sprk_matter | Grant root (see above) |
| sprk_workassignment (read: `_sprk_workassignment_value`) | `sprk_WorkAssignment@odata.bind` | Work Assignment | Lookup | One-of | sprk_workassignment | Grant root (see above) |
| sprk_organization (read: `_sprk_organization_value`) | `sprk_Organization@odata.bind` | Organization | Lookup | No | sprk_organization | Firm/org association — targets the custom `sprk_organization` table, NOT the OOB `account` (owner steer 2026-08-11; `GrantExternalAccessEndpoint.cs:330-334`). Supersedes the originally documented `sprk_organization` → account |
| sprk_grantedby | `sprk_GrantedBy@odata.bind` | Granted By | Lookup | No | systemuser | Core User who created the grant (audit field — resolved from the caller's AAD oid and OMITTED when unresolvable rather than failing the grant; `GrantExternalAccessEndpoint.cs:199-231`) |

### Access Control Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_accesslevel | Access Level | Choice | Yes | Determines the BFF's effective rights: View Only / Collaborate / Full Access |
| sprk_granteddate | Granted Date | DateTime (DateOnly) | Yes | Date access was granted (set to UTC now on create; `GrantExternalAccessEndpoint.cs:300`) |
| sprk_expiresdate | Expires Date | DateTime (DateOnly) | No | Optional expiration date. **Corrected 2026-08-20**: the live field is `sprk_expiresdate`, NOT the originally documented `sprk_expiresdate` (verified live, task 070 — the old name 400'd any grant carrying an expiry; `GrantExternalAccessEndpoint.cs:321-326`). ⚠️ **Expiry is NOT ENFORCED anywhere**: the field is write-only today — no worker deactivates expired rows and the participation query filters on `statecode` only (`ExternalParticipationService.cs:406`) |

### Approval Fields (Document/File Access)

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_approvedby | Approved By | Lookup → SystemUser | No | Core User who approved document/file access (Plane 2) |
| sprk_approveddate | Approved Date | DateTime (DateOnly) | No | Date document/file access was approved |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| statecode | Status | State | Active (0) / Inactive (1) — deactivating this record revokes the grant (the BFF's participation query reads `statecode eq 0` only; `ExternalParticipationService.cs:406`) |
| statuscode | Status Reason | Status | Active: Active (1) / Inactive: Inactive (2) |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup → SystemUser | User who created the record |
| modifiedby | Modified By | Lookup → SystemUser | User who last modified the record |

---

## Choice Values

### sprk_accesslevel (Access Level)

> **Corrected 2026-08-20** — the original Plane 2/Plane 3 columns described NOT-IMPLEMENTED behavior (no SPE container role is ever assigned — external SPE access is broker-only per ADR-028 A1 — and no external AI Search filter exists). The actual enforcement is the BFF effective-rights mapping (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:126-134`):

| Value | Label | Effective `AccessRights` (BFF) | SPE / AI Search |
|-------|-------|-------------------------------|-----------------|
| 100000000 | View Only | Read | NOT IMPLEMENTED (broker-only; no search plane) |
| 100000001 | Collaborate | Read + Create + Write | NOT IMPLEMENTED |
| 100000002 | Full Access | Read + Create + Write + Delete | NOT IMPLEMENTED |

---

## Form Layout

> **Note (2026-08-20)**: field references below corrected to the live logical names (`sprk_contact`, `sprk_project`, `sprk_organization`, `sprk_expiresdate`); the layout itself is the original design and has not been re-verified against the deployed form.

### Main Form: Information

**Header Section**
- sprk_name (External Record Access — computed)
- sprk_contact (Contact)
- sprk_project (Project)
- sprk_accesslevel (Access Level)
- statecode (Status)

**Access Details Section**
- sprk_organization (Organization)
- sprk_matter (Matter)
- sprk_grantedby (Granted By)
- sprk_granteddate (Granted Date)
- sprk_expiresdate (Expires Date)

**File Access Approval Section**
- sprk_approvedby (Approved By)
- sprk_approveddate (Approved Date)

**System Section**
- createdon, modifiedon, createdby, modifiedby

---

## Views

### Active External Record Access (Default View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 200 | 1 (ASC) |
| sprk_contact | 150 | — |
| sprk_project | 150 | — |
| sprk_accesslevel | 120 | — |
| sprk_granteddate | 110 | — |
| sprk_expiresdate | 110 | — |
| sprk_grantedby | 150 | — |

**Filter**: statecode = Active

### All External Record Access

Same columns as above, no filter.

### Access by Project (Subgrid View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_contact | 180 | 1 (ASC) |
| sprk_accesslevel | 120 | — |
| sprk_organization | 150 | — |
| sprk_granteddate | 110 | — |
| sprk_expiresdate | 110 | — |
| sprk_grantedby | 150 | — |

**Filter**: statecode = Active
**Default View Name**: Access by Project
**Used On**: sprk_project form — External Participants subgrid

### Expiring Access (System View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 200 | — |
| sprk_contact | 150 | — |
| sprk_project | 150 | — |
| sprk_accesslevel | 120 | — |
| sprk_expiresdate | 110 | 1 (ASC) |

**Filter**: statecode = Active AND sprk_expiresdate ≤ [next 30 days]

---

## Relationships

### N:1 Relationships (Lookups — this table references)

> **Note (2026-08-20)**: relationship schema names below are the ORIGINAL design names and have NOT been re-verified against live metadata (the live field logical names differ from the design — see Core Lookup Fields). "This Field" column corrected; the organization lookup targets `sprk_organization`, not `account`. A `sprk_workassignment` root lookup also exists live (task 028) and is not in this original list.

| Relationship (design name — unverified) | This Field | Parent Table | Behavior |
|-------------|-----------|--------------|----------|
| sprk_externalrecordaccess_contactid_contact | sprk_contact | contact | Restrict (do not delete Contact if active access records exist) |
| sprk_externalrecordaccess_projectid_sprk_project | sprk_project | sprk_project | Cascade — deactivate all access when project is deactivated |
| sprk_externalrecordaccess_matterid_sprk_matter | sprk_matter | sprk_matter | Cascade — deactivate all access when matter is deactivated |
| (organization lookup, added task 070) | sprk_organization | sprk_organization | Referential (no cascade) |
| sprk_externalrecordaccess_grantedby_systemuser | sprk_grantedby | systemuser | Referential (no cascade) |
| sprk_externalrecordaccess_approvedby_systemuser | sprk_approvedby | systemuser | Referential (no cascade) |

---

## Security Roles

| Role | Create | Read | Write | Delete |
|------|--------|------|-------|--------|
| System Administrator | Yes | Yes | Yes | Yes |
| System Customizer | Yes | Yes | Yes | Yes |
| SDAP Admin | Yes | Yes | Yes | Yes |
| SDAP User (Core) | Yes | Yes | Yes | No |
| Basic User | No | No | No | No |

**Note**: External Contacts never read this table directly — the BFF reads it app-only on their behalf (broker-only, ADR-028 A1). The original "Power Pages table permissions (Contact scope)" model is RETIRED.

---

## Power Pages Table Permission Configuration — ⚠️ RETIRED / NOT IMPLEMENTED

> **Corrected 2026-08-20**: Power Pages is retired. External access is served by Static Web Apps + Entra External ID (CIAM); the BFF resolves the caller's Contact and reads this table app-only (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`, `ExternalParticipationService.cs`). No Power Pages table permission chain or web role exists. The original design is preserved below for lineage only.

```
(retired design)
Level 0: sprk_externalrecordaccess
         Scope: Contact
         Relationship: sprk_contact
         CRUD: Read only
         Web Role: "Secure Project Participant"
         → Unlocks parent chain to sprk_project and its children
```

---

## Business Rules

1. **Unique participation grant**: Only one active record per (Contact, Project) pair. If a second grant is attempted, update the existing one instead.

2. **Computed name**: Auto-generate `sprk_name` as `"{ContactFullName} → {ProjectName}"` via pre-create plugin (thin validation only, per ADR-002).

3. **Deactivation = revocation** *(corrected 2026-08-20 — the original "full three-plane revocation" was never built)*: Setting statecode = Inactive revokes the grant because the BFF's participation query only reads Active rows (`ExternalParticipationService.cs:406`). The revoke endpoint deactivates the row + invalidates the Redis participation cache, plus a defensive SPE permission cleanup only when a `ContainerId` is supplied (`Api/ExternalAccess/RevokeExternalAccessEndpoint.cs:93-147`). There is no web-role removal (Power Pages retired) and no search-filter exclusion (no external search plane exists).

4. **Expiry enforcement — ⚠️ NOT IMPLEMENTED**: The originally promised scheduled BFF worker that checks `sprk_expiresdate` and deactivates expired records was never built. As of 2026-08-20 the expiry date is stored on grant (`GrantExternalAccessEndpoint.cs:321-326`) but enforced NOWHERE — the participation query filters on `statecode` only, so an expired but still-Active grant remains fully effective until someone deactivates it manually.

---

## BFF API Integration

This table is queried by:

| BFF Component | Query | Purpose |
|--------------|-------|---------|
| `ExternalParticipationService` (via `CallerPrincipalAuthorizationFilter` / `CallerPrincipalResolver`) | Active records where `_sprk_contact_value` = resolved Contact (plus org-grant rows with no Contact for the caller's active orgs) | Determine the caller's grant set (projects/matters/work assignments) |
| `GrantExternalAccessEndpoint` | Create new record + invalidate participation cache | Grant external access (ONE Dataverse row — no SPE/web-role/search orchestration; broker-only) |
| `RevokeExternalAccessEndpoint` | Deactivate record by ID + invalidate cache (+ defensive SPE cleanup when `ContainerId` supplied) | Revoke external access |
| `ProjectClosureEndpoint` | Deactivate all records for project | Cascade revocation on project close |
| `ExternalUserContextEndpoint` | Resolved principal's grant set | Return user's project membership to SPA |

**Redis Cache Key** *(corrected 2026-08-20)*: tenant-scoped `ITenantCache` entry — resource `external-access-grant`, contact-id component, version 3, 60s TTL (`ExternalParticipationService.cs:28-34`; per ADR-009). The old flat `sdap:external:access:{contactId}` key is no longer accurate.

---

## Deployment

Add to **SpaarkeCore** solution. Deploy via PAC CLI:

```bash
# Pack and import solution
pac solution pack --folder ./src/solutions/SpaarkeCore --zipfile SpaarkeCore.zip --managed false
pac solution import --path SpaarkeCore.zip --force-overwrite
```

---

*Schema version: 1.1 | Created: 2026-03-16 | Project: sdap-secure-project-module | Corrected: 2026-08-20 by `unified-access-control-r2` (field names, org lookup, expiry field + unenforced expiry, retired Power Pages model, actual grant/revoke behavior)*
