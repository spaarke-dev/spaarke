---
name: spe-knowledge-dir-gaps
description: What the repo's knowledge/sharepoint-embedded/ curation does and does not cover — container CRUD yes, upload/versioning/concurrency no
metadata:
  type: reference
---

`knowledge/sharepoint-embedded/` (captured 2026-05-14) is curated around **container and permission CRUD**, not content operations. `SOURCE.md` indexes samples from `microsoft/SharePoint-Embedded-Samples` (container CRUD C#/TS, PowerShell bootstrap, embedded chat) and `docs/` holds five Learn captures (containers, containertypes, overview, semantic-index, knowledge-source).

**Gap confirmed 2026-08-20**: it has nothing on upload size limits, upload sessions, eTag/If-Match concurrency, or versioning. `NOTES.md` line ~407 still carries an open TODO: "Document the chunked upload pattern Spaarke uses for documents >4 MB (Graph upload session approach)" — note that TODO itself encodes the stale 4 MB figure (see [[graph-driveitem-upload-facts]]; the real limit is 250 MB).

**How to apply**: for SPE *content*-plane questions (upload, download, versioning, concurrency, Office coauthoring), go straight to Learn `/sharepoint/dev/embedded/build/manage-files` and the Graph `driveitem-*` reference; the local knowledge dir will not answer them. For container/permission/container-type questions, check the local dir first — it is good there.
