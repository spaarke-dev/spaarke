# UAC-r2 Investigation 01 — SPA / External-Access Plane Mechanics (this worktree)

> **Status**: Investigation findings, 2026-08-20. Code read of `spaarke-wt-unified-access-control-r2` ONLY.
> **Scope**: Verifies the runtime path of the external-access plane and audits
> [`spa-external-access-model-briefing.md`](../../spa-external-access-model-briefing.md) §1/§2/§3/§6/§7/§11 against THIS worktree.
> All paths below are relative to `src/server/api/Sprk.Bff.Api/` unless prefixed.

---

## Summary

The briefing is **substantially accurate in this worktree** — the ExternalAccess code here is line-for-line identical (or within ±3 lines) of the r3 worktree it was written from. The two planes, the systemuser-first principal resolution, the grant-allow-list-vs-generic-membership asymmetry in `AccessibleRecordSetService`, the ScopeDimension OR-filter child derivation, and the Tier-1/Tier-2 split all check out with exact line matches.

Four findings materially **change the picture** the briefing paints:

1. **`PATCH /api/v1/external/todos/{id}` has NO record-scope check** — any resolved caller on either plane can modify ANY `sprk_todo` by GUID (`Api/ExternalAccess/ExternalProjectDataEndpoints.cs:328-345`, gap self-documented at `:338-342`). The briefing calls this whole surface "legacy… superseded"; it is live, mounted on the same dual-plane group.
2. **The "no `<link-entity>` joins" guard is really a "no OTHER-entity" guard** — a self-join to the module's own entity passes (`Api/ExternalAccess/ExternalModuleDataEndpoints.cs:160-161` compares entity NAMES, not element presence).
3. **The gate (`IAccessibleRecordSetService`) is only exercised on the workforce plane** — the CIAM strategy fills the principal's root sets straight from grants and never calls `ComposeAsync` (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:317-341`). The briefing's "the gate can already be called for a child entity type" is true but reaches only workforce callers; CIAM child extension goes through the strategy/descriptors, not the gate.
4. **`AccessibleRecordSetAuthorizationFilter` is orphaned** — defined (`Api/ExternalAccess/AccessibleRecordSetAuthorizationFilter.cs`) but attached to no route anywhere in this worktree (grep: only self-references). Enforcement rides entirely on the strategy-composition + module-registry path.

Everything is fail-closed on the read seam; the deny paths are explicit; there is no empty-but-permissive scope on reads. Cache staleness bounds: grants 60 s, contact/systemuser membership 5 min, oid→systemuser 10 min.

---

## 1. Plane classification (Q1)

