---
name: dataverse-cascade-delete-2026-10-18
description: Dataverse relationship cascade-delete semantics, multi-parent rule, async cascade thresholds, restore/recycle bin, audit, system-table defaults; what is NOT documented (privilege check on cascaded children)
metadata:
  type: reference
---

## 2026-10-18: Dataverse cascade delete behaviour
**Question**: Delete cascade options/defaults, multi-parental restriction, privilege checks on cascaded children, async/large cascades, audit, notes/activities/doc-location defaults, restore.
**Findings**: Delete options = Cascade All / Remove Link / Restrict only (Active/UserOwned/NoCascade not valid for Delete; "Cascade None" is internal-only). "Parental" Delete=Cascade All; Referential = Remove Link; maker rule "No new relationship can have any action set to Cascade All/Active/User-Owned if the related table already exists as a related table in another relationship that has [any such action]" - applies to ANY action (Assign/Share/etc.), not just Delete, i.e. to Configurable Cascading too. NO Learn doc states cascaded children are deleted with system privileges or that the caller's Delete priv is checked per child; only analogue is Assign page ("owner privilege for the child records isn't validated") - treat as undocumented. Async cascade thresholds: Delete 5,000, Assign/Merge 1,000 records (admin page, system job "Cascade Operations"); sync mode = one rolled-back transaction. Restore: admin 1-90 days (GA wording) vs dev page 30 days + "prerelease"; cascade-deleted children restorable only parent-first. Audit: Delete action=3 is audited; no doc on whether cascaded children get own Delete audit rows; DeleteRecordChangeHistory exists. System tables (account) -> Annotation/Task/Appointment/SharePointDocumentLocation/connection are Delete=Cascade; account_parent_account self-ref is RemoveLink. Custom table HasNotes/HasActivities relationship defaults NOT documented (verify via metadata).
**Sources**:
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-edit-entity-relationships
- https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-edit-1n-relationships-solution-explorer
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/configure-entity-relationship-cascading-behavior
- https://learn.microsoft.com/en-us/power-platform/admin/async-cascading
- https://learn.microsoft.com/en-us/power-platform/admin/restore-deleted-table-records
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/restore-deleted-records
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/annotation
**Open questions**: privilege model of cascaded deletes (needs live probe/MS support); 2-minute limit not documented for cascade; max cascade depth undocumented; default Type of Behavior for new custom 1:N undocumented on Learn.
