# Testing Constraints

> **Domain**: Unit and Integration Testing
> **See Also**: [Testing and Code Quality Procedure](../../docs/procedures/testing-and-code-quality.md) (definitive testing strategy)
> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (removed incorrect ADR-022 reference, fixed pattern paths)

---

## When to Load This File

Load when:
- Writing unit tests for new code
- Creating integration tests
- Setting up test fixtures and mocks
- Reviewing test coverage
- Implementing test data builders

---

## MUST Rules

### Test Structure (ADR-022)

- ✅ **MUST** write tests for all code changes
- ✅ **MUST** mirror `src/` directory structure in `tests/unit/`
- ✅ **MUST** use xUnit as the test framework
- ✅ **MUST** use NSubstitute for mocking
- ✅ **MUST** follow Arrange-Act-Assert (AAA) pattern
- ✅ **MUST** name tests using `{Method}_{Scenario}_{ExpectedResult}` convention

### Unit Tests

- ✅ **MUST** test one behavior per test method
- ✅ **MUST** isolate unit tests from external dependencies
- ✅ **MUST** mock all I/O operations (database, HTTP, file system)
- ✅ **MUST** use test data builders for complex object creation
- ✅ **MUST** keep unit tests fast (<100ms per test)

### Integration Tests

- ✅ **MUST** use `WebApplicationFactory<Program>` for API integration tests
- ✅ **MUST** use test containers or in-memory databases where appropriate
- ✅ **MUST** clean up test data after each test run
- ✅ **MUST** use separate test configuration (not production settings)

### Coverage Requirements

- ✅ **MUST** maintain minimum 80% line coverage for new code
- ✅ **MUST** cover all public API endpoints with integration tests
- ✅ **MUST** cover error handling paths (not just happy path)

---

## MUST NOT Rules

### Anti-Patterns

- ❌ **MUST NOT** test implementation details (private methods directly)
- ❌ **MUST NOT** use production databases or services in tests
- ❌ **MUST NOT** create interdependent tests (tests must be isolated)
- ❌ **MUST NOT** ignore or skip tests without documented reason
- ❌ **MUST NOT** use `Thread.Sleep` or arbitrary delays in tests
- ❌ **MUST NOT** hard-code test data that could change (use builders)

### Mocking Anti-Patterns

- ❌ **MUST NOT** mock value objects or DTOs
- ❌ **MUST NOT** over-mock (verify behavior, not implementation)
- ❌ **MUST NOT** use `Arg.Any<>()` when specific values matter

---

## Quick Reference Patterns

### Test Naming

```csharp
// ✅ Good: Clear scenario and expected result
[Fact]
public async Task GetDocument_WhenNotFound_ReturnsNotFound()

// ❌ Bad: Unclear what's being tested
[Fact]
public async Task Test1()
```

### AAA Pattern

```csharp
[Fact]
public async Task CreateContainer_WithValidInput_ReturnsCreatedContainer()
{
    // Arrange
    var request = new CreateContainerRequestBuilder().Build();
    _speFileStore.CreateContainerAsync(Arg.Any<CreateContainerRequest>())
        .Returns(new Container { Id = "123" });

    // Act
    var result = await _sut.CreateContainer(request);

    // Assert
    result.Should().BeOfType<Created<Container>>();
}
```

### Mocking with NSubstitute

```csharp
// Setup
var speFileStore = Substitute.For<ISpeFileStore>();
speFileStore.GetContainerAsync("123").Returns(new Container { Id = "123" });

// Verification
await speFileStore.Received(1).GetContainerAsync("123");
```

---

## Pattern Files (Complete Examples)

- [Unit Test Structure](../patterns/testing/unit-test-structure.md)
- [Mocking Patterns](../patterns/testing/mocking-patterns.md)
- [Integration Tests](../patterns/testing/integration-tests.md)

---

## Authoritative References

- [Testing and Code Quality Procedure](../../docs/procedures/testing-and-code-quality.md) — Module-specific test guidance, coverage targets, architecture test enforcement
- Note: ADR-022 is the PCF Platform Libraries ADR, not a testing strategy ADR. There is no dedicated testing-strategy ADR — testing rules live in the procedure doc above.

---

**Lines**: ~115
