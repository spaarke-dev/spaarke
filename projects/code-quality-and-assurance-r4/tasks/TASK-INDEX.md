# Task Index — Code Quality & Assurance R4

> **Last Updated**: 2026-09-04
> **Status**: Initialized — **AUTONOMOUS execution** (owner direction 2026-09-04)
> **Tasks**: 33 generated · **P2b**: not yet decomposed (sized by task 020)
> **Lint**: `pwsh scripts/Validate-TaskPoml.ps1 projects/code-quality-and-assurance-r4/tasks` → 33/33 clean

> **How to run this project**: work top to bottom, dispatching each task via `task-execute` without asking
> between waves. **Build between waves** (`dotnet build Spaarke.sln` for any `.cs`). Stop only for a
> plan.md §3.5 hard stop, a fired `<escalation><trigger>`, or a red build. A task failing its own
> verification is **not** a stop — fix and re-run, or mark 🔄 and carry on with the wave.

Legend: 🔲 not started · 🔄 needs retry · ✅ complete · ⏸️ blocked

---

## P1 — The shared surface is knowable

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 001 | [Amend ADR-012 — enumerate 15, record 3 evaluation questions](001-adr-012-amendment-enumerate-shared-set.poml) | — | STANDARD | sonnet/high | ❌ | ✅ |
| 002 | [SharedPackageCensusTests — a 16th fails the build](002-shared-package-census-test.poml) | 001 | FULL | sonnet/high | ✅ | ✅ |
| 003 | [Publish the governance baseline (6 measures)](003-publish-governance-baseline.poml) | — | STANDARD | sonnet/high | ✅ | ✅ |

**Capability at phase end**: a 16th shared package fails the build with a message naming the evaluation questions; nothing existing was un-promoted. **Touches nothing hot.**

## P2a — Every ADR is routed, accurate, and measured

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 010 | [Classify all 49 ADRs on three axes; INDEX 36→49](010-classify-49-adrs-three-axes.poml) | — | FULL | **opus/xhigh** | ❌ | 🔲 |
| 011 | [Route every ADR — 49/49 routed, not 7/49 enforced](011-route-every-adr-to-a-mechanism.poml) | 010 | STANDARD | sonnet/high | ❌ | 🔲 |
| 012 | [§6.5 records for every stale/contested ADR](012-section-6-5-records-for-stale-adrs.poml) | 010 | FULL | **opus/xhigh** | ❌ | 🔲 |
| 013 | [Classification guard census + adr-audit issue body](013-classification-guard-and-audit-issue-body.poml) | 011 | FULL | sonnet/high | ❌ | 🔲 |

**Capability at phase end**: `n/49` + breakdown + amendment count visible weekly; nothing stale enforced; a new unclassified ADR fails the build. **Edit-only.**

## P2b — Enforce the criterion set

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 020 | [Size the FR-07 criterion set, then re-run /task-create](020-size-the-fr07-criterion-set.poml) | 011, 012 | FULL | **opus/xhigh** | ✅ | 🔲 |
| 021+ | *Per-ADR arch tests — **not yet decomposed*** | 020 | — | — | — | ⏸️ |

> **Deliberate decomposition boundary.** FR-07's set size is unknown until classification completes, so committing to a task count now would be a guess (plan.md §2, "Split P2"). Task 020 emits the criterion-bounded set; **then run `/task-create` again** to generate 021 onward.

## P3 — The governance surface maintains itself

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 030 | [Author the file revision-header standard](030-file-revision-header-standard.poml) | — | STANDARD | sonnet/high | ✅ | 🔲 |
| 031 | [Update-DocHeader.ps1 — idempotent, zero LLM authoring](031-update-doc-header-script.poml) | 030 | FULL | sonnet/xhigh | ✅ | 🔲 |
| 032 | [Run the backfill across 230 primitives](032-run-header-backfill.poml) | 031 | FULL | sonnet/high | ❌ | 🔲 |
| 033 | [Revision-header census — unstamped file fails build](033-revision-header-census-test.poml) | 032 | FULL | sonnet/high | ✅ | 🔲 |
| 034 | [PostToolUse usage-measurement hook](034-usage-measurement-hook.poml) | — | FULL | sonnet/high | ❌ | 🔲 |
| 035 | [**The one new workflow** — nightly job + rolling issue](035-the-one-nightly-workflow.poml) | 033, 036 | FULL | sonnet/xhigh | ❌ | 🔲 |
| 036 | [drift × usage ranking, U-shaped weight](036-drift-usage-ranking-script.poml) | 033, 034 | FULL | sonnet/high | ✅ | 🔲 |
| 037 | [Three-tier auto-bump](037-three-tier-auto-bump.poml) | 035 | FULL | sonnet/xhigh | ❌ | 🔲 |
| 038 | [Point-of-use signal + resolve the phantom script](038-point-of-use-signal-and-phantom-script.poml) | 035 | STANDARD | sonnet/medium | ❌ | 🔲 |

