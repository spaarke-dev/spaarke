# ADR-008: Authorization execution model — endpoint filters over global middleware

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-008 Concise](../../.claude/adr/ADR-008-endpoint-filters.md) - ~85 lines, decision + constraints + patterns
- [API Constraints](../../.claude/constraints/api.md) - MUST/MUST NOT rules for API development
- [Endpoint Filters Pattern](../../.claude/patterns/api/endpoint-filters.md) - Authorization filter code examples

**When to load this full ADR**: Authorization pattern decisions, middleware ordering details

---

## Context

Multiple authorization middlewares (`DataverseSecurityContext`, `DocumentAuthorization`, `UacAuthorization`) complicated the pipeline and ran before routing had resolved route values (e.g., `documentId`). Resource-based authorization is cleaner when enforced at the endpoint level.

## Decision

| Rule | Description |
|------|-------------|
| **Minimal global middleware** | Keep global middleware limited to cross-cutting concerns (security headers, exception handling, correlation/telemetry) |
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
| 2 | Exception handling + security headers | Consistent RFC7807 errors + hardening |
| 3 | Endpoint executes | With endpoint authorization filter(s) |

### Authorization Patterns

| Pattern | Implementation |
|---------|----------------|
| Single resource | Endpoint filter: `DocumentAuthorizationFilter(Operation.Read)` |
| AI analysis | Endpoint filters via `AnalysisAuthorizationFilterExtensions` helpers |
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
- [ ] No global authorization middleware performing resource checks
- [ ] Protected endpoints have explicit authorization filter
- [ ] Bulk handlers authorize each item
- [ ] List queries scoped to caller's permissions

## AI-Directed Coding Guidance

- Default: `.RequireAuthorization()` on the route group + add explicit endpoint filters for resource checks.
- Use existing filters/helpers:
	- `DocumentAuthorizationFilter` for document operations
	- `AnalysisAuthorizationFilterExtensions` for analysis routes
- Do not add new global middleware to perform resource authorization; it will not have the right route/body context and will be harder to audit.
