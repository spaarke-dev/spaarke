# Integration Tests Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Writing end-to-end HTTP tests against the BFF API, or architecture compliance tests enforcing ADR constraints.

## Read These Files
1. `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` — WebApplicationFactory with service replacement and in-memory config
2. `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` — Shared fixture wiring
3. `tests/integration/Spe.Integration.Tests/SystemIntegrationTests.cs` — IClassFixture usage, HTTP assertions, ProblemDetails validation
4. `tests/Spaarke.ArchTests/ADR001_MinimalApiTests.cs` — NetArchTest pattern for ADR enforcement

## Constraints
- **ADR-001**: Architecture tests must assert no Azure Functions dependency
- **ADR-007**: Architecture tests must assert Graph types don't appear outside Infrastructure namespace
- **ADR-008**: Architecture tests must assert endpoint filters are used for auth

## Key Rules
- Always disable Redis and external AI in test config (`"Redis:Enabled": "false"`)
- Replace `IGraphClientFactory` with `FakeGraphClientFactory` via `services.RemoveAll<T>()` + re-register
- Use `IClassFixture<T>` to share factory across tests — never create per-test factories
- Error responses MUST use `application/problem+json` content type
