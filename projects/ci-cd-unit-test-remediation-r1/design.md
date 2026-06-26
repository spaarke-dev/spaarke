# CI/CD + Unit Test Remediation — Design Document

> **Project ID**: `ci-cd-unit-test-remediation-r1`
> **Status**: Design (pre-spec, pre-implementation)
> **Created**: 2026-06-25
> **Supersedes**: `projects/ci-cd-github-enhancement/` + `projects/test-architecture-reset-r1/` (both merged into this project; legacy folders removed 2026-06-25, preserved in git history)
> **Forcing function**: SDAP CI `Build & Test (Release)` matrix entry temporarily disabled in `.github/workflows/sdap-ci.yml` (line 86); team trust in CI is at a multi-month low; >7,900 unit tests with chronic flakes generate noise rather than signal; parallel project work on hot paths (BFF API, spaarke.ai code page) is producing avoidable merge + deploy friction.

---

## 1. The problem in one sentence

CI/CD is slow (15–20 min p95) and untrustworthy, the underlying ~7,900-test unit suite optimizes for the wrong metric (coverage %), and our hottest deploy surfaces (BFF API, spaarke.ai code page) get hammered by parallel projects whose merges and deploys collide — so the team ignores CI, ships slower, and treats the protections as an impediment instead of a safety net.

## 2. Three intertwined causes (one project)

The team initially scoped two parallel projects:
- `ci-cd-github-enhancement` — tiered CI model, escape hatches
- `test-architecture-reset-r1` — delete wiring tests, restore Release matrix, change policy

Independent assessment (2026-06-25) concluded they cannot be delivered in isolation:
- CI Tier 1/2 budgets (<3 min / <8 min p95) are **mathematically unachievable** with the current 7,900-test flaky suite.
- Test deletion without CI tiering still leaves whole-repo build/test running on every PR — PR-to-merge time unchanged.
- Both projects ignore a third, equally painful symptom: **multiple in-flight projects modifying the same hot files** (`Program.cs`, `Sprk.Bff.Api.csproj`, spaarke.ai widget/route registries, `package.json`) producing deploy churn even after CI passes.

This project consolidates the work into **three streams** delivered through **three synchronized phases** over ~3 weeks elapsed, with **no dev halt** (contrast with the Q1 2026 dependency-cascade week where everyone stopped for 5 days).

| Stream | What it addresses | Source |
|---|---|---|
| **A — CI tiering** | Slow, all-or-nothing blocking pipeline | `ci-cd-github-enhancement` |
| **B — Test reset** | 7,900 tests, flaky, coverage-driven, mock-heavy | `test-architecture-reset-r1` |
| **C — Hot-path coordination** | BFF + spaarke.ai parallel-project deploy friction | NEW (raised 2026-06-25) |

## 3. Symptoms observed (consolidated evidence)

| # | Symptom | Source / evidence |
|---|---|---|
| S-1 | PR-to-merge time 15–20 min p95 | sdap-ci.yml runs Debug+Release matrix, full unit tests, format, lint, plugin-size, dependency audit, Trivy, NetArchTest — all blocking |
| S-2 | Master CI red on flake → hotfix → red on different flake | PR #417, hotfix #433, hotfix #434 (June 2026) |
| S-3 | Whack-a-mole skipping | Commits `6164472a3`, `8128d32cc`, `725e1af97`, `3a1ac24f9`, `d5bf08ee8` over multi-week cycle |
| S-4 | 400+ dependency-fail refactor week | Q1 2026 (5-day full halt) |
| S-5 | Tests test wiring, not behavior | DI registration tests, constructor null-checks, `Mock<HttpMessageHandler>` setups break en-masse on production refactors |
| S-6 | Real bugs not caught | Today's 9-bug Daily Briefing cascade caught 0 by any unit test |
| S-7 | Parallel projects collide on BFF | Multiple worktrees touching `Program.cs`, `Sprk.Bff.Api.csproj`, `Services/Ai/*` registries → rebase pain, conflicting DI |
| S-8 | Parallel projects collide on spaarke.ai code page | Widget registries, route maps, `package.json` adds → merge conflicts and deploy reordering |
| S-9 | Three places reinforce coverage% culture | `tests/CLAUDE.md` (80/70/90% targets, <1s/test rule); `.claude/constraints/testing.md` (80% line coverage MUST rule, mock-all-I/O MUST rule, <100ms/test); ADR-022 (cited as the source) |

