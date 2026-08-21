# SharePoint Embedded — platform research, 2026-08-20

> **Purpose**: establish current SPE platform state before scoping `sdap-SPE-admin-app-r2`. The app was
> built March 2026; the local knowledge corpus was curated 2026-05-14. Both predate material changes.
> **Method**: Microsoft Learn + M365 Message Center, cross-checked against the code in
> `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs`.
> **Consumed by**: [`../design.md`](../design.md) §4.

---

## 1. Container-type APIs are GA on v1.0

`fileStorageContainerType`, `fileStorageContainerTypeRegistration`, and the application permission-grant
APIs are **generally available on v1.0**.

**Code impact**: `CreateGraphClient` hardcodes `https://graph.microsoft.com/beta` (two sites, ~L4195 and
~L4212), and `ListContainersPageAsync` hand-builds a `/beta/...` nextLink URL (~L925). The Settings screen
exposes a **Graph Endpoint** field showing `https://graph.microsoft.com/v1.0` that nothing reads —
`ContainerTypeConfig` carries no endpoint field. Beta schema drift is a standing generator of the
wrong-property-name defect class in §3.

---

## 2. `containerTypes` does not support application permissions — this is the root cause

`GET /storage/fileStorage/containerTypes` (v1.0):

| Permission type | Least privileged | Higher privileged |
|---|---|---|
| Delegated (work or school account) | `FileStorageContainerType.Manage.All` | Not available |
| Delegated (personal Microsoft account) | Not supported | Not supported |
| **Application** | **Not supported** | **Not supported** |

> *"Either the SharePoint Embedded admin role or the Global admin role is required to call this API."*

**Code impact**: `GetClientForConfigAsync` → `CreateGraphClient` builds a `ClientSecretCredential` with
`https://graph.microsoft.com/.default` — pure app-only. The Container Types screen therefore **cannot
work** regardless of credential values. The UI's hardcoded remediation text ("Check the app registration
credentials in the config") is misleading: the same config serves Containers successfully.

The delegated path already exists in-code — `GetClientForOwningAppAsync` performs OBO exchange with
per-`(configId + sha256(userToken))` caching, built for SPE-084 multi-app support. The work is routing
admin-role calls through it, not building it.

Leading hypothesis for the Search screen failure is the same root cause (Graph `/search/query` app-only
coverage is narrow); to be confirmed once real errors are surfaced.

---

## 3. Property names verified against current v1.0 schema

The v1.0 `fileStorageContainerType.settings` shape:

```
urlTemplate · isDiscoverabilityEnabled · isSearchEnabled · isItemVersioningEnabled
itemMajorVersionLimit · maxStoragePerContainerInBytes · isSharingRestricted
consumingTenantOverridables · agent.chatEmbedAllowedHosts
```

| Code writes | Correct v1.0 property | Note |
|---|---|---|
| `majorVersionLimit` | `itemMajorVersionLimit` | PATCH is a no-op |
| `storageUsedInBytes` | `maxStoragePerContainerInBytes` | PATCH is a no-op **and** semantically wrong — the real property is a quota *ceiling*, not consumption |
| `deletedDateTime` in `$select` | not declared on `fileStorageContainer` | Rejected by Graph; a comment in the same method already states this |

The app exposes four container-type settings; the real surface is nine. `isSearchEnabled` /
`isDiscoverabilityEnabled` / `agent.chatEmbedAllowedHosts` are absent and are the settings that govern
whether container content is indexable and agent-discoverable at all.

---

## 3b. Container-type lifecycle — how creation actually works (added 2026-08-20, second pass)

Researched in response to "if we can't create container types in our tool, how do we create them?"
**Correction to the first pass**: creation is *not* restricted to trial, and *not* admin-role-gated.

### Creation paths (four, all viable)

