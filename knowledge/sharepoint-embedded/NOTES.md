> ⚠️ STUB — senior engineer review pending
>
> Draft annotation in progress (2026-05-14, co-created with Ralph). Substantive content across most §1/§2 subsections; explicit TODOs marked where Spaarke production exposure hasn't happened yet or where specific values (IDs, config keys, file paths) need confirmation. Review and remove banner when satisfied.

# NOTES — `sharepoint-embedded`

Project-specific commentary on how Spaarke applies SharePoint Embedded (SPE) patterns. Annotate from real Spaarke project experience; don't fabricate.

Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

---

## 1. How this fits Spaarke's architecture

### Container provisioning model — tiered, not flat

Spaarke uses a **tiered container model**, not a single shared container:

| Tier | When provisioned | Purpose |
|---|---|---|
| **Business-unit container** | One per Dataverse business unit on BU creation | Default storage for that BU's documents — most matters and routine content land here |
| **Secure project / matter container** | Per-project / per-matter when **`sprk_issecure = true`** on the parent matter/project record | Dedicated container for sensitive engagements (e.g., M&A, regulated, conflicts-sensitive). Container = security boundary; deleting the container offboards the work cleanly. Documents and files created under the secure record are added to its dedicated container, not the BU container. |

This is **a refinement of** what `.claude/constraints/data.md` currently documents. That file says "MUST NOT assume per-entity containers — each environment has ONE default container" — accurate for the *default* fallback path (background jobs, create-new-entity flows without a parent BU), but doesn't describe the full BU + secure-project pattern. **TODO**: align `.claude/constraints/data.md` with this tiered model at next constraints refresh.

**Implication for the agent**: any code that hardcodes "the container" (singular) is suspect. Container resolution should always go through whatever lookup the BFF uses to find: (a) the secure container if the parent matter/project is flagged secure, (b) the BU container for the user's home BU, (c) the environment `DefaultContainerId` only as a last-resort fallback.

**Cross-refs**:
- `.claude/adr/ADR-005-flat-storage.md` — flat-storage rule (no folders inside containers; hierarchy in Dataverse). Holds across all tiers.
- `.claude/constraints/data.md` — current constraints file (needs update per above).
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` — facade exposing container CRUD via `CreateContainerAsync(Guid containerTypeId, ...)`.
- `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/ContainerEndpoints.cs` — admin endpoints for container provisioning (BU containers).
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs` — endpoint for secure-project containers (external access flow).

**Dataverse field pinning record → container**: the parent record (BU, matter, project, etc.) carries a **`sprk_containerid`** field that holds the SPE container ID (Drive ID format `b!...`). Documents created under that record inherit their container target via the parent. When `sprk_issecure = true` on a matter or project, its `sprk_containerid` points at the dedicated secure container; otherwise the document path resolves to the BU container's `sprk_containerid`.

---

### Exposing SPE content to declarative agents

The declarative-agent path to SPE content is via the `OneDriveAndSharePoint` capability with `items_by_url` or `items_by_sharepoint_ids` pointing at the SPE container's SharePoint-equivalent endpoint. **Spaarke is not yet wired to this path** as of 2026-05-14 — declarative agents currently use Foundry-side knowledge bases over AI Search (see `knowledge/azure-ai-search/`) for grounding.

**Future state**: when Spaarke ships a declarative agent grounded on matter documents:
- Bind the DA's SharePoint knowledge source to the relevant matter's SPE container URL
- Multi-tenant agents must scope per-client (tier-2 secure containers) — cannot share a DA across clients with overlapping content
- See `knowledge/declarative-agents/NOTES.md` §1 "Spaarke's declarative agent shape" for the full 3-source composition pattern

**Decision criteria** (DA-grounded vs application-code retrieval):
- DA-grounded on SPE: when the user-facing surface is Copilot Chat and the agent should ground on the user's matter content automatically. Requires Microsoft to GA the SP knowledge source binding to SPE container types.
- Application-code retrieval (AI Search index built from SPE upload pipeline): when Spaarke's BFF code needs deterministic, fast, permission-filtered retrieval (most current AI features).

_TODO: Once a first Spaarke DA grounds on SPE, document the exact manifest pattern (capability + `items_by_url` shape) and any limits hit (URL depth, items per binding, etc.)._

---

### MCP server + MCP app for SharePoint Embedded — forward-looking design

**Status as of 2026-05-14**: Spaarke does **not** expose SPE as an MCP server or have an MCP app for SPE content. This is a forward-looking design question. Two related but distinct surfaces:

