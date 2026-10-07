---
name: word-addin-save-collision-path-2026-09-08
description: Word add-in /api/office/save uses ConflictBehavior.Replace (silent same-name overwrite) vs OBO path's Fail/409; content dedup is gate-after-write. Repo analysis, 2026-09-08.
metadata:
  type: project
---

# Word add-in save collision path (2026-09-08)

**Question**: Does the Word add-in save path inherit the shipped OBO fail-on-collision behaviour, and what happens on a
same-name save?

**Findings**: The add-in posts JSON/base64 to `POST /api/office/save`; `OfficeService` calls `OfficeStorageUploader`,
which calls the no-policy `SpeFileStore.UploadSmallAsync` overload. That overload defaults to `ConflictBehavior.Replace`
and `UploadSessionManager` sends `@microsoft.graph.conflictBehavior=replace`, so a same-name Office save REPLACES the
existing SPE item before Dataverse/content-dedup reconciliation (no large-upload/session route in the add-in). OBO
instead defaults to `ConflictBehavior.Fail`, maps Graph 409 to `SpaarkeStorageException`, and `@spaarke/sdap-client`
exposes `UploadNameConflictError` — the Office client doesn't use that package and maps non-OK to generic errors.
Office content dedup is gate-after-write on the immutable suppress path, so it can't prevent the overwrite and may
delete the newly overwritten item on a hash hit.

**Sources**: projects/spaarkeai-word-add-in-r1/tasks/005-spike-4-addin-collision-path.poml;
src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts; Services/Office/OfficeService.cs, OfficeStorageUploader.cs,
OfficeDocumentPersistence.cs; Infrastructure/Graph/SpeFileStore.cs, UploadSessionManager.cs; Api/OBOEndpoints.cs;
Services/Documents/ContentDedupDetector.cs; src/client/shared/Spaarke.SdapClient/src/operations/UploadOperation.ts;
projects/unified-access-control-r2/tasks/094-upload-collision-preflight-probe.poml.

**Open questions**: Whether SPE exposes the replaced bytes via normal version history in the target tenant (verify
operationally). Collision fix + editable dedup policy need an owner decision; don't duplicate task 094's OBO pre-flight.
Related: [[spe-version-comment-2026-09-12]].
