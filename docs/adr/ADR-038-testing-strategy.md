# ADR-038: Testing Strategy — Integration-Heavy Pyramid, Path-Based KEEP Categories, Coverage as Observation

## Status

Accepted (2026-06-26) — `ci-cd-unit-test-remediation-r1` Phase 1 Stream B

**This is a STANDALONE ADR.** It does NOT supersede ADR-022 (which is "PCF Platform Libraries — Field-Bound Controls Only", a frontend ADR with no testing scope). Earlier project drafts referenced ADR-022 as a testing source; that was a misattribution corrected in `.claude/constraints/testing.md` line 25 by the same project (task CICD-022).

## Domain

.NET test architecture (server-side); applies to `tests/unit/**` and `tests/integration/**`. Does NOT apply to React/PCF Jest tests (separate framework, separate ADR if needed in the future).

## Context

### The coverage-driven culture this ADR replaces

For a multi-month window prior to 2026-06-26 the test suite optimized for one metric: **line coverage percentage**. Three load-bearing directive files mandated coverage targets that future Claude Code sessions reading the directives would generate tests to hit:

1. `tests/CLAUDE.md` (lines 153–155): "80% Core / 70% Endpoints / 90% Utilities" coverage targets, with `<1s/test` performance rule.
2. `.claude/constraints/testing.md` (line 51): "**MUST** maintain minimum 80% line coverage for new code"; line 38 "**MUST** mock all I/O"; line 40 "**MUST** keep unit tests fast (<100ms per test)".
3. The constraint file's own footer at lines 131–133 acknowledged the misattribution to ADR-022 — but the line-25 reference was still authoritative for Claude sessions reading top-down.

The emergent consequence: ~7,900 tests across `tests/unit/Sprk.Bff.Api.Tests/`, `tests/unit/Spaarke.Plugins.Tests/`, `tests/unit/Spaarke.Scheduling.Tests/`. Each generated to hit the targets. Each session adds more wiring tests (DI registration, `Mock<HttpMessageHandler>` setups, constructor null-argument tests) because those are the cheap ways to lift coverage % without testing behavior.

### Evidence section: why the policy reverses

**Symptom S-5** — *Tests test wiring, not behavior.* DI registration tests, constructor null-checks, and `Mock<HttpMessageHandler>` setups break **en masse** on production refactors. The R3 dependency-cascade week (Q1 2026) saw 5 full days of development halted while ~400 mock-laden tests had to be retrofitted to a refactored Graph client. The tests blocked the refactor without catching a single behavioral bug.

**Symptom S-6** — *Real bugs not caught.* The 2026-06-25 Daily Briefing cascade surfaced **9 production bugs**. After full unit test suite execution, **0** of those 9 bugs were caught by any unit test. Behaviorally, the 7,900 tests gave zero signal on the real problems. The integration test suite (`tests/integration/Spe.Integration.Tests/`, 462 tests, classified by task CICD-010 as the "stable signal — 0 failures across every sampled run") would have caught at least 4 of the 9 if the corresponding scenarios had been authored.

The data is unambiguous: **coverage % as a gate is a vanity metric that costs more than it returns.** The architecture forcing wiring-test generation is the directive layer, not the code.

### What the spec demands

Spec `ci-cd-unit-test-remediation-r1` FR-B01..FR-B07 + design.md §§4–5 + SC-06 + SC-10 require:

- Drop coverage-% MUST rules from both directive files.
- Replace mock-first authoring template with integration-first.
- Encode 6 KEEP path categories as MUST rules (deletion requires same-PR replacement).
- Ban specific antipatterns (`Mock<HttpMessageHandler>`, `Mock<IServiceClient>`, DI-registration tests, constructor null-checks).
- Coverage measurement remains observable (tracked nightly via augmented `nightly-health.yml`), never gating.
- Cultural reset binding for ≥6 months from 2026-06-26.

## Decision

