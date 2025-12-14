# Security Roles - AI Document Analysis

> **Solution**: Spaarke_DocumentIntelligence
> **Version**: 1.0.0.0
> **Last Updated**: December 14, 2025

---

## Overview

This document defines the security roles required for the AI Document Analysis feature. These roles should be created in the Spaarke Dataverse solution.

---

## Role Definitions

### 1. Analysis User (`sprk_AnalysisUser`)

**Purpose**: Standard user role for creating and working with document analyses.

| Entity | Create | Read | Write | Delete | Append | Append To |
|--------|--------|------|-------|--------|--------|-----------|
| `sprk_analysis` | User | User | User | User | User | User |
| `sprk_analysisaction` | None | Organization | None | None | None | None |
| `sprk_analysisskill` | None | Organization | None | None | None | None |
| `sprk_analysisknowledge` | None | Organization | None | None | None | None |
| `sprk_analysistool` | None | Organization | None | None | None | None |
| `sprk_analysisplaybook` | None | Organization | None | None | Organization | Organization |
| `sprk_analysisworkingversion` | User | User | User | User | User | User |
| `sprk_analysischatmessage` | User | User | User | User | User | User |
| `sprk_analysisemailmetadata` | User | User | User | User | User | User |
| `sprk_document` | None | User | None | None | User | User |

**Notes**:
- Users can only access their own analyses (User-level access)
- Can read all published playbooks, skills, actions, etc. (Organization-level read)
- Cannot modify configuration entities (actions, skills, knowledge, tools, playbooks)

---

### 2. Analysis Administrator (`sprk_AnalysisAdmin`)

**Purpose**: Administrator role for configuring analysis capabilities across the organization.

| Entity | Create | Read | Write | Delete | Append | Append To |
|--------|--------|------|-------|--------|--------|-----------|
| `sprk_analysis` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysisaction` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysisskill` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysisknowledge` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_knowledgedeployment` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysistool` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysisplaybook` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysisworkingversion` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysischatmessage` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_analysisemailmetadata` | Organization | Organization | Organization | Organization | Organization | Organization |
| `sprk_document` | Organization | Organization | Organization | Organization | Organization | Organization |

**Notes**:
- Full access to all analysis entities
- Can create and manage playbooks, skills, actions, knowledge sources
- Can view and manage all users' analyses
- Should be assigned to administrators and power users

---

### 3. Analysis Read Only (`sprk_AnalysisReadOnly`)

**Purpose**: View-only access to analyses (for reviewers, auditors).

| Entity | Create | Read | Write | Delete | Append | Append To |
|--------|--------|------|-------|--------|--------|-----------|
| `sprk_analysis` | None | Organization | None | None | None | None |
| `sprk_analysisaction` | None | Organization | None | None | None | None |
| `sprk_analysisskill` | None | Organization | None | None | None | None |
| `sprk_analysisknowledge` | None | Organization | None | None | None | None |
| `sprk_analysistool` | None | Organization | None | None | None | None |
| `sprk_analysisplaybook` | None | Organization | None | None | None | None |
| `sprk_analysisworkingversion` | None | Organization | None | None | None | None |
| `sprk_analysischatmessage` | None | Organization | None | None | None | None |
| `sprk_analysisemailmetadata` | None | Organization | None | None | None | None |
| `sprk_document` | None | Organization | None | None | None | None |

**Notes**:
- Read-only access to all analysis entities
- Cannot create, modify, or delete any records
- Suitable for auditors, compliance reviewers, managers

---

## Environment Variable Permissions

All roles require read access to the following environment variable definitions:

| Environment Variable | Permission |
|---------------------|------------|
| `sprk_BffApiBaseUrl` | Read |
| `sprk_EnableAiFeatures` | Read |
| `sprk_EnableMultiDocumentAnalysis` | Read |
| `sprk_DeploymentEnvironment` | Read |

**Note**: Environment variable **values** are readable by all users who have access to the environment variable definition.

---

## Web Resource Permissions

| Web Resource | Analysis User | Analysis Admin | Analysis Read Only |
|--------------|---------------|----------------|-------------------|
| `sprk_analysis_commands.js` | Execute | Execute | Execute |
| `sprk_AnalysisIcon16.svg` | Read | Read | Read |
| `sprk_AnalysisIcon32.svg` | Read | Read | Read |

---

## Custom Page Permissions

| Custom Page | Analysis User | Analysis Admin | Analysis Read Only |
|-------------|---------------|----------------|-------------------|
| `sprk_analysisbuilder` | Use | Use | None |
| `sprk_analysisworkspace` | Use | Use | Use (Read-Only) |

---

## Role Hierarchy

```
sprk_AnalysisAdmin (inherits sprk_AnalysisUser)
    └── sprk_AnalysisUser

sprk_AnalysisReadOnly (standalone - no inheritance)
```

---

## Assignment Recommendations

| User Type | Recommended Roles |
|-----------|------------------|
| Regular Users | `sprk_AnalysisUser` + Business Unit roles |
| Power Users | `sprk_AnalysisUser` + `sprk_AnalysisAdmin` |
| System Administrators | `sprk_AnalysisAdmin` |
| Auditors/Compliance | `sprk_AnalysisReadOnly` |
| External Reviewers | `sprk_AnalysisReadOnly` (via Teams) |

---

## Implementation Steps

### Step 1: Create Roles in Solution

1. Open Power Platform Admin Center
2. Navigate to Environments → [Environment] → Settings → Security → Security Roles
3. Create each role with the permissions defined above
4. Add roles to the Spaarke_DocumentIntelligence solution

### Step 2: Configure Role Inheritance

1. Open `sprk_AnalysisAdmin` role
2. In the "Member's privilege inheritance" section
3. Add `sprk_AnalysisUser` as inherited role

### Step 3: Test Permissions

1. Create test users with each role
2. Verify Analysis User can:
   - Create new analysis from Document form
   - Edit own analyses
   - Cannot see other users' analyses
   - Can view all playbooks
3. Verify Analysis Admin can:
   - View all analyses
   - Create/edit playbooks
   - Manage skills and actions
4. Verify Analysis Read Only can:
   - View all analyses
   - Cannot create/edit anything

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| User cannot create analysis | Missing `sprk_AnalysisUser` role | Assign role to user |
| User cannot see playbooks | Missing Organization-level read on `sprk_analysisplaybook` | Verify role configuration |
| Admin cannot manage skills | Missing `sprk_AnalysisAdmin` role | Assign admin role |
| API returns 403 | User has Dataverse role but not API permission | Check BFF API authorization |

---

*Last updated: December 14, 2025*
