# ADR-008: Authorization execution model — endpoint filters over global middleware
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
Multiple authorization middlewares (DataverseSecurityContext, DocumentAuthorization, UacAuthorization) complicated the the pipeline and ran before routing had resolved route values (e.g., `documentId`). Resource-based authorization is cleaner when enforced at the endpoint level.

## Decision
- Keep one **context enrichment** middleware (`SpaarkeContextMiddleware`) that resolves minimal request context (user/tenant/principal/correlation).
- Perform **resource-based authorization** per endpoint using **endpoint filters** (Minimal API) or policy/handlers (Controllers) that call `AuthorizationService`.
- Remove global authorization middlewares that perform resource checks.

## Consequences
Positive:
- Authorization logic sits next to endpoints, sees route values and DTOs, and is easy to reason about.
- Simpler pipeline; fewer unexpected interactions.
Negative:
- Slightly more boilerplate at endpoint definition (one filter per protected route).

## Alternatives considered
- A monolithic `DocumentSecurityMiddleware`. Rejected because it lacks route/body context and becomes a god-middleware.

## Operationalization
- Replace the three security middlewares with `SpaarkeContextMiddleware` + endpoint filters (e.g., `DocumentAuthorizationFilter(Operation.Read)`).
- Handlers must call `AuthorizationService` for composite checks and bulk operations.
- Update design docs and diagrams to show context middleware + endpoint-level authorization.

## Exceptions
Bulk/list endpoints apply authorization at query construction time inside handlers (scoped to caller).

## Success metrics
- Fewer auth-related defects; clear 401/403 mapping.
- Pipeline remains short and predictable; improved readability of endpoint security.
