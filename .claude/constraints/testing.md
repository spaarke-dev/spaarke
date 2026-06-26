# Testing Constraints

> **Domain**: .NET unit + integration testing (server-side; `tests/unit/**` and `tests/integration/**`)
> **Source ADR**: [ADR-038](../adr/INDEX.md) — Testing Strategy (Integration-Heavy Pyramid, Path-Based KEEP Categories)
> **Operational Standard**: [`docs/standards/TEST-ARCHITECTURE.md`](../../docs/standards/TEST-ARCHITECTURE.md)
> **See Also**: [Testing and Code Quality Procedure](../../docs/procedures/testing-and-code-quality.md), [`tests/CLAUDE.md`](../../tests/CLAUDE.md)
> **Last Updated**: 2026-06-26
> **Last Reviewed**: 2026-06-26
> **Reviewed By**: ci-cd-unit-test-remediation-r1 task CICD-022 (Stream B directive rewrite per spec FR-B01)
> **Status**: Current (full rewrite — superseded the coverage-% culture directives)

---

## When to Load This File

Load when:
- Writing unit tests for new code (`tests/unit/domain/**`)
- Writing integration tests for any endpoint, auth path, mutation, tenant boundary, or regression scenario
- Modifying or deleting any existing test file
- Reviewing a PR that touches `tests/**`
- Designing a new test fixture or test double

---

## MUST Rules

### 1. Six KEEP path categories (deletion-protected)

Tests under these six paths are **KEEP-protected**. Deleting a file under any of these paths in a PR requires **a same-PR replacement** covering the same scenario. Enforced at code-review (`task-execute` Step 9.5) by path inspection — NOT by CSV lookup.

| Path | Category | What lives here |
|---|---|---|
| `tests/integration/auth/**` | security-auth | Authentication, authorization, OBO exchange, claims handling, token validation |
| `tests/integration/regression/**` | regression | One file per past production bug — "every bug = regression test" |
| `tests/integration/data-mutation/**` | data-mutation | Writes, transactions, rollback semantics |
| `tests/integration/tenant/**` | tenant-isolation | Tenant boundary enforcement (cross-tenant reads MUST 404, not 403) |
| `tests/integration/contract/**` | endpoint-contract | Route + status + ProblemDetails + payload shape. "Every new endpoint = ≥1 integration test." |
| `tests/unit/domain/**` | domain-logic | Pure domain logic: calculations, mappings, parsing, serialization, handler-internal orchestration |

### 2. Authoring rules

- ✅ **MUST** write a regression test for every fixed production bug — file lands under `tests/integration/regression/Issue{N}_*Tests.cs`
- ✅ **MUST** write at least one integration test under `tests/integration/contract/**` for every new endpoint
- ✅ **MUST** mirror `src/` directory structure within each KEEP path (e.g., `tests/integration/contract/Api/Ai/ChatEndpointsTests.cs` mirrors `src/server/api/.../Api/Ai/`)
- ✅ **MUST** use xUnit as the test framework
- ✅ **MUST** use Moq (NOT NSubstitute) for mocking — matches the existing codebase
- ✅ **MUST** use FluentAssertions for assertions
- ✅ **MUST** follow integration-first AAA pattern (see `tests/CLAUDE.md` for the template)
- ✅ **MUST** name tests `{Method}_{Scenario}_{ExpectedResult}` (e.g., `GetDocument_WhenNotFound_ReturnsNotFound`)
- ✅ **MUST** test one behavior per test method
- ✅ **MUST** use `TimeProvider` (or `FakeTimeProvider`) for any code that reads the current time, schedules, or delays. **Banned**: `Stopwatch`, `DateTime.UtcNow`, `Task.Delay` in tests.

### 3. Test isolation

- ✅ **MUST** isolate tests from external production services (use real test tenants for integration, in-memory for unit)
- ✅ **MUST** clean up test data after each integration test run
- ✅ **MUST** use separate test configuration (not production settings)
- ✅ **MUST** be runnable in any order (no inter-test state dependencies)

### 4. Coverage is observation, not gate

- 🚫 **MUST NOT** mandate any line-coverage percentage. Coverage is measured nightly via `nightly-health.yml` Tier 3 and surfaced in the rolling health report for awareness only. **Binding for ≥6 months from 2026-06-26 per ADR-038.**

---

## MUST NOT Rules

### Banned scaffolding-test antipatterns (17 — extended 2026-06-26 per spec FR-B08)

The first 5 (B1-B5) attack wiring antipatterns; B6-B17 attack the deeper scaffolding-class debt (tests written to drive design or lift coverage % rather than to protect regressions). Full ADR-038 §7 has concrete BAD + GOOD C# examples for each. Industry consensus (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests) anchors the framing.

**B1-B5 — Wiring antipatterns** (existing):

1. ❌ **MUST NOT** use `Mock<HttpMessageHandler>` — transport-level mock encodes wire format into the test; breaks on production refactors without catching real bugs. **Use a fake `HttpClient` via test-double + integration boundary instead.**
2. ❌ **MUST NOT** use `Mock<IServiceClient>` or other typed HttpClient wrappers as test doubles when they hide the HttpMessageHandler antipattern.
3. ❌ **MUST NOT** write DI-registration tests (`Assert.NotNull(services.GetRequiredService<X>())` or similar container-introspection assertions). DI wiring is verified by the app actually starting; tests should assert behavior.
4. ❌ **MUST NOT** write constructor null-argument tests (`Assert.Throws<ArgumentNullException>(() => new X(null))`). Add `ArgumentNullException.ThrowIfNull(x)` in production code if needed; do not test it.
5. ❌ **MUST NOT** mock the class-under-test's collaborators when an in-memory test double + a real integration boundary is cheaper and more honest.

