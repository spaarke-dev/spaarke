# Investigation 03 — The Native Plane and the Existing UAC Core

> **Project**: unified-access-control-r2
> **Date**: 2026-08-20
> **Method**: full read of `Spaarke.Core/Auth/**`, `IAccessDataSource`/`DataverseAccessDataSource`, `CachedAccessDataSource`, all 23 `Api/Filters/*`, DI modules, POA seam, external-access stack cross-references, and the governing docs (ADR-003, `constraints/auth.md`, `docs/architecture/uac-access-control.md`, `.claude/patterns/auth/uac-access-control.md`). All claims cited `path:line` against this worktree. Static analysis only — no runtime verification was performed; findings marked **[static]** are high-confidence code traces that should be confirmed with one live request each before being treated as production bugs.

---

## 1. Summary

1. **The "Unified Access Control" core is neither unified nor, in large part, functional.** It is a document(`sprk_documents`)-only, Read-only gate. The documented dual-mode (`RetrievePrincipalAccess` app-only / direct-query OBO) no longer exists in code — **`RetrievePrincipalAccess` is never called anywhere server-side** (comments only). Both modes run the same direct query `GET sprk_documents({id})` (`src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:323`) and grant at most `AccessRights.Read` (`:368-372`).
2. Because the snapshot can never contain more than Read, **every operation in `OperationAccessPolicy` that requires Write/Create/Delete/Share always denies**, and several live filters pass operation strings (`"read"`, `"finance.read"`, `"finance.confirm"`, `"entity.associate_document"`) that are **not in the policy map at all** → unconditional deny (`unknown_operation`). Multiple mapped production endpoints are statically always-403. **[static]**
3. Conversely, the **app-only (service-principal) mode grants Read based on what the BFF's app identity can see, not the caller** — and its result is cached under a key that ignores auth mode, so an unscoped SP-mode snapshot can satisfy a subsequent OBO-mode AI authorization for up to 60 s. This is the one genuine **access-widening** defect found. **[static]**
4. The Redis decorator's "fail-open" is **availability fail-open, not authorization fail-open**: on Redis errors it falls through to Dataverse; a total outage produces `AccessRights.None` → deny. A Redis outage *narrows* access, never widens it.
5. **Verdict: two authorization systems.** The UAC core (`AuthorizationService`/`IAccessDataSource`) and the external-access stack (`CallerPrincipalResolver`/`AccessibleRecordSetService`) share no type, no seam, no cache, no principal model. They never meet.
6. The **POA grant seam** can grant to `systemuser` only, has **no revoke/modify**, and its share-reader assumes every POA row is a systemuser. Team grant + revoke *are* proven possible in this codebase — but only inside `PlaybookSharingService`'s private duplicate POA client.
7. **ADR-003 is already dead letter as written**: `AiAuthorizationService` and the entire ExternalAccess stack are "new service layers for auth"; `CachedAccessDataSource` caches snapshots across requests against the ADR's "per-request only" MUST. UAC-r2 should take **§6.5 path B** (amend/supersede ADR-003), not accumulate a fourth undocumented evaluator.

---

## 2. What the UAC Core Actually Evaluates

### 2.1 The pipeline

`AuthorizationService.AuthorizeAsync` (`src/server/shared/Spaarke.Core/Auth/AuthorizationService.cs:28-128`):

1. Takes `AuthorizationContext { UserId, ResourceId, Operation, CorrelationId }` (`:131-137`). `UserId` is the Azure AD `oid` **or** `ClaimTypes.NameIdentifier` depending on the calling filter (inconsistent: `DocumentAuthorizationFilter.cs:48` uses NameIdentifier only; `ResourceAccessHandler.cs:107-133` tries oid → NameIdentifier → sub).
2. Fetches an `AccessSnapshot` via `IAccessDataSource.GetUserAccessAsync(userId, resourceId, userAccessToken: null, ct)` — **hard-coded `null` token, i.e. always service-principal mode** (`AuthorizationService.cs:48-52`; the comment at `:46-47` says "For AI authorization with OBO, see AiAuthorizationService").
3. Iterates the injected `IEnumerable<IAuthorizationRule>` in order; first non-`Continue` result wins (`:58-91`).
4. **No rule decides → deny** `sdap.access.deny.no_rule` (`:93-107`). **Any exception → deny** `sdap.access.error.system_failure` (`:109-127`).

