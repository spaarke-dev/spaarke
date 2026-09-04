# Project Plan: Code Quality & Assurance R4

> **Last Updated**: 2026-09-04
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: r3 hardened the code and left live forcing-functions behind. r4 applies the same move one
level up — to the layer that governs the code. The governance surface (49 ADRs, 16 constraints, 94
patterns, 71 skills, 9,750 tests) is authored well, enforced thinly, and decays because nothing fires
when it does.

**Scope** — five capabilities, each independently shippable:

- **P1** The shared surface is knowable — ADR-012 enumerates its 15 packages; a census fails on a 16th
- **P2** Every ADR is routed, accurate, and measured — 49 classified on three axes, then the criterion set enforced
- **P3** The governance surface maintains itself — a revision-header standard, a usage signal, and one nightly job
- **P4** Don't rebuild, don't diverge — a generated export index, a second rung for absence claims, a divergence register
- **P5** Tailored review that actually runs — a generated per-project checklist feeding the reviewer that has been written but unwired since March

**Timeline**: one phase at a time; P1 ships in days. **Estimated effort**: ~32–38 tasks excluding P2b.

**Execution posture**: **AUTONOMOUS** (owner direction, 2026-09-04). The pipeline produced artifacts +
tasks; execution runs wave by wave without a per-wave "continue". Stop only for a true decision or a
genuine problem — the per-task `<escalation><trigger>` blocks are those stop conditions, and they are now
load-bearing rather than advisory. See §3.5.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):

- **ADR-012** Shared Component Library — the direct subject of P1, and **amended** by it (path B, pre-declared)
- **ADR-038** Testing Strategy — every new test obeys the 7 KEEP paths and 17 bans; positive **and** negative controls on every arch test; **no DI resolution** (ban B3)
- **CLAUDE.md §6.5** — the ADR conflict protocol; FR-08 and FR-09b depend on it
- **CLAUDE.md §10 / §11** — hot-path declaration and component justification

**From spec**:

- Exactly **one** new CI workflow (NFR-04). No new agent, no new skill, no new POML block.
- **Advisory by default** (NFR-05). Only the censuses and FR-07's arch tests block — each asserts an enumerated fact.
- **No threshold** on test count, duplication percentage, or file size. The line r4 must not cross is a gate that blocks on a count-proxy for a judgment question — the retired God-class ratchet.
- **Sized by completeness, never by count** (NFR-03). FR-07's set is bounded by a functional criterion.
- **Nothing existing is un-promoted.** FR-01 is enumeration, not cleanup.

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| **Split P2** into P2a (classify) + P2b (write tests) | FR-07's size is genuinely unknown until FR-05 classifies all 49. Committing to a task count before that is a guess. | P2b is not decomposed at pipeline time; task 020 emits the set, then `/task-create` runs again |
| **Autonomous execution** | Owner direction 2026-09-04: run autonomously as long as it is safe and accurate; gate only on a true decision or a significant issue | No per-wave "continue". The POML escalation triggers become the stop conditions (§3.5) |
| **FR-10 → a revision-header standard** | Owner direction during planning: one standard way to date and revision-track files, at the top, human readable, carrying version + revision type, **applied by script not LLM** | Supersedes FR-10 as written; recorded as a §6.5 path-B amendment (§8 R4 below) |
| **Frontmatter, not blockquote** | Skills cannot drop frontmatter (Claude Code parses `description`/`tags`/`appliesTo`). A blockquote standard would leave skills carrying two headers. | ~159 files gain frontmatter; migrated blockquote lines are removed so there is one source of truth |
| **Header applied to `.claude/**` only in r4** | `docs/**` is 324 more files. A 554-file diff across both trees while 17 worktrees are active is the exact collision NFR-06 warns about. | Same script, different `-Path`, run when the worktree count is low |

### Discovered Resources

