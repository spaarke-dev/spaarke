# MDA Form Notes — sprk_fieldmappingprofile

> **Task**: SRFR-060 · **Executed**: 2026-07-02 · **Env**: spaarkedev1
> Also see: [`wave-6-task-060.log`](./wave-6-task-060.log) for full execution trace.

## FormXml + solution artifacts

| Artifact | Path |
|---|---|
| Unpacked solution folder | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/` |
| Profile Main FormXml | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Entities/sprk_FieldMappingProfile/FormXml/main/{4d66f128-7800-f111-8407-7c1e520aa4df}.xml` |
| Rules SavedQuery (view used by subgrid) | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Entities/sprk_FieldMappingRule/SavedQueries/{825182f1-d19e-4615-9d03-f2953a3a55b0}.xml` |
| Solution zip (deployed) | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/FieldMappingAdminSolution-v1.0.5.zip` |
| Solution manifest | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Other/Solution.xml` (`<Version>1.0.5</Version>`) |
| Deploy script | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/Deploy-FieldMappingAdminSolution.ps1` |
| README | `infrastructure/dataverse/solutions/FieldMappingAdminSolution/README.md` |

## Import log summary

- **Solution**: `FieldMappingAdminSolution` v1.0.5 (unmanaged)
- **Target env**: `https://spaarkedev1.crm.dynamics.com/`
- **Async op ID**: `3ab443cc-5576-f111-ab0e-7ced8ddc4a05`
- **Result**: `completed successfully within 00:00:29`
- **Publish**: `Published All Customizations.` (via `--publish-changes` flag)

## UAT confirmation

Wave 8 SRFR-084 owns the Matter → Event profile end-to-end scenario. Task 060 acceptance is limited to metadata verification (post-import re-export shows all required fields + editable-grid `controlDescription` present). Manual maker walk-through deferred to Wave 8.

**Post-deploy verification path** (for the reviewer):
1. Open MDA app in spaarkedev1 → navigate to `Field Mapping Profile` entity
2. Create new: fill name, pick source recordtype (e.g., Matter), pick target recordtype (e.g., Event), set compatibility mode, save
3. In the "Field Mapping Rules" subgrid: click "+ New Field Mapping Rule" → verify inline-editing works (subgrid is editable, not a modal-only add)
4. Add sourcefield, targetfield, mapping_type, executionorder inline → save
5. Confirm rules persist and show in the 12-column subgrid layout
