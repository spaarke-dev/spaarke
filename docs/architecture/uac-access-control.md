# Unified Access Control (UAC) Architecture

> **Domain**: Authorization, Access Control, Permission Management
> **Status**: Verified (Production-Ready Internal; Design External)
> **Last Updated**: 2026-08-20
> **Last Reviewed**: 2026-08-20
> **Reviewed By**: unified-access-control-r2 (drift correction); previously ai-procedure-refactoring-r2 (2026-04-05)
> **Source ADRs**: ADR-003, ADR-008, ADR-009, ADR-028 (Amendment A1 — broker-only external access)

> **Verification note (2026-04-05)**: All 6 referenced classes confirmed present in code — `AuthorizationService` (Spaarke.Core/Auth), `OperationAccessPolicy` (Spaarke.Core/Auth), `CachedAccessDataSource` (Sprk.Bff.Api/Infrastructure/Caching), `DocumentAuthorizationFilter`, `AiAuthorizationFilter` (Sprk.Bff.Api/Api/Filters), `DataverseAccessDataSource` (Spaarke.Dataverse).

> **Correction note (2026-08-20, `unified-access-control-r2`)**: This doc previously described several DESIGN-era behaviors that were never built (three-plane grant orchestration, Power Pages external access, `RetrievePrincipalAccess` app-only mode, SPE container roles per access level). Corrected against code; unbuilt behavior is now marked NOT IMPLEMENTED. External access is broker-only per ADR-028 A1: a grant writes one `sprk_externalrecordaccess` row and invalidates one Redis cache entry (`Api/ExternalAccess/GrantExternalAccessEndpoint.cs:98-117`).

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Three-plane access model | Dataverse records (native security), SPE files, AI Search — each plane requires independent access management. A grant writes Plane 1 data only (one `sprk_externalrecordaccess` row); Plane 2 is broker-only (BFF app-only Graph — external users are never added to containers); Plane 3 external filtering is NOT IMPLEMENTED. See "Three-Plane Access Model" below |
| Fail-closed design | Any error, unknown operation, missing access data, or no rule decision → deny. Security boundary must never fail open |
| `OperationAccessPolicy` maps Graph ops to `AccessRights` | 66 operations mapped (56 canonical + 10 legacy aliases; `Spaarke.Core/Auth/OperationAccessPolicy.cs`); download requires `Write` (not just Read) for security compliance |
| Direct-query `DataverseAccessDataSource` | BOTH auth modes (app-only service principal AND OBO) use the same direct query (`GET sprk_documents({id})?$select=sprk_documentid`, `Spaarke.Dataverse/DataverseAccessDataSource.cs:323`) and grant at most `Read` (`:368-372`). `RetrievePrincipalAccess` has ZERO call sites in this path — it appears only in comments |
| `CachedAccessDataSource` decorator (ADR-009) | Cache permission **data**, not decisions; fail-open on Redis errors (falls through to Dataverse) |
| Endpoint filters, not global middleware (ADR-008) | 23 domain-specific filters apply authorization at endpoint level |
| Single `OperationAccessRule` | Dataverse's own row-level security (roles, teams, business units, record sharing) is enforced by the direct-query probe — the record is only retrievable if the probe identity can read it — so one rule is sufficient. NOTE: `AuthorizationService` passes `userAccessToken: null` (`Spaarke.Core/Auth/AuthorizationService.cs:48-52`), so the probe runs as the service principal, NOT scoped to the calling user |

---

## Three-Plane Access Model

| Plane | What It Controls | Internal Mechanism | External Mechanism |
|-------|-----------------|-------------------|-------------------|
| **Plane 1: Dataverse Records** | CRUD access to Dataverse rows | Security roles, teams, BU, record sharing | `sprk_externalrecordaccess` grant rows, read by the BFF (`Infrastructure/ExternalAccess/ExternalParticipationService.cs`). External callers are Static Web Apps + Entra External ID (CIAM) — Power Pages is retired |
| **Plane 2: SPE Files** | Read/write/delete files in SharePoint Embedded containers | BFF `AuthorizationService` → `OperationAccessPolicy` | Broker-only (ADR-028 A1): external users never authenticate to SPE; all external file access is app-only via the BFF. Contacts are NOT added to containers |
| **Plane 3: AI Search** | Query results from Azure AI Search | BFF constructs filter from user's accessible entities | NOT IMPLEMENTED — no CIAM/external route reaches AI Search |

