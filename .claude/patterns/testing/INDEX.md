# Testing Patterns Index

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

> Pointer-based pattern files for unit tests, integration tests, and mocking.
> Each file points to canonical source code — read the code, not descriptions.

| Pattern | When to Load | Last Reviewed | Status |
|---------|-------------|---------------|--------|
| [unit-test-structure.md](unit-test-structure.md) | Writing xUnit unit tests (AAA, naming, assertions) | 2026-04-05 | Verified |
| [mocking-patterns.md](mocking-patterns.md) | Setting up Moq, WireMock, or custom fakes | 2026-04-05 | Verified |
| [integration-tests.md](integration-tests.md) | WebApplicationFactory E2E tests, arch compliance tests | 2026-04-05 | Verified |

## Test Structure
```
tests/
├── unit/Sprk.Bff.Api.Tests/          # BFF API unit tests
├── unit/Spaarke.Plugins.Tests/        # Plugin unit tests
├── integration/Spe.Integration.Tests/ # E2E API tests
└── Spaarke.ArchTests/                 # ADR compliance tests (NetArchTest)
```

## Stack
xUnit 2.9.0 + FluentAssertions 6.12.0 + Moq 4.20.70 + WireMock.Net 1.5.45

## Run
`dotnet test` · Coverage: `dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings`
