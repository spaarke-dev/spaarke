---
source: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/containers
fetched: 2026-05-14
refreshed: 2026-08-26
refreshed-by: sdap-SPE-admin-app-r2 (task 061), per knowledge/REFRESH-PROCEDURE.md
refresh-scope: >
  Added container archival (GA Feb 2026, documentation-only — not yet implemented or live-verified by
  any Spaarke project as of this refresh), the per-container item recycle bin (GA v1.0, distinct from
  the deleted-*containers* collection this corpus already covered), the container URL field (Graph's own
  documented shape does not hold up under test — record below), and what R2 established empirically
  about storage consumption reporting. Original 2026-05-14 app-architecture snapshot retained below
  unchanged — it was not found stale.
note: Original URL 404'd; captured equivalent app-architecture page that documents container, container type, and owning-application concepts, plus the containertypes creation page (see learn-containertypes.md).
fallback_url: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/app-architecture
---

# App Architecture | Microsoft Learn

> ## 🔄 2026-08-26 refresh summary
>
> This page (an app-architecture concept page, substituted in 2026-05-14 for a 404'd containers/ URL)
> covers container *concepts*. It did not previously cover container *lifecycle features* — archival,
> the item recycle bin, or the URL field — which is what `sdap-SPE-admin-app-r2` needed and is added
> below. Jump to:
>
> - [Container archival — GA Feb 2026](#container-archival--ga-february-2026-documentation-only)
> - [Per-container item recycle bin — GA v1.0](#per-container-item-recycle-bin--ga-v10)
> - [The container URL field — VERIFIED, and it is not what the docs suggest](#-the-container-url-field--verified-2026-08-24-and-graphs-documented-shape-does-not-hold-up)
> - [Storage consumption reporting — VERIFIED partitioned by operation](#-storage-consumption-reporting--verified-2026-08-23-through-2026-08-24)

All files and documents in SharePoint Embedded are stored in containers, with all containers and container content created and stored within a Microsoft 365 Tenant. All containers and container content are created, managed, and interacted via the SharePoint Embedded application using Microsoft Graph.

## SharePoint Embedded application

A Microsoft Entra ID application registration. As an owning or guest application to a container type, it has access to containers of that container type.

## Owning tenant and consuming tenant

SharePoint Embedded introduces the concepts of owning tenant and consuming tenant. Owning tenant is a Microsoft Entra ID tenant where a container type is created. This is often also the tenant where your SharePoint Embedded application is registered. Consuming tenant is a Microsoft Entra ID tenant where a container type is used. Only a consuming tenant may have containers of such container type. All container and content created via the application is stored within the consuming tenant's Microsoft 365 tenant boundary.

The same Microsoft Entra ID tenant can be both owning and consuming tenant of a given container type in the SharePoint Embedded ecosystem.

## Container, container type, and owning application

A container is the basic storage unit in SharePoint Embedded. Also, a container defines a security and compliance boundary.

A container type is a SharePoint Embedded resource that defines the relationship, access privileges, and billing accountability between an application and a set of containers. Also, the container type defines behaviors on the set of containers. Learn more about [container types](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/containertypes).

The container type is represented on each container as an immutable property and is used across the entire SharePoint Embedded ecosystem. Each container type is strongly coupled with one SharePoint Embedded application, which is referred to as the owning application. The owning application developer (the owning tenant) is responsible for creating and managing their container types. SharePoint Embedded mandates a 1:1 relationship between owning application and container type.

## Access Model

An application's access to containers and container content is determined by a set of permissions configured between the application and the container type it attempts to access. This set of permission is determined at container Type creation time for owning application. The SharePoint Embedded ecosystem allows applications to access containers of container types it doesn't own.

When multiple applications are deployed in a tenancy (for example, two apps developed by ISVs and an LOB app), each application can access only the stack of containers of the container type they own. When two apps share access to the same container type, both apps can access the same stack of containers.

### Example

Contoso is an ISV and built a human resource management application on SharePoint Embedded. The application is registered and deployed in Fabrikam, an auditing firm. Fabrikam also developed an LOB auditing application on SharePoint Embedded that is used internally.

In this scenario, both the human resource management application developed by Contoso and the auditing application developed by Fabrikam have their own container type. Contoso is the owning tenant of the human resource management application; and the application is the owning app for its container Type. Likewise, Fabrikam is the owning tenant the auditing application; and the application is the owning app for its container type. In addition, Fabrikam is the consuming tenant for both applications.

---

## Container archival — GA February 2026 (documentation-only)

**Source**: [MC1215074 — SharePoint Embedded container archival](https://mc.merill.net/message/MC1215074);
[What's new in SharePoint Embedded](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new);
`sdap-SPE-admin-app-r2` `notes/spe-platform-research-2026-08-20.md` §5.1, `design.md` §4.3.

> ⚠️ **Not empirically verified by any Spaarke project as of this refresh (2026-08-26).** No container has
> been archived or restored against a live tenant, and no code implements this. This section is a
> documentation summary only — treat it as a starting point for implementation, not as confirmed
> behavior.

Archival reduces storage cost by **up to 75%** and improves Copilot result relevance by de-prioritizing
inactive content. Admins opt in **per container type** via PowerShell
(`Set-SPOContainerType -IsArchiveEnabled`) — this is a container-*type*-level toggle, not a
per-container one. Once enabled, individual containers are archived and restored via Graph APIs and
SharePoint admin center options (specific endpoint names not yet captured in this corpus; verify against
current Learn docs before implementing).

**Why this is flagged as the highest-value new capability for a legal-operations platform** (R2's
framing, `design.md` §4.3): containers provisioned per project/matter go cold on a predictable lifecycle
(matter closes → container becomes inactive but is rarely deleted for retention reasons). This is a
direct, recurring, and currently un-levered cost line for any tenant using a container-per-matter model.
`sdap-SPE-admin-app-r2` scoped this as FR-E01 (task 050, not yet started as of this refresh) — check
`projects/sdap-SPE-admin-app-r2/tasks/TASK-INDEX.md` for current status before assuming it shipped.

## Per-container item recycle bin — GA v1.0

**Source**: [Restore recycleBinItem — Graph
v1.0](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-restore-recyclebinitem?view=graph-rest-1.0);
`sdap-SPE-admin-app-r2` `notes/spe-platform-research-2026-08-20.md` §5.4.

`GET /storage/fileStorage/containers/{containerId}/recycleBin/items` — with restore (`207 Multi-Status`
for partial success across multiple items) and permanent delete.

**🔴 This is a different Graph resource from `/storage/fileStorage/deletedContainers`, which this corpus
already documents elsewhere and which the SPE Admin app's "Recycle Bin" screen currently implements.**
Deleted-*containers* and deleted-*items-within-a-container* are two distinct features that happen to
share the word "recycle bin" in casual conversation:

| Feature | Graph resource | What it recovers |
|---|---|---|
| Deleted containers | `/storage/fileStorage/deletedContainers` | An entire soft-deleted **container** |
| Per-container item recycle bin | `/storage/fileStorage/containers/{id}/recycleBin/items` | An individual **file/item** deleted from within a still-live container |

`sdap-SPE-admin-app-r2` spec FR-E03 identifies the item recycle bin as "the likelier admin intent behind
a screen called 'Recycle Bin'" — an admin who deletes a file expects to find it here, not by restoring
the whole container. As of this refresh the item recycle bin is **not yet implemented** (task 052,
`🔲` in `TASK-INDEX.md`); the existing deleted-containers screen (task 022, **complete and
live-verified 2026-08-24** — see `notes/task-022-findings.md`) is retained alongside it once built, not
replaced.

**Handling `207 Multi-Status` matters.** A bulk restore/delete against multiple items can partially
succeed — collapsing that to a single pass/fail result hides which specific items failed. Report
per-item outcomes.

## 🔴 The container URL field — VERIFIED 2026-08-24, and Graph's documented shape does not hold up

**Source**: `sdap-SPE-admin-app-r2` task 028, `notes/task-028-findings.md`. Measured live against
Spaarke Dev, 2026-08-24, app-only as the owning app. Read via Graph's own CSDL
(`https://graph.microsoft.com/{v1.0,beta}/$metadata`, no token required) plus live calls.

**There is no URL property directly on `fileStorageContainer`, on either API version.**

| Version | `fileStorageContainer` properties |
|---|---|
| **v1.0** | `assignedSensitivityLabel`, `containerTypeId`, `createdDateTime`, `customProperties`, `description`, `displayName`, `lockState`, `settings`, `status`, `viewpoint` |
| **beta** | the above **+** `archivalDetails`, `dataLocationCode`, `externalGroupId`, `informationBarrier`, `owners`, `ownershipType`, `storageUsedInBytes` |

`?$select=id,webUrl` fails honestly: `400 BadRequest — Could not find a property named 'webUrl' on type
'microsoft.graph.fileStorageContainer'`. The URL lives one level down, on the `drive` navigation
property (`drive` derives from `baseItem`, which carries `webUrl` on both API versions).

**🔴 On the containers COLLECTION (LIST), `$expand=drive` is accepted, returns `200`, and silently omits
the data anyway** — verified across six request variants, all `200`, none returning a populated `drive`:

```
GET .../containers?$select=id,displayName&$expand=drive($select=webUrl)
→ 200 · @odata.context echoes "…containers(id,displayName,drive(webUrl))" · no `drive` on any row
```

Same result on both v1.0 and beta. **Graph's own `@odata.context` header asserts the field is included,
and then omits it** — this is not a client-side bug to debug against; it is the platform's behavior on
the collection endpoint specifically.

**GET-single (one container by ID) DOES return it correctly** — `GET
/containers/{id}?$expand=drive($select=webUrl)` on either version returns `200` with `drive.webUrl`
populated. So: **if you need a container's URL, resolve it per-container via GET-single. Do not attempt
to bulk-populate it via a LIST `$expand` — Graph will appear to accept the request and give you nothing.**

**Do not assemble a URL from the container ID.** The container ID (`b!...`) base64url-decodes to three
GUIDs, one of which is the SharePoint site GUID that does appear verbatim in the real URL — so a URL
*could* be synthesized without a Graph call. **Don't.** The tenant hostname
(`{tenant}.sharepoint.com`) is not encoded in the ID at all, so any assembled URL would hard-code a guess
that fails silently in a multi-tenant deployment.

## ✅ Storage consumption reporting — VERIFIED 2026-08-23 through 2026-08-24

**Source**: `sdap-SPE-admin-app-r2` tasks 020 and 024, `notes/beta-vs-v1-surface-verification.md`,
`notes/storage-consumption-spike.md`. Live-verified against Spaarke Dev.

`storageUsedInBytes` on a container instance (**consumption**, not the `maxStoragePerContainerInBytes`
**ceiling** documented in `learn-containertypes.md`) is real, live data — but its availability is
**partitioned by operation, not by container or by API version alone**:

| Surface | Reports `storageUsedInBytes`? |
|---|---|
| `GET /beta/…/containers` (**LIST**) | ✅ **yes** |
| `GET /beta/…/containers/{id}` (**GET-single**) | ❌ **no** — omitted even on beta, even with an explicit `$select` |
| `GET /v1.0/…/containers` (either LIST or GET) | ❌ **not in the v1.0 schema at all** — `$select=storageUsedInBytes` returns `400`, not merely an omitted-by-default value |

So a per-container consumption figure can only be sourced from a **beta LIST** call. This is a genuine
reason to keep a client on `/beta` for this one property even after other surfaces migrate to v1.0 —
migrating this specific call to v1.0 does not modernize it, it **deletes the feature**.

**Live-verified figures** (2026-08-24, app-only token, Spaarke Dev, 5 containers, all reporting):

```
GET /beta/storage/fileStorage/containers?$filter=containerTypeId eq {id}
    &$select=id,displayName,createdDateTime,storageUsedInBytes
→ 200, 5 of 5 containers reporting, total 902,616,643 bytes (≈ 860.8 MB)
```

**The property is untyped** (present only in the `beta` OData schema, so Kiota-based SDKs surface it
through an untyped `AdditionalData` bag, not a typed member) — its runtime CLR type varies (`long`,
`int`, `double`, `decimal`, `string`, `JsonElement` have all been observed or are plausible depending on
magnitude). A byte count crosses the int32/int64 boundary in realistic use (2,147,483,647 bytes ≈ 2 GB;
one Spaarke Dev container was already 40% of the way there at time of measurement) — a reader that only
handles one numeric shape will silently drop values once a container grows past whatever boundary it
assumed. **Absent and zero are different states and must not be collapsed**: `null`/absent means "Graph
did not report a figure" (e.g., because you called GET-single, or v1.0); `0` means a genuinely empty
container. Treating a partial or absent result as zero produces a dashboard that confidently reports "0
B" for a tenant holding real data — the exact failure this finding corrects.
