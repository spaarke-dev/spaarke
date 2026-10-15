# Current Task — pcf-orphan-cleanup-r1

> Last updated: 2026-10-15 (project closed)

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | None. **Project closed 2026-10-15** |
| **Status** | 001–003 ✅; 004, 005, 006, 007 cancelled by the owner (reasons in `tasks/TASK-INDEX.md`) |
| **Next Action** | None. If a regression is traced to a deletion, restore it with the command in `notes/dataverse-cleanup-log.md` |

## Outcome

- **Source:** UQC, DrillThroughWorkspace and SpeDocumentViewer folders deleted (task 002, PR #412). SpeDocumentViewer was later restored by `spaarkeai-word-add-in-r1`, because it is in use. FieldMappingAdmin bundle removed from `FieldMappingAdminSolution` (2026-10-15).
- **spaarkedev1:** deleted 6 PCFs (EventCalendarFilter, AnalysisBuilder, DueDatesWidget, LegalWorkspace, AnalysisWorkspace, FieldMappingAdmin) and 3 canvas pages (`sprk_analysisbuilder_40af8`, `sprk_visualizationdrillthroughworkspace_e48a5`, `sprk_eventspage_786f4`). `sprk_` customcontrols went from 31 to 25. Owner smoke tests found no regressions.
- **Held (out of scope; `pcf-legacy-retirement-r1` / #1468):** EventAutoAssociate, EventFormController, RegardingLink. SpeDocumentViewer is live and in use.
- **spaarkedev2** does not exist. Other environments receive changes via the standard release procedure.
- **Pre-existing defects found and filed:** #1895, #1896, #1950, #1951, #1952, #1956. Created the missing Matters system workspace (#1955).
- **Backups:** `backups-2026-06-22/` and `backups-2026-10-14/`.