### 1. Integration-heavy test pyramid

The portfolio is heavy at the integration boundary, modest at the unit boundary, no UI tests yet. Approximate ratio of the surviving suite: ~70% integration / ~30% unit. This is **shape**, not a hard target.

### 2. Six KEEP path categories as MUST rules

Tests under these paths are KEEP-protected. Deletion requires a same-PR replacement covering the same scenario. Enforced at code-review (Step 9.5 of `task-execute`) by path check, NOT by CSV consultation at runtime.

| Path | Category | Definition |
|---|---|---|
| `tests/integration/auth/**` | security-auth | Authentication / authorization path, token validation, claims handling, OBO exchange |
| `tests/integration/regression/**` | regression | One test per past production bug — "every bug = regression test" |
| `tests/integration/data-mutation/**` | data-mutation | Writes, transactions, rollback semantics |
| `tests/integration/tenant/**` | tenant-isolation | Tenant boundary enforcement (cross-tenant reads must 404, not 403) |
| `tests/integration/contract/**` | endpoint-contract | Endpoint HTTP contract: route + status + ProblemDetails + payload shape. "Every new endpoint = ≥1 integration test." |
| `tests/unit/domain/**` | domain-logic | Pure domain logic — calculation / mapping / parsing / serialization / handler-internal orchestration |

### 3. Coverage as observation, never gate

`nightly-health.yml` Tier 3 (augmented by task CICD-043) collects coverage from full unit + integration runs and publishes to the rolling health-report issue. It informs trend awareness but NEVER blocks a PR or master push. **Any future project introducing coverage-% gating MUST be rejected at design-doc review.** Binding for ≥6 months from 2026-06-26.

### 4. Mock at module boundaries, not at HTTP-handler level

- ✅ Acceptable: `Mock<IDocumentRepository>` for a unit test of `DocumentService` (module boundary)
- ❌ Banned: `Mock<HttpMessageHandler>` (transport-level mock — encodes wire format into the test)
- ❌ Banned: `Mock<IServiceClient>` (typed wrappers around HttpClient that hide the same antipattern)
- ❌ Banned: DI-registration tests (`Assert.NotNull(services.GetRequiredService<X>())`)
- ❌ Banned: Constructor null-check tests (`Assert.Throws<ArgumentNullException>(() => new X(null))`)
- ❌ Banned: Mocking the class-under-test's collaborators when an in-memory test double + integration boundary is cheaper and more honest

### 5. TimeProvider over Stopwatch for time-dependent tests

Use `TimeProvider` (or `FakeTimeProvider` in tests) for any code that needs the current time, delays, or scheduling. `Stopwatch` + `Task.Delay` in tests is banned — it produces flakiness on shared CI runners and ties test correctness to runner clock noise.

### 6. Forcing-function enforcement points

The policy is enforced at three layers:

1. **`task-execute` Step 9.5** (modified by task CICD-060) — runs `code-review` + `adr-check` UNCONDITIONALLY on test-modifying PRs, regardless of default rigor level. The override is binding per spec FR-B07.
2. **`nightly-health.yml` Tier 3 coverage job** — observation only; surfaces drift in nightly issue.
3. **Path-check deletion safety** — any deletion under the 6 KEEP paths requires same-PR replacement (Step 9.5 enforces by path inspection, not CSV).

### 7. Build-vs-Maintain Criteria (Scaffolding-Test Bans — added 2026-06-26 per spec FR-B08)

The 5 bans in §4 (B1-B5) attack symptoms. The 12 bans below (B6-B17) attack the deeper structural debt: scaffolding-class tests written during development to drive design, validate construction, or lift coverage %, with no ongoing regression-protecting role. Industry consensus supports the distinction:

- **Kent Beck** — "delete the scaffolding once the building stands" (TDD by Example, ch. 12 retrospective)
- **Michael Feathers** — characterization-tests-vs-behavior-tests distinction (Working Effectively with Legacy Code, ch. 13)
- **Google test-sizes taxonomy** — small (unit) tests have intentional short-half-life when they test internals; medium/large tests carry long-term contract value
- **DHH / 37signals** — "less tests, written more carefully" (Rails Doctrine; HEY codebase ratio shifts post-launch)

The 17 total bans (B1-B17) are MUST NOT for new tests AND DELETE candidates for existing ones. Each ban: signature + concrete C# BAD example + acceptable GOOD alternative + one-line rationale.

---

#### B6. Mirror tests (test code 1:1 with production code)

A test method per production method, asserting the implementation does what it does.

```csharp
// BAD — mirror test
[Fact]
public void GetName_ReturnsName()
{
    var sut = new UserDto { Name = "Alice" };
    sut.GetName().Should().Be("Alice");  // tests `=> Name;`
}
```

**Why scaffolding**: The test fails only if the implementation diverges from itself. Production change to `Name` field flows automatically to test; no behavior is protected.

```csharp
// GOOD — test the behavior the field participates in (integration contract)
[Fact]
public async Task GetUserByEmail_WhenFound_ReturnsCanonicalCase()
{
    var response = await client.GetAsync("/api/users?email=alice@example.com");
    var user = await response.Content.ReadFromJsonAsync<UserResponse>();
    user.Name.Should().Be("Alice Smith");  // tests case-canonicalization behavior, not field plumbing
}
```

#### B7. Tests-with-all-mocks-and-trivial-assertion (assertion count ≤ 2, every collaborator mocked)

```csharp
// BAD
[Fact]
public async Task ProcessOrder_CallsAllCollaborators()
{
    var pricing = new Mock<IPricingService>();
    var inventory = new Mock<IInventoryService>();
    var notifier = new Mock<INotifier>();
    var sut = new OrderProcessor(pricing.Object, inventory.Object, notifier.Object);
    await sut.ProcessAsync(new Order());
    pricing.Verify(p => p.CalculateAsync(It.IsAny<Order>()), Times.Once);
    inventory.Verify(i => i.ReserveAsync(It.IsAny<Order>()), Times.Once);
}
```

**Why scaffolding**: Mocks specify what the implementation calls; the test reverses on every internal-flow refactor. Tests *interaction shape*, not behavior.

```csharp
// GOOD — integration test against real collaborators (or delete entirely)
[Fact]
public async Task PostOrder_WithValidPayment_PersistsConfirmedOrder()
{
    var response = await client.PostAsJsonAsync("/api/orders", validOrder);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    (await db.Orders.FindAsync(orderId)).Status.Should().Be(OrderStatus.Confirmed);
}
```

#### B8. Internal/private method tests (via `InternalsVisibleTo` or reflection)

```csharp
// BAD
[Fact]
public void NormalizeFilename_HandlesUnicode()
{
    var method = typeof(FileUploader).GetMethod("NormalizeFilename",
        BindingFlags.NonPublic | BindingFlags.Instance);
    var result = method.Invoke(new FileUploader(), new object[] { "fileé.pdf" });
    result.Should().Be("file_.pdf");
}
```

**Why scaffolding**: Locks the implementation; "private" no longer means "free to refactor." Behavior should be tested via the public surface.

```csharp
// GOOD — test through the public endpoint
[Fact]
public async Task Upload_WithUnicodeFilename_PersistsNormalizedName()
{
    using var content = new MultipartFormDataContent { /* file with unicode name */ };
    var response = await client.PostAsync("/api/files", content);
    var saved = await db.Files.OrderByDescending(f => f.Id).FirstAsync();
    saved.NormalizedName.Should().NotContain("é");
}
```

#### B9. Pass-through wrapper tests (testing trivial delegation)

Tests of methods that do nothing except delegate to a single collaborator: `=> _service.DoIt(x)`.