**Capability at phase end**: the clock fires without a human — a nightly issue ranked by drift × usage, clean nights close it, phantom mechanism claims are caught. **Touches `.claude/`, `.github/workflows/`, `settings.json`.**

## P4 — Don't rebuild, don't diverge

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 040 | [Generate the shared export index](040-generate-shared-export-index.poml) | — | FULL | sonnet/high | ❌ | 🔲 |
| 041 | [Wire index regeneration into the nightly job](041-wire-index-regeneration-nightly.poml) | 040, 035 | STANDARD | sonnet/medium | ❌ | 🔲 |
| 042 | [Escalation ladder for absence claims](042-escalation-ladder-for-absence-claims.poml) | 040 | STANDARD | sonnet/high | ❌ | 🔲 |
| 043 | [Functional equivalence check at Step 6.6](043-functional-equivalence-check.poml) | 040, 042 | STANDARD | sonnet/high | ❌ | 🔲 |
| 044 | [Known-divergence register — a list, not a mechanism](044-known-divergence-register.poml) | — | STANDARD | sonnet/high | ✅ | 🔲 |
| 046 | [Nightly boundary-crossing drift check (FR-19b) — with a kill criterion](046-boundary-crossing-drift-check.poml) | 040,041,044 | FULL | sonnet/high | ❌ | 🔲 |
| 045 | [`constraints/reuse.md` — **written last**](045-constraints-reuse-md.poml) | 040–044, 046 | STANDARD | sonnet/high | ❌ | 🔲 |

**Capability at phase end**: a new export equivalent to an existing capability returns a concrete `file:line`; known divergences are named. **Touches `.claude/skills/` — contended with `unified-access-control-r2`.**

## P5 — Tailored review that actually runs

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 050 | [Generate the per-project review checklist](050-generate-review-checklist.poml) | — | FULL | sonnet/xhigh | ✅ | 🔲 |
| 051 | [Wire the checklist into all three consumers](051-wire-checklist-three-consumers.poml) | 050 | STANDARD | sonnet/medium | ❌ | 🔲 |
| 052 | [**Prove** the headless runner works in Actions](052-prove-headless-runner.poml) | — | FULL | sonnet/high | ✅ | 🔲 |
| 053 | [Refresh the nightly review prompt (.NET 8→10)](053-refresh-nightly-review-prompt.poml) | 011, 050 | STANDARD | sonnet/medium | ✅ | 🔲 |
| 054 | [Wire the reviewer — as a **section**, not a workflow](054-wire-the-nightly-reviewer.poml) | 035, 051, 052, 053 | FULL | sonnet/xhigh | ❌ | 🔲 |
| 055 | [Correct the **four** misleading docs](055-correct-four-misleading-docs.poml) | 054 | STANDARD | sonnet/medium | ✅ | 🔲 |
| 056 | [Test-health section — reported, never gated](056-test-health-section.poml) | 035 | FULL | sonnet/high | ❌ | 🔲 |
| 057 | [KEEP-category directive at task-create](057-keep-category-directive.poml) | 050 | STANDARD | sonnet/medium | ❌ | 🔲 |
| 058 | [knip dead-export observation — exit 0](058-knip-dead-export-observation.poml) | 035 | FULL | sonnet/high | ❌ | 🔲 |

**Capability at phase end**: the reviewer written in March finally runs, scoped to the diff and active PRs, reporting per project, blocking nothing.

## Wrap-up

| # | Task | Deps | Rigor | Tier/Effort | Safe | Status |
|---|---|---|---|---|---|---|
| 090 | [Wrap-up — verify 9 criteria, run /test-diet, archive](090-project-wrap-up.poml) | 045, 058 | STANDARD | sonnet/high | ❌ | 🔲 |

