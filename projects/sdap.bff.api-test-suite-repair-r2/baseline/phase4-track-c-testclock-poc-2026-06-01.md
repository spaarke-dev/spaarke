# Phase 4 Track C — TestClock + seeded-Guid PoC findings

> **Task**: 042 — `P4.C — TestClock + seeded-Guid PoC in Services/Workspace/* (greenfield abstraction)`
> **Date**: 2026-06-01
> **Rigor**: FULL (greenfield `src/` abstraction + Step 9.5 mandatory)
> **Predecessor design intent**: `projects/sdap.bff.api-test-suite-repair-r2/design.md §5.5 Track C` + spec.md FR-13
> **Scope discipline (D-04)**: pilot-grade — ONE consuming class converted; abstractions ship greenfield; rollout deferred to r3 generalization
> **Commit policy**: this task produces a findings doc + working code; commit is bundled by main session per project convention (no commit by the agent itself).

---

## 1. Target class chosen — `PortfolioService`

**File**: `src/server/api/Sprk.Bff.Api/Services/Workspace/PortfolioService.cs`

### Why this one (out of the 10 Workspace files)

I inventoried direct non-deterministic call sites across `src/server/api/Sprk.Bff.Api/Services/Workspace/` (10 files, 4194 LOC). Results:

| File | `DateTime[Offset].UtcNow` | `Guid.NewGuid()` | Has test class | PoC fit |
|---|---|---|---|---|
| WorkspaceAiService | 2 | 0 | no | medium (AI playbook deps complicate stubbing) |
| PortfolioService | 2 | 0 | **no** | **best** — clean inputs, both outputs observable in records (`CachedAt`, `Timestamp`) |
| MatterPreFillService | 0 | 1 | no | medium (only requestId — internal logging context) |
| TodoGenerationService | 2 (`DateTime.UtcNow`) | 0 | yes (31 KB) | poor — `BackgroundService` with `PeriodicTimer`; not a focused unit |
| BriefingService | 2 | 0 | no | poor — depends on PortfolioService + IOpenAiClient |
| ProjectPreFillService | 0 | 1 | no | medium |
| Others | 0 | 0 | n/a | n/a |

`PortfolioService` won on three axes simultaneously:

1. **Two non-determinisms with observable outputs.** Both `DateTimeOffset.UtcNow` calls stamp the `CachedAt` / `Timestamp` fields on the returned response records — test-assertable without indirection.
2. **No pre-existing test class.** Creating a new focused `PortfolioServiceTests.cs` for the PoC means the determinism pattern is demonstrated in a clean, idiomatic test file rather than retrofitted into a 31 KB test class with its own state. Risk to existing tests: zero.
3. **Simple dependencies (3, all easily mocked).** `IDistributedCache` + `IGenericEntityService` + `ILogger<>` — no AI / OBO / file-store coupling. Tests stay under the 100 ms-per-test budget from `.claude/constraints/testing.md`.

### Smallness sanity check (D-04 pilot-grade)

- Production lines modified: ~6 (constructor signature, two call-site replacements, static→instance modifier, XML doc paragraph)
- Production lines added: 1 new file (`IGuidProvider.cs`) — interface + default impl, ~52 lines including XML docs
- DI lines added: 2 (`TryAddSingleton<TimeProvider>(TimeProvider.System)` + `TryAddSingleton<IGuidProvider, DefaultGuidProvider>()`)
- Test lines added: 1 new file (`PortfolioServiceTests.cs`) — 5 tests, ~230 lines including inline `FixedTimeProvider` + `FakeGuidProvider` helpers

Total under r2's NFR-02 50%-line-replacement guard with significant margin.

---

## 2. Pattern applied — `TimeProvider` (BCL) + `IGuidProvider` (custom seam)

### Time seam — `System.TimeProvider` from .NET 8 BCL

Per the user instruction in the task scope ("prefer over custom interface — newer .NET BCL API") and the existing precedent in `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionSyncTests.cs`, I adopted **`System.TimeProvider`** rather than a hand-rolled `IClock`/`ISystemClock`.

**Reasons** (recorded for r3 generalization):

