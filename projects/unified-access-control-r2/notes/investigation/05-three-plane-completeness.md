# Investigation 05 — Three-Plane Completeness for the `contact` Principal

> **Project**: unified-access-control-r2 · **Date**: 2026-08-20 · **Scope**: Does the documented three-plane access model (`docs/architecture/uac-access-control.md`) actually hold end-to-end for a `contact` principal? Where does it break?
> **Method**: code-grounded trace of every enforcement seam; all claims cited `path:line` against the worktree `c:/code_files/spaarke-wt-unified-access-control-r2`.

---

## Summary

The three-plane model **does not hold** for a contact principal. What exists in code is a **one-plane model with a broker in front of it**: all contact access (Dataverse rows AND file bytes) is enforced by a single in-BFF grant-set check (`sprk_externalrecordaccess` → `CallerPrincipal`), executed app-only. Plane 2 (SPE container membership) is **vestigial** — the grant path never touches SPE (`GrantExternalAccessEndpoint.cs:116-117`), and `SpeContainerMembershipService.GrantMembershipAsync` has **zero callers**. Plane 3 (AI Search) is **unreachable** by a contact — no `/api/ai/*` route accepts a CIAM token — and the trimming machinery that exists for workforce callers (`privilege_group_ids`) is never populated at index time, making it a pass-through even internally. The doc's core sentence — "a single grant orchestrates all three [planes]" — is **refuted**: a grant writes exactly one Dataverse row and invalidates one cache key; it orchestrates one plane.

The deliberate broker-only redesign (ADR-028 A1) is actually a *defensible* architecture — one enforcement point is easier to make correct than three synchronized ones — but the doc still describes the abandoned three-plane orchestration, and several real holes exist in the one plane that remains (unscoped `PATCH /todos/{id}`, an authorization-free app-only download route on the workforce surface, an operation-string bug that makes the only two `DocumentAuthorizationFilter` attachments deny everyone, and unrevocable anonymous share links).

---

## Per-Plane Enforcement Table (contact vs systemuser)

