# sprk_gridconfiguration Entity Schema

> **Entity Purpose**: Store custom grid view configurations for the Universal DataGrid component.
> Enables admin-configurable views without solution deployment.

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_gridconfiguration |
| **Display Name** | Grid Configuration |
| **Plural Display Name** | Grid Configurations |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | Organization |
| **Description** | Custom grid view and FetchXML configurations for Universal DataGrid |

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_gridconfigurationid | Grid Configuration | Uniqueidentifier | Auto | - | Primary key |
| sprk_name | Name | String | Yes | 100 | Configuration display name |

### Core Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_entitylogicalname | Entity Logical Name | String | Yes | 100 | Target entity for this configuration (e.g., "sprk_event") |
| sprk_viewtype | View Type | Choice | Yes | - | Configuration type (see Choice Values below) |
| sprk_savedviewid | Saved View ID | String | No | 36 | GUID reference to savedquery (for SavedView type) |
| sprk_fetchxml | FetchXML | Multiline | No | 1048576 | Custom FetchXML query (for CustomFetchXML type) |
| sprk_layoutxml | Layout XML | Multiline | No | 1048576 | Column layout definition |
| sprk_configjson | Configuration JSON | Multiline | No | 1048576 | Additional JSON configuration (filters, formatting, etc.) |

### Display Fields

| Logical Name | Display Name | Type | Required | Default | Description |
|--------------|--------------|------|----------|---------|-------------|
| sprk_isdefault | Is Default | Boolean | No | false | Whether this is the default view for the entity |
| sprk_sortorder | Sort Order | Integer | No | 100 | Display order in view selector (lower = first) |
| sprk_iconname | Icon Name | String | No | 50 | Fluent UI icon name for view selector |
| sprk_description | Description | Multiline | No | 2000 | Admin notes about this configuration |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| statecode | Status | State | Active/Inactive |
| statuscode | Status Reason | Status | Reason for state |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup | User who created the record |
| modifiedby | Modified By | Lookup | User who last modified the record |

## Choice Values

### sprk_viewtype (View Type)

| Value | Label | Description |
|-------|-------|-------------|
| 1 | Saved View | Reference to existing savedquery view |
| 2 | Custom FetchXML | Inline FetchXML and layout |
| 3 | Linked View | Reference to another configuration (for reuse) |

## Form Layout

### Main Form: Information

**Header Section**
- sprk_name (Display Name)
- sprk_entitylogicalname
- sprk_viewtype
- sprk_isdefault

**General Section**
- sprk_savedviewid (visible when viewtype = SavedView)
- sprk_fetchxml (visible when viewtype = CustomFetchXML)
- sprk_layoutxml
- sprk_configjson

**Display Section**
- sprk_sortorder
- sprk_iconname
- sprk_description

**System Section**
- createdon
- modifiedon
- createdby
- modifiedby

## Views

### Active Grid Configurations (Default View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 200 | 1 (ASC) |
| sprk_entitylogicalname | 150 | - |
| sprk_viewtype | 120 | - |
| sprk_isdefault | 80 | - |
| sprk_sortorder | 80 | - |
| modifiedon | 150 | - |

**Filter**: statecode = Active

### All Grid Configurations

Same columns as above, no filter.

### Configurations by Entity

Same columns, grouped by sprk_entitylogicalname.

## Business Rules

1. **Require SavedViewId for SavedView Type**
   - When sprk_viewtype = 1 (SavedView), sprk_savedviewid is required
   - When sprk_viewtype != 1, sprk_savedviewid should be empty

2. **Require FetchXML for CustomFetchXML Type**
   - When sprk_viewtype = 2 (CustomFetchXML), sprk_fetchxml is required
   - LayoutXML is recommended but not required

3. **Single Default per Entity**
   - Only one record per entity can have sprk_isdefault = true
   - Implemented via plugin or workflow

## Security

### Security Roles

| Role | Create | Read | Write | Delete |
|------|--------|------|-------|--------|
| System Administrator | Yes | Yes | Yes | Yes |
| System Customizer | Yes | Yes | Yes | Yes |
| Basic User | No | Yes | No | No |

### Field Security

No field-level security required - this is admin configuration data.

## Example Records

### Example 1: Reference to savedquery

```json
{
  "sprk_name": "Active Events",
  "sprk_entitylogicalname": "sprk_event",
  "sprk_viewtype": 1,
  "sprk_savedviewid": "12345678-1234-1234-1234-123456789012",
  "sprk_isdefault": true,
  "sprk_sortorder": 1,
  "sprk_iconname": "CalendarAgenda"
}
```

### Example 2: Custom FetchXML

```json
{
  "sprk_name": "Overdue Events",
  "sprk_entitylogicalname": "sprk_event",
  "sprk_viewtype": 2,
  "sprk_fetchxml": "<fetch><entity name='sprk_event'><attribute name='sprk_eventid'/><attribute name='sprk_eventname'/><attribute name='sprk_duedate'/><filter><condition attribute='sprk_duedate' operator='lt' value='@today'/></filter></entity></fetch>",
  "sprk_layoutxml": "<grid><row><cell name='sprk_eventname' width='200'/><cell name='sprk_duedate' width='100'/></row></grid>",
  "sprk_isdefault": false,
  "sprk_sortorder": 10,
  "sprk_iconname": "Warning"
}
```

## Integration with ViewService

The `ViewService` in `@spaarke/ui-components` queries this entity:

```typescript
// ViewService retrieves configurations for an entity
const configs = await viewService.getViews("sprk_event", { includeCustom: true });

// Returns merged list of:
// - savedquery views (sprk_viewtype = 1 references)
// - Custom FetchXML views (sprk_viewtype = 2)
// - Sorted by sprk_sortorder
```

## Deployment

This entity should be added to the **SpaarkeCore** solution or a dedicated **SpaarkeConfiguration** solution.

### Power Platform CLI Commands

```bash
# Create entity via maker portal, then export
pac solution export --name SpaarkeCore --path ./exports --managed false

# Or create via solution file
pac solution pack --folder ./SpaarkeCore --zipfile SpaarkeCore.zip
pac solution import --path SpaarkeCore.zip
```

---

*Schema version: 1.0 | Created: 2026-02-05*
