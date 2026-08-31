---
name: graph-driveitem-upload-facts
description: Microsoft Graph driveItem upload facts verified 2026-08-20 — 250 MB simple-PUT limit (was 4 MB pre-Oct-2023), If-Match support matrix per endpoint, upload-session replace semantics, SPE-confirmed guidance
metadata:
  type: reference
---

Verified 2026-08-20 against Microsoft Learn + the docs source repos (`microsoftgraph/microsoft-graph-docs-contrib`, `SharePoint/sp-dev-docs`).

**Simple upload size limit**: `PUT /drives/{d}/items/{id}/content` (and the `root:/path:/content` create shape) supports **250 MB**, not 4 MB. Doc history in `microsoft-graph-docs-contrib` for `api-reference/v1.0/api/driveitem-put-content.md`: 4 MB → 25 MB (commit `091eb5e9`, 2023-10-16) → 256 MB (`a3ca9e86`, same day) → **250 MB** (`ebb59b6c`, 2023-10-25), stable since. Any Spaarke code branching at a 4 MB threshold is stale by ~3 years. Limit is stated per-method with no delegated/app-only or SPE/OneDrive variation.

**If-Match support matrix** (searched all v1.0 `driveitem-*.md` API docs): exactly four document an `if-match` request header — `driveitem-delete`, `driveitem-update` (PATCH), `driveitem-move`, and **`driveitem-createuploadsession`**. `driveitem-put-content` does **NOT** document it (its headers table is only Authorization + Content-Type, and never had if-match — the pre-redirect OneDrive doc at `OneDrive/onedrive-api-docs@e869407` also lacked it). So the resumable path has a *documented* precondition while the simple-PUT path Spaarke currently relies on does not. Chunk PUTs to `uploadUrl` take no auth and no precondition (doc explicitly says don't send Authorization).

**conflictBehavior is name-collision only**, never a content/version comparison. `driveItem` instance attributes: values `fail | replace | rename`, "conflict resolution behavior for actions that create a new item", "The default for PUT is *replace*"; the createUploadSession body sample says "fail (default)" — the two docs disagree on default, so always set it explicitly. `driveItemUploadableProperties` has **no eTag/cTag property**. For replace-in-place use `POST /drives/{d}/items/{itemId}/createUploadSession` (doc's "Update existing file" shape) with `replace`.

**SPE specifics**: `sp-dev-docs/docs/embedded/build/manage-files.md` (ms.date 2026-07-13) states the 250 MB / upload-session split for containers explicitly and says "Office files stored in SharePoint Embedded have versioning enabled automatically for Word, Excel, and PowerPoint." SPE limits page (`embedded/plan/limits-calling-patterns`, updated 2026-08-12): max file size 250 GB, 500 versions/file, 3,000 resource units/min per container (upload = 2 units). No SPE deviation on preconditions is documented either way.

Related: [[spe-knowledge-dir-gaps]]