1. **MCP server for SPE operations** — would expose tools like `open-document`, `list-matter-files`, `search-container`, `grant-share` to Foundry agents and declarative agents via the MCP protocol. This complements the Spaarke MCP server already planned for redline / compare / playbook operations (see `knowledge/mcp-apps/NOTES.md` §1).
2. **MCP app (widget) for SPE content** — would render lists, previews, or selection interfaces for SPE files inside Copilot Chat. Pattern reference: `knowledge/mcp-apps/trey-research/` (dashboard widget over data).

**Decision criteria**:
- **Build SPE MCP server when**: agents (Foundry or DA) need to invoke SPE operations imperatively. The existing BFF `SpeFileStore` endpoints can be re-exposed as MCP tools without a separate service — same auth, same ADR-007 facade. See `knowledge/mcp-tool-handler/` skill for the pattern.
- **Build SPE MCP app when**: a Spaarke skill needs rich UI over SPE results (e.g., a file-picker widget for an agent action). Use `knowledge/widget-design/` skill.
- **Don't build either if**: the existing BFF + Foundry SP KS path covers the need. Don't add MCP layers without a concrete agent use case.

_TODO: Confirm direction. Some near-term Spaarke skills (Redline, TabularReview) may need a "select target document from container" widget — that's the trigger for the MCP app. The MCP server itself may emerge organically as we expose Spaarke MCP tools that happen to be SPE-backed._

---

### Substrate semantic index — what's indexed and what isn't

_TODO: Confirm against `docs/learn-semantic-index.md` whether SPE container content lands in the **tenant-level semantic index** the same way SharePoint Online sites do. Spaarke's working assumption is yes (consuming tenant indexes the container partition), but verify with a test: upload a docx to an SPE container and search via Copilot in that tenant. Also document what the index does **NOT** expose directly to application code — index queries are only available through Copilot Chat and (preview) the Copilot Retrieval API._

---

### Copilot Retrieval API (pay-as-you-go preview)

_TODO: This is the only programmatic way to query the substrate index from application code. Capture: endpoint shape, billing model (per-query metering), how it scopes to a specific container or container type, and whether it respects Spaarke's row-level access (it should — index inherits container permissions). Until this is in GA and budget-approved, Spaarke uses Azure AI Search for application-grounded retrieval (see `knowledge/azure-ai-search/`)._

---

### BFF for upload and operations, Foundry SharePoint knowledge source for agent grounding

Two distinct paths interact with SPE content:

- **BFF (`SpeFileStore`)** — uploads, downloads, metadata writes, permission grants. App-only or OBO as appropriate. Path: `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`. This is the **only sanctioned way** application code touches SPE per ADR-007. The parallel AI Search ingestion (file → SPE + AI Search index) lives in the same BFF — see `knowledge/azure-ai-search/`.
- **Foundry SharePoint knowledge source (preview)** — agent-side grounding for declarative agents and Foundry Agent Service workflows. Configured at the agent level pointing at the SPE container type (see `docs/learn-knowledge-source.md`). **Spaarke is not yet wired to this path**; AI Search remains primary.

**Decision criteria** (when to add Foundry SPE KS vs. continue with AI Search):
- **Foundry SPE wins** on managed-substrate-index reuse (no separate ingestion), automatic permission inheritance from container ACLs, and lower operational burden.
- **AI Search wins** on chunking control (structure-aware via Document Intelligence layout model), hybrid + semantic reranking, fine-grained query-time permission filtering, and Redis-cached responses per ADR-009.
- **Current state**: Spaarke uses AI Search for all application-grounded retrieval. Foundry SPE KS is a forward option once the preview stabilizes and the cost model is clearer.

---

## 2. How we build with it

### Container type — programmatic create for the deployment pipeline

Container type creation is a **one-time-per-environment bootstrap** for Spaarke's owning tenant, plus per-consuming-tenant registration. Spaarke has scripts for both:

| Script | Purpose |
|---|---|
| `scripts/Create-NewContainerType.ps1` | Create a new container type in the owning tenant |
| `scripts/Create-ContainerType-PowerShell.ps1` | Alternative PowerShell-only flow (no Graph SDK) |
| `scripts/Check-ContainerType-Registration.ps1` | Verify registration in a consuming tenant |
| `scripts/Find-ContainerTypeOwner-AzCli.ps1` | Locate the owner of an existing container type |
| `scripts/Configure-ProductionAppSettings.ps1` | Write the container type ID into prod App Service settings |

**Deployment pipeline pattern**:
1. Container type created once in Spaarke owning tenant (manual or pipeline-bootstrap step).
2. Container type ID stored in environment config (see "Container type IDs" below).
3. Per-consuming-tenant registration runs as part of customer onboarding — Spaarke's automation invokes `Check-ContainerType-Registration.ps1` first, then registers if missing.
4. BFF reads the container type ID from `Graph:ContainerTypeId` config at startup.