## 4. Root cause hypothesis (consolidated)

Three reinforcing mechanisms:

1. **CI architecture**: One monolithic blocking workflow conflates "build broke" with "format drifted." All-or-nothing → all-fails-equally → team ignores it.
2. **Test culture**: Coverage % is enshrined in `tests/CLAUDE.md` (lines 152–155), `.claude/constraints/testing.md` (line 51 MUST rule), and ADR-022. Every Claude session reads these directives and generates wiring tests to hit the targets. The 7,900 tests are an emergent consequence of the directive, not a design choice.
3. **Hot-path topology**: BFF API and spaarke.ai code page are single binaries / single bundles with central registries. The architecture forces concurrent projects through the same files. The project-management process has no early-warning system for "two active projects want to modify the same DI module."

The current `task-execute` rigor model marks tests as **STANDARD** (skips code-review at Step 9.5), so generated wiring tests slip through without challenge. The directive begets the tests; the rigor model lets them merge; the CI then has to grade 7,900 of them on every PR.

## 5. Scope

### In scope (all three streams)

**Stream A — CI tiering** (from `ci-cd-github-enhancement`)
- Always-running `ci-router.yml` that decides which downstream jobs apply and **always reports a required status check** — avoids GitHub's documented "skipped workflow → required check pending forever" footgun. Tier 1/2/3 jobs dispatch conditionally from the router.
- New `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `ci-tier3-postmerge.yml`
- **Tier 1 contents (narrow, blocking, <3 min p95)**: compile (Debug only, no `-warnaserror`); critical NetArchTest MUST-NOT subset; changed-surface integration smoke (~60-90s budget); security/auth smoke when the change touches `Infrastructure/Auth/` or claims handling. **NOT in Tier 1**: format, lint, markdown link validator, dependency audit, Trivy, full unit tests, full integration tests, Last Reviewed stamp check — all of these live in Tier 2 (advisory) or Tier 3 (post-merge).
- **Tier 1 has NO commit-based skip mechanism.** Relief for docs-only PRs is path-based: the router detects docs-only diffs and reports Tier 1 success without running the heavier jobs.
- Escape hatch: `[skip-nonblocking-ci]` commit marker (renamed from the original `[skip-ci]` to make scope explicit) skips Tier 2 + Tier 3 only; `[docs-only]` label provides equivalent effect via the router. Monthly audit of marker usage.
- Path-aware downstream dispatch: BFF-only diffs don't trigger code-page jobs, and vice versa (decision lives in the router, not in `paths:` filters on the workflows themselves, so required-check semantics stay clean)
- Branch protection requires only the router's status check
- Existing `sdap-ci.yml` kept in parallel for 1 sprint of comparison, then retired

**Stream B — Test reset** (from `test-architecture-reset-r1`)
- Audit + categorize all .NET tests (`tests/unit/Sprk.Bff.Api.Tests/`, `tests/unit/Spaarke.Plugins.Tests/`, `tests/unit/Spaarke.Scheduling.Tests/`, `tests/integration/*`) into KEEP / DELETE / QUARANTINE. KEEP expands into six explicit sub-categories: **(1)** regression test for known production bug, **(2)** security/auth path, **(3)** data mutation path, **(4)** tenant isolation path, **(5)** endpoint/API contract, **(6)** domain logic. These are also the **portfolio shape** for future test work (see SC-10 in §7).
- **Binding deletion-safety rule**: no deletion PR may reduce coverage of KEEP sub-categories 1–4 (regression, security/auth, data mutation, tenant isolation) unless a replacement test lands in the same PR. Code-review at Step 9.5 enforces; reviewer flags any test in these categories slated for removal.
- Sliced deletion (NOT one mega-PR) — one PR per test project, sequenced to avoid merge conflicts with in-flight feature PRs
- Restore `Release` to the `Build & Test` matrix once the surviving suite is stable
- Re-enable post-`[Skip]` tests that should run after cleanup
- **Portfolio composition counts are NOT pre-declared.** The Phase 1 inventory recommends targets; spec.md formalizes them; SC-10 (§7) is about shape, not count. This avoids reintroducing a vanity metric.

**Stream C — Hot-path coordination** (NEW)

> **Framing: these are compensating controls, not the end state.** The structural cause of hot-path collision is centralized registries (`Program.cs` DI block, widget/route registries in spaarke.ai) that force concurrent edits to the same file. The proper long-term fix is architectural (module self-registration, folder discovery, generated registries, feature manifests, convention-based registration). That work is **explicitly OUT of scope here** (see §5 Out of Scope) and parked as a candidate follow-up project. Doing it inside this project would invert the scope and reintroduce the "huge project + risk" that combining ci-cd-github-enhancement + test-architecture-reset-r1 was designed to avoid.

- Path-aware downstream dispatch (decision lives in the router from Stream A, not in `paths:` filters) so unrelated projects don't trigger each other's tests
- **GitHub merge queue** enabled for `master` (serializes merges, runs Tier 1 on the merged-state). **Initial config: batch size = 1, no speculative batching**, for first 2 weeks. Revisit based on Tier 1 flake data and merge throughput; speculative batching only after Tier 1 is measured stable.
- **Active-project registry** in `projects/INDEX.md` showing which projects currently touch which hot paths (BFF, spaarke.ai, code pages)
- `conflict-check` skill auto-invoked at task start when modifying `src/server/api/Sprk.Bff.Api/` or `src/solutions/SpaarkeAi/` (or whichever path matches the spaarke.ai code page)
- Strengthen `.claude/constraints/bff-extensions.md` with a **"declare touched hot paths in design.md"** rule (binding, like the existing Placement Justification section)
- Continuous deployment from `master` for BFF + spaarke.ai instead of per-project feature-branch deploys

### Project primitives updates (binding deliverables of this project)

These are not nice-to-haves — they are **the load-bearing artifacts** that prevent backslide:

| Artifact | Change |
|---|---|
| `tests/CLAUDE.md` | **Full rewrite.** Remove 80/70/90% coverage targets. Remove "<1s per test" rule. Replace mock-first AAA template with an integration-first template. Add "every bug = regression test" + "every new endpoint = ≥1 integration test." Ban `Mock<HttpMessageHandler>`, `Mock<IServiceClient>`, DI-registration tests, constructor null-checks. |
| `.claude/constraints/testing.md` | **Rewrite the MUST/MUST NOT rules.** Drop "minimum 80% line coverage" MUST rule. Drop "mock all I/O" MUST rule (replace with "mock at module boundaries, not at HTTP-handler level"). Drop "<100ms per test" MUST rule. Add bans matching `tests/CLAUDE.md`. |
| `docs/adr/ADR-022-*.md` | **Supersede with a new ADR** documenting the policy reversal (coverage-as-observation, not gate). Include the evidence section (S-5, S-6) so future readers see *why*. |
| `docs/standards/TEST-ARCHITECTURE.md` | **New file.** Test pyramid (heavy integration, modest unit, no UI yet). Timing-assertion ban (`TimeProvider`, not `Stopwatch`). Mock boundary rules. Forcing-function enforcement points. |
| Root `CLAUDE.md` §8 (rigor levels) | **Update**: Tests no longer auto-STANDARD. **Deleting** tests = FULL rigor (must run code-review at Step 9.5). **Adding integration tests** = STANDARD. **Adding unit tests** = also FULL while the cultural reset is fresh (≥6 months). |
| Root `CLAUDE.md` §10 (BFF Hygiene) | **Add Stream C clauses**: declare touched hot paths in design.md; run `conflict-check` before BFF-touching tasks; merge-queue cadence note. |
| Root `CLAUDE.md` §17 (Pointers) | Add `docs/standards/TEST-ARCHITECTURE.md` and the new ADR. |
| `.claude/skills/project-pipeline/SKILL.md` | **Step 2 (resource discovery)** must surface "hot-path overlap with other active projects" warnings. **Step 3 (WBS)** must require a "Hot-Path Declaration" element when BFF or spaarke.ai is touched. |
| `.claude/skills/task-execute/SKILL.md` | **Step 0.5** auto-invokes `conflict-check` when changed-files include hot paths. **Step 9.5** code-review runs for ALL test PRs (override of current STANDARD rule, for the duration of the cultural reset). |
| `.claude/skills/conflict-check/SKILL.md` | Add hot-path watchlist: `Program.cs`, `*.csproj` (BFF), `Services/Ai/*Module.cs`, `src/solutions/SpaarkeAi/src/*Registry*`, `package.json` at hot paths. Auto-trigger criteria. |
| `docs/procedures/ci-cd-workflow.md` | Document the tiered model, escape hatches, merge queue behavior, path-based filtering. |
| `docs/procedures/testing-and-code-quality.md` | Reflect new policy. Cite the new ADR. |
| `docs/architecture/ci-cd-architecture.md` | Update to reflect Tier 1/2/3 + retired sdap-ci.yml. |
| `projects/INDEX.md` | **New** active-project registry with hot-path declarations. |

### Out of scope (so we don't sprawl)

- Azure deployment workflow YAML (`deploy-bff-api.yml`, `deploy-promote.yml`, `deploy-slot-swap.yml`, `deploy-infrastructure.yml`, `provision-customer.yml`) — they work; untouched
- Rewriting production code being tested (separate concern; bug-driven over 6 months)
- React/PCF Jest test architecture (different framework, different failure modes; file a sibling project if needed)
- Migrating off xUnit / Moq / FluentAssertions (not the issue)
- CI runner type / cost optimization (separate concern)
- **Registry architecture redesign** for BFF DI (`Program.cs`, `*Module.cs`) and spaarke.ai widget/route registries (module self-registration, folder discovery, generated registries, feature manifests, convention-based registration). Necessary long-term but a **candidate follow-up project** after this one ships — doing it here would invert the project shape (architectural redesign + CI + tests + culture = huge project, high risk, exactly what combining the two existing drafts was designed to avoid)
- Coverage **measurement** elimination — coverage is still **tracked for observation**, just never a gate. Trend data is useful.

## 6. Approach — 3 streams × 3 synchronized phases, no dev halt

The Q1 2026 5-day halt was *reactive* (a dependency upgrade had broken everything; no choice but stop). This project is *proactive* and can be sequenced as **additive almost the whole way through**. The one synchronized cutover (Phase 3, ~half a day) is small and scheduled.

### Phase 1 — Diagnose + escape valves (Week 1)

Parallel work; nothing blocks anything else.

**Stream A**:
- Catalog last 50 `sdap-ci.yml` failures (legitimate vs flaky vs false-positive)
- Measure current p50/p95 of sdap-ci.yml end-to-end
- **Ship escape hatches FIRST**: `[skip-ci]` and `[docs-only]` work immediately on the existing `sdap-ci.yml`. Dev gets relief week 1.

**Stream B**:
- Inventory all .NET tests into `notes/test-inventory-categorized.csv` (path / category / rationale)
- **Rewrite `tests/CLAUDE.md` and `.claude/constraints/testing.md`** — single-file edits, no impact on running code or CI, can land any time
- Draft `docs/standards/TEST-ARCHITECTURE.md`
- Draft superseding ADR for ADR-022

**Stream C**:
- Create `projects/INDEX.md` with hot-path declarations for every active project (one-pass audit; ~2 hours)
- Update `conflict-check` skill with hot-path watchlist
- Design path filters for Tier 1/2 workflows (`paths:` / `paths-ignore:`)

**Phase 1 deliverable PR set**: escape hatches live; `tests/CLAUDE.md` + constraints rewritten; inventory CSV in repo; INDEX.md live.

**Dev impact**: ZERO. All additive or read-only.

### Phase 2 — Build new structure in shadow (Week 2)

**Stream A**:
- Create `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `ci-tier3-postmerge.yml` running in **shadow mode** (parallel to `sdap-ci.yml`, not yet required)
- Markdown file-path validator script
- Last Reviewed stamp check
- Enable GitHub merge queue config (Stream C) — also in shadow

**Stream B**:
- **Sliced deletion** by test project, one PR at a time:
  - PR-B2.1: `tests/unit/Spaarke.Plugins.Tests/` DELETE category
  - PR-B2.2: `tests/unit/Spaarke.Scheduling.Tests/` DELETE category
  - PR-B2.3: `tests/unit/Sprk.Bff.Api.Tests/` DELETE category (largest; may be sub-sliced)
- Move QUARANTINE tests to `tests/quarantine/` namespace, excluded from CI
- Each PR rebases on master and coordinates with active project PRs touching the same test files

**Stream C**:
- Path-aware Tier 1/2 filter rules tested in shadow workflows
- `task-execute` SKILL.md Step 0.5 updated to invoke `conflict-check` on hot-path-touching tasks
- Root `CLAUDE.md` §8 rigor table updated (tests = FULL during reset window)
- Root `CLAUDE.md` §10 BFF Hygiene expanded with hot-path declaration rule
- `project-pipeline` SKILL.md updated to surface hot-path overlap during planning

**Phase 2 deliverable PR set**: new tier workflows running in shadow (data collected, never blocking); ~5,000 wiring tests removed across 3-4 sliced PRs; project-pipeline and task-execute updated.

**Dev impact**: Soft constraint — *new test additions* in active feature PRs should follow the new policy. Feature code untouched. Conflicts with sliced deletion PRs are managed by sequencing deletion away from active feature areas (the inventory CSV gives this visibility).

### Phase 3 — Cutover + monitor (Week 3)

**The one synchronized event** (~half-day):
- Restore `Release` to the `Build & Test` matrix in `sdap-ci.yml` (or, equivalently, in `ci-tier1-blocking.yml`)
- Flip branch protection: required checks → Tier 1 only
- Retire `sdap-ci.yml` (or trim to a redirect)
- Enable merge queue as the merge path on master
- Announce to team

**Rollback plan (24–48h trigger window)**:
- **Owner**: cutover decision + rollback authority sits with a single named project owner (designated in spec.md). Single decision-maker, no committee.
- **Rollback triggers** (any ONE within 24–48h post-cutover): Tier 1 flake rate >2% sustained over 24h; master green rate <90% over 24h; >3 reports of false-positive blocks within 24h; merge queue stalling (>4 PRs stuck >2h).
- **Rollback mechanics** (all settings flips, no code changes — the shadow workflows stay running for diagnosis): (1) flip branch protection required checks back to the pre-cutover set (documented before cutover); (2) disable merge queue in repo settings; (3) re-enable `sdap-ci.yml` as the required check (already running in parallel, so it's a UI flip not a deploy); (4) Tier 1/2/3 + router continue running in shadow.
- **Re-attempt**: after rollback, focused fix in the failing stream (A, B, or C); re-attempt cutover ~1 week later. Document cause + fix in `notes/cutover-attempts.md`.

**Monitoring (2 weeks post-cutover)**:
- Tier 1 flake rate < 1%
- Tier 1 p95 < 3 min
- Tier 2 p95 < 8 min
- Master green rate ≥ 95% (Stream B SC-1)
- Zero new `[Trait("status", "repaired")]` markers (Stream B SC-2)
- Hot-path collision incidents tracked in `projects/INDEX.md` retrospective
- Two-week team survey

**Phase 3 deliverable**: green master, fast CI, merge queue on, INDEX.md tracking overlaps, retrospective + survey report.

**Dev impact**: ~4-hour window of slowed merges on cutover day. Normal velocity otherwise.

## 7. Success criteria (consolidated)

| # | Criterion | Measurable as | Source stream |
|---|---|---|---|
| SC-1 | Tier 1 p95 < 3 min | GitHub Actions analytics, 30-day rolling | A |
| SC-2 | Tier 2 p95 < 8 min | GitHub Actions analytics | A |
| SC-3 | PR-to-merge time decreases by ≥30% | gh API, before/after comparison | A |
| SC-4 | Tier 1 flake rate < 1% | Retry-pass / total run ratio | A |
| SC-5 | Zero false positives in advisory tier on typical PRs (2-week observation) | PR comment audit | A |
| SC-6 | Master green rate ≥ 95% over 30 days post-cutover | gh API on master commits | B |
| SC-7 | Zero new `[Trait("status", "repaired")]` markers post-cutover | grep on new commits | B |
| SC-8 | No new `[Skip = "...flake..."]` markers post-cutover | grep | B |
| SC-9 | Every endpoint has ≥1 integration test against real Dataverse | endpoint audit | B |
| SC-10 | **Portfolio shape, not count** — DELETE category gone; quarantine namespace empties within 30 days; all six KEEP sub-categories represented (regression / security-auth / data-mutation / tenant-isolation / endpoint-contract / domain-logic). Count reduction (~60% working estimate) is observation, not gate. | `dotnet test --list-tests` + Phase 1 audit cross-ref | B |
| SC-11 | `Build & Test (Release+Debug)` full matrix restored and green | `.github/workflows/` | A+B |
| SC-12 | `tests/CLAUDE.md` + `.claude/constraints/testing.md` no longer mandate coverage % | diff | B |
| SC-13 | `docs/standards/TEST-ARCHITECTURE.md` exists + linked from root `CLAUDE.md` | file exists | B |
| SC-14 | Superseding ADR replaces ADR-022 coverage clauses | ADR INDEX shows superseded | B |
| SC-15 | Last 5 production bugs each have a regression test | audit | B |
| SC-16 | GitHub merge queue enabled on master | repo settings | C |
| SC-17 | `projects/INDEX.md` lists every active project with hot-path declarations | file inspection | C |
| SC-18 | `conflict-check` auto-invokes on hot-path tasks | task-execute log audit | C |
| SC-19 | Hot-path collision incident rate (master-side rebase conflicts on `Program.cs` / spaarke.ai registries) drops by ≥50% over 30 days vs prior 30 days | git log analysis | C |
| SC-20 | Team survey (2 weeks post-cutover): majority report CI signals are trustworthy AND parallel-project friction has reduced | survey | A+C |

## 8. Risks + mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | Real regression slips through during deletion window | M | M | Keep all integration, auth, security, and regression-for-past-bug tests; Debug matrix still runs even with Release temporarily off |
| R-2 | "Recreate on touch" forcing function fails ("we'll add the regression test later") | H | H | Root CLAUDE.md §8 forces FULL rigor on test PRs for ≥6 months; PR template adds "Does this fix a bug? Where's the regression test?" question; code-review at Step 9.5 checks |
| R-3 | Quarantine becomes dead-code limbo | M | L | Hard 1-month deletion deadline on quarantine; auto-cron PR to flag old quarantined files |
| R-4 | Coverage targets reintroduced by future project | M | M | New ADR explicitly bans gating on coverage; CLAUDE.md pointer reinforces; ADR-022 superseded so future Claude sees the policy reversal |
| R-5 | Tier 1 still flaky after migration | M | H | Tier 1 only contains build + arch tests + doc validators in shadow phase — measured before flipping branch protection; if flake > 1%, pause cutover and re-triage |
| R-6 | Escape hatches abused | M | L | Monthly audit (CI/CD doc); quarterly retrospective |
| R-7 | Sliced deletion PRs conflict with in-flight feature PRs | M | M | Inventory CSV is source of truth for "what's being deleted where"; coordinate slice sequence with `projects/INDEX.md` hot-path declarations |
| R-8 | Merge queue surprises team | M | L | Enable in shadow first (informational only); document behavior in `docs/procedures/ci-cd-workflow.md`; team announcement before cutover |
| R-9 | Hot-path declarations in `projects/INDEX.md` go stale | H | M | `project-pipeline` skill updates INDEX.md at project start; `task-execute` updates on hot-path changes; weekly cron audit |
| R-10 | Stream A (CI) lands without Stream B (tests) ready, or vice versa | L | H | This document binds them — they ship as one project. Phase 3 cutover gated on Stream B Phase 2 deletion landing and Tier 1 measured stable |
| R-11 | Team disagrees on KEEP vs DELETE categorization | M | L | Audit owner has tie-breaker authority; appeals tracked in `notes/categorization-appeals.md`, resolved in 24h |
| R-12 | spaarke.ai code page hot-path is harder to coordinate than BFF | M | M | Phase 1 spike: identify spaarke.ai-specific registry-pattern enforcement (folder-discovery instead of central-file edits, where feasible) — defer mechanism to spec.md |

## 9. Effort estimate

| Phase | Stream A | Stream B | Stream C | Calendar |
|---|---|---|---|---|
| Phase 1 (diagnose + valves) | 2 days | 3-5 days (audit) | 0.5 day (INDEX + conflict-check) | Week 1 |
| Phase 2 (shadow + slice) | 2 days | 3 days (3 deletion PRs) | 1 day | Week 2 |
| Phase 3 (cutover + monitor) | 0.5 day cutover | (passive monitoring) | (passive monitoring) | Week 3 + 2-week observation |
| Ongoing | escape-hatch audit (monthly) | bug-driven test rebuild (~6 months) | INDEX freshness (weekly cron) | continuous |

**Active project work**: ~3 weeks elapsed, normal dev throughput throughout (one ~4-hour cutover window).
**Cultural change**: ≥6 months for test suite to grow back from integration + regression-driven additions.

## 10. Coordination + dependencies

- **Q1 2026 dependency-week veterans** — pull their lessons; the 5-day halt was reactive, this is proactive, but the mitigations they used (sliced rebases, sequencing) are still applicable
- **Active project owners** — must declare hot paths in `projects/INDEX.md` during Phase 1; ongoing for new projects via `project-pipeline` skill
- **Spaarke.Scheduling.Tests owners** — R3 added this project; many of the flakes are here; coordinate KEEP/DELETE categorization
- **Anyone using `[Trait("status", "repaired")]`** — that taxonomy goes away; surface in retrospective
- **Doesn't block any production deployment** — every deploy workflow is untouched

## 11. Decisions deferred to spec.md

1. Exact categorization criteria after Phase 1 inventory reveals real distribution
2. Whether to use a Dataverse emulator vs real test tenant for integration tests
3. Whether `TimeProvider` is required for ALL time-dependent code or only test-adjacent paths
4. How to handle 26 currently-skipped `IGenericEntityService` tests (rewrite vs delete)
5. spaarke.ai-specific registry-pattern enforcement mechanism (folder-discovery vs central edit) — Stream C spike output
6. Whether the markdown file-path validator (Tier 1) blocks or warns on docs-only PRs that introduce new broken links
7. Merge queue specific config (batch size, timeout)
8. Whether existing PR comment ADR-deduplication pattern is reused for Tier 2 advisory comments
9. Final Tier 1 contents — initial narrow proposal in §5 Stream A; refined after Phase 1 diagnostic surfaces actual blocking-check ROI
10. Test portfolio composition counts/percentages — derived from Phase 1 inventory, never pre-declared
11. Specific commit-marker name for the skip mechanism — `[skip-nonblocking-ci]` is the working name; alternatives like `[skip-advisory]` evaluated in spec.md
12. Router workflow signal model — single composite required check vs neutral conclusion for skipped tiers (GitHub merge-queue behavior with neutral checks worth a quick spike before locking in)

## 12. Why "one combined project" instead of sequencing two

| Decision | Rationale |
|---|---|
| **Combine, don't sequence** | CI tier budgets (<3 min / <8 min p95) are mathematically unachievable on a 7,900-test flaky suite. Sequencing test reset first leaves whole-repo CI in place until cleanup completes (6+ months). Combining lets both ship together at Week 3 cutover. |
| **Three streams, not two** | Hot-path collision is a third symptom hidden under "slow CI." Even with fast CI + sound tests, BFF and spaarke.ai concurrent-project friction is structural. The infrastructure work (merge queue, path filters, INDEX.md, conflict-check auto-invoke) belongs in this project because the same CI/CD overhaul is touching the same files. |
| **Project primitives are deliverables, not side-effects** | `tests/CLAUDE.md` is read by Claude every session that touches tests. If it isn't rewritten *first*, every test deleted in Phase 2 will start to regrow through new sessions following the old template. Phase 1 rewriting it is the highest-leverage single artifact change. |
| **No dev halt** | The Q1 incident memory ("5 days, no development") is the team's deepest scar. This design's whole sequencing is built around additive shadow workflows + sliced deletion + a single ~4-hour cutover. Phase 1 is zero-impact. Phase 2 is feature-PR safe. Phase 3 is scheduled. |

## 13. References

- `projects/ci-cd-github-enhancement/spec.md` + `design.md` — Stream A source (removed 2026-06-25; preserved in git history at `b2fa78c81`-or-earlier)
- `projects/test-architecture-reset-r1/design.md` — Stream B source (removed 2026-06-25; preserved in git history)
- `tests/CLAUDE.md` — to be rewritten (Phase 1, Stream B)
- `.claude/constraints/testing.md` — to be rewritten (Phase 1, Stream B)
- `docs/adr/ADR-022-*.md` — to be superseded (Phase 1, Stream B)
- `.github/workflows/sdap-ci.yml` — to be tiered + retired (Phase 2–3, Stream A)
- `.claude/skills/conflict-check/SKILL.md` — to be enhanced with hot-path watchlist (Phase 1, Stream C)
- `.claude/skills/project-pipeline/SKILL.md` — to be enhanced with hot-path overlap warnings (Phase 2, Stream C)
- `.claude/skills/task-execute/SKILL.md` — to be enhanced with hot-path auto-conflict-check + FULL-rigor-on-tests (Phase 2, Streams B+C)
- `.claude/constraints/bff-extensions.md` — Section addition for hot-path declaration (Phase 2, Stream C)
- `docs/architecture/ci-cd-architecture.md` — to be updated (Phase 3)
- `docs/procedures/ci-cd-workflow.md` — to be updated (Phase 3)
- `docs/procedures/testing-and-code-quality.md` — to be updated (Phase 2)
- `docs/standards/TEST-ARCHITECTURE.md` — NEW (Phase 1)
- `projects/INDEX.md` — NEW (Phase 1, Stream C)

---

*Next step: Review and approve this design. Open questions in §11. Then produce `spec.md` and `plan.md` per the project process; spec.md will encode the binding success criteria as POML acceptance and the plan.md will produce sliced task POMLs for `task-execute` invocation.*
