# Test Architecture

> **Last Updated**: 2026-06-26
> **Last Reviewed**: 2026-06-26
> **Reviewed By**: ci-cd-unit-test-remediation-r1 (task CICD-023, spec FR-B04)
> **Status**: New
> **Applies To**: All .NET test projects (`tests/unit/**`, `tests/integration/**`). React/PCF Jest is OUT of scope.
> **Supersedes**: The coverage-% gating culture previously codified in `tests/CLAUDE.md` (80/70/90% targets), `.claude/constraints/testing.md` (80% line coverage MUST rule, "<100ms per test" rule, "mock all I/O" rule). This document is the binding cross-cutting reference for what good Spaarke tests look like — concrete patterns to use, concrete anti-patterns to refuse.

---

## 1. Purpose

This document binds the **shape** of the Spaarke test portfolio: which tests are worth writing, which mocks are worth using, and which forcing functions keep that shape stable as the codebase evolves. It is the standard cited from `tests/CLAUDE.md`, `.claude/constraints/testing.md`, root `CLAUDE.md` §17, and ADR-038 (Testing Strategy).

Spaarke deliberately rejects coverage-percentage as a quality signal. Coverage is **observed** (Tier 3 nightly health) and **never gating** (no PR check, no merge gate, no rigor-level escalation triggered by % movement). The 2026 Q2 7,900-test inventory demonstrated that coverage maximization produced wiring tests that broke on every refactor while catching zero of the last nine production bugs. Coverage targets are explicitly absent from this document and MUST NOT be re-introduced in any directive file for ≥6 months (binding per `ci-cd-unit-test-remediation-r1` spec MUST NOT rules).

### Rules

1. **MUST** structure new tests as integration-first where a meaningful integration boundary exists (per §2)
2. **MUST** place new tests under one of the six KEEP path conventions (per §3)
3. **MUST** use `TimeProvider` for any time-dependent test (per §4)
4. **MUST** mock at module boundaries only — never at the HTTP-handler level or DI-registration level (per §5)
5. **MUST NOT** introduce coverage-% targets or "<N ms per test" rules to any directive file
6. **MUST NOT** add tests in the explicit ban list (per §5)

---

## 2. Test Pyramid (Integration-Heavy)

Spaarke inverts the classical unit-heavy pyramid. The shape is:

```
        ┌─────────────────┐
        │   UI tests       │   0% — not yet (React/PCF Jest is out of scope for this standard)
        ├─────────────────┤
        │                 │
        │   Integration    │   ~70% — heavy: real DI graph, real Dataverse where possible,
        │                 │            real HttpClient against test BFF, end-to-end auth path
        │                 │
        ├─────────────────┤
        │   Unit (domain) │   ~30% — modest: pure logic only, no I/O, no mocks
        └─────────────────┘
```

### Rationale (binding)

- **BFF + Dataverse + Graph + AI = an integration system.** The interesting failure modes — claims propagation, OBO token exchange, ProblemDetails shape, Dataverse retry semantics, tenant isolation enforcement — only surface when real components interact. Unit tests that mock the boundaries cannot observe these failures.
- **Wiring tests are a category we have decided not to write.** Constructor null-checks, DI-registration assertions, and `Mock<HttpMessageHandler>` setups exercise the test harness, not the system. They generate ~60% of churn on production refactors with no defect detection (evidence: S-5, S-6 in `ci-cd-unit-test-remediation-r1/design.md` §3).
- **No UI tests yet.** React/PCF Jest tests have a different failure mode (DOM, JSX, Fluent v9 theming) and a different framework. A sibling standard will be authored if/when that surface stabilizes.

### Implications

- New code with a meaningful integration surface (endpoint, service that talks to Dataverse, auth path, tenant-scoped query) **MUST** ship with an integration test, not a unit test.
- Pure logic (transforms, validators, parsers, FormatX helpers) **MUST** go under `tests/unit/domain/**` as a fast in-process unit test.
- Anything between — a service with one mockable dependency — defaults to integration. If you cannot articulate why a unit test is more valuable than an integration test, write the integration test.

---

## 3. Six KEEP Categories

These six categories are the **only** test categories Spaarke maintains. Path conventions encode them at runtime — `task-execute` Step 9.5 (code-review) checks paths, not CSVs.

