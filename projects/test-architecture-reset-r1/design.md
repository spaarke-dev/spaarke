# Test Architecture Reset — Design Document

> **Project ID**: `test-architecture-reset-r1`
> **Status**: Design (pre-spec, pre-implementation)
> **Author**: Initiated 2026-06-23 after a multi-hour CI whack-a-mole session
> **Trigger event**: PR #417 merged → master CI red on flake → hotfix #433 → red on different flake → hotfix #434 → red on yet another flake. Same pattern as the 400+ dependency-fails refactor week (Q1 2026).
> **Forcing function**: SDAP CI `Build & Test (Release)` matrix entry **temporarily disabled** in `.github/workflows/sdap-ci.yml`. This project's primary deliverable restores it on a sound footing.

---

## 1. The problem in one sentence

We have ~8,000 tests, an established `[Trait("status", "repaired")]` taxonomy for fragile tests, 11+ progressively-skipped timing tests, and CI that has been red on every master commit since R3 (#415) merged — but local test runs pass consistently. The tests are noise generators, not signal sources.

## 2. Symptoms observed

| Symptom | Evidence | Frequency |
|---|---|---|
| Production refactor breaks tests | Today's R2.3: 28 tests skipped after IGenericEntityService refactor | Every refactor |
| CI fails on timing assertions but passes locally | `Performance_BatchQuery_SingleRoundTrip`, `RunContext_CarriesFreshCorrelationIdPerRun_NFR08`, `StorageRetryPolicyTests.ExecuteAsync_CancellationDuringRetry_StopsRetrying`, `AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` — all 5/5 pass local | Every CI run |
| Whack-a-mole skipping | Commits `6164472a3` ("bulk-remove 7 timing-budget CI assertions"), `8128d32cc`, `725e1af97` ("skip 1 more cancellation-timing flake — 11 total"), `3a1ac24f9` ("skip 2 more — 10 total now"), `d5bf08ee8` ("relax 2 pre-existing CI-jitter flakes") | Multi-week cycle |
| 400+ dependency fails refactor week | Q1 2026 (referenced by user) | One major event so far |
| Tests test wiring, not behavior | `NodeExecutorRegistry_RegistersAllDeliveryExecutors`, constructor-arg tests, `Mock<HttpMessageHandler>`-based ExecutorTests that broke en-masse when production swapped one abstraction for another | Pervasive |
| Real bugs not caught | Today's 9-bug Daily Briefing cascade: 0 caught by any test | Every project |

## 3. Root cause hypothesis

**The current test architecture optimizes for the wrong metric: coverage percentage.**

Cascading consequences:
- To hit 80% coverage on a BFF that mostly delegates to external services (Graph, Dataverse, AI), you write extensive **wiring tests** (DI registration, constructor null-checks, factory invocations). These have low signal-to-noise.
- Mock-heavy tests are easy to write but tightly couple to implementation. A production refactor that doesn't change observable behavior still breaks 20+ tests because they were mocking the inner HttpClient call, not asserting the outer contract.
- Timing assertions written on a developer laptop ("must complete in <500ms") were never tuned for shared CI runner characteristics, so they flake.
- No incentive for **integration tests against real systems** — those are slow, hard to set up, and don't move the coverage % needle as efficiently as 50 unit tests around a single class.

The team has been **patching individual tests** for months — but the underlying incentive (coverage %, mock-heavy norms, ban-on-real-Dataverse-in-tests) is intact, so the same pattern resurfaces.

## 4. Out-of-scope (so we don't sprawl)

- Rewriting the production code being tested (separate concern)
- Migrating to a different test framework (not the issue — xUnit + Moq + FluentAssertions are fine)
- Changing CI runner type / cost optimization (separate concern, though related)
- React/PCF test architecture (different test framework, different failure modes — file a sibling project if needed)

## 5. Scope of this project

In scope:
- All .NET test projects under `tests/`:
  - `tests/unit/Sprk.Bff.Api.Tests/` (~7,900 tests)
  - `tests/unit/Spaarke.Plugins.Tests/`
  - `tests/unit/Spaarke.Scheduling.Tests/`
  - `tests/integration/Sprk.Bff.Api.IntegrationTests/`
  - `tests/integration/Spe.Integration.Tests/`
- `tests/CLAUDE.md` (test policy — currently mandates 80%/70%/90% coverage targets)
- `.github/workflows/sdap-ci.yml` (Build & Test job — restore Release matrix after audit)
- `docs/standards/` — add a new `TEST-ARCHITECTURE.md` standard

Explicitly out of scope:
- PCF / Code Page Jest tests (different framework, different problems)
- Cypress / E2E browser tests (none exist yet)
- Load / perf tests (separate non-functional concern)

## 6. Approach

A 3-phase plan, with the **forcing function** preventing slide-back:

### Phase 1 — Audit + categorize (1 day)
Categorize every test in scope into one of:
- **KEEP** — tests real behavior, doesn't break on refactor, no flake history. Examples likely include: integration tests against real systems, security/auth tests, tests tagged as regressions for specific past bugs, compliance-driven tests.
- **DELETE with prejudice** — DI registration tests, constructor null-checks, property getter/setter tests, mock-of-mock tests (e.g., `Mock<HttpMessageHandler>` setups), tests skipped/repaired/quarantined ≥2x, any test that was in `[Trait("status", "repaired")]` more than once.
- **QUARANTINE** — uncertain. Move to a `tests/quarantine/` namespace and exclude from CI. Reassess in 1 month; anything not naturally re-needed gets deleted.

Deliverable: `notes/test-inventory-categorized.csv` (path / category / rationale).

### Phase 2 — Mass deletion + workflow change (1 day)
- One bundled PR removing the DELETE category
- `tests/CLAUDE.md` updated:
  - Drop the 80%/70%/90% coverage targets entirely
  - Add: "every bug gets a regression test" rule
  - Add: "every new endpoint gets at least 1 integration test against a real (or containerised) Dataverse" rule
  - Ban: DI registration tests, constructor-null tests, mocks of `HttpMessageHandler` or `IServiceClient`
- New `docs/standards/TEST-ARCHITECTURE.md`:
  - Test pyramid (lots of integration, few unit, no UI tests yet)
  - Mocking guidelines (mock at module boundaries, not at HTTP handler level)
  - Timing-assertion ban (use `TimeProvider` abstraction, not `Stopwatch`)
- Restore `Release` to the `Build & Test` matrix in `sdap-ci.yml`
- Re-enable any tests that were in the `[Skip]` doghouse but should still run after the bigger cleanup

### Phase 3 — Bug-driven rebuild (ongoing, ≥6 months)
- Every bug fix MUST include a regression test (code review enforces)
- Every new endpoint MUST include at least 1 integration test
- Coverage % is tracked for observation only, NEVER as a gate
- Quarantined tests reviewed monthly; deleted if not reactivated

## 7. Success criteria

| # | Criterion | Measurable as |
|---|---|---|
| SC-1 | CI master green rate ≥95% over 30 days post-Phase-2 | Count of master commits with CI green / total |
| SC-2 | Zero `[Trait("status", "repaired")]` markers added | Grep on new commits |
| SC-3 | No skipped tests for "CI flake" reason | grep for new `[Skip = "...flake..."]` |
| SC-4 | Every endpoint has ≥1 integration test exercising real Dataverse | Audit of `tests/integration/` vs endpoint count |
| SC-5 | Test count reduced by ≥60% (from ~8,000 toward ~3,000) | `dotnet test --list-tests` |
| SC-6 | CI Build & Test (Release+Debug) full matrix restored and green | `.github/workflows/sdap-ci.yml` matrix entries |
| SC-7 | `tests/CLAUDE.md` no longer mentions coverage % targets | Diff |
| SC-8 | `docs/standards/TEST-ARCHITECTURE.md` exists and is referenced from `CLAUDE.md` | File exists; index points to it |
| SC-9 | Last 5 production bugs each have a corresponding regression test | Audit |

## 8. Risks + mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Real regression slips through in the Phase 1-2 window | Medium | Medium | Keep all integration tests + auth tests + security tests; Release matrix is disabled but Debug still runs |
| Phase 3 forcing function fails ("we'll add the regression test later") | High | High | Code review checklist + PR template change; rejection criteria explicit in `TEST-ARCHITECTURE.md` |
| Quarantine becomes "dead code limbo" | Medium | Low | Hard 1-month deletion deadline on the quarantine namespace |
| Team disagreement on which tests to KEEP | Medium | Low | The audit owner has tie-breaker authority; appeals tracked but resolved in 24h |
| Coverage targets re-introduced by future project | Medium | Medium | `TEST-ARCHITECTURE.md` explicitly bans them; CLAUDE.md pointer reinforces |

## 9. Effort estimate

| Phase | Effort | Calendar |
|---|---|---|
| Phase 1 (audit) | 1 day | 1 day |
| Phase 2 (mass delete + policy + workflow) | 1 day | 1-2 days |
| Phase 3 (forcing function + ongoing) | Ongoing, ~1 hour per bug/feature thereafter | 6 months for full bug-driven repopulation |

Total active project work: **2-3 days**. Ongoing cultural change: 6+ months.

## 10. Coordination + dependencies

- Coordinate with the team that owns `Spaarke.Scheduling.Tests` (R3 added this; many of the flakes are here)
- Inform any project currently relying on `[Trait("status", "repaired")]` tagging — that taxonomy goes away
- Doesn't block any production deployment

## 11. Decisions deferred to spec.md

- Exact deletion criteria (might tune after seeing Phase 1 inventory)
- Whether to introduce a Dataverse emulator vs. use a real test tenant for integration tests
- Whether to require `TimeProvider` for ALL time-dependent code or only the test paths that hit timing assertions
- How to handle the existing 26 skipped IGenericEntityService tests (rewrite vs delete vs already-deleted-via-prod-refactor)

---

*Next step: review and approve this design; then produce `spec.md` and `plan.md` per the project process.*
