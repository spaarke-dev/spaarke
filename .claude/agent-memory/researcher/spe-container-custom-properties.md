---
name: spe-container-custom-properties
description: SPE fileStorageContainer customProperties facts verified 2026-10-06 — not settable at create, PATCH sub-resource merge semantics, Read+Write CT perms, delegated writers can modify, container id == drive id, no documented limits
metadata:
  type: reference
---

Verified 2026-10-06 against Learn (Graph v1.0/beta reference + sp-dev-docs SPE pages) plus the repo's own live probes
(`projects/sdap-SPE-admin-app-r2/notes/probe_customprops_shape.py`, 2026-08-28, beta, app-only owning app).
Asked in the context of stamping an ownership marker on containers of a shared container type (customer-provisioning-orchestration-r1).

**Create**: `POST /storage/fileStorage/containers` body table lists ONLY displayName, description, containerTypeId, settings
(v1.0 and beta identical). customProperties not documented at create. Repo probe proved `PATCH /containers/{id}` with a
`{customProperties:{...}}` wrapper returns `400 Unsupported request body property: customProperties` — so expect the same at
create (inferred, not probed). Two-step: create -> (activate) -> `PATCH /containers/{id}/customProperties` with the map as BODY ROOT.

**Write semantics** (v1.0 GA; doc + probe agree): PATCH merges — only named properties change; unknown name = created;
`null` deletes. Add returns 201, update 200. Attributes: value (required string), isSearchable, isPatternToken (new, docs 2026-09-29).
Update doc: "The application calling this API must have read and write permissions to the fileStorageContainer for the respective container type."
No conditional write / ETag / immutability flag exists — any Write-capable caller can overwrite.

**Read**: `GET /containers/{id}/customProperties` or `/customProperties/{name}` (v1.0). Plain GET container example omits
customProperties; list-containers says $expand unsupported for customProperties; repo code reads via `?$select=id,customProperties`
(works). Treat customProperties as NOT in the default GET payload — always $select or use the sub-resource.

**Who can modify**: CT app perms Read+Write (Full includes them). Delegated = intersection of app CT perms and member role;
resource doc: writer/manager/owner "can read and modify fileStorageContainer metadata"; reader = read only.
So: any app with Write/Full on the CT (app-only, every container of the type) and any Writer+ member via such an app.

**Ids**: SPE manage-files page: "For SharePoint Embedded, the drive ID is the container ID that starts with b!."
`GET /drives/{driveId}` is a documented alias of `GET /containers/{id}/drive`.

**Limits**: none documented for custom property count / value length / naming. RU cost: single-item GET = 1 RU;
per-container 3,000 RU/min, per app per tenant 12,000 RU/min, per user 600 (limits-calling-patterns, updated 2026-09-17).

Related: [[spe-knowledge-dir-gaps]] [[graph-driveitem-upload-facts]]