**B6-B17 — Scaffolding-class debt** (new 2026-06-26; see [ADR-038 §7](../adr/INDEX.md) for full BAD/GOOD examples):

6. ❌ **MUST NOT** write **mirror tests** — test methods that assert the implementation does what it does (`GetName_ReturnsName` → `=> Name;`). Test the behavior the field participates in.
7. ❌ **MUST NOT** write **tests-with-all-mocks-and-trivial-assertion** — every collaborator mocked, ≤2 assertions, often just `Verify.Once()`. Tests interaction shape, not behavior.
8. ❌ **MUST NOT** test **internal/private methods** via `[InternalsVisibleTo]` or reflection. Locks implementation; test through the public surface instead.
9. ❌ **MUST NOT** write **pass-through wrapper tests** — methods that delegate `=> _service.DoIt(x)` need no test; if the wrapper grows logic later, test the logic then.
10. ❌ **MUST NOT** write **coverage-fillers** — tests with `NotThrow()` / `NotNull()` assertions added solely to push coverage %. Coverage is observation (§3), never gate; assert the result.
11. ❌ **MUST NOT** write **language-feature redundancy tests** — tests of `required` keyword, record equality, sealed hierarchies, exhaustive switch. The C# compiler/runtime enforces these.
12. ❌ **MUST NOT** write **snapshot tests of trivial output** — JSON round-trips, default `ToString()`, default `Equals`. Tests the framework, not your code.
13. ❌ **MUST NOT** name test methods without scenario+expected — `Test1`, `Foo_Works`, `DoIt_Bug417` violate the `{Method}_{Scenario}_{ExpectedResult}` convention and typically lack a clear behavior to defend.
14. ❌ **MUST NOT** write **exhaustive-switch/sealed-hierarchy coverage tests** — C# 12 compile-time exhaustive switch makes "all cases handled" a compiler concern, not a runtime test concern.
15. ❌ **MUST NOT** write tests with **setup-to-assertion ratio > 10:1** — 50+ lines of mock setup with 1-2 trivial assertions buries behavior signal in plumbing. Refactor to integration test.
16. ❌ **MUST NOT** test **pure getters/setters/auto-properties** — the C# language guarantees the round-trip; test the validation/computation if it exists.
17. ❌ **MUST NOT** test **generated code field-by-field** — record equality, AutoMapper profiles, EF projections. Use `AssertConfigurationIsValid()` once, then test the behavior of the output type.

### General anti-patterns

- ❌ **MUST NOT** test implementation details (private methods directly)
- ❌ **MUST NOT** use production databases or services
- ❌ **MUST NOT** ignore or skip tests without a `[Trait("skip-reason", "<concrete reason + ticket>")]` marker and a follow-up issue
- ❌ **MUST NOT** use `Thread.Sleep` or arbitrary delays (use `TimeProvider`/`FakeTimeProvider`)
- ❌ **MUST NOT** mock value objects or DTOs
- ❌ **MUST NOT** introduce a new test under any path OTHER than the 6 KEEP categories — if you have a test that doesn't fit, the test is the wrong shape OR a new KEEP category needs an ADR amendment first.

---

## Mock-boundary rules

✅ **Acceptable mocking** (module boundary — between cohesive units):

```csharp
// Unit test of DocumentService — mock the repository (true module boundary)
var repo = new Mock<IDocumentRepository>();
repo.Setup(r => r.GetByIdAsync("123")).ReturnsAsync(new Document { Id = "123" });
var sut = new DocumentService(repo.Object);
```

❌ **Banned mocking** (transport-level or wire-format):

```csharp
// Wiring test — couples to HttpClient internals; breaks on refactors
var handler = new Mock<HttpMessageHandler>();
handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ...);  // BANNED
```

---

## Quick Reference Patterns

### Test naming

```csharp
// ✅ Good: Clear scenario and expected result
[Fact]
public async Task GetDocument_WhenNotFound_ReturnsNotFound()

// ❌ Bad: Unclear what's being tested
[Fact]
public async Task Test1()
```

### Integration-first AAA (preferred)

```csharp
[Fact]
public async Task CreateDocument_WithValidInput_ReturnsCreatedAndPersists()
{
    // Arrange — real WebApplicationFactory<Program>; real test tenant; in-memory FakeTimeProvider
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
}
```

### TimeProvider usage

```csharp
// ✅ Acceptable
var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-26T12:00:00Z"));
var sut = new ExpirationCalculator(time);
time.Advance(TimeSpan.FromMinutes(5));
sut.IsExpired(token).Should().BeTrue();

// ❌ Banned
var stopwatch = Stopwatch.StartNew();
await Task.Delay(5000);  // flake source
stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
```

---

## Pattern Files (Complete Examples)

- [Unit Test Structure](../patterns/testing/unit-test-structure.md)
- [Mocking Patterns](../patterns/testing/mocking-patterns.md)
- [Integration Tests](../patterns/testing/integration-tests.md)

---

## Authoritative References

- **[ADR-038](../adr/INDEX.md)** — Testing Strategy ADR (standalone — does NOT supersede ADR-022; ADR-022 is the unrelated PCF Platform Libraries ADR)
- **[`docs/standards/TEST-ARCHITECTURE.md`](../../docs/standards/TEST-ARCHITECTURE.md)** — Operational standard (test pyramid, KEEP categories with examples, forcing-function enforcement)
- **[`tests/CLAUDE.md`](../../tests/CLAUDE.md)** — Module-specific Claude session directive (integration-first template, repo conventions)
- **[Testing and Code Quality Procedure](../../docs/procedures/testing-and-code-quality.md)** — Development-process guidance

---

**Lines**: ~165
