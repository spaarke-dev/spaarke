# Spaarke Codebase Assessment

_Date: 2025-10-02_

## Repository Snapshot
- **Structure**: Primary application code lives in `spaarke/src`, anchored by the Spe BFF minimal API (`src/api/Spe.Bff.Api`), shared libraries (`src/shared/Spaarke.Core`, `src/shared/Spaarke.Dataverse`), Microsoft Dataverse entity exports (`src/Entities`), and prototype agents.
- **Runtime entry**: `Program.cs` wires minimal API endpoints, DI modules, Microsoft Graph access, Dataverse services, caching, and background workers.
- **Testing baseline**: `tests/unit/Spe.Bff.Api.Tests` validates endpoint wiring, helper utilities, and background services. Integration and E2E folders exist but do not yet contain implementations.

## Architecture
### Strengths
- **Composed services**: DI helpers (`Infrastructure/DI/*.cs`) encapsulate authorization, document orchestration, and worker setup, keeping cross-cutting concerns isolated and reusable.
- **Minimal API organization**: Endpoint modules (`Api/*.cs`) group related routes, centralize validation, and return uniform `ProblemDetails` responses, aligning with ADR guidance.
- **Integration seams**: `Infrastructure/Graph` centralizes app-only and OBO Microsoft Graph clients via `GraphClientFactory`; `Spaarke.Dataverse` presents a clean interface for Dataverse CRUD operations.
- **Background design**: `Services/BackgroundServices` and `Services/Jobs` incorporate idempotency tracking, retry-aware processing, and Azure Service Bus scaffolding for asynchronous workflows.

### Weaknesses / Risks
- **Authorization effectively disabled**: Policies in `Program.cs` (`RequireAssertion(_ => true)`) and the placeholder `DataverseAccessDataSource` yield permissive access, leaving rule evaluation toothless.
- **Dual Dataverse implementations**: Both `DataverseService` (Power Platform client) and `DataverseWebApiService` (REST) sit behind `IDataverseService`, creating redundancy and confusion without a clear migration path.
- **OBO functionality stubbed**: `Services/OboSpeService` still returns sample data or `null` because Graph SDK v5 changes were never reconciled, so user-context endpoints cannot function in production.
- **Background duality**: An in-memory `JobProcessor` and Service Bus–driven `DocumentEventProcessor` coexist without a unified operational story, complicating deployment expectations.

## Component Approach
### Strengths
- **Graph abstraction**: `SpeFileStore` wraps Graph container and drive operations, enabling consistent logging, retry application, and DTO mapping.
- **Resilience primitives**: `Infrastructure/Resilience/RetryPolicies.cs` standardizes transient-error handling with Polly; the idempotency service leverages distributed cache semantics for event deduplication.
- **Shared core libraries**: `Spaarke.Core.Auth` and `Spaarke.Core.Cache` provide reusable authorization rule chains and per-request caching supporting the API and future services.

### Weaknesses / Risks
- **Monolithic helpers**: `SpeFileStore` exceeds 600 lines, mixing upload sessions, downloads, and container management, making maintenance difficult and increasing regression risk.
- **Manual resilience wiring**: Callers construct retry policies inline for each Graph operation, making it easy to omit resilience guards on new code paths; centralized handlers are absent.
- **DTO duplication**: Overlapping models live in both API and shared layers without dedicated mapping utilities, spreading transformation logic across handlers.
- **Configuration friction**: `GraphClientFactory` demands numerous environment variables (`UAMI_CLIENT_ID`, `TENANT_ID`, `API_APP_ID`, etc.) without defaults or clear documentation, creating a brittle bootstrap experience.

## Code Quality
### Strengths
- **Verbose logging**: Handlers and services log contextual identifiers and operation phases, aiding observability and troubleshooting.
- **Testing foundation**: Unit suites cover authorization rules, endpoint grouping, caching helpers, background processing, and error helpers, offering a foundation for broader coverage.
- **Validation utilities**: Helpers (`ProblemDetailsHelper`, `PathValidator`, `FileOperationExtensions`) consolidate common validation logic and keep endpoints tidy.

### Weaknesses / Risks
- **Placeholder logic prevails**: TODOs for rate limiting, telemetry, Graph operations, and Dataverse access remain unresolved, leaving critical runtime scenarios unimplemented.
- **Namespace drift**: Some files (e.g., `Services/OboSpeService.cs`) use mismatched namespaces (`namespace Services`) relative to repository conventions (`Spe.Bff.Api.Services`).
- **Inconsistent style**: Varied use of records vs. classes and mixed response patterns (`Results.*` vs. helper wrappers) create cognitive friction for contributors.
- **Test realism gaps**: Existing tests validate placeholder behaviors (sample data, stubbed idempotency) rather than real Graph/Dataverse interactions, limiting future safety nets.

## Recommendations
1. **Restore Graph OBO end-to-end** by upgrading the SDK or wrapping REST calls, and gate endpoints behind feature flags until fully functional.
2. **Complete authorization wiring**: implement `DataverseAccessDataSource` to return real access snapshots and enforce authorization policies (`canmanagecontainers`, `canwritefiles`).
3. **Standardize Dataverse access** on the preferred implementation, retiring legacy alternatives to reduce duplication and confusion.
4. **Refactor `SpeFileStore`** into smaller services (container, drive, upload) and layer resilience via shared delegating handlers instead of per-call policy creation.
5. **Document configuration** requirements (UAMI client IDs, tenant IDs, secrets) in `README.md` or deployment runbooks to minimize setup friction.
6. **Finalize background processing strategy**: choose Service Bus or in-memory queues, wire outcomes to persistence/metrics, and remove unused pathways.
7. **Tighten conventions** using `EditorConfig` or analyzers to enforce namespace patterns, response handling, and logging rules.
8. **Expand automated testing** with integration suites that mock Graph/Dataverse endpoints, covering throttling, authorization failures, and CRUD error cases.

## Quality Gates (Current Session)
- Build / lint / tests: _Not run (read-only assessment; no code changes executed)._ 

## Requirements Coverage
- Overview of `spaarke/src` structure – **Done**
- Architecture strengths & weaknesses – **Done**
- Component approach assessment – **Done**
- Code quality assessment – **Done**
