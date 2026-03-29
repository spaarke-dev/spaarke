# sprk_workspacelayout Entity Schema

> **Task**: WKSP-004 | **Created**: 2026-03-29

## Entity Definition

| Property | Value |
|----------|-------|
| Display Name | Workspace Layout |
| Plural Name | Workspace Layouts |
| Schema Name | sprk_workspacelayout |
| Primary Key | sprk_workspacelayoutid |
| Primary Name Attribute | sprk_name |
| Ownership Type | User (Owner-based) |
| Publisher Prefix | sprk |
| Solution | Spaarke (unmanaged) |

## Columns

| Column | Schema Name | Type | Required | Max Length | Default | Purpose |
|--------|------------|------|----------|-----------|---------|---------|
| ID | sprk_workspacelayoutid | Uniqueidentifier | PK | -- | Auto-generated | Primary key |
| Name | sprk_name | String | Yes | 100 | -- | Workspace display name |
| Layout Template ID | sprk_layouttemplateid | String | Yes | 50 | -- | Template identifier (e.g., "2-col-equal", "sidebar-main") |
| Sections JSON | sprk_sectionsjson | Memo (Multiline Text) | Yes | 1048576 | -- | JSON layout definition (see schema below) |
| Is Default | sprk_isdefault | Boolean | Yes | -- | false | Whether this is the user's default workspace |
| Sort Order | sprk_sortorder | WholeNumber (Integer) | No | -- | 0 | Display order in workspace dropdown |
| Owner | ownerid | Owner (Lookup to User) | Yes | -- | Current user | Standard Dataverse owner field |

### Column Notes

- **sprk_workspacelayoutid**: Standard Dataverse GUID primary key, auto-generated on create.
- **sprk_name**: User-provided display name shown in the workspace switcher dropdown. Max 100 characters.
- **sprk_layouttemplateid**: References one of 6 predefined layout templates: `2-col-equal`, `3-row-mixed`, `sidebar-main`, `single-column`, `3-col-equal`, `hero-grid`.
- **sprk_sectionsjson**: Stores the full layout definition as JSON (see schema below). Uses Multiline Text with max length (1,048,576 characters) to accommodate large section arrays.
- **sprk_isdefault**: Only one record per user should have this set to `true`. The BFF API enforces single-default by clearing the previous default on save.
- **sprk_sortorder**: Controls display ordering in the workspace dropdown. Lower values appear first.
- **ownerid**: Standard Dataverse ownership field. Not a custom column -- provided by the platform when using Owner security model.

## sprk_sectionsjson Schema

### Structure

The `sprk_sectionsjson` column stores a JSON object with the following structure:

```json
{
  "schemaVersion": 1,
  "rows": [
    {
      "id": "row-1",
      "columns": "1fr 1fr",
      "columnsSmall": "1fr",
      "sections": ["get-started", "quick-summary"]
    }
  ]
}
```

### Field Definitions

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | integer | Yes | Schema version for forward compatibility. Current version: `1`. Config builder checks this and migrates if needed. |
| `rows` | array | Yes | Array of row definitions describing the layout grid. |
| `rows[].id` | string | Yes | Unique row identifier (e.g., "row-1", "row-2"). |
| `rows[].columns` | string | Yes | CSS Grid `grid-template-columns` value for standard viewports (e.g., "1fr 1fr", "1fr 2fr"). |
| `rows[].columnsSmall` | string | Yes | CSS Grid `grid-template-columns` value for small/mobile viewports (e.g., "1fr"). |
| `rows[].sections` | string[] | Yes | Array of section IDs placed in this row's slots, left-to-right. Each ID must match a `SectionRegistration.id` in the section registry. |

### Constraints

- Each section ID may appear **at most once** across all rows (no duplicate sections per layout).
- Unknown section IDs are skipped with a console warning by the config builder (graceful degradation).
- Unfilled slots in a row are skipped during rendering.
- Overflow sections (more sections than template slots) get auto-appended in additional `1fr` rows.

### Full Example

A "Two Column" layout with 4 sections across 2 rows:

```json
{
  "schemaVersion": 1,
  "rows": [
    {
      "id": "row-1",
      "columns": "1fr 1fr",
      "columnsSmall": "1fr",
      "sections": ["get-started", "quick-summary"]
    },
    {
      "id": "row-2",
      "columns": "1fr 1fr",
      "columnsSmall": "1fr",
      "sections": ["latest-updates", "my-documents"]
    }
  ]
}
```

## Layout Templates

The `sprk_layouttemplateid` column references one of these predefined templates:

| Template ID | Display Name | Grid Layout |
|-------------|-------------|-------------|
| `2-col-equal` | Two Column | 2 rows x 2 cols (`1fr 1fr`) |
| `3-row-mixed` | Three Row | Row 1: `1fr 1fr`, Row 2: `1fr`, Row 3: `1fr 1fr` |
| `sidebar-main` | Sidebar + Main | `1fr 2fr` |
| `single-column` | Single Column | `1fr` per row |
| `3-col-equal` | Three Column | `1fr 1fr 1fr` |
| `hero-grid` | Hero + Grid | Row 1: `1fr`, Row 2: `1fr 1fr 1fr` |

Templates are defined in code (section registry), not stored in Dataverse.

## Security Model

### Owner-Based Access

- The entity uses the **Owner** security model (standard Dataverse ownership).
- Each user owns their own layout records.
- Users can only **Create, Read, Update, Delete** their own `sprk_workspacelayout` records.
- No cross-user access is required or permitted for workspace layouts.

### System Workspaces

- System workspaces (e.g., the default "Home" workspace) are **NOT stored in this entity**.
- System workspaces are defined in code and injected at the BFF API layer.
- System workspaces are read-only -- users cannot edit or delete them.
- Users can "Save As" a system workspace to create their own editable copy (stored in this entity).

### Business Rules

- **Max 10 user workspaces per user**: Enforced by the BFF API on POST (create).
- **Single default per user**: When `sprk_isdefault` is set to `true`, the BFF API clears the previous default for that user.
- **User-created only**: Only records owned by the current user can be modified or deleted via the API.