**What a grant actually does** (corrected 2026-08-20 — the old "three-plane orchestration" was never built): granting external access = (1) create ONE `sprk_externalrecordaccess` record + (2) invalidate the contact's Redis participation cache (`Api/ExternalAccess/GrantExternalAccessEndpoint.cs:13-23, 98-117`). No web role is assigned (Power Pages retired), and no SPE container membership is written — `SpeContainerMembershipService.GrantMembershipAsync` exists but has NO production callers. Revoking = deactivate the record + invalidate the cache, plus a defensive removal of any stray SPE container permission when a `ContainerId` is supplied (`Api/ExternalAccess/RevokeExternalAccessEndpoint.cs:93-147`).

---

## Dual-Mode DataverseAccessDataSource

Two auth modes exist, but BOTH run the SAME direct-query check (`Spaarke.Dataverse/DataverseAccessDataSource.cs:323`) — `RetrievePrincipalAccess` is NOT called in either mode (zero call sites; corrected 2026-08-20):

| Auth Mode | When Used | Method |
|-----------|-----------|--------|
| **App-only** | Default — `AuthorizationService` always passes `userAccessToken: null` (`Spaarke.Core/Auth/AuthorizationService.cs:48-52`) | Direct query: `GET sprk_documents({id})?$select=sprk_documentid` with the service principal token |
| **OBO** | Only when a caller passes a user token (e.g. `AiAuthorizationService`) | Same direct query with the OBO-exchanged user token |

Direct query pattern: query the document directly → 200 = grant `AccessRights.Read` (at most — Write/Delete etc. are never granted by this probe, `:368-372`); 403/404 = access denied (empty permission set). In app-only mode the probe runs as the service principal, so it is NOT scoped to the calling user's Dataverse privileges. See [sdap-auth-patterns.md Pattern 5](sdap-auth-patterns.md) for the OBO bugs that were fixed.

---

## Redis Caching TTLs (ADR-009)

Actual keys per `Infrastructure/Caching/CachedAccessDataSource.cs:17-19, 65, 153, 180` (corrected 2026-08-20 — plain `IDistributedCache`, NOT `ITenantCache`; no tenant segment, no version suffix):

| Data | Cache Key Pattern | TTL |
|------|-------------------|-----|
| User Roles | `sdap:auth:roles:{userOid}` | 2 min |
| Team Memberships | `sdap:auth:teams:{userOid}` | 2 min |
| Resource Access | `sdap:auth:access:{userOid}:{resourceId}` | 60 sec |

Fail-open on Redis errors: falls through to Dataverse. Cache stores permission **data**, not decisions (allows rule changes without cache invalidation).

The EXTERNAL participation cache is separate and DOES use `ITenantCache`: tenant-scoped, resource `external-access-grant`, contact-id component, version 3 (`Infrastructure/ExternalAccess/ExternalParticipationService.cs:28-34`), 60s TTL — invalidated by the grant/revoke endpoints.

---

## Endpoint Filters (ADR-008)

23 domain-specific filters in `Api/Filters/` (plus `AccessibleRecordSetAuthorizationFilter` and `CallerPrincipalAuthorizationFilter` wiring for the external surface in `Api/ExternalAccess/`), each applied per-endpoint:

| Filter | Domain |
|--------|--------|
| `DocumentAuthorizationFilter` | General document access |
| `AiAuthorizationFilter` | AI analysis access |
| `AnalysisAuthorizationFilter` | Document analysis |
| `CommunicationAuthorizationFilter` | Email operations |
| `FinanceAuthorizationFilter` | Finance module |
| + 18 more | Various domains |

