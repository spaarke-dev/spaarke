<!--
Maintainer notes (stripped before Claude reads this file):
- Loads whenever Claude reads or edits a file under tests/. Keep it to the rules an author needs at the keyboard;
  the canonical text is ADR-038, the MUST/MUST NOT list is .claude/constraints/testing.md.
- Size target: about 12 KB — a target, not a cap; exceed it when the content is load-bearing and say why in the PR. Do not paste ban examples here; they live in ADR-038 §7.
- Headings cited by name elsewhere — do not rename: "Expect to Defend at Project Close" (test-diet skill),
  "Authoring Template — Integration-First" (.claude/constraints/testing.md rule 2).
- Previous full version (bad/good examples for B6–B17, history): .claude/archive/2026-10-07/modules/tests.CLAUDE.md
-->
# tests/ — test authoring rules

**Scope:** .NET xUnit tests (ADR-038). `tests/e2e`, `tests/load`, `tests/scripts`, `tests/manual` and `tests/eval` follow their own READMEs; the KEEP-path rules below do not apply to them.

Canonical source: [ADR-038](../docs/adr/ADR-038-testing-strategy.md). MUST/MUST NOT list: [`.claude/constraints/testing.md`](../.claude/constraints/testing.md). Operational standard: [`docs/standards/TEST-ARCHITECTURE.md`](../docs/standards/TEST-ARCHITECTURE.md). Test PRs run at FULL rigor (root `CLAUDE.md` §8).

## KEEP paths (deletion-protected)

```
tests/
├── integration/
│   ├── auth/**           # security-auth (OBO, claims, token validation)
│   ├── regression/**     # one file per past production bug — "every bug = regression test"
│   ├── data-mutation/**  # writes, transactions, rollback semantics
│   ├── tenant/**         # tenant boundary enforcement (cross-tenant reads MUST 404)
│   ├── contract/**       # endpoint contract: route + status + ProblemDetails + payload shape
│   └── seam/**           # vertical slice across an AI convergence seam (dispatch→executor→ledger→disposition)
│                         #   with production types — router-unit ≠ working slice (E-40)
├── unit/domain/**        # pure domain logic — calculations, mappings, parsing, serialization
└── Spaarke.ArchTests/**  # structural fitness functions (eighth KEEP path, ADR-038 Amendment A1)
```

Removing a file under any KEEP path requires a same-PR replacement covering the same scenario — or, when the scenario no longer exists (feature or path removed), a statement in the PR saying so, approved explicitly at code-review (`task-execute` Step 9.5).

**Where they compile.** The seven `integration/*` and `unit/domain` folders have no project file of their own; they are compiled into `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` (`Compile Include` per folder). The globs already exist: a new file under a KEEP folder needs no csproj change, and a second identical glob breaks the build (NETSDK1022 — it happened once, from two branches each adding it). `Spaarke.ArchTests` is its own project and is **not** in `Spaarke.sln`.

### Structural fitness functions — `tests/Spaarke.ArchTests/**`

These assert an invariant over source or assemblies rather than runtime behaviour (`LayerDependencyTests`, `CredentialGuardTests`, `CredentialCensusTests`, …). ADR-038's "Some discovery loss" consequence names architecture tests as the sanctioned **replacement** for the wiring tests banned by B1–B5, so a classifier that deletes them deletes the mechanism the ADR prescribes.

Two classifier heuristics do **not** apply here:
- **Naming (B13)** — the name states the invariant enforced (`NoSecretBearingConfidentialClientOutsideTheAllowlist`), not `{Method}_{Scenario}_{ExpectedResult}`.
- **Setup-to-assertion ratio (B15)** — the arrange step is a source scan of the server tree; a high ratio is inherent.

They are replaced, not removed, by these rules:

| Rule | Why |
|---|---|
| Every rule carries a **negative control** proving the detector fires on a seeded violation | A detector nobody has seen fail is a detector nobody knows works |
| Every rule carries a **positive control** proving it does NOT fire on the sanctioned shape | A guard that flags the code it protects gets deleted rather than obeyed |
| Every allowlist / census entry carries a **written reason and an ADR citation** | An unexplained exemption is indistinguishable from an oversight six months later |
| The **maintenance procedure** lives in the test file itself | The person who trips the guard is the person who needs the procedure |

## Mandatory Authoring Rules

| Rule | Path |
|---|---|
| **Every bug fix → one new regression test** | `tests/integration/regression/Issue{N}_*Tests.cs` |
| **Every new endpoint → ≥1 integration test** | `tests/integration/contract/**` |
| **Every new auth path → ≥1 integration test** | `tests/integration/auth/**` |
| **Every new write path → ≥1 integration test verifying rollback semantics** | `tests/integration/data-mutation/**` |
| **Every new tenant-touching feature → ≥1 isolation test** | `tests/integration/tenant/**` |
| **A change to a dispatch-spine seam → a vertical-slice test** | `tests/integration/seam/**` |
| **Pure domain logic → unit test** | `tests/unit/domain/**` |

A planned test that fits no KEEP path is the wrong shape: re-scope it, or escalate to an ADR amendment. Shape target (ADR-038 §1): roughly 70% integration / 30% unit — a shape, not a gate.

## Authoring Template — Integration-First (PREFERRED)

```csharp
[Fact]
public async Task CreateDocument_WithValidInput_ReturnsCreatedAndPersists()
{
    // Arrange — a WebApplicationFactory<Program> fixture; test tenant; FakeTimeProvider
    using var factory = new TestWebApplicationFactory();
    var client = factory.CreateClient();
    var request = new CreateDocumentRequestBuilder().Build();

    // Act
    var response = await client.PostAsJsonAsync("/api/documents", request);

    // Assert — behaviour (HTTP contract + persistence side effect)
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<Document>();
    created!.Id.Should().NotBeNullOrEmpty();
    var persisted = await factory.GetRepository().GetByIdAsync(created.Id);
    persisted!.Name.Should().Be(request.Name);
}
```

`TestWebApplicationFactory` and `GetRepository()` are placeholders. KEEP-path tests compile into `Sprk.Bff.Api.Tests`, so use its fixtures: `CustomWebAppFactory` (`tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs`) or the per-area `WebApplicationFactory<Program>` subclasses in that project (e.g. `WorkspaceTestFixture`); copy the nearest one. Fixtures in `tests/integration/Spe.Integration.Tests` are a separate assembly and cannot be referenced from KEEP-path tests.

## Authoring Template — Unit (DOMAIN LOGIC ONLY)

Only for pure domain logic under `tests/unit/domain/**`: no mocks, no DI, no I/O. If you are about to mock the class-under-test's collaborators, write an integration test instead.

```csharp
[Fact]
public void Calculate_WithValidInputs_ReturnsExpectedSum()
{
    var sut = new ScoreCalculator();
    var result = sut.Calculate(new[] { 10, 20, 30 });
    result.Should().Be(60);
}
```

## Banned Antipatterns (17)

Do not write tests of these shapes. Bad/good examples for every ban: **ADR-038 §7** (summary table at the end of §7). `tests/Spaarke.ArchTests/Adr038TestBanGuardTests.cs` enforces some of them.