**Applicable skills**: `adr-check` · `code-review` · `task-create` · `task-execute` · `doc-drift-audit`
(FR-13 Tier 2) · `test-diet` (the contract FR-26 extends) · `conflict-check` (NFR-06) ·
`ai-procedure-maintenance` (FR-15's phantom citation).

**Canonical implementations to copy**:

| Path | Reuse as |
|---|---|
| `tests/Spaarke.ArchTests/SourceScan.cs` | **The** source-scanning primitive — MUST reuse, MUST NOT fork |
| `tests/Spaarke.ArchTests/CredentialCensusTests.cs` | Census pattern for FR-02 and the FR-10 header census |
| `.github/workflows/nightly-health.yml` | Rolling-issue pattern for FR-12 |
| `.github/workflows/adr-audit.yml` | Idempotent tracking-issue pattern for FR-09a and FR-23d |
| `scripts/quality/post-edit-lint.sh`, `task-quality-gate.sh` | Hook pattern for FR-11 (live since March) |
| `scripts/quality/nightly-review-prompt.md` | **Already written** — FR-23 is pure wiring, not authoring |

**Verified baseline (2026-09-04, this worktree)**:

| Fact | Measured |
|---|---|
| Sanctioned dirs under `src/client/shared/` | **15** |
| `.claude/adr/INDEX.md` entries vs ADR files | **36 / 49** |
| ArchTest files | 31 |
| `nightly-quality.yml` | **absent** — yet described in **4** docs |
| `Find-SkillReferenceDrift.ps1` | **absent** — yet cited by `ai-procedure-maintenance` |
| Governance primitives | 71 skills · 16 constraints · 94 patterns · 49 ADRs = **230** |
| Machine-parseable `last-reviewed:` frontmatter | skills **63/71** · constraints **0/16** · patterns **0/94** |
| Human-readable `> **Last Reviewed**:` blockquote | patterns **87/94** · constraints **15/16** |

The last two rows are the finding that rewrote FR-10: those files are **not undated** — they are dated in
the other of two competing conventions. Only ~9 files across the whole set genuinely lack a date.

---

## 3. Implementation Approach

### Phase Structure

```
P1  The shared surface is knowable          (001–003)   touches nothing hot
 └─ ADR-012 amendment (path B) · SharedPackageCensusTests · baseline report

P2a Classify and correct                    (010–013)   edit-only
 └─ 49 ADRs on 3 axes · INDEX.md 36→49 · accuracy pass · classification guard

P2b Enforce the criterion set               (020 sizing → N tasks)
 └─ Set discovered by P2a, then /task-create runs again

P3  The governance surface maintains itself (030–038)   .claude/ + workflows
 └─ Revision-header standard + script + backfill + census · usage hook
 └─ ONE nightly workflow + drift×usage ranking · auto-bump · point-of-use line

P4  Don't rebuild, don't diverge            (040–045)   skill directives
 └─ Export index generator · escalation ladder · equivalence check
 └─ Divergence register · constraints/reuse.md (written LAST)

P5  Tailored review that actually runs      (050–058)   workflow section
 └─ Checklist generator · prompt refresh · reviewer wiring · 4 doc fixes
 └─ Test-health section · KEEP directive · knip

090 Wrap-up + /test-diet
```

### Critical Path

- **P2b BLOCKED BY P2a** — the only genuine unknown in the project. Task 020 resolves it.
- **FR-20 (`constraints/reuse.md`) is written LAST in P4** — it documents shipped reality, so it cannot precede the thing it documents.
- **FR-16's export index BLOCKS FR-17 and FR-18** — both cite a rung that does not exist until the index does.
- **FR-21's checklist BLOCKS FR-23** — the reviewer needs scope input, or it runs generically across 17 worktrees and produces findings nobody acts on.
- **FR-12's workflow BLOCKS FR-23, FR-25, FR-27** — all three are *sections* of it, not separate workflows (NFR-04).

### High-Risk Items

| Risk | Mitigation |
|---|---|
| **PR #894** (`ci/tier2-unit-scope`, draft, held for the CI shadow window) edits `.github/workflows/` | Coordinate before P3 merges; `/conflict-check` immediately before and before merge |
| **`unified-access-control-r2`** (PR #939, executing) declares `skill-directives=Y` and edits the same `task-create` / `code-review` SKILL.md files r4 touches in P3–P5 | Same; sequence P4/P5 skill edits after #939 lands, or coordinate file ownership |
| A 110-file header backfill lands mid-flight across 17 worktrees | Scoped to `.claude/**` only; `docs/**` deferred; the script is idempotent so a re-run after a merge is free |
| FR-23's Claude Code headless runner in GitHub Actions is **unverified** | Spec's own Unresolved Question #2. P5 task 052 proves the runner before 053 wires it |
| Scope creep back into rejected territory | The spec's Out-of-Scope list is reproduced in each task's `<constraints>` so a later task cannot silently re-adopt it |

### 3.5 Autonomous execution contract

Owner direction 2026-09-04: **run autonomously as long as it is safe and accurate.** Do not stop between
waves for confirmation. Stop only when a true decision is required or a significant issue arises.

**What runs without asking**: every task whose acceptance criteria are a closed set and whose verification
is mechanical — which is most of them. Dispatch each task through `task-execute` at its declared
`<model-tier>`/`<effort>`, run the Step 9.5 gates, mark the task ✅, and continue to the next wave.

**Between every wave, verify before dispatching the next** (project-pipeline Step 5):

- any `.cs` touched → `dotnet build Spaarke.sln` (the solution, not one project — test projects glob shared sources)
- any `.ts`/`.tsx` touched → build the relevant package
- **a red build STOPS the run.** Do not dispatch the next wave on a broken tree.

**Hard stops — these are decisions, not obstacles.** Surface and wait:

| Where | Condition | Why it cannot be autonomous |
|---|---|---|
| **012** | A stale/contested ADR governing auth, security, tenant isolation, or compliance | CLAUDE.md §6.5 **requires** explicit human sign-off. Non-negotiable. |
| **020** | The criterion set is empty, or large enough that P2b exceeds the rest of the project | Changes the project's shape — an r4-versus-r5 scope call |
| **052** | The headless runner does not work | P5 drops FR-23 rather than building an alternative; that is an owner call |
| **012** | A path-B amendment would change a rule other active worktrees depend on | Cross-worktree coordination |
| **any** | `/conflict-check` reports another worktree actively editing the same file | Silent overwrite is what NFR-06 exists to prevent |
| **any** | A fired `<escalation><trigger>` | Each one marks a known judgment boundary. Firing is a legitimate outcome, **not a failure** — do not retry past it |

**What is NOT a stop**: a task failing its own verification. Fix it and re-run, or mark it 🔄 and continue
with the wave's other tasks. One failure does not abort a wave.

**One prerequisite before P3**: resolve risk **R4** — `spec.md` FR-10 still describes the superseded
requirement. Amend it (or record the divergence) before task 030 runs.

---

## 4. Phase Breakdown

### P1 — The shared surface is knowable (tasks 001–003)

**Objectives**: enumerate the sanctioned shared set; make a 16th package fail the build; record what the
promotion evaluation actually asks; publish the measured baseline.

**Deliverables**:
- [ ] ADR-012 amended (path B) — all 15 packages named, one line of reason each, no `"etc."` remaining
- [ ] Three evaluation questions recorded — **none of them a gate**
- [ ] `SharedPackageCensusTests` — passes on 15, **fails on a synthetic 16th**, keyed on **directory presence** not `package.json`
- [ ] Baseline report (FR-04's six measures)

**Inputs**: `src/client/shared/` (15 dirs) · `.claude/adr/ADR-012-shared-components.md` · `SourceScan.cs` · `CredentialCensusTests.cs`
**Outputs**: amended ADR-012 · `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` · `notes/baseline-2026-09.md`

**Watch**: the census must be keyed on directory presence. `Spaarke.LegalWorkspace` has no `package.json`,
so a `package.json`-keyed census enumerates 14 and silently misses it — which is the exact failure the
test exists to prevent. It also needs an allow-list for `@spaarke/*` names that are applications
(`office-addins`, `secure-project-workspace`, `document-upload-wizard`, `pcf-shared`).

### P2a — Classify and correct (tasks 010–013)

**Objectives**: classify all 49 ADRs on enforceability / accuracy / (for judgment-only) checkability;
grow INDEX.md 36→49; ensure nothing stale gets enforced; make a new unclassified ADR fail the build.

**Deliverables**:
- [ ] 49 ADRs classified on three axes, one line of reason per axis, recorded in `.claude/adr/INDEX.md`
- [ ] INDEX.md grows 36 → 49 as a side effect
- [ ] Each ADR routed to a mechanism; coverage stated as **49/49 routed**, not 7/49 enforced
- [ ] Every `stale`/`contested` ADR gets its own §6.5 record (path B or C) — and **no arch test**
- [ ] A classification-guard census: a new ADR without a classification fails the build

**Watch**: FR-08 is the point of FR-05. Classification will surface stale ADRs mid-project **by
construction** — that is designed, not a surprise. Each one gets a §6.5 record rather than being silently
enforced or silently skipped.

### P2b — Enforce the criterion set (task 020, then N tasks)

**Deliberately not decomposed at pipeline time.** Task 020 applies FR-07's criterion — every ADR that is
`enforceable` **and** `current` **and** whose violation is both **silent** and **expensive** (security,
tenant isolation, auth, data integrity, cross-layer boundary) — and emits the bounded set, prioritised
within it by FR-04's citation counts. `/task-create` then runs again for the resulting tasks.

Sized by the criterion, never by a count: the phase is done when the set is covered, whether that is 4
ADRs or 14. Each test carries positive **and** negative controls and no DI resolution.

### P3 — The governance surface maintains itself (tasks 030–038)

**Objectives**: give every governance file a machine-readable revision header; measure which primitives
are actually used; make the clock fire without a human.

**Deliverables**:
- [ ] `docs/standards/FILE-REVISION-HEADER.md` — the standard (§8 R4)
- [ ] `scripts/quality/Update-DocHeader.ps1` — idempotent, `-Path` + `-Check` modes
- [ ] Backfill run across the 230 `.claude/` primitives — **by script, zero LLM authoring**
- [ ] Header census test: a new unstamped file fails the build; positive + negative controls
- [ ] `PostToolUse` usage hook appending `timestamp, name`, registered in `.claude/settings.json`
- [ ] **One** nightly workflow with a rolling issue: stale primitives ranked by `drift_signal × usage_weight` (**U-shaped** — heavily used *and* never used both rank high), broken pointer paths, **broken mechanism claims**, Class-1 regeneration diffs, unclassified ADRs. Advisory. A clean night closes the issue.
- [ ] Three-tier auto-bump (FR-13): machine bumps `last-reviewed` when referenced files have not changed; Tier 2 routes to `doc-drift-audit`; Tier 3 reaches the owner
- [ ] One-line point-of-use signal in `code-review` Step 9.5 (FR-14) — a one-bit flag, not a review
- [ ] FR-15 resolved: `Find-SkillReferenceDrift.ps1` either satisfied by the link check **or** the citation removed. Cited-and-nonexistent is the only unacceptable outcome.

**Watch**: `last-updated` and `last-reviewed` must stay distinct fields — FR-13's auto-bump moves only
`last-reviewed`, so the stamp honestly means *verified nothing changed*. Collapsing them breaks the tier.

### P4 — Don't rebuild, don't diverge (tasks 040–045)

**Deliverables**:
- [ ] Export index generated from shared-library barrel files — symbol, kind, package, source path; **shared libs only**; greppable in one pass; regenerates inside the nightly job; **nothing hand-authored**
- [ ] `task-create` + `code-review` require a recorded escalation beyond the lexical pass before `<existing>` may say "none", and record **which passes ran**
- [ ] Equivalence check in `code-review` Step 6.6 — resolves **against the index**, returns a concrete `file:line` or nothing; **advisory only; no sub-agent**
- [ ] Divergence register — both `.eml` builders, both R6 financial handlers; the shared behavior; why merging was deferred. **A list, not a mechanism.** If it needs tooling, it has left scope.
- [ ] `.claude/constraints/reuse.md`, **written last**, 100–200 lines, **pointing at** CLAUDE.md §11 and ADR-012 rather than restating them

### P5 — Tailored review that actually runs (tasks 050–058)

**Deliverables**:
- [ ] `projects/{name}/review-checklist.md` **generated** from spec ADR Tensions + design hot-path/justification + POML constraints/criteria/justification; machine-readable front-matter plus prose; **never hand-authored**; consumed by all three of the nightly reviewer, `code-review` Step 9.5, and wrap-up
- [ ] `nightly-review-prompt.md` refreshed — **.NET 8 → .NET 10**, sections aligned to what P2a routes to it, accepts the checklist as scope input
- [ ] Reviewer wired as a **second section of the P3 workflow** — scoped to the diff since last run plus PRs on active branches, **never the whole repo across 17 worktrees**; rolling issue per active project; non-blocking
- [ ] **Four** docs corrected (spec says three; `docs/procedures/DEPENDENCY-MANAGEMENT.md` is the fourth): they no longer describe a `nightly-quality.yml` with five jobs, SonarCloud, and a `<15 min` target that does not exist
- [ ] Test-health section: `Skip=` count with per-entry age, Flaky-trait count with age, net test delta. **No threshold on any of the three.**
- [ ] `task-create` directive: a new test must name its ADR-038 KEEP category
- [ ] `knip` over the shared libs, **exit 0**, as a section of the nightly issue; excludes `.claude/worktrees/` and sibling worktrees

---

## 5. Dependencies

### External

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| GitHub Actions minutes for one nightly job | Available | Low | Advisory tier; no blocking gate |
| Claude Code headless in GitHub Actions (FR-23) | **Unverified** | Medium | P5 task 052 proves the runner before 053 wires it; P5 is the last phase and droppable |

### Internal

| Dependency | Location | Status |
|---|---|---|
| `SourceScan` | `tests/Spaarke.ArchTests/SourceScan.cs` | Production — reuse, never fork |
| `nightly-health.yml`, `adr-audit.yml` | `.github/workflows/` | Production — pattern sources |
| `nightly-review-prompt.md` | `scripts/quality/` | **Written 2026-03-14, never wired** |
| FR-04 fan-in + citation measurements | Taken 2026-09-03 | P1 confirms rather than derives |

---

## 6. Testing Strategy

Governed by **ADR-038**. r4 adds no BFF source change, so the publish-size gate does not apply (NFR-07);
all additions are test-only under `tests/Spaarke.ArchTests/`.

**Arch/census tests** (the only blocking additions — each asserts an enumerated fact):
- `SharedPackageCensusTests` — 15 pass, synthetic 16th fails
- ADR classification guard — unclassified new ADR fails
- Revision-header census — unstamped new file fails
- FR-07's criterion set — one per qualifying ADR

**Every one of them**: positive **and** negative controls; reuses `SourceScan`; **no DI resolution**
(ban B3); failure message names the §6.5 challenge path.

**Advisory, exit-0, never gating**: the nightly job, the equivalence check, the reviewer, test-health
reporting, `knip`.

**Explicitly not added**: any threshold on test count, duplication percentage, or file size.

---

## 7. Acceptance Criteria

Mirrors spec §Success Criteria; each is verified by exercising the mechanism, not by asserting it.

- [ ] **P1** — add a synthetic 16th shared directory; the census fails with a message naming the three evaluation questions and the amendment requirement; nothing existing was un-promoted
- [ ] **P2a** — read `adr-audit.yml`'s issue body: `n/49` plus classification breakdown plus amendment count; nothing marked `stale` has a test
- [ ] **P2b** — the criterion set is covered; each test has positive + negative controls
- [ ] **P3** — a nightly issue exists, ranked by `drift × usage`; a clean night closes it; a deliberately-introduced phantom `*.yml` reference is flagged; a new unstamped primitive fails the build
- [ ] **P4** — seed a known-equivalent symbol and confirm the hit returns `file:line` against the generated index; the divergence register enumerates both known pairs
- [ ] **P5** — the checklist is generated (not authored) and consumed by all three consumers; all four misleading docs corrected
- [ ] **Global** — `git diff --stat .github/workflows/` shows **exactly one** addition across the whole project
- [ ] **Global** — each phase merged as its own PR and delivered its capability without later phases
- [ ] **Global** — each phase declared its net new concepts; none exceeded one (P1=0, P2=1, P3=1, P4=1, P5=1)

---

## 8. Risk Register

| ID | Risk | P | I | Mitigation |
|---|---|---|---|---|
| R1 | `.claude/` skill-directive collision with `unified-access-control-r2` (PR #939) | High | Med | `/conflict-check` before and before merge; sequence P4/P5 after #939 |
| R2 | `.github/workflows/` collision with PR #894 | Med | Med | Coordinate before P3 merges; #894 is held for the shadow window anyway |
| R3 | P2b turns out much larger than estimated | Med | High | Exactly why P2 is split — 020 sizes it before anything is committed |
| R4 | **FR-10 as specified is superseded** by owner direction (revision-header standard) | — | — | **Path-B amendment to the spec.** Recorded here and in the P3 tasks; `spec.md` FR-10 must be updated to match before P3 executes, or the two disagree |
| R5 | FR-23's headless runner does not work in Actions | Med | Med | Proven in task 052 before wiring; P5 is droppable per NFR-01 |
| R6 | A later task silently re-adopts rejected scope | Med | High | The Out-of-Scope list is reproduced in each task's `<constraints>` |
| R7 | The header backfill conflicts with in-flight worktree edits | Med | Low | `.claude/**` only; idempotent script; re-run after merge is free |

---

## 9. Next Steps

1. **Resolve R4** — update `spec.md` FR-10 to the revision-header standard so spec and tasks agree
2. **Run** `/conflict-check` for `.claude/` and `.github/workflows/`
3. **Execute autonomously from task 001**, wave by wave per §3.5, building between waves
4. **After P2a**, re-run `/task-create` for P2b, then continue
5. **Stop only** on a §3.5 hard stop, a fired escalation trigger, or a red build

---

**Status**: Ready for Tasks — **autonomous execution**
**Next Action**: resolve R4, then start task 001 and run through

---

*For Claude Code: load §2 (constraints + canonical implementations) and the relevant §4 phase before executing any task.*