| # | Category | Path convention | Definition | Concrete example |
|---|----------|-----------------|------------|------------------|
| 1 | **regression** | `tests/integration/regression/**` | Every confirmed production bug becomes a regression test that fails before the fix and passes after. Named after the bug ID or PR. | `tests/integration/regression/Issue417_DailyBriefingCascadeTests.cs` — reproduces the 9-bug cascade caught in June 2026 by hitting `/api/ai/briefing` with the exact payload that triggered the original failure, asserts ProblemDetails shape + no swallowed exception. |
| 2 | **security-auth** | `tests/integration/auth/**` | Auth path, token validation, claims handling, OBO exchange, scope enforcement. Anything that decides "may this caller perform this action." | `tests/integration/auth/OnBehalfOfTokenExchangeTests.cs` — drives a real `WebApplicationFactory<Program>` with a forged user assertion, asserts the OBO exchange succeeds for valid claims and returns 401 ProblemDetails for missing `scp=Files.ReadWrite.All`. |
| 3 | **data-mutation** | `tests/integration/data-mutation/**` | Writes, transactions, rollback, optimistic concurrency, audit-stamp invariants. Anything that changes Dataverse state. | `tests/integration/data-mutation/DocumentUploadCommitTests.cs` — drives `POST /api/files/{containerId}/upload` end-to-end against a test container, asserts the Dataverse `sprk_document` row is created with correct `sprk_owner` + `sprk_createdon`, and asserts rollback when SPE commit fails mid-flight. |
| 4 | **tenant-isolation** | `tests/integration/tenant/**` | Tenant boundary enforcement — that user A in tenant X cannot read/write resources in tenant Y, that queries filter by tenant, that cross-tenant references are rejected. | `tests/integration/tenant/CrossTenantDocumentAccessTests.cs` — provisions documents in tenant A, authenticates as user in tenant B, asserts every `GET`/`PATCH`/`DELETE` returns 404 ProblemDetails (not 403, to avoid information disclosure). |
| 5 | **endpoint-contract** | `tests/integration/contract/**` | Every new endpoint must have at least one integration test asserting the request/response contract (route, verbs, status codes, ProblemDetails shape, content-type). | `tests/integration/contract/SemanticSearchEndpointContractTests.cs` — asserts `POST /api/ai/search` accepts `application/json`, returns `200 + SearchResultsDto` for happy path, `400 + ProblemDetails` for missing `query` field, `401 + ProblemDetails` for missing bearer token. |
| 6 | **domain-logic** | `tests/unit/domain/**` | Pure logic — no I/O, no mocks, no DI. Validators, parsers, formatters, transforms, state machines. Fast (in-process), deterministic, no `TimeProvider` ceremony needed beyond passing `DateTimeOffset` parameters. | `tests/unit/domain/DocumentNameValidatorTests.cs` — asserts `DocumentNameValidator.IsValid("doc<>name.pdf") == false` and `IsValid("doc.pdf") == true`. Twenty assertions per file is fine; the file is small, the test is fast, the failure is localized. |

### Deletion safety (binding)

Per `ci-cd-unit-test-remediation-r1` spec FR-B06, any deletion under one of these six paths requires a **same-PR replacement** of equivalent coverage in the same category. Code-review at Step 9.5 enforces this path check. A test in `tests/integration/auth/**` may be deleted only if another test in `tests/integration/auth/**` lands in the same PR exercising the same auth path.

### What is NOT a KEEP category

If a test does not fit one of the six, it is a DELETE candidate — even if it passes. The most common DELETE patterns are exhaustively listed in §5's ban list.

---

## 4. TimeProvider over Stopwatch

