# sprk_externalrecordaccess — Views and Subgrid Configuration

> **Purpose**: Documents views and subgrid configuration for the External Record Access table.
> **Schema Version**: 1.1
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module
> **Corrected 2026-09-08 (`unified-access-control-r2` task 026, review finding M4)**: this file documented
> `sprk_contactid`, `sprk_projectid` and `sprk_expirydate` as live column names, across 16+ references. **None
> of the three exist on `sprk_externalrecordaccess`.** They are corrected below to `sprk_contact`,
> `sprk_project` and `sprk_expiresdate` — verified against the sibling
> [`entity-schema.md`](entity-schema.md) in this same folder (itself corrected 2026-08-20 against live
> metadata via `$metadata` and cross-checked here against `Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs`
> and `ExternalParticipationService.cs`, both of which carry the identical verified names). The "Account"
> column is also corrected to **`sprk_organization`** — this table has no `sprk_accountid`/account lookup;
> the firm/org association targets the custom `sprk_organization` table, not the OOB `account` entity. This
> file is recorded as the plausible seed of five stale-column recurrences across this project (task 016,
> task 021/C4/C5, and others) — task 016 found it wrong on 2026-08-23 and left it unfixed, against root
> CLAUDE.md §2. **Verify every column here against `entity-schema.md` or live metadata before trusting it —
> do not treat this reconciliation as a reason to stop checking.**

---

## System Views

### 1. Active Participants (Default View)

| Property | Value |
|----------|-------|
| **View Name** | Active Participants |
| **Is Default** | Yes |
| **FetchXML Filter** | `statecode = 0` (Active) |
| **Sort** | sprk_contact ASC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Contact | sprk_contact | 180 | 1 ASC |
| Project | sprk_project | 180 | — |
| Access Level | sprk_accesslevel | 120 | — |
| Granted Date | sprk_granteddate | 110 | — |
| Expiry Date | sprk_expiresdate | 110 | — |
| Granted By | sprk_grantedby | 150 | — |
| Status | statecode | 80 | — |

**FetchXML**:
```xml
<fetch>
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid"/>
    <attribute name="sprk_name"/>
    <attribute name="sprk_contact"/>
    <attribute name="sprk_project"/>
    <attribute name="sprk_accesslevel"/>
    <attribute name="sprk_granteddate"/>
    <attribute name="sprk_expiresdate"/>
    <attribute name="sprk_grantedby"/>
    <attribute name="statecode"/>
    <filter>
      <condition attribute="statecode" operator="eq" value="0"/>
    </filter>
    <order attribute="sprk_contact" descending="false"/>
  </entity>
</fetch>
```

---

### 2. All Participants (By Project)

| Property | Value |
|----------|-------|
| **View Name** | By Project |
| **Is Default** | No |
| **FetchXML Filter** | None (all records) |
| **Sort** | sprk_project ASC, sprk_contact ASC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Project | sprk_project | 180 | 1 ASC |
| Contact | sprk_contact | 180 | 2 ASC |
| Access Level | sprk_accesslevel | 120 | — |
| Expiry Date | sprk_expiresdate | 110 | — |
| Organization | sprk_organization | 150 | — |
| Status | statecode | 80 | — |

**FetchXML**:
```xml
<fetch>
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid"/>
    <attribute name="sprk_name"/>
    <attribute name="sprk_project"/>
    <attribute name="sprk_contact"/>
    <attribute name="sprk_accesslevel"/>
    <attribute name="sprk_expiresdate"/>
    <attribute name="sprk_organization"/>
    <attribute name="statecode"/>
    <order attribute="sprk_project" descending="false"/>
    <order attribute="sprk_contact" descending="false"/>
  </entity>
</fetch>
```

---

### 3. All Participants (By Contact)

| Property | Value |
|----------|-------|
| **View Name** | By Contact |
| **Is Default** | No |
| **FetchXML Filter** | None (all records) |
| **Sort** | sprk_contact ASC, sprk_granteddate DESC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Contact | sprk_contact | 180 | 1 ASC |
| Project | sprk_project | 180 | — |
| Access Level | sprk_accesslevel | 120 | — |
| Granted Date | sprk_granteddate | 110 | 2 DESC |
| Expiry Date | sprk_expiresdate | 110 | — |
| Status | statecode | 80 | — |