**Where the container type IDs live**:
- **Dev / Test**: `appsettings.<env>.json` under **`Graph:ContainerTypeId`** (binding in `src/server/api/Sprk.Bff.Api/Configuration/GraphOptions.cs`).
- **Production**: Azure Key Vault reference, App Service binding via `scripts/Configure-ProductionAppSettings.ps1`.

**🚨 Critical gotcha**: **Standard container types cannot be deleted today** (only trial container types can). A botched production registration is permanent. Always test container type configuration in dev/trial first; never run `Create-NewContainerType.ps1` against production without owning-tenant verification.

_TODO: Confirm the Key Vault secret name convention used to bind the production `Graph:ContainerTypeId` value (so the agent can reference it when scaffolding new environments)._

---

### Container create — BU and secure project/matter/document flows

Spaarke provisions containers in two distinct flows, with different entry points:

| Flow | Entry point | Auth | Trigger |
|---|---|---|---|
| **Business-unit container** | `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/ContainerEndpoints.cs` | App-only | New BU created in Dataverse → admin/automation provisions BU container → record container ID on BU |
| **Secure project / matter / document container** | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs` | App-only (system) or OBO (user-initiated) | Project / matter flagged as "secure" → BFF provisions dedicated container → record container ID on the project/matter record |

**Code path under the hood** (both flows):
- BFF endpoint → `SpeFileStore.CreateContainerAsync(containerTypeId, displayName, description, ct)` → `ContainerOperations` → Graph `POST /storage/fileStorage/containers`.
- Container is created **inactive**; must activate by granting at least one owner permission (see "Common pitfalls").

**Spaarke convention**:
- Container `displayName` follows the convention: **`Spaarke-<BU-name>`** for BU containers, **`Spaarke-Secure-<matter-id>`** for secure-matter containers.
- `description` carries audit metadata (created-by, source flow).
- After create, BFF writes the resulting container ID (Drive ID format `b!...`) into the parent Dataverse record's **`sprk_containerid`** field (BU record or secure matter/project record).

_TODO: Document the post-create work that always happens: (a) activate container by granting owner permission, (b) write container ID to Dataverse, (c) emit audit log entry, (d) any default columns/metadata schema applied?_

---

### Adding users to container security — Dataverse users + external Power Pages contacts

Spaarke has **two identity sources** that grant access to SPE containers, with very different onboarding patterns:

| Identity source | Auth path | Onboarding |
|---|---|---|
| **Dataverse users** | Entra AD users with Dataverse access; OBO from BFF | Standard — user already authenticates via Entra; their `oid` is used in SPE grant |
| **External Power Pages SPA contacts** | Dataverse Contact records (NOT direct Entra), authenticating via external IdP (Entra External ID / Azure AD B2C) into a **Power Pages** site | **Invitation-based linking** — admin pre-creates Contact + Invitation; user redeems invitation code; redemption links external identity to the pre-existing Contact |

**Important scoping**: The external user surface is the **Power Pages custom web app** (deployed via `.claude/skills/power-page-deploy/`), NOT a SPA hosted inside a model-driven app. Two different surfaces — don't confuse them. The pattern below applies only to the Power Pages external workspace.

#### External Power Pages contact onboarding — invitation flow

Microsoft's supported pattern is **invitation-based linking** — never open self-registration as the source of truth. This is how external contacts get access:

1. **Admin pre-creates the Dataverse Contact** (model-driven app, or approved automation), with `emailaddress1` validated (the primary email is the only field invitation delivery uses — `emailaddress2`/`emailaddress3` are ignored).
2. **Admin assigns web roles** to the Contact (directly on the contact, or via the invitation in step 3).
3. **Admin creates an Invitation record** in Portal Management → Security → Contacts → Create Invitation (or directly in the Invitations table). Settings: Type=Single, optional expiry, optional account association, optional "Execute Workflow on Redeeming Contact".
4. **Admin sends the invitation** via the **built-in Send Invitation workflow** in Portal Management. **🚨 Do NOT convert this to a real-time workflow** — Microsoft explicitly states this is unsupported and breaks redemption.
5. **User receives email** containing the redemption URL + invitation code.
6. **User signs in** via the configured external IdP (Entra External ID or Azure AD B2C), then **redeems the invitation code** on the redemption page.
7. **Redemption links** the authenticated external identity to the pre-created Contact. An "Invite Redemption" activity is recorded against both the invitation and the contact.
8. Web roles + page permissions + Dataverse table permissions now grant the user access to the SPA. On future sign-ins, the user is recognized as the linked Contact.

#### Required Power Pages site settings

For the invitation-only pattern (no open self-signup), set these on the Power Pages site:

| Setting | Value | Why |
|---|---|---|
| `Authentication/Registration/Enabled` | `true` | Invitation redemption is part of the registration pipeline (still needed even with self-signup disabled) |
| `Authentication/Registration/ExternalLoginEnabled` | `true` | Users sign in via external IdP, not local |
| `Authentication/Registration/OpenRegistrationEnabled` | `false` | Block arbitrary self-signup |
| `Authentication/Registration/InvitationEnabled` | `true` | Allow invitation redemption |

#### Spaarke automation pattern

Microsoft's built-in Send Invitation workflow is the supported baseline. **Power Automate is not required** — the standard process is to create the invitation record and trigger `Flow > Send Invitation` from the contact or invitation record (see [`learn.microsoft.com/.../invite-contacts`](https://learn.microsoft.com/en-us/power-pages/security/invite-contacts)).

A typical Spaarke automation flow:

- **Trigger**: Dataverse Contact created or updated, where a custom field flag changes (e.g., `spaarke_portalapproved = Yes`).
- **Guards**: `emailaddress1` exists, contact is active, no existing open invitation, correct business unit/account, optional domain allowlist.
- **Actions**: create Invitation row → relate to invited contact → set Type=Single + expiry → assign target web roles → optionally set Execute Workflow on Redeeming Contact → trigger Send Invitation.
- **Escalation path**: if Spaarke needs greater control or richer audit, replace Power Automate with an Azure Function or BFF endpoint that orchestrates the same Dataverse + Send Invitation steps.

_TODO: confirm Spaarke's actual custom field name(s) on Contact (e.g., `spaarke_portalapproved`, or different) once implemented; document the canonical flow location (Power Automate solution vs. Azure Function vs. BFF endpoint)._

#### Document access inheritance via `sprk_externalrecordaccess` (dual-write model)

Spaarke uses a **dual-write access model** for secure matters and projects. **Dataverse is the source of truth**; SPE container permissions are derived from it and kept in sync. This is the central pattern for external-contact (and matter-scoped internal-user) access to SPE files.

**The model**:
- Each matter or project record carries a `sprk_issecure` flag. When `true`, a dedicated SPE container is provisioned (see §1 "Container provisioning model").
- The **`sprk_externalrecordaccess`** Dataverse table holds the authoritative list of users and contacts (both Dataverse users and Power Pages contacts) who have access to a specific secure matter/project.
- Access defined on `sprk_externalrecordaccess` **inherits down to Documents** — every `sprk_document` record under the secure matter/project is governed by the parent record's access list.
- Spaarke automation **also projects those grants to the SPE container's access rights** so file-level access on the substrate aligns with the Dataverse access list. Dataverse is the source of truth; SPE permissions are a projection.

**Add/remove flow**:
- **Add** a row to `sprk_externalrecordaccess` (user or contact, linked to a secure matter/project) → automation grants the corresponding SPE container permission.
- **Remove** a row → automation revokes the SPE container permission. File access ends — for Dataverse-surface Documents AND for `webUrl`-based Office opens (Word/Excel/PowerPoint).

**Implication for the SPE grant call**:
- Grant is **system-triggered**, not user-OBO. The BFF (or supporting automation) uses **app-only auth** to project the Dataverse access list onto SPE.
- `grantedToV2.user.id` depends on user type:
  - **Internal Dataverse user (Entra)**: Entra `oid` — well-known after authentication.
  - **External Power Pages contact**: external user's identity as Microsoft 365 sees it post-invitation-redemption — _TODO: confirm exact claim (likely the B2B guest `oid` if Entra External ID provisions a guest object on first sign-in, or the B2C `sub`). Resolve once the BFF projection code path is identified._

**Timing question — pre-redemption access rows**:
What if a contact is added to `sprk_externalrecordaccess` BEFORE they've redeemed their Power Pages invitation? Their Dataverse identity exists, but their external Microsoft 365 identity isn't yet provisioned. Plausible options:
- (a) **Defer + backfill**: queue SPE grants pending; backfill on first invitation redemption.
- (b) **Sharing link by email**: grant via email-matched sharing link (doesn't require external identity to pre-exist).
- (c) **Reject at write**: validate that the contact has redeemed before allowing the access row.

_TODO: confirm which option Spaarke implements. (a) is cleanest; (b) most flexible; (c) strictest._

**Code paths** (likely — to confirm):
- _TODO: identify the BFF endpoint, Azure Function, Service Bus job, or Dataverse plugin that subscribes to `sprk_externalrecordaccess` changes and projects them to SPE grants/revokes. Per ADR-002 (thin plugins), the plugin likely enqueues a job rather than calling Graph directly; the BFF job processor does the SPE work._
- _TODO: confirm whether Spaarke runs a periodic reconciliation worker that detects and corrects drift between `sprk_externalrecordaccess` and live SPE container ACLs (eventual-consistency safety net)._

**Why this matters for the agent**: any feature touching secure-matter access (adding a collaborator, revoking access on offboarding, audit reports) must go through `sprk_externalrecordaccess` — **never write SPE container permissions directly** outside the projection layer. Bypassing the source-of-truth table will drift Dataverse and SPE out of sync and is invisible until a permission audit catches it.

#### Critical gotchas (Power Pages external SPA)

- **Only `emailaddress1`** is used for invitation delivery. Document this in the admin UX so contacts are never created with email in field 2 or 3.
- **DO NOT convert Send Invitation workflow to real-time** — unsupported per Microsoft.
- **Email uniqueness matters** — duplicate contact emails cause identity matching issues during redemption. Admin tooling should enforce uniqueness before inviting.
- **Local login is deprecated** — Microsoft recommends external IdP (Azure AD B2C / Entra External ID) over local. Spaarke should standardize on external.
- **Three-layer auth alignment**: web roles + page permissions + Dataverse table permissions must all line up. Users can authenticate successfully and still hit 403s if any layer is misconfigured — reconcile all three on every web role.

#### Dataverse user permission grant pattern (for reference)

For Entra-AD-direct Dataverse users (the simpler case): standard OBO from BFF, SPE grant call is:

```json
{
  "roles": ["read" | "write" | "owner"],
  "grantedToV2": { "user": { "id": "<entra-oid>" } }
}
```

#### Code paths

- `src/server/api/Sprk.Bff.Api/Api/PermissionsEndpoints.cs` — general permission grant/revoke endpoints
- `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/ContainerTypePermissionEndpoints.cs` — container-type-level permissions (admin scope)
- `src/server/api/Sprk.Bff.Api/Endpoints/SpeAdmin/ContainerPermissionEndpoints.cs` — per-container permission management
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UserOperations.cs` — Graph-side permission implementation
- `src/server/api/Sprk.Bff.Api/Models/PermissionsModels.cs` — DTO shapes
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs` — secure-container provisioning + likely the grant entry point for invited external contacts

#### Critical security note

Container = security boundary. Permission changes are immediate on the SPE container ACL, but **propagation to the substrate semantic index** (and therefore to Copilot grounding) may lag. _TODO: document the observed lag from real testing — important for understanding why a newly-shared file may not appear in Copilot results for several minutes._

#### Cross-references

- Microsoft: [Power Pages — Invite contacts](https://learn.microsoft.com/en-us/power-pages/security/invite-contacts)
- Spaarke skill: `.claude/skills/power-page-deploy/` — Power Pages SPA deployment
- Spaarke code: external-workspace SPA source (path TBD — likely `src/client/external-spa/` or `src/solutions/<workspace>/`)

_TODO: Permission scope vocabulary in Spaarke — what role grants are typical? `read` for SPA contacts viewing a matter? `write` for collaborators? `owner` only for the system?_

---

### Document metadata — what's available, how to set programmatically

Two layers of metadata sit on SPE content:

| Layer | Where stored | Set how | Use |
|---|---|---|---|
| **Drive item native metadata** | On the SPE driveItem itself | Graph `PATCH /drives/{driveId}/items/{itemId}` with field updates | name, createdBy, lastModifiedBy, eTag, system fields |
| **Container columns (custom metadata)** | Per-container schema (like SP list columns) | `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/ContainerColumnEndpoints.cs` — define columns on container; set values per item | Spaarke-specific structured metadata (matter ID, sensitivity, classification, etc.) |
| **Dataverse `sprk_document` record** | Dataverse | BFF writes on upload | Canonical Spaarke metadata; links the SPE file to matter / parent records |

**Spaarke pattern**:
- **Native Graph metadata** is read-only for most purposes — derived from the file itself.
- **Container columns** are the right surface for Spaarke-specific structured metadata that should travel with the file (and be searchable via the substrate index). Define columns once per container type via `ContainerColumnEndpoints`.
- **Dataverse `sprk_document`** carries the canonical record — `sprk_graphitemid`, `sprk_graphdriveid`, `sprk_filepath`, plus all the Spaarke business metadata.

**Required Dataverse fields on every `sprk_document`** (per `.claude/constraints/data.md`):
- `sprk_graphitemid` — Graph drive item ID from SPE upload result
- `sprk_graphdriveid` — Graph drive ID from SPE upload result
- `sprk_filepath` — flat path within the container

**Rule (from constraints/data.md)**: NEVER hardcode or guess these values — always populate from the SPE upload result.

_TODO: Document the standard set of container columns Spaarke defines (matter-id, sensitivity-label, classification, retention-policy?). Cross-reference to data-model docs if they exist._

_TODO: How does column metadata flow to the substrate semantic index — automatic? Configurable? Worth confirming for retrieval scenarios._

---

### Permission scopes — application vs. delegated

`SpeFileStore` exposes both auth modes:

| Operation type | Auth mode | Method shape |
|---|---|---|
| **Container provisioning, system uploads, background indexing** | **App-only** (`FileStorageContainer.Selected` application permission) | `UploadSmallAsync(driveId, path, content, ct)` — no `HttpContext` parameter |
| **End-user operations** (user-initiated upload, download, share) | **Delegated / OBO** (user's token, on-behalf-of) | `DownloadFileAsUserAsync(HttpContext ctx, driveId, itemId, ct)` — takes `HttpContext` |

**Spaarke divergence from boilerplate**: the sample in `knowledge/sharepoint-embedded/samples/container-crud-typescript/containers.ts` (lines 22–33) auto-falls-back from CCA (client credentials) to OBO based on availability. **This is not the Spaarke pattern**. Spaarke chooses explicitly based on operation:
- Container CRUD, BU/matter provisioning → **app-only** (no user context at scale-out time)
- Any user-initiated read/write → **OBO** (user's token, user's ACL evaluation)

The choice is encoded in the method signature — methods with `HttpContext ctx` are OBO; methods without are app-only.

**Cross-refs**:
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` — both overloads visible side-by-side.
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — the factory that produces either-flavor `GraphServiceClient`.
- ADR-007: **no `GraphServiceClient` injected anywhere above `SpeFileStore`**.

