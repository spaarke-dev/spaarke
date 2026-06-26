# CLAUDE.md - Tests Module

> **Last Updated**: 2026-06-26 (rewritten end-to-end by `ci-cd-unit-test-remediation-r1` task CICD-021 per spec FR-B02)
>
> **Purpose**: Module-specific Claude Code directive for authoring, reviewing, and deleting `.NET` tests under `tests/**`.
>
> **Source ADR**: [ADR-038](../docs/adr/ADR-038-testing-strategy.md) — Testing Strategy (Integration-Heavy Pyramid, Path-Based KEEP Categories, Coverage as Observation)
>
> **Operational standard**: [`docs/standards/TEST-ARCHITECTURE.md`](../docs/standards/TEST-ARCHITECTURE.md)
>
> **Constraint loader**: [`.claude/constraints/testing.md`](../.claude/constraints/testing.md)

---

## Module Overview

Test projects for validating Spaarke .NET components, organized into **6 KEEP path categories** (canonical at runtime per ADR-038):

```
tests/
├── integration/
│   ├── auth/**              # security-auth category (OBO, claims, token validation)
│   ├── regression/**        # one file per past production bug — "every bug = regression test"
│   ├── data-mutation/**     # writes, transactions, rollback semantics
│   ├── tenant/**            # tenant boundary enforcement (cross-tenant reads MUST 404)
│   └── contract/**          # endpoint contract: route + status + ProblemDetails + payload shape
└── unit/
    └── domain/**            # pure domain logic — calculations, mappings, parsing, serialization
```

These six paths are **deletion-protected**. Removing a file under any of them requires a same-PR replacement covering the same scenario. Enforced at code-review (`task-execute` Step 9.5).

---

## Test Pyramid (Integration-Heavy)

Per ADR-038 §1: shape ~70% integration / ~30% unit. No UI tests yet (out of scope for this directive). This is **shape, not a hard target** — the inventory adjusts as the suite grows from regression-driven additions.

---

## Authoring Template — Integration-First (PREFERRED)

```csharp
[Fact]
public async Task CreateDocument_WithValidInput_ReturnsCreatedAndPersists()
{
    // Arrange — real WebApplicationFactory<Program>; test tenant; FakeTimeProvider
    using var factory = new TestWebApplicationFactory();
    var client = factory.CreateClient();
    var request = new CreateDocumentRequestBuilder().Build();

    // Act
    var response = await client.PostAsJsonAsync("/api/documents", request);

    // Assert — behavior (HTTP contract + persistence side effect)
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<Document>();
    created!.Id.Should().NotBeNullOrEmpty();

    var persisted = await factory.GetRepository().GetByIdAsync(created.Id);
    persisted.Should().NotBeNull();
    persisted!.Name.Should().Be(request.Name);
}
```

This template lives under `tests/integration/{auth,regression,data-mutation,tenant,contract}/**`.

---

## Authoring Template — Unit (DOMAIN LOGIC ONLY)

Use this template ONLY for pure domain logic placed under `tests/unit/domain/**`. If you're tempted to mock the class-under-test's collaborators, integration-first is the right shape instead.

```csharp
[Fact]
public void Calculate_WithValidInputs_ReturnsExpectedSum()
{
    // Arrange — no mocks, no DI, no I/O
    var sut = new ScoreCalculator();

    // Act
    var result = sut.Calculate(new[] { 10, 20, 30 });

    // Assert
    result.Should().Be(60);
}
```

---

## Mandatory Authoring Rules

| Rule | Path |
|---|---|
| **Every bug fix → one new regression test** | `tests/integration/regression/Issue{N}_*Tests.cs` |
| **Every new endpoint → ≥1 integration test** | `tests/integration/contract/**` |
| **Every new auth path → ≥1 integration test** | `tests/integration/auth/**` |
| **Every new write path → ≥1 integration test verifying rollback semantics** | `tests/integration/data-mutation/**` |
| **Every new tenant-touching feature → ≥1 isolation test** | `tests/integration/tenant/**` |
| **Pure domain logic → unit test** | `tests/unit/domain/**` |

Tests authored elsewhere are anti-pattern by construction. If a planned test doesn't fit ANY of the 6 paths, the test is the wrong shape — re-scope or escalate to an ADR amendment.

---

## Banned Antipatterns (5)

These wiring-test patterns generated the ~7,900-test suite that gave 0 signal on the 2026-06-25 Daily Briefing 9-bug cascade. **DO NOT write new tests of these shapes. Existing instances WILL be removed in Phase 2 task 053.**

1. ❌ `Mock<HttpMessageHandler>` — transport-level mock; encodes wire format; breaks on refactors. Use a real test double via `WebApplicationFactory` boundary instead.
2. ❌ `Mock<IServiceClient>` (or other typed HttpClient wrappers) when they hide the same antipattern.
3. ❌ **DI-registration tests** — `Assert.NotNull(services.GetRequiredService<X>())`. DI wiring is verified by the app starting; tests assert behavior.
4. ❌ **Constructor null-argument tests** — `Assert.Throws<ArgumentNullException>(() => new X(null))`. Use `ArgumentNullException.ThrowIfNull(x)` in production code; do not test it.
5. ❌ **Mocking the class-under-test's own collaborators** when an in-memory test double + real integration boundary is cheaper and more honest.

---

## Frameworks

