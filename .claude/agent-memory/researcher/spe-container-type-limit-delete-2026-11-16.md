---
name: spe-container-type-limit-delete-2026-11-16
description: SPE container types - 25/tenant, standard types not deletable (trial only), create delegated-only; Learn MCP verified working 2026-11-16
metadata:
  type: reference
---

**Question**: Is "types permanent, capped at 25 per tenant, cannot be deleted; creation delegated-only" (root CLAUDE.md) still accurate per Learn? (Also a Learn MCP tool verification run, task 040 reuse-governance-tightening-r1.)

**Findings**: All three parts accurate on 2026-11-16. Limit: "up to 25 container types in total (one may be a free trial; rest standard)". Delete: only trial types deletable (Graph delete + Remove-SPOContainerType); standard deletion "isn't yet supported"; all containers incl. recycle bin must be removed first. Create: Graph POST /storage/fileStorage/containerTypes - delegated FileStorageContainerType.Manage.All, Application "Not supported", needs SPE Admin or Global Admin role. Nuance: "permanent" is shorthand for "standard types cannot be deleted"; the Graph create page links limits to sharepoint/dev/embedded/development/limits-calling (not fetched).
Learn MCP search + fetch worked with no auth prompt.

**Sources**:
- https://learn.microsoft.com/sharepoint/dev/embedded/build/create-container-type
- https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes
- https://learn.microsoft.com/powershell/module/microsoft.online.sharepoint.powershell/remove-spocontainertype
- knowledge/sharepoint-embedded/docs/learn-containertypes.md; docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md

**Open questions**: limits-calling page may hold a separate numeric limit statement; not checked.
