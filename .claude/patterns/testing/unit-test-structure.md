# Unit Test Structure Pattern

> **Domain**: Testing / Unit Tests
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-022

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisContextBuilderTests.cs` | Service tests |
| `tests/unit/Sprk.Bff.Api.Tests/Filters/AiAuthorizationFilterTests.cs` | Filter tests |
| `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` | Plugin tests |

---

## Arrange-Act-Assert Pattern

```csharp
[Fact]
public async Task MethodName_ExpectedBehavior_WhenCondition()
{
    // Arrange
    var mockDependency = new Mock<IDependency>();
    mockDependency.Setup(d => d.GetDataAsync()).ReturnsAsync(expectedData);
    var sut = new SystemUnderTest(mockDependency.Object);

    // Act
    var result = await sut.ProcessAsync();

    // Assert
    result.Should().NotBeNull();
    result.Status.Should().Be("Success");
}
```

---

## Naming Convention

```
{MethodName}_{ExpectedBehavior}_{Condition}
```

Examples:
- `GetDocument_ReturnsStream_WhenDocumentExists`
- `BuildSystemPrompt_WithSkills_IncludesSkillPromptFragments`
- `InvokeAsync_UserWithAccess_ProceedsToEndpoint`
- `Execute_ThrowsException_WhenNameMissing`

---

## Test Class Structure

```csharp
public class AuthorizationServiceTests
{
    private readonly Mock<IAccessDataSource> _mockDataSource;
    private readonly AuthorizationService _sut;

    public AuthorizationServiceTests()
    {
        _mockDataSource = new Mock<IAccessDataSource>();
        _sut = new AuthorizationService(_mockDataSource.Object, new TestLogger<AuthorizationService>());
    }

    [Fact]
    public async Task AuthorizeAsync_ReturnsAuthorized_WhenUserHasAccess()
    {
        // Arrange
        _mockDataSource.Setup(d => d.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UacSnapshot { Teams = new[] { "team-1" } });

        // Act
        var result = await _sut.AuthorizeAsync("user-1", "resource-1", Operation.Read);

        // Assert
        result.IsAuthorized.Should().BeTrue();
    }

    [Theory]
    [InlineData("team-1", true)]
    [InlineData("team-unknown", false)]
    public async Task AuthorizeAsync_ChecksTeamMembership(string teamId, bool expected)
    {
        // Parameterized test
    }
}
```

---

## FluentAssertions Examples

```csharp
// Basic assertions
result.Should().NotBeNull();
result.Should().Be(expected);
result.Should().BeOfType<OkResult>();

// Collection assertions
items.Should().HaveCount(3);
items.Should().Contain(x => x.Name == "Test");
items.Should().BeEmpty();

// Exception assertions
action.Should().Throw<InvalidPluginExecutionException>()
    .WithMessage("*required*");

// HTTP assertions
response.StatusCode.Should().Be(HttpStatusCode.OK);
response.Headers.Should().ContainKey("OData-EntityId");
```

---

## Test Data Builders (Inline)

```csharp
private static AnalysisAction CreateAction(string systemPrompt = "Default prompt") => new()
{
    Id = Guid.NewGuid(),
    Name = "Test Action",
    SystemPrompt = systemPrompt,
    SortOrder = 1
};

private static AnalysisSkill CreateSkill(string fragment = "Test instruction") => new()
{
    Id = Guid.NewGuid(),
    Name = "Test Skill",
    PromptFragment = fragment,
    Category = "Default"
};
```

---

## Key Points

1. **AAA pattern** - Clear separation of setup, execution, verification
2. **Descriptive names** - Method_Behavior_Condition format
3. **One assertion focus** - Each test verifies one behavior
4. **Inline builders** - Keep test data creation close to tests
5. **Theory for parameterized** - Use InlineData for variations

---

## Related Patterns

- [Mocking Patterns](mocking-patterns.md) - Mock setup
- [Integration Tests](integration-tests.md) - E2E testing

---

**Lines**: ~100
