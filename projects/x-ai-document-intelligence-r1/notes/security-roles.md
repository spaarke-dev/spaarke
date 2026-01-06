# Analysis Feature Security Roles

> **Created**: 2025-12-28
> **Environment**: spaarkedev1.crm.dynamics.com
> **Solution**: Spaarke_DocumentIntelligence
> **Status**: CREATED

---

## Overview

Two security roles created for the Analysis feature:

| Role | Role ID | Scope | Purpose |
|------|---------|-------|---------|
| **Spaarke AI Analysis User** | `9af584ac-2ce4-f011-8406-7ced8d1dc988` | Business Unit | Standard users who create and manage their own analyses |
| **Spaarke AI Analysis Admin** | `3bd6932a-2ee4-f011-8406-7ced8d1dc988` | Organization | Administrators who manage configuration entities |

---

## Analysis User Role

### Purpose
Allows users to create, read, update, and delete their own analysis records. Users cannot access other users' analyses or modify configuration entities.

### Entity Privileges

| Entity | Create | Read | Write | Delete | Scope |
|--------|--------|------|-------|--------|-------|
| sprk_analysis | Yes | Yes | Yes | Yes | User |
| sprk_analysisaction | No | Yes | No | No | Organization |
| sprk_analysisskill | No | Yes | No | No | Organization |
| sprk_analysisknowledge | No | Yes | No | No | Organization |
| sprk_analysisplaybook | No | Yes | No | No | Organization |
| sprk_analysistool | No | Yes | No | No | Organization |
| sprk_analysisworkingversion | Yes | Yes | Yes | Yes | User |
| sprk_analysischatmessage | Yes | Yes | Yes | Yes | User |
| sprk_analysisemailmetadata | Yes | Yes | Yes | Yes | User |
| sprk_aiknowledgedeployment | No | Yes | No | No | Organization |

### Additional Privileges
- Read access to User entity (for owner lookups)
- Web Resource access for PCF controls

---

## Analysis Admin Role

### Purpose
Allows administrators full access to all analysis entities including configuration entities. Can manage actions, skills, knowledge, tools, and playbooks.

### Entity Privileges

| Entity | Create | Read | Write | Delete | Scope |
|--------|--------|------|-------|--------|-------|
| sprk_analysis | Yes | Yes | Yes | Yes | Organization |
| sprk_analysisaction | Yes | Yes | Yes | Yes | Organization |
| sprk_analysisskill | Yes | Yes | Yes | Yes | Organization |
| sprk_analysisknowledge | Yes | Yes | Yes | Yes | Organization |
| sprk_analysisplaybook | Yes | Yes | Yes | Yes | Organization |
| sprk_analysistool | Yes | Yes | Yes | Yes | Organization |
| sprk_analysisworkingversion | Yes | Yes | Yes | Yes | Organization |
| sprk_analysischatmessage | Yes | Yes | Yes | Yes | Organization |
| sprk_analysisemailmetadata | Yes | Yes | Yes | Yes | Organization |
| sprk_aiknowledgedeployment | Yes | Yes | Yes | Yes | Organization |

### Additional Privileges
- Full access to environment variables (for AI configuration)
- Read/Write access to User entity
- Web Resource access for PCF controls

---

## Creation Steps

### Via Power Platform Admin Center

1. **Navigate to Environment**
   ```
   https://admin.powerplatform.microsoft.com
   → Environments → SPAARKE DEV 1 → Settings → Users + permissions → Security roles
   ```

2. **Create Analysis User Role**
   - Click "New role"
   - Name: `Analysis User`
   - Business Unit: Root
   - Copy from: None (create from scratch)
   - Configure privileges per table above

3. **Create Analysis Admin Role**
   - Click "New role"
   - Name: `Analysis Admin`
   - Business Unit: Root
   - Copy from: `Analysis User` (then expand privileges)
   - Configure full Organization-scope privileges

4. **Add to Solution**
   ```
   make.powerapps.com → Solutions → Spaarke_DocumentIntelligence
   → Add existing → Security → Security roles
   → Select both roles
   ```

### Via PAC CLI (Export/Import)

Once roles are created:
```powershell
# Export solution with roles
pac solution export --name Spaarke_DocumentIntelligence --path ./solution.zip

# Roles will be included in solution package
```

---

## Verification

After creation, verify with:
```powershell
pac env fetch --xml "<fetch><entity name='role'><attribute name='name'/><filter><condition attribute='name' operator='like' value='%Analysis%'/></filter></entity></fetch>"
```

Expected output:
```
name            roleid
Analysis User   {guid}
Analysis Admin  {guid}
```

---

## Assignment

### Typical User Assignment
- **Analysis User**: Assigned to all users who need to create analyses
- **Analysis Admin**: Assigned to power users and administrators

### Role Inheritance
Users with `Analysis Admin` do NOT automatically have `Analysis User` privileges - assign both if needed for clarity.

---

## ADR Compliance

Per ADR-003 (Lean Authorization):
- Roles follow least-privilege principle
- User role scoped to own records only
- Admin role for configuration only
- No custom privilege escalation

---

## Known Issues

### SQL Integrity Errors on Choice Tables

When configuring the Admin role, the following tables returned "SQL Integrity violation" errors:

| Table | Type | Resolution |
|-------|------|------------|
| `sprk_aiknowledgetype` | Choice/Option Set | Skip - metadata table |
| `sprk_airetrievalmode` | Choice/Option Set | Skip - metadata table |
| `sprk_aiskilltype` | Choice/Option Set | Skip - metadata table |

**Explanation**: These are backing tables for choice/picklist fields. They don't store user data and don't support direct privilege assignment. This is expected behavior - privileges on choice values are inherited from the parent entity.

---

## Verification Results (2025-12-28)

```
pac env fetch --xml "<fetch><entity name='role'><attribute name='name'/><filter><condition attribute='name' operator='like' value='%Spaarke AI Analysis%'/></filter></entity></fetch>"

name                      roleid
Spaarke AI Analysis Admin b7bbc845-6adf-41cb-b0bf-3c77cac93135
Spaarke AI Analysis User  504bef50-a4ee-4df8-9267-d4155a8554e7
```

**Status**: Both roles created successfully.

---

*Documentation created: 2025-12-28*
*Roles created: 2025-12-28*
