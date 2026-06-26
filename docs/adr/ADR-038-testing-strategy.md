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