1. **No NuGet dependency required.** `TimeProvider` is in `System.Runtime` (BCL) since .NET 8. Avoids triggering `bff-extensions.md §B` ("MUST check `dotnet list package --vulnerable --include-transitive` before adding any package") or risking publish-size regression vs the ~60 MB baseline.
2. **Existing test precedent.** `PrecedentProjectionSyncTests.FixedTimeProvider` already subclasses `TimeProvider` with a fixed `GetUtcNow()`. Reusing the shape gives the codebase **one** determinism pattern, not two. Phase 5 task 080 can codify a single procedure.
3. **`TimeProvider` is abstract, not an interface** — fits ADR-010's "MUST register concretes by default (not interfaces); MUST NOT create interfaces without genuine seam requirement". The seam is provided by the BCL itself.
4. **Production override path is clean**: `services.TryAddSingleton<TimeProvider>(TimeProvider.System)`. Tests pass an instance via constructor; production resolves to the platform clock.
5. **Future-proof**: `Microsoft.Extensions.TimeProvider.Testing` NuGet (`FakeTimeProvider`) is a drop-in if the inline `FixedTimeProvider` ever needs richer features (e.g., advance-time-by, scheduled-callbacks). r3 should evaluate that package's CVE / publish-size cost vs the inline helper before adopting.

### Identity seam — `IGuidProvider` (custom interface, justified)

For `Guid.NewGuid()` there is no BCL equivalent of `TimeProvider`. I introduced a minimal interface + default implementation:

```csharp
public interface IGuidProvider { Guid NewGuid(); }

public sealed class DefaultGuidProvider : IGuidProvider {
    public Guid NewGuid() => Guid.NewGuid();
}
```

**ADR-010 compliance** (recorded in `IGuidProvider.cs` XML docs): justified by (a) genuine test-seam requirement, (b) no BCL equivalent, (c) production surface is a single method — minimal. Matches the "allowed seams" pattern in ADR-010 §Allowed Seams ("single-impl + test seam").

### DI registration (in `Infrastructure/DI/WorkspaceModule.cs`)

```csharp
services.TryAddSingleton<TimeProvider>(TimeProvider.System);
services.TryAddSingleton<IGuidProvider, DefaultGuidProvider>();
```

`TryAddSingleton` (not `AddSingleton`) deliberately: the seam may be pre-registered elsewhere (today by tests via constructor injection; tomorrow by other feature modules) without conflict. This is the same TryAdd discipline used by `WorkspaceModule` for `Options<TodoGenerationOptions>`.

### Test-side helpers — kept inline

Both `FixedTimeProvider` (TimeProvider subclass) and `FakeGuidProvider` (IGuidProvider impl with a `Queue<Guid>`) live inline at the bottom of `PortfolioServiceTests.cs` as `private sealed class`. r3 should evaluate promoting them to a shared test-helper assembly once 2+ Workspace test classes adopt the pattern — premature now per D-04.

The `FakeGuidProvider` **throws `InvalidOperationException` when exhausted** (rather than degrading to `Guid.Empty`). This is a deliberate design choice: missing test seeds should surface immediately as a failed test, not as a silent zero-Guid in production assertions.

---

## 3. Code changes summary

### Files modified (2)

1. **`src/server/api/Sprk.Bff.Api/Services/Workspace/PortfolioService.cs`**
   - Added optional `TimeProvider? timeProvider = null` constructor parameter — defaults to `TimeProvider.System` so existing DI behavior unchanged.
   - Added `private readonly TimeProvider _timeProvider` field.
   - Replaced `Timestamp: DateTimeOffset.UtcNow` (line ~193) with `Timestamp: _timeProvider.GetUtcNow()`.
   - Changed `private static PortfolioSummaryResponse AggregatePortfolio(...)` to instance method (drops `static`) so it can read `_timeProvider`; replaced `CachedAt: DateTimeOffset.UtcNow` (line ~244) with `CachedAt: _timeProvider.GetUtcNow()`. Documented the static-removal in XML remarks (pure-aggregate logic otherwise unchanged).

2. **`src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs`**
   - Added `using Microsoft.Extensions.DependencyInjection.Extensions;` for `TryAdd*` methods.
   - Added two `TryAddSingleton` registrations (`TimeProvider`, `IGuidProvider`) at the top of `AddWorkspaceServices`, with inline rationale comment.
   - Updated XML summary `Registration count: 9` → `Registration count: 11` and ADR-010 sentence to note the IGuidProvider single-impl-test-seam exception.

### Files added (2)

3. **`src/server/api/Sprk.Bff.Api/Services/Workspace/IGuidProvider.cs`** (new) — interface + `DefaultGuidProvider` impl with XML docs citing ADR-010 allowed-seam pattern + Phase 4 PoC scope (D-04).

