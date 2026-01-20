# Dataverse Ownership Fields Specification

> **Project**: ai-playbook-node-builder-r3
> **Task**: 005 - Add Dataverse Ownership Fields
> **Created**: 2026-01-19
> **Purpose**: Manual field creation guide for Dataverse scope entities

---

## Overview

This document provides exact specifications for adding ownership-related fields to all four scope entities in Dataverse. These fields enable the ownership model (System vs Customer scopes), immutability enforcement, and scope lineage tracking (Save As, Extend operations).

**Target Entities**:
1. `sprk_analysisaction`
2. `sprk_analysisskill`
3. `sprk_analysisknowledge`
4. `sprk_analysistool`

---

## Global Option Set: Scope Owner Type

**Create this option set FIRST before adding fields to entities.**

| Property | Value |
|----------|-------|
| **Schema Name** | `sprk_scopeownertype` |
| **Display Name** | Scope Owner Type |
| **Description** | Indicates whether a scope is system-managed or customer-owned |
| **Is Global** | Yes |
| **Type** | Picklist (Choice) |

### Options

| Value | Label | Description |
|-------|-------|-------------|
| `1` | System | System-managed scope (SYS- prefix, immutable) |
| `2` | Customer | Customer-owned scope (CUST- prefix, editable) |

### Web API Script (Optional)

If using Web API to create:

```powershell
$optionSetDef = @{
    "@odata.type"  = "Microsoft.Dynamics.CRM.OptionSetMetadata"
    "Name"         = "sprk_scopeownertype"
    "DisplayName"  = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label" = "Scope Owner Type"
                "LanguageCode" = 1033
            }
        )
    }
    "Description"  = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label" = "Indicates whether a scope is system-managed or customer-owned"
                "LanguageCode" = 1033
            }
        )
    }
    "IsGlobal"     = $true
    "OptionSetType" = "Picklist"
    "Options"      = @(
        @{ "Value" = 1; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "System"; "LanguageCode" = 1033 }) } }
        @{ "Value" = 2; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Customer"; "LanguageCode" = 1033 }) } }
    )
}
```

---

## Field Specifications

### Field 1: sprk_ownertype (Choice)

| Property | Value |
|----------|-------|
| **Schema Name** | `sprk_ownertype` |
| **Display Name** | Owner Type |
| **Data Type** | Choice (Picklist) |
| **Required Level** | Business Required |
| **Global Option Set** | `sprk_scopeownertype` |
| **Default Value** | `2` (Customer) |
| **Description** | Indicates whether this scope is system-managed or customer-owned |
| **Searchable** | Yes |
| **Auditing** | Enabled |

**Business Rules**:
- System (1): Scope has `SYS-` prefix, cannot be modified or deleted
- Customer (2): Scope has `CUST-` prefix, can be modified by users with permission

### Field 2: sprk_isimmutable (Boolean)

| Property | Value |
|----------|-------|
| **Schema Name** | `sprk_isimmutable` |
| **Display Name** | Is Immutable |
| **Data Type** | Yes/No (Boolean) |
| **Required Level** | Optional |
| **Default Value** | `No` (false) |
| **True Label** | Yes |
| **False Label** | No |
| **Description** | When true, this scope cannot be modified. System scopes are always immutable. |
| **Searchable** | Yes |
| **Auditing** | Enabled |

**Business Rules**:
- Set to `true` for all System scopes (sprk_ownertype = 1)
- Set to `false` by default for Customer scopes
- Can be set to `true` for Customer scopes to lock them
- Once immutable, cannot be changed back to mutable via UI

### Field 3: sprk_parentscope (Self-Referencing Lookup)

| Property | Value |
|----------|-------|
| **Schema Name** | `sprk_parentscope` |
| **Display Name** | Parent Scope |
| **Data Type** | Lookup |
| **Target Entity** | Same entity (self-reference) |
| **Required Level** | Optional |
| **Description** | References the parent scope when this scope was created via "Extend" operation |
| **Searchable** | Yes |
| **Auditing** | Enabled |

**Relationship Details (per entity)**:

| Entity | Relationship Schema Name |
|--------|-------------------------|
| `sprk_analysisaction` | `sprk_analysisaction_parentscope` |
| `sprk_analysisskill` | `sprk_analysisskill_parentscope` |
| `sprk_analysisknowledge` | `sprk_analysisknowledge_parentscope` |
| `sprk_analysistool` | `sprk_analysistool_parentscope` |

**Cascade Configuration**:
| Cascade Type | Behavior |
|--------------|----------|
| Assign | NoCascade |
| Delete | RemoveLink |
| Merge | NoCascade |
| Reparent | NoCascade |
| Share | NoCascade |
| Unshare | NoCascade |

**Usage**:
- When user "Extends" a scope, the new scope's `sprk_parentscope` points to the original
- Enables inheritance tracking and scope hierarchy navigation
- Used to find all extended versions of a system scope

### Field 4: sprk_basedon (Self-Referencing Lookup)

| Property | Value |
|----------|-------|
| **Schema Name** | `sprk_basedon` |
| **Display Name** | Based On |
| **Data Type** | Lookup |
| **Target Entity** | Same entity (self-reference) |
| **Required Level** | Optional |
| **Description** | References the source scope when this scope was created via "Save As" operation |
| **Searchable** | Yes |
| **Auditing** | Enabled |

**Relationship Details (per entity)**:

| Entity | Relationship Schema Name |
|--------|-------------------------|
| `sprk_analysisaction` | `sprk_analysisaction_basedon` |
| `sprk_analysisskill` | `sprk_analysisskill_basedon` |
| `sprk_analysisknowledge` | `sprk_analysisknowledge_basedon` |
| `sprk_analysistool` | `sprk_analysistool_basedon` |

**Cascade Configuration**:
| Cascade Type | Behavior |
|--------------|----------|
| Assign | NoCascade |
| Delete | RemoveLink |
| Merge | NoCascade |
| Reparent | NoCascade |
| Share | NoCascade |
| Unshare | NoCascade |

**Usage**:
- When user performs "Save As" on a scope, the new scope's `sprk_basedon` points to the original
- New scope is a full copy (not linked for updates)
- Used for lineage tracking and audit purposes

---

## Entity-Specific Configuration

### sprk_analysisaction

| Field | Schema Name | Type | Default |
|-------|-------------|------|---------|
| Owner Type | `sprk_ownertype` | Choice | Customer (2) |
| Is Immutable | `sprk_isimmutable` | Yes/No | No |
| Parent Scope | `sprk_parentscope` | Lookup (self) | null |
| Based On | `sprk_basedon` | Lookup (self) | null |

**Lookup Relationship Names**:
- `sprk_analysisaction_parentscope` (for Extend)
- `sprk_analysisaction_basedon` (for Save As)

### sprk_analysisskill

| Field | Schema Name | Type | Default |
|-------|-------------|------|---------|
| Owner Type | `sprk_ownertype` | Choice | Customer (2) |
| Is Immutable | `sprk_isimmutable` | Yes/No | No |
| Parent Scope | `sprk_parentscope` | Lookup (self) | null |
| Based On | `sprk_basedon` | Lookup (self) | null |

**Lookup Relationship Names**:
- `sprk_analysisskill_parentscope` (for Extend)
- `sprk_analysisskill_basedon` (for Save As)

### sprk_analysisknowledge

| Field | Schema Name | Type | Default |
|-------|-------------|------|---------|
| Owner Type | `sprk_ownertype` | Choice | Customer (2) |
| Is Immutable | `sprk_isimmutable` | Yes/No | No |
| Parent Scope | `sprk_parentscope` | Lookup (self) | null |
| Based On | `sprk_basedon` | Lookup (self) | null |

**Lookup Relationship Names**:
- `sprk_analysisknowledge_parentscope` (for Extend)
- `sprk_analysisknowledge_basedon` (for Save As)

### sprk_analysistool

| Field | Schema Name | Type | Default |
|-------|-------------|------|---------|
| Owner Type | `sprk_ownertype` | Choice | Customer (2) |
| Is Immutable | `sprk_isimmutable` | Yes/No | No |
| Parent Scope | `sprk_parentscope` | Lookup (self) | null |
| Based On | `sprk_basedon` | Lookup (self) | null |

**Lookup Relationship Names**:
- `sprk_analysistool_parentscope` (for Extend)
- `sprk_analysistool_basedon` (for Save As)