```csharp
// BAD
[Fact]
public void GetUser_DelegatesToRepository()
{
    var repo = new Mock<IUserRepository>();
    repo.Setup(r => r.GetById("1")).Returns(new User());
    var sut = new UserFacade(repo.Object);
    sut.GetUser("1");
    repo.Verify(r => r.GetById("1"), Times.Once);  // tests one line: `=> _repo.GetById(id);`
}
```

**Why scaffolding**: Verifies one line of code. If the wrapper grows logic later, write a test then — for the logic, not the delegation.

```csharp
// GOOD — delete the test; OR if wrapper aggregates value, test the aggregation
[Fact]
public async Task GetUserWithTenantScope_AppliesTenantFilter()
{
    var user = await sut.GetUserAsync(userId: "1", tenantId: "t1");
    user.TenantId.Should().Be("t1");  // aggregation behavior, not delegation
}
```

#### B10. Coverage-fillers (tests authored to push coverage % up)

Tests with no clear scenario, often `[Theory]` with trivially-different inputs and weak assertions like `NotThrow()` or `NotNull()`.

```csharp
// BAD
[Theory]
[InlineData(1)]
[InlineData(2)]
[InlineData(3)]
public void Add_AnyInteger_DoesNotThrow(int x)
{
    var act = () => new Calculator().Add(x, x);
    act.Should().NotThrow();  // covers `return a + b;` without asserting the result
}
```

**Why scaffolding**: ADR-038 §3 makes coverage observation only, never gate. Tests authored for coverage are by construction not authored for behavior.

```csharp
// GOOD — assert the result
[Theory]
[InlineData(1, 1, 2)]
[InlineData(2, 3, 5)]
public void Add_GivenInputs_ReturnsSum(int a, int b, int expected)
{
    new Calculator().Add(a, b).Should().Be(expected);
}
```

#### B11. Language-feature redundancy tests (testing what the compiler enforces)

```csharp
// BAD — tests `required` keyword
[Fact]
public void RequiredProperty_WhenMissing_FailsToConstruct()
{
    var act = () => new Document();  // 'required' is enforced at compile time / init
    act.Should().Throw<InvalidOperationException>();
}

// BAD — tests record-equality generated code
[Fact]
public void Point_TwoIdenticalInstances_AreEqual()
{
    new Point(1, 2).Should().Be(new Point(1, 2));  // records get Equals/GetHashCode free
}
```

**Why scaffolding**: The C# language/runtime already enforces this. Tests fail when the language semantics change — which never happens.

```csharp
// GOOD — delete; OR test the semantic that DEPENDS on the feature (e.g., dedup behavior)
[Fact]
public void OrderProcessor_DedupesIdenticalLineItems()
{
    sut.Add(new LineItem("A", qty: 10));
    sut.Add(new LineItem("A", qty: 10));
    sut.LineItems.Should().HaveCount(1);  // tests dedup, not record equality
}
```

#### B12. Snapshot tests of trivial output (JSON round-trip, `ToString()`, default equality)

```csharp
// BAD
[Fact]
public void Person_SerializesToExpectedJson()
{
    var p = new Person { Name = "Alice", Age = 30 };
    JsonSerializer.Serialize(p)
        .Should().Be("""{"Name":"Alice","Age":30}""");  // tests System.Text.Json
}
```

**Why scaffolding**: Tests the framework, not your code. Property reorder/rename triggers failure with no behavior impact.

```csharp
// GOOD — test the contract that flows through serialization
[Fact]
public async Task GetPerson_ResponseHasNameAndAgeFields()
{
    var response = await client.GetAsync("/api/people/1");
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    doc.RootElement.GetProperty("name").GetString().Should().Be("Alice");
    doc.RootElement.GetProperty("age").GetInt32().Should().Be(30);
}
```

#### B13. Tests whose name doesn't describe behavior

Test methods named `Test1`, `Foo_Works`, `Method_TestCase_Bug417`. Per the naming convention `{Method}_{Scenario}_{ExpectedResult}`, tests without scenario+expected in the name typically lack a clear behavior to defend.

