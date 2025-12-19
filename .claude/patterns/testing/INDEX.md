# Testing Patterns Index

> **Domain**: Testing Strategy
> **Last Updated**: 2025-12-19

---

## When to Load

Load these patterns when:
- Writing new unit tests
- Setting up integration tests
- Creating mocks or test data
- Validating ADR compliance via architecture tests
- Configuring test coverage

---

## Test Structure

```
tests/
├── unit/                          # Unit tests
│   ├── Sprk.Bff.Api.Tests/        # BFF API tests (~50 files)
│   ├── Spaarke.Plugins.Tests/     # Plugin tests
│   └── Spaarke.Core.Tests/        # Core library tests
├── integration/                   # Integration tests
│   └── Spe.Integration.Tests/     # End-to-end API tests
└── Spaarke.ArchTests/             # Architecture validation
```

---

## Available Patterns

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [unit-test-structure.md](unit-test-structure.md) | AAA pattern, naming, assertions | ~100 |
| [mocking-patterns.md](mocking-patterns.md) | Moq, WireMock, custom fakes | ~110 |
| [integration-tests.md](integration-tests.md) | WebApplicationFactory, HTTP tests | ~90 |

---

## Test Framework Stack

| Package | Version | Purpose |
|---------|---------|---------|
| xUnit | 2.9.0 | Test framework |
| FluentAssertions | 6.12.0 | Assertion library |
| Moq | 4.20.70 | Mocking framework |
| WireMock.Net | 1.5.45 | HTTP mocking |
| coverlet.collector | 6.0.2 | Code coverage |

---

## Coverage Targets

| Category | Target |
|----------|--------|
| Core services | 80%+ |
| Endpoints | 70%+ |
| Utilities | 90%+ |

---

## Architecture Tests (ADR Compliance)

| Test File | ADR | Validates |
|-----------|-----|-----------|
| ADR001_MinimalApiTests.cs | ADR-001 | No Azure Functions |
| ADR002_PluginTests.cs | ADR-002 | Thin plugins |
| ADR007_GraphIsolationTests.cs | ADR-007 | Graph types contained |
| ADR008_AuthorizationTests.cs | ADR-008 | Endpoint filters used |

---

## Running Tests

```bash
# All tests
dotnet test

# Specific project
dotnet test tests/unit/Sprk.Bff.Api.Tests/

# With coverage
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings

# Filter by trait
dotnet test --filter "Category=Unit"
```

---

**Lines**: ~80
