# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: 6 complete (SRFR-060, SRFR-061, SRFR-062 all ✅); Wave 7 next (group E — 070, 071, 072 parallel; independent from Wave 8)
**Task**: none active
**Status**: idle
**Started**: —
**Rigor**: —

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | — (SRFR-062 complete) |
| **Step** | — |
| **Status** | idle |
| **Next Action** | Wave 7 group E parallel: SRFR-070 (OOB mapping audit), SRFR-071 (ADR-024 amendment MINIMAL), SRFR-072 (FieldMappingHandler inline ref MINIMAL). Independent — can run anytime. After Wave 7, group F Wave 8 (deploys + UAT). |

## Session Notes / Key Learnings (SRFR-060)

- **Editable subgrid mechanism in current-gen Dataverse**: `MscrmControls.Grid.EditableGrid` (the legacy control name) does NOT exist as a registered `customcontrol` in current environments (verified via MCP catalog query). The correct control for editable-grid UX is **`Microsoft.PowerApps.PowerAppsOneGrid`** with `<EnableEditing type="TwoOptions" static="true">true</EnableEditing>`. Referenced in FormXml via a `<controlDescription forControl="{control-uniqueid-guid}">` block.
- **`forControl` attribute semantics**: `controlDescription forControl` MUST reference the `uniqueid` GUID assigned to the target control's `<control>` tag, NOT the control's `id` string. Discovered by grepping the Matter form pattern in the archive `c:/code_files/spaarke/exports/SC3/`.
- **PAC solution round-trip works well for form authoring**: `add-solution-component` → `export` → `unpack` → edit FormXml → `pack` (bump `<Version>` first) → `import --publish-changes --async`. Four iterations were needed (v1.0.2 through v1.0.5) due to D-13 and D-14 discoveries, but total wall-time was still ~1h.
- **Pre-existing user-defined Main form was a head start**: The `sprk_fieldmappingprofile` entity already had `Field Mapping Profile main form {4d66f128-...}` with the source/target lookups + subgrid bound to `sprk_fieldmappingrule_FieldMappingProfile_n1`. Task 060 only needed to ADD compatibilitymode + defaultvalue, make description visible, expand the subgrid SavedQuery to 12 columns, and add the editable-grid `controlDescription`. Saved substantial authoring time.

## Applicable ADRs (session-level)

- None (metadata-only task; no code changes; STANDARD rigor skipped Step 9.5).

## Files Modified This Session

- `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Entities/sprk_FieldMappingProfile/FormXml/main/{4d66f128-...}.xml` — added compatibilitymode + defaultvalue cells, made description visible, added subgrid `uniqueid`, added `controlDescription` for editable OneGrid
- `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Entities/sprk_FieldMappingRule/SavedQueries/{825182f1-...}.xml` — expanded from 4 → 13 columns (12 rule fields + name), order by executionorder
- `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Other/Solution.xml` — `<Version>` 1.0.1 → 1.0.5
- `infrastructure/dataverse/solutions/FieldMappingAdminSolution/FieldMappingAdminSolution-v1.0.5.zip` — packed solution (deployed)
- `infrastructure/dataverse/solutions/FieldMappingAdminSolution/README.md` — new
- `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Deploy-FieldMappingAdminSolution.ps1` — new
- `projects/set-regarding-and-field-mapping-resolver-r1/notes/wave-6-task-060.log` — new
- `projects/set-regarding-and-field-mapping-resolver-r1/notes/mda-form-notes.md` — new
- `projects/set-regarding-and-field-mapping-resolver-r1/tasks/060-fieldmappingprofile-mda-form.poml` — status → complete
- `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` — 060 🔲 → ✅

## Deploy state

- **spaarkedev1**: FieldMappingAdminSolution v1.0.5 deployed 2026-07-02 (async op `3ab443cc-5576-f111-ab0e-7ced8ddc4a05` success + Published All Customizations)
- **UAT (Wave 8 SRFR-083)**: same zip can be redeployed via `Deploy-FieldMappingAdminSolution.ps1 -Environment <uat-url> -Version 1.0.5` (bump version if needed)

## Next Action

- SRFR-062 (Wave 6 ribbon CustomAction — sequential after 061). Once 062 lands, Wave 6 closes and Wave 7/8 can proceed.