**FetchXML**:
```xml
<fetch>
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid"/>
    <attribute name="sprk_name"/>
    <attribute name="sprk_contact"/>
    <attribute name="sprk_project"/>
    <attribute name="sprk_accesslevel"/>
    <attribute name="sprk_granteddate"/>
    <attribute name="sprk_expiresdate"/>
    <attribute name="statecode"/>
    <order attribute="sprk_contact" descending="false"/>
    <order attribute="sprk_granteddate" descending="true"/>
  </entity>
</fetch>
```

---

### 4. Expiring Access (System View)

| Property | Value |
|----------|-------|
| **View Name** | Expiring Access |
| **Is Default** | No |
| **FetchXML Filter** | Active AND expiry within 30 days |
| **Sort** | sprk_expiresdate ASC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Contact | sprk_contact | 180 | — |
| Project | sprk_project | 180 | — |
| Access Level | sprk_accesslevel | 120 | — |
| Expiry Date | sprk_expiresdate | 110 | 1 ASC |
| Organization | sprk_organization | 150 | — |

---

## Subgrid Configuration — sprk_project Form

### Subgrid: External Participants

This subgrid is added to the sprk_project main form to show external participants for that project.

| Property | Value |
|----------|-------|
| **Subgrid Name** | ExternalParticipants |
| **Label** | External Participants |
| **Related Entity** | sprk_externalrecordaccess |
| **Relationship** | via the `sprk_project` lookup. The relationship's own schema (unique) name is **not verified against live metadata** — see `entity-schema.md`'s "N:1 Relationships" section for the same caveat; do not assume a name derived from the old `sprk_projectid` convention. |
| **Default View** | Active Participants |
| **Rows** | 5 (default) |
| **Show Records From** | All record types |

**Subgrid Columns**:

| Column | Attribute | Width |
|--------|-----------|-------|
| Contact | sprk_contact | 180 |
| Access Level | sprk_accesslevel | 120 |
| Organization | sprk_organization | 150 |
| Granted Date | sprk_granteddate | 110 |
| Expiry Date | sprk_expiresdate | 110 |
| Granted By | sprk_grantedby | 150 |

**Actions available on subgrid**:
- ➕ Add Record (opens quick create form)
- ✏️ Edit (opens record)
- ❌ Deactivate (revokes access — triggers BFF API three-plane revocation)

**Placement on sprk_project form**:
- Section: "External Access"
- Position: Below the "Secure Project Configuration" section (added in task 002)
- Visible when: `sprk_issecure = true` (business rule hides section on non-secure projects)

---

## Quick Create Form — sprk_externalrecordaccess

For the subgrid "Add Record" action, configure a Quick Create form:

| Field | Visible | Required |
|-------|---------|----------|
| sprk_contact (Contact) | Yes | Yes |
| sprk_project (Project) | Auto-filled from subgrid context | Yes |
| sprk_accesslevel (Access Level) | Yes | Yes |
| sprk_expiresdate (Expiry Date) | Yes | No |
| sprk_organization (Organization) | Yes | No |

**Note**: Actual access granting (three-plane orchestration) is done by the BFF API Grant Access endpoint (task 011). The quick create form creates the Dataverse record only; the BFF handles SPE container membership and invitation creation.

---

## Deployment Instructions

1. **Create views** in the Power Apps maker portal (make.powerapps.com) → Tables → sprk_externalrecordaccess → Views
2. **Add subgrid** via Form editor on sprk_project main form → Components → Subgrid
3. **Create Quick Create form** for sprk_externalrecordaccess
4. **Add to SpaarkeCore solution** via Solution explorer → Add existing → Views, Forms
5. **Export and import** using PAC CLI

---

*Schema version: 1.1 | Created: 2026-03-16 | Project: sdap-secure-project-module | Corrected: 2026-09-08 by `unified-access-control-r2` task 026 (review finding M4 — `sprk_contactid`→`sprk_contact`, `sprk_projectid`→`sprk_project`, `sprk_expirydate`→`sprk_expiresdate`, `sprk_accountid`→`sprk_organization`)*
