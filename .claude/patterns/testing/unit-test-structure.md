# Unit Test Structure Pattern

## When
Writing xUnit unit tests for BFF API services, endpoint filters, or Dataverse plugins.

## Read These Files
1. `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisContextBuilderTests.cs` — Service tests with AAA and inline builders
2. `tests/unit/Sprk.Bff.Api.Tests/Filters/AiAuthorizationFilterTests.cs` — Filter tests with constructor setup
3. `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` — Plugin tests with exception assertions

## Constraints
- **ADR-022**: xUnit + FluentAssertions + Moq are the approved testing stack

## Key Rules
- Name tests: `{MethodName}_{ExpectedBehavior}_{Condition}`
- Initialize `_sut` and mocks in constructor, not per-test
- Each test verifies one behavior only
- Use `[Theory]` + `[InlineData]` for parameterized variations
- Use inline static builder helpers (not separate builder classes) to keep data close to tests
- Use `action.Should().Throw<T>().WithMessage("*keyword*")` for exception assertions