| # | Ban | # | Ban |
|---|---|---|---|
| B1 | `Mock<HttpMessageHandler>` — use the `WebApplicationFactory` boundary | B10 | Coverage-fillers (`NotThrow()` / `NotNull()` to lift %) |
| B2 | `Mock<IServiceClient>` or typed-HttpClient wrapper mocks hiding B1 | B11 | Tests of what the compiler enforces (`required`, record equality) |
| B3 | DI-registration tests (`GetRequiredService<X>()` not null) | B12 | Snapshot tests of trivial output (JSON round-trip, default `ToString`) |
| B4 | Constructor null-argument tests — guard with `ThrowIfNull` only where null can arrive, and don't test it | B13 | Names without scenario + expected result |
| B5 | Mocking the class-under-test's own collaborators when a real boundary is cheaper | B14 | Exhaustive-switch / sealed-hierarchy coverage tests |
| B6 | Mirror tests (one test per production method) | B15 | Setup-to-assertion ratio > 10:1 — a signal to move to an integration boundary; where the arrange is inherent (multi-record fixture), say so in a comment |
| B7 | All-mocks + trivial assertion (`Verify(Times.Once)`) | B16 | Pure getter/setter/auto-property tests |
| B8 | Reflection into non-public members (`InternalsVisibleTo` is allowed — Amendment A2) | B17 | Generated-code field-by-field tests (record equality, AutoMapper, EF projections) |
| B9 | Pass-through wrapper tests (`=> _service.DoIt(x)`) | | |

## Expect to Defend at Project Close

**Every test you write today, expect to defend at project close.** A test that cannot be defended as maintain-class — a regression-protector, a contract anchor, or business logic with branches, under a KEEP path — is deleted by the `/test-diet` pass in the project's `090-wrapup-*` task.

| Class | Half-life | Purpose | KEEP path? |
|---|---|---|---|
| **Build-class** | days/weeks (the project) | Drive design, validate construction, satisfy coverage % | NO — deleted in diet pass |
| **Maintain-class** | months/years | Protect against regression, anchor a contract, exercise branched business logic | YES — lives in a KEEP path |

Before authoring, ask:
1. **What production behaviour would break if this test were deleted?** No concrete answer → build-class.
2. **Does it live under a KEEP path?** If not, it is the wrong shape — re-scope, or escalate to an ADR amendment.
3. **Is the assertion about behaviour the caller would notice, or implementation the caller can't see?** The latter is scaffolding (ADR-038 §7).

A test that passes all three is maintain-class: name it `{Method}_{Scenario}_{ExpectedResult}`, place it in the right KEEP path. A test that fails any of them: fix it now (rescope, rename, restructure) or accept that `/test-diet` will delete it. Binding ≥ 6 months from 2026-06-26 (ADR-038).

## Conventions

- **Naming:** `{MethodOrEndpoint}_{Scenario}_{ExpectedResult}` — e.g. `GetDocument_WhenNotFound_ReturnsNotFound`.
- **Class names:** `{ClassUnderTest}Tests.cs` (domain unit tests), `{Endpoint}ContractTests.cs` (contract), `Issue{N}_{Description}Tests.cs` (regression).
- **Frameworks:** xUnit, Moq (not NSubstitute), FluentAssertions (prefer it over `Assert.Equal` / `Assert.NotNull`). Versions: the test `.csproj` files (`Directory.Packages.props` is not authoritative — central package management is off).
- **Time:** `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`). Banned in tests: `Stopwatch`, `DateTime.UtcNow`, `Task.Delay` — they flake on shared CI runners. Pattern: construct the SUT with a `FakeTimeProvider`, then `time.Advance(...)`.
- **Moq** for module-boundary mocks only, never for `HttpClient` / `HttpMessageHandler`.
- **Test data builders** for complex objects, next to the test class or under `tests/integration/Shared/` for cross-class reuse.
- **Real test tenant over in-memory emulators** where the code exercises Dataverse-specific behaviour (transactions, FetchXML semantics, policy enforcement). Clean up test data after each integration test.

## Coverage

Measured, **never a gate** (nightly `nightly-health.yml`). Binding ≥ 6 months from 2026-06-26 (ADR-038): do not reintroduce coverage-% targets in any directive file.

## Running tests

```bash
dotnet test                                                      # Spaarke.sln — does NOT include Spaarke.ArchTests
dotnet test tests/Spaarke.ArchTests/                             # fitness functions — always run this too
dotnet test tests/unit/Sprk.Bff.Api.Tests/                       # all KEEP folders (compiled here)
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ComposeActiveDocumentContractTests"
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings   # informational only
```