---

### Auth for preview and file open — Word for Web vs. desktop

Opening an SPE document in Office has different auth shapes depending on **how** the user opens it. All paths must result in the user's identity reaching SPE (otherwise the Copilot grounding context is lost — see next subsection).

| Open path | Auth | Notes |
|---|---|---|
| **Word for Web (browser)** | User's M365 session (SSO via Entra) directly to SPE substrate | User must have a permission grant on the container (see "Adding users"); webUrl-based navigation suffices |
| **Word desktop** | User's M365 session via Office Identity API → SPE | Same permission requirement; Office Click-to-Run handles auth transparently if user is signed in |
| **In-browser preview** (Spaarke-rendered, not Office) | Spaarke BFF OBO → SPE → returns thumbnail/preview asset | Different code path; Spaarke renders preview itself, doesn't hand off to Office |

**Spaarke convention** (per Ralph 2026-05-14):
- File open in Word/Excel/PowerPoint (both Web and desktop) → `webUrl` flow (see next subsection)
- In-place preview (Spaarke's own viewer for the document record page) → BFF OBO path

_TODO: Document the exact code path for in-place preview — likely under `src/client/code-pages/SpeDocumentViewer/` or related PCF. What endpoint does the viewer call? Does it stream from SPE through the BFF, or get a direct preview URL from Graph?_

_TODO: Confirm: are there cases where Spaarke needs to mediate the user's auth to SPE for desktop Office (e.g., if the user isn't already signed into M365)? Office add-in scenarios may need this._