---

## Parallel Execution Groups

Only two genuine parallel opportunities exist. **This is expected, not a planning defect** — 20 of 33 tasks write to `.claude/` or `.github/workflows/`, and both are main-session-only or contended.

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **P1-A** | 002, 003 | 001 for 002; 003 is independent | Both `parallel-safe`; different files |
| **P5-A** | 050, 052 | none | Checklist generator and runner spike are unrelated |

Everything else is serial: either a hard dependency chain (030→031→032→033) or a `.claude/` write.

> **P4-A was withdrawn.** Task 040 was initially grouped with 044, but it emits `.claude/catalogs/shared-export-index.json` — a `.claude/` write, so it is main-session-only. The generator script lives under `scripts/`, which is what made this easy to miss; the *artifact* is what determines the boundary.

### `/goal` wave eligibility

> **This table is about the `/goal` loop specifically — not about whether to run autonomously.** Execution
> **is** autonomous (see the banner above). `/goal` is a narrower mechanism: a Haiku evaluator judges a
> compiled stopping condition from the transcript. It is not eligible here because r4's waves are small,
> judgment-dense, or irreversible — a transcript-only evaluator cannot tell whether an ADR was correctly
> classified `stale` or whether 230 files were rewritten without losing a date. Dispatch normally and
> autonomously; just don't wrap a wave in `/goal`.

| Wave | Eligible | Reason |
|---|---|---|
| P1 | ❌ | Only 3 tasks, and 001 is an ADR amendment carrying a §6.5 obligation — judgment, not machine-verifiable |
| P2a | ❌ | 010 and 012 are judgment-dense (`opus`/`xhigh`) with live escalation triggers |
| P3 | ❌ | 032 is an irreversible 230-file bulk rewrite; 035 creates the one permitted workflow |
| P4 | ❌ | Mostly `.claude/` serial writes on a contended surface |
| P5 | ❌ | 052 is a spike whose negative result changes the phase's shape |

**No wave is `/goal`-eligible.** Per `task-create` Step 3.85 the bar is a machine-verifiable end-state across ≥3 well-specified low-ambiguity tasks. Dispatch autonomously without the `/goal` wrapper.

---

## Critical Path

```
030 → 031 → 032 → 033 → 035 → 054 → 055        (longest chain: 7)
                    ↑      ↑
              034 → 036    └── 051 ← 050
                               052
                               053 ← 011 ← 010
```

**Blocking dependencies**

- **P2b BLOCKED BY P2a** — the project's only genuine unknown; task 020 resolves it
- **035 blocks 037, 038, 041, 054, 056, 058** — six tasks attach to the one workflow
- **040 blocks 041, 042, 043, 045** — the index is the rung FR-17 and FR-18 resolve against
- **052 gates 054** — a negative spike result means 054 does not proceed as designed
- **045 must run last in P4** — it documents shipped reality by design

---

## High-Risk Items

| Risk | Mitigation |
|---|---|
| `.claude/skills/` collision with `unified-access-control-r2` (PR #939, executing) | `/conflict-check` before **and** before merge; affects 038, 042, 043, 051, 057 |
| `.github/workflows/` collision with PR #894 (draft, held for CI shadow window) | Coordinate before P3 merges; affects 013, 035, 041, 054, 056, 058 |
| 032's 230-file rewrite lands mid-flight | `.claude/**` only; script is idempotent, so re-run after merge is free |
| **FR-10 supersedes spec.md as written** | plan.md **R4** — update `spec.md` FR-10 before P3 executes, or spec and tasks disagree |
| 052 returns negative | P5 drops FR-23 per NFR-01; do **not** build an alternative reviewer |

---

## Standing Constraints (every task)

- Reuse `SourceScan`; **never fork it** · positive **and** negative controls on every arch test · **no DI resolution** (ADR-038 ban B3)
- **No threshold** on test count, duplication percentage, or file size — the line r4 must not cross
- **Exactly one** new workflow project-wide (NFR-04) — verify with `git diff --stat .github/workflows/`
- Never scan `.claude/worktrees/` or the ~17 sibling worktrees
- Class-1 artifacts (export index, review checklist) are **generated, never hand-authored**
- `.claude/`-touching tasks are **main-session only** — a sub-agent gets "Edit denied", which is the boundary working