| Path | Billing classifications | Role required |
|---|---|---|
| **Graph** `POST /storage/fileStorage/containerTypes` (beta) | `standard`, `trial`, `directToCustomer` — default `standard` | **Delegated-only.** `FileStorageContainerType.Manage.All`. **No admin role.** Any non-guest owning-tenant user; the caller is auto-assigned as an owner. |
| **SharePoint Embedded VS Code extension** | trial + standard | Non-guest owning-tenant member |
| **PowerShell** `New-SPOContainerType` | trial + standard | SharePoint Embedded Administrator (tenant-wide operations) |
| **SharePoint admin center → Apps** (GA Jul 2026) | standard, with billing type selection | SharePoint Embedded Administrator; **no Global admin consent needed** |

> ⚠️ The Graph API *reference* page carries the boilerplate note *"Either the SharePoint Embedded admin role
> or the Global admin role is required."* The **conceptual doc contradicts it for create**: *"delegated-only
> and can be called by any non-guest owning-tenant user. The caller doesn't need an administrator role."*
> Treat the conceptual doc as authoritative for create and **verify empirically** before designing around it.

### List is ownership-filtered, not admin-gated — this changes the D1 calculus

> *"List results are filtered by ownership. Non-administrator users see only the container types they've been
> granted permission on, while SharePoint Embedded Administrators and Global Administrators see every
> container type in the tenant."*

So the Container Types screen **works under delegated auth for an ordinary owning-tenant user** — it simply
scopes to container types that user owns. The SPE Admin role widens the view to tenant-wide; it is not a
precondition for the screen functioning. This materially de-risks rebuilding the screen (design.md D1).

**Unchanged**: `Application` permission remains **Not supported** for both list and create. The app-only root
cause in §2 stands.

### Constraints the admin UI must model

| Constraint | Consequence for the app |
|---|---|
| One owning Entra app ↔ **one** container type (1:1, strongly coupled) | Not a general CRUD grid; creation implies an app registration decision |
| Container type ID and owning app ID are **immutable** | No edit affordance on either |
| **Cannot** convert trial → production, or standard → pass-through | Billing choice is permanent at creation; the UI must say so before submit |
| Max **25** container types per tenant; at most **one** trial | Show remaining quota; block at limit with a real message |
| **Only trial** container types can be deleted; standard deletion unsupported | Delete affordance must be conditional, not universally rendered |
| Settings replication to consuming tenants takes **up to 24 hours**; consuming-tenant overrides persist | **Direct hit on §2.4** — an admin saves a setting, sees no change, and concludes the tool is broken. Must show "pending replication (up to 24h)" and surface override state. |
| Billing-profile attach is via the VS Code extension or an Azure admin flow — **not** Graph | The one step that genuinely cannot live in the admin app. Deep-link out. |
| Registration on consuming tenants needs `FileStorageContainerTypeReg.Selected` (application permission) | Distinct from the delegated list/create path |

---

## 3c. Legal hold, eDiscovery, retention — use Purview, do not build

Researched in response to "for the legal hold related do the research and add if it makes sense."

**Finding: SharePoint Embedded compliance is delivered through Microsoft Purview, not through
container-level app APIs.**

- Retention policies configured for **all SharePoint sites** apply to all SPE containers.
- Purview **eDiscovery** can search, hold, and export SPE content. Tenant-wide: configure eDiscovery Search
  for all SharePoint sites. Scoped: choose sites under the SharePoint sites workload and supply the
  **container URL**.
- **Legal holds** preserve copies into a hidden Recoverable Items folder, same mechanism as SharePoint/OneDrive.
- **Retention labels** are supported; Microsoft advises retention policies/labels over eDiscovery holds for
  long-term retention unrelated to an investigation.
- Container content inherits the host tenant's full compliance posture.

**Disposition — do NOT build hold/retention management into SPE Admin.** It would duplicate Purview with a
worse, narrower, unauditable surface, and it fails CLAUDE.md §11 question 2 (extension test) outright.
**R1's deferral of SPE-080 (eDiscovery) and SPE-081 (retention labels) was the correct call** — but for the
wrong stated reason. It was deferred as "beta API, limited availability"; the real reason is that it is
someone else's surface.

