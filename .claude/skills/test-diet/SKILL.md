---
description: Project-close test reconciliation - classifies tests added/modified during the project as scaffolding (delete) vs maintain (keep at KEEP path) per ADR-038 build-vs-maintain criteria
tags: [testing, cleanup, project-close, build-vs-maintain, scaffolding, maintain]
techStack: [dotnet, xunit, git]
appliesTo: ["test diet", "project close test review", "build vs maintain reconciliation", "scaffolding cleanup"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-26
---

# test-diet

> **Category**: Quality / Project-close hygiene
> **Last Reviewed**: 2026-06-26
> **Reviewed By**: `ci-cd-unit-test-remediation-r1` task CICD-081 (per spec FR-B09)
> **Exemplar rationale**: Diet reports are per-project ephemeral output — no canonical snapshot holds.

---

## Purpose

Reconcile **scaffolding-class** vs **maintain-class** tests at every project's close per ADR-038 §7. For each test file added or modified during the project, classify each test method against the 17-ban list (B1-B17), recommend DELETE for scaffolding, confirm KEEP path for maintain, flag AMBIGUOUS for human judgment. Emit `git rm` / `git mv` commands but DO NOT auto-execute — final delete decision is reviewer's.

**Binding from 2026-06-26 per spec FR-B09**: every project's `090-wrapup-*` task MUST invoke `/test-diet` before completing. Skipping is a HARD WARNING enforced by `task-execute` Step 11.

---

## Applies When

- Project is at wrap-up (`090-wrapup-*` task or equivalent) — automatic invocation by `task-execute` Step 11
- User says `/test-diet`, "test diet", "project close test review", "reconcile build vs maintain tests"
- Invoked by `/repo-cleanup` as a sub-step at project-completion mode
- Pre-merge check before final wrap-up PR

**NOT applicable when:**
- Project is actively in development (build-class tests are still load-bearing scaffolding for ongoing work)
- User wants generic code review (use `/code-review` instead)

---

## Workflow

### Step 1: Determine project scope

```
LOAD projects/{project-name}/current-task.md
   → identify project branch (e.g., work/{project-name})
LOAD projects/{project-name}/spec.md
   → identify project start commit (typically the first commit on the branch)

ENUMERATE test files touched during the project:
   git log {start-commit}..HEAD --name-only --diff-filter=AM -- 'tests/**/*.cs' | sort -u

IF no test files touched: report "No test changes — diet not applicable" and exit
```

### Step 2: Load build-vs-maintain criteria

```
READ docs/adr/ADR-038-testing-strategy.md §7 (Build-vs-Maintain Criteria — 17 bans)
READ .claude/constraints/testing.md (MUST NOT block — B1-B17 one-liners)
READ tests/CLAUDE.md ("Expect to Defend at Project Close" section)

The 17 bans are the canonical classifier. Apply per test method.
```

### Step 3: Classify each test method

For each touched test file, enumerate `[Fact]` / `[Theory]` / `[SkippableFact]` methods and classify each as one of:

| Classification | Criteria | Action |
|---|---|---|
| **MAINTAIN** | Tests behavior, lives under one of 6 KEEP paths, fails on real regression | KEEP — confirm at canonical path |
| **SCAFFOLDING** | Matches any B1-B17 ban (mirror, all-mocks-trivial, internal, pass-through, coverage-filler, language-feature, snapshot-trivial, name-without-scenario, exhaustive-switch, setup-to-assertion >10:1, getter/setter, generated-code, or any of B1-B5 wiring antipatterns) | DELETE — emit `git rm` for whole file OR Edit for method-level |
| **AMBIGUOUS** | Mixed signals (e.g., setup-heavy but assertion is behavioral) | FLAG for reviewer judgment; do not emit removal command |

Per-method evaluation heuristics (apply in order):

1. **Path check (B-path)**: if file is NOT under `tests/integration/{auth,regression,data-mutation,tenant,contract}/**` OR `tests/unit/domain/**`, flag as path-violation; recommend `git mv` to canonical path OR delete if no canonical path applies.
2. **Naming check (B13)**: if test name doesn't match `{Method}_{Scenario}_{ExpectedResult}` shape (e.g., `Test1`, `Foo_Works`, `DoIt_Bug417`), classify SCAFFOLDING.
3. **Mock-shape check (B1, B2, B7, B15)**: if test contains `Mock<HttpMessageHandler>`, `Mock<IServiceClient>`, OR `Mock<>` count ≥ 3 AND assertion count ≤ 2, classify SCAFFOLDING.
4. **Wiring check (B3, B4)**: if test asserts `services.GetRequiredService<>` OR `Throws<ArgumentNullException>` on constructor, classify SCAFFOLDING.
5. **Mirror check (B6)**: if test method is a single-line assertion against a single-line production method (1:1), classify SCAFFOLDING.
6. **Pass-through check (B9)**: if test only verifies a `Verify.Once()` on a single delegated call, classify SCAFFOLDING.
7. **Coverage-filler check (B10)**: if assertion is only `NotThrow()` or `NotNull()` without value/state verification, classify SCAFFOLDING.
8. **Language-feature check (B11, B14, B16)**: if test asserts record equality, `required` enforcement, exhaustive switch, OR pure auto-property round-trip, classify SCAFFOLDING.
9. **Snapshot check (B12)**: if test compares serialized output against a string literal of the framework's default format, classify SCAFFOLDING.
10. **Internal-access check (B8)**: if test uses `BindingFlags.NonPublic` reflection OR `[InternalsVisibleTo]` access path, classify SCAFFOLDING.
11. **Generator check (B17)**: if test asserts field-by-field that a mapper/projection preserves source fields, classify SCAFFOLDING (replace with `AssertConfigurationIsValid()` once).
12. **Setup ratio check (B15)**: if test's arrange section is >10× its assert section by line count, classify SCAFFOLDING.

If a test fails 0 of 1-12 AND lives at a KEEP path AND has a clear `{Method}_{Scenario}_{ExpectedResult}` name, classify MAINTAIN.

If a test fails 1-2 of 1-12 BUT has a clear behavioral assertion on a non-trivial scenario, classify AMBIGUOUS (reviewer judgment).

### Step 4: Emit reconciliation report

Write `projects/{project-name}/notes/test-diet-report.md`:

```markdown
# Test diet report — {project-name}

**Run date**: {YYYY-MM-DD HH:MM}
**Branch**: work/{project-name}
**Scope**: tests touched between {start-commit} and HEAD

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | N | confirmed |
| SCAFFOLDING (DELETE candidate) | D | review commands below |
| AMBIGUOUS (reviewer judgment) | A | listed below |
| PATH-VIOLATION (wrong KEEP path) | P | review `git mv` commands below |
| **Total tests touched** | **N+D+A+P** | — |

## Delete commands (DO NOT auto-execute — reviewer judgment required)

```bash
# Whole-file deletions (entire file is scaffolding)
git rm tests/unit/.../FooTests.cs
git rm tests/unit/.../BarTests.cs

# Method-level deletions (use Edit tool to remove specific [Fact]/[Theory] methods)
# tests/unit/.../BazTests.cs
#   - DELETE method: Foo_Works (B13 — name without scenario)
#   - DELETE method: Bar_Test1 (B13)
#   - KEEP method: Process_WhenValidInput_PersistsOrder (MAINTAIN)
```

## Path-move commands

```bash
# Files at wrong path — proposed canonical path
git mv tests/unit/.../FooTests.cs tests/integration/contract/Api/.../FooContractTests.cs
```

## Ambiguous — reviewer judgment

| File:Method | Ambiguity reason | Suggestion |
|---|---|---|
| FooTests.cs:Process_WhenX_ReturnsY | Setup-to-assertion ratio 8:1 (under 10:1 threshold) but mock-heavy | Likely scaffolding — review test for behavioral assertion |

## Maintain — confirmed (no action)

| File:Method | KEEP path | Why maintain |
|---|---|---|
| Issue417_DailyBriefingCascadeTests.cs:Reproduce_Bug | integration/regression | Regression test (every bug = regression rule) |

## Count delta

- Tests added during project: A
- Tests classified MAINTAIN: M
- Tests classified SCAFFOLDING: D
- Tests classified AMBIGUOUS: B
- Net post-diet expected count: A - D (after reviewer-confirmed deletes)

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1-B17.
```

### Step 5: Surface report to user

Report to user with summary:

```
✅ /test-diet complete

Scope: {N} test files touched during {project-name}
Classified:
  - MAINTAIN: {M} (keep, confirmed)
  - SCAFFOLDING: {D} delete candidates
  - AMBIGUOUS: {B} requiring judgment
  - PATH-VIOLATION: {P} requiring git mv

Report: projects/{project-name}/notes/test-diet-report.md

NEXT STEPS:
  1. Review delete commands in the report
  2. Apply approved deletions via `git rm` / Edit
  3. Apply path moves via `git mv`
  4. Re-run `dotnet build` + spot-check `dotnet test` after deletions
  5. Resolve AMBIGUOUS bucket via per-test judgment
  6. Commit reconciled state with message:
     `test: diet pass per ADR-038 §7 (removed {D} scaffolding tests; {B} ambiguous resolved)`

This skill does NOT auto-execute deletes — final decision is reviewer's per spec FR-B09.
```

### Step 6: Update task status

If invoked from `090-wrapup-*` task:
- mark the wrap-up step "test diet executed" complete
- include `notes/test-diet-report.md` path in the task's notes section

---

## Outputs

- `projects/{project-name}/notes/test-diet-report.md` — full classification + commands
- Summary table to user with counts and next-step checklist
- `git rm` / `git mv` commands ready for reviewer to apply

---

## Behavior contracts (binding)

| Contract | Enforcement |
|---|---|
| **Read-only by default** | Skill never executes `git rm` or `git mv` — emits commands only |
| **Path-check protective** | If a delete candidate is under a KEEP path AND has no same-PR replacement, the report flags it as PATH-VIOLATION-PROTECTED instead of SCAFFOLDING |
| **Ambiguity is honest** | When heuristics conflict, classification is AMBIGUOUS, not biased toward DELETE |
| **Auditable** | Each classification cites the specific ban(s) (B-number) that triggered it |
| **Idempotent** | Re-running on unchanged tree produces an identical report |

---

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| No `current-task.md` | Project not initialized via `/project-pipeline` | Provide branch name + start commit explicitly |
| Classifier disagrees with reviewer | Heuristics are imperfect (B15 ratio threshold, B9 single-line check) | Reviewer overrides — skill defers; reviewer's call is final |
| `dotnet build` fails after recommended deletes | A "scaffolding" test was actually load-bearing for compilation (e.g., shared helper class) | Restore the file; reclassify as MAINTAIN; update report |
| Project has thousands of touched tests | Long-running project or hot-path project | Skill handles in batches; consider running with `--bucket-by-category` flag |
| Cannot determine project start commit | Multiple merges, rebases | Operator passes `--from-commit <sha>` explicitly |

---

## Related Skills

- `/repo-cleanup` — invokes `/test-diet` as a sub-step at project-completion mode
- `/code-review` — companion check (different scope: code quality vs test classification)
- `/adr-check` — companion check (validates ADR compliance, including ADR-038 §7 bans on the project's deltas)
- `/merge-to-master` — runs AFTER `/test-diet` reconciliation merges into wrap-up PR

---

## Reference

- **Spec FR-B09**: `projects/ci-cd-unit-test-remediation-r1/spec.md` (project-close test diet protocol — binding workflow)
- **ADR-038 §7**: `docs/adr/ADR-038-testing-strategy.md` (Build-vs-Maintain Criteria — 17 bans, BAD/GOOD examples)
- **Constraints**: `.claude/constraints/testing.md` (MUST NOT block, B1-B17)
- **Module directive**: `tests/CLAUDE.md` ("Expect to Defend at Project Close" section)
- **Wired into**: `.claude/skills/task-execute/SKILL.md` Step 11 (project-complete branch); `.claude/skills/repo-cleanup/SKILL.md` (project-completion mode sub-step)

---

*This skill ensures every project's test deltas are reconciled against the build-vs-maintain criteria before wrap-up. Binding ≥6 months from 2026-06-26.*
