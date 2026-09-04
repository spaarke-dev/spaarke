# Code Quality & Assurance R4 — AI Implementation Specification

> **Status**: Executing — `/project-pipeline` ran 2026-09-04; 33 tasks generated (P2b deliberately deferred to task 020's sizing)
> **Created**: 2026-09-03 · **Amended**: 2026-09-04 — **FR-10 rewritten** (revision-header standard, owner direction), Owner Clarifications gained 3 rows, Unresolved Questions 1 + 3 resolved
> **Source**: [`design.md`](design.md) (620 lines, 13 findings, 5 phases)
> **Binding owner constraint**: this must NOT be a massive change to how we work. Every phase is a small, independently shippable capability, and **r4 may stop cleanly after any phase**.

---

## Executive Summary

r3 hardened the code and left live forcing-functions behind. **r4 applies the same move one level up, to the layer that governs the code.** Our governance surface — 49 ADRs, 16 constraints, 94 patterns, 71 skills, 9,750 tests — is authored well, enforced thinly, and decays because nothing fires when it does.

Two workstreams: **don't rebuild / don't diverge** (we don't rebuild what exists, and we don't run the same function in two code paths), and **enforcement & continuity** (rules we already wrote are enforced where enforceable, verified for accuracy, and maintained without anyone remembering to).

**No new subsystem.** Exactly one new CI workflow, no new agent, no new skill, no new POML block.

---

## Scope

### In Scope

| Phase | Capability | FRs |
|---|---|---|
| **P1** | The shared surface is knowable | FR-01 … FR-04 |
| **P2** | Every ADR is routed, accurate, and measured | FR-05 … FR-09 |
| **P3** | The governance surface maintains itself | FR-10 … FR-15 |
| **P4** | Don't rebuild, don't diverge | FR-16 … FR-20 |
| **P5** | Tailored review that actually runs, over a visible test suite | FR-21 … FR-27 |

### Out of Scope

Assessed and **rejected or cut** — listed so a later task does not silently re-adopt them (design §1.2):

- **Un-packaging or regressing any existing shared component.** Owner position: they are in the shared library and they stay.
- **Any reviewer sub-agent.** Reviewer agents produce differences of opinion without substantive value; F-5's question is retrieval and needs an index.
- **The `promote` verdict + ledger + quota (former F-3)** — cut; three new concepts for an unmeasured signal, addressing promotion policy rather than either real risk.
- **Remediating the 68 skipped tests** — analysis complete, already filed as [#794](https://github.com/spaarke-dev/spaarke/issues/794). r4 makes the number visible only.
- **Any threshold on test count, duplication percentage, or file size.** Count-proxies for judgment questions — the retired God-class ratchet.
- **Writing 42 arch tests.** The deliverable is classification + routing + a criterion-bounded set.
- **Merging the known-divergent code paths** (r3's `.eml` builders, R6 financial handlers). r4 records them; remediation is a separate project.
- Generated "capability manifest"; `ast-grep` for C#; SonarAnalyzer/Roslyn duplicate analyzer; `jscpd` hard gate; new `<reuse-evidence>` POML block; LSP/semantic-index MCP sidecar; repo-wide duplication audit.

### Affected Areas

| Path | Change |
|---|---|
| `.claude/adr/ADR-012-shared-components.md` | Amendment (path B) — enumerate 15, add three evaluation questions |
| `.claude/adr/INDEX.md` | Grows 36 → 49 entries; gains three classification axes |
| `tests/Spaarke.ArchTests/` | New census + arch-test files; reuses `SourceScan` |
| `.github/workflows/` | **One** new nightly workflow; `adr-audit.yml` issue-body edit |
| `.claude/settings.json` | Usage hook registration (`PostToolUse`) |
| `scripts/quality/nightly-review-prompt.md` | Refresh (.NET 8 → 10; align sections) |
| `.claude/skills/{task-create,code-review}/SKILL.md` | Directive edits — escalation ladder, equivalence check, KEEP-category, point-of-use signal |
| `.claude/constraints/reuse.md` | New (written last, documents shipped reality) |
| `docs/{architecture/ci-cd-architecture,procedures/ci-cd-workflow,procedures/testing-and-code-quality}.md` | Correct descriptions of a `nightly-quality.yml` that does not exist |
| `CLAUDE.md` §11 | Pointer to `constraints/reuse.md` + ADR-012 amendment reference |
| `projects/{name}/review-checklist.md` | New **generated** artifact |

---

## Requirements

### P1 — The shared surface is knowable

**FR-01 — Enumerate the sanctioned shared set.**
List all 15 directories under `src/client/shared/` in ADR-012, one line of reason each.
*Acceptance*: no `"etc."` remains; all 15 named; **no package is un-promoted or marked for removal**; `@spaarke/visuals` (0 current consumers) is recorded as legitimately anticipatory.

**FR-02 — Census the set.**
Add `SharedPackageCensusTests` to `tests/Spaarke.ArchTests/`.
*Acceptance (closed set)*: (a) passes on the 15; (b) **fails on a synthetic 16th**; (c) keyed on **directory presence**, NOT `package.json` — `Spaarke.LegalWorkspace` has none and a `package.json`-keyed census would enumerate 14 and silently miss it; (d) carries an explicit allow-list for `@spaarke/*` names that are applications (`office-addins`, `secure-project-workspace`, `document-upload-wizard`, `pcf-shared`); (e) positive **and** negative controls per ADR-038; (f) no DI resolution (ADR-038 ban B3); (g) **the failure message names FR-03's three questions and the amendment requirement.**

**FR-03 — Write down what the promotion evaluation asks.**
Amend ADR-012: 2+ consumers stays a trigger **to evaluate**, not a mandate to promote and not a bar to clear.
*Acceptance*: three questions recorded — (1) is the API stable across consumers or does each need branching; (2) testable in isolation without consumer fixtures; (3) is the commonality semantic or coincidental. **None is a gate**; anything failing may still be promoted with a stated reason.

**FR-04 — Publish the baseline.**
*Acceptance (closed set)*: (a) `<extension>` yes/no ratio across 448 justifications; (b) `<existing>none` count (53 today); (c) per-package import fan-in (measured 2026-09-03: `ui-components` 54 surfaces, `auth` 37, `communication-components` 8, seven at 2, three at 1, two at 0); (d) **ADR citation counts across POML tasks** — 50 distinct ADRs, top being ADR-021 (3,061), ADR-013 (2,131), ADR-038 (1,745), ADR-010 (1,527), ADR-028 (1,521), ADR-012 (1,210); (e) `<escalation><trigger>` firing count; (f) §6.5 amendment count.

### P2 — Every ADR is routed, accurate, and measured

**FR-05 — Classify all 49 ADRs on three axes.**
*Acceptance*: recorded in `.claude/adr/INDEX.md` with one line of reason per axis —
(a) **enforceability**: `enforceable` / `partially-enforceable` / `judgment-only`;
(b) **accuracy**: `current` / `stale` / `contested`;
(c) for `judgment-only` only: `checkable-by-reading` / `aesthetic`.
`INDEX.md` grows from **36 to 49** entries as a side effect.

**FR-06 — Route, don't filter.**
*Acceptance*: the classification maps each ADR to a mechanism — `enforceable` → arch test (blocking); `partially` → arch test + nightly review; `judgment-only`+`checkable-by-reading` → nightly review (FR-23); `judgment-only`+`aesthetic` → **recorded as deliberately unenforced**. Coverage is stated as *49/49 routed*, not *7/49 enforced*.

**FR-07 — Enforce the criterion set.**
*Acceptance*: **every** ADR that is `enforceable` **and** `current` **and** whose violation is both **silent** (won't surface at review or runtime) **and** **expensive** (security, tenant isolation, auth, data integrity, cross-layer boundary) has an arch test, prioritised within that set by FR-04's citation counts. **Sized by this criterion, never by a count** — the phase is done when the set is covered, whether that is 4 ADRs or 14. Each test carries positive + negative controls and no DI resolution.

**FR-08 — Nothing stale gets enforced.**
*Acceptance*: no ADR marked `stale` or `contested` receives an arch test until amended or confirmed; each such ADR gets its own CLAUDE.md §6.5 record (path B or C).

**FR-09 — Make enforcement measurable and self-sustaining.**
*Acceptance (closed set)*: (a) `adr-audit.yml`'s issue body carries `n/49` plus the classification breakdown plus the amendment count; (b) **every arch-test failure message names the §6.5 challenge path** — "if this rule is wrong here, paths A/B/C are how you say so"; (c) **a new ADR added without a classification fails the build.**

### P3 — The governance surface maintains itself

**FR-10 — One revision-header standard, applied by script, assertable by census.**

> **AMENDED 2026-09-04** (owner direction, recorded as a spec-level path-B amendment per CLAUDE.md §6.5). The original text asked for "a parseable `last-reviewed`; ~110 files gain a **first** stamp (0/16 constraints, 0/94 patterns)". **That count is an artifact of measuring only YAML frontmatter.** Re-measured 2026-09-04:
>
> | Surface | frontmatter `last-reviewed:` | blockquote `> **Last Reviewed**:` |
> |---|---|---|
> | skills (71) | 63 | — |
> | constraints (16) | **0** | **15** |
> | patterns (94) | **0** | **87** |
>
> The files are **not undated** — the repo carries two competing conventions, and only ~9 files genuinely lack a date. The work is a **format standardisation**, not a 110-file authoring job. The requirement is restated accordingly, and widened per owner direction: a date alone is insufficient — the header must also carry a version and the *kind* of the last change.

*Requirement*: adopt **one** repo-wide file revision header — top-of-file, human readable, machine parseable — and backfill it across the 230 `.claude/` governance primitives **by script, not by LLM**.

*The header* (`docs/standards/FILE-REVISION-HEADER.md`):

```yaml
---
version: 1.0
status: active
revision-type: baseline
last-updated: 2026-08-14
last-reviewed: 2026-05-17
reviewed-by: ai-procedure-quality-r1
---
```

Only `version` and `revision-type` are new; the other four already exist somewhere in the tree. `revision-type` describes the **last** change: `major` = a MUST/MUST NOT or the rule's meaning changed · `minor` = content added or clarified · `editorial` = typo/link/formatting, no version bump · `baseline` = standard applied retroactively, history predates versioning · `initial` = first authored under the standard.

**`last-updated` and `last-reviewed` MUST stay distinct** — FR-13's auto-bump moves only `last-reviewed`, so the stamp honestly means *verified nothing changed*. Collapsing them breaks the three-tier model.

*Why frontmatter and not the blockquote*: skills **cannot** drop frontmatter — Claude Code parses `description`/`tags`/`appliesTo` from it. Choosing the blockquote as the standard would leave all 71 skills carrying two headers.

*Acceptance (closed set)*: (a) the standard is published at `docs/standards/FILE-REVISION-HEADER.md`; (b) `scripts/quality/Update-DocHeader.ps1` applies it — **preserving existing dates** (inventing none), deriving `last-updated` from `git log -1 --format=%as` where absent, seeding `version: 1.0`/`revision-type: baseline`, removing the migrated blockquote lines so there is one source of truth, **adding keys only** for skills (never touching `description`/`tags`/`techStack`/`appliesTo`/`alwaysApply`/`exemplar`), idempotent, with `-Path` and a `-Check` mode that exits non-zero; (c) the backfill has run across `.claude/{skills,constraints,patterns,adr}`; (d) a census test asserts the header on every such file with the positive **and** negative controls ADR-038 requires, and **a new unstamped file fails the build**.

*Scope*: `.claude/**` (230 primitives) in r4 — these are what FR-12's staleness ranking consumes. `docs/**` (324 files) is the **same script with a different `-Path`**, deliberately deferred: a 554-file diff across both trees while ~17 worktrees are active is precisely the collision NFR-06 warns about.

**FR-11 — Usage is measured.**
*Acceptance*: a `PostToolUse` hook appends `timestamp, name` to a usage log — matching the **Skill** tool for skills, and **Read** on `.claude/{adr,patterns,constraints}/**` for the rest. Registered in `.claude/settings.json` alongside the two hooks live since March. Retroactive ADR citation counts (FR-04) mean ranking works from day one rather than after months of accumulation.

**FR-12 — One nightly job, one rolling issue.**
*Acceptance (closed set)* — the issue reports: (a) stale primitives, **top N by `drift_signal × usage_weight`** where usage weight is **U-shaped** (heavily used *and* never used both rank high); (b) broken pointer paths across `.claude/**` and `docs/**`; (c) **broken mechanism claims** — every `*.yml` named in `docs/**` or `.claude/**` must exist in `.github/workflows/`; (d) Class-1 regeneration diffs; (e) unclassified ADRs. **Advisory, never blocking.** A clean night closes the issue.

**FR-13 — Mechanical review clears itself.**
*Acceptance*: primitives whose referenced files have not changed since `last-reviewed` have their stamps **auto-bumped by machine** — honestly, since the stamp then means *verified nothing changed*. Tier 2 (referenced files did change) routes to `doc-drift-audit` on the diff. Tier 3 (is this still the rule we want) reaches the owner only for ADR/constraint semantics.

**FR-14 — Capture the point-of-use signal.**
*Acceptance*: one line in `code-review` Step 9.5 output — *did any primitive you loaded turn out to be wrong?* Usually "no". Picked up by FR-12's job. **Not a review — a one-bit flag at the moment of discovery.**

**FR-15 — Resolve the phantom drift script.**
*Acceptance*: `ai-procedure-maintenance`'s citation of `Find-SkillReferenceDrift.ps1` is **either satisfied by FR-12's link check or removed**. The current state — cited, nonexistent — is the only unacceptable outcome.

### P4 — Don't rebuild, don't diverge

**FR-16 — Generate the export index.**
*Acceptance (closed set)*: (a) generated from the shared-library barrel files — symbol, kind, package, source path; (b) **shared libs only** — not 940 BFF service files, not 37 solutions, not 46 PCF controls; (c) greppable in one pass; (d) **regenerates inside FR-12's nightly job**, any diff reported as a finding; (e) **nothing hand-authored** — a hand edit means the generator is broken.

**FR-17 — Give absence assertions a second rung.**
*Acceptance*: `task-create` and `code-review` require a recorded escalation beyond the lexical pass before `<existing>` may say "none", and record **which passes ran**.

**FR-18 — Detect functional equivalence.**
*Acceptance (closed set)*: (a) a check in `code-review` Step 6.6 asking whether a new exported symbol is functionally equivalent to a named existing capability despite a different implementation; (b) resolves **against the FR-16 index** — returns a concrete `file:line` or nothing; (c) **advisory severity only**; (d) **no sub-agent**; (e) hit rate accumulates against the FR-04 baseline.

**FR-19 — Record the known divergences.**
*Acceptance*: an enumerated register naming both code paths, the shared behavior, and why merging was deferred — seeded from r3's out-of-scope list (two live `.eml` builders; two distinct R6 financial handlers). **A list, not a mechanism**; r4 does not merge them. If it needs tooling, it has left scope.

**FR-20 — Document shipped reality.**
*Acceptance*: `.claude/constraints/reuse.md`, written **last** in this phase, 100–200 lines per the `constraints/INDEX.md` convention, **pointing at** CLAUDE.md §11 and ADR-012 rather than restating them.

### P5 — Tailored review that actually runs

**FR-21 — Generate the per-project review checklist.**
*Acceptance (closed set)*: (a) `projects/{name}/review-checklist.md` is **generated** from `spec.md` ADR Tensions, `design.md` hot-path + component-justification, and POML `<constraint>`/`<acceptance-criteria>`/`<justification>`; (b) **never hand-authored**; (c) machine-readable front-matter (ADRs in scope, hot files, invariants) plus a prose checklist; (d) consumed by **all three** of FR-23, `code-review` Step 9.5, and wrap-up.

**FR-22 — Refresh the existing prompt.**
*Acceptance*: `scripts/quality/nightly-review-prompt.md` updated `.NET 8` → `.NET 10`; sections aligned to what FR-06 routes to it; accepts FR-21's checklist as scope input.

**FR-23 — Wire the reviewer that already exists.**
*Acceptance (closed set)*: (a) runs as a **second section of FR-12's workflow** — not a second workflow; (b) scoped to the diff since last run plus PRs on active branches, **never the whole repo across ~17 worktrees**; (c) covers the `judgment-only` + `checkable-by-reading` ADRs; (d) findings land in a **rolling GitHub issue per active project**, idempotently updated; (e) **non-blocking**.

**FR-24 — Correct the three misleading docs.**
*Acceptance*: `ci-cd-architecture.md`, `ci-cd-workflow.md`, `testing-and-code-quality.md` no longer describe a `nightly-quality.yml` with five jobs, SonarCloud, and a `<15 min` target that **does not exist**; FR-12's mechanism-claim check would catch a recurrence.

**FR-25 — Make test growth visible.**
*Acceptance*: the nightly issue reports `Skip=` count with per-entry age (68 today, down from 168 on 2026-08-19), Flaky-trait count with age, and net test delta since last run. **No threshold on any of the three.**

**FR-26 — Bound the test obligation up front.**
*Acceptance*: one directive at `task-create` — a new test must **name its ADR-038 KEEP category**; FR-21's checklist carries the project's stated test obligation so `/test-diet` reconciles against it rather than against a general classifier.

**FR-27 — Observe dead exports.**
*Acceptance*: `knip` over the shared libs, **exit 0**, reported as a section of FR-12's issue. Excludes `.claude/worktrees/` and the ~17 sibling worktrees or it reports phantom findings.

### Non-Functional Requirements

- **NFR-01 — Phase independence.** Each of P1–P5 must be **deployable** (mergeable and leavable indefinitely), **functionally complete** (works end-to-end including the case that proves it fires), and **independently valuable**. A phase failing any test is re-cut before task-creation.
- **NFR-02 — Complexity budget.** Each phase declares its **net new concepts**; none may exceed **one** without explicit argument. P1 = 0, P2 = 1, P3 = 1, P4 = 1, P5 = 1.
- **NFR-03 — Sizing by completeness.** No phase is sized by task count, wave, or time box. Enumerated sets are bounded by a functional criterion (FR-07).
- **NFR-04 — Exactly one new workflow.** FR-12's nightly job, with FR-23 as a second *section* of it. No new agent, no new skill, no new POML block, no second home for reuse rules.
- **NFR-05 — Advisory by default.** FR-12, FR-18, FR-23, FR-25, FR-27 are advisory/exit-0. Only the censuses (FR-02, FR-09c, FR-10) and FR-07's arch tests block — correct, because each asserts an enumerated fact. **The line r4 must not cross: a gate that blocks on a count-proxy for a judgment question.**
- **NFR-06 — Collision discipline.** P1 touches nothing hot; P2 is edit-only. `/conflict-check` immediately before **and** before merge for P3, P4, P5.
- **NFR-07 — No BFF source change.** Publish-size gate (≤60 MB) not applicable; test-only additions under `tests/Spaarke.ArchTests/`.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-012** Shared components | Direct subject of FR-01/02/03 — **amended** |
| **ADR-038** Testing strategy | All new tests obey the 7 KEEP paths + 17 bans; positive/negative controls; no DI-resolution tests (ban B3); FR-26 makes the KEEP category explicit at authoring |
| CLAUDE.md §6.5 | ADR conflict protocol — FR-08, FR-09b |
| CLAUDE.md §10 / §11 | Hot-path declaration + component justification |

### MUST Rules

- ✅ MUST reuse `SourceScan` for all source scanning; MUST NOT fork it
- ✅ MUST pair every arch test with positive **and** negative controls
- ❌ MUST NOT use DI resolution in tests (ADR-038 ban B3)
- ❌ MUST NOT un-package, regress, or mark for removal any existing shared component
- ❌ MUST NOT add a threshold on test count, duplication percentage, or file size
- ❌ MUST NOT hand-author any Class-1 artifact (FR-16 index, FR-21 checklist)
- ❌ MUST NOT create a second CI workflow (NFR-04)
- ❌ MUST NOT scan `.claude/worktrees/` or sibling worktrees in any repo-wide pass

### Existing Patterns to Follow

- `tests/Spaarke.ArchTests/CredentialCensusTests.cs` — the census pattern for FR-02 and FR-10
- `.github/workflows/nightly-health.yml` — rolling-issue pattern for FR-12
- `.github/workflows/adr-audit.yml` — idempotent tracking-issue pattern for FR-09a, FR-23d
- `scripts/quality/post-edit-lint.sh`, `task-quality-gate.sh` — hook pattern for FR-11 (live since March)
- `.claude/skills/doc-drift-audit/SKILL.md` — diff-based review for FR-13 Tier 2
- `.claude/skills/test-diet/SKILL.md` — read-only, reviewer-actionable output; the contract FR-26 extends

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```

> **Undeclared surface**: P3 edits `.claude/settings.json` (FR-11 hook). Tracked file, copied per worktree, genuine merge conflict surface — not one of the five declared categories. Flagged rather than silently incurred.

### New Components (§11 three-question gate)

| New component | Existing overlap | Can extend? | Cost-of-doing-nothing |
|---|---|---|---|
| `SharedPackageCensusTests` | `CredentialCensusTests`, `Adr038TestBanGuardTests` | No — a census asserts one enumerated set; folding a second subject in breaks per-file locality. Reuses `SourceScan`. | Package #16 lands silently; ADR-012's amendment rule stays unenforceable |
| Revision-header standard + census (FR-10) | `CredentialCensusTests`, `SharedPackageCensusTests`; the two incumbent header conventions | No — those assert a *package list*; this asserts *header presence and shape across 230 files*. Different failure message. **The two incumbent conventions are not extensible into each other**: skills cannot drop frontmatter (Claude Code parses it), so the blockquote cannot be the standard. | FR-12's ranking cannot parse two conventions, so it reads 181 of 230 files as undated when only ~9 truly are — and ranks them all as maximally stale |
| Nightly workflow (FR-12) | `nightly-health.yml`, `adr-audit.yml` | **Considered, rejected** — folding a governance report into `nightly-health`'s issue mixes audiences and dilutes a channel that gets read | Four specified cadences have already failed to fire. A fifth is the predictable outcome. |
| Usage hook (FR-11) | `post-edit-lint.sh`, `task-quality-gate.sh` | **Yes — same mechanism, new matcher.** No new capability. | FR-12's ranking degenerates to a calendar, which produced the 55-file wall |
| Export index (FR-16) | `spaarke-components-inventory.json` | No — that indexes Dataverse components from a live environment; this indexes TS exports from barrels | FR-17 and FR-18 both cite a rung that does not exist |
| Arch tests (FR-07) | 35 files in `tests/Spaarke.ArchTests/` | **Yes — this IS the extension.** Same project, primitive, CI wiring, audit workflow. | 42 of 49 ADRs stay silently violable |
| `review-checklist.md` (FR-21) | `spec.md`, `design.md`, POML tasks | **No, and it must not be authored** — it is a *projection* of those three; hand-writing creates a fourth thing to sync | FR-23 runs generically across 17 worktrees and produces findings nobody acts on |
| Nightly reviewer wiring (FR-23) | `scripts/quality/nightly-review-prompt.md` — **already written** | **Yes — pure wiring.** No new prompt, no new tool. | A complete reviewer stays unwired for a seventh month while three docs claim it runs |
| `.claude/constraints/reuse.md` (FR-20) | `bff-extensions.md`, CLAUDE.md §11 | No — §11 is the principle and must stay short (loads every turn); the ladder is procedure | The FR-17 ladder gets duplicated into two skills and drifts |

---

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-012** | *"Sanctioned shared packages … plus the domain component libraries (…, etc.). New siblings require an ADR amendment."* | The `"etc."` makes the sanctioned set unbounded while requiring an amendment for additions — self-defeating. The 2+ trigger also has no written evaluation. | **B (amendment)** | Pre-declared so it is not discovered mid-task. Amendment enumerates the 15 and records three evaluation questions. **Does not raise the trigger and does not un-package anything.** |
| **ADR-038** | Arch-fitness tests must pair positive + negative controls; no DI-resolution tests | None — r4 complies | **C (comply)** | The `SourceScan` discipline is exactly the right constraint for FR-02/07/10 |
| **Any ADR marked `stale` or `contested` by FR-05** | Unknown until classification | Discovered mid-project **by construction** — FR-05 exists to surface them | **B or C, per ADR** | Each gets its own §6.5 record rather than being silently enforced or silently skipped. **This is the point of FR-08.** |

---

## Success Criteria

1. [ ] The shared set is knowable — a 16th package fails the build, the message names the evaluation questions, **nothing existing was un-promoted** — *Verify*: add a synthetic 16th directory; test fails with the expected message
2. [ ] Every ADR is routed — 49 classified on three axes; `n/49` + breakdown + amendment count visible weekly; **nothing `stale` enforced** — *Verify*: read `adr-audit.yml`'s issue body
3. [ ] **The clock fires without a human** — a nightly issue exists, ranked by `drift × usage`, clean nights close it — *Verify*: the fifth written cadence is a job, not a paragraph
4. [ ] Docs cannot claim mechanisms that don't exist — every `*.yml` named in `docs/**` or `.claude/**` resolves — *Verify*: the check flags a deliberately-introduced phantom reference
5. [ ] Divergence is detectable — FR-18 returns `file:line` or nothing against a generated index; known divergences enumerated — *Verify*: seed a known-equivalent symbol; confirm the hit
6. [ ] Test growth is visible, ungated — skip count, flaky count, net delta reported; **no threshold added** — *Verify*: read the nightly issue
7. [ ] Exactly one new workflow — *Verify*: `git diff --stat .github/workflows/` shows one addition across the whole project
8. [ ] Every phase shipped standing alone — *Verify*: each merged as its own PR and delivered its capability without later phases
9. [ ] Complexity stayed bounded — *Verify*: each phase declared net new concepts; none exceeded one

---

## Dependencies

### Prerequisites

- Branch `work/code-quality-and-assurance-r4` exists and is pushed; **worktree not yet created**
- Draft PR [#935](https://github.com/spaarke-dev/spaarke/pull/935) open
- FR-04's fan-in and citation measurements **already taken** (2026-09-03) — P1 confirms rather than derives them
- No dependency on `sdap-ci.yml` retirement or the CI shadow window

### External

- GitHub Actions minutes for one nightly job (advisory tier)
- Claude Code headless execution in CI for FR-23 — the prompt exists; the runner does not

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Objective | Is the objective duplication? | **No.** Code quality and efficiency; duplication is an indicator. The two real risks are rebuilding what exists and the same function in two code paths. | Workstream 1 reframed; FR-18/FR-19 became central; former F-3 cut |
| Promotion trigger | Raise ADR-012's 2+ to three consumers? | **No.** A count is arbitrary; 2+ is a good trigger to **evaluate**. Anticipatory promotion is legitimate. | FR-03 records three questions, none a gate |
| Existing packages | Retroactively justify or un-promote any of the 15? | **No.** They are in the shared library and they stay. | FR-01 is enumeration, not cleanup |
| Reviewer agent | Build a `duplication-reviewer` sub-agent? | **No.** Reviewer agents produce differences of opinion without substantive value. | FR-18 is retrieval against an index; dropped from r4 and r5 |
| Scope | Minimum viable cut, or the whole thing? | **Full P1–P5**, each an independently valid stopping point. | NFR-01 |
| Batch sizing | F-9 batch by count or budget? | **Neither** — a functional criterion. | FR-07 |
| `task-execute` edit | Include or defer the Step 11 hook? | **Include** — but the three-tier auto-bump made it moot. | FR-13 removed the highest-collision edit |
| Project structure | One project or split? | **One project, two workstreams** — the r3 precedent. | Single worktree |
| Staleness threshold | 120 days? | **Replaced** by `drift × usage` ranking. | FR-12a |
| Tests | How do we manage the suite? | Make growth and silencing **visible**, never gated; leave the 68 skips to #794. | FR-25, FR-26 |
| **Revision tracking** (2026-09-04) | Is "a parseable `last-reviewed`" enough? | **No.** We need **one standard way to date and revision-track our files** — at the top, human readable, carrying more than a date (revision type, version number). It is a large number of files, but it should run **through a script with regex or other matching**, so it is not really a lot of LLM work. | **FR-10 rewritten** (§P3): a published standard + `Update-DocHeader.ps1` + a scripted backfill + census. Tasks 030–033. |
| **Execution posture** (2026-09-04) | Operator-gate every wave? | **No.** Run **autonomous** as long as it is safe and accurate; stop only for a true decision or a significant issue that genuinely needs operator direction. | plan.md §3.5; the `<escalation><trigger>` blocks already in each POML are the stop conditions |
| **Scope trim** (2026-09-04) | Full P1–P5, or defer P4/P5 to r5? | **Full P1–P5, with P2 split** — P2a classifies; P2b's arch tests are sized by task 020, then decomposed by re-running `/task-create`. | Resolves Unresolved Questions 1 and 3 below |

---

## Assumptions

- **Checklist filename**: assuming `review-checklist.md` (input) over the owner's original `code-review.md` (reads like output). Trivially changed before P5.
- **Doc corrections**: assuming FR-24 lands inside P5 rather than as a separate filing.
- **Citation counts as blast-radius proxy**: assuming FR-04's counts are the right prioritiser for FR-07. Puts ADR-021 (3,061, no test) and ADR-028 auth (1,521, no ADR-named test) near the front — though ADR-028 may already be effectively enforced by the unnamed auth guards, which FR-05 will determine.
- **Usage-weight thresholds**: assuming terciles from the FR-04 baseline, tuned after two months of output.
- **Nightly cadence**: assuming nightly (matching `nightly-health.yml`) rather than weekly for FR-12.

---

## Unresolved Questions

- [x] **Scope trim** — ~~should P4/P5 defer to r5?~~ **RESOLVED 2026-09-04 (owner)**: full P1–P5, no deferral. Each phase remains an independently valid stopping point per NFR-01.
- [ ] **FR-23 runner** — Claude Code headless in GitHub Actions is assumed available; the auth/runner mechanics are unverified. — **Blocks**: P5 only. **Task 052 is the probe**; if it fails, P5 drops FR-23 and does *not* build an alternative.
- [x] **FR-07 set size** — ~~unknown until FR-05 completes~~ **RESOLVED 2026-09-04 (owner) by splitting P2**: P2a (tasks 010–013) classifies; task **020** emits the criterion-bounded set and reports its size; P2b is then decomposed by re-running `/task-create`. The 33 tasks generated at pipeline time therefore exclude P2b by design.

---

## Scope Estimate

**Derived per phase, honestly.** The uncertainty is concentrated in FR-07, whose size is discovered by FR-05.

| Phase | Deliverables | Est. tasks | Elapsed |
|---|---|---|---|
| **P1** | ADR-012 amendment; census test; baseline report | **3–4** | Days |
| **P2** | Classify 49 (fan-out candidate); arch tests for the criterion set; issue-body edit; classification-guard census | **8–14** | The variable phase |
| **P3** | Revision-header standard + `Update-DocHeader.ps1` + scripted backfill + census; usage hook; nightly workflow + ranking script; auto-bump; point-of-use line | **9** | — |
| **P4** | Index generator + wiring; escalation ladder; equivalence check; divergence register; `constraints/reuse.md` | **6–7** | — |
| **P5** | Checklist generator + integration; prompt wiring; prompt refresh; 3 doc fixes; test-health section; KEEP directive; knip | **8–9** | — |
| | **Total** | **~32–42** | |

**Honest flag, as requested.** r3 ran 35 tasks; `ci-cd-unit-test-remediation-r1` ran 45. **r4 at full scope is an average-sized project for this repo, not a small one** — and the design grew from 7 findings to 13 across review. Each individual item got *smaller*; the count went *up*.

What makes it survivable is NFR-01: **P1 alone is 3–4 tasks and ships in days**, and every phase after it is a clean stopping point. The commitment is one phase at a time, not 40 tasks.

Three genuine trims if the total is still too large:

1. **Split P2** into P2a (classify + accuracy, ~4 tasks) and P2b (write the tests, N tasks). Removes the single biggest estimate uncertainty from the commitment.
2. **Defer P4 + P5 to r5** → r4 = P1+P2+P3 ≈ **18–26 tasks**, delivering both top-priority gaps (know the enforcement denominator; make the clock fire).
3. **Defer P5 only** → r4 ≈ **24–33 tasks**, keeping the reuse-gate work but leaving the nightly reviewer for r5.

**Recommendation**: option 1 alone. P4 and P5 carry the owner's stated objective (don't rebuild / don't diverge) and the six-month-unwired reviewer — deferring them defers the point of the project.

---

*AI-optimized specification. Original design: [`design.md`](design.md).*
