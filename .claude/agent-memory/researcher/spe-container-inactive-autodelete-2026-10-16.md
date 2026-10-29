---
name: spe-container-inactive-autodelete-2026-10-16
description: SPE new containers are created inactive and auto-deleted if not activated within 24h; activate action OR any modifying write (customProperties, permission add, upload, property update) activates; Spaarke create-path audit
metadata:
  type: reference
---

## 2026-10-16: Are inactive SPE containers auto-deleted? What activates one?

**Question**: Does SPE auto-delete containers left `inactive`, after how long, and what activates one; are Spaarke's create paths exposed?

**Findings**: YES. Graph v1.0 `fileStorageContainer` resource (updated_at 2026-08-12): "Containers are created as inactive and require activation. Inactive containers are subjected to automatic deletion in 24 hours." Activate page (updated_at 2025-07-23): "A container must be activated within 24 hours after creation, or it will be automatically deleted ... can be activated by calling the activate API, or any other API that updates or modifies the fileStorageContainer or its content. For example, uploading a file ..., adding permission ..., creating a custom property ..., or updating the properties ... can activate it." `status` (inactive|active) is READ-ONLY; `lockState` (unlocked|lockedReadOnly) is separate and unrelated to activation. Create response example shows `"status": "inactive"`.

Spaarke code (2026-10-16, uac-r2 worktree): only the control-plane H8 path calls explicit `/activate` (GraphContainerProvisioner.cs:152 create, :234 Activate.PostAsync). BFF paths (ContainerOperations.CreateContainerAsync :49/:91 -> BindNewContainerAsync PATCH customProperties :141 -> MarkOwnedAsync PATCH customProperties; SpeAdminGraphService.CreateContainerAsync :1383/:1416 -> bind :1432 -> MarkOwned :1795) and scripts (New-BusinessUnitContainer.ps1, Provision-Customer.ps1) never call /activate but PATCH /customProperties within seconds of create, which the docs list as an activating write. So implicit activation; risk is low but unmeasured (no repo note records status=active after a stamp-only flow). Side finding: SpeAdminGraphService.ActivateContainerAsync (:1599) PATCHes read-only `status`="active" instead of POST /activate.

**Sources**:
- https://learn.microsoft.com/en-us/graph/api/resources/filestoragecontainer?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-activate?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post?view=graph-rest-1.0
- knowledge/sharepoint-embedded/NOTES.md:144,469 (says "grant owner permission" activates — narrower than docs)
- projects/sdap-SPE-admin-app-r2/notes/task-050-findings.md §5/§9 (live: create -> inactive, /activate -> 204 -> active)

**Open questions**: "can activate" wording is soft; live probe needed: create -> PATCH customProperties only -> GET $select=status.
