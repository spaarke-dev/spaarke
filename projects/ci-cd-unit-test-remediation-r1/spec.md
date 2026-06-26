# CI/CD + Unit Test Remediation — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-25
> **Source**: `projects/ci-cd-unit-test-remediation-r1/design.md`
> **Operating model**: Solo developer + 100% Claude Code spec-driven execution + 5-6 simultaneous worktrees. All enforcement is Claude-automated via skill workflows, ADRs, and constraints. No human review gate exists in normal operation.

---

## Executive Summary

Combine the previously-separate `ci-cd-github-enhancement` and `test-architecture-reset-r1` efforts (now removed; superseded by this project) into one delivery: (A) tiered CI with a required-status router and `<3 min p95` blocking tier, (B) test reset that deletes ~60% wiring tests and rewrites the three places that mandate coverage-% culture (`tests/CLAUDE.md`, `.claude/constraints/testing.md`, ADR-022), and (C) hot-path coordination (merge queue, `projects/INDEX.md`, auto-`conflict-check`) sized for 5-6 parallel worktrees. Registry architecture redesign is OUT OF SCOPE.

Ships in **~2 weeks elapsed, no dev halt**, with one ~4-hour cutover window.

## Scope

### In Scope

**Stream A — CI tiering**
- New `ci-router.yml` (always runs, always reports the single required status check)
- New `ci-tier1-blocking.yml` (compile + arch MUST-NOT + changed-surface integration smoke + auth smoke)
- New `ci-tier2-advisory.yml` (format, lint, full unit tests, ADR check, markdown link validator, Last Reviewed stamp, plugin size — non-blocking PR comment)
- Tier 3 = **augment existing `nightly-health.yml`** with full integration + coverage observation + Trivy + dep audit (no new workflow file)
- Path-aware dispatch in router (BFF/SpaarkeAi/docs-only diffs trigger correct subset)
- Existing `sdap-ci.yml` parallels for 1 sprint, then retired
- New `scripts/validate-markdown-links.ps1`

**Stream B — Test reset**
- Phase 1 audit categorizes all .NET tests into KEEP / DELETE; produces transient inventory CSV as a Phase 1 work artifact (NOT canonical)
- **Path conventions become the canonical category truth** (no CSV at runtime): tests under `tests/integration/auth/**`, `tests/integration/regression/**`, `tests/integration/data-mutation/**`, `tests/integration/tenant/**`, `tests/integration/contract/**`, plus `tests/unit/domain/**` are the 6 KEEP categories — encoded in `.claude/constraints/testing.md` as MUST rules
- Sliced deletion in 3 PRs (one per test project: `Spaarke.Plugins.Tests`, `Spaarke.Scheduling.Tests`, `Sprk.Bff.Api.Tests`)
- Rewrite `tests/CLAUDE.md` (drop 80/70/90% targets, drop `<1s/test`, integration-first template, ban `Mock<HttpMessageHandler>` + DI-registration tests)
- Rewrite `.claude/constraints/testing.md` (drop coverage-% MUST rule + mock-all-I/O MUST rule + `<100ms/test`; add path conventions; ban mock-of-internals)
- New ADR supersedes ADR-022's coverage clauses
- New `docs/standards/TEST-ARCHITECTURE.md`
- Restore `Release` to `Build & Test` matrix after surviving suite green ≥7 days
- No quarantine namespace — binary KEEP/DELETE; doubt = KEEP

**Stream C — Hot-path coordination (compensating; registry redesign is OUT of scope)**
- GitHub merge queue on `master` (initial batch=1, no speculative; revisit after 2 weeks)
- New `projects/INDEX.md` listing every active worktree with hot-path declarations (BFF Y/N, SpaarkeAi Y/N) — populated in Phase 1, maintained atomically by `project-pipeline` and `task-execute` skills (no cron)
- `task-execute` Step 0.5 auto-invokes `conflict-check` when changed files match watchlist (`src/server/api/Sprk.Bff.Api/**`, `src/solutions/SpaarkeAi/**`)
- `task-execute` Step 9.5 runs `code-review` for ALL test-modifying PRs (override of current STANDARD-rigor skip; enforces path-convention deletion-safety)
- `conflict-check` SKILL.md gains hot-path watchlist + auto-trigger criteria
- `.claude/constraints/bff-extensions.md` gains binding **"Hot-Path Declaration"** section
- Root `CLAUDE.md` §8 (rigor table) + §10 (BFF Hygiene) + §17 (pointers) updates inline
- `project-pipeline` SKILL.md: Step 2 reads INDEX.md for overlap warnings; Step 3 requires hot-path declaration in design.md for BFF/SpaarkeAi-touching projects
- New `deploy-spaarke-ai.yml` mirroring `deploy-bff-api.yml` pattern (CD from master)
- Audit `deploy-bff-api.yml` confirms master-trigger (fix if needed)