`CallerPrincipalResolver.DeterminePlane` (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:234-254`):

```csharp
var issuer = user.FindFirst("iss")?.Value;
if (!string.IsNullOrEmpty(issuer) &&
    issuer.Contains("ciamlogin.com", StringComparison.OrdinalIgnoreCase))
    return CallerPrincipalPlane.CiamContact;          // :237-241
if (!string.IsNullOrEmpty(_ciamTenantId)) {
    var tid = MembershipEndpoints.ExtractTenantId(user);
    if (... tid == _ciamTenantId ...) return CallerPrincipalPlane.CiamContact;  // :243-251
}
return CallerPrincipalPlane.Workforce;                // :253
```

- **CIAM** iff `iss` contains `ciamlogin.com` OR `tid == Ciam:TenantId` (config `Ciam:TenantId` read at `:201`). **Workforce otherwise.**
- The Workforce default is NOT fail-open: an unregistered plane → 401 fail-closed (`:211-223`, "No caller-principal strategy registered … denying (fail-closed)"), and the workforce strategy itself denies any caller that resolves to neither systemuser nor contact (`WorkforcePrincipalResolver.cs:162-171`). The CIAM strategy denies on missing identity claims (401, `:293-301`) and on unresolvable contact (403 `sdap.access.deny.contact_not_found`, `:307-314`).
- Authentication upstream: the `/api/v1/external` group carries the `ExternalCollaboration` policy accepting the `Ciam` scheme + workforce default JwtBearer (`Infrastructure/DI/AuthorizationModule.cs:278-286`); the group-level `CallerPrincipalAuthorizationFilter` (`Api/Filters/CallerPrincipalAuthorizationFilter.cs:29-50`) runs the resolver and short-circuits with the strategy's ProblemDetails on deny. Group wiring: `Api/ExternalAccess/ExternalAccessEndpoints.cs:54-57`.

**Verdict: fail-closed.** Ambiguity in plane selection defaults to Workforce, whose resolution then fails closed.

## 2. Principal resolution (Q2)

Workforce decision tree, `WorkforcePrincipalResolver.ResolveAsync` (`Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs:88-171`):

1. **oid missing** → 401 deny `sdap.access.deny.missing_identity_claims` (`:94-103`).
2. **oid → systemuser** via `MembershipEndpoints.ResolveSystemUserIdAsync` (`:109-111`; Redis-cached 10 min, `Api/Membership/MembershipEndpoints.cs:75-85, 399-476`). Hit → `SystemUser` principal; contactId DERIVED via `IIdentityNormalizationService.ResolveAsync` (`sprk_primarycontact` / AAD cross-ref), non-fatal on failure (`:119-137`) — a systemuser principal may carry `ContactId = null`.
3. **else oid / verified-email → contact** via `TryResolveContactByWorkforceIdentityAsync` (`:148-160`; impl `Services/Ai/Membership/IdentityNormalizationService.cs:242-281`: oid cross-ref on `contact.azureactivedirectoryobjectid` `:285-327`, then `emailaddress1` match `:331+`). Hit → `ContactOnly` principal (non-null anchor).
4. **else explicit deny** 403 `sdap.access.deny.principal_not_resolved` (`:162-171`) — "never a silent pass-through with an unscoped principal".

CIAM: oid (`sprk_externalobjectid`) authoritative; email is a FIRST-LOGIN fallback with a no-hijack rule — an email matching a contact already bound to a **different** oid is denied (`Infrastructure/ExternalAccess/ExternalParticipationService.cs:211-258`, deny at `:247-257`; oid bind-on-first-login `:357-390`, `If-Match: *` update-only).

**Trusted claims**: `oid` (both planes), `tid`, and email from `email`/`preferred_username`/`upn`/`ClaimTypes.Email` (workforce order `WorkforcePrincipalResolver.cs:178-186`; CIAM order `CallerPrincipalResolver.cs:288-291`). All read from the already-validated JWT; Dataverse reads are app-only (broker-only, no OBO).

**Empty-but-permissive scope?** No, on reads. `CallerPrincipal` doc: "there is no 'unscoped' principal — an unresolvable caller is denied, never represented here" (`CallerPrincipalResolver.cs:63-65`). A resolved principal with zero accessible roots gets: fetch → 0 rows without querying (`ExternalModuleDataEndpoints.cs:184-191`); record → 403; `/projects` → empty list. **BUT** `PATCH /todos/{id}` proceeds for ANY resolved principal without any scope consultation — see §6.

Asymmetry worth recording: the **workforce** contact-by-email fallback (`IdentityNormalizationService.cs:264-278`) has NO oid-binding/no-hijack check (TopCount 1 on `emailaddress1`), unlike the CIAM path. Mitigated by the email being tenant-verified by Entra, but a customer-tenant user whose verified email equals an external contact's `emailaddress1` becomes that contact principal (grants ∪ gated standing membership).

## 3. The gate — `IAccessibleRecordSetService` (Q3)

`Infrastructure/ExternalAccess/AccessibleRecordSetService.cs`:

- Interface `:39-55` (`ComposeAsync`, `IsRecordAccessibleAsync`); empty recordId → deny (`:168-171`); `Contains` on the composed set (`:174-175`).
- Composition switches on **Kind** (`:153-161`):
  - **SystemUser** (`ComposeForSystemUserAsync :184-241`) = ADR-034 membership (`_membership.ResolveAsync(systemUserId, entityType, …)` `:192-194` — **entity-type-generic**) ∪ own-contact grants (`:203-226`; derived contact preferred, verified-email fallback via `ResolveExternalContactAsync` `:208-213` — subject to the no-hijack rule, so a CIAM-bound contact never unions to a systemuser by email).
  - **ContactOnly** (`ComposeForContactAsync :244-301`) = grants (`:258-268`) ∪ standing-grant membership IFF `contact.sprk_standinggrant` (live FLS-secured read, `ContactStandingGrantReader.cs:69-128`, fail-closed; membership via `ResolveByContactAsync(contactId, entityType, …)` `:276-278` — **entity-type-generic**, `sprk_assigned*` allow-listed, no transitive: `MembershipResolverService.cs:409, 479`).
- **Grant term allow-list**: `GrantSupportedRootEntities = { sprk_project, sprk_matter, sprk_workassignment }` (`:99-104`); `IsGrantSupported` gates both compose paths (`:203`, `:258`); `GrantedIdsFor` slices the grant set per type (`:111-120`).
- Called for exactly the three root types by `WorkforcePrincipalStrategy` (`CallerPrincipalResolver.cs:422-427`); children are never composed here.

**Briefing's "key extensibility fact" — CONFIRMED**: the membership term passes `entityType` straight through (generic), while the grant term is allow-listed to the 3 roots. `ComposeAsync("sprk_todo")` would already return the systemuser's ADR-034 to-do membership plus no grants.

**Nuance the briefing under-states**: the gate is only invoked from the **workforce** strategy. `CiamContactPrincipalStrategy` never constructs a `WorkforcePrincipal` and never calls `ComposeAsync` — it loads `GetGrantSetAsync` directly (`CallerPrincipalResolver.cs:317-341`). Extending the gate does nothing for a CIAM partner; the CIAM extension seam is the strategy (or purely the module descriptors, since child dims read `CallerPrincipal` root sets that BOTH strategies already fill).

**Orphaned enforcement point**: `AddAccessibleRecordSetAuthorizationFilter` (`Api/ExternalAccess/AccessibleRecordSetAuthorizationFilter.cs:45-63`) is attached to **no endpoint** in this worktree (the `/api/v1/collab` group that used it was removed by R2 task 018, `ExternalAccessEndpoints.cs:21-23`). The composition service is live; the per-route record filter is dead code awaiting a consumer.

## 4. Child derivation as shipped — `POST /api/v1/external/api/dataverse/fetch` (Q4)

Trace (`Api/ExternalAccess/ExternalModuleDataEndpoints.cs:113-240`):

1. Group filter has put `CallerPrincipal` on `HttpContext.Items` (`:122-123`; absent → 500, not a bypass).
2. Validate `EntityName` + `FetchXml` present (`:125-129`).
3. **Module lookup by entity** (`registry.FindByEntity`, `:131-139`): unregistered entity → 403 `sdap.external.module.not_registered` (fail-closed).
4. **Entity guard** (`:147-172`): `FetchXmlEntityExtractor.ExtractEntities` returns primary + every `<link-entity>` name at any depth (`Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs:49-113`); any referenced entity ≠ module entity → 400 `DV_FETCHXML_ENTITY_MISMATCH`; malformed XML → 400 `DV_FETCHXML_MALFORMED`. **Precisely**: this rejects cross-entity joins; a `<link-entity name="{same entity}">` self-join passes (set-of-names comparison, `:160-161`). Nothing else is inspected (aggregates, attributes, order all pass).
5. **Scope dimensions** computed from the descriptor, empty dims dropped (`:180-183`); **all-empty → 200 with 0 rows, no Dataverse query** (`:184-191`).
6. **Server-side injection** — `Tier2ScopeFilterInjector.Inject` (`Api/ExternalAccess/Tier2ScopeFilterInjector.cs:48-99`) adds `<filter type="or">` with one `<condition attribute="{dim}" operator="in">` per non-empty dimension, one `<value>` per accessible id (`:75-86`), inserted before the first `<order>` (`:88-95`).
7. App-only execution, then **`ScopeRows` in-memory re-filter** (defense-in-depth, `:207-208`; impl `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs:153-187`): drops rows whose `@logicalName` ≠ module entity (`:195-204`) and keeps only rows where SOME dimension's attribute value ∈ that dimension's set; `TryGetAttributeId` tolerates Guid / `EntityReference` / string (`:212-237`). A row whose FetchXML didn't project the lookup attribute is DROPPED (fail-closed over-hide).
8. **No paging**: `MoreRecords: false, PagingCookie: null` always (`:214-219`) — per-module `pageSize` must cover the accessible set in one page.

**Registered modules today** (`Infrastructure/DI/ExternalAccessModule.cs`):

| Module | Entity | Scope |
|---|---|---|
| `collaboration` | `sprk_project` | own id ∈ P (`:146-152`) |
| `documents` | `sprk_document` | `sprk_project ∈ P` OR `sprk_matter ∈ M` OR `sprk_workassignment ∈ W` (`:166-176`) |
| `invoices` | `sprk_invoice` | `sprk_matter ∈ M` OR `sprk_project ∈ P` (`:181-190`) |
| `work-assignments` | `sprk_workassignment` | own id ∈ W (`:196-202`) |
| `matters` | `sprk_matter` | own id ∈ M (`:208-214`) |
| `service-requests` | `sprk_servicerequest` | `sprk_requestedby ∈ {callerContactId}`, workforce-only, CIAM always-empty (`:221-230`) |
| `grid-configuration` | `sprk_gridconfiguration` | static 6-id allow-list (`:27-35`, `:240-246`) |

Only `sprk_document` + `sprk_invoice` are CHILD modules. `GET /record/{entity}/{id}` for a child module always 403s (a child's own id is never in a parent-id set — by design, `ExternalModuleRegistry.cs:123-128`). Metadata/savedquery/savedqueries are registered-module-gated schema reads (`ExternalModuleDataEndpoints.cs:318-321, 365-371, 400-403`).

## 5. Caching (Q5)

| Cache | Key shape (via `ITenantCache`) | TTL | Invalidation | Staleness meaning |
|---|---|---|---|---|
| Grant set (per contact) | `tenant:{tid}:external-access-grant:{contactId}:v3` (`ExternalParticipationService.cs:28-34`) | **60 s** (`:18`) | Grant/Revoke/Close endpoints remove the grantee's key (`GrantExternalAccessEndpoint.cs:169-170`, `RevokeExternalAccessEndpoint.cs:163`, `ProjectClosureEndpoint.cs:276`); `InvalidateAsync` (`:149-181`) for standing-grant toggles. **Org grants are NOT fan-out-invalidated** (`GrantExternalAccessEndpoint.cs:157-166`) | Revocation via BFF endpoints: immediate. Grant-row deactivation done directly in Dataverse, and org-grant/org-membership changes: up to 60 s |
| oid → systemuserid | `tenant:{tid}:membership-currentuser:{oid}:v1` | **10 min** (`MembershipEndpoints.cs:75-85`) | TTL only | "a freshly disabled user continues to look like an authenticated systemuser for at most 10 min" (`:80-84`, verbatim) |
| PersonIdentity (systemuser→contact etc.) | `membership-identity` | **10 min** (`IdentityNormalizationService.cs:55`) | TTL only | derived-contact link stale ≤10 min |
| ADR-034 membership result | `tenant:{tid}:membership-resolved:{principal}:{entityType}:{optionsHash}:v3` (`MembershipResolverService.cs:66-84`) | **5 min** | TTL only (Phase-2 invalidation channel reserved, `:70-73`) | removing a member (assigned lookup/team) keeps their external root access up to 5 min; applies to both `ResolveAsync` and `ResolveByContactAsync` (`:481`) |
| Standing-grant flag | **never cached** — live read every compose (`ContactStandingGrantReader.cs:81-104`) | n/a | n/a | flag off → standing TERM gone immediately; the membership it unioned is the 5-min row above |
| App-Role→module map (Tier-1) | `tenant:{tid}:approle-module-map:all:v1` | **60 s** (`ModuleEntitlementResolver.cs:46-51`) | TTL only | UX-only (NFR-06) |
| recordtype-ref (ExternalDataService) | in-memory per instance, no TTL (`ExternalDataService.cs:44-46`) | process | none | metadata only |

**No authorization DECISION is cached** — the composed set is recomputed per request (3 `ComposeAsync` calls per workforce request, `CallerPrincipalResolver.cs:422-427`) over the cached DATA above. Worst-case revocation latency on the external plane: **60 s (grants) / 5 min (membership) / 10 min (disabled systemuser)**.

## 6. Enforcement completeness (Q6)

Every route on the dual-plane group `/api/v1/external` (`ExternalAccessEndpoints.cs:54-100`) passes through `ExternalCollaboration` auth + `CallerPrincipalAuthorizationFilter`. Tier-2 record scope, route by route:

| Route | Tier-2 scope? | Mechanism |
|---|---|---|
| `GET /me`, `GET /me/entitlements` | n/a (self-context) | principal projection |
| `GET /projects` | ✅ | ids from principal (`ExternalProjectDataEndpoints.cs:131`) |
| `GET /projects/{id}`, `/documents`, `/todos`, `/contacts`, `/organizations`; `POST /todos` | ✅ (project dimension ONLY) | `HasProjectAccess` 403 (`:148-150` et al.); create additionally requires `Collaborate` (`:280-284`) |
| `GET /projects/{id}/documents/{documentId}/content` | ✅ | authz-before-stream: `HasProjectAccess` + document→project match, uniform 403 (`:195-216`), app-only SPE stream (`:218-238`) |
| **`PATCH /todos/{id}`** | ❌ **NONE** | any resolved principal (either plane, even with ZERO accessible roots) can update ANY `sprk_todo` by GUID (`:328-345`); acknowledged in-code (`:338-342` "acceptable for now given the app's low blast radius") — written when the group was CIAM-only, now reachable by every workforce user |
| `POST /api/dataverse/fetch` | ✅ | injected OR-filter + `ScopeRows` (§4) |
| `GET /api/dataverse/record/{entity}/{id}` | ✅ | `IsRecordAccessible` BEFORE read (`ExternalModuleDataEndpoints.cs:267-273`) |
| `GET /api/dataverse/metadata|savedquery|savedqueries` | module-gated, no record data | `:318-321, 365-371, 400-403` |

Adjacent surface: the admin group `/api/v1/external-access` (grant/revoke/invite/invite-and-grant/close-project/provision-project) is workforce-default `RequireAuthorization()` with **no role/policy restriction** (`ExternalAccessEndpoints.cs:109-141`; `GrantExternalAccessEndpoint.cs` contains no role check) — ANY authenticated workforce-tenant token can mint or revoke external grants. Out of the read-path scope but security-relevant to UAC-r2's admin layer.

**Answer**: the Tier-2 gate covers every external READ. The single route where a caller reaches (writes) data with no scope filter is `PATCH /api/v1/external/todos/{id}`.

## 7. Extension cost — `sprk_todo` / `sprk_event` / `sprk_communication` as child modules (Q7)

**Core change is genuinely small** — one descriptor per entity in `ExternalAccessModule.AddExternalAccess` (mirror `:166-176`), e.g.:

```csharp
services.AddExternalModule(new ExternalModuleDescriptor {
    Name = "todos", RecordEntity = "sprk_todo",
    ScopeDimensions = new[] {
        new ScopeDimension { Attribute = "sprk_regardingproject",        AccessibleIds = p => p.GetAccessibleProjectIds().ToHashSet() },
        new ScopeDimension { Attribute = "sprk_regardingmatter",         AccessibleIds = p => p.GetAccessibleMatterIds() },
        new ScopeDimension { Attribute = "sprk_regardingworkassignment", AccessibleIds = p => p.GetAccessibleWorkAssignmentIds() },
    }});
```

No registry conflicts (`Register` throws on duplicate entity, `ExternalModuleRegistry.cs:285-291` — none of the three is registered). The FetchXML guard does NOT block it (dims are attributes on the child row). `TryGetAttributeId` already handles lookup-projected `EntityReference` (`:226-230`). `sprk_regardingproject` is verified live (`ExternalDataService.cs:96, 242`); the matter/WA/event/communication regarding logical names must be metadata-verified before wiring.

**It is NOT "just descriptors"** — six real costs/blockers:

1. **Client FetchXML must project every scope-dimension lookup column**, or `ScopeRows` drops all rows (fail-closed 0-row over-hide) — per-widget `sprk_gridconfiguration` fetch changes (`ExternalAccessModule.cs:161-163`, `ExternalModuleRegistry.cs:178`).
2. **Tier-1 entitlement**: CIAM blanket set is exactly `{ "assigned-work" }` (`ModuleEntitlementResolver.cs:44`). New tabs under new module codes need `OutsideCounselModules` extended (code) and/or `sprk_approlemodulemap` rows (data) for workforce.
3. **`grid-configuration` allow-list**: each new DataGrid widget's config record id must be added to the static set (`ExternalAccessModule.cs:27-35`) — code change per grid.
4. **Only 3 root sets exist on `CallerPrincipal`** (P/M/W). `sprk_todo`'s other 8 regarding lookups (invoice, communication, event, …) can NOT confer access without composing new root sets on both strategies — that's 2-hop derivation (child of child), against ADR-034's 1-hop cap.
5. **OR fan-out / paging**: the injector emits one `<value>` per accessible id with no cap (`Tier2ScopeFilterInjector.cs:75-86`); a workforce systemuser with large ADR-034 membership produces a very large IN-list, and the seam does no paging (`ExternalModuleDataEndpoints.cs:214-219`) — accessible rows beyond one page are silently truncated.
6. **Read-only seam**: fetch/record/metadata/savedquery only. Todo/event/communication creates + updates need new scoped endpoints — and the existing unscoped `PATCH /todos/{id}` should be fixed, not reused.

## 8. Briefing accuracy audit (Q8)

### Confirmed (exact or ±3-line match in this worktree)

| Briefing claim | Verified at |
|---|---|
| §1 plane rule (ciamlogin.com / Ciam:TenantId, else workforce) | `CallerPrincipalResolver.cs:234-254` |
| §1 strategy seam `:166-173`; CIAM `:263-343` grant-only (`:316-318`); workforce `:352-451` | exact |
| §1 both strategies fill the same 3 root sets on `CallerPrincipal` (`:90-113`) | `:91, :97, :102` |
| §2 two kinds `ExternalCallerContext.cs:141-150`; SystemUser carries derived nullable ContactId; ContactOnly non-null anchor; ContactOnly has no Email | `:141-150, :169-174, :249-257` |
| §2 systemuser-first order `:88-171`, steps `:109 / :150 / :169-170`, explicit deny | exact |
| §3 gate interface `:39-55`; empty-id deny `:168-171`; Kind switch `:153-161`; systemuser compose `:184-241`; contact compose `:244-301`; 3 root ComposeAsync calls `:422-427` | exact |
| §3 **key extensibility fact** (grant term allow-listed `:99-104` [decl `:103-104`]; membership term generic `:192`, `:276`) | **CONFIRMED** |
| §3 child-derives-from-root note `ExternalCallerContext.cs:92-95` | exact |
| §6 Tier-1: CIAM blanket `{assigned-work}`; workforce roles ∩ `sprk_approlemodulemap`; 60 s cache; not the security boundary | `ModuleEntitlementResolver.cs:44, :46, :89-116` |
| §7 fetch seam; guard `:147-172`; ScopeDimension `:49-58`; OR `in`-filter injection; in-memory re-filter; all-empty ⇒ 0 rows no query | `ExternalModuleDataEndpoints.cs:147-191`, `Tier2ScopeFilterInjector.cs:75-86`, `ExternalModuleRegistry.cs:153-187` |
| §7 documents dims `:166-176`; invoices dims `:181-190`; no per-child grants | `ExternalAccessModule.cs` exact; `ExternalParticipationService.cs:399-404` (sprk_invoice lookup never read) |
| §7 events/communications have NO external read path today | `ExternalDataService.cs` has no event/communication queries; no module registered |
| §11 seams: `ExternalAccessModule.cs:146-246`; `Tier2ScopeFilterInjector`; `ComposeForContactAsync :244-301`; `ResolveByContactAsync :349-491` nulls `RelatedByRole` (`:479`); allow-list `:511+` | exact |

### Wrong / stale / imprecise

1. **§7 "`<link-entity>` joins are rejected" — IMPRECISE.** Only OTHER-entity references are rejected; a `<link-entity>` naming the module's OWN entity (self-join) passes the set-of-names check (`ExternalModuleDataEndpoints.cs:160-161`; extractor `FetchXmlEntityExtractor.cs:94-110`). Aliased columns from an out-of-scope same-entity row can ride out on an accessible primary row; `ScopeRows` vets only the primary row's dimension attributes. Corrected statement: "cross-entity references are rejected; same-entity self-joins are not."
2. **§7 "(There is also an older R1 path… It's legacy; the module seam supersedes it.)" — WRONG as an operational statement.** `ExternalProjectDataEndpoints` is live and mounted on the same dual-plane group (`ExternalAccessEndpoints.cs:92`); it is the ONLY document-download route (`:67-74, :182-247`) and the ONLY todo read/write surface — including the **unscoped `PATCH /todos/{id}`** (`:328-345`). Not superseded; actively load-bearing and carrying the one enforcement gap.
3. **§3 "Composition depends on principal Kind, not plane" — needs a caveat.** True of the service, but the service is reached only from the workforce strategy; the CIAM plane bypasses `ComposeAsync` entirely (`CallerPrincipalResolver.cs:317-341`). "The gate can already be *called* for a child entity type" therefore buys nothing for CIAM partners; their child access flows only through descriptors reading `CallerPrincipal` root sets.
4. **§1 "Workforce plane otherwise (fail-forward default)" — MISLEADING wording.** The default is fail-closed in aggregate: unresolvable workforce callers are denied (`WorkforcePrincipalResolver.cs:162-171`), unregistered planes 401 (`CallerPrincipalResolver.cs:211-223`).
5. **§11 "`ExternalModuleRegistry.cs:49-58` (ScopeDimension, ScopeRows, TryGetAttributeId)" — IMPRECISE.** `:49-58` is `ScopeDimension` only; `ScopeRows` is `:153-187`, `TryGetAttributeId` `:212-237`.
6. **§2/§3 omission (not an error): the SystemUser grant-union email fallback** (`AccessibleRecordSetService.cs:208-213`) silently yields NO grants for a systemuser whose contact is already CIAM-oid-bound (no-hijack rule returns null, `ExternalParticipationService.cs:247-257`) — relevant to the briefing's "parallel workforce/contact access" story.
7. **§11 omission: `AccessibleRecordSetAuthorizationFilter` is attached nowhere** — any UAC-r2 plan that assumes a per-route record gate exists on the workforce surface must first wire it.

## Risks

1. **HIGH — unscoped todo write**: `PATCH /api/v1/external/todos/{id}` lets any resolved caller (CIAM partner or any workforce user with zero grants) modify any `sprk_todo` (`ExternalProjectDataEndpoints.cs:328-345`). The in-code "low blast radius" rationale predates the dual-plane group and sprk_todo's promotion to a first-class 11-parent entity.
2. **MEDIUM — admin group unrestricted**: `/api/v1/external-access/*` (grant/revoke/invite/provision/close) requires only an authenticated workforce token — no role/policy (`ExternalAccessEndpoints.cs:109-141`). Any licensed user can grant external access.
3. **MEDIUM-LOW — self-join seam**: same-entity `<link-entity>` passes the fetch guard (§8.1); exploitability depends on same-entity relationships existing on module entities.
4. **LOW — staleness windows**: direct-in-Dataverse revocation ≤60 s; membership removal ≤5 min; disabled systemuser ≤10 min; org-grant changes ≤60 s (no per-member invalidation, `GrantExternalAccessEndpoint.cs:157-166`). All bounded and documented, but UAC-r2 should state them as accepted SLOs.
5. **LOW — scale**: IN-list fan-out + no paging on the module fetch seam (§7 items 5) will bite first for workforce systemusers with broad ADR-034 membership.
6. **LOW — over-hide**: document download is project-dimension only; matter/WA-rooted documents are listable (module seam) but not downloadable via `/projects/{id}/documents/{docId}/content`.

## Extension assessment

Registering the three child modules is a **days-not-weeks** change on the server (3 descriptors + metadata verification of regarding-lookup logical names), with the true cost in the periphery: widget fetch-column projection, Tier-1 module codes, grid-config allow-list ids, and — if partners must WRITE todos/events — new scoped write endpoints (plus fixing the existing unscoped PATCH). The hard boundary is that scope dimensions can target only the three composed root sets (P/M/W); regarding-lookups at other roots (invoice/event/communication parents) are 2-hop and blocked by both the principal shape and ADR-034's 1-hop cap.

## Open questions

1. Is the unscoped `PATCH /todos/{id}` an accepted risk or a UAC-r2 remediation item? (A scoped fix = read the todo's regarding-project/matter/WA and require dimension membership — the same derivation the fetch seam uses.)
2. Should `/api/v1/external-access/*` admin endpoints gain a role/policy gate as part of UAC-r2's admin layer (they mint the very grants the Tier-2 model trusts)?
3. Should the fetch guard reject `<link-entity>` ELEMENTS outright (not just other-entity names), closing the self-join seam before more entities (with self-lookups, e.g. parent matter) are registered?
4. For CIAM child inheritance, confirm the intended seam is descriptors-only (model A) — the gate-extension path (model B) reaches workforce principals only (§3 nuance).
5. Whose job is wiring `AccessibleRecordSetAuthorizationFilter` to future record-scoped workforce routes — or should it be deleted as dead code until needed?
6. Are the regarding-lookup logical names for `sprk_event` / `sprk_communication` (`sprk_regardingmatter` etc.) confirmed against live metadata? (Only `sprk_todo.sprk_regardingproject` is code-verified here.)
