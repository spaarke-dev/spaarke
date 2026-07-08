# FieldMappingAdminSolution — Field Mapping Profile MDA Form

> **Version**: 1.0.5
> **Created**: 2026-07-02 (SRFR-060)
> **Purpose**: Native MDA main form for `sprk_fieldmappingprofile` with editable subgrid of `sprk_fieldmappingrules` — the MVP admin authoring surface per spec FR-B2-01.

---

## What this solution contains

- **Entity**: `sprk_fieldmappingprofile` (metadata + Main form + Information form + views + relationships)
- **Entity**: `sprk_fieldmappingrule` (metadata + views + N:1 relationship to profile)
- **Main form**: `Field Mapping Profile main form` (`{4d66f128-7800-f111-8407-7c1e520aa4df}`) — the canonical authoring form
- **SavedQuery**: `Active Field Mapping Rules` — the 12-column view used by the profile form's rules subgrid
- **PCF Control**: `Spaarke.Controls.FieldMappingAdmin` (unrelated, pre-existing; retained to avoid regression)

## Field Mapping Profile Main Form structure

| Region | Field | Notes |
|---|---|---|
| Body row 1 | `sprk_name` (required), `sprk_sourcerecordtype` (lookup), `sprk_targetrecordtype` (lookup) | 2-1-1 column layout |
| Body row 2 | `sprk_compatibilitymode` (Choice), `sprk_defaultvalue` (text) | Added by SRFR-060 |
| Body row 3 | `sprk_description` (multi-line) | Now visible (previously hidden pre-SRFR-060) |
| Rules section | Editable subgrid `Subgrid_fieldmappingrules` | Uses `Microsoft.PowerApps.PowerAppsOneGrid` with `EnableEditing=true` |
| Header | `statecode`, `statuscode`, `ownerid` | Standard |

**Relationship bound to subgrid**: `sprk_fieldmappingrule_FieldMappingProfile_n1` (auto-detected from unpacked FormXml).

**Editable subgrid mechanism**: `controlDescription forControl="{a1b2c3d4-e5f6-7890-abcd-ef1234567890}"` (the `uniqueid` on the subgrid `<control>` tag) → `customControl name="Microsoft.PowerApps.PowerAppsOneGrid"` with `<EnableEditing type="TwoOptions" static="true">true</EnableEditing>`.

## Subgrid columns (12 fields per spec §A.2.2)

Rendered in this order (execution order first for visual priority):

1. `sprk_executionorder`
2. `sprk_sourcefield`
3. `sprk_sourcefieldtype`
4. `sprk_targetfield`
5. `sprk_targetfieldtype`
6. `sprk_mapping_type` (underscore convention per SRFR-002 W1)
7. `sprk_mappingdirection`
8. `sprk_syncmode` (per-rule per D-3)
9. `sprk_defaultvalue`
10. `sprk_compatibilitymode`
11. `sprk_iscascadingsource`
12. `sprk_isrequired`
13. `sprk_name` (trailing for context)

## Deploy

### To spaarkedev1 (already deployed 2026-07-02 as v1.0.5)

```bash
pac auth select --index <spaarkedev1-index>
pac solution import --path FieldMappingAdminSolution-v1.0.5.zip --publish-changes --async
```

### To UAT / Prod (task 083 in Wave 8)

Bump version in `Other/Solution.xml` (`<Version>` element), then:

```bash
pac solution pack --zipfile FieldMappingAdminSolution-v1.0.X.zip --folder . --packagetype Unmanaged
pac auth select --index <target-env-index>
pac solution import --path FieldMappingAdminSolution-v1.0.X.zip --publish-changes --async
```

## Acceptance checklist (per SRFR-060 POML)

- [x] Main form for `sprk_fieldmappingprofile` deployed to spaarkedev1 (Order=1, FallbackForm=true → default)
- [x] Form carries identity + real columns: `sprk_name`, `sprk_sourcerecordtype`, `sprk_targetrecordtype`, `sprk_compatibilitymode`, `sprk_defaultvalue`, `sprk_description`, statecode
- [x] Editable subgrid of `sprk_fieldmappingrules` (via `PowerAppsOneGrid` + `EnableEditing=true` — the modern-app editable-grid control that replaces the legacy `MscrmControls.Grid.EditableGrid`)
- [x] SavedQuery `Active Field Mapping Rules` updated to 12 columns
- [x] Solution zip + FormXml artifacts stored in repo (this folder)
- [x] Import log confirms zero errors (verified via `pac solution import --async` success)
- [ ] UAT smoke — deferred to Wave 8 SRFR-084 (Matter → Event profile end-to-end scenario)

## Notes for reviewer

- Two Main forms exist on the entity (`Information` OOB + `Field Mapping Profile main form` user-defined). The user-defined form has `DisplayConditions Order="1" FallbackForm="true"` which makes it the default primary form for all roles.
- The Web API `systemform` update path was NOT used; instead the canonical `pac solution` round-trip was used per the `dataverse-deploy` skill pattern.
- The `Microsoft.PowerApps.PowerAppsOneGrid` control is the modern equivalent of the legacy `MscrmControls.Grid.EditableGrid` (which does NOT exist in current-gen environments — verified by MCP query on `customcontrol` catalog). It provides the editable-cell UX with the `EnableEditing=true` parameter.