NOT all filters call `AuthorizeAsync` (corrected 2026-08-20). The `oid` → `AuthorizationContext`/`OperationAccessPolicy` → `AuthorizationService.AuthorizeAsync()` → 403-with-deny-code pattern is followed by 4 filters (`DocumentAuthorizationFilter:79`, `EntityAccessFilter:154`, `FinanceAuthorizationFilter:85`, `OfficeDocumentAccessFilter:132`); 3 more route through `IAiAuthorizationService.AuthorizeAsync` instead (`AiAuthorizationFilter:83`, `AnalysisAuthorizationFilter:140`, `VisualizationAuthorizationFilter:106`). The remaining filters apply domain-specific checks (job ownership, webhook signatures, rate limits, tenant scoping, caller-principal resolution, record∈accessible-set, etc.) without going through `AuthorizationService`.

---

## Fail-Closed Scenarios

| Scenario | Result |
|----------|--------|
| Dataverse query fails | **Deny** |
| No rule makes a decision | **Deny** |
| User has `AccessRights.None` | **Deny** |
| Unknown operation string | **Deny** |
| Any exception | **Deny** |
| External caller whose token cannot be resolved to a Contact | **Deny** (403 `sdap.access.deny.contact_not_found`, `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:312-313`) |
| External caller requesting a record outside their grant set | **Deny** (403 `sdap.access.deny.record_not_in_accessible_set`, `Api/ExternalAccess/AccessibleRecordSetAuthorizationFilter.cs:137-146`) |
| External caller with zero active grants, on list endpoints | **200 with empty results** — NOT a 403 (corrected 2026-08-20). A resolved Contact with no grants gets an empty grant set (`CallerPrincipalResolver.cs:319-341`); e.g. `/api/v1/external/me` returns an empty project list |

Deny codes follow pattern `{domain}.{area}.{action}.{reason}` (e.g., `sdap.access.deny.insufficient_rights`, `sdap.access.deny.unknown_operation`).

---

## External Caller Access Levels

Actual level → effective-rights mapping per `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:126-134` (corrected 2026-08-20). The former "SPE Container Role" column reflected `SpeContainerMembershipService`'s role map, which is NOT wired — external SPE access is broker-only and no container role is ever assigned:

| Access Level | Effective `AccessRights` (BFF) | SPE Container Role |
|-------------|-------------------------------|--------------------|
| View Only | Read | n/a — NOT IMPLEMENTED (broker-only) |
| Collaborate | Read + Create + Write | n/a — NOT IMPLEMENTED (broker-only) |
| Full Access | Read + Create + Write + Delete | n/a — NOT IMPLEMENTED (broker-only) |

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "Access Denied" despite permissions | Cache staleness | Wait 60s–2min TTL |
| "Unknown operation" error | Operation not in policy | Use valid operation from `OperationAccessPolicy` |
| External user denied despite participation | Grant row inactive / wrong root lookup, or stale participation cache | Check the `sprk_externalrecordaccess` row is Active and its typed root lookup is bound; cache refreshes within 60s. (The former "BFF must add Contact to container via Graph" advice described unbuilt behavior — external SPE access is broker-only, no container membership exists) |
| AI Search returns no results for external | External AI Search filtering is NOT IMPLEMENTED — no CIAM route reaches AI Search | Expected behavior until an external search plane is built |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | OBO patterns including Pattern 5 (direct query fix) |
| [external-access-spa-architecture.md](external-access-spa-architecture.md) | External SPA three-plane access detail |
| `.claude/patterns/auth/uac-access-control.md` | Concise implementation guide |
| `.claude/constraints/auth.md` | MUST/MUST NOT rules |

---

*Last Updated: 2026-08-20 — corrected against code by the `unified-access-control-r2` investigation (three-plane orchestration, Power Pages retirement, direct-query-only access probe, cache keys, filter patterns, fail-closed semantics, access-level rights mapping).*
