# Design — SDAP SPE Admin App R2 (make it work, on current SPE platform)

> **Status**: DESIGN (re-scoped 2026-08-20) · **Surface**: BFF + SpeAdminApp code page · **Origin**: r3 RED-1, re-scoped after live diagnosis + platform research
> **Supersedes**: the 2026-08-20 morning draft, which scoped R2 as a *behavior-preserving decomposition* of `SpeAdminGraphService.cs`. That framing is withdrawn — see [§1](#1-why-this-design-was-re-scoped).
> **Lineage**: follow-on to [`sdap-SPE-admin-app-r1`](../sdap-SPE-admin-app-r1/) · **Epic**: Code Quality (#427)
> **Research base**: [`notes/spe-platform-research-2026-08-20.md`](notes/spe-platform-research-2026-08-20.md) · [`notes/RED-1-investigation-research.md`](notes/RED-1-investigation-research.md) (superseded framing; retained for lineage)

---

## Hot-Path Declaration (CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Infrastructure/Graph/SpeAdminGraphService.cs + Api/SpeAdmin/** -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

---

## 1. Why this design was re-scoped

R2 was raised from a code review that graded the SPE Admin app out of standard on architecture, coding
standards, and file size. The headline finding was `Infrastructure/Graph/SpeAdminGraphService.cs` at
**4,911 LOC / 90 public methods / 23 nested types**, and the original R2 scope was a behavior-preserving
decomposition of that file.

**A live walkthrough on 2026-08-20 (Spaarke Dev tenant, config "Spaarke PAYGO 1") invalidated that scope.**
The app has never been fully functional. Four of nine screens fail outright, one more fails silently. A
decomposition that explicitly preserves behavior would have carefully relocated broken code into tidier
broken code.

Two further findings settled the re-scope:

1. **The stated safety net for the refactor does not exist.** The 359 tests across 14 SpeAdmin test files
   make no HTTP call and stand up no host — no `WebApplicationFactory`, no `HttpClient`, no WireMock. They
   assert DTO property shapes, auth-filter behavior in isolation, and validation constraints. The Graph
   interaction — the substance of the app — has **zero** automated coverage. The test files say so:
   *"Tests requiring SpeAdminGraphService (external Graph API + Key Vault) are structured as documented
   manual test procedures at the bottom of this file."* `Phase2IntegrationTests.cs:1315` contains a passing
   test whose stated purpose is *"only to make the manual test plan visible in the test runner."*
   This is how R1 closed 75 tasks in one day with *"4176 passing, 0 failing"* for an app that had never run
   successfully. The original design's acceptance criterion — "SpeAdmin integration tests green" — is
   satisfied whether the app works or not.

2. **The SPE platform moved substantially since the code was written** (see [§4](#4-platform-currency-2026-08-20)),
   including one change that makes a core screen *architecturally impossible* as built, and one that makes
   part of the app redundant with Microsoft's own tooling.

None of the observed defects are caused by file size, and none are fixed by splitting the file. The
decomposition remains real technical debt — it moves to the end of the sequence, where it can be done
safely and against seams the fix work has revealed.

---

## 2. Current state — verified

### 2.1 Working

| Surface | Notes |
|---|---|
| BU selector | Reads `businessunit`; correct |
| Config selector | Resolves `sprk_specontainertypeconfig`; values accurate |
| Containers | List + create + lifecycle actions render; 4 containers listed correctly |
| File Browser | Lists real SPE items; upload works |
| Settings | Environment + container-type-config CRUD saves (**but see 3.4 — one field is inert**) |

### 2.2 Broken

| Surface | Observed error | Root cause (verified) |
|---|---|---|
| **Container Types** | *"Failed to retrieve container types from the Graph API. Check the app registration credentials in the config."* | **App-only auth is not supported by the API.** See [§3.1](#31-the-auth-model-is-the-root-cause-not-credentials). The message is a hardcoded string and is misleading — the same credentials serve Containers successfully. |
| **Recycle Bin** | *"Parsing OData Select and Expand failed: Could not find a property named 'deletedDateTime' on type 'microsoft.graph.fileStorageContainer'."* | `$select` requests `deletedDateTime`, which the entity type does not declare. A comment 15 lines below in the same method states this. One-line defect; also possibly the wrong API entirely ([§3.3](#33-recycle-bin-may-be-the-wrong-feature)). |
| **Search** | *"SearchContainers(...): The call failed, please try again."* | Not yet isolated. Suspected same auth root cause as Container Types (Graph `/search/query` app-only coverage is narrow). Generic message hides the Graph error. |
| **Security** | *"Access denied to the Graph Security API. Ensure the app registration has SecurityEvents.Read.All permission."* | Accurate message. Missing Azure permission grant — **config, not code**. |
| **Audit Log** | *"Failed to retrieve audit log entries from Dataverse."* | Dataverse-side, not Graph. Not yet isolated. |

### 2.3 Silently broken

| Surface | Symptom | Root cause (verified) |
|---|---|---|
| **Storage Used** | Dashboard reads `0 B`; every Containers row reads `—` | `StorageUsedInBytes: null` is **hardcoded in four places** with the comment *"Not always returned by Graph."* The code asks Graph for the field, then discards it. Never implemented; never reported as missing. |
| **Container type settings writes** | Saves appear to succeed | PATCH body writes `majorVersionLimit` and `storageUsedInBytes`. The real v1.0 properties are **`itemMajorVersionLimit`** and **`maxStoragePerContainerInBytes`**. Both wrong → near-certain no-ops. |
| **Sync Status** | Dashboard reads `OK` | Reports success independent of whether the concerns above returned data. |

### 2.4 The systemic finding

**The app reports success when it is not succeeding.** Storage silently zero; Sync Status "OK"; a Settings
field that controls nothing; error messages that name the wrong cause and send an admin to check
credentials that are already correct. For an admin tool this is worse than being large — an operator
cannot trust what it tells them, and every diagnosis starts from a false premise.

This — not the line count — is the defect the code review was pointing at without naming.

---

## 3. Root causes

### 3.1 The auth model is the root cause, not credentials

`GET /storage/fileStorage/containerTypes` (v1.0 GA) permission table:

| Permission type | Least privileged | Higher privileged |
|---|---|---|
| Delegated (work or school account) | `FileStorageContainerType.Manage.All` | Not available |
| Delegated (personal Microsoft account) | Not supported | Not supported |
| **Application** | **Not supported** | **Not supported** |

> *"Either the SharePoint Embedded admin role or the Global admin role is required to call this API."*

`GetClientForConfigAsync` builds a `ClientSecretCredential` with `https://graph.microsoft.com/.default` —
pure app-only. **No credential value can make Container Types work under the current auth model.**

This traces to a decision R1 recorded and self-graded "Correct":

> *"App-only tokens for Phase 1; OBO extensibility architected — SPE Admin is admin tooling; user identity
> less critical in Phase 1"* → *"Correct — simplified auth; OBO path available for Phase 2+"*

That is the single load-bearing wrong decision. It is the confirmed cause of Container Types, the leading
hypothesis for Search, and it constrains any container-type-lifecycle work.

**Mitigating factor**: the delegated machinery already exists. `GetClientForOwningAppAsync` performs OBO
token exchange for multi-app scenarios (SPE-084) and is cached per `(configId + sha256(userToken))`. The
work is routing the admin-role-requiring calls through a delegated path and handling the operator
prerequisite (the signed-in admin must hold the **SharePoint Embedded Administrator** or Global admin role),
not building OBO from scratch.

> ⚠️ **ADR check required before implementation.** Changing the SPE Admin auth posture touches ADR-028
> (Spaarke Auth v2) and the ADR-008 authorization-filter pattern. If a conflict surfaces, apply the
> CLAUDE.md §6.5 resolution protocol and choose path A / B / C explicitly. Do not proceed silently.

### 3.2 The code was written against an imagined API surface

Three verified instances of the same defect class — a property name or shape that does not exist:

- `deletedDateTime` in the recycle-bin `$select` (contradicted by a comment in the same method)
- `majorVersionLimit` instead of `itemMajorVersionLimit`
- `storageUsedInBytes` instead of `maxStoragePerContainerInBytes`

The last also carries a **semantic** error: `maxStoragePerContainerInBytes` is a quota **ceiling**, not
consumption. The code models a limit as a metric, which is why the storage story never cohered.

Every one of these would have been caught by a single real call. None were, because no test makes one.

### 3.3 Recycle Bin may be the wrong feature

The code targets `/storage/fileStorage/deletedContainers` — deleted **containers**. Graph v1.0 now ships a
per-container item recycle bin at `/storage/fileStorage/containers/{containerId}/recycleBin/items`, with
restore (`207 Multi-Status`) and permanent delete. These are different features serving different admin
needs. Which one (or both) the app should expose is an open decision — see [D3](#6-open-decisions).

### 3.4 Dead configuration

The Settings screen exposes a **Graph Endpoint** field, populated with `https://graph.microsoft.com/v1.0`.
Nothing reads it. `CreateGraphClient` hardcodes `https://graph.microsoft.com/beta` in two places, and
`ContainerTypeConfig` carries no endpoint field at all. An admin troubleshooting via that setting will
chase their tail indefinitely. The displayed value is also, as of the v1.0 GA, the correct one.

---

## 4. Platform currency (2026-08-20)

Research base: [`notes/spe-platform-research-2026-08-20.md`](notes/spe-platform-research-2026-08-20.md).
The local [`knowledge/sharepoint-embedded/`](../../knowledge/sharepoint-embedded/) corpus was curated
2026-05-14 and predates every item below — **it requires a refresh as part of this project**.

### 4.1 Container-type APIs are GA on v1.0

`fileStorageContainerType`, `fileStorageContainerTypeRegistration`, and the application permission-grant
APIs are generally available on **v1.0**. The app hardcodes `/beta`. Beta shapes drift, which is a standing
generator of exactly the defect class in §3.2. Migrate the GA'd surfaces to v1.0 and make the endpoint
honest (either wire the Settings field or delete it).

### 4.2 Microsoft shipped an SPE admin experience — GA early July 2026

The SharePoint admin center now provides a unified SharePoint Embedded **Apps** page: create and install SPE
apps without Global admin consent, Installed vs Owned views, create-or-attach an Entra app, up to three
owners for settings and billing, and permanent billing-type selection (User org / Owner org).

This overlaps the SPE Admin app's Container Types and Register screens. It is a genuine alternative for
container-type *creation*, but — per §4.2b — **not a reason to drop the screens**, and D1 is resolved
accordingly.

### 4.2b Container-type lifecycle — creation paths and the delegated-auth payoff

**Corrects a first-pass assumption.** Graph container-type creation is **not** trial-restricted and **not**
admin-role-gated. Four creation paths exist:

| Path | Billing classifications | Role required |
|---|---|---|
| **Graph** `POST /storage/fileStorage/containerTypes` (beta) | `standard`, `trial`, `directToCustomer` (default `standard`) | **Delegated-only**, `FileStorageContainerType.Manage.All`. **No admin role** — any non-guest owning-tenant user; caller is auto-assigned as owner |
| SPE **VS Code extension** | trial + standard | Non-guest owning-tenant member |
| **PowerShell** `New-SPOContainerType` | trial + standard | SharePoint Embedded Administrator |
| **SharePoint admin center → Apps** | standard, with billing selection | SharePoint Embedded Administrator |

**And listing is ownership-filtered, not admin-gated:**

> *"List results are filtered by ownership. Non-administrator users see only the container types they've been
> granted permission on, while SharePoint Embedded Administrators and Global Administrators see every
> container type in the tenant."*

The Container Types screen therefore works under delegated auth for an ordinary owning-tenant user, scoped
to what they own; the SPE Admin role simply widens it tenant-wide. **The screen is far cheaper to restore
than the first pass assumed** — and since Workstream B builds the delegated path regardless, the marginal
cost of keeping Container Types is small. This resolves [D1](#6-open-decisions) in favour of rebuilding.

**Unchanged**: `Application` permission is **Not supported** for both list and create. The §3.1 root cause
stands; delegated auth is mandatory, not optional.

**Constraints the UI must model** (each is a §2.4 trap if ignored):

| Constraint | UI consequence |
|---|---|
| One owning Entra app ↔ **one** container type (1:1) | Not a CRUD grid; creation implies an app-registration decision |
| Container type ID + owning app ID **immutable** | No edit affordance on either |
| **Cannot** convert trial → production, or standard → pass-through | Permanent at creation; state it *before* submit, not after |
| Max **25** per tenant, at most **one** trial | Show remaining quota; block at limit with a real message |
| **Only trial** types are deletable | Conditional delete affordance, not universal |
| Settings replication takes **up to 24 hours**; consuming-tenant overrides persist | **Direct §2.4 hit.** Must show "pending replication (up to 24h)" and surface override state, or an admin will save, see nothing, and conclude the tool is broken |
| Billing-profile attach is PowerShell (`Add-SPOContainerTypeBilling`) — **not** Graph | Belongs to provisioning tooling, not this app — see §4.2d. The app **reads** billing state; it does not write it. |

### 4.2d Billing-profile attach belongs to provisioning, not to this app

Researched in response to "can this be addressed with C# code or PCF?"

**It is scriptable — as PowerShell, not Graph:**

```powershell
New-SPOContainerType -ContainerTypeName <name> -OwningApplicationId <appId>
Add-SPOContainerTypeBilling -ContainerTypeId <id> -AzureSubscriptionId <sub> -ResourceGroup <rg> -Region <region>
```

Requires the **SharePoint Embedded Administrator** role *plus* **owner or contributor on the Azure
subscription**. (`Microsoft.Syntex` resource-provider registration can lag; `SubscriptionNotRegistered`
means wait and retry.)

**Feasibility from our surfaces:**

| Surface | Verdict |
|---|---|
| **C# in the BFF** | Technically possible, practically bad. `Microsoft.Online.SharePoint.PowerShell` is a .NET Framework module; hosting it from a Linux-hosted .NET 10 App Service means shelling out to a Windows PowerShell that isn't there, or reverse-engineering the undocumented SPO admin endpoint beneath the cmdlet. Fragile and unsupported. **Rejected.** |
| **PCF** | **No.** Browser JavaScript cannot run PowerShell and must never hold Azure subscription owner credentials. |
| **Provisioning tooling** | ✅ **Correct home.** |

**Decision: billing-profile attach is out of scope for SPE Admin and belongs to
[`customer-provisioning-orchestration-r1`](../customer-provisioning-orchestration-r1/).** The rationale is
that it is a *provisioning* act, not an *administration* act:

- The billing method **cannot be changed after creation** — one-shot, not an operation
- It needs Azure subscription owner/contributor — a higher and different privilege set than a day-to-day SPE admin
- One owning Entra app ↔ one container type, max 25 per tenant — it happens a handful of times, ever
- `customer-provisioning-orchestration-r1` already owns repeatable per-customer setup and is already
  PowerShell (`Provision-Customer.ps1` + Bicep) — the cmdlets drop straight in

Clean CLAUDE.md §11 outcome: an existing component already does this class of work.

**What SPE Admin does instead** (Workstream C, small but genuinely missing): container types carry
`billingClassification` and **`billingStatus`** (e.g. `"valid"`). **Surface both, and warn when
`billingStatus` is not valid.** Invalid billing is a live operational failure mode with zero visibility
today. Read billing state in the app; write it in provisioning.

> **Cross-project action**: raise `Add-SPOContainerTypeBilling` + `New-SPOContainerType` as a provisioning-
> tooling requirement on `customer-provisioning-orchestration-r1` before R2 closes, so the capability lands
> somewhere rather than falling between the two projects.

### 4.2c Legal hold, eDiscovery, retention — Purview's surface, not ours

SPE compliance is delivered through **Microsoft Purview**, not container-level app APIs. Retention policies
scoped to all SharePoint sites apply to all SPE containers; Purview eDiscovery can search, hold, and export
SPE content (tenant-wide, or scoped by supplying the **container URL** under the SharePoint sites workload);
legal holds preserve into a hidden Recoverable Items folder exactly as for SharePoint/OneDrive; retention
labels are supported.

**Decision: do NOT build hold/retention management into SPE Admin.** It would duplicate Purview with a
narrower, unauditable surface and fails CLAUDE.md §11 question 2 outright. **R1's deferral of SPE-080 /
SPE-081 was correct** — though for the wrong stated reason (recorded as "beta API"; the real reason is that
it is someone else's surface).

**What R2 does instead** (Workstream C, small):
1. In-app guidance that SPE compliance is governed in Purview, with a deep-link to the compliance portal.
2. **Expose each container's URL** in the Containers grid/detail — it is the scoping key for an eDiscovery
   search and is currently surfaced nowhere. An admin looking for "legal hold" should be routed, not
   stonewalled.

*Not verified*: whether per-container hold/retention **state** is queryable for a read-only posture column.
Spike before committing; do not assume.

### 4.3 New capabilities the app does not model

| Capability | Status | Relevance to Spaarke |
|---|---|---|
| **Container archival** | GA Feb 2026; opt in via `Set-SPOContainerTypeConfiguration -IsArchiveEnabled $true` (**corrected 2026-08-27 by task 050** — this cell said `Set-SPOContainerType -IsArchiveEnabled`, which has no such parameter; needs SPO module ≥ 16.0.27515.12000), manage via the Graph **beta** `archive`/`unarchive` actions (absent from v1.0) + admin center | **Highest-value item on this list.** Up to **75% storage cost reduction** plus improved Copilot relevance by de-prioritizing inactive content. Spaarke provisions a container per project/matter; legal matters close and go cold on a predictable lifecycle. This is a direct, recurring cost line. |
| **`fileStorageContainer.informationBarrier`** | Beta (Mar 2026) | **Ethical walls / conflict-of-interest screens** — a first-class legal-industry requirement, not a generic compliance nicety. Container-per-matter maps cleanly onto IB segments. Beta status is a real caveat (cf. R1's SPE-080/081 eDiscovery deferral for the same reason). |
| **`fileStorageContainerType.permissions`** relationship | New | Manage container-type owners. Supersedes part of the current ContainerTypePermissions screen. |
| **Container type `settings` surface** | GA v1.0 | Real shape is `urlTemplate`, `isDiscoverabilityEnabled`, `isSearchEnabled`, `isItemVersioningEnabled`, `itemMajorVersionLimit`, `maxStoragePerContainerInBytes`, `isSharingRestricted`, `consumingTenantOverridables`, `agent.chatEmbedAllowedHosts`. The app exposes four settings, two under wrong names. |
| **Per-container recycle bin (items)** | GA v1.0 | See §3.3. |
| **Native PDF viewer** | Enhanced 2026 | In-file search, comments/sticky notes, printing. Consumption-side; relevant to `SpeFileViewer` / `SpeDocumentViewer`, not to this project. |

### 4.4 Deprecation — no exposure

The SharePoint Embedded agent SDK (React `ChatEmbedded`) was **deprecated March 2026**, replaced by
Microsoft Foundry Agent Service with a SharePoint Embedded knowledge source. **Verified: Spaarke does not
use it anywhere.** No action required; recorded so nobody reaches for it.

### 4.5 SPE knowledge source — flagged, out of scope

The Copilot Retrieval API is GA and now offers a **`sharePointEmbedded` data source in preview**, billed
pay-as-you-go on the Copilot Studio message meter; querying users no longer each need an M365 Copilot
licence, though one licensed user must initialise the semantic index. Foundry IQ / Azure AI Search underpins
it as a permission-aware managed knowledge layer.

**Relevance**: Spaarke already operates a custom retrieval layer over SPE (`Services/Ai/RagService.cs`,
`RagIndexingPipeline`, `RagQueryBuilder`, `SearchIndexNameResolver`). The SPE knowledge source is a
credible **alternative or complement** to it, with meaningfully different licensing and index-ownership
characteristics.

**This is an AI-architecture decision, not an SPE-admin one, and it is explicitly OUT OF SCOPE for R2.**
It should be raised as a separate evaluation against the existing RAG stack. The only R2-relevant slice is
admin *visibility*: `isSearchEnabled` / `isDiscoverabilityEnabled` / `agent.chatEmbedAllowedHosts` are
container-type settings that govern whether content is indexable and agent-discoverable at all — an admin
needs to see and control those, and today cannot. That slice is folded into §5 Workstream C.

---

## 5. Scope

Ordered. Each workstream is independently shippable; the sequence is load-bearing.

### Workstream A — Make failures visible (do first, gates everything else)

Rationale: §2.4. Until the app tells the truth, every subsequent diagnosis starts from a false premise.

- Replace hardcoded error strings with the real Graph / Dataverse error (code, message, request id),
  surfaced through ProblemDetails per ADR-019. Preserve a friendly summary; stop asserting a cause the
  code has not established.
- Audit the ~70 `catch (ODataError)` sites that return `null` / `false` / empty. Distinguish
  "legitimately absent" from "call failed" — the latter must not render as an empty grid.
- Make Sync Status reflect actual per-concern outcomes.
- **Outcome**: Search and Audit Log root causes fall out of this workstream rather than needing a separate
  investigation. Do not pre-scope fixes for them until A is landed.

### Workstream B — Resolve the auth model

Per §3.1. The architectural decision of the project ([D2](#6-open-decisions)).

- Route admin-role-requiring calls through a delegated (OBO) path, reusing `GetClientForOwningAppAsync`'s
  existing token-exchange and caching machinery.
- Keep app-only for the operations where it is supported and correct (container CRUD, drive items) — this
  is a **hybrid**, not a wholesale migration.
- Surface the operator prerequisite explicitly: the signed-in admin must hold the SharePoint Embedded
  Administrator or Global admin role. A missing role must produce an actionable message naming the role.
- Grant `SecurityEvents.Read.All` (Azure config; unblocks the Security screen with no code change).
- 🔔 **GATED**: the ADR-028 / ADR-008 §6.5 conflict check in [§5.1](#51--binding--adr-conflict-check-gates-workstream-b)
  MUST be completed, with a named path A / B / C decision, **before** any Workstream B implementation task
  starts. This is binding, not advisory.

### Workstream C — Correct the API surface

- Migrate GA'd surfaces from `/beta` to `/v1.0` (§4.1). Wire the Settings **Graph Endpoint** field or
  delete it — no third option.
- Fix the recycle-bin `$select` (§3.2).
- Fix `itemMajorVersionLimit` and `maxStoragePerContainerInBytes` (§3.2), including the quota-vs-consumption
  semantic split: a quota **ceiling** control and a consumption **metric** are different features and must
  not share a field.
- Implement real storage reporting, or remove the Dashboard tile and the Containers column. Not both null.
- Expand the container-type settings surface to the real v1.0 shape — all nine properties, including
  `isSearchEnabled` / `isDiscoverabilityEnabled` / `agent.chatEmbedAllowedHosts` (§4.5) and
  `consumingTenantOverridables`.
- Show **replication state** on container-type settings ("pending, up to 24h") and consuming-tenant override
  state (§4.2b). Without this, correct saves look like failures — a §2.4 trap.
- Adopt `fileStorageContainerType.permissions` for container-type **owner management** (up to three owners).
- **Expose container URL** in the Containers grid/detail — the eDiscovery scoping key, surfaced nowhere today (§4.2c).
- Add in-app Purview guidance + deep-link for hold / retention / eDiscovery (§4.2c).
- Container Types screen: model the immutability, no-conversion, quota (25 / one trial), and
  conditional-delete constraints from §4.2b.
- Surface `billingClassification` + **`billingStatus`**, and warn when billing is not valid (§4.2d). Read
  only — attach is provisioning's job, not this app's.

### Workstream D — Build the harness that should have existed

Per §1.1. R1 recommended this and it was never acted on.

- **WireMock-backed Graph tests** over the mapping surface — request shape, response mapping, error
  translation. CI-runnable. This is where most of the value sits, because most of the code is mapping, and
  it catches the entire §3.2 defect class.
- **`[Category("LiveIntegration")]`** suite against a real dev container type for operations involving
  consent, registration, permission, and role flows. Nightly or manual gate.
- Retire the DTO-shape tests and the manual-test-plan-as-passing-test per ADR-038's scaffolding bans;
  `/test-diet` classifies at project close.

### Workstream E — New capabilities

Per [D4](#6-open-decisions). Ordered by value.

- **Container archival** (GA Feb 2026) — strongest ROI (§4.3). Up to **75% storage cost reduction** plus
  improved Copilot relevance. Admin surface: archive/restore per container, expose archived state in the
  Containers grid. Note the per-container-type opt-in is PowerShell (`Set-SPOContainerType
  -IsArchiveEnabled`) — the app manages archived *containers*, not the opt-in itself.
- **Real quota management** — `maxStoragePerContainerInBytes` as a per-container **ceiling** control, cleanly
  separated from consumption reporting (§3.2). Gives legal customers per-matter storage caps and makes the
  storage tile mean something.
- **Per-container item recycle bin** — `/containers/{id}/recycleBin/items` with restore + permanent delete
  (§3.3, [D3](#6-open-decisions)). Likely what admins actually wanted from a screen called "Recycle Bin."
- ~~**Information barriers**~~ — **REMOVED from scope 2026-08-21 by owner decision** (`/design-to-spec`
  interview: *"we don't need ethical walls or conflict of interest functionality"*). Not deferred and not
  filed as a follow-on; the conditional beta-API risk review this bullet called for is moot. See
  [`spec.md`](spec.md) Owner Clarifications OC-03.
- **Explicitly NOT built**: legal hold / retention / eDiscovery management — Purview's surface (§4.2c).

### Workstream F — Decomposition — SPLIT OUT of R2 per [D5](#6-open-decisions)

> **Not in R2.** Carried here as the seed for a follow-on project (suggested name:
> `speadmingraphservice-decomposition-r1`, mirroring the sibling `chatendpoints-decomposition-r1`).
> **Entry condition**: R2 Workstreams A–E merged, and the Workstream D harness green — the refactor is
> unprotected without it (§1.1).
>
> **Exception — the two cheap hygiene items below ship inside R2**, since they are file moves with no
> behavioral surface and both reduce confusion during A–E work.

Sequencing rationale: the seams are currently inferred from a method-name census. After A–E, they will be
known from having worked inside most of the 14 concerns, and the harness will exist to make the split safe
for the first time.

**Ships in R2 (hygiene):**
- Delete the 3-line dead stub `Services/SpeAdmin/SpeAdminGraphService.cs`.
- Move `Api/ContainerItemEndpoints.cs` into `Api/SpeAdmin/` — it serves `/api/spe/containers/{id}/items/*`
  and is merely misfiled.

**Deferred to the follow-on:**

- Move the **23 nested public types** to `Models/SpeAdmin/` — the prerequisite for any real extraction, and
  the highest-value single step (~350 LOC out, and it breaks the type-gravity that binds 25 consumer files
  to this one class).
- Split along the **14 existing comment-banner sections** (the original authors already drew the seams,
  tagged with SPE ticket IDs) rather than inventing five buckets.
- Decide the **43-method `ForConfig` facade** (ADR-007 IL-isolation adapter, `CICD-088b`). It is a
  cross-cutting facade over every concern; per-concern extraction either duplicates it or recreates the
  aggregate. Recommended: its own file, honestly labelled as a mechanical adapter with one reason to change.
- **Do NOT** introduce a `SpeAdminDriveService` interface. It would be the third drive abstraction alongside
  `ISpeFileOperations` and `DriveItemOperations` — CLAUDE.md §11 points against it. See §7.

---

## 5.1 🔔 BINDING — ADR conflict check gates Workstream B

**Workstream B changes the SPE Admin authentication posture from app-only to hybrid delegated. That is an
architectural decision about auth, and it MUST surface through the CLAUDE.md §6.5 ADR Conflict Resolution
Protocol — as an explicit path A / B / C choice with a named human decision — rather than being made
quietly inside a task.**

This is not advisory. A task that implements delegated auth without having run this gate is in violation of
§6.5's "no fourth path" rule, and `code-review` should flag it as Critical.

### Why this fires

- **ADR-028 (Spaarke Auth v2)** is the canonical auth architecture. Per the auth-v4 research
  (`project_spaarke-auth-v4-dataverse-MI`), ADR-028 **has no OBO exception documented** — meaning even the
  *status quo* app-only posture sits uneasily with it, and a move to hybrid delegated certainly requires an
  explicit decision rather than an assumption.

  > ⚠️ **CORRECTION (2026-08-21, `/design-to-spec`)** — this premise is **stale**. ADR-028 **Amendment A4
  > (2026-08-17)**, published four days after this design was written, both specifies the OBO credential
  > shape *and* retains exception **E-1 "SpeAdmin per-tenant container-type ops"**, explicitly exempting
  > *"per-customer owning apps, which are other applications' identities"* from the no-client-secret rule.
  > `spaarke-auth-v4-dataverse-MI` has correspondingly scoped `SpeAdminTokenProvider` /
  > `SpeAdminGraphService` **out** of its migration on exactly that basis (its `design.md:149`). The gate
  > below still fires — but it resolves as **§6.5 path C (comply under E-1)** rather than the path A/B this
  > paragraph anticipates. Completed block: [`spec.md`](spec.md) → ADR Tensions.
- **ADR-008** governs the authorization-filter pattern that `SpeAdminAuthorizationFilter` implements across
  the whole `/api/spe` route group. Introducing a second auth path through that group touches the pattern.
- R1 recorded the current posture as a deliberate decision and self-graded it **"Correct"** (§3.1). It was
  not. Reversing a recorded architectural decision is precisely the case §6.5 exists for — the failure mode
  to avoid is a second silent decision replacing a first silent decision.

### Required output at spec time (§6.5 format)

The spec MUST contain an **ADR Tensions** section carrying this block, completed:

> 🔔 **ADR Conflict — Resolution Required**
> - **ADR in question**: ADR-028 (Spaarke Auth v2) [and ADR-008 if the filter pattern is affected]
> - **Specific rule**: [quote the MUST / MUST NOT being challenged]
> - **Conflict**: `GET /storage/fileStorage/containerTypes` does not support application permissions at all
>   (§3.1); Container Types cannot function under the current app-only posture regardless of credentials.
> - **Proposed path**: A (project-scoped exception) / B (ADR amendment) / C (pivot to comply)
> - **Rationale**: […]
> - **Impact if A or B accepted**: […]
> - **Alternative considered and rejected**: […]

**Note that path C is genuinely available** and must be shown to have been considered, not dismissed:
D1(b) — defer Container Types entirely to the SharePoint admin center (§4.2) — is an ADR-compliant way to
meet the requirement without changing the auth posture. It was rejected on 2026-08-20 for cost reasons
(D2 pays for the delegated path anyway), but that rejection is an *input* to the §6.5 decision, not a
substitute for making it.

### Enforcement points

| Stage | Gate |
|---|---|
| `/design-to-spec` | Emit the **ADR Tensions** section above with the block completed; do not leave it as a placeholder |
| `task-create` | The Workstream B tasks carry an `<escalation><trigger>` for the auth boundary |
| `task-execute` Step 9.5 | `adr-check` violations resolve via path A / B / C — **not** retry-until-clean |
| `code-review` Step 6 | Accepts a reasoned path-A exception cited in the PR description; otherwise Critical |

---

## 6. Open decisions

| # | Decision | Options | Status |
|---|---|---|---|
| **D1** | Container Types / Register screens | (a) Rebuild on delegated auth · (b) Defer entirely to the SharePoint admin center · (c) Read-only + deep-link | ✅ **RESOLVED 2026-08-20 → (a) rebuild.** Operator rationale: since D2 builds the delegated path regardless, the marginal cost is small. Reinforced by §4.2b — list is ownership-filtered rather than admin-gated, and Graph create is delegated-only with **no admin role required**, so the screen is materially cheaper to restore than the first pass assumed. Billing-profile attach still deep-links out (§4.2b). |
| **D2** | Auth model | (a) **Hybrid** — delegated for the calls that require it, app-only where supported · (b) Full delegated · (c) Stay app-only and descope | ✅ **RESOLVED → (a) hybrid.** Architectural; gates B and D1. ADR-028 / ADR-008 check required per §6.5. |
| **D3** | Recycle bin semantics | (a) Deleted **containers** (current) · (b) Per-container **item** recycle bin · (c) Both | ✅ **RESOLVED → (c) both.** (b) is the likelier admin intent; (a) already exists and needs only the §3.2 one-line fix. |
| **D4** | New capabilities in R2 | Archival · Quota · Item recycle bin · Information barriers | ✅ **RESOLVED → three of four.** Archival + quota + item recycle bin ship. **Information barriers REMOVED from scope entirely (owner decision, 2026-08-21 `/design-to-spec` interview: *"we don't need ethical walls or conflict of interest functionality"*)** — not deferred, no follow-on filed; the beta-API risk review D4 called for at spec time is therefore moot. Legal hold / retention / eDiscovery explicitly excluded — Purview's surface (§4.2c). |
| **D5** | **Does Workstream F — splitting the 4,911-LOC `SpeAdminGraphService.cs` — ship inside R2, or as its own follow-on project?** | (a) Workstream F in R2 · (b) Ship A–E as R2, decompose in a follow-on | ✅ **RESOLVED 2026-08-21 → (b) split out.** Three reasons: (1) scope grew materially with D1 + D4 — A–E is already a full project; (2) F is a large rewrite in a BFF hot path where 13 of 17 active worktrees are working (§8), so one combined PR is heavy merge exposure; (3) **F is better work after A–E** — the seams are currently inferred from a method census, but rebuilding Container Types, splitting quota from consumption, and adding archival means having worked inside most of the 14 concerns, and the Workstream D harness will exist to make the refactor safe. Precedent: `chatendpoints-decomposition-r1` is a standalone sibling. Cheap to flip if the operator disagrees. |

---

## 7. Placement justification (CLAUDE.md §11)

Per the three-question template, for the only genuinely *new* surface in this project:

- **Delegated auth path (Workstream B)** — *Existing*: `GetClientForOwningAppAsync` already performs OBO
  token exchange with per-`(configId + userTokenHash)` caching. *Extension*: yes — extend it to the
  admin-role call sites rather than adding a parallel path. *Cost of doing nothing*: Container Types
  returns 403 permanently; the API does not support application permissions at all (§3.1).
- **New capabilities (Workstream E)** — *Existing*: none. Archival, per-container quota ceilings, and the
  item recycle bin have no Spaarke equivalent and cannot be expressed through any current surface.
  *Cost of doing nothing*: archival is a standing, compounding storage bill on cold matter
  containers; without a quota ceiling there is no per-matter storage control at all. *(Information
  barriers were part of this justification until 2026-08-21, when the owner removed them from scope — D4.)*
- **Legal hold / retention / eDiscovery — rejected on the extension test.** Purview already delivers this
  for SPE containers (§4.2c). Building it here would duplicate an audited compliance surface with a
  narrower, unauditable one. R2 routes admins to Purview and exposes the container URL they need instead.
- **Workstream F extractions** — extractions of existing code, net-negative on complexity. No new
  capability, endpoint, package, or Dataverse surface.
- **Explicitly rejected**: a `SpeAdminDriveService` interface. `ISpeFileOperations` and `DriveItemOperations`
  already abstract drive operations for the runtime document stack; a third abstraction over the same Graph
  calls fails the extension test. The admin/runtime stack duplication (§8) is real but is a separate
  convergence question, deliberately **not** deepened here.

---

## 8. Risks & coordination

| Risk | Mitigation |
|---|---|
| **No test safety net exists today** — the refactor is unprotected, contrary to the prior design's premise | Workstream D precedes Workstream F. Non-negotiable ordering. |
| **BFF hot path with heavy merge contention** — 13 of 17 active worktrees touch BFF; `chatendpoints-decomposition-r1` is sequenced behind this project | `/conflict-check` before each PR. Prefer several small PRs (A, B, C, D) over one atomic mega-PR; if D5(a), land F as its own tightly-scoped PR in a quiet window. |
| **Auth change conflicts with ADR-028 / ADR-008** | 🔔 **Binding gate — see [§5.1](#51--binding--adr-conflict-check-gates-workstream-b).** §6.5 protocol, explicit path A/B/C with a named human decision, completed at `/design-to-spec` and cited in the PR. Silent implementation is a §6.5 violation and a Critical code-review finding. |
| ~~**Information barriers are beta**~~ | **Risk retired 2026-08-21** — information barriers removed from scope by owner decision (D4), so the beta-API exposure no longer applies to R2. |
| **Live SPE admin path** — container/permission operations affect real tenant state | `[Category("LiveIntegration")]` runs against a dedicated dev container type, never a shared/production one. |
| **Two parallel SPE stacks** — `SpeFileStore`/`DriveItemOperations`/`UploadSessionManager` (user-OBO runtime, ~3,000 LOC) duplicate admin-stack Graph operations and DTOs (`FileHandleDto` vs `SpeContainerItemSummary`, etc.) | Convergence is **out of scope**; recorded as a named follow-on. R2 must not deepen the split (§7). |

---

## 9. Acceptance criteria

**Functional (the point of the project)**
- [ ] All nine screens either work against the Spaarke Dev tenant, or are deliberately removed per D1/D3/D4 with the rationale recorded.
- [ ] No screen reports success while returning no data. Storage is either real or absent — never a silent `0 B`.
- [ ] Every failure surfaces the actual underlying error; no hardcoded message asserts an unestablished cause.
- [ ] The Settings **Graph Endpoint** field either takes effect or no longer exists.
- [ ] Container-type settings writes verifiably persist (confirmed by read-back against a live tenant).

**New capabilities (D4)**
- [ ] Container archival: archive/restore per container; archived state visible in the Containers grid.
- [ ] Per-container storage **ceiling** (`maxStoragePerContainerInBytes`) settable, and distinct from any consumption display.
- [ ] Per-container item recycle bin: list, restore (`207` partial-success handled), permanent delete.
- [ ] Container-type owner management via `fileStorageContainerType.permissions`.
- [ ] Container URL exposed; Purview deep-link present for hold / retention / eDiscovery.
- [ ] `billingClassification` + `billingStatus` surfaced, with a warning when billing is not valid (§4.2d).
- [ ] Billing *attach* raised as a requirement on `customer-provisioning-orchestration-r1` (cross-project handoff recorded, §4.2d).
- [ ] Container-type settings show replication-pending state and consuming-tenant overrides.
- [x] ~~Information barriers: shipped, or deferred with the beta-risk rationale recorded.~~ **Withdrawn 2026-08-21 — out of scope by owner decision (D4).**

**Platform currency**
- [ ] GA'd container-type surfaces call **v1.0**, not `/beta`. Container-type **create** may remain on beta (no v1.0 equivalent) — record that as a deliberate, isolated exception.
- [ ] Property names verified against current v1.0 schema — no repeat of the §3.2 class.
- [ ] [`knowledge/sharepoint-embedded/`](../../knowledge/sharepoint-embedded/) refreshed (2026-05-14 → current) per `knowledge/REFRESH-PROCEDURE.md`.

**Governance**
- [ ] 🔔 ADR-028 / ADR-008 §6.5 conflict check completed with a **named path A / B / C decision** before any Workstream B implementation ([§5.1](#51--binding--adr-conflict-check-gates-workstream-b)); the decision is cited in the PR description.

**Quality**
- [ ] WireMock Graph coverage over the mapping surface; a wrong property name fails CI.
- [ ] `[Category("LiveIntegration")]` suite exists and runs green against a dev container type.
- [ ] Scaffolding-class tests retired per ADR-038; `/test-diet` report clean at project close.
- [ ] `dotnet build -c Release` 0 errors under the analyzer gate; ArchTests green; publish size neutral (≤60 MB ceiling, current baseline ~44.96 MB incl. PDBs); no new NuGet; no new HIGH CVE.

**Decomposition (if D5(a))**
- [ ] Nested types relocated to `Models/SpeAdmin/`; split follows the existing banner seams; no component carries multiple diverged responsibilities per `docs/standards/COMPONENT-COMPLEXITY.md`.
- [ ] No new drive abstraction introduced (§7).

---

## 10. Dependencies / prerequisites

- ✅ **Dev SPE environment — CONFIRMED AVAILABLE 2026-08-21.** `spaarkedev1` (environment "Spaarke Dev",
  tenant `a221a95e-…`, config "Spaarke PAYGO 1") with its container type and containers is usable for
  Workstreams B and D. This unblocks the live tier of the test harness.

  > ⚠️ **Do not run destructive tests against the working containers.** The 2026-08-20 File Browser
  > walkthrough of "Spaarke Dev Container 2" showed **real working documents** — signed NDAs, Compose drafts,
  > matter files. Delete-container, permanent-delete, recycle-bin-purge, and restore paths must target a
  > **dedicated throwaway container** provisioned by the test fixture and torn down after, never a container
  > holding real content. Read-only and additive operations may run against the existing containers.

  > **Still to confirm**: whether the operator holds the **SharePoint Embedded Administrator** role. Lower
  > stakes than first assessed — per §4.2b, container-type list is ownership-filtered and Graph create needs
  > no admin role, so R2 does not depend on it. It is required only for tenant-wide listing and for the
  > PowerShell billing cmdlets, and billing is now provisioning's scope (§4.2d).
- `SecurityEvents.Read.All` grant on the app registration (Workstream B; Azure config).
- Standalone otherwise; BFF hot path → `/conflict-check` before each PR.
- Sequence `chatendpoints-decomposition-r1` after this project per [`projects/INDEX.md`](../INDEX.md).

Uses the repo INITIALIZE-ONLY pattern — worktree + task breakdown created at execution start
(`/design-to-spec` → `/project-pipeline`).
