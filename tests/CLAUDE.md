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

## Banned Antipatterns (17 — extended 2026-06-26 per spec FR-B08)

The original 5 (B1-B5) attack wiring antipatterns; the 12 new bans (B6-B17) attack the deeper scaffolding-class debt — tests written during development to drive design or lift coverage % rather than to protect regressions. Industry framing: Beck "delete the scaffolding", Feathers characterization-vs-behavior, Google test-sizes, DHH less-tests. **DO NOT write new tests of these shapes.** Existing instances were partially removed in Phase 2 task 053 (9 files, 179 tests); the full sweep happens in Phase 2.5 tasks CICD-083..085 targeting BFF unit test count ≤3,500.

### B1-B5 — Wiring antipatterns

1. ❌ `Mock<HttpMessageHandler>` — transport-level mock; encodes wire format; breaks on refactors. Use a real test double via `WebApplicationFactory` boundary instead.
2. ❌ `Mock<IServiceClient>` (or other typed HttpClient wrappers) when they hide the same antipattern.
3. ❌ **DI-registration tests** — `Assert.NotNull(services.GetRequiredService<X>())`. DI wiring is verified by the app starting; tests assert behavior.
4. ❌ **Constructor null-argument tests** — `Assert.Throws<ArgumentNullException>(() => new X(null))`. Use `ArgumentNullException.ThrowIfNull(x)` in production code; do not test it.
5. ❌ **Mocking the class-under-test's own collaborators** when an in-memory test double + real integration boundary is cheaper and more honest.

### B6-B17 — Scaffolding-class debt

#### B6. Mirror tests — test method 1:1 with production method

```csharp
// ❌ BAD
[Fact]
public void GetName_ReturnsName()
{
    var sut = new UserDto { Name = "Alice" };
    sut.GetName().Should().Be("Alice");  // tests `=> Name;`
}
// ✅ GOOD — test the behavior the field participates in
[Fact]
public async Task GetUserByEmail_WhenFound_ReturnsCanonicalCase()
{
    var user = await client.GetFromJsonAsync<UserResponse>("/api/users?email=alice@x.com");
    user.Name.Should().Be("Alice Smith");  // tests case-canonicalization
}
```

#### B7. All-mocks + trivial assertion — every collaborator mocked, ≤2 assertions, often `Verify.Once()`

```csharp
// ❌ BAD
var a = new Mock<IA>(); var b = new Mock<IB>(); var c = new Mock<IC>();
var sut = new Processor(a.Object, b.Object, c.Object);
await sut.ProcessAsync();
a.Verify(x => x.DoAsync(), Times.Once);  // tests interaction shape
b.Verify(x => x.DoAsync(), Times.Once);
// ✅ GOOD — integration test against real collaborators (or delete entirely)
var response = await client.PostAsJsonAsync("/api/orders", validOrder);
response.StatusCode.Should().Be(HttpStatusCode.Created);
(await db.Orders.FindAsync(orderId)).Status.Should().Be(OrderStatus.Confirmed);
```

#### B8. Internal/private method tests via `[InternalsVisibleTo]` or reflection

```csharp
// ❌ BAD
var method = typeof(FileUploader).GetMethod("NormalizeFilename",
    BindingFlags.NonPublic | BindingFlags.Instance);
method.Invoke(new FileUploader(), new object[] { "fileé.pdf" }).Should().Be("file_.pdf");
// ✅ GOOD — test through the public surface
var response = await client.PostAsync("/api/files", multipartWithUnicodeName);
(await db.Files.OrderByDescending(f => f.Id).FirstAsync()).NormalizedName.Should().NotContain("é");
```

#### B9. Pass-through wrapper tests — methods that delegate `=> _service.DoIt(x)`

```csharp
// ❌ BAD
var repo = new Mock<IUserRepository>();
repo.Setup(r => r.GetById("1")).Returns(new User());
new UserFacade(repo.Object).GetUser("1");
repo.Verify(r => r.GetById("1"), Times.Once);  // tests one line
// ✅ GOOD — delete; OR test the aggregation if wrapper adds value
var user = await sut.GetUserAsync("1", tenantId: "t1");
user.TenantId.Should().Be("t1");  // aggregation, not delegation
```

#### B10. Coverage-fillers — assertions like `NotThrow()` / `NotNull()` added to push coverage %

```csharp
// ❌ BAD
[Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
public void Add_AnyInteger_DoesNotThrow(int x) =>
    (() => new Calculator().Add(x, x)).Should().NotThrow();
// ✅ GOOD — assert the result
[Theory] [InlineData(1, 1, 2)] [InlineData(2, 3, 5)]
public void Add_GivenInputs_ReturnsSum(int a, int b, int expected) =>
    new Calculator().Add(a, b).Should().Be(expected);
```

#### B11. Language-feature redundancy — tests of `required`, record equality, exhaustive switch

```csharp
// ❌ BAD — tests the compiler
(() => new Document()).Should().Throw<InvalidOperationException>();  // 'required' enforced at init
new Point(1, 2).Should().Be(new Point(1, 2));  // records get Equals/GetHashCode free
// ✅ GOOD — test the dependent semantic
sut.Add(new LineItem("A", 10));
sut.Add(new LineItem("A", 10));
sut.LineItems.Should().HaveCount(1);  // dedup, not equality
```

#### B12. Snapshot tests of trivial output — JSON round-trip, default `ToString()`, default `Equals`