**What *is* worth doing** (design.md Workstream C, small):

1. Document in-app that SPE compliance is governed in Purview, with a deep-link to the compliance portal
   and the container URL an admin needs to scope an eDiscovery search — an admin looking for "legal hold"
   in this tool should be routed, not stonewalled.
2. Surface each container's **URL** in the Containers grid / detail. It is the scoping key for eDiscovery
   and is currently not exposed anywhere.

**Not verified**: whether per-container hold/retention *state* is queryable for a read-only posture column.
Worth a spike before committing to it; do not assume.

---

## 4. Microsoft shipped an SPE admin experience — GA early July 2026

A unified SharePoint Embedded **Apps** page in the SharePoint admin center (MC1290827), rolled out end of
June 2026, complete early July 2026:

- Create and install SPE apps **without Global admin consent**
- "Installed apps" (deployed in the org) vs "Owned apps" (created by the org)
- Create a new Entra app or attach an existing one during creation
- Assign up to **three owners** for app settings and billing
- Select a **permanent** billing type: "User org" or "Owner org"

**Overlaps** the SPE Admin app's Container Types and Register screens — the same screens that are broken.
Container *type* lifecycle is now Microsoft's surface. Containers, file browse, per-container permissions,
columns, custom properties, and quota remain Spaarke's. This is the largest scope lever available
(design.md D1).

---

## 5. New capabilities the app does not model

### 5.1 Container archival — GA February 2026

Reduces storage cost by **up to 75%** and improves Copilot result relevance by de-prioritising inactive
content. Admins opt in per container type via PowerShell `Set-SPOContainerType -IsArchiveEnabled`, then
manage archived containers via new Graph APIs and SharePoint admin center options.

**Spaarke fit**: containers are provisioned per project/matter (`ProvisionProjectEndpoint`,
`DemoProvisioningService`). Legal matters close and go cold on a predictable lifecycle, so a large and
growing fraction of container storage is inactive by definition. This is a recurring cost line with a
direct lever — the strongest ROI item found in this research.

### 5.2 `fileStorageContainer.informationBarrier` — beta, March 2026

Manages a container's information barrier. IB segments restrict communication and collaboration between
user groups where compliance policy requires.

**Spaarke fit**: this is **ethical walls / conflict-of-interest screens** — a first-class legal-industry
requirement rather than a generic compliance nicety. Container-per-matter maps cleanly onto IB segments.
**Caveat**: beta. R1 deferred SPE-080/081 (eDiscovery, retention labels) for precisely this reason; apply
the same bar.

### 5.3 `fileStorageContainerType.permissions` relationship — new

Manages container type **owners**. Supersedes part of the current ContainerTypePermissions screen.

### 5.4 Per-container item recycle bin — GA v1.0

`/storage/fileStorage/containers/{containerId}/recycleBin/items`, with restore (`207 Multi-Status`) and
permanent delete. **Distinct** from `/storage/fileStorage/deletedContainers` (deleted *containers*), which
is what the code currently targets. Two different features; design.md D3 decides which the app exposes.

### 5.5 Native PDF viewer — enhanced 2026

In-file search, comments and sticky notes, printing. Consumption-side — relevant to `SpeFileViewer` /
`SpeDocumentViewer`, **not** to this project. Recorded for the PCF owners.

### 5.6 Compliance posture (unchanged, worth stating)

Container content inherits the host tenant's security and compliance posture, participates in Microsoft
Purview retention policies, and is subject to retention holds, expiration, and disposition review exactly
as SharePoint or OneDrive content. SPE has no built-in UI for all compliance operations — which is the
standing justification for the SPE Admin app existing at all.

---

## 6. Deprecation — SPE agent SDK