### Out of Scope

- Registry architecture redesign for BFF DI and SpaarkeAi widget/route registries — candidate follow-up project
- Azure deployment workflows other than BFF + SpaarkeAi
- React/PCF Jest test architecture
- Coverage measurement elimination (stays observable, never gating)
- Team-process artifacts (surveys, retrospectives, named-owner ceremony) — N/A for solo-dev

### Affected Areas

- `.github/workflows/` — 3 new (router, tier1, tier2) + 1 new CD (spaarke-ai) + 1 augmented (nightly-health) + 1 audited (deploy-bff-api) + 1 retired (sdap-ci)
- `tests/CLAUDE.md`, `tests/unit/**`, `tests/integration/**` — rewrite + sliced deletion + path reorganization (Phase 1 reorganizes paths if needed for the 6 KEEP conventions)
- `.claude/constraints/testing.md`, `.claude/constraints/bff-extensions.md` — rewrite + new section
- `.claude/skills/{project-pipeline,task-execute,conflict-check}/SKILL.md` — updates
- `docs/standards/TEST-ARCHITECTURE.md` — new
- `docs/procedures/ci-cd-workflow.md`, `docs/procedures/testing-and-code-quality.md`, `docs/architecture/ci-cd-architecture.md` — routine updates
- `docs/adr/ADR-022-*.md` — superseded by new ADR
- `CLAUDE.md` (root) — §8, §10, §17 updates
- `projects/INDEX.md` — new
- `scripts/validate-markdown-links.ps1` — new

## Requirements

### Functional Requirements

**Stream A — CI tiering**

1. **FR-A01** — `ci-router.yml` runs on every PR and push to master; always reports a single required status check (`CI / Router`); dispatches Tier 1 + Tier 2 jobs conditionally based on diff classification. Acceptance: required check never stuck pending.
2. **FR-A02** — Tier 1 contents (blocking, p95 ≤ 3 min): compile (Debug only, no `-warnaserror`); critical NetArchTest MUST-NOT subset; changed-surface integration smoke; auth smoke when diff touches `Infrastructure/Auth/` or claims handling. NOT in Tier 1: format, lint, markdown validator, dependency audit, Trivy, full unit/integration tests.
3. **FR-A03** — Tier 2 (advisory, non-blocking, p95 ≤ 8 min): format + lint + full unit tests + ADR compliance + markdown link validator + Last Reviewed stamp + plugin size. Posts deduplicated PR comment. Not a required status check.
4. **FR-A04** — Tier 3 = additive contributions to existing `nightly-health.yml` (full integration, coverage observation, Trivy, dep audit). No new workflow file.
5. **FR-A05** — Router path-aware dispatch: BFF-only diff skips SpaarkeAi tier jobs and vice versa; docs-only diff skips all heavier tier jobs. Decision lives in router, not `paths:` filters (preserves required-check semantics). **No commit-marker skip mechanism** — path detection is the only relief.
6. **FR-A06** — Branch protection on master requires only `CI / Router`; pre-cutover state exported to `notes/branch-protection-pre-cutover.json`. `sdap-ci.yml` parallels for 1 sprint then retires.

**Stream B — Test reset**