| Plane | Contact (CIAM) — what enforces | Fail-closed? | Actually wired? | Systemuser — what enforces | Fail-closed? | Actually wired? |
|---|---|---|---|---|---|---|
| **1. Dataverse rows** | BFF-only: `CallerPrincipal` grant set (`CallerPrincipalResolver.cs:303-341`) + per-handler `HasProjectAccess` (`ExternalProjectDataEndpoints.cs:148`, 165, 197, 258, 276, 303, 320) + Tier-2 FetchXML injection (`ExternalModuleDataEndpoints.cs:202`) + record∈set gate (`ExternalModuleDataEndpoints.cs:267`). Dataverse row security does NOT apply — every read is app-only (`ExternalDataService.cs:599-626`, `ExternalParticipationService.cs:615-639`) | Yes, except `PATCH /todos/{id}` (unscoped write, `ExternalProjectDataEndpoints.cs:328-345`) and empty-grant callers get 200-empty rather than 403 | **Yes** — group filter at `ExternalAccessEndpoints.cs:54-57` | Native Dataverse security in MDA; on the shared `/api/v1/external` surface: accessible-set = ADR-034 membership ∪ own-contact grants (`AccessibleRecordSetService.cs:184-241`) | Yes | Yes |
| **2. SPE files** | **Broker-only.** Single route: `GET /api/v1/external/projects/{id}/documents/{documentId}/content` — authz-before-stream: project∈grant-set + document→project parentage check, then app-only `DownloadFileAsync` (`ExternalProjectDataEndpoints.cs:195-224`). Contact is **never added to the SPE container** (`GrantExternalAccessEndpoint.cs:116-117`; `GrantMembershipAsync` has no callers — verified by grep, only `ProjectClosureEndpoint.cs:127` calls the removal API). `DocumentAuthorizationFilter` never runs on contact routes and could not pass for a contact anyway (no systemuser row → `AccessRights.None`, `DataverseAccessDataSource.cs:185-198`) | Yes on the one route that exists | **Yes** for project-child docs; **no route at all** for matter/WA-child docs | OBO Graph (SPE enforces the user's own container membership) on `/api/documents/*` preview/content/office (`FileAccessEndpoints.cs:184`, 376, 441) — but the same group exposes an **unfiltered app-only** `/api/documents/{id}/download` (`FileAccessEndpoints.cs:101-109`, 865); the UAC-checked twin `/api/v1/documents/{id}/download` is dead — see gap G-3 | Partially — see gaps G-2/G-3 | Partially |
| **3. AI Search** | **Nothing.** No `/api/ai/*` group accepts the Ciam scheme (all default-scheme; `AuthorizationModule.cs:33-36`, 278-286 — only `CiamExternal`/`ExternalCollaboration` name the Ciam scheme, and neither is used on any AI route). No search route exists under `/api/v1/external` (`ExternalAccessEndpoints.cs:46-101`). No code anywhere builds a search filter from participations (no caller of `GetGrantSetAsync`/`GetParticipationsAsync` outside `Infrastructure/ExternalAccess` + `AccessibleRecordSetService` — verified by grep) | Fail-closed by absence | **Not wired** | Query-time: `tenantId` filter (`RagService.cs:1101`) + `privilege_group_ids` filter (`RagService.cs:1241-1242`, `PrivilegeFilterBuilder.cs:43-67`) + per-document `AiAuthorizationFilter` on analysis routes (`AiAuthorizationFilter.cs:50-118`). **Index-time population of `privilege_group_ids` does not exist** — always written empty (`RecordSyncJob.cs:557`, `KnowledgeDocument.cs:258-260` default `new List<string>()`, nulled for session index at `RagIndexingPipeline.cs:475`) → every doc is "public" → the privilege filter passes everything | Empty-group callers still see all "public" docs = **everything** (`PrivilegeFilterBuilder.cs:47-54`) — not fail-closed in practice | Query filter wired; trimming data unbuilt |

---

## Plane-by-Plane Trace

### Plane 1 — Dataverse records

**How a contact reads Dataverse.** A CIAM token authenticates only on the `ExternalCollaboration` policy (`AuthorizationModule.cs:278-286`), which is applied solely to the `/api/v1/external` group (`ExternalAccessEndpoints.cs:54-57`). The group-level `CallerPrincipalAuthorizationFilter` (`CallerPrincipalAuthorizationFilter.cs:29-50`) resolves the token via `CiamContactPrincipalStrategy`: oid → `Contact.sprk_externalobjectid` (email first-login fallback with anti-hijack, `ExternalParticipationService.cs:211-258`), then loads the grant set — direct grants + **org-inherited grants** (Term 3, `ExternalParticipationService.cs:450-464`) — onto `CallerPrincipal` (`CallerPrincipalResolver.cs:303-341`). Every downstream read is executed **app-only** with a managed-identity token (`ExternalDataService.cs:599-626`), so **Dataverse row-level security is inert on this path; the BFF's own filter is the only boundary.** Confirmed.

**Scope-filtered routes** (all in `ExternalProjectDataEndpoints.cs`): `/projects` (grant-list projection, :131), `/projects/{id}` (:148), `/projects/{id}/documents` (:165), `/projects/{id}/documents/{docId}/content` (:197+208), `/projects/{id}/todos` (:258), `POST /projects/{id}/todos` (:276 + Create-level check :281-284), `/projects/{id}/contacts` (:303), `/projects/{id}/organizations` (:320). The module read seam (`ExternalModuleDataEndpoints.cs`) is scope-filtered twice: server-side FetchXML `<filter type='or'>` injection (`:202`, `Tier2ScopeFilterInjector.cs:48-80`) + in-memory `ScopeRows` defense-in-depth (`:207-208`, `ExternalModuleRegistry.cs:153-187`), with a single-entity/no-`link-entity` guard against join-based over-read (`ExternalModuleDataEndpoints.cs:141-172`). Metadata/savedquery reads are schema-only and module-gated (`:317-321`, 363-371, 399-403).

**Routes NOT scope-filtered:**
1. **`PATCH /api/v1/external/todos/{id}`** — updates any `sprk_todo` by GUID with **no ownership or project check**; the code comment admits it (`ExternalProjectDataEndpoints.cs:328-345`, write executes app-only at `ExternalDataService.cs:327-367`). Any resolvable CIAM contact — including one with **zero grants** (empty grant set does not deny; `CallerPrincipalResolver.cs:307-341` denies only unresolvable contacts) — can modify name/notes/due-date/status of ANY to-do in the tenant, including internal users' personal to-dos.
2. **Attribute-level exposure on the module fetch seam** — the caller controls the FetchXML `$select`-equivalent; any column of a registered module entity (`sprk_project`, `sprk_document`, `sprk_invoice`, `sprk_workassignment`, `sprk_matter`) on an accessible row is readable. Row scoping is enforced; **column scoping does not exist**.

**Writes.** A contact can create `sprk_todo` (scoped, Create-level gated) and update any `sprk_todo` (unscoped — above). No other Dataverse write surface exists for a contact. Bounds: request DTO field list only (`ExternalDataService.cs:272-286`, 332-340).

### Plane 2 — SPE files

**Is a contact added to the container?** **No.** The grant endpoint writes only the Dataverse row and returns `SpeContainerMembershipGranted: false` by design ("Broker-only … no synthetic SPE container permission is written on grant", `GrantExternalAccessEndpoint.cs:17-19`, 116-117). `SpeContainerMembershipService.GrantMembershipAsync` (`SpeContainerMembershipService.cs:48`) has **no callers anywhere in `src/`** (grep-verified); only `RemoveAllExternalMembersAsync` is used, by project closure (`ProjectClosureEndpoint.cs:127`). The role map (`ViewOnly→reader`, `Collaborate/FullAccess→writer`, `SpeContainerMembershipService.cs:24-29`) is dead configuration.

**What a contact actually calls.** Exactly one file route: `GET /api/v1/external/projects/{id}/documents/{documentId}/content` (`ExternalProjectDataEndpoints.cs:67`). Enforcement order is correct and fail-closed: (1) project ∈ grant set (:197); (2) document's `_sprk_project_value` must equal the requested project — mismatch or missing doc is a uniform 403 with no existence leak (:208-216, backed by `ExternalDataService.GetDocumentProjectAndNameAsync`, `ExternalDataService.cs:208-219`); only then (3) SPE pointer resolution (`DocumentStorageResolver.cs:25-108`) and app-only `DownloadFileAsync` (:221-224) — explicitly NOT the OBO path. Graph pointers never reach the client. **A contact cannot reach a document whose parent project they lack** — parentage, not container co-location, is the boundary, so documents of different projects sharing a container do not leak.

**Filter applicability.** `DocumentAuthorizationFilter` is attached at only two places in the whole BFF (`DataverseDocumentsEndpoints.cs:443`, `FileAccessEndpoints.cs:118`) — neither on a contact-reachable route. It could never authorize a contact anyway: it feeds `AuthorizationService` → `DataverseAccessDataSource.LookupDataverseUserIdAsync` (oid → `systemusers.azureactivedirectoryobjectid`, `DataverseAccessDataSource.cs:256-294`); a principal with no systemuser row gets `AccessRights.None` (`:186-198`) → deny. Fail-closed, but structurally systemuser-only.

**Latent defects found on this plane (workforce side, relevant to UAC-r2):**
- **G-3**: The operation string `"read"` passed at both filter attachments is **not a key** in `OperationAccessPolicy._operationRequirements` (`OperationAccessPolicy.cs:25-149` — Graph-style + legacy names only; no `"read"`), and `OperationAccessRule` denies unknown operations (`OperationAccessRule.cs:35-46`). The only registered rule is `OperationAccessRule` (`SpaarkeCore.cs:72`). Net effect: the UAC-checked download `/api/v1/documents/{id}/download` and `eml-render` **deny every caller** with `sdap.access.deny.unknown_operation` — which explains why the *unchecked* route survives as the working path.
- **G-2**: `GET /api/documents/{documentId}/download` streams **app-only** with no per-document authorization — group has only `.RequireAuthorization()` (`FileAccessEndpoints.cs:30`, 101-109, 865-868). Any workforce-authenticated principal (including a contact-only workforce guest with zero grants) can download **any** document by GUID.
- Per-contact SPE removal on revoke matches the contact **GUID as a substring of the UPN** (`RevokeExternalAccessEndpoint.cs:222-235`) — never matches a real UPN; effectively a no-op (moot under broker-only, but misleading).
- `POST /api/documents/{id}/share-link` creates **anonymous, non-expiring** "Anyone" view links (`FileAccessEndpoints.cs:640-642`) — a permanent, grant-system-external access channel (see revocation section).

### Plane 3 — AI Search

**Contact reachability: none.** All `/api/ai/*` groups authenticate on the default (workforce) scheme; the CIAM scheme is bound only to `CiamExternal` and `ExternalCollaboration` policies (`AuthorizationModule.cs:266-286`), used nowhere on AI routes. The `/api/v1/external` surface maps no search/AI endpoint (`ExternalAccessEndpoints.cs:46-101`). So Plane 3 for a contact is fail-closed **by absence**, not by trimming.

**The trimming that exists (workforce)** and why it wouldn't serve contacts:
- Query-time: unconditional `tenantId` (`RagService.cs:1101`), knowledge-source/tag/parent-entity filters, and the `privilege_group_ids` filter (`RagService.cs:1238-1242`). The filter shape ORs the caller's **Azure AD group ids** with a public clause `not privilege_group_ids/any()` (`PrivilegeFilterBuilder.cs:43-67`). Groups are resolved from the workforce JWT or Graph OBO `/me/memberOf` (`PrivilegeGroupResolver.cs:97-116`) — a CIAM contact has neither workforce groups nor an OBO-exchangeable token.
- **Index-time population: unbuilt.** `privilege_group_ids` is written as an empty list by the record-sync indexer (`RecordSyncJob.cs:557`), defaults empty on `KnowledgeDocument` (`KnowledgeDocument.cs:258-260`), and is nulled for the session-files schema (`RagIndexingPipeline.cs:469-475`). No code assigns real group ids. Consequence: **every indexed document is "public"**, so the empty-group "fail-closed" case (`PrivilegeFilterBuilder.cs:50-54`) actually returns *all* documents in the tenant. The mechanism is fail-closed on paper, pass-through in practice.
- Per-document `AiAuthorizationFilter`/`AiAuthorizationService` requires oid→systemuser + OBO (`AiAuthorizationService.cs:74-85`, 163-207; `DataverseAccessDataSource.cs:185-198`) — deny for contacts.
- Nothing filters on the contact grant set: zero references to `GetGrantSetAsync`/participations under `Services/Ai/**` (grep-verified). The doc's "BFF constructs filter from contact's participation records" (`uac-access-control.md:34`) describes code that does not exist.

---

## Single-Grant Claim — Verdict: **REFUTED**

Doc claim (`docs/architecture/uac-access-control.md:36`): *"Granting external access = (1) create `sprk_externalrecordaccess` record + (2) assign web role + (3) add Contact Entra UPN to SPE container via Graph."*

Code reality — a grant does exactly one plane:
1. Creates the `sprk_externalrecordaccess` row (`GrantExternalAccessEndpoint.cs:146-147`) and invalidates the per-contact Redis participation cache (`:167-172`). **That's all.**
2. **No web role** — the platform is not Power Pages; there is no web-role code anywhere on the grant path. `RevokeAccessResponse` hard-codes `WebRoleRemoved: false` (`RevokeExternalAccessEndpoint.cs:182`).
3. **No SPE membership** — explicitly by design (`GrantExternalAccessEndpoint.cs:17-19`, 116-117); `GrantMembershipAsync` has no callers.

There is also **no manual/operator step filling the gap** — none is needed, because Planes 2 and 3 are not independently granted for contacts: file access is derived at request time from the same Dataverse grant (broker download), and search access does not exist. The honest statement is: **one grant governs one enforcement point, which fronts two consumption surfaces (rows + file bytes); the third surface is absent.** `invite-and-grant` composes CIAM user provisioning + the same single-row grant (`InviteAndGrantExternalUserEndpoint.cs:120`).

---

## Revocation Cross-Plane Analysis

**Revoke** (`RevokeExternalAccessEndpoint.cs`): deactivates the grant row (:96-97), best-effort SPE permission removal *only if the caller passes a `ContainerId`* (:122-141) using the broken GUID-in-UPN match (:222-235), invalidates the contact's cache (:161-166). Because contact access on both effective surfaces is recomputed from the grant set per request, **revocation is single-step and complete for contacts**: worst-case latency = the 60s participation-cache TTL (`ExternalParticipationService.cs:18`), typically immediate (cache-version drift fixed by task 073 #7, `:22-34`). Org-grant revokes skip per-member invalidation and ride the 60s TTL (`RevokeExternalAccessEndpoint.cs:153-159`).

**Where access can orphan:**
1. **Anonymous share links** — `driveitem.createlink` view/anonymous, non-expiring (`FileAccessEndpoints.cs:640-642`); nothing records or revokes them on grant revocation. A revoked contact (or anyone with the URL) keeps file access indefinitely. **The largest orphan channel.**
2. **Legacy SPE container permissions** — any contact historically added to a container stays there unless close-project runs *with* a `containerId` (`ProjectClosureEndpoint.cs:124-133`); the per-contact revoke's matcher never matches (above).
3. **Cascade revoke on close-project is likely broken**: the query `$select`s `_sprk_contactid_value` (`ProjectClosureEndpoint.cs:181`, row type :315), but the grant table's contact FK projects as `_sprk_contact_value` — verified live per `ExternalParticipationService.cs:399-404` (and the filter side of this exact bug class was already fixed once, `ProjectClosureEndpoint.cs:155-163`). An invalid `$select` property 400s in Dataverse (→ throw → 500) or, if tolerated, yields all-null ContactIds → rows dropped at :190 → "Nothing to revoke". Either way, closure does not cascade.
4. **Redis-down window**: cache invalidation failure is non-fatal by design; staleness bounded at 60s (`ExternalParticipationService.cs:174-180`).
5. **Workforce plane**: a revoked contact who can also authenticate as a workforce user retains whatever ADR-034 membership grants them, at blanket Collaborate level (below) — revoking the external grant does not touch membership.

---

## Child-Access Implications per Plane (UAC-r2: "members of a parent can access the parent's children")

| Plane | What "child access" means | What exists | What's missing |
|---|---|---|---|
| **1. Dataverse** | Child rows (document/invoice/todo/…) readable when they roll up to an accessible root | Built for **list reads**: polymorphic OR scope-dimensions per child module — `sprk_document` rolls up via `sprk_project`/`sprk_matter`/`sprk_workassignment`, `sprk_invoice` via matter/project (`ExternalAccessModule.cs:166-190`); server-side filter injection (`Tier2ScopeFilterInjector.cs:48-80`). Project-child reads also on the typed routes (`ExternalProjectDataEndpoints.cs`) | Child **single-record** reads fail closed by design (`ExternalModuleRegistry.cs:126-127` — a child's own id is never in a parent-id set), so record-open flows need a "resolve parent then check" step; only document/invoice are registered as child modules; todos are exposed project-only (`ExternalDataService.cs:237-247`); no grandchild (root→child→grandchild) traversal |
| **2. SPE files** | Download/preview of a child document under an accessible root | Download exists **only for project children** (`/projects/{id}/documents/{docId}/content` route + `_sprk_project_value` equality check, `ExternalProjectDataEndpoints.cs:208-216`) | **Matter- and WA-child documents are listable but not downloadable** — no route, and the parentage check is project-typed only. Because the plane is broker-only, closing this is **pure Dataverse work** (generalize the parentage check to the three typed lookups + a polymorphic route) — **no SPE/Graph work needed**. Container topology is irrelevant to the broker path: each document row carries its own driveId/itemId (`DocumentStorageResolver.cs:52-53`); containers are provisioned **per secure project** (`ProvisionProjectEndpoint.cs:16-23`, stored at `sprk_specontainerid`, `:451`), so only a future *native-SPE* (non-broker) access model would face cross-container child problems |
| **3. AI Search** | Search results trimmed to chunks of documents under accessible roots | Nothing (no contact search surface; index carries `parentEntityType/parentEntityId` as a *scoping* pair, not per-caller security fields) | Everything: root-id security fields on the index (project/matter/WA of each chunk), indexer population, a grant-set→OData filter builder (the analog of `PrivilegeFilterBuilder` keyed on record ids instead of AAD groups), fail-closed empty-set semantics, and a CIAM-reachable search endpoint |

---

## Systemuser-vs-Contact Asymmetries

1. **Plane selection by token issuer** — `ciamlogin.com` issuer or CIAM `tid` → contact strategy; else workforce (`CallerPrincipalResolver.cs:234-254`). Deliberate.
2. **Set composition** — systemuser: ADR-034 membership ∪ own-contact grants (`AccessibleRecordSetService.cs:184-241`); contact: grants ∪ standing-grant membership (gated on FLS-secured `contact.sprk_standinggrant`, fail-closed, `ContactStandingGrantReader.cs:69-127`) ∪ org grants (`ExternalParticipationService.cs:450-464`). Deliberate.
3. **⚠ Blanket Collaborate for workforce callers** — every project in a workforce caller's accessible set is surfaced at `Collaborate` (Read|Create|Write) regardless of grant level (`CallerPrincipalResolver.cs:354-360, 429-431`). A contact holding a **ViewOnly** grant who can also authenticate as a workforce guest (contact-only workforce principal, `WorkforcePrincipalResolver.cs:12-14`) gets their level **silently upgraded** to Collaborate on the same records. Documented decision (`notes/r2-coordination-response.md` per the comment) — but it is a real privilege-elevation path UAC-r2 should close with per-grant levels on the workforce plane.
4. **UAC filter family is systemuser-only** — `AuthorizationService`/`DataverseAccessDataSource` resolve oid→systemuser and return `None` otherwise (`DataverseAccessDataSource.cs:185-198`); `AiAuthorizationService` additionally requires OBO (`AiAuthorizationService.cs:74-85`). Fail-closed, but means "contact UAC" and "systemuser UAC" are entirely disjoint mechanisms.
5. **Surface partition** — contacts: `/api/v1/external*` only; workforce: everything. Enforced by auth schemes (`AuthorizationModule.cs:266-286`). Deliberate and sound.
6. **⚠ `AuthorizationService` always evaluates app-only** (`userAccessToken: null`, `AuthorizationService.cs:48-52`) and `QueryUserPermissionsAsync` "checks" access by reading the document **with whatever token it has** (`DataverseAccessDataSource.cs:313-366`) — under the app token this succeeds for any existing document, so the per-user rights check degenerates to an existence check (currently masked by the G-3 unknown-operation bug that denies first). The doc's "App-only uses RetrievePrincipalAccess" (`uac-access-control.md:21`, 44) no longer matches code.
7. **⚠ Grant administration lacks a role gate** — `/api/v1/external-access/*` (grant/revoke/invite/close/provision) requires only an authenticated workforce token (`ExternalAccessEndpoints.cs:109-111`); no admin role/policy. Any workforce user can grant any contact FullAccess to any record, or provision/close projects.

---

## Genuinely Unbuilt — Gaps UAC-r2 Must Close (by security severity)

| # | Severity | Gap | Evidence |
|---|---|---|---|
| G-1 | **Critical** | Unscoped external write: `PATCH /api/v1/external/todos/{id}` lets any resolvable CIAM contact (even zero grants) modify ANY `sprk_todo` app-only | `ExternalProjectDataEndpoints.cs:328-345` |
| G-2 | **Critical** | Authorization-free app-only download on the workforce surface: `GET /api/documents/{id}/download` — auth-only group, app-only stream; any workforce principal (incl. zero-grant contact-only guests) exfiltrates any document by GUID | `FileAccessEndpoints.cs:30, 101-109, 865-868` |
| G-3 | **High** | The only two `DocumentAuthorizationFilter` attachments pass operation `"read"`, which is not in `OperationAccessPolicy` → unconditional deny; the "UAC-checked" internal download path is dead, leaving G-2 as the de-facto path | `DataverseDocumentsEndpoints.cs:443`, `FileAccessEndpoints.cs:118`, `OperationAccessPolicy.cs:25-149`, `OperationAccessRule.cs:35-46` |
| G-4 | **High** | AI-search security trimming is unbuilt: `privilege_group_ids` never populated at index time (all docs "public"); no grant-set-based filter exists; no contact-reachable search surface | `RecordSyncJob.cs:557`, `KnowledgeDocument.cs:258-260`, `RagIndexingPipeline.cs:475`, `PrivilegeFilterBuilder.cs:47-54` |
| G-5 | **High** | No admin-role gate on grant/revoke/provision/close endpoints | `ExternalAccessEndpoints.cs:109-111` |
| G-6 | **High** | Anonymous non-expiring share links are an unrevocable, untracked access channel orthogonal to every grant | `FileAccessEndpoints.cs:640-642` |
| G-7 | **Medium** | Close-project cascade revoke likely broken (`$select` uses `_sprk_contactid_value`; live FK is `_sprk_contact_value`) | `ProjectClosureEndpoint.cs:181, 315` vs `ExternalParticipationService.cs:399-404` |
| G-8 | **Medium** | Workforce blanket-Collaborate upgrades ViewOnly grants for dual-identity contacts; per-grant levels not enforced on the workforce plane | `CallerPrincipalResolver.cs:354-360` |
| G-9 | **Medium** | `AuthorizationService` app-only direct-query check is not a per-user check (existence check under app token) | `AuthorizationService.cs:48-52`, `DataverseAccessDataSource.cs:313-366` |
| G-10 | **Medium** | Column-level scoping absent on the module fetch seam (any attribute of an accessible module row is readable) | `ExternalModuleDataEndpoints.cs:141-172` (row/entity guard only) |
| G-11 | **Functional** | Matter/WA-child document download routes missing (children listable, bytes unreachable); child single-record reads fail closed with no parent-resolving alternative | `ExternalProjectDataEndpoints.cs:67`, `ExternalModuleRegistry.cs:126-127` |
| G-12 | **Functional** | Contact file surface is download-only: no preview/office/upload for contacts even at Collaborate/FullAccess (SPE writer role is dead config) | `SpeContainerMembershipService.cs:24-29`, single route at `ExternalProjectDataEndpoints.cs:67` |
| G-13 | **Hygiene** | Vestigial/broken SPE membership code invites false confidence: uncalled `GrantMembershipAsync`, GUID-in-UPN revoke matcher | `SpeContainerMembershipService.cs:48`, `RevokeExternalAccessEndpoint.cs:222-235` |

---

## Doc Corrections — `docs/architecture/uac-access-control.md`

| Doc location | Claim | Status | Corrected reality |
|---|---|---|---|
| :18 (Key Decisions) + :28-34 (Three-Plane table) | "SPE files … External Mechanism: SPE container membership via Graph"; "a single grant orchestrates all three" | **Wrong** | External SPE access is broker-only app-only; container membership is never granted (`GrantExternalAccessEndpoint.cs:116-117`; `GrantMembershipAsync` uncalled). A grant touches one plane. |
| :32 (Plane 1 External Mechanism) | "Power Pages table permissions (web role → parent chain)" | **Stale** | No Power Pages; external SPA is CIAM + BFF; enforcement is the `CallerPrincipal` grant set (`CallerPrincipalResolver.cs:303-341`, `ExternalProjectDataEndpoints.cs:148`). |
| :34 (Plane 3 External) | "BFF constructs filter from contact's participation records" | **Wrong (unbuilt)** | No such code; no contact search surface at all (grep: no `GetGrantSetAsync` caller under `Services/Ai/**`). |
| :36 (Three-plane orchestration) | Grant = record + web role + SPE membership; Revoke = deactivate + remove from container + remove web role | **Wrong** | Grant = record + cache invalidation only (`GrantExternalAccessEndpoint.cs:98-117`). Revoke = deactivate + cache invalidation; SPE removal only if `ContainerId` supplied and matcher is broken (`RevokeExternalAccessEndpoint.cs:120-182`); `WebRoleRemoved` always false (:182). |
| :21, :44 (Dual-mode table) | "App-only uses `RetrievePrincipalAccess`" | **Stale** | App-only path also uses the direct query — with the app token, so it degenerates to an existence check (`DataverseAccessDataSource.cs:313-366`; `AuthorizationService.cs:48-52` always passes null token). |
| :65-76 (Endpoint filters) | "12+ domain-specific filters, each applied per-endpoint … extract oid → OperationAccessPolicy → 403" | **Misleading** | Filter classes exist (`Api/Filters/` has 23), but `DocumentAuthorizationFilter` is attached on only 2 routes, both broken by the `"read"` unknown-operation bug (G-3); none apply on contact routes. |
| :89 (Fail-closed table) | "External caller with no active participation → Deny" | **Stale** | A resolvable contact with zero grants is resolved successfully with an empty scope and receives 200-empty responses (`CallerPrincipalResolver.cs:307-341`); only an *unresolvable* contact is denied 403 (`:312-314`). |
| :95-101 (External Caller Access Levels table) | ViewOnly→Reader/Read; Collaborate→Writer/Read+Create; FullAccess→Writer/Read+Create+Write | **Wrong on both columns** | SPE-role column is dead config (never granted). Effective Dataverse rights: ViewOnly=Read; Collaborate=Read+Create+**Write**; FullAccess=Read+Create+Write+**Delete** (`CallerPrincipalResolver.cs:124-134`). Also missing: workforce callers get blanket Collaborate (`:360`). |
| :111 (Troubleshooting) | "External user denied despite participation → BFF must add Contact to container via Graph API" | **Wrong** | Container membership is irrelevant to external access; the broker path needs only the grant row + document parentage. |
| :112 (Troubleshooting) | "AI Search returns no results for external → BFF must include project IDs from participation records" | **Wrong (unbuilt)** | There is no external AI search; no participation-based filter exists to mis-configure. |
| :10 (Verification note) | "All 6 referenced classes confirmed present" | **Technically true, materially misleading** | Classes exist but the doc's description of how they compose (dual-mode, per-endpoint application, three-plane orchestration) does not match code; the entire ExternalAccess enforcement stack (CallerPrincipal, AccessibleRecordSet, module registry, Tier-2 injector) post-dates and is absent from the doc. |

---

*Investigation complete. Companion notes: `01-spa-plane.md`, `04-grant-lifecycle.md` in this directory.*