The SharePoint Embedded agent SDK (React `ChatEmbedded` control) was **deprecated March 2026**, replaced by
Microsoft Foundry Agent Service with a SharePoint Embedded knowledge source.

**Verified 2026-08-20: Spaarke has no usage** — `ChatEmbedded`, `chatEmbed`, and
`@microsoft/sharepointembedded` return no matches across `src/`. No action required. Recorded so nobody
reaches for a deprecated control.

---

## 7. SPE knowledge source in Foundry — flagged, out of scope for R2

The **Copilot Retrieval API is GA** and now offers a **`sharePointEmbedded` data source in preview**, billed
pay-as-you-go on the Copilot Studio message meter. Querying users no longer each require an M365 Copilot
licence, though **one licensed user must initialise the semantic index**. Foundry IQ (built on Azure AI
Search) provides the permission-aware managed knowledge layer. Some agentic retrieval features are GA in
the `2026-04-01` REST API; the SPE data source itself remains preview.

**Relevance to Spaarke**: a credible alternative or complement to the existing custom retrieval layer
(`Services/Ai/RagService.cs`, `RagIndexingPipeline`, `RagQueryBuilder`, `SearchIndexNameResolver`,
`IRagService`). The differences that matter are licensing, who owns and pays for the index, permission
trimming, and whether per-matter scoping is expressible.

**Disposition**: **out of scope for R2** — this is an AI-architecture decision, not an SPE-admin one, and
belongs in a separate evaluation against the existing RAG stack (candidate owner: the
`ai-advanced-capabilities` program). The only R2-relevant slice is admin *visibility* of
`isSearchEnabled` / `isDiscoverabilityEnabled` / `agent.chatEmbedAllowedHosts` (design.md Workstream C).

---

## 8. Knowledge-corpus refresh required

[`knowledge/sharepoint-embedded/`](../../../knowledge/sharepoint-embedded/) was curated **2026-05-14**
(`knowledge/REFRESH-LOG.md`, Batch 3) and predates every finding above. Refresh per
`knowledge/REFRESH-PROCEDURE.md` as part of this project; at minimum `learn-containertypes.md`,
`learn-containers.md`, and `learn-overview.md`, plus new coverage of archival, information barriers, and
the admin-center Apps experience.

---

## Sources

- [List containerTypes — Graph v1.0](https://learn.microsoft.com/en-us/graph/api/filestorage-list-containertypes?view=graph-rest-1.0)
- [Create fileStorageContainerType — Graph beta](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes?view=graph-rest-beta)
- [Create and configure a container type](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type) (updated 2026-07-27)
- [Create apps with PowerShell — New-SPOContainerType](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/admin/create-apps-powershell)
- [Create eDiscovery holds in an eDiscovery case](https://learn.microsoft.com/en-us/purview/ediscovery-create-holds)
- [Learn about retention for SharePoint and OneDrive](https://learn.microsoft.com/en-us/purview/retention-policies-sharepoint)
- [MC1290827 — Create a line-of-business SharePoint Embedded app in SharePoint admin center](https://mc.merill.net/message/MC1290827)
- [MC1215074 — SharePoint Embedded container archival](https://mc.merill.net/message/MC1215074)
- [What's new in SharePoint Embedded](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new)
- [Restore recycleBinItem — Graph v1.0](https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-restore-recyclebinitem?view=graph-rest-1.0)
- [SharePoint Embedded administrator role](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/adminrole)
- [Set-SPOContainerType](https://learn.microsoft.com/en-us/powershell/module/microsoft.online.sharepoint.powershell/set-spocontainertype?view=sharepoint-ps)
- [Add Microsoft 365 Copilot and agent experiences](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/agent-experiences)
- [Set up SharePoint Embedded as a knowledge source in Microsoft Foundry](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/declarative-agent/sharepoint-embedded-knowledge-source)
- [Plan security, compliance, and governance — SharePoint Embedded](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/security-compliance-governance)
- [What is Foundry IQ?](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-foundry-iq)