7. **FR-B01** — `.claude/constraints/testing.md` is rewritten end-to-end: removes MUST 80% line coverage; removes MUST mock-all-I/O; removes `<100ms/test`; adds the 6 path-based KEEP categories as MUST rules (tests under `tests/integration/auth/**`, `tests/integration/regression/**`, `tests/integration/data-mutation/**`, `tests/integration/tenant/**`, `tests/integration/contract/**`, `tests/unit/domain/**` are KEEP-protected — deletion requires same-PR replacement); bans `Mock<HttpMessageHandler>`, `Mock<IServiceClient>`, DI-registration tests, constructor null-checks.
8. **FR-B02** — `tests/CLAUDE.md` is rewritten end-to-end: drops 80/70/90% targets; drops `<1s/test`; replaces mock-first AAA template with integration-first; adds "every bug = regression test" + "every new endpoint = ≥1 integration test"; bans match FR-B01.
9. **FR-B03** — New ADR (next available number) supersedes ADR-022's coverage clauses with the evidence section (S-5, S-6 from design.md). ADR INDEX shows ADR-022 as Superseded.
10. **FR-B04** — New `docs/standards/TEST-ARCHITECTURE.md`: test pyramid (integration-heavy), 6-category portfolio shape, `TimeProvider` instead of `Stopwatch` for time-dependent tests, mock-boundary rules.
11. **FR-B05** — Phase 1 audit produces transient `notes/test-inventory.csv` as a work artifact. Phase 1 also **reorganizes existing tests into the 6 KEEP path conventions** (move-and-rename, no semantic changes) so the canonical category check is path-based at runtime.
12. **FR-B06** — Sliced DELETE in 3 PRs by test project, sequenced via INDEX.md to avoid active-worktree conflicts. Code-review at Step 9.5 enforces deletion-safety by path check (any deletion under the 6 KEEP-protected paths requires a same-PR replacement).
13. **FR-B07** — Restore `Release` to `Build & Test` matrix only after surviving suite is green ≥7 consecutive days. Root `CLAUDE.md` §8 rigor table updated: test PRs = FULL rigor (no STANDARD skip on Step 9.5) for the duration of this project and ongoing.

**Stream C — Hot-path coordination**

14. **FR-C01** — GitHub merge queue enabled on `master` (initial batch=1, no speculative, queue timeout 30 min). Revisit after 2 weeks based on observed 5-6-worktree throughput.
15. **FR-C02** — `projects/INDEX.md`: every active worktree project listed with name, branch, worktree path, hot-path declarations (BFF Y/N, SpaarkeAi Y/N), last-touched date. Populated in Phase 1 (one sweep, ~2h). Maintained atomically by `project-pipeline` (new project start) and `task-execute` (when hot path is touched). No cron.
16. **FR-C03** — `.claude/skills/task-execute/SKILL.md` Step 0.5 auto-invokes `conflict-check` when changed files match watchlist (`src/server/api/Sprk.Bff.Api/**` or `src/solutions/SpaarkeAi/**`). Step 9.5 runs `code-review` for ALL test-modifying PRs. `.claude/skills/conflict-check/SKILL.md` gains the watchlist + trigger criteria.
17. **FR-C04** — `.claude/constraints/bff-extensions.md` gains binding **"Hot-Path Declaration"** section. `.claude/skills/project-pipeline/SKILL.md` Step 2 reads INDEX.md and warns on overlap; Step 3 requires `<hot-path-declaration>` element in `design.md` for any BFF or SpaarkeAi-touching project. Root `CLAUDE.md` §10 (BFF Hygiene) + §17 (pointers) updated.
18. **FR-C05** — New `.github/workflows/deploy-spaarke-ai.yml` mirrors `deploy-bff-api.yml` pattern (CD from master, slot/staging, smoke). Audit confirms `deploy-bff-api.yml` triggers from master; fix if not.

### Non-Functional Requirements

- **NFR-01** — Tier 1 p95 ≤ 3 min over 30-day window
- **NFR-02** — Tier 2 p95 ≤ 8 min over 30-day window
- **NFR-03** — Tier 1 flake rate < 1%
- **NFR-04** — Master green rate ≥ 95% over 30 days post-cutover
- **NFR-05** — Solo dev sustains 5-6 parallel worktrees throughout; merge queue does not stall under peak parallel-merge load; no dev halt during the 2-week project (one ~4-hour cutover window acceptable)
- **NFR-06** — All enforcement is Claude-automated via skills, ADRs, constraints, and module CLAUDE.md. No human gate exists in normal operation

## Technical Constraints

### Applicable ADRs

- **ADR-022** — to be **superseded** (FR-B03). Primary policy reversal target.
- **ADR-028**, **ADR-030**, **ADR-032** — referenced for context (Tier 1 auth smoke alignment; path-aware dispatch interaction with feature flags)

### MUST Rules

- ✅ MUST keep existing Azure deployment workflows functionally untouched (`deploy-bff-api.yml` trigger audit only; `deploy-promote.yml`, `deploy-infrastructure.yml`, `deploy-office-addins.yml` zero changes)
- ✅ MUST keep `sdap-ci.yml` running in parallel through Phase 2; retire only post-cutover after 14 days of new-tier stability
- ✅ MUST enforce deletion-safety via path check at Step 9.5 (FR-B06); no CSV consultation at runtime
- ❌ MUST NOT restore `Release` matrix before Phase 2 deletion has merged AND surviving suite green ≥7 days
- ❌ MUST NOT add commit-marker skip mechanism to Tier 1 (FR-A05)
- ❌ MUST NOT introduce coverage-% targets to any new directive file (binding for ≥6 months)
- ❌ MUST NOT redesign BFF DI registry or SpaarkeAi widget/route registries in this project — OUT of scope

