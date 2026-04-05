# Mocking Patterns

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Setting up test doubles for unit or integration tests (Moq for interfaces, WireMock for HTTP, custom fakes for stateful behavior).

## Read These Files
1. `tests/unit/Sprk.Bff.Api.Tests/Mocks/FakeGraphClientFactory.cs` — Custom fake for Graph client
2. `tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseWebApiWireMockTests.cs` — WireMock request matching and stateful retry
3. `tests/unit/Sprk.Bff.Api.Tests/AuthorizationTests.cs` — Moq setup, verification, and test data source

## Constraints
- **ADR-022**: Moq 4.20.70 and WireMock.Net 1.5.45 are the approved mocking libraries

## Key Rules
- Prefer `MockBehavior.Strict` only when detecting unexpected calls is the test goal
- Use `SetupSequence` for retry/multi-call flows
- Always call `_mockServer.Stop()` and `.Dispose()` in `IDisposable.Dispose()`
- Use `TestLogger<T>` (custom fake) to capture log output — do not mock `ILogger` with Moq
- Verify call counts with `mockService.Verify(…, Times.Once)` after Act, not before
