# Dataverse Entity Deployment Guide

> **Project**: Events and Workflow Automation R1
> **Created**: 2026-02-01
> **Purpose**: Manual Dataverse entity creation guide for deployment

---

## Overview

This document provides the complete schema definitions for the 5 Dataverse entities required by the Events and Workflow Automation R1 project. These entities must be created in Dataverse before the API stubs can be replaced with actual implementations.

## Entity Summary

| Entity | Logical Name | Display Name | Purpose |
|--------|--------------|--------------|---------|
| Event | `sprk_event` | Event | Main event tracking entity |
| Event Type | `sprk_eventtype` | Event Type | Event categorization and requirements |
| Event Log | `sprk_eventlog` | Event Log | State transition audit trail |
| Field Mapping Profile | `sprk_fieldmappingprofile` | Field Mapping Profile | Parent-child field mapping configuration |
| Field Mapping Rule | `sprk_fieldmappingrule` | Field Mapping Rule | Individual field mapping rules |

---

## 1. Event Type Entity (`sprk_eventtype`)

**Create this entity FIRST** - Event depends on it.

### Fields

| Display Name | Logical Name | Type | Required | Options/Notes |
|--------------|--------------|------|----------|---------------|
| Name | `sprk_name` | Single Line Text (100) | Yes | Primary field |
| Event Code | `sprk_eventcode` | Single Line Text (50) | No | Unique identifier code |
| Description | `sprk_description` | Multiline Text | No | |
| Status | `statecode` | Choice | Yes | Active (0), Inactive (1) |
| Requires Due Date | `sprk_requiresduedate` | Choice | No | No (0), Yes (1) |
| Requires Base Date | `sprk_requiresbasedate` | Choice | No | No (0), Yes (1) |

### Seed Data (Event Types)

```json
[
  { "sprk_name": "Meeting", "sprk_eventcode": "MEETING", "sprk_requiresduedate": 0, "sprk_requiresbasedate": 1 },
  { "sprk_name": "Deadline", "sprk_eventcode": "DEADLINE", "sprk_requiresduedate": 1, "sprk_requiresbasedate": 0 },
  { "sprk_name": "Reminder", "sprk_eventcode": "REMINDER", "sprk_requiresduedate": 1, "sprk_requiresbasedate": 0 },
  { "sprk_name": "Follow-up", "sprk_eventcode": "FOLLOWUP", "sprk_requiresduedate": 1, "sprk_requiresbasedate": 0 },
  { "sprk_name": "Court Date", "sprk_eventcode": "COURTDATE", "sprk_requiresduedate": 1, "sprk_requiresbasedate": 1 },
  { "sprk_name": "Filing Deadline", "sprk_eventcode": "FILING", "sprk_requiresduedate": 1, "sprk_requiresbasedate": 0 },
  { "sprk_name": "Task", "sprk_eventcode": "TASK", "sprk_requiresduedate": 0, "sprk_requiresbasedate": 0 },
  { "sprk_name": "Milestone", "sprk_eventcode": "MILESTONE", "sprk_requiresduedate": 1, "sprk_requiresbasedate": 1 }
]
```

---

## 2. Event Entity (`sprk_event`)

### Basic Fields

| Display Name | Logical Name | Type | Required | Options/Notes |
|--------------|--------------|------|----------|---------------|
| Event Name | `sprk_eventname` | Single Line Text (200) | Yes | Primary field |
| Description | `sprk_description` | Multiline Text | No | |
| Event Type | `sprk_eventtype_ref` | Lookup | No | N:1 to `sprk_eventtype` |
| Status | `statecode` | Choice | Yes | Active (0), Inactive (1) |
| Status Reason | `statuscode` | Choice | Yes | See Status Reason options below |
| Base Date | `sprk_basedate` | Date Only | No | Event date |
| Due Date | `sprk_duedate` | Date Only | No | |
| Completed Date | `sprk_completeddate` | Date Only | No | Auto-set on completion |
| Priority | `sprk_priority` | Choice | No | Low (0), Normal (1), High (2), Urgent (3) |
| Source | `sprk_source` | Choice | No | User (0), System (1), Workflow (2), External (3) |
| Remind At | `sprk_remindat` | DateTime | No | For future reminder calc |
| Related Event | `sprk_relatedevent` | Lookup | No | N:1 self-reference |
| Related Event Type | `sprk_relatedeventtype` | Choice | No | Reminder (0), Notification (1), Extension (2) |
| Related Event Offset Type | `sprk_relatedeventoffsettype` | Choice | No | Hours Before (0), Hours After (1), Days Before (2), Days After (3), Fixed (4) |

### Status Reason (`statuscode`) Options

| Value | Label | State |
|-------|-------|-------|
| 1 | Draft | Active (0) |
| 2 | Planned | Active (0) |
| 3 | Open | Active (0) |
| 4 | On Hold | Active (0) |
| 5 | Completed | Inactive (1) |
| 6 | Cancelled | Inactive (1) |
| 7 | Deleted | Inactive (1) |

### Regarding Lookup Fields

These lookups enable entity-specific subgrid filtering:

| Display Name | Logical Name | Type | Target Entity |
|--------------|--------------|------|---------------|
| Regarding Account | `sprk_regardingaccount` | Lookup | `account` |
| Regarding Analysis | `sprk_regardinganalysis` | Lookup | `sprk_analysis` |
| Regarding Contact | `sprk_regardingcontact` | Lookup | `contact` |
| Regarding Invoice | `sprk_regardinginvoice` | Lookup | `sprk_invoice` |
| Regarding Matter | `sprk_regardingmatter` | Lookup | `sprk_matter` |
| Regarding Project | `sprk_regardingproject` | Lookup | `sprk_project` |
| Regarding Budget | `sprk_regardingbudget` | Lookup | `sprk_budget` |
| Regarding Work Assignment | `sprk_regardingworkassignment` | Lookup | `sprk_workassignment` |