---

### webUrl-based document opens (Word Copilot can "see" the file)

Spaarke opens documents in Office (Word / Excel / PowerPoint Web/Desktop) via the container drive item's `webUrl` — **never** by downloading and re-uploading.

**Flow** (two-component pattern, per Ralph 2026-05-14):
1. **PCF trigger** on the Dataverse document record form (button or click handler).
2. PCF invokes a **Code Page** (React 18 standalone dialog) at `src/client/code-pages/SpeDocumentViewer/` — the Code Page resolves the `webUrl` from the drive item and issues `Xrm.Navigation.openUrl(webUrl)` (or equivalent) to launch Office.

**The Graph call** the Code Page (or BFF on its behalf) makes:
```
GET /drives/{driveId}/items/{itemId}
→ webUrl property
```

**Why Word Copilot can "see" the file**: when the document opens via `webUrl` in Office Web (or desktop signed into M365), the file's location in the SPE substrate is **the same** location the substrate semantic index indexed it at. Copilot's grounding pipeline picks up the SPE-stored file as if it were a regular SharePoint document — same Copilot ribbon, same grounding, same citation pattern.

**What breaks this** (anti-patterns):
- **Download-and-reopen**: file is now in OneDrive or local disk, not the SPE substrate. Copilot grounds on the *copy*, not the canonical document. Citations point at the wrong place; co-authoring breaks; storage doubles.
- **Streaming through Spaarke's BFF preview**: useful for read-only thumbnail but does NOT give Word Copilot context — the user is viewing through Spaarke's viewer, not Office.
- **Wrong identity at open time**: if the user opens via a service-account-rendered URL, Copilot grounds against that service account, not the user.