```csharp
// BAD
[Fact] public void Test1() { /* what scenario? what's expected? */ }
[Fact] public void Foo_Works() { /* "works" is not a behavior */ }
[Fact] public void DoIt_Bug417() { /* bug numbers belong in regression file names */ }
```

**Why scaffolding**: Reader cannot tell what's being protected. Test was added for coverage or pasted from a template; it doesn't survive a "what would break if this test were deleted?" question.

```csharp
// GOOD
[Fact] public async Task GetDocument_WhenNotFound_ReturnsNotFound() { ... }
[Fact] public async Task UploadFile_WhenSizeExceedsLimit_Returns413PayloadTooLarge() { ... }
```

#### B14. Tests of types the type system enforces (exhaustive switch, sealed-hierarchy coverage)

C# 12 exhaustive switch generates compile-time warnings/errors when a case is missed; tests asserting "all cases handled" add CI cost with zero signal.

```csharp
// BAD
[Theory]
[InlineData(OrderStatus.Pending)]
[InlineData(OrderStatus.Shipped)]
[InlineData(OrderStatus.Delivered)]
public void Process_AnyStatus_DoesNotThrowSwitchException(OrderStatus s)
{
    var act = () => sut.Process(s);
    act.Should().NotThrow();  // compiler error if a case is missed
}
```

**Why scaffolding**: Compiler already prevents the failure mode.

```csharp
// GOOD — test the behavior of each branch
[Theory]
[InlineData(OrderStatus.Pending, "queued")]
[InlineData(OrderStatus.Shipped, "in-transit")]
[InlineData(OrderStatus.Delivered, "received")]
public void StatusToShippingLabel_GivenStatus_ReturnsExpectedLabel(OrderStatus s, string expected)
{
    sut.ToShippingLabel(s).Should().Be(expected);
}
```

#### B15. Tests where assertion count is dwarfed by setup (setup-to-assertion ratio > 10:1)

50+ lines of arrange/mock/setup with 1-2 trivial assertions. The setup contains the test's reasoning; behavior signal is buried.

```csharp
// BAD — 60 lines of setup, 1 weak assertion
[Fact]
public async Task ProcessOrder_Succeeds()
{
    var mock1 = new Mock<IServiceA>(); mock1.Setup(...).Returns(...);
    var mock2 = new Mock<IServiceB>(); mock2.Setup(...).Returns(...);
    // ... 50 more lines of mock configuration ...

    var result = await sut.ProcessAsync(order);

    result.Should().NotBeNull();  // 1 assertion of trivial property
}
```

**Why scaffolding**: A reader can't determine what's being tested without reading every mock setup. Setup is the test logic; the test as a whole expresses configuration of a fake world, not behavior of the real one.

```csharp
// GOOD — refactor to integration test where setup is amortized in WebApplicationFactory
[Fact]
public async Task PostOrder_WithValidPayment_ReturnsConfirmation()
{
    var response = await client.PostAsJsonAsync("/api/orders", validOrder);
    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var order = await db.Orders.FindAsync(orderId);
    order.Status.Should().Be(OrderStatus.Confirmed);
}
```

#### B16. Tests of pure getters/setters/auto-properties (no logic)

```csharp
// BAD
[Fact]
public void Name_AfterSet_ReturnsSetValue()
{
    var sut = new Document();
    sut.Name = "test.pdf";
    sut.Name.Should().Be("test.pdf");  // tests `{ get; set; }`
}
```

**Why scaffolding**: Auto-properties have no behavior. The C# language guarantees the round-trip.

```csharp
// GOOD — delete; OR if the property has validation/computation, test that
[Fact]
public void SetEmail_WithInvalidFormat_ThrowsArgumentException()
{
    var sut = new User();
    var act = () => sut.Email = "not-an-email";
    act.Should().Throw<ArgumentException>();  // actual validation
}
```

