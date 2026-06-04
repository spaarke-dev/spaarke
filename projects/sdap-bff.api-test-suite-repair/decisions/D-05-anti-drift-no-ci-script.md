# D-05: Anti-drift enforcement — code review checklist + PR template (NO CI script)

**Status**: Locked (2026-05-30 design phase, captured 2026-05-31)
**Source**: [`design.md`](../design.md) §5.5
**Binding on**: FR-22 (`.claude/constraints/bff-extensions.md` update), FR-23 (PR template), FR-24 (code review checklist), FR-25 (root `CLAUDE.md` §10 reference); Phase 4 tasks 080, 081, 082, 083

---

## Context

The 2026-05-30 audit established that every BFF-touching project shipped tests, but tests drifted over time as services evolved and no project owned correction. The open question was whether anti-drift should be CI-enforced (mechanical: a PR-checking script that fails PRs without test changes) or governance-enforced (procedural: code review + PR template + checklist). The owner direction was "do not want PR process overburdened" — a CI script would produce false positives on doc-only, config-only, and refactor PRs.

## Decision

**Option D (no CI script).** Per §5.5 (verbatim):

> Anti-drift is enforced by:
> 1. New section in [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) titled "Test update obligation" with explicit checklist
> 2. PR template (`.github/pull_request_template.md`) adds one question: "If this PR modifies `src/server/api/Sprk.Bff.Api/Services/`, has a corresponding test been added/updated?"
> 3. Code review checklist in [`docs/procedures/`](../../docs/procedures/) gets one new line: "verify test-update obligation per `.claude/constraints/bff-extensions.md`"

## Rationale

Per §5.5 "Why NOT a CI script": user direction is "do not want PR process overburdened." A CI script that fails PRs without test changes produces false positives on doc-only PRs, config-only PRs, and refactor PRs where test coverage is unchanged-but-still-valid. Manual review by humans (reinforced by template + checklist) catches the same signal without the noise.

Per §5.5 "Why this is robust enough": The 2026-05-30 audit showed every project shipped tests; the problem wasn't "tests weren't written" — it was "tests were written, then drifted as services evolved, and no project owned correction." That's a code-review-discipline problem, not a CI-enforcement problem. Solving it with CI gates would be over-engineering against the wrong failure mode.

## Rejected alternatives

- **Option C: CI script that fails PRs without test changes** — false positives on doc/config/refactor PRs; overburdens PR process per owner direction; misdiagnoses the failure mode (drift, not absence).
- **No anti-drift mechanism (let code review catch it)** — code review has demonstrably failed to catch drift across multiple projects; explicit checklist + template question are the lightweight reinforcement.
- **Mandatory test coverage gate (e.g., codecov threshold)** — coverage-numeric gates don't catch drift in still-covered-but-now-incorrect tests; addresses a different failure mode.

## Downstream Impact

- **FR-22** — Phase 4 task 080 adds "Test update obligation" section to `.claude/constraints/bff-extensions.md` (main-session-only per sub-agent write boundary)
- **FR-23** — Phase 4 task 081 updates `.github/pull_request_template.md` with the Services-modification question
- **FR-24** — Phase 4 task 082 adds the test-update-obligation line to code review checklist in `docs/procedures/`
- **FR-25** — Phase 4 task 083 updates root `CLAUDE.md` §10 to reference the new bff-extensions.md section (main-session-only per sub-agent write boundary)
- **No CI workflow change** — `.github/workflows/sdap-ci.yml` is NOT modified for anti-drift purposes (only for D-02 / FR-09 / FR-10 strict-gate purposes, which is a separate concern)

## Reassessment trigger

If post-project quarterly audit reveals new drift in the Services-without-test-change pattern despite the template + checklist, the procedural-only enforcement is insufficient and CI-mechanical enforcement should be re-examined — at that point with the data to size false-positive tolerance.
