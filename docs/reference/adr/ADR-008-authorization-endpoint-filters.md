# ADR-008: Authorization execution model — endpoint filters over global middleware

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

Multiple authorization middlewares (`DataverseSecurityContext`, `DocumentAuthorization`, `UacAuthorization`) complicated the pipeline and ran before routing had resolved route values (e.g., `documentId`). Resource-based authorization is cleaner when enforced at the endpoint level.

## Decision

| Rule | Description |
|------|-------------|
| **One context middleware** | `SpaarkeContextMiddleware` resolves minimal request context (user/tenant/principal/correlation) |
| **Endpoint-level auth** | Resource-based authorization via endpoint filters (Minimal API) or policy/handlers (Controllers) |
| **Remove global auth** | No global middlewares performing resource checks |

## Consequences

**Positive:**
- Authorization logic sits next to endpoints, sees route values and DTOs, easy to reason about
- Simpler pipeline; fewer unexpected interactions

**Negative:**
- Slightly more boilerplate at endpoint definition (one filter per protected route)

## Alternatives Considered

Monolithic `DocumentSecurityMiddleware`. **Rejected** because it lacks route/body context and becomes a god-middleware.

## Operationalization

### Middleware Pipeline

| Order | Middleware | Purpose |
|-------|------------|---------|
| 1 | Standard ASP.NET (auth, routing) | Framework |
| 2 | `SpaarkeContextMiddleware` | Enrich context (user, tenant, correlation) |
| 3 | Endpoint executes | With authorization filter |

### Authorization Patterns

| Pattern | Implementation |
|---------|----------------|
| Single resource | Endpoint filter: `DocumentAuthorizationFilter(Operation.Read)` |
| Composite checks | Handler calls `AuthorizationService` directly |
| Bulk operations | Handler calls `AuthorizationService` for each item |
| List endpoints | Authorization at query construction (scoped to caller) |

## Exceptions

Bulk/list endpoints apply authorization at query construction time inside handlers (scoped to caller).

## Success Metrics

| Metric | Target |
|--------|--------|
| Auth-related defects | Fewer |
| 401/403 mapping | Clear |
| Pipeline length | Short |
| Endpoint security | Readable |

## Compliance

**Code review checklist:**
- [ ] No global authorization middlewares (except `SpaarkeContextMiddleware`)
- [ ] Protected endpoints have explicit authorization filter
- [ ] Bulk handlers authorize each item
- [ ] List queries scoped to caller's permissions