**Requirement chain** for Word Copilot grounding to work:
1. File lives in an SPE container that the user has read access to (permission grant exists)
2. Container content is indexed in the consuming tenant's substrate semantic index (auto for SPE)
3. User opens via `webUrl` with their own M365 identity
4. User's Copilot license is active for the tenant

If any of these break, Copilot grounding fails silently (it returns no results from the document, often without an error).

**Cross-refs**:
- `src/client/code-pages/SpeDocumentViewer/` — the Code Page that handles the webUrl flow.
- _TODO: confirm the PCF that triggers the Code Page — likely on the `sprk_document` form. Add path._

---

### Word add-in upload flow

Spaarke ships a Word add-in for document creation and SPE upload. The flow is:

1. User opens Word, invokes the Spaarke add-in taskpane.
2. Taskpane reads the current Word document via `WordHostAdapter` (`src/client/office-addins/word/WordHostAdapter.ts`).
3. User selects a target (matter, project, BU) and metadata in the taskpane UI.
4. Taskpane calls Spaarke BFF upload endpoint with the document content + target.
5. **BFF executes "SPE First, Dataverse Second"**:
   - Upload to SPE via `SpeFileStore.UploadSmallAsync` or `CreateUploadSessionAsync` + chunked upload (large files)
   - On success, capture `sprk_graphitemid`, `sprk_graphdriveid`, `sprk_filepath`
   - Create `sprk_document` Dataverse record with those values populated