### Existing Patterns

- `.github/workflows/nightly-health.yml` — Tier 3 augmentation target (FR-A04)
- `.github/workflows/deploy-bff-api.yml` — pattern for `deploy-spaarke-ai.yml` (FR-C05)
- Existing PR-comment ADR-deduplication in `sdap-ci.yml` — reused for Tier 2 (FR-A03)

## Success Criteria

| # | Criterion | Verify by |
|---|---|---|
| SC-01 | Tier 1 p95 < 3 min | GitHub Actions analytics, 30-day rolling |
| SC-02 | Tier 2 p95 < 8 min | GitHub Actions analytics |
| SC-03 | Tier 1 flake rate < 1% | retry-pass / total ratio |
| SC-04 | PR-to-merge time decreases ≥ 30% | gh API pre/post |
| SC-05 | Master green rate ≥ 95% over 30 days post-cutover | gh API on master |
| SC-06 | All 6 KEEP path conventions populated; DELETE category fully removed; Release+Debug full matrix restored and green | `dotnet test --list-tests` + workflow status |
| SC-07 | `tests/CLAUDE.md` + `.claude/constraints/testing.md` no longer mandate coverage %; new ADR supersedes ADR-022 | diffs + ADR INDEX |
| SC-08 | GitHub merge queue enabled; INDEX.md lists all 5-6 active worktrees with hot-path declarations; `conflict-check` auto-invokes on hot-path tasks | repo settings + file + task-execute log |
| SC-09 | `deploy-spaarke-ai.yml` exists with ≥1 successful CD-from-master deploy; `deploy-bff-api.yml` confirmed master-triggered | workflow runs |
| SC-10 | Hot-path collision incidents (master rebase conflicts on `Program.cs` / SpaarkeAi registries) drop ≥ 50% over 30 days vs prior 30 days | git log analysis |

## Rollback

Settings-only; <15 min; solo dev's judgment is the trigger.

1. Flip branch protection back to pre-cutover state (saved to `notes/branch-protection-pre-cutover.json`)
2. Disable merge queue in repo settings
3. Re-enable `sdap-ci.yml` as the required check (still running in parallel)

Shadow workflows stay running for diagnosis. After rollback, focused fix in the failing stream; re-attempt cutover ~1 week later. Document cause in `notes/cutover-attempts.md`.

## Owner Clarifications

| Topic | Answer | Implementation impact |
|---|---|---|
| Decision authority | Solo developer; no team formality | All "named owner" language removed; cutover go/no-go is the dev's judgment |
| SpaarkeAi CD scope | Include both BFF + SpaarkeAi CD | FR-C05 builds new `deploy-spaarke-ai.yml` |
| Hot-path watchlist | BFF + SpaarkeAi only | FR-C03 watchlist narrow; can expand if collision rate stays high |
| Category enforcement | Path conventions as MUST rule, not CSV at runtime | FR-B01 + FR-B05 + FR-B06 — Phase 1 reorganizes tests into 6 KEEP paths; review-time check is path-based |
| Cutover timing | Relative (Day 14 from Phase 1 start) | No calendar anchor in spec |
| Operating model | Solo + 100% Claude + 5-6 parallel worktrees | Drove leanness: dropped quarantine, dropped commit-marker, dropped CSV-at-runtime, dropped team SCs |

## Assumptions

- 5-6 active worktrees is the design point; INDEX.md initial sweep documents the exact list
- Phase 1 path reorganization (FR-B05) is mechanical (move-and-rename) and does not change test semantics — git-mv style, in one PR
- ADR-022 successor gets the next available ADR number at authoring time
- Merge queue + neutral-conclusion check semantics work as documented (verify in Phase 1 spike — see Unresolved)

## Unresolved Questions

- [ ] Router signal model — single composite required check vs neutral conclusion for skipped tiers (Phase 1 spike against GitHub merge-queue + branch-protection docs). Blocks: FR-A01 final implementation.
- [ ] ADR-022 successor number — TBD at authoring. Non-blocking.
- [ ] Whether `tests/unit/domain/**` actually exists today or needs to be created during Phase 1 path reorganization. Resolve in Phase 1 first hour.

---

*AI-optimized specification. Source: `design.md`. Operating model: solo developer + 100% Claude Code spec-driven + 5-6 parallel worktrees.*