### 2.2 The rule chain today — ADR verified

The ADR-003 update note (`.claude/adr/ADR-003-authorization-seams.md:88`) says the original rule list was superseded by a single `OperationAccessRule`. **Verified true**: the only `IAuthorizationRule` implementation in the repo is `OperationAccessRule` (`src/server/shared/Spaarke.Core/Auth/Rules/OperationAccessRule.cs:20`), and the only registration is `SpaarkeCore.cs:72` (`src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`). `ExplicitGrantRule`/`ExplicitDenyRule`/`TeamMembershipRule` do not exist anywhere.

`OperationAccessRule.EvaluateAsync` (`OperationAccessRule.cs:29-79`):
- Unknown operation → **Deny** `sdap.access.deny.unknown_operation` (`:35-46`).
- `OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, operation)` — bitwise `(user & required) == required` (`OperationAccessPolicy.cs:193-200`) → Allow, else Deny `sdap.access.deny.insufficient_rights`.

### 2.3 What `OperationAccessPolicy` maps

`src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:25-149` — a static dictionary of **66 operations** (not "70+" as the doc claims): 37 `driveitem.*`, 19 `container.*`, 10 legacy names (`preview_file`, `download_file`, `upload_file`, `replace_file`, `delete_file`, `read_metadata`, `update_metadata`, `share_document`, `create_container`, `delete_container`). Notable policy decisions:
- `driveitem.content.download` / `download_file` require **Write**, not Read (`:37`, `:140`) — deliberate security rule.
- There is **no** `"read"`, `"write"`, `"finance.*"`, or `"entity.*"` key. `constraints/auth.md:182-188` even warns "Use operations from OperationAccessPolicy (not generic 'read' or 'write')" — a warning three live call sites violate (see §4).

### 2.4 Fail-closed proof

| Layer | Behavior | Evidence |
|---|---|---|
| Rule chain exhausted | Deny `no_rule` | `AuthorizationService.cs:93-107` |
| Exception in evaluation | Deny `system_failure` | `AuthorizationService.cs:109-127` |
| Unknown operation | Deny `unknown_operation` | `OperationAccessRule.cs:35-46` |
| Data-source exception | Snapshot = `AccessRights.None` (deny downstream) | `DataverseAccessDataSource.cs:229-247` |
| oid has no systemuser | Snapshot = `None` | `DataverseAccessDataSource.cs:186-198` |
| Document query 403/404/other | empty permissions → `None` | `DataverseAccessDataSource.cs:333-359` |

Fail-closed is real **at the decision layer**. The failure of the model is elsewhere: what the data layer *proves* (§3) and what operations the callers *ask for* (§4).

---

## 3. `IAccessDataSource` Reality — the Dual Mode That Isn't

### 3.1 Contract vs implementation

`IAccessDataSource.GetUserAccessAsync(userId, resourceId, userAccessToken?, ct)` (`src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs:26-30`) documents: token present → OBO as the user; token null → service principal. `AccessSnapshot` carries `AccessRights` flags + `TeamMemberships` + `Roles` (`:37-51`).

`DataverseAccessDataSource.GetUserAccessAsync` (`DataverseAccessDataSource.cs:146-248`) does:
1. Pick token: OBO exchange via MSAL `AcquireTokenOnBehalfOf` when a user token was passed (`:164-175`, `:105-144`); otherwise app token (`:177-182`). OBO requires `API_CLIENT_SECRET` config; under pure managed identity `_cca == null` and OBO **throws** (`:107-112`) → caught at `:229` → `None` → deny.
2. Map caller `oid` → Dataverse `systemuserid` (`LookupDataverseUserIdAsync`, `:256-294`). **A contact-only principal can never pass** — no systemuser row → `None` (`:186-198`).
3. `QueryUserPermissionsAsync` (`:305-379`): **the only access probe** — `GET sprk_documents({resourceId})?$select=sprk_documentid` with the chosen token (`:323-331`). Success ⇒ one `PermissionRecord` with **exactly `AccessRights.Read`** (`:368-372`). Failure ⇒ empty ⇒ `None`.
4. Teams and roles are queried (`:426-493`) and stored in the snapshot, **but no rule reads them** — `OperationAccessRule` only inspects `AccessRights`. They are cargo.