6. Taskpane shows success state with link to the new Dataverse record.

**Code paths**:
- `src/client/office-addins/word/WordHostAdapter.ts` — Word-specific host adapter
- `src/client/office-addins/shared/adapters/WordAdapter.ts` — shared adapter base
- `src/client/office-addins/word/taskpane/` — taskpane UI
- BFF upload endpoint: _TODO: confirm exact route (likely under `Sprk.Bff.Api/Api/`)_

**Critical rule** (per `.claude/constraints/data.md`):
- "SPE First, Dataverse Second" — never create the `sprk_document` before the SPE upload completes. Failed uploads must NOT leave orphan Dataverse records.

_TODO: Document the chunked upload pattern Spaarke uses for documents >4 MB (Graph upload session approach). Confirm timeout / retry handling._

_TODO: How does the Word add-in handle the case where the user creates a document fresh (no existing file) vs. modifies an existing Spaarke document? Two flows or one?_

---

### Email-to-SPE upload flow (custom `sprk_communication` pattern)

**Spaarke does NOT use the out-of-the-box Dataverse `email` entity, and does NOT use Exchange Online server-side sync** for email ingestion. All email-to-SPE flows go through a Spaarke-specific architecture:

**The model**:
- **`sprk_communication`** is a custom Dataverse entity that handles communications. It has a `type` field; `type = Email` for email records.
- A **custom Spaarke save component** is the canonical path for landing an email (and its attachments) in SPE. It is called by:
  - The **Outlook add-in** (user-initiated, individual emails)
  - Any other Spaarke flow that needs to save an email to SPE — current and future
- The email body is stored as a **`.eml`** file in SPE (portable format, not `.msg`).
- Attachments are saved separately to SPE alongside the body, all parented to the same `sprk_communication`.

**Flow (via Outlook add-in)**:
1. User receives an email, invokes the Spaarke Outlook add-in taskpane.
2. Taskpane reads the current email via `OutlookHostAdapter` (`src/client/office-addins/outlook/OutlookHostAdapter.ts`):
   - Email body + headers
   - Attachments (file blobs)
3. User selects target matter/project (and any categorization) in the taskpane UI.
4. Taskpane calls Spaarke BFF, which routes to the **custom email save component**.
5. The save component executes "SPE First, Dataverse Second" for the whole bundle:
   - **Email body** → uploaded as `.eml` to the target container → `sprk_communication` (type=Email) record created with `sprk_graphitemid`, `sprk_graphdriveid`, `sprk_filepath` populated.
   - **Each attachment** → uploaded separately to SPE → linked record created and associated to the `sprk_communication`.
   - Whole bundle linked to the target matter/project via Dataverse association.
6. Taskpane shows success state with link to the new `sprk_communication` record.

