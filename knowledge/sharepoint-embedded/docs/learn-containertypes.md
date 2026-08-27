---
source: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/containertypes
fetched: 2026-05-14
refreshed: 2026-08-26
refreshed-by: sdap-SPE-admin-app-r2 (task 061), per knowledge/REFRESH-PROCEDURE.md
refresh-scope: >
  Added the app-only permission limitation (root cause of a live-tenant defect class), the correct
  v1.0 settings property names (nine total, Graph $metadata-verified), the four container-type
  creation paths including the July-2026 admin-center path, empirically-verified lifecycle
  constraints, and the unresolved create-role documentation conflict (Graph reference page vs
  conceptual doc). Original 2026-05-14 Learn snapshot retained below with inline correction
  callouts at the two points it is now known to be misleading.
note: Supplemental — captured to fill the gap left by the 404 on the containers/ concept URL listed in the directive.
---

# Create new SharePoint Embedded container types | Microsoft Learn

> ## 🔄 2026-08-26 refresh summary
>
> Refreshed by `sdap-SPE-admin-app-r2` task 061 after the project's Workstream B/C/D tasks (010–030)
> found and fixed a live defect class rooted in stale/wrong assumptions about this exact surface.
> Everything below is either a Microsoft Learn citation or a project finding explicitly marked
> **VERIFIED** with its date and source task. Jump to:
>
> - [App-only permission is NOT supported](#-app-only-permission-is-not-supported-for-containertypes-verified) — the root cause of a real production defect
> - [The real v1.0 settings shape (nine properties)](#-the-real-v10-settings-shape-nine-properties-verified-2026-08-24)
> - [Four container-type creation paths](#four-container-type-creation-paths-2026-08-26)
> - [The create-role documentation conflict — unresolved](#-the-create-role-documentation-conflict--not-resolved-empirically)
> - [Lifecycle constraints — verified against a live tenant](#lifecycle-constraints--verified-against-app-behaviour-2026-08-23)

A container type is a SharePoint Embedded resource that defines the relationship, access privileges, and billing accountability between a SharePoint Embedded application and a set of containers. Also, the container type defines behaviors on the set of containers.

Each container type is strongly coupled with one SharePoint Embedded application, which is referred to as the owning application. The owning application developer is responsible for creating and managing their container types. SharePoint Embedded mandates a 1:1 relationship between the owning application and a container type.

A container type is represented on each container instance as an immutable property (ContainerTypeID) and is used across the entire SharePoint Embedded ecosystem, including:

- **Access authorization**: A SharePoint Embedded application must be associated with a container type to get access to container instances of that type. Once associated, the application has access to all container instances of that type. The actual access privilege is determined by the application-ContainerTypeID permission setting. The owning application by default has full access privilege to all container instances of the container type it's strongly coupled with.
- **Easy exploration**: Container types can be created for trial purposes, allowing developers to explore SharePoint Embedded application development and assess its features for free.
- **Billing**: Container types for nontrial purposes are billable and must be created with an Azure Subscription.
- **Configurable behaviors**: Container type defines selected behaviors for all container instances of that type.

> **Notes**:
> 1. You must specify the purpose of the container type you're creating at creation time. A container type set for trial purposes can't be converted for production; or vice versa.
> 2. Standard and passthrough container types can't be converted once created. If you want to convert a standard container type to passthrough billing or vice versa, you must delete and re-create the container type.

## Tenant requirements

- An active instance of SharePoint is required in your Microsoft 365 tenant.
- Users who authenticate into SharePoint Embedded container types and containers must be in Microsoft Entra ID (Members and Guests)
- A Microsoft Entra ID app registration needs to be configured for container type management.

## Creating container types

SharePoint Embedded has two different container types you can create.

1. Trial container type. Uses the `trial` billing classification.
2. Standard container type. Uses the `standard` or `directToCustomer` billing classification.

To create a container type, your Microsoft Entra ID application needs to have the `FileStorageContainerType.Manage.All` application permission on the owning tenant. Your Microsoft Entra ID application needs to call the [Create fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes) endpoint on behalf of a SharePoint Embedded Administrator:

> ⚠️ **CORRECTED 2026-08-26** — this sentence is misleading on the permission *type*. `FileStorageContainerType.Manage.All` for container-type creation is a **delegated** permission, not an application permission — see [App-only permission is NOT supported](#-app-only-permission-is-not-supported-for-containertypes-verified) below. `sdap-SPE-admin-app-r2` task 010 empirically confirmed (2026-08-21, live Spaarke Dev tenant) that an app-only (client-credentials) token with this role assigned still receives `403 accessDenied` calling the sibling LIST endpoint on both `v1.0` and `beta`. Whether the "on behalf of a SharePoint Embedded Administrator" clause for CREATE is even still accurate is itself in dispute — see the create-role conflict section below.

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypes
Content-Type: application/json

{
  "name": "{ContainerTypeName}",
  "owningAppId": "{ApplicationId}",
  "billingClassification": "{BillingClassification}",
  "settings": {
    ...
  }
}
```

> ⚠️ **Still `/beta` as of 2026-08-26.** [Create fileStorageContainerType — Graph beta reference](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes?view=graph-rest-beta) is the only version Microsoft documents for this call. This is **narrower** than the general "container-type APIs are GA on v1.0" claim — see [v1.0 GA status by operation](#v10-ga-status-is-per-operation-not-blanket-verified-2026-08-20-through-2026-08-24) below.

Replace:

- `{ContainerTypeName}` with a user-friendly name.
- `{ApplicationId}` with the ID of your application.
- `{BillingClassification}` with either `trial`, `standard`, or `directToCustomer`.

## Trial container type

A container type can be created for trial/development purposes and isn't linked to any Azure billing profile. For trial container types, the developer tenant is the same as the consuming tenant. Each developer can have only one container type with `trial` billing classification in their tenant at a time. The trial container type is valid for up to 30 days but can be removed at any time within this period.

You can easily set up a trial container type using the [SharePoint Embedded Visual Studio Code extension](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/spembedded-for-vscode).

Restrictions applied to trial container types:

- The tenant can have up to five containers of the container type.
- Each container has up to 1 GB of storage space.
- The container type expires after 30 days.
- The developer must permanently delete all containers of an existing container type in trial status to create a new container type for trial.
- The container type is restricted to work in the developer tenant. It can't be deployed in other consuming tenants.

## Standard container types (nontrial)

A standard container type can be used in production environments. Each tenant can have 25 container types at a time.

### Billing models

- **Standard billing** — All consumption-based charges are directly billed to the tenant who owns or develops the application. The admin in the developer tenant must establish a valid billing profile when creating a standard container type.
- **Passthrough billing** (`directToCustomer`) — Consumption-based charges are billed directly to the tenant registered to use the SharePoint Embedded application (consuming tenant). Admins in the developer tenant don't need to set up an Azure billing profile.

### Set the billing profile (standard)

For standard billing container types, the developer tenant Global Administrator needs to:

- Create an Azure subscription in their tenancy
- Create a resource group attached to the Azure subscription

After creating the container type with `standard` billing classification, attach a billing profile to the container type using SharePoint Online Management Shell:

```powershell
Add-SPOContainerTypeBilling -ContainerTypeId <ContainerTypeId> -AzureSubscriptionId <AzureSubscriptionId> -ResourceGroup <ResourceGroup> -Region <Region>
```

> **VERIFIED 2026-08-24** (`sdap-SPE-admin-app-r2` design.md §4.2d, `notes/task-028-findings.md` sibling
> research) — billing-profile attach requires the **SharePoint Embedded Administrator** role *plus*
> owner/contributor on the target Azure subscription, and it is PowerShell-only
> (`Microsoft.Online.SharePoint.PowerShell`, a .NET-Framework module). It **cannot** be driven from a
> Linux-hosted .NET service or from browser JavaScript holding subscription-owner credentials — Spaarke
> deliberately routes this to provisioning tooling (`customer-provisioning-orchestration-r1`), not to an
> admin web app. `Microsoft.Syntex` resource-provider registration can lag; a `SubscriptionNotRegistered`
> error means wait and retry, not a real failure.

Every container type must have an owning application. A single owning app can only own one container type at a time. An Azure subscription can be attached to any number of container types.

## Configuring container types

The Developer Admin may apply configuration when calling the [Create fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes) endpoint. Alternatively, they may call the [Update fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestoragecontainertype-update) endpoint to reconfigure an existing container type.

> **Important**: Updating settings on a container type may take up to **24 hours** for the new values to be replicated on all consuming tenants. If a consuming tenant applied overrides on container type settings, the new values aren't applied and the overrides remain in place.

> **VERIFIED 2026-08-24** (`sdap-SPE-admin-app-r2` task 025, `notes/task-025-schema-verification.md`) — as
> of this refresh, the *live* `consumingTenantOverridables` value observed on all four Spaarke Dev
> container types is `"sharingCapability,itemMajorVersionLimit,isOfficeRestricted"` — a comma-delimited
> enum-flags string. Two of those three flags (`sharingCapability`, `isOfficeRestricted`) are **not**
> members of the installed Graph SDK's typed override enum (`Microsoft.Graph` 6.5.0), which only declares
> `UrlTemplate · IsDiscoverabilityEnabled · IsSearchEnabled · IsItemVersioningEnabled ·
> ItemMajorVersionLimit · MaxStoragePerContainerInBytes`. Parsing this field through the typed enum
> silently drops real data; read it as a raw string instead.

## Registering container types

To create and interact with containers, you must register the container type within the Consuming Tenant. The owning application defines the permissions for the container type by invoking the [Create fileStorageContainerTypeRegistration](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertyperegistrations) endpoint.

## Deleting container types

The Developer Admin can only delete trial container types in their tenant. Deletion of standard container types is not yet supported. To delete a container type, you must first remove all containers of that container type, including from the deleted container collection.

---

## 🔴 App-only permission is NOT supported for containerTypes — VERIFIED

**Source (documentation)**: [List containerTypes — Graph v1.0](https://learn.microsoft.com/en-us/graph/api/filestorage-list-containertypes?view=graph-rest-1.0), captured in
`projects/sdap-SPE-admin-app-r2/notes/spe-platform-research-2026-08-20.md` §2.

`GET /storage/fileStorage/containerTypes` (v1.0) permissions table:

| Permission type | Least privileged | Higher privileged |
|---|---|---|
| Delegated (work or school account) | `FileStorageContainerType.Manage.All` | Not available |
| Delegated (personal Microsoft account) | Not supported | Not supported |
| **Application** | **Not supported** | **Not supported** |

**VERIFIED empirically, 2026-08-21**, `sdap-SPE-admin-app-r2` task 010
(`notes/obo-spike-findings.md`, live calls against Spaarke Dev tenant `a221a95e-…`, read-only, no
container type created/modified/deleted). App-only (client-credentials) token, correctly audienced
(`aud = https://graph.microsoft.com`), with `FileStorageContainerType.Manage.All` granted and
admin-consented:

```
GET /v1.0/storage/fileStorage/containerTypes    → 403 accessDenied
GET /beta/storage/fileStorage/containerTypes    → 403 accessDenied
  "Caller does not have required permissions for this API"
```

**This is a root-cause finding, not a theoretical one.** It was the actual reason a live production
admin tool's Container Types screen could not work under any application-permission configuration,
regardless of credential correctness — a delegated (OBO) token is structurally required. If your
application's Container Types surface uses app-only/client-credentials auth, this API will refuse it
regardless of which role or scope you grant the app registration.

## ✅ The real v1.0 settings shape — nine properties, VERIFIED 2026-08-24

**Source**: `GET https://graph.microsoft.com/{v1.0,beta}/$metadata` (the OData CSDL Microsoft publishes)
— a stronger authority than documentation prose, and it needs no token.
`sdap-SPE-admin-app-r2` task 025, `notes/task-025-schema-verification.md`.

`ComplexType Name="fileStorageContainerTypeSettings"` — **exactly nine properties on v1.0**:

| # | Property | Type | v1.0 | beta |
|---|---|---|---|---|
| 1 | `consumingTenantOverridables` | `fileStorageContainerTypeSettingsOverride` (enum flags) | ✅ | ✅ |
| 2 | `isDiscoverabilityEnabled` | `Edm.Boolean` | ✅ | ✅ |
| 3 | `isItemVersioningEnabled` | `Edm.Boolean` | ✅ | ✅ |
| 4 | `isSearchEnabled` | `Edm.Boolean` | ✅ | ✅ |
| 5 | `isSharingRestricted` | `Edm.Boolean` | ✅ | ✅ |
| 6 | `itemMajorVersionLimit` | **`Edm.Int64`** | ✅ | ✅ |
| 7 | `maxStoragePerContainerInBytes` | **`Edm.Int64`** | ✅ | ✅ |
| 8 | `sharingCapability` | `sharingCapabilities` (enum) | ✅ | ✅ |
| 9 | `urlTemplate` | `Edm.String` | ✅ | ✅ |
| — | `isOfficeRestricted` | `Edm.Boolean` | ❌ | ✅ **beta-only, a 10th property, not part of the v1.0 nine** |

**Common naming mistakes to avoid** (confirmed against the SDK and Graph's own metadata, task 023,
`notes/task-023-findings.md`, and task 025):

| Wrong name (does not exist) | Correct v1.0 name | Note |
|---|---|---|
| `majorVersionLimit` | `itemMajorVersionLimit` | A PATCH using the wrong name is a silent no-op — Graph ignores unknown members on a merge-PATCH; it does **not** error. |
| `storageUsedInBytes` | `maxStoragePerContainerInBytes` | Not just a naming error — these are **different concepts on different resources**. `maxStoragePerContainerInBytes` is a quota **ceiling** on the container *type*. `storageUsedInBytes` is a consumption **measurement** on a container *instance* (see `learn-containers.md`). |
| `isVersioningEnabled` | `isItemVersioningEnabled` | Same silent-no-op risk. |
| `agent.chatEmbedAllowedHosts` | **does not exist, on either API version** | Confirmed absent from both CSDL documents, the SDK's generated model, and the live payload of all four Spaarke Dev container types. A prior R2 requirement doc asserted this property existed — it was fictional. If you find this name cited anywhere (including elsewhere in this corpus before this refresh), treat it as wrong. |

**Settings PATCH is a nested object, not top-level properties.** `{ "settings": { "itemMajorVersionLimit": … } }`, never `{ "itemMajorVersionLimit": … }` at the container-type root — the latter is silently ignored by Graph's merge-PATCH semantics (task 023 §2).

**Writability is not empirically established.** Every PATCH against a container type in the Spaarke Dev
tenant has returned `400 invalidRequest` in every shape tested, including a no-op write-back of the
current value (`notes/task-023-findings.md` §6, `notes/live-verification-2026-08-24.md`). Do not assume
a 200 response proves a write took effect — read the value back, and if you can't, say so rather than
inferring success.

## Four container-type creation paths (2026-08-26)

**Source**: `sdap-SPE-admin-app-r2` task 010 correction pass, `notes/spe-platform-research-2026-08-20.md`
§3b; `design.md` §4.2b. The original 2026-05-14 curation captured only the Graph path and the VS Code
extension link; the PowerShell path and the admin-center path (GA July 2026) are new to this corpus.

| Path | Billing classifications | Role required |
|---|---|---|
| **Graph** `POST /storage/fileStorage/containerTypes` (beta only — see above) | `standard`, `trial`, `directToCustomer` (default `standard`) | **Delegated-only.** `FileStorageContainerType.Manage.All`. Per the conceptual doc: **no admin role** — any non-guest owning-tenant user; the caller is auto-assigned as owner. *(Not empirically confirmed — see the conflict below.)* |
| **SharePoint Embedded VS Code extension** | trial + standard | Non-guest owning-tenant member |
| **PowerShell** `New-SPOContainerType` | trial + standard | **SharePoint Embedded Administrator** (tenant-wide operations) — [Create apps with PowerShell](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/admin/create-apps-powershell) |
| **SharePoint admin center → Apps** ([new in July 2026](learn-overview.md#-the-admin-center-apps-experience--ga-early-july-2026-verified)) | standard, with billing type selection | **SharePoint Embedded Administrator**; no Global admin consent needed |

Note the role requirement is **not uniform across paths**: PowerShell and the admin-center path both
require the SharePoint Embedded Administrator role; the Graph path's conceptual documentation claims no
role is needed at all. That inconsistency is the subject of the next section.

## 🔴 The create-role documentation conflict — NOT resolved empirically

**This is a known, deliberately unresolved contradiction. Do not silently pick one side.**

> **Graph API reference page** (boilerplate permissions note, [Create fileStorageContainerType —
> beta](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes?view=graph-rest-beta)):
>
> *"Either the SharePoint Embedded admin role or the Global admin role is required to call this API."*

versus

> **Conceptual doc** ([Create and configure a container
> type](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type), updated
> 2026-07-27):
>
> *"[Create is] delegated-only and can be called by any non-guest owning-tenant user. The caller doesn't
> need an administrator role."*

**Attempted empirical resolution, and why it failed**: `sdap-SPE-admin-app-r2` task 010 (2026-08-21)
tried to settle this against the live Spaarke Dev tenant and could not — testing CREATE is forbidden by
the project's live-tenant safety rules (creating a container type is not reversible in the way this
task's read-only constraint allowed), and testing LIST as a non-admin delegated user requires an
interactive/device-code sign-in this session could not perform. **Recorded as unresolved and carried
forward** (`notes/obo-spike-findings.md` §"Step 7"); as of task 027 (2026-08-24,
`notes/task-027-findings.md` line 46) it remained open: *"the finding does not exist... still open."*
No later R2 task closed it.

**What IS empirically settled** (same source): **LIST** is confirmed ownership-filtered rather than
admin-gated —

> *"List results are filtered by ownership. Non-administrator users see only the container types they've
> been granted permission on, while SharePoint Embedded Administrators and Global Administrators see
> every container type in the tenant."*
> — [List containerTypes — Graph v1.0](https://learn.microsoft.com/en-us/graph/api/filestorage-list-containertypes?view=graph-rest-1.0)

If you are building against this API and need to know whether CREATE genuinely requires an admin role,
**verify it yourself against your tenant before designing around either claim.**

## Lifecycle constraints — verified against app behaviour (2026-08-23)

**Source**: `sdap-SPE-admin-app-r2` task 030, `notes/task-030-findings.md` §1. Every numeric/behavioral
constraint already captured above (30-day trial expiry, 5-container/1GB trial cap, 25 container types
per tenant, one trial max, only-trial-deletable, bidirectional non-convertibility of billing
classification) was independently re-checked against this file's 2026-05-14 content and **confirmed
accurate** — none of those specific facts had gone stale. Five additional constraints an application
needs but this original page states only implicitly:

- A trial container type **cannot be registered on another consuming tenant** — restricted to the
  developer tenant for its whole life (stated above but easy to miss; a UI that offers a "Register"
  action for a trial type can only fail).
- Deleting a container type requires **first permanently deleting every container of that type**,
  including from the deleted-container collection — not just deactivating them.
- `v1.0` `PATCH` to change `billingClassification` after creation is not offered by the API at all; the
  only lever is delete-and-recreate, and only for trial types (standard/passthrough cannot be deleted).
- **Settings replication delay (up to 24h) is easy to build a UI around incorrectly** — an admin who
  saves a setting and immediately re-reads it may see the *old* value and reasonably conclude the save
  failed. Surface "pending replication" state explicitly rather than treating an unchanged read-back as
  proof of failure.
- `billingClassification` returned by Graph is **not guaranteed to already be lowercase-typed** on every
  SDK version — a live regression traced to a Graph SDK upgrade (`Microsoft.Graph` 5.101.0 → 6.5.0) found
  the field silently null for months where a comment claimed a specific SDK version's typing behavior as
  a permanent fact (`notes/task-030-findings.md` §7). **Any comment that names a specific dependency
  version as the reason for a workaround is a claim with an expiry date** — treat it as suspect on your
  next SDK upgrade, not just here.