---

## Manual Creation Steps in Power Platform

### Step 1: Create Global Option Set

1. Navigate to **Power Apps** > **Solutions**
2. Open or create an **unmanaged solution** (per ADR-022)
3. Click **New** > **More** > **Choice** (Global)
4. Enter:
   - Display Name: `Scope Owner Type`
   - Name: `sprk_scopeownertype`
5. Add options:
   - Value: `1`, Label: `System`
   - Value: `2`, Label: `Customer`
6. Click **Save**

### Step 2: Add Fields to Each Entity

For each of the four entities (`sprk_analysisaction`, `sprk_analysisskill`, `sprk_analysisknowledge`, `sprk_analysistool`):

#### Add sprk_ownertype (Choice)

1. Open the entity in solution
2. Click **New** > **Column**
3. Configure:
   - Display Name: `Owner Type`
   - Name: `sprk_ownertype`
   - Data Type: `Choice`
   - Sync this choice with: `sprk_scopeownertype` (global)
   - Required: `Business Required`
   - Default Value: `Customer`
4. Click **Save**

#### Add sprk_isimmutable (Yes/No)

1. Click **New** > **Column**
2. Configure:
   - Display Name: `Is Immutable`
   - Name: `sprk_isimmutable`
   - Data Type: `Yes/No`
   - Default Value: `No`
4. Click **Save**

#### Add sprk_parentscope (Self-Reference Lookup)

1. Click **New** > **Column**
2. Configure:
   - Display Name: `Parent Scope`
   - Name: `sprk_parentscope`
   - Data Type: `Lookup`
   - Related Table: **Same entity** (e.g., `sprk_analysisaction`)
3. Advanced options:
   - Delete: `Remove Link`
4. Click **Save**

#### Add sprk_basedon (Self-Reference Lookup)

1. Click **New** > **Column**
2. Configure:
   - Display Name: `Based On`
   - Name: `sprk_basedon`
   - Data Type: `Lookup`
   - Related Table: **Same entity** (e.g., `sprk_analysisaction`)
3. Advanced options:
   - Delete: `Remove Link`
4. Click **Save**

### Step 3: Publish Customizations

1. In the solution, click **Publish all customizations**
2. Wait for publishing to complete

### Step 4: Export Solution (for Source Control)

1. Click **Export** on the solution
2. Choose **Unmanaged**
3. Download and extract the solution zip
4. Commit to git repository

---

## Verification Checklist

After creating fields, verify:

- [ ] Global option set `sprk_scopeownertype` exists with values 1 (System) and 2 (Customer)
- [ ] All four entities have `sprk_ownertype` field
- [ ] All four entities have `sprk_isimmutable` field
- [ ] All four entities have `sprk_parentscope` self-referencing lookup
- [ ] All four entities have `sprk_basedon` self-referencing lookup
- [ ] Default values are set correctly (ownertype=Customer, isimmutable=No)
- [ ] Lookups use RemoveLink cascade for delete
- [ ] Solution exports cleanly as unmanaged

---

## Web API Script (Complete)

For automated deployment, use this PowerShell script pattern:

```powershell
# Assumes Invoke-DataverseApi helper function is defined
# See: docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md

$entities = @(
    "sprk_analysisaction",
    "sprk_analysisskill",
    "sprk_analysisknowledge",
    "sprk_analysistool"
)

# 1. Create global option set (if not exists)
# 2. For each entity:
#    - Add sprk_ownertype picklist
#    - Add sprk_isimmutable boolean
#    - Create sprk_parentscope relationship (via RelationshipDefinitions)
#    - Create sprk_basedon relationship (via RelationshipDefinitions)
# 3. Publish customizations
```

**Note**: Self-referencing lookups MUST be created via RelationshipDefinitions endpoint, not Attributes endpoint. See the guide for complete examples.

---

## Related Documents

- [DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md](../../../../docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) - Web API creation patterns
- [ADR-022](../../../../.claude/adr/ADR-022-pcf-platform-libraries.md) - Unmanaged solution requirement
- [Project CLAUDE.md](../CLAUDE.md) - Scope ownership model context

---

*Specification created: 2026-01-19 for task 005*