### Denormalized Reference Fields

These enable unified cross-entity views with clickable links:

| Display Name | Logical Name | Type | Notes |
|--------------|--------------|------|-------|
| Regarding Record Id | `sprk_regardingrecordid` | Single Line Text (50) | GUID as string |
| Regarding Record Name | `sprk_regardingrecordname` | Single Line Text (200) | Display name |
| Regarding Record Type | `sprk_regardingrecordtype` | Choice | See options below |

### Regarding Record Type Options

| Value | Label |
|-------|-------|
| 0 | Project |
| 1 | Matter |
| 2 | Invoice |
| 3 | Analysis |
| 4 | Account |
| 5 | Contact |
| 6 | Work Assignment |
| 7 | Budget |

---

## 3. Event Log Entity (`sprk_eventlog`)

### Fields

| Display Name | Logical Name | Type | Required | Options/Notes |
|--------------|--------------|------|----------|---------------|
| Event Log Name | `sprk_eventlogname` | Single Line Text (100) | Yes | Primary (auto-generated) |
| Event | `sprk_event` | Lookup | Yes | N:1 to `sprk_event` |
| Action | `sprk_action` | Choice | Yes | See Action options below |
| Description | `sprk_description` | Multiline Text | No | Details of change |

### Action Options

| Value | Label |
|-------|-------|
| 0 | Created |
| 1 | Updated |
| 2 | Completed |
| 3 | Cancelled |
| 4 | Deleted |

---

## 4. Field Mapping Profile Entity (`sprk_fieldmappingprofile`)

### Fields

| Display Name | Logical Name | Type | Required | Options/Notes |
|--------------|--------------|------|----------|---------------|
| Profile Name | `sprk_name` | Single Line Text (100) | Yes | Primary field |
| Source Entity | `sprk_sourceentity` | Single Line Text (100) | Yes | Logical name (e.g., `sprk_matter`) |
| Target Entity | `sprk_targetentity` | Single Line Text (100) | Yes | Logical name (e.g., `sprk_event`) |
| Mapping Direction | `sprk_mappingdirection` | Choice | Yes | Parent to Child (0), Child to Parent (1), Bidirectional (2) |
| Sync Mode | `sprk_syncmode` | Choice | Yes | One-time (0), Manual Refresh (1) |
| Is Active | `sprk_isactive` | Yes/No | Yes | Default: Yes |
| Description | `sprk_description` | Multiline Text | No | Admin notes |

---

## 5. Field Mapping Rule Entity (`sprk_fieldmappingrule`)

### Fields

| Display Name | Logical Name | Type | Required | Options/Notes |
|--------------|--------------|------|----------|---------------|
| Rule Name | `sprk_name` | Single Line Text (100) | Yes | Primary field |
| Mapping Profile | `sprk_fieldmappingprofile` | Lookup | Yes | N:1 to profile |
| Source Field | `sprk_sourcefield` | Single Line Text (100) | Yes | Schema name |
| Source Field Type | `sprk_sourcefieldtype` | Choice | Yes | See Field Type options |
| Target Field | `sprk_targetfield` | Single Line Text (100) | Yes | Schema name |
| Target Field Type | `sprk_targetfieldtype` | Choice | Yes | Must be compatible |
| Compatibility Mode | `sprk_compatibilitymode` | Choice | Yes | Strict (0), Resolve (1) |
| Is Required | `sprk_isrequired` | Yes/No | No | Default: No |
| Default Value | `sprk_defaultvalue` | Single Line Text (500) | No | When source is empty |
| Is Cascading Source | `sprk_iscascadingsource` | Yes/No | No | Default: No |
| Execution Order | `sprk_executionorder` | Whole Number | No | Default: 0 |
| Is Active | `sprk_isactive` | Yes/No | Yes | Default: Yes |

### Field Type Options

| Value | Label |
|-------|-------|
| 0 | Text |
| 1 | Lookup |
| 2 | OptionSet |
| 3 | Number |
| 4 | DateTime |
| 5 | Boolean |
| 6 | Memo |

---

## Deployment Steps

### Power Apps Maker Portal

1. Navigate to https://make.powerapps.com
2. Select your environment (e.g., spaarkedev1)
3. Go to Tables (Dataverse)
4. Create each entity in order:
   1. Event Type (no dependencies)
   2. Field Mapping Profile (no dependencies)
   3. Event (depends on Event Type)
   4. Event Log (depends on Event)
   5. Field Mapping Rule (depends on Field Mapping Profile)

### PAC CLI (Alternative)

```powershell
# Authenticate
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Create solution
pac solution init --publisher-name spaarke --publisher-prefix sprk

# Add tables (would need to create XML definitions first)
# This approach requires solution packaging knowledge
```

---

## Verification Queries

After creating entities, verify with these OData queries:

```
# Check Event Type exists
GET /api/data/v9.2/sprk_eventtypes?$top=1

# Check Event exists
GET /api/data/v9.2/sprk_events?$top=1

# Check Event Log exists
GET /api/data/v9.2/sprk_eventlogs?$top=1

# Check Field Mapping Profile exists
GET /api/data/v9.2/sprk_fieldmappingprofiles?$top=1

# Check Field Mapping Rule exists
GET /api/data/v9.2/sprk_fieldmappingrules?$top=1
```

---

## Post-Deployment

After entities are created in Dataverse:

1. Seed Event Type records using the seed data above
2. Update BFF API to use actual Dataverse queries (replace stubs)
3. Configure forms and views in model-driven app
4. Deploy PCF controls

---

*This document serves as the source of truth for entity schema during manual deployment.*