4. **`tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/PortfolioServiceTests.cs`** (new) — 5 tests:
   - `GetPortfolioSummaryAsync_StampsCachedAtFromTimeProvider_OnCacheMiss` (PoC time-seam in production path)
   - `GetHealthMetricsAsync_StampsTimestampFromTimeProvider_OnCacheMiss` (second time-seam call site)
   - `FakeGuidProvider_ReturnsSeededSequence_InOrder` (identity-seam happy path)
   - `FakeGuidProvider_ThrowsWhenExhausted_SoTestsFailLoudly` (identity-seam loud-failure guard)
   - `DefaultGuidProvider_ProducesUniqueGuids` (production identity-seam regression guard)

### Verification

```text
dotnet build src/server/api/Sprk.Bff.Api/                 → 0 errors / 2 NU1903 warnings (pre-existing Kiota CVE — see RB-T044 outside this task)
dotnet build tests/unit/Sprk.Bff.Api.Tests/               → 0 errors / 2 NU1903 warnings (same pre-existing)
dotnet test --filter "FullyQualifiedName~PortfolioServiceTests"     → Passed: 5 / Failed: 0 / Skipped: 0
dotnet test --filter "FullyQualifiedName~Services.Workspace"        → Passed: 201 / Failed: 0 / Skipped: 0
```

No regression in the 196 pre-existing Workspace tests (`EffortScoringServiceTests`, `PriorityScoringServiceTests`, `TodoGenerationServiceTests`).

---

## 4. ADR / governance compliance (Step 9.5)

| Rule | Status | Note |
|---|---|---|
| ADR-010 (DI minimalism) — concretes by default | PASS | `TimeProvider` is BCL concrete-abstract; `IGuidProvider` justified as allowed-seam single-impl+test-seam (XML-doc cite). |
| ADR-010 — feature-module DI | PASS | Both registrations live in `WorkspaceModule.cs`, not inline in `Program.cs`. |
| ADR-010 — `≤15 non-framework lines` (principle) | n/a | Phase 5 baseline notes 265 registrations is the inherited tech-debt baseline; this task adds 2 net registrations following the same module pattern. |
| ADR-001 (Minimal API + Workers) | n/a | No endpoint or worker changes. |
| ADR-013 (refined) — no CRUD→AI direct dep | PASS | No AI-internal types injected anywhere. |
| ADR-029 (BFF publish hygiene) | PASS | No new NuGet packages. `TimeProvider` is `System.Runtime`. |
| `bff-extensions.md §A` (placement) — state placement decision | PASS | Placement: in BFF, `Services/Workspace/`. Justification: tightly scoped to Workspace consumers (Phase 4 PoC), not a cross-cutting platform abstraction. r3 may revisit promotion to `Spaarke.Core` if other modules adopt. |
| `bff-extensions.md §B` (new packages) — none added | PASS | No new direct package refs; CVE surface unchanged. |
| `bff-extensions.md §F` (test update obligation) | PASS | PortfolioService didn't have a test class; one created in matching `tests/.../Services/Workspace/` folder covering the modified surface. |
| NFR-01 r2 (production code IN scope; tests minimal) | PASS | Production change is the PoC abstraction + 1 consumer migration. Test changes are a new file for the PoC consumer (per design.md §5.5 explicit allowance). |
| NFR-02 (no rewrite — `<50%` line replacement) | PASS | PortfolioService modification is constructor-additive + 2 call-site replacements + 1 modifier change. Well under threshold. |
| FR-13 acceptance — PoC working ≥1 Workspace test class | PASS | `PortfolioServiceTests` (5 tests, all green) demonstrates both seams. |
| FR-13 acceptance — pattern doc drafted | PASS | This document. Phase 5 task 080 will merge the canonical pattern into `docs/procedures/testing-and-code-quality.md`. |
| FR-13 acceptance — r3 migration plan referenced | PASS | §5 below. |

**Step 9.5 verdict**: PASS. No HIGH findings.

---

## 5. Recommended rollout strategy for r3 (post-r2 generalization)

This PoC is deliberately narrow per D-04. r3 (or a follow-on quality investment per CLAUDE.md D-06) should generalize in this order:

### Wave 1 — Same-file non-determinisms (cheap, isolated)

Target the remaining direct `DateTime[Offset].UtcNow` / `Guid.NewGuid()` call sites in `Services/Workspace/*` that the PoC inventory already located. In priority order:

1. **`MatterPreFillService.AnalyzeFilesAsync`** (line 153) — `var requestId = Guid.NewGuid()` → `_guidProvider.NewGuid()`. Single site; logging context; constructor signature is already moderately complex but adding `IGuidProvider` is additive. **Recommended first** because it exercises the second seam in production.
2. **`ProjectPreFillService`** (line 140) — same pattern as MatterPreFillService. Apply together.
3. **`BriefingService`** (2 sites at lines 137, 189) — `DateTimeOffset.UtcNow` for `GeneratedAt` + `Deadline.AddDays(14)`. Direct analogue of PortfolioService — adopt `TimeProvider` ctor param, same pattern.
4. **`WorkspaceAiService`** (2 sites at lines 410, 465) — both `GeneratedAt: DateTimeOffset.UtcNow`. Same as above.
5. **`TodoGenerationService`** (2 sites at lines 244, 849) — `DateTime.UtcNow.Date` + `DateTime.UtcNow` in `CalculateInitialDelay`. **Higher risk** because it's a `BackgroundService` with `PeriodicTimer` — the existing 31 KB test class needs careful augmentation, not replacement.

After Wave 1, every Workspace file uses the seams. ADR-010 audit count: 0 direct `*UtcNow` / `Guid.NewGuid()` in `Services/Workspace/*`.

### Wave 2 — Promote helpers to shared test-assembly

Once 2+ test classes inline `FixedTimeProvider` + `FakeGuidProvider`, promote them to a shared `tests/unit/Sprk.Bff.Api.Tests/TestUtilities/Determinism/` namespace:

- `tests/unit/Sprk.Bff.Api.Tests/TestUtilities/Determinism/FixedTimeProvider.cs`
- `tests/unit/Sprk.Bff.Api.Tests/TestUtilities/Determinism/FakeGuidProvider.cs`

Evaluate `Microsoft.Extensions.TimeProvider.Testing` NuGet at this point — its `FakeTimeProvider.Advance(...)` is more powerful than the inline subclass, but adds a package reference (justify per `bff-extensions.md §B`).

### Wave 3 — Cross-module generalization (evaluate, do not force)

Search the broader `src/server/` for direct `DateTime[Offset].UtcNow` / `Guid.NewGuid()` in production code (excluding `Program.cs`, `Migrations`, `appsettings`, and `Logging`). Common candidates likely in `Services/Ai/`, `Services/Insights/`, `Services/Jobs/Handlers/`. Apply seams **only where tests need the determinism** — do not retrofit en masse. ADR-010 minimalism still binds.

If `IGuidProvider` migrates beyond Workspace, **promote it to `Spaarke.Core`** (no cross-feature DI seam should live in a single feature module). Re-register from `Spaarke.Core`'s module extension; `WorkspaceModule` keeps the `TryAdd` for backward compatibility.

### Out-of-scope until r3 design re-evaluates

- **`HostedService` / `BackgroundService` deterministic scheduling.** `TodoGenerationService.CalculateInitialDelay` uses `DateTime.UtcNow` for `PeriodicTimer` start computation. Migrating that to `TimeProvider` is correct, but the existing tests don't exercise the scheduler; adding scheduler tests is a larger investment.
- **End-to-end integration tests using `IClassFixture<WebApplicationFactory>`.** The PoC stays at unit-test scope. Integration tests would need a custom `TimeProvider` registered via `IConfigureOptions` or `WebApplicationFactory` overrides — a separate pattern.
- **`TimeProvider.System.GetUtcNow()` vs `DateTimeOffset.Now`.** Several Workspace files use `.Now` (local-time) in log messages — out of scope; local-time is rarely test-asserted.

---

## 6. Cross-references

- **Project**: `projects/sdap.bff.api-test-suite-repair-r2/`
- **Task POML**: `projects/sdap.bff.api-test-suite-repair-r2/tasks/042-testclock-poc-workspace.poml`
- **Design intent**: `projects/sdap.bff.api-test-suite-repair-r2/design.md §5.5 (Phase 4 Track C)`
- **Spec FR**: `projects/sdap.bff.api-test-suite-repair-r2/spec.md FR-13`
- **Pattern source (existing test precedent)**: `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionSyncTests.cs` (`FixedTimeProvider` subclass shape reused here)
- **Phase 5 merge target (for the pattern doc)**: task `080` will merge a distilled excerpt of §2 above into `docs/procedures/testing-and-code-quality.md`.
- **CLAUDE.md governance loaded**: `.claude/constraints/bff-extensions.md` §A/§B/§F + ADR-010 + ADR-029 + testing.md
- **NFR commit-chain note**: per project convention this task does NOT commit; commit (and the NFR-04 ledger-citation requirement) is the main session's responsibility when it bundles the Phase 4 P4-W1 wave.

---

*Filed by sub-agent executing task 042 on 2026-06-01 under FULL rigor. Production code change is in scope per D-01 (NFR-01 inversion). No `.claude/` writes. No commit.*
