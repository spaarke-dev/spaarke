# sprk_externalrecordaccess Entity Schema

> **Entity Purpose**: Junction table linking external Contacts to Projects (and optionally Matters) with a specific access level. This is the single source of truth for "who can access what" in the Secure Project module, driving both the Power Pages table permission parent chain (Plane 1) and BFF API external authorization filter (Planes 2 & 3).
>
> **Schema Version**: 1.0
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_externalrecordaccess |
| **Display Name** | External Record Access |
| **Plural Display Name** | External Record Accesses |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | Organization |
| **Description** | Tracks external user (Contact) access grants to Spaarke records (Projects/Matters). Drives Power Pages table permission chain and BFF API authorization. |

---

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_externalrecordaccessid | External Record Access | Uniqueidentifier | Auto | — | Primary key (auto-generated GUID) |
| sprk_name | Name | String | Computed | 200 | Auto-generated display name (e.g., "Jane Smith → Acme Litigation") |

### Core Lookup Fields

| Logical Name | Display Name | Type | Required | Target Entity | Description |
|--------------|--------------|------|----------|---------------|-------------|
| sprk_contactid | Contact | Lookup | Yes | contact | External user granted access — the key subject of this record |
| sprk_projectid | Project | Lookup | Yes* | sprk_project | Project to which access is granted (*required when sprk_matterid is null) |
| sprk_matterid | Matter | Lookup | No | sprk_matter | Matter to which access is granted (nullable — future expansion) |
| sprk_accountid | Account | Lookup | No | account | Organization/firm the Contact belongs to (for reporting) |
| sprk_grantedby | Granted By | Lookup | Yes | systemuser | Core User who created the access grant |

### Access Control Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_accesslevel | Access Level | Choice | Yes | Determines Plane 2/3 permissions: View Only / Collaborate / Full Access |
| sprk_granteddate | Granted Date | DateTime (DateOnly) | Yes | Date access was granted (defaults to today on create) |
| sprk_expirydate | Expiry Date | DateTime (DateOnly) | No | Optional expiration date for time-limited access grants |

### Approval Fields (Document/File Access)

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_approvedby | Approved By | Lookup → SystemUser | No | Core User who approved document/file access (Plane 2) |
| sprk_approveddate | Approved Date | DateTime (DateOnly) | No | Date document/file access was approved |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| statecode | Status | State | Active (0) / Inactive (1) — **deactivating this record revokes all three planes** |
| statuscode | Status Reason | Status | Active: Active (1) / Inactive: Inactive (2) |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup → SystemUser | User who created the record |
| modifiedby | Modified By | Lookup → SystemUser | User who last modified the record |

---

## Choice Values

### sprk_accesslevel (Access Level)

| Value | Label | Plane 2 (SPE) | Plane 3 (AI Search) | Dataverse Table Permissions |
|-------|-------|---------------|---------------------|----------------------------|
| 100000000 | View Only | Reader container role | Project in search filter | Read only |
| 100000001 | Collaborate | Writer container role | Project in search filter | Read + Create (docs), Read/Create/Write (events/tasks) |
| 100000002 | Full Access | Writer container role | Project in search filter | Read + Create + Write (all tables) |

---

## Form Layout

### Main Form: Information

**Header Section**
- sprk_name (External Record Access — computed)
- sprk_contactid (Contact)
- sprk_projectid (Project)
- sprk_accesslevel (Access Level)
- statecode (Status)

**Access Details Section**
- sprk_accountid (Account)
- sprk_matterid (Matter)
- sprk_grantedby (Granted By)
- sprk_granteddate (Granted Date)
- sprk_expirydate (Expiry Date)

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
| sprk_contactid | 150 | — |
| sprk_projectid | 150 | — |
| sprk_accesslevel | 120 | — |
| sprk_granteddate | 110 | — |
| sprk_expirydate | 110 | — |
| sprk_grantedby | 150 | — |

**Filter**: statecode = Active

### All External Record Access

Same columns as above, no filter.