#### B17. Tests of generated code (record equality, AutoMapper field-by-field, EF projection shape)

```csharp
// BAD — field-by-field AutoMapper test
[Fact]
public void UserDto_MapsAllFieldsFromUser()
{
    var user = new User { Id = "1", Name = "Alice", Email = "a@b" };
    var dto = mapper.Map<UserDto>(user);
    dto.Id.Should().Be(user.Id);
    dto.Name.Should().Be(user.Name);
    dto.Email.Should().Be(user.Email);  // tests the AutoMapper profile generator
}
```

**Why scaffolding**: Validates what the generator was configured to produce. Configuration drift is caught by AutoMapper's `AssertConfigurationIsValid()` in one test, not field-by-field.

```csharp
// GOOD — single config-validity assertion + behavior tests on output shape
[Fact]
public void MapperConfiguration_IsValid() =>
    mapper.ConfigurationProvider.AssertConfigurationIsValid();

[Fact]
public async Task GetUserDto_InPublicContext_StripsPii()
{
    var response = await client.GetAsync("/api/users/1?context=public");
    var dto = await response.Content.ReadFromJsonAsync<UserDto>();
    dto.Email.Should().BeNull();  // public context strips PII — actual behavior
}
```

---

### Summary table of all 17 bans

| # | Pattern | Why scaffolding | Acceptable replacement |
|---|---|---|---|
| B1 | `Mock<HttpMessageHandler>` | Wire-format coupling | Real test double via `WebApplicationFactory` |
| B2 | `Mock<IServiceClient>` typed HttpClient wrappers | Same as B1 hidden | Integration boundary |
| B3 | DI-registration tests | App start verifies wiring | Behavior assertions on the registered service |
| B4 | Constructor null-check tests | `ArgumentNullException.ThrowIfNull` in production code | Delete; trust the throw helper |
| B5 | Mocking the SUT's collaborators when in-memory is honest | Implementation-shape lock-in | Integration test |
| B6 | Mirror tests | Implementation == implementation | Test the behavior the field participates in |
| B7 | All-mocks + trivial assertion | Interaction-shape lock-in | Integration test or delete |
| B8 | Internal/private method tests via reflection | Implementation lock-in | Public-surface test |
| B9 | Pass-through wrapper tests | Tests one line of code | Delete or test the aggregation |
| B10 | Coverage-fillers | Coverage ≠ behavior | Assert the result, not "doesn't throw" |
| B11 | Language-feature redundancy | Compiler/runtime enforces it | Test the dependent semantic |
| B12 | Snapshot of trivial output | Tests the framework | Test the contract through the framework |
| B13 | Test names without scenario+expected | Reader can't defend the test | Rename per convention or delete |
| B14 | Exhaustive-switch / sealed-hierarchy coverage tests | Compiler enforces exhaustiveness | Test the per-branch behavior |
| B15 | Setup-to-assertion ratio > 10:1 | Setup is the test logic | Integration test with amortized setup |
| B16 | Getter/setter/auto-property tests | C# guarantees the round-trip | Delete or test the validation logic |
| B17 | Generated-code tests (records, AutoMapper, EF projections) | Tests the generator | One config-validity assertion + behavior tests on output |

### How this list is used

1. **At authoring time** — `tests/CLAUDE.md` and `.claude/constraints/testing.md` cite this section; future Claude sessions reading the directives reject these patterns.
2. **At project close** — `/test-diet` skill (added by spec FR-B09 / task CICD-081) uses this list as the classifier for build-vs-maintain reconciliation.
3. **At retroactive cleanup** — Phase 2.5 tasks CICD-082..085 (spec FR-B10) inventory and delete existing instances against this list, targeting BFF unit test count ≤3,500.

## Consequences

### Positive