| Framework | Version | Notes |
|---|---|---|
| xUnit | 2.9.x | Test framework |
| Moq | 4.20.x | Mocking library (NOT NSubstitute — codebase standard) |
| FluentAssertions | 6.12.x | Assertion library |
| FakeTimeProvider | (Microsoft.Extensions.TimeProvider.Testing) | For time-dependent tests — see TimeProvider rule below |

---

## Time-Dependent Tests: TimeProvider over Stopwatch

❌ **Banned**: `Stopwatch`, `DateTime.UtcNow` (in tests), `Task.Delay` (in tests) — sources of flakiness on shared CI runners.

✅ **Required pattern**:

```csharp
[Fact]
public void IsExpired_AfterTtlElapsed_ReturnsTrue()
{
    // Arrange
    var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-26T12:00:00Z"));
    var sut = new TokenValidator(time);
    var token = sut.IssueToken(ttl: TimeSpan.FromMinutes(5));

    // Act
    time.Advance(TimeSpan.FromMinutes(6));

    // Assert
    sut.IsExpired(token).Should().BeTrue();
}
```

---

## Test Naming Convention

```
{MethodOrEndpoint}_{Scenario}_{ExpectedResult}
```

Examples:
- `GetDocument_WhenNotFound_ReturnsNotFound`
- `UploadFile_WhenTokenExpired_ReturnsUnauthorized`
- `CreateDocument_WithValidInput_ReturnsCreatedAndPersists` (integration-first naming + observable behavior)

---

## Test Class Names

```
{ClassUnderTest}Tests.cs   // unit tests, domain logic only
{Endpoint}ContractTests.cs // contract tests under tests/integration/contract/**
Issue{N}_{Description}Tests.cs // regression tests under tests/integration/regression/**
```

Examples:
- `tests/unit/domain/Calculators/ScoreCalculatorTests.cs`
- `tests/integration/contract/Api/Ai/ChatEndpointsContractTests.cs`
- `tests/integration/regression/Issue417_DailyBriefingCascadeTests.cs`

---

## Test Data Builders (PREFERRED)

```csharp
public class DocumentBuilder
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "test-document.pdf";
    private int _size = 1024;

    public DocumentBuilder WithId(string id) { _id = id; return this; }
    public DocumentBuilder WithName(string name) { _name = name; return this; }
    public DocumentBuilder WithSize(int size) { _size = size; return this; }

    public Document Build() => new Document { Id = _id, Name = _name, Size = _size };
}

// Usage
var document = new DocumentBuilder().WithName("contract.pdf").WithSize(5000).Build();
```

Builders live next to the test class that uses them (or under `tests/integration/Shared/Builders/` for cross-class reuse).

---

## Coverage Policy

Coverage is measured but **NEVER gates**. Nightly via augmented `nightly-health.yml` Tier 3 (task CICD-043). Surfaced in rolling health-report issue. **Binding ≥6 months from 2026-06-26 per ADR-038**: do NOT reintroduce coverage-% targets in any directive file.

```bash
# Local coverage report (informational only — never a gate)
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

---

## Integration Test Configuration

```json
// appsettings.Test.json (test project — separate from production settings)
{
    "TestSettings": {
        "UseRealTestTenant": true,
        "TestTenantId": "<from-keyvault-or-local-secrets>"
    }
}
```

Real test tenants > in-memory emulators where the production code is exercising Dataverse-specific behavior (transactions, FetchXML semantics, policy enforcement). Spec §271 (decisions deferred to spec) flagged the in-memory-vs-real-tenant decision as open; default is real test tenant.

---

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Author tests under one of the 6 KEEP paths | Author tests under any other path |
| Write a regression test for every fixed bug | Skip "we'll add the test later" |
| Write an integration test for every new endpoint | Rely on unit test of the handler only |
| Use FluentAssertions for assertions | Use basic `Assert.Equal` / `Assert.NotNull` |
| Use Moq for module-boundary mocks | Use Moq to mock HttpClient/HttpMessageHandler |
| Use FakeTimeProvider for time-dependent tests | Use Stopwatch + Task.Delay |
| Clean up test data after each integration test | Leave test artifacts behind |
| Use test data builders for complex objects | Hand-construct repetitive object graphs in each test |

---

## Running Tests

```bash
# Run all tests
dotnet test

# Run by KEEP category (after task 050 path reorganization completes)
dotnet test tests/integration/contract/
dotnet test tests/integration/auth/
dotnet test tests/integration/regression/
dotnet test tests/unit/domain/

# Run with filter
dotnet test --filter "FullyQualifiedName~ChatEndpointsContractTests"

# Run with coverage (informational only — never a gate)
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings
```

---

## Cross-References

- **[ADR-038](../docs/adr/ADR-038-testing-strategy.md)** — Testing strategy ADR (standalone — does NOT supersede ADR-022, which is PCF Platform Libraries)
- **[`docs/standards/TEST-ARCHITECTURE.md`](../docs/standards/TEST-ARCHITECTURE.md)** — Operational standard (test pyramid, KEEP category examples, forcing-function enforcement)
- **[`.claude/constraints/testing.md`](../.claude/constraints/testing.md)** — MUST/MUST NOT rules loaded by Claude on test-touching tasks
- **[`docs/procedures/testing-and-code-quality.md`](../docs/procedures/testing-and-code-quality.md)** — Process guidance
- Root `CLAUDE.md` §8 — Test PRs are FULL rigor (override of default STANDARD per spec FR-B07, binding ≥6 months from 2026-06-26)

---

*Refer to root `CLAUDE.md` for repository-wide standards. This file is module-scoped to `tests/**`.*
