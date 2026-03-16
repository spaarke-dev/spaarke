# sprk_externalrecordaccess — Views and Subgrid Configuration

> **Purpose**: Documents views and subgrid configuration for the External Record Access table.
> **Schema Version**: 1.0
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module

---

## System Views

### 1. Active Participants (Default View)

| Property | Value |
|----------|-------|
| **View Name** | Active Participants |
| **Is Default** | Yes |
| **FetchXML Filter** | `statecode = 0` (Active) |
| **Sort** | sprk_contactid ASC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Contact | sprk_contactid | 180 | 1 ASC |
| Project | sprk_projectid | 180 | — |
| Access Level | sprk_accesslevel | 120 | — |
| Granted Date | sprk_granteddate | 110 | — |
| Expiry Date | sprk_expirydate | 110 | — |
| Granted By | sprk_grantedby | 150 | — |
| Status | statecode | 80 | — |

**FetchXML**:
```xml
<fetch>
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid"/>
    <attribute name="sprk_name"/>
    <attribute name="sprk_contactid"/>
    <attribute name="sprk_projectid"/>
    <attribute name="sprk_accesslevel"/>
    <attribute name="sprk_granteddate"/>
    <attribute name="sprk_expirydate"/>
    <attribute name="sprk_grantedby"/>
    <attribute name="statecode"/>
    <filter>
      <condition attribute="statecode" operator="eq" value="0"/>
    </filter>
    <order attribute="sprk_contactid" descending="false"/>
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
| **Sort** | sprk_projectid ASC, sprk_contactid ASC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Project | sprk_projectid | 180 | 1 ASC |
| Contact | sprk_contactid | 180 | 2 ASC |
| Access Level | sprk_accesslevel | 120 | — |
| Expiry Date | sprk_expirydate | 110 | — |
| Account | sprk_accountid | 150 | — |
| Status | statecode | 80 | — |

**FetchXML**:
```xml
<fetch>
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid"/>
    <attribute name="sprk_name"/>
    <attribute name="sprk_projectid"/>
    <attribute name="sprk_contactid"/>
    <attribute name="sprk_accesslevel"/>
    <attribute name="sprk_expirydate"/>
    <attribute name="sprk_accountid"/>
    <attribute name="statecode"/>
    <order attribute="sprk_projectid" descending="false"/>
    <order attribute="sprk_contactid" descending="false"/>
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
| **Sort** | sprk_contactid ASC, sprk_granteddate DESC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Contact | sprk_contactid | 180 | 1 ASC |
| Project | sprk_projectid | 180 | — |
| Access Level | sprk_accesslevel | 120 | — |
| Granted Date | sprk_granteddate | 110 | 2 DESC |
| Expiry Date | sprk_expirydate | 110 | — |
| Status | statecode | 80 | — |

**FetchXML**:
```xml
<fetch>
  <entity name="sprk_externalrecordaccess">
    <attribute name="sprk_externalrecordaccessid"/>
    <attribute name="sprk_name"/>
    <attribute name="sprk_contactid"/>
    <attribute name="sprk_projectid"/>
    <attribute name="sprk_accesslevel"/>
    <attribute name="sprk_granteddate"/>
    <attribute name="sprk_expirydate"/>
    <attribute name="statecode"/>
    <order attribute="sprk_contactid" descending="false"/>
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
| **Sort** | sprk_expirydate ASC |

**Columns**:

| Column | Attribute | Width | Sort |
|--------|-----------|-------|------|
| Contact | sprk_contactid | 180 | — |
| Project | sprk_projectid | 180 | — |
| Access Level | sprk_accesslevel | 120 | — |
| Expiry Date | sprk_expirydate | 110 | 1 ASC |
| Account | sprk_accountid | 150 | — |

---

## Subgrid Configuration — sprk_project Form

### Subgrid: External Participants

This subgrid is added to the sprk_project main form to show external participants for that project.

| Property | Value |
|----------|-------|
| **Subgrid Name** | ExternalParticipants |
| **Label** | External Participants |
| **Related Entity** | sprk_externalrecordaccess |
| **Relationship** | sprk_project_sprk_externalrecordaccess (via sprk_projectid) |
| **Default View** | Active Participants |
| **Rows** | 5 (default) |
| **Show Records From** | All record types |

**Subgrid Columns**:

| Column | Attribute | Width |
|--------|-----------|-------|
| Contact | sprk_contactid | 180 |
| Access Level | sprk_accesslevel | 120 |
| Account | sprk_accountid | 150 |
| Granted Date | sprk_granteddate | 110 |
| Expiry Date | sprk_expirydate | 110 |
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
| sprk_contactid (Contact) | Yes | Yes |
| sprk_projectid (Project) | Auto-filled from subgrid context | Yes |
| sprk_accesslevel (Access Level) | Yes | Yes |
| sprk_expirydate (Expiry Date) | Yes | No |
| sprk_accountid (Account) | Yes | No |

**Note**: Actual access granting (three-plane orchestration) is done by the BFF API Grant Access endpoint (task 011). The quick create form creates the Dataverse record only; the BFF handles SPE container membership and invitation creation.

---

## Deployment Instructions

1. **Create views** in the Power Apps maker portal (make.powerapps.com) → Tables → sprk_externalrecordaccess → Views
2. **Add subgrid** via Form editor on sprk_project main form → Components → Subgrid
3. **Create Quick Create form** for sprk_externalrecordaccess
4. **Add to SpaarkeCore solution** via Solution explorer → Add existing → Views, Forms
5. **Export and import** using PAC CLI

---

*Schema version: 1.0 | Created: 2026-03-16 | Project: sdap-secure-project-module*