### Access by Project (Subgrid View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_contactid | 180 | 1 (ASC) |
| sprk_accesslevel | 120 | — |
| sprk_accountid | 150 | — |
| sprk_granteddate | 110 | — |
| sprk_expirydate | 110 | — |
| sprk_grantedby | 150 | — |

**Filter**: statecode = Active
**Default View Name**: Access by Project
**Used On**: sprk_project form — External Participants subgrid

### Expiring Access (System View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 200 | — |
| sprk_contactid | 150 | — |
| sprk_projectid | 150 | — |
| sprk_accesslevel | 120 | — |
| sprk_expirydate | 110 | 1 (ASC) |

**Filter**: statecode = Active AND sprk_expirydate ≤ [next 30 days]

---

## Relationships

### N:1 Relationships (Lookups — this table references)

| Relationship | This Field | Parent Table | Behavior |
|-------------|-----------|--------------|----------|
| sprk_externalrecordaccess_contactid_contact | sprk_contactid | contact | Restrict (do not delete Contact if active access records exist) |
| sprk_externalrecordaccess_projectid_sprk_project | sprk_projectid | sprk_project | Cascade — deactivate all access when project is deactivated |
| sprk_externalrecordaccess_matterid_sprk_matter | sprk_matterid | sprk_matter | Cascade — deactivate all access when matter is deactivated |
| sprk_externalrecordaccess_accountid_account | sprk_accountid | account | Referential (no cascade) |
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

**Note**: External Contacts access this table exclusively through Power Pages table permissions (Contact scope), NOT Dataverse security roles.

---

## Power Pages Table Permission Configuration

This table is **Level 0** in the Power Pages parent permission chain. See [power-pages-access-control.md](../../../../docs/architecture/power-pages-access-control.md) for full configuration steps.

```
Level 0: sprk_externalrecordaccess
         Scope: Contact
         Relationship: sprk_contactid
         CRUD: Read only
         Web Role: "Secure Project Participant"
         → Unlocks parent chain to sprk_project and its children
```

---

## Business Rules

1. **Unique participation grant**: Only one active record per (Contact, Project) pair. If a second grant is attempted, update the existing one instead.

2. **Computed name**: Auto-generate `sprk_name` as `"{ContactFullName} → {ProjectName}"` via pre-create plugin (thin validation only, per ADR-002).

3. **Deactivation = full revocation**: Setting statecode = Inactive triggers BFF API to orchestrate Plane 2 (SPE membership removal) and Plane 3 (search filter exclusion). This is NOT done by a plugin — the BFF API handles it via the grant/revoke endpoints.

4. **Expiry enforcement**: A scheduled BFF API worker checks `sprk_expirydate` and deactivates expired records. Power Pages checks statecode — an expired but still-Active record remains accessible until the worker runs.

---

## BFF API Integration

This table is queried by:

| BFF Component | Query | Purpose |
|--------------|-------|---------|
| `ExternalCallerAuthorizationFilter` | Active records where `sprk_contactid` = authenticated Contact | Determine accessible projects for this caller |
| `GrantAccessEndpoint` | Create new record | Grant external access (orchestrates all 3 planes) |
| `RevokeAccessEndpoint` | Deactivate record by ID | Revoke external access (orchestrates all 3 planes) |
| `ProjectClosureEndpoint` | Deactivate all records for project | Cascade revocation on project close |
| `ExternalUserContextEndpoint` | Active records for Contact | Return user's project membership to SPA |

**Redis Cache Key**: `sdap:external:access:{contactId}` (60s TTL, per ADR-009)

---

## Deployment

Add to **SpaarkeCore** solution. Deploy via PAC CLI:

```bash
# Pack and import solution
pac solution pack --folder ./src/solutions/SpaarkeCore --zipfile SpaarkeCore.zip --managed false
pac solution import --path SpaarkeCore.zip --force-overwrite
```

---

*Schema version: 1.0 | Created: 2026-03-16 | Project: sdap-secure-project-module*