**No automatic ingestion (intentional)**:
Spaarke has **no catch-all email ingestion path** — no Exchange Online connector writing to a Spaarke mailbox, no Power Automate watching a shared inbox, no scheduled poller. Every email enters Spaarke via **explicit user action** (Outlook add-in or future user-facing flows that call the same save component). This is by design — preserves user intent ("save THIS email to THAT matter") and avoids automatic-misclassification scenarios.

**🚨 Critical**: if a server-side email ingestion path is added later, it MUST route through the same custom save component — never call SPE directly, never create `sprk_communication` directly, never resurrect the OOB `email` entity or server-side sync.

**Code paths**:
- `src/client/office-addins/outlook/OutlookHostAdapter.ts` — Outlook-specific host adapter (reads email/attachments via Office.js)
- `src/client/office-addins/outlook/taskpane/` — taskpane UI
- `src/client/office-addins/outlook/commands/` — ribbon commands
- Custom email save component (BFF-side): _TODO: confirm exact path. Likely under `src/server/api/Sprk.Bff.Api/` — search for `sprk_communication` or `EmailToSpe` to locate._

**Critical rules**:
- **`.eml` format** for the email body — portable, not Outlook-specific.
- **SPE First, Dataverse Second** for the whole bundle (body + each attachment).
- **No direct `sprk_document` creation** for email content — go through the `sprk_communication` save component, which decides whether to create `sprk_communication`, attachment records, or both.
- **No OOB `email` entity. No server-side sync.** Explicitly out of scope.

_TODO: Confirm — do attachments become `sprk_document` records linked to the parent `sprk_communication`, or do they also become `sprk_communication` records (e.g., type=Attachment)? Document the convention so the agent doesn't create the wrong shape._

_TODO: Document the Outlook 1.8+ Mailbox API requirement set dependency and any tenant-side configuration needed (per `OutlookHostAdapter.ts` header)._

_TODO: Attachment dedup policy — if the same attachment was already saved (different email, same file), does Spaarke detect and link to the existing record, or create a new one?_

---

### Common pitfalls

Production-relevant constraints — verify against current code, but these are real:

- **Upload ordering — "SPE First, Dataverse Second"** (per `.claude/constraints/data.md`). Upload to SPE **before** creating the `sprk_document` record. Reversing the order causes orphan Dataverse records when the upload fails. Always populate `sprk_graphitemid`, `sprk_graphdriveid`, and `sprk_filepath` from the SPE upload result — never guess or hardcode.
- **`DefaultContainerId` is Drive ID format**, not raw GUID. Format is base64-encoded `b!...`. Wiring the wrong format will pass type checks and fail at runtime with a Graph 400.
- **Beta endpoint usage** — `/beta/storage/fileStorage/containers` is the working endpoint as of curation date; some operations are not yet on v1.0. Verify before relying on v1.0.
- **Container "activation" step** — newly created containers are inactive until at least one owner permission is granted (see `samples/powershell/CreateContainer.ps1`). The boilerplate-aspnet `ActivateContainer` method exists but isn't always called explicitly. Spaarke's provisioning flow must ensure activation; otherwise downstream upload calls fail with permission errors.
- **SPO token vs. Graph token isolation** — the SP-scoped (`Container.Selected`) token must be acquired separately from Graph tokens (see `samples/embedded-chat/ChatAuthProvider.ts`). Mixing scopes in one request fails.
- **Container type registration must happen in every consuming tenant** before containers can be created there. Easy to forget when onboarding a new customer environment — add a tenant-onboarding checklist item.
- **Standard container types cannot be deleted** today (only trial types). Production registration is permanent.
- **Permission propagation lag** — substrate semantic index may take time to reflect new permission grants; Copilot grounding for a newly-shared file may fail briefly. SLA / expected lag: _TODO_.

---

### Cross-references in this knowledge base

- Companion topic: `knowledge/azure-ai-search/` — the parallel AI Search ingestion path Spaarke uses for application retrieval.
- Companion topic: `knowledge/foundry-iq/` — the alternative agent-grounding path (Foundry SP KS), not yet wired.
- Companion topic: `knowledge/declarative-agents/` — DA composition pattern, including SPE knowledge source binding.
- Companion topic: `knowledge/mcp-apps/` — if a widget surfaces SPE file content, `useMcpApp` host-bridge governs the data flow.
- Companion topic: `knowledge/mcp-tool-handler/` — if exposing SPE operations as MCP tools to Foundry agents.
- Skill that loads this NOTES.md: `.claude/skills/spe-integration/SKILL.md` — fires on "SharePoint Embedded", "SPE container", "container type", "webUrl document open".