Time-dependent tests **MUST** inject `TimeProvider` and **MUST NOT** read wall-clock time via `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, or `Stopwatch`. Wall-clock and `Stopwatch` create non-deterministic tests that flake under CI load, fail on slow developer machines, and produce unreproducible timing assertions.

### Anti-pattern (ban)

```csharp
// BAD — flakes under CI load, fails on slow developer machines
[Fact]
public async Task CacheEntryExpires_AfterTtl()
{
    var cache = new TokenCache(ttl: TimeSpan.FromMilliseconds(500));
    cache.Set("user-1", "token-abc");

    var sw = Stopwatch.StartNew();
    await Task.Delay(600);
    sw.Stop();

    Assert.True(sw.ElapsedMilliseconds >= 500); // flakes when CI is busy
    Assert.Null(cache.Get("user-1"));            // races against actual cache eviction
}
```

### Correct pattern (binding)

```csharp
// GOOD — deterministic, fast, reproducible
[Fact]
public void CacheEntryExpires_AfterTtl()
{
    var time = new FakeTimeProvider(start: DateTimeOffset.Parse("2026-06-25T12:00:00Z"));
    var cache = new TokenCache(time, ttl: TimeSpan.FromMinutes(5));

    cache.Set("user-1", "token-abc");
    Assert.Equal("token-abc", cache.Get("user-1"));

    time.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
    Assert.Null(cache.Get("user-1"));
}
```

### Wiring

- Production code that observes time **MUST** accept `TimeProvider` via constructor injection. `Program.cs` registers `TimeProvider.System` as the default.
- `Microsoft.Extensions.TimeProvider.Testing` provides `FakeTimeProvider` — already a transitive dependency through `Microsoft.Extensions.Hosting`. No new package needed.
- For domain-logic tests under `tests/unit/domain/**`, prefer **pure functions** that accept `DateTimeOffset now` as a parameter — no `TimeProvider` needed if the function is stateless.

### Forbidden APIs in test code

- `DateTime.UtcNow` / `DateTime.Now` / `DateTimeOffset.UtcNow` / `DateTimeOffset.Now`
- `Stopwatch.StartNew()` / `Stopwatch.GetTimestamp()` for assertions (debug-only logging is fine)
- `Task.Delay(...)` followed by a wall-clock assertion (use `FakeTimeProvider.Advance` instead)
- `Thread.Sleep(...)` in any test

---

## 5. Mock-Boundary Rules

Mock at **module boundaries**, not inside the system. A module boundary is a seam the production code itself has — a Dataverse client, an external HTTP client, an `IDistributedCache` you legitimately own. A non-boundary is a class under test, an HTTP handler pipeline inside `HttpClient`, or a DI registration.

### Acceptable mocking (examples)

| Boundary | Acceptable mock | Example use |
|----------|----------------|-------------|
| `IDataverseClient` (production-defined interface around the Dataverse SDK) | `Mock<IDataverseClient>` set up with `Setup(x => x.RetrieveAsync(...)).ReturnsAsync(...)` | Integration test verifies an endpoint's ProblemDetails shape when Dataverse returns a 404; mocking is the only way to reliably trigger that 404. |
| `IDistributedCache` | `Mock<IDistributedCache>` or a real in-memory implementation | Test that a cache miss triggers exactly one downstream call (`Verify(x => x.GetAsync(...), Times.Once)`). |
| `TimeProvider` | `FakeTimeProvider` (per §4) | All time-dependent tests. |
| Service Bus `ServiceBusSender` | `Mock<ServiceBusSender>` | Verify a background job is enqueued with the correct payload after an endpoint succeeds. |

### Banned mocks (explicit ban list)

The following mocks are **forbidden** in any new test. Existing tests using these patterns are DELETE candidates (per the six KEEP categories in §3 — no ban-list pattern fits any KEEP category).

| # | Forbidden pattern | Why it's wrong | What to do instead |
|---|-------------------|---------------|--------------------|
| 1 | **`Mock<HttpMessageHandler>`** — building a fake HTTP pipeline inside `HttpClient` to assert the BFF "would have" called Graph | Mocks the pipeline, not the boundary. Breaks on every Graph SDK upgrade. Asserts headers/bodies the production code never reads back. | Use `WebApplicationFactory<Program>` and intercept at the actual integration seam: mock `IGraphClientFactory`'s return value, or run against a real test Graph endpoint. |
| 2 | **`Mock<IServiceClient>` for ServiceClient (Dataverse SDK)** — mocking the Microsoft.PowerPlatform.Dataverse.Client surface | The Dataverse SDK has 200+ surface methods; partial mocks return default values that mask real failures. | Use `IDataverseClient` (Spaarke's facade — a real boundary) or run integration tests against a Dataverse test environment. |
| 3 | **DI-registration tests** — `services.BuildServiceProvider(); Assert.NotNull(sp.GetService<ISomething>())` | Tests the test harness. Passes whenever code compiles. Generates noise on every refactor. Caught 0 of the last 9 production bugs. | If you genuinely need to verify a service resolves, write an endpoint integration test — it exercises the same DI graph end-to-end with a real outcome to assert. |
| 4 | **Constructor null-checks** — `Assert.Throws<ArgumentNullException>(() => new Service(null, mock2, mock3))` | The C# compiler + nullable reference types already enforce this. Hand-written tests of compiler-enforced behavior are pure churn. | Delete. If the constructor doesn't enforce non-null, the production code itself is the bug — fix it once, no test needed. |
| 5 | **Mocking the class under test's collaborators when an integration boundary is available** — e.g., mocking `SpeFileStore` inside a `DocumentsController` unit test when a `WebApplicationFactory<Program>` integration test would exercise the real flow | Asserts the system "would have" called collaborators in the right order — but the right order is enforced by the production code, not by the test. Inverts dependencies into the test. | Write the integration test. If integration is genuinely too expensive (it almost never is), justify in the test class's XML doc comment with a concrete cost number. |

### One-line acceptable example per banned pattern (so authors see the alternative)

- **Banned 1 alternative** — `tests/integration/contract/GraphFileDownloadContractTests.cs`: `WebApplicationFactory<Program>` + `Mock<IGraphClientFactory>` returns a `GraphServiceClient` wired to a stubbed `IRequestAdapter`. The mock lives at the factory boundary; the request pipeline is real.
- **Banned 2 alternative** — `tests/integration/data-mutation/DocumentCreateTests.cs`: `Mock<IDataverseClient>` returns a deterministic `Entity` for the `RetrieveAsync` call. The Spaarke facade is mocked; the Dataverse SDK is not.
- **Banned 3 alternative** — `tests/integration/contract/HealthzEndpointTests.cs`: hits `GET /healthz` end-to-end through `WebApplicationFactory<Program>`. If DI is broken, the test 500s; the assertion is on the response, not on `GetService<T>`.
- **Banned 4 alternative** — none. The pattern has no defensive value. Delete.
- **Banned 5 alternative** — `tests/integration/auth/DocumentReadAuthorizationTests.cs`: `WebApplicationFactory<Program>` with a real `DocumentsController` and real `SpeFileStore`; `Mock<IGraphClientFactory>` substitutes only at the Graph boundary.

---

## 6. Forcing-Function Enforcement

This standard is binding because three mechanisms enforce it at the points where tests are written or reviewed:

| # | Enforcement point | What it checks | Where it is wired |
|---|-------------------|---------------|-------------------|
| 1 | **`task-execute` Step 9.5 — code-review for all test-modifying PRs** | Every PR that touches `tests/**` runs the `code-review` skill at Step 9.5. The reviewer checks: (a) any deletion under a KEEP path has a same-PR replacement (§3); (b) no banned mock patterns are introduced (§5); (c) any time-dependent test uses `TimeProvider` (§4); (d) new tests live under one of the six KEEP paths. Test PRs are explicitly FULL rigor (NOT auto-STANDARD) for ≥6 months. | `.claude/skills/task-execute/SKILL.md` Step 9.5; root `CLAUDE.md` §8 rigor table; `ci-cd-unit-test-remediation-r1` spec FR-B07 |
| 2 | **`nightly-health.yml` Tier 3 (observation, never gating)** | Full integration test run against a real-ish environment, coverage observation (tracked for trend, never a merge gate), Trivy scan, dependency audit. Coverage trend reports are an FYI signal — a 5% drop triggers an investigation issue, not a block. | `.github/workflows/nightly-health.yml`; `ci-cd-unit-test-remediation-r1` spec FR-A04 |
| 3 | **ADR-038 (planned)** — supersedes ADR-022's coverage clauses | Records the policy reversal (coverage-as-observation, not gate) with the evidence section (symptoms S-5, S-6 from `ci-cd-unit-test-remediation-r1` design.md §3). Standalone testing-strategy ADR (NOT a supersession of ADR-022, which is the PCF Platform Libraries ADR — the spec FR-B03 misattribution is corrected in `.claude/constraints/testing.md`). | `.claude/adr/ADR-038-testing-strategy.md` (drafted in `ci-cd-unit-test-remediation-r1` task CICD-024) |

### What is NOT a forcing function

- A CI script checking test counts or test names — explicitly rejected. The path conventions are the contract; the reviewer checks paths in the diff.
- A pre-commit hook — too slow, too noisy, easy to bypass.
- A nightly job that opens issues for stale tests — coverage observation is the only nightly signal we want.

---

## 7. Cross-References

| Reference | What it adds |
|-----------|--------------|
| [`tests/CLAUDE.md`](../../tests/CLAUDE.md) | Per-test-tree authoring guidance (integration-first AAA template, "every bug = regression test", "every new endpoint = ≥1 integration test"). Cites this document as the cross-cutting standard. |
| [`.claude/constraints/testing.md`](../../.claude/constraints/testing.md) | Binding MUST/MUST NOT rules in constraint form (path conventions, ban list, no coverage targets). Cites this document for definitions and examples. |
| [`.claude/adr/ADR-038-testing-strategy.md`](../../.claude/adr/ADR-038-testing-strategy.md) (planned, forward reference) | Standalone testing-strategy ADR recording the policy reversal away from coverage-% gating. NOT a supersession of ADR-022 (which is PCF Platform Libraries). |
| [`docs/procedures/testing-and-code-quality.md`](../procedures/testing-and-code-quality.md) | Day-to-day testing workflow: how to run, how to debug, how to file new tests in the right path. |
| [`projects/ci-cd-unit-test-remediation-r1/spec.md`](../../projects/ci-cd-unit-test-remediation-r1/spec.md) | Source-of-truth FRs (FR-B01..FR-B07) that produced this standard. |
| [`projects/ci-cd-unit-test-remediation-r1/design.md`](../../projects/ci-cd-unit-test-remediation-r1/design.md) | Symptom evidence (S-5, S-6) — why coverage-% culture failed in practice. |

---

*Maintained as part of `docs/standards/`. To extend or revise: update this file, then update the cross-references and the rule numbering in §1. Any revision that introduces a coverage-% target requires an explicit ADR superseding ADR-038 — this is not a casual edit.*