### 3.2 What each mode actually proves

| Mode | Selected when | Query runs as | Proves | Max rights granted |
|---|---|---|---|---|
| **OBO** | Bearer token passed (only `AiAuthorizationService.cs:78,176-180` does) | the caller | The **caller** can read the `sprk_documents` row (Dataverse row security enforced) | `Read` |
| **App-only (SP)** | token null — `AuthorizationService.cs:51`, `PermissionsEndpoints.cs:76,159` | the **BFF app identity** | The **app** can read the row — i.e., essentially that the document exists | `Read` — **granted to the caller regardless of the caller's actual access** |

**Answer to "is there any mode that returns access data NOT scoped to the caller": yes — the app-only mode.** The mapped `systemuserid` from step 2 is used only for logging and the `PermissionRecord` DTO; it never constrains the query (`:203`, `:305-379`). `RetrievePrincipalAccess` — the mechanism that WOULD scope app-only checks to the caller — is referenced in comments (`:299`, `:316`), a dead DTO (`PrincipalAccessResponse`, `:530-533`), and a dead mapper (`MapDataverseAccessRights`, `:391-421`, no callers), **but is never invoked**. Grep across `src/server/**` confirms zero call sites.

### 3.3 Consequences

- **Read-ceiling**: no code path can ever produce Write/Create/Delete/Share in a snapshot ⇒ every policy operation requiring more than Read **always denies** through this chain. **[static]**
- **SP-mode over-grant**: any authenticated internal user "has Read" on any document the app can see. Directly observable via `GET /api/documents/{id}/permissions` (`PermissionsEndpoints.cs:76`): `CanPreview=true` for everyone, all Write-derived capabilities `false` for everyone (`:219-244`). The endpoint is simultaneously an over-granter (preview) and an under-granter (everything else).
- **Entity hard-coding**: the probe is `sprk_documents` only (`:323`). Passing any other resource (e.g. `EntityAccessFilter`'s `"account:{guid}"` composite, `EntityAccessFilter.cs:142`) produces a malformed/failed query → `None` → deny. The UAC core cannot authorize non-document resources at all.

---

## 4. Coverage Map — What Is Actually Behind Which Gate

### 4.1 Endpoints behind the UAC core (`AuthorizationService` / `ResourceAccessHandler`) — and their real behavior

| Surface | Gate | Operation passed | In policy? | Static outcome |
|---|---|---|---|---|
| `GET /api/documents/{id}/eml-render` | `AddDocumentAuthorizationFilter("read")` (`Api/FileAccessEndpoints.cs:117-118`) | `"read"` | **NO** | **Always 403** (`unknown_operation`) **[static]** |
| `GET /api/v1/documents/{id}/download` | same (`Api/DataverseDocumentsEndpoints.cs:443`) | `"read"` | **NO** | **Always 403** **[static]** |
| Chat archive-ingest in-handler check | `IAuthorizationService.AuthorizeAsync` (`Api/Ai/ChatDocumentEndpoints.cs:911-917`) | `"read"` | **NO** | **Always 403** at that check **[static]** |
| Finance endpoints (5 routes) | `AddFinanceAuthorizationFilter` (`Api/Finance/FinanceEndpoints.cs:18-65`) | `"finance.read"` / `"finance.confirm"` | **NO** | **Always 403** **[static]** |
| Office save w/ target entity | `AddEntityAccessFilter` (`Api/Office/OfficeEndpoints.cs:173`; op at `Api/Filters/EntityAccessFilter.cs:64`) | `"entity.associate_document"` | **NO** | **Always 403** when `TargetEntity` present **[static]** |
| Upload endpoints (3 routes) | `RequireAuthorization("canwritefiles")` → `ResourceAccessHandler` → `upload_file` (Write\|Create) (`Api/UploadEndpoints.cs:108,159,214`; `Infrastructure/DI/AuthorizationModule.cs:244-245`) | valid op | yes | **Always 403** — snapshot ceiling is Read **[static]** |
| Container CRUD (8 routes) | `RequireAuthorization("canmanagecontainers")` → `create_container` (Create\|Write) (`Api/DocumentsEndpoints.cs:61-418`; `AuthorizationModule.cs:246-247`) | valid op | yes | **Always 403** **[static]** |
| `GET/POST /api/documents/.../permissions[.batch]` | none (capability read) (`Api/PermissionsEndpoints.cs:26-42`) | n/a | n/a | Returns SP-mode capabilities — CanPreview true for all, rest false |

All of the above are live-mapped (`Infrastructure/DI/EndpointMappingExtensions.cs:130-136,160,287`). The ~30 fine-grained policies (`canpreviewfiles`, `candownloadfiles`, …, `AuthorizationModule.cs:182-247`) have **zero consumers** besides `canwritefiles`/`canmanagecontainers`. `OfficeDocumentAccessFilter` is **dead code** — defined (`Api/Filters/OfficeDocumentAccessFilter.cs`) but never applied anywhere.

### 4.2 Endpoints behind `IAiAuthorizationService` (the working OBO path)

`AiAuthorizationService` (`Services/Ai/AiAuthorizationService.cs:47-158`) extracts the caller's bearer token and checks **Read** per document via the same `IAccessDataSource` — but OBO-scoped, so genuinely the caller's access. Applied via `AiAuthorizationFilter` across the whole AI surface (Chat, Analysis, KnowledgeBase, ReviewMemo, Dispatch, WordExport, AdminKnowledge — ~40 routes, e.g. `Api/Ai/ChatEndpoints.cs:74-365`, `Api/Ai/AnalysisEndpoints.cs:59,78`). Caveat: when the request carries **no document IDs, the filter passes through** (`Api/Filters/AiAuthorizationFilter.cs:75-79`) and session/tenant checks in the handler are the only gate. `AnalysisAuthorizationFilter` and `VisualizationAuthorizationFilter` use the same service.

### 4.3 What the UAC core does NOT cover

- **Dataverse row reads generally: not covered.** The core's only probe is `sprk_documents` Read. MDA CRUD flows through host `Xrm.WebApi` (native Dataverse security) and never transits the BFF. BFF-side Dataverse reads in other domains use their own mechanisms: Communication uses **Dataverse impersonation** (`MSCRMCallerID`; `Services/Communication/Access/IThreadPrivateGrantProvider.cs:3-8` records the 2026-07-16 rework away from hand-computed unions), record search / workspace / SPE-admin / tenant / registration each have bespoke filters that do not touch `AuthorizationService`.
- **SPE file operations at runtime**: the OBO endpoints (`FileAccessEndpoints` preview/content/office/share-link, `Api/FileAccessEndpoints.cs:33-90`) carry **no per-document UAC filter** — enforcement is delegated to Graph/SPE acting as the user. Only the app-only proxy paths attempted UAC gating, and those are the broken-op sites above.
- **External (contact) callers**: structurally impossible in the core — oid→systemuser mapping fails (§3.1.2).

**True scope of the UAC core today**: Read-level document checks for the AI surface (working, OBO), plus a set of document/finance/office/upload/container gates that statically always deny. It is not a general authorization system.

---

## 5. One System or Two? — Verdict

**Two.** Evidence:

- Grep of `Infrastructure/ExternalAccess/**` for `IAccessDataSource|AuthorizationService|OperationAccessPolicy|AccessSnapshot`: **zero matches**. The external stack never calls the UAC core.
- `AccessibleRecordSetService`/`IAccessibleRecordSetService` are referenced only by the ExternalAccess module and `/api/v1/external` endpoints (`Infrastructure/DI/ExternalAccessModule.cs:255`; `Api/ExternalAccess/AccessibleRecordSetAuthorizationFilter.cs:55`; `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:369`). **No MDA-backed internal BFF endpoint ever passes through it.**
- An external CIAM caller **cannot** pass through `AuthorizationService` even accidentally: the data source requires a `systemuser` row for the oid (`DataverseAccessDataSource.cs:186-198`).
- Different decision primitives: bitmask-per-operation vs record∈composed-set (`AccessibleRecordSetService.cs:39-55`); different data: `sprk_documents` Read probe vs ADR-034 membership ∪ `sprk_externalrecordaccess` grants ∪ standing-grant membership (`AccessibleRecordSetService.cs:5-16,144-301`); different principal models: Azure AD oid→systemuser vs plane-agnostic `CallerPrincipal` (CIAM contact / workforce systemuser / contact-only, `CallerPrincipalResolver.cs:38-120`); different caching: Redis 60s snapshots vs per-request composition.
- One nuance: a **workforce systemuser** using the Teams-host/external SPA surface does flow through `AccessibleRecordSetService` (systemuser plane = ADR-034 membership ∪ own contact grants, `AccessibleRecordSetService.cs:184-241`) — so the *external* system already serves both principal types; the *core* system serves only systemusers. The overlap is one-directional and lives entirely in the external stack.
- The only shared machinery between the planes is `IMembershipResolverService` (ADR-034), which the external stack and the Communication derivation both consume — it is a membership *data* service, not an authorization decision seam.

There is no shared abstraction. UAC-r2's "one system or two" question is settled by code: **two systems today; the name "Unified Access Control" describes an aspiration, not the implementation.**

---

## 6. POA Grant Seam — Capability Table

### 6.1 What exists

| Capability | Where | Notes |
|---|---|---|
| Resolve ObjectTypeCode | `DataverseWebApiService.GetEntityObjectTypeCodeAsync` (`src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:1009-1034`) | cached per logical name |
| **Grant** — systemuser only | `GrantAccessAsync` (`:1041-1066`) | Principal hard-coded `systemusers({id})` (`:1058`); `accessRightsCsv` = free-text AccessMask; app-only |
| **Read shares** | `GetSharedSystemUserIdsAsync` (`:1076-1107`) | `principalobjectaccessset` filter on objectid+objecttypecode, **no `principaltypecode` filter** — every principalid is *assumed* systemuser (documented assumption `:1069-1072`); fails soft (empty list) |
| Test seam | `IDataverseAccessGrantService` (`Services/Communication/Access/IDataverseAccessGrantService.cs:14-29`) | mirrors only the two methods above; registered singleton `CommunicationModule.cs:652` |
| Consumer | `DirectThreadAccessService` (`Services/Communication/Access/DirectThreadAccessService.cs`) | thread share `:92`, per-message shares `:193`, share read-back `:124,265` |
| **Revoke** | **NOT on the seam.** Exists only in `PlaybookSharingService.RevokeAccessFromTeamAsync` (`Services/Ai/PlaybookSharingService.cs:331-350`) via its own private HttpClient | POST `RevokeAccess` with `Revokee` |
| **Grant to team** | **NOT on the seam.** Exists only in `PlaybookSharingService.GrantAccessToTeamAsync` (`:302-329`) — principal `teams({teamId})` | proves team-principal POA works in this environment |
| **Modify** | Nowhere. No `ModifyAccess` call in the repo | — |
| **Grant to contact** | Nowhere (see §8) | — |

### 6.2 `accessRightsCsv` values used in practice

- `"ReadAccess"` — the only value on the shared seam (`DirectThreadAccessService.cs:25`, used at `:92` and `:193`).
- Playbook path composes `"ReadAccess"`, `"WriteAccess,AppendAccess,AppendToAccess"`, `"ShareAccess"` (`PlaybookSharingService.cs:472-493`).

### 6.3 What UAC-r2 would have to add for share-to-team + revoke

1. **Generalize the principal** in `DataverseWebApiService.GrantAccessAsync` — parameterize the principal entity set (`systemusers` | `teams`) or accept a typed principal ref; today it is a one-line hard-coding (`:1058`).
2. **Add `RevokeAccessAsync`** — the exact payload shape already exists at `PlaybookSharingService.cs:336-349`; port it onto the shared singleton and the `IDataverseAccessGrantService` seam (CLAUDE.md §11: extend, don't duplicate — and fold PlaybookSharingService's duplicate POA client into the shared surface while there).
3. **Add `ModifyAccessAsync`** (POST `ModifyAccess`) if in-place mask changes are needed; otherwise revoke+grant.
4. **Fix the share reader** — once any non-systemuser share exists on an entity this path reads, `GetSharedSystemUserIdsAsync` will silently return team GUIDs as if they were user GUIDs. It needs a `principaltypecode` disambiguation (or return typed principals). The in-code assumption (`:1069-1072`) explicitly depends on "no team/contact shares are written by this path" — a cascade grant-writer breaks that precondition.

---

## 7. ADR-003 Tension Analysis (§6.5 recommendations)

ADR-003 concise (`.claude/adr/ADR-003-authorization-seams.md`): MUST implement new auth logic as `IAuthorizationRule` (`:21`); MUST cache UAC snapshots per-request only (`:23`); MUST NOT create new service layers for auth (`:28`); MUST NOT cache authorization decisions (`:30`).

### (a) Does the existing code already violate it?

| Rule | Status | Evidence |
|---|---|---|
| New auth logic as `IAuthorizationRule` | **Violated twice, pre-existing** | `AiAuthorizationService` is a parallel auth service (`Services/Ai/AiAuthorizationService.cs:28`), and the entire ExternalAccess stack (`CallerPrincipalResolver`, `AccessibleRecordSetService`, strategies, filters) is a second full evaluation layer with zero `IAuthorizationRule` participation |
| No new service layers for auth | **Violated** — same evidence | |
| Snapshots per-request only | **Violated** | `CachedAccessDataSource` caches `AccessSnapshot` cross-request in Redis for 60 s (`Infrastructure/Caching/CachedAccessDataSource.cs:37,102-105`); `docs/architecture/uac-access-control.md:22` *blesses* this via ADR-009 — the two documents contradict each other. The per-request mechanism ADR-003 envisioned (`RequestCache`) is registered (`SpaarkeCore.cs:75`) but has **zero consumers** |
| No caching of decisions | **Honored** | Only data is cached; decisions are recomputed (`CachedAccessDataSource.cs:10-14`) — keep this rule |

Per CLAUDE.md §6.5 these are pre-protocol violations (not retroactively excused, but the protocol applies to new decisions). What matters for UAC-r2: the ADR no longer describes reality, so **continuing to cite it as the constraint set for new access work would be building on fiction**.

### (b) Would UAC-r2's likely designs violate it?

- **Cascade grant-writer** (materializes POA/table grants when a membership/grant event occurs): this is access-data **write** machinery, not decision evaluation — ADR-003's seams govern evaluation. But its "MUST NOT create new service layers for auth" reads broadly enough to catch it, and a reviewer applying the letter would flag it. → **Path B** (amend ADR-003 to scope its MUSTs to *decision evaluation* on the SPE/document plane and to name the grant-materialization plane), with **path A** (documented project exception citing this investigation) acceptable as an interim if the amendment is deferred. Not path C: expressing a grant-writer as an `IAuthorizationRule` is a category error.
- **Derive-at-read-time evaluator** (compute effective access at read): as a new service it would be the **fourth** parallel evaluator (after core, AI, external) — exactly the accretion pattern that produced today's split. Path C (implement inside the existing rule chain by extending `IAccessDataSource`/`AccessSnapshot` so `AuthorizationService` can serve it) is *technically* available, but the existing `IAccessDataSource` is the component this investigation shows to be broken (Read-ceiling, SP-mode unscoped, entity hard-coded) — extending it without redefining it perpetuates the defects. → **Path B**: UAC-r2 should supersede/amend ADR-003 with an ADR that (1) names the two planes (native systemuser plane / contact-SPA plane) and their evaluation seams, (2) redefines the data seam contract (what a snapshot proves, per mode, per entity), (3) retains "MUST NOT cache decisions" and "machine-readable deny codes", and (4) resolves the snapshot-caching contradiction explicitly (either bless the 60 s data cache with the mode-keying fix from §8-F3, or drop the decorator).
- **Anti-recommendation**: shipping any new evaluator silently under "it's just a service" repeats the external-stack precedent and is the §6.5 forbidden fourth path.

### 🔔 ADR Conflict — Resolution Required (for the orchestrator to surface)

- **ADR in question**: ADR-003 Lean Authorization Seams
- **Specific rules**: "MUST implement new auth logic as `IAuthorizationRule`"; "MUST NOT create new service layers for auth"; "MUST cache UAC snapshots per-request only"
- **Conflict**: the codebase already contains two non-rule auth layers and a cross-request snapshot cache; UAC-r2's designs (grant-writer, derive-at-read evaluator) cannot honestly comply with a two-seam model that no longer exists
- **Proposed path**: **B** (amendment/supersession) for the ADR itself; A as interim cover for the grant-writer if B is deferred
- **Alternative considered (rejected)**: C — folding everything back into the `IAuthorizationRule` chain over the current `IAccessDataSource`; rejected because the data seam itself is the defective component (§3) and contacts can never transit it

---

## 8. Security Findings

**F1 — SP-mode access data is not scoped to the caller (over-grant).** `AuthorizationService` and `PermissionsEndpoints` call the data source with `userAccessToken: null`; the resulting probe runs as the app identity and grants the caller Read on any document the app can see (`AuthorizationService.cs:48-52`; `PermissionsEndpoints.cs:76,159`; `DataverseAccessDataSource.cs:177-182,323,368-372`). Today its blast radius is limited by F2 (most SP-gated ops deny anyway) — the live exposures are `PermissionsEndpoints` capability leakage (CanPreview=true universally) and F3. **[static, HIGH confidence]**

**F2 — Multiple mapped endpoints statically always-deny.** Unknown ops `"read"` (3 sites: `FileAccessEndpoints.cs:118`, `DataverseDocumentsEndpoints.cs:443`, `ChatDocumentEndpoints.cs:915`), `"finance.read"`/`"finance.confirm"` (`FinanceEndpoints.cs:18-65`), `"entity.associate_document"` (`EntityAccessFilter.cs:64`); plus the Read-ceiling killing `canwritefiles`/`canmanagecontainers` (`UploadEndpoints.cs:108-214`, `DocumentsEndpoints.cs:61-418`). Fail-closed, so not an access leak — but it means the eml-render reading pane, the v1 download proxy, finance module, Office save-with-entity, and legacy upload/container endpoints cannot work as UAC-gated. Either these surfaces are unused in production or they error and nobody attributed it to the filter. Recommend one live probe each. **[static, HIGH confidence on the code trace; MEDIUM on production impact]**

**F3 — Cache key ignores auth mode → SP-poisoning of the OBO path (genuine widening).** `CachedAccessDataSource` keys on `sdap:auth:access:{userOid}:{resourceId}` only (`CachedAccessDataSource.cs:65`) and serves hits to all callers (`:70-85`). Sequence: user without access to doc Y calls `GET /api/documents/{Y}/permissions` (SP mode → Read granted per F1 → cached 60 s) then calls an AI endpoint on doc Y; `AiAuthorizationService` (OBO-intending) hits the cache and authorizes on the unscoped snapshot (`AiAuthorizationService.cs:176-183`). 60-second object-level authorization bypass on the AI document surface. Fix options: include auth mode in the key, or stop caching SP-mode snapshots, or (root cause) make SP mode caller-scoped again via `RetrievePrincipalAccess`. **[static, HIGH confidence]**

**F4 — "Fail-open on Redis errors" is availability-only — verified NOT an access-widening defect.** Redis read error → log + fall through to Dataverse (`CachedAccessDataSource.cs:92-99`); write error → swallowed (`:137-143`); Redis+Dataverse both down → `None` → deny (`DataverseAccessDataSource.cs:229-247`). A Redis outage narrows access (adds latency, may lock users out via F5), never widens it. The doc word "fail-open" (`docs/architecture/uac-access-control.md:22,59`) is misleading and should be rephrased "fall through to source".

**F5 — Negative snapshots are cached.** A transient Dataverse error yields a `None` snapshot (not an exception), which `CachedAccessDataSource` then caches for 60 s (`:102-105`) — a one-request blip becomes a 60 s deny for that user+resource. Fail-closed direction, but an availability defect in an auth path.

**F6 — POA share reader will mislabel non-systemuser principals.** `GetSharedSystemUserIdsAsync` has no `principaltypecode` filter (`DataverseWebApiService.cs:1085`, assumption at `:1069-1072`). Safe today only because the seam writes systemuser-only shares; any cascade/team grant-writer invalidates the precondition (§6.3.4).

**F7 — Duplicate POA clients.** `PlaybookSharingService` maintains its own Dataverse HTTP client + POA grant/revoke/read (`PlaybookSharingService.cs:16-72,302-428`) parallel to `DataverseWebApiService`'s primitives — two implementations of the same security-sensitive mechanism (CLAUDE.md §11 violation, consolidation candidate for UAC-r2).

**F8 — Claim-inconsistency across filters.** `DocumentAuthorizationFilter`/`FinanceAuthorizationFilter` use `ClaimTypes.NameIdentifier` only (`DocumentAuthorizationFilter.cs:48`, `FinanceAuthorizationFilter.cs:54`) while others prefer `oid`; the data source needs the Entra oid to map to systemuser. Where NameIdentifier ≠ oid the lookup fails → deny (fail-closed, but another silent-deny source).

---

## 9. Doc Corrections (WRONG/STALE vs code)

`docs/architecture/uac-access-control.md`:
1. `:21` + `:24` + `:44` — "App-only uses `RetrievePrincipalAccess`" / "RetrievePrincipalAccess already factors in security roles, team memberships…": **WRONG.** No server-side `RetrievePrincipalAccess` call exists (grep: comments only). Both modes use the direct query (`DataverseAccessDataSource.cs:305-379`); app-only mode proves app visibility, not caller access; snapshot ceiling is Read.
2. `:53-58` — cache keys `spaarke:tenant:{tenantId}:user-roles:{userOid}:v1` "via `ITenantCache`": **WRONG.** Actual keys are `sdap:auth:roles|teams|access:*` via raw `IDistributedCache`, with **no tenant scoping** (`CachedAccessDataSource.cs:65,153,180`).
3. `:22,59` — "fail-open on Redis errors": misleading; actual behavior is fall-through-to-source; decisions are never granted from cache absence (F4).
4. `:65-76` — "12+ filters … All filters follow the same pattern → call `AuthorizationService.AuthorizeAsync()`": **WRONG.** Of the 23 filters, only Document/Finance/EntityAccess/OfficeDocumentAccess(+dead) use `AuthorizationService`; Ai/Analysis/Visualization use `IAiAuthorizationService`; Communication uses impersonation; the external group uses `CallerPrincipal`/`AccessibleRecordSet`; and the `AuthorizationService`-calling filters pass op strings not in the policy (F2).
5. `:20` — "70+ operations mapped": actual count **66** (`OperationAccessPolicy.cs:25-149`). Minor.
6. `:5` — "Status: Verified (Production-Ready Internal)": stale given F1-F3.

`.claude/adr/ADR-003-authorization-seams.md`:
7. `:11` — "two seams only": stale — three evaluation families exist (core, `AiAuthorizationService`, ExternalAccess stack).
8. `:23` — "MUST cache UAC snapshots per-request only": contradicted by the blessed `CachedAccessDataSource` cross-request 60 s cache; also the per-request vehicle (`RequestCache`, `SpaarkeCore.cs:75`) has zero consumers. The amendment must pick one story.
9. `:69` — links `../patterns/auth/authorization-service.md`: **dead link** — file does not exist (patterns/auth glob).

`.claude/constraints/auth.md`:
10. `:199` — "Uses `RetrievePrincipalAccess` in app-only contexts; uses direct query pattern in OBO contexts": **WRONG** — direct query in both; app-only unscoped (F1).

`.claude/patterns/auth/uac-access-control.md`:
11. `:21` — "Access data loaded once per request via RequestCache — not re-queried per check": **WRONG** — `RequestCache` is never used; per-check calls go to Redis/Dataverse via `CachedAccessDataSource`.

---

## Appendix — Key File Inventory

| Component | Path |
|---|---|
| Decision engine | `src/server/shared/Spaarke.Core/Auth/AuthorizationService.cs` |
| Rule (only one) | `src/server/shared/Spaarke.Core/Auth/Rules/OperationAccessRule.cs` |
| Policy map (66 ops) | `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs` |
| Data seam | `src/server/shared/Spaarke.Dataverse/IAccessDataSource.cs`, `DataverseAccessDataSource.cs` |
| Redis decorator | `src/server/api/Sprk.Bff.Api/Infrastructure/Caching/CachedAccessDataSource.cs` |
| DI wiring | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`, `AuthorizationModule.cs:156-268` |
| Policy-based handler | `src/server/api/Sprk.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs` |
| AI auth (OBO) | `src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs`, `Api/Filters/AiAuthorizationFilter.cs` |
| Capabilities endpoint | `src/server/api/Sprk.Bff.Api/Api/PermissionsEndpoints.cs` |
| POA primitives | `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:991-1107` |
| POA seam + consumer | `src/server/api/Sprk.Bff.Api/Services/Communication/Access/IDataverseAccessGrantService.cs`, `DirectThreadAccessService.cs` |
| Duplicate POA client (team grant/revoke) | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs:302-350` |
| External evaluation core | `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/AccessibleRecordSetService.cs`, `CallerPrincipalResolver.cs` |
