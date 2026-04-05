# Unified Access Control (UAC) Architecture

> **Domain**: Authorization, Access Control, Permission Management
> **Status**: Verified (Production-Ready Internal; Design External)
> **Last Updated**: 2026-03-16
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Source ADRs**: ADR-003, ADR-008, ADR-009

> **Verification note (2026-04-05)**: All 6 referenced classes confirmed present in code — `AuthorizationService` (Spaarke.Core/Auth), `OperationAccessPolicy` (Spaarke.Core/Auth), `CachedAccessDataSource` (Sprk.Bff.Api/Infrastructure/Caching), `DocumentAuthorizationFilter`, `AiAuthorizationFilter` (Sprk.Bff.Api/Api/Filters), `DataverseAccessDataSource` (Spaarke.Dataverse).

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Three-plane access model | Dataverse records (native security), SPE files (Graph container membership), AI Search (query-time filter) — each plane requires independent access management; a single grant orchestrates all three |
| Fail-closed design | Any error, unknown operation, missing access data, or no rule decision → deny. Security boundary must never fail open |
| `OperationAccessPolicy` maps Graph ops to `AccessRights` | 70+ operations mapped; download requires `Write` (not just Read) for security compliance |
| Dual-mode `DataverseAccessDataSource` | App-only uses `RetrievePrincipalAccess` (works with service principal); OBO uses direct query (`GET /sprk_documents({id})`) because `RetrievePrincipalAccess` returns 404 with delegated tokens |
| `CachedAccessDataSource` decorator (ADR-009) | Cache permission **data**, not decisions; fail-open on Redis errors (falls through to Dataverse) |
| Endpoint filters, not global middleware (ADR-008) | 12+ domain-specific filters apply authorization at endpoint level |
| Single `OperationAccessRule` | Dataverse `RetrievePrincipalAccess` already factors in security roles, team memberships, business units, record sharing, and field-level security — one rule is sufficient |

---

## Three-Plane Access Model

| Plane | What It Controls | Internal Mechanism | External Mechanism |
|-------|-----------------|-------------------|-------------------|
| **Plane 1: Dataverse Records** | CRUD access to Dataverse rows | Security roles, teams, BU, record sharing | Power Pages table permissions (web role → parent chain) |
| **Plane 2: SPE Files** | Read/write/delete files in SharePoint Embedded containers | BFF `AuthorizationService` → `OperationAccessPolicy` | Same + SPE container membership via Graph |
| **Plane 3: AI Search** | Query results from Azure AI Search | BFF constructs filter from user's accessible entities | BFF constructs filter from contact's participation records |

**Three-plane orchestration**: Granting external access = (1) create `sprk_externalrecordaccess` record + (2) assign web role + (3) add Contact Entra UPN to SPE container via Graph. Revoking = deactivate record + remove from container + remove web role if no other participation.

---

## Dual-Mode DataverseAccessDataSource

| Auth Mode | When Used | Method |
|-----------|-----------|--------|
| **App-only** | Background jobs, webhooks, no user context | `RetrievePrincipalAccess` API |
| **OBO** | User-initiated BFF requests | Direct query: `GET /sprk_documents({id})?$select=sprk_documentid` |

OBO direct query pattern: exchange user's bearer token for Dataverse-scoped token via MSAL OBO → query document directly → 200 = Read access granted (Dataverse enforces row-level security); 403/404 = access denied. See [sdap-auth-patterns.md Pattern 5](sdap-auth-patterns.md) for the OBO bugs that were fixed.

---

## Redis Caching TTLs (ADR-009)

| Data | Cache Key Pattern | TTL |
|------|-------------------|-----|
| User Roles | `sdap:auth:roles:{userOid}` | 2 min |
| Team Memberships | `sdap:auth:teams:{userOid}` | 2 min |
| Resource Access | `sdap:auth:access:{userOid}:{resourceId}` | 60 sec |

Fail-open on Redis errors: falls through to Dataverse. Cache stores permission **data**, not decisions (allows rule changes without cache invalidation).

---

## Endpoint Filters (ADR-008)

12+ domain-specific filters, each applied per-endpoint:

| Filter | Domain |
|--------|--------|
| `DocumentAuthorizationFilter` | General document access |
| `AiAuthorizationFilter` | AI analysis access |
| `AnalysisAuthorizationFilter` | Document analysis |
| `CommunicationAuthorizationFilter` | Email operations |
| `FinanceAuthorizationFilter` | Finance module |
| + 7 more | Various domains |

All filters follow the same pattern: extract user `oid` claim → build `AuthorizationContext` with operation from `OperationAccessPolicy` → call `AuthorizationService.AuthorizeAsync()` → return 403 with deny reason code if not allowed.

---

## Fail-Closed Scenarios

| Scenario | Result |
|----------|--------|
| Dataverse query fails | **Deny** |
| No rule makes a decision | **Deny** |
| User has `AccessRights.None` | **Deny** |
| Unknown operation string | **Deny** |
| Any exception | **Deny** |
| External caller with no active participation | **Deny** |

Deny codes follow pattern `{domain}.{area}.{action}.{reason}` (e.g., `sdap.access.deny.insufficient_rights`, `sdap.access.deny.unknown_operation`).

---

## External Caller Access Levels

| Access Level | SPE Container Role | Dataverse (via table permissions) |
|-------------|-------------------|----------------------------------|
| View Only | Reader | Read |
| Collaborate | Writer | Read + Create |
| Full Access | Writer | Read + Create + Write |

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "Access Denied" despite permissions | Cache staleness | Wait 60s–2min TTL |
| "Unknown operation" error | Operation not in policy | Use valid operation from `OperationAccessPolicy` |
| External user denied despite participation | SPE container membership missing | BFF must add Contact to container via Graph API |
| AI Search returns no results for external | Missing project IDs in filter | BFF must include project IDs from participation records |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | OBO patterns including Pattern 5 (direct query fix) |
| [external-access-spa-architecture.md](external-access-spa-architecture.md) | External SPA three-plane access detail |
| `.claude/patterns/auth/uac-access-control.md` | Concise implementation guide |
| `.claude/constraints/auth.md` | MUST/MUST NOT rules |

---

*Last Updated: 2026-03-16*
