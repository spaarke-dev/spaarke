---
name: spe-dedup-content-identity-2026-07
description: SPE / Microsoft Graph platform capabilities for content identity, de-duplication, and version-of-truth — hashes facet, versions, delta, custom columns, eTag/cTag. For a file-duplication-detection feature on SharePoint Embedded.
metadata:
  type: project
---

# SPE content identity / dedup platform research (2026-07-14)

**Question**: What native SPE/Graph capability supports detecting "this file already exists / which copy is authoritative" — content hashes, versioning, container scoping, custom columns, delta, native dedup, eTag/cTag.

**Findings (synthesis)**:

1. **Hashes (Q1) — the load-bearing finding.** The v1.0 `hashes` resource doc (updated 2025) now marks **`sha256Hash` as "This property isn't supported. Don't use."** `quickXorHash` is the ONLY hash guaranteed for OneDrive work/school (= SharePoint = SPE family). `crc32Hash`/`sha1Hash` are "if available" = consumer OneDrive only. **Design MUST use `quickXorHash`, not sha256.** quickXorHash is a proprietary 160-bit base64 hash; algorithm is public (learn.microsoft.com/onedrive/developer/code-snippets/quickxorhash) so we can compute it client/server-side to compare. Requested via `GET /drives/{id}/items/{id}?$select=file` (hashes is inside the `file` facet). SPE-specific population is NOT explicitly documented — HIGH confidence it returns quickXorHash (SharePoint substrate) but recommend a 1-hour spike to confirm it is present immediately post-upload (SharePoint computes it server-side; may lag async for very large uploads).

2. **Versions (Q2).** `/versions` IS supported on SPE — the list-versions doc explicitly calls out SPE needs `FileStorageContainer.Selected` + container-type permissions. `driveItemVersion` carries `id` (e.g. "3.0"), `size`, `lastModifiedBy`, `lastModifiedDateTime` — **NO per-version hash**. Restore via `driveItemVersion: restore`. Limit 500 versions/file (default). Versioning is inherent to the SharePoint substrate. Versions solve "history of one item," NOT "which of two separate items is truth."

3. **Container scoping + query by metadata (Q3/Q4).** Custom columns on SPE items are GA in v1.0 (Jan 2026: fileStorageContainer list/create/update/delete column APIs). Query items by custom column: `GET /drives/{containerId}/items?$expand=listitem($expand=fields)&$filter=startswith(listitem/fields/{Col},'x')` (eq/startswith/orderby supported). Microsoft Search API also works over containers. So we CAN stamp a canonical-doc-id / content-hash column and query it per container efficiently. Dedup scoped per-container is the natural boundary (Spaarke = tiered BU + secure-matter containers). Cross-container dedup requires our own index (Dataverse or AI Search).

4. **Delta (Q5).** `GET /drives/{containerId}/root/delta` works on SPE (containers are drives). Returns nextLink pages then deltaLink; supports `?token=latest` and timestamp tokens (OneDrive-Business/SharePoint only). Ideal for backfill + ongoing hash-capture job. Gotcha: **delta OMITS `ctag` on create/modify for OneDrive-for-Business drives** — do NOT rely on delta for content-change detection; use it to enumerate then GET the item for hash/ctag. 410 Gone/resyncRequired handling required.

5. **Native dedup (Q6).** No logical dedup. Uploading identical bytes twice = two driveItems with distinct IDs. SharePoint does BLOB-level single-instance/shredded storage at the backend but it is invisible and never collapses logical items. Dedup is entirely our responsibility.

6. **eTag/cTag (Q7).** `eTag` changes on content OR metadata change; `cTag` changes ONLY on content change → cTag is the right content-change token (but omitted by delta, see #4). Item `id` is stable across renames/moves/content edits (immutable identity). Also `sharepointIds`. Use `If-Match: {etag}` on writes for optimistic concurrency (412 on race).

**Recommended design shape**: quickXorHash as content-identity key + a stamped `sprk_canonicalhash` (and/or canonical-doc-id) container column, queryable via listitem/fields $filter; delta job to backfill/maintain; Dataverse `sprk_document` as the cross-container authority index (SPE per-container query can't span containers). Flag NOTES.md TODO: it says native driveItem metadata is "read-only" — accurate for hashes/system fields, but custom COLUMNS are writable and queryable (GA Jan 2026), which is the dedup lever.

**Sources**:
- learn.microsoft.com/graph/api/resources/hashes (sha256 unsupported; quickXor guaranteed) — MOST authoritative for Q1
- learn.microsoft.com/graph/api/resources/file ; /driveitem-get ($select=file)
- learn.microsoft.com/graph/api/driveitem-list-versions + resources/driveitemversion (SPE note, no per-version hash)
- learn.microsoft.com/graph/api/driveitem-delta (ctag omission table, timestamp tokens, 410 handling)
- learn.microsoft.com/sharepoint/dev/embedded/whats-new (columns GA Jan 2026; version migration; restore)
- learn.microsoft.com/sharepoint/dev/embedded/development/limits-calling (500 versions/file, 30M files/container, throttling)
- learn.microsoft.com/sharepoint/dev/embedded/development/content-experiences/search-content ; tutorials/metadata (custom column $filter)
- knowledge/sharepoint-embedded/NOTES.md (Spaarke container model, ContainerColumnEndpoints.cs)

**Open questions**:
- Is quickXorHash present immediately post-upload for SPE, or async for large files? (spike)
- Does the substrate compute quickXorHash for ALL file types or only Office/known types? (spike)
- Any size ceiling above which hash is not returned? (not documented; spike alongside chunked-upload path)