- **Tier 1 budget (< 3 min p95)** becomes achievable: with the 7,900-test wiring suite no longer blocking PRs (path-aware dispatch + Tier 3 absorption), Tier 1 contents shrink to compile + arch MUST-NOT + changed-surface integration smoke + auth smoke. Per task CICD-011 baseline (sdap-ci p95 = 23.05 min today), this is the only path to the 87% reduction the SC requires.
- **Real bugs gain regression tests** (per "every bug = regression test"). The 9-bug 2026-06-25 cascade becomes a forcing example: each bug ships with a `tests/integration/regression/Issue{N}_*Tests.cs` reproduction.
- **Wiring antipatterns no longer regenerate.** Future Claude Code sessions reading the rewritten directives see integration-first templates and explicit bans.
- **Coverage trend remains observable** without being load-bearing. If coverage % drops dramatically, the nightly report surfaces it — but a single drop doesn't block work.

### Negative / risks accepted

- **Reduced ability to "lift" coverage cheaply.** Teams accustomed to wiring tests will need to author integration tests (slower per test, higher value).
- **Cultural change takes ≥6 months.** Per design.md §257, the test suite "growing back from integration + regression-driven additions" is the long-horizon outcome.
- **Some discovery loss.** Wiring tests sometimes accidentally caught contract changes (e.g., DI-registration test would fail if a service was renamed). Replacement: NetArchTest-style architecture tests at Tier 1 plus endpoint-contract category at Tier 2/3.
- **Tenant-isolation count is currently 1.** Per task CICD-020 inventory: only 1 file in `tests/integration/tenant/**` post-reorg. This is a flag for regression-test-driven backfill over the ≥6-month cultural change window.

### Reversibility

This ADR is binding for ≥6 months from 2026-06-26. After that window, the next architecture review may revisit coverage policy if evidence supports a change. Reversing this ADR requires (a) a new ADR with explicit supersession marker, (b) the evidence section updated with new symptoms refuting S-5 / S-6, and (c) the three directive files (`tests/CLAUDE.md`, `.claude/constraints/testing.md`, `docs/standards/TEST-ARCHITECTURE.md`) co-edited in the supersession PR.

## References

- `docs/standards/TEST-ARCHITECTURE.md` — operational standard (test pyramid, KEEP categories with examples, TimeProvider usage, mock-boundary rules, forcing-function enforcement) — drafted in task CICD-023
- `tests/CLAUDE.md` — rewritten in task CICD-021 (Claude session-loaded directive)
- `.claude/constraints/testing.md` — rewritten in task CICD-022 (Claude constraint loader; line-25 ADR-022 misattribution fixed in same task)
- `.github/workflows/nightly-health.yml` — augmented in task CICD-043 (Tier 3 coverage observation)
- `.claude/skills/task-execute/SKILL.md` Step 9.5 — modified in task CICD-060 (unconditional quality gate for test PRs per spec FR-B07)
- Spec: `projects/ci-cd-unit-test-remediation-r1/spec.md` (FR-B01..B07, SC-06..SC-10, MUST/MUST NOT block)
- Design rationale: `projects/ci-cd-unit-test-remediation-r1/design.md` §§3–5 (symptoms, root cause hypothesis, scope)
- Inventory evidence (transient): `projects/ci-cd-unit-test-remediation-r1/notes/test-inventory-summary.md` — 492 files classified; 11 DELETE; 481 KEEP across all 6 categories (with tenant-isolation backfill flagged)

## Non-supersession note

ADR-022 ("PCF Platform Libraries — Field-Bound Controls Only", Frontend domain) is **unchanged** by this ADR. Earlier drafts of the `ci-cd-unit-test-remediation-r1` project misattributed coverage-% rules to ADR-022; that misattribution lives in `.claude/constraints/testing.md` line 25 and is fixed in the same project's task CICD-022. ADR-022 status remains Accepted; its scope (React 16/17 on PCF, platform-provided React) is independent of and orthogonal to testing strategy.