```csharp
// ❌ BAD — tests System.Text.Json
JsonSerializer.Serialize(new Person { Name = "Alice", Age = 30 })
    .Should().Be("""{"Name":"Alice","Age":30}""");
// ✅ GOOD — test the contract through the framework
using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
doc.RootElement.GetProperty("name").GetString().Should().Be("Alice");
```

#### B13. Test names without scenario+expected — violate `{Method}_{Scenario}_{ExpectedResult}` convention

```csharp
// ❌ BAD
[Fact] public void Test1() { ... }
[Fact] public void Foo_Works() { ... }
[Fact] public void DoIt_Bug417() { ... }  // bug numbers belong in regression file names
// ✅ GOOD
[Fact] public async Task GetDocument_WhenNotFound_ReturnsNotFound() { ... }
[Fact] public async Task UploadFile_WhenSizeExceedsLimit_Returns413PayloadTooLarge() { ... }
```

#### B14. Exhaustive-switch / sealed-hierarchy coverage tests — C# 12 compiler enforces exhaustiveness

```csharp
// ❌ BAD
[Theory] [InlineData(OrderStatus.Pending)] [InlineData(OrderStatus.Shipped)] [InlineData(OrderStatus.Delivered)]
public void Process_AnyStatus_DoesNotThrowSwitchException(OrderStatus s) =>
    (() => sut.Process(s)).Should().NotThrow();
// ✅ GOOD — per-branch behavior
[Theory] [InlineData(OrderStatus.Pending, "queued")] [InlineData(OrderStatus.Shipped, "in-transit")]
public void StatusToShippingLabel_GivenStatus_ReturnsExpectedLabel(OrderStatus s, string expected) =>
    sut.ToShippingLabel(s).Should().Be(expected);
```

#### B15. Setup-to-assertion ratio > 10:1 — 50+ lines of mock setup with 1-2 trivial assertions

```csharp
// ❌ BAD — 60 lines of mock setup, 1 weak assertion
var mock1 = new Mock<IA>(); mock1.Setup(...).Returns(...);
var mock2 = new Mock<IB>(); mock2.Setup(...).Returns(...);
// ... 50 more lines ...
(await sut.ProcessAsync(order)).Should().NotBeNull();
// ✅ GOOD — integration test, setup amortized in WebApplicationFactory
var response = await client.PostAsJsonAsync("/api/orders", validOrder);
response.StatusCode.Should().Be(HttpStatusCode.Accepted);
(await db.Orders.FindAsync(orderId)).Status.Should().Be(OrderStatus.Confirmed);
```

#### B16. Pure getter/setter/auto-property tests — C# guarantees the round-trip

```csharp
// ❌ BAD
var sut = new Document();
sut.Name = "test.pdf";
sut.Name.Should().Be("test.pdf");  // tests `{ get; set; }`
// ✅ GOOD — delete; OR test the validation if any
var sut = new User();
(() => sut.Email = "not-an-email").Should().Throw<ArgumentException>();
```

#### B17. Generated-code field-by-field tests — record equality, AutoMapper profiles, EF projections

```csharp
// ❌ BAD — tests the AutoMapper profile generator
var dto = mapper.Map<UserDto>(user);
dto.Id.Should().Be(user.Id);
dto.Name.Should().Be(user.Name);
dto.Email.Should().Be(user.Email);
// ✅ GOOD — single config-validity assertion + behavior tests on output shape
[Fact] public void MapperConfiguration_IsValid() =>
    mapper.ConfigurationProvider.AssertConfigurationIsValid();
[Fact] public async Task GetUserDto_InPublicContext_StripsPii()
{
    var dto = await client.GetFromJsonAsync<UserDto>("/api/users/1?context=public");
    dto.Email.Should().BeNull();  // public context strips PII — actual behavior
}
```

---

## Expect to Defend at Project Close

**Every test you write today, expect to defend at project close.** If it can't be defended as integrate/maintain class — that is, a regression-protector, a contract-anchor, or a business-logic-with-branches test under one of the 6 KEEP paths — it gets deleted in the `/test-diet` pass at the project's `090-wrapup-*` task (added by spec FR-B09 / task CICD-081).

The build-vs-maintain distinction:

| Class | Half-life | Purpose | KEEP path? |
|---|---|---|---|
| **Build-class** | days/weeks (the project) | Drive design, validate construction, satisfy coverage % | NO — deleted in diet pass |
| **Maintain-class** | months/years | Protect against regression, anchor a contract, exercise branched business logic | YES — lives in one of the 6 KEEP paths |

When authoring a test, ask three questions:

1. **What production behavior would break if this test were deleted?** If you can't name a concrete behavior, it's build-class — the test exists for design or coverage, not protection.
2. **Does this test live under one of the 6 KEEP paths?** If not, the test is the wrong shape — re-scope to a KEEP path or escalate to an ADR amendment.
3. **Is the assertion in this test about behavior the caller would notice, or about implementation the caller can't see?** If the latter, it's scaffolding by ADR-038 §7 definition.

A test that survives all three questions is maintain-class — author it, name it per `{Method}_{Scenario}_{ExpectedResult}`, place it at the right KEEP path, and trust it to defend itself at project close.

A test that fails any question is build-class. **Either fix the test now (rescope, rename, restructure) or accept that the `/test-diet` pass will delete it.**

This is the cultural reset codified by ADR-038 §7 (binding ≥6 months from 2026-06-26) and enforced by `/test-diet` (added by task CICD-081) at every project's wrap-up.

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
