# SDAP Refactor Playbook (v2)
Date: 2025-09-27

This playbook consolidates approved simplifications and shows exactly how to implement them in code and docs.

## 1) Runtime Standardization (ADR-001, ADR-002)
- Minimal API for sync endpoints; BackgroundService workers for async via Service Bus.
- Plugins limited to validation/projection; no orchestration.

## 2) Storage Seam Simplification (ADR-007)
- Replace generic `IResourceStore`/adapters with a single `SpeFileStore` facade.
- Configure Graph retry/correlation inside the facade; return SDAP DTOs only.

## 3) Authorization Execution Model (ADR-008)
- One `SpaarkeContextMiddleware` for context enrichment.
- Resource authorization via endpoint filters calling `AuthorizationService`.

## 4) Caching Strategy (ADR-009)
- Redis-only cross-request cache + per-request cache; short TTLs; versioned keys.
- No decision caching; cache inputs (snapshots) only.

## 5) DI Minimalism (ADR-010)
- Concretes by default; two seams (IAccessDataSource, IAuthorizationRule set).
- Feature-module registration; one typed client per upstream.

## 6) Verification Checklist
- CI audit passes (no Functions/WebJobs; DI minimalism).
- Endpoints protected by filters; 401/403 behavior verified.
- Redis hit rates plotted; Dataverse reads reduced.
- No Graph types referenced outside `SpeFileStore`.

## 7) Agent Prompts (delta)
- Update DI to minimal block; remove extra services/interfaces.
- Replace auth middlewares with endpoint filters; wire AuthorizationService.
- Replace IResourceStore usage with SpeFileStore; delete thin adapters.
- Remove HybridCache; add Redis helpers + RequestCache.
