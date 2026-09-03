# Design — Code Quality & Assurance R4 — Governance That Enforces Itself

> **Status**: Design draft (pre `/design-to-spec`)
> **Created**: 2026-09-02 · **Rescoped + renamed** 2026-09-03 (was `reuse-governance-tightening-r1`)
> **Origin**: Owner-supplied `reuse-duplication-action-items.md` (Sept 2026 external synopsis), assessed against real repo state 2026-09-02; widened 2026-09-03 after the [AI-native development model assessment](../../docs/assessments/ai-native-development-model-2026-09-03.md).
> **Character**: **Small and phased. No new subsystem.** Every phase is independently mergeable and ships value on its own. If a phase starts growing a subsystem, it has left scope.
> **Lineage**: r1 (quality *system*) → r2 (first structural remediation) → r3 (multi-surface program, ✅ complete 2026-08-14, forcing-functions live) → **r4 (the governance layer itself)**.
> **Parent frame**: [`docs/assessments/ai-native-development-model-2026-09-03.md`](../../docs/assessments/ai-native-development-model-2026-09-03.md) — owns the taxonomy, the principles, and the full gap register. This design owns the subset r4 executes.

---

## 0. Premise

**r3 hardened the code. r4 hardens the layer that governs the code.**

r3 proved the pattern works: it left behind live forcing-functions — ArchTests, analyzers-as-errors, config fail-fast, a naming gate — so that quality *holds* as the codebase grows. r4 applies the same move one level up. Our governance surface (49 ADRs, 16 constraints, 94 patterns, 71 skills) is authored well and enforced thinly, and it decays because nothing fires when it does.

The organizing principle, from the parent assessment:

> **Documentation must be load-bearing, not descriptive.** A doc something depends on fails loudly. A doc that only describes drifts silently.

Two workstreams, both instances of that principle:

| # | Workstream | Premise |
|---|---|---|
| **1 — Reuse & shared surface** | F-1 … F-8 | The reuse gate works, but the *shared surface it governs is unbounded* (§0.1) |
| **2 — Enforcement & continuity** | F-9, CU-1 … CU-3 | Rules we already wrote are unenforced (7/49) and unmaintained (55/71 skills stale ≥3 months) |

### 0.1 Workstream 1 premise (owner-confirmed)

The origin document assumed Spaarke's failure mode is **under-reuse** — agents fail to find prior art, so they duplicate. Repo evidence does not support that premise, and the owner has confirmed the correction.

What the evidence actually shows:

| Evidence | Finding |
|---|---|
| 448 POML tasks carry a `<justification>` element with concrete, citing answers | The reuse gate exists and is **working**, not rubber-stamping |
| [task-create/SKILL.md:308](../../.claude/skills/task-create/SKILL.md#L308) already requires `<existing>` to cite `file:line` from Grep evidence | "Record the queries, not just the conclusion" is **already the rule** |
| [code-review/SKILL.md:729](../../.claude/skills/code-review/SKILL.md#L729) flags `"None" with no grep evidence → WARNING` | The gate is **enforced at review time**, not just authored |
| **15** `@spaarke/*` packages in [src/client/shared/](../../src/client/shared/) under an ADR-012 that claims a single source of truth | The live risk is **over-promotion**, not under-reuse |

**Therefore workstream 1's controls push in the direction the evidence points**: bound the shared surface, sharpen absence-assertions, and make promotion a deliberate decision. It does **not** build machinery to find more things to share.

### 0.2 Workstream 2 premise — we have declared this cadence twice already

The decay is not from never having thought about it. **Two prior projects each declared a recurring cadence, in writing, and neither converted into recurring work:**

| Declaration | Where | What actually happened |
|---|---|---|
| *"Cadence after launch: monthly proactive audit; per-commit validation; quarterly deep audit"* | [`projects/ai-procedure-quality-r1/README.md`](../ai-procedure-quality-r1/README.md) | 55 of 71 skills still carry that project's own 2026-05/06 review stamp. No monthly audit has run. |
| *"**Cadence**: Monthly on the first business day"* | [`knowledge/REFRESH-PROCEDURE.md`](../../knowledge/REFRESH-PROCEDURE.md) | [`REFRESH-LOG.md`](../../knowledge/REFRESH-LOG.md) shows 3 refreshes in 4 months, every one triggered by a project needing the content — never by the calendar. |

Both declarations are prose with no mechanism — **the exact failure the load-bearing principle names.** So workstream 2's premise is not "we should adopt a cadence." It is:

> **A third written cadence would fail the same way. The deliverable is a mechanism that fires without a human remembering.**

That is CU-1, and it is why CU-1 must be a scheduled job producing a rolling issue — the shape already proven by [`nightly-health.yml`](../../.github/workflows/nightly-health.yml) and [`adr-audit.yml`](../../.github/workflows/adr-audit.yml), both of which *do* run.

### 0.3 The three documentation classes (compact)

Full table, evidence, and repo-wide status live in the [parent assessment §2.5](../../docs/assessments/ai-native-development-model-2026-09-03.md). r4 needs only the routing rule and the class labels used in the findings below:

| Class | Drift control | r4 findings |
|---|---|---|
| **1 — Derivable** (inventories, export surfaces) | **Generate it. Never hand-write it.** | F-8 |
| **2 — Non-derivable** (rationale, rejections, invariants, scars) | **Make it executable** — an ADR with an arch-test cannot silently reverse | F-1, F-4, **F-9**, CU-2 |
| **3 — Navigational** (intent → location) | **Validate the paths** — a broken pointer fails loudly | CU-1 (link check) |

> **Routing rule, binding on every document this project writes**: *Can the code answer this question?* **Yes** → generate it, or don't write it. **No** → write it, and pin it to a mechanism whose failure mode is a red build.

Class 2 is the durable investment — it survives any increase in model context or retrieval quality, because the information was never in the repo to be found. That is why **F-9 (P2) ships well ahead of F-8 (P4)**, even though both are single-phase capabilities.

---

## 1. Scope

### 1.1 Goals

Listed by workstream. **Shipping order is by capability, not by workstream** — see §3, where the two workstreams interleave.

**Workstream 2 — enforcement & continuity** (the highest-value goals in the project):

1. Turn **7/49 ADR enforcement** into a known denominator, then enforce every ADR that meets the silent-and-expensive criterion (**F-9** → P2).
2. Build the mechanism that **converts elapsed time into a work item** — the thing two prior written cadences failed to do (**CU-1** → P3).
3. Make review stamps **assertable** rather than decorative (**CU-2** → P3).
4. Give procedure maintenance a home in **work that already happens**, so the report in goal 2 has something that clears it (**CU-3** → P3).

**Workstream 1 — reuse & shared surface**:

5. Make the sanctioned shared-package set **enumerable and enforced**, with promotion criteria that an agent meets or trips (**F-1 + F-4** → P1).
6. Establish a **baseline** so we can tell whether any of this changed behavior (**F-6** → P1).
7. Generate the **shared-lib export index** F-2's escalation ladder depends on — Class 1, minimum scope (**F-8** → P4).
8. Close the one real weakness in the existing reuse gate: **grep-only absence assertions** (**F-2** → P4).
9. Give the gate a **`promote` verdict** so "this is the Nth occurrence" has somewhere to go (**F-3** → P4).
10. Produce **evidence** on functional-equivalence duplication and dead exports, so r5 is a decision rather than an assumption (**F-5, F-7** → P5).

### 1.2 Non-goals (explicit scope fence)

These were assessed and **rejected or deferred** with reasons. They are listed so a later task does not silently re-adopt them.

| Rejected | Reason |
|---|---|
| Generated "capability manifest" document | 940 BFF service files + 46 PCF + 37 solutions. A manifest that large is context-bloat an agent will not read — it re-creates the retrieval failure at a different layer. Replaced by F-1's **scoped census**. |
| `ast-grep` + `sgconfig.yml` for C# | [tests/Spaarke.ArchTests/SourceScan.cs](../../tests/Spaarke.ArchTests/SourceScan.cs) already provides structural source scanning, in the **blocking** CI tier, in a language we maintain, with a stated negative-control discipline. Adding ast-grep for C# duplicates a working primitive. |
| `SonarAnalyzer.CSharp` / Roslyn duplicate analyzer | [Directory.Build.props](../../Directory.Build.props) sets `TreatWarningsAsErrors=true` repo-wide with a one-code allowlist. A new analyzer turns every finding across 940 service files into a **build error on day one**. |
| `jscpd` threshold as a hard gate | We already ran this experiment. The God-class LOC ratchet was **retired 2026-08-20** ([COMPONENT-COMPLEXITY.md](../../docs/standards/COMPONENT-COMPLEXITY.md)) because it gated on a count-proxy for a judgment question and blocked normal work on active files. A duplication-percentage gate is the same instrument class. |
| New `<reuse-evidence>` POML block | Would be a parallel system alongside a working `<justification>`. Extend, per CLAUDE.md §11 applied to our own procedures. |
| `duplication-reviewer` sub-agent | See §4 — the premise it rests on is factually wrong about this repo, and the underlying concern is better tested inside `code-review` first. Deferred to r5 pending F-5 evidence. |
| LSP/semantic-index MCP sidecar | ~17 active worktrees + agent worktrees ⇒ not one stale index but N, or one index confidently answering about the wrong tree. |
| Repo-wide reuse/duplication audit | Operationally impractical at this scale, same as the test audit. Bounded consumption only (F-3 ledger). |
| **Re-adopting anything already rejected by a predecessor** | Four projects have now worked this same procedure surface: [`ai-procedure-refactoring-r1`](../ai-procedure-refactoring-r1/), [`-r2`](../ai-procedure-refactoring-r2/), [`ai-procedure-quality-r1`](../ai-procedure-quality-r1/), and r4. **Before adding any control not listed in §2, check those three for a prior rejection** — the 2026-05-17 CLAUDE.md rewrite and the AIP-00x protocol-layer removal both deleted machinery on purpose. Re-adding it silently is the failure this row exists to prevent. |

---

## 2. Findings

Findings are grouped by workstream for readability. **They do not ship in this order** — §3 sequences by capability, and the two workstreams interleave (P1 is workstream 1, P2–P3 are workstream 2, P4–P5 return to workstream 1).

### Workstream 1 — Reuse & shared surface

### F-1 — The sanctioned shared-package set is unenumerable 🔴 (sharpest finding in workstream 1)

[ADR-012](../../.claude/adr/ADR-012-shared-components.md) states:

> **Sanctioned shared packages (as of this amendment)**: `@spaarke/ui-components`, `@spaarke/visuals`, `@spaarke/auth`, plus the domain component libraries (`@spaarke/events-components`, etc.). **New siblings require an ADR amendment.**

The `"etc."` is load-bearing and self-defeating: the ADR requires an amendment for new siblings while making the sanctioned set unbounded. Enumerated against reality:

**Named in ADR-012 (4):** `@spaarke/ui-components`, `@spaarke/visuals`, `@spaarke/auth`, `@spaarke/events-components`

**Covered only by "etc." (11):** `@spaarke/ai-context`, `@spaarke/ai-outputs`, `@spaarke/ai-widgets`, `@spaarke/communication-components`, `@spaarke/compose-components`, `@spaarke/daily-briefing-components`, `@spaarke/document-operations`, `@spaarke/legal-workspace`, `@spaarke/notifications`, `@spaarke/sdap-client`, `@spaarke/smart-todo-components`

**This is not a claim that the 11 are wrong.** Several are clearly justified. The claim is that we cannot currently tell, because there is no list to check against and no mechanism that fires when a 16th appears.

**Fix**: enumerate all 15 in ADR-012 with a one-line reason each, then add `SharedPackageCensusTests` to [tests/Spaarke.ArchTests/](../../tests/Spaarke.ArchTests/) asserting the set of `src/client/shared/*/package.json` names against that list. Package #16 becomes a build failure that asks "why?" — exactly the [CredentialCensusTests](../../tests/Spaarke.ArchTests/CredentialCensusTests.cs) pattern, whose own docstring makes the argument:

> "A census makes the COUNT itself the assertion, so the next miss is a build failure instead of a discovery two projects later."

**Cost: one test file + one ADR edit. Zero CI workflow change** — it rides the existing ArchTests wiring in `ci-tier1-blocking.yml` and `adr-audit.yml`.

### F-2 — Absence assertions are grep-only 🟡

The gate requires grep evidence, which is correct as far as it goes. But a lexical miss and a genuine absence are indistinguishable in the current output, and short conceptual queries are the weakest possible retrieval input. A grep for `"summar"` that misses `DigestBuilder` produces a confident, evidence-bearing, **wrong** "none found."

**Fix**: add an escalation requirement — if the lexical pass returns nothing, you must escalate (shared-package export index → symbol/type search) **before** `<existing>` may say "none," and record which passes ran. One rule, authored once in `constraints/reuse.md`, referenced from both skills.

### F-3 — There is no `promote` verdict 🟡

`<extension>` is binary yes/no. A task that is the third independent implementation of the same shape has nowhere to say so — it answers "No, cannot extend" truthfully and proceeds, and the promotion signal is lost.

**Fix**: add a third state that records the occurrence count and files a ledger entry against the existing [adr-audit.yml](../../.github/workflows/adr-audit.yml) tracking-issue mechanism (findings → idempotent GitHub issue, already built). Consumed at a bounded quota, per the test-diet precedent — never exhaustively.

### F-4 — ADR-012 promotion trigger is 2 consumers, with no qualitative test 🟡

ADR-012 "When to Add to Shared Library" triggers at **"Used by 2+ modules/surfaces"** with no further criteria. 15 packages is what a 2-consumer trigger with no qualitative gate produces.

**Fix (CLAUDE.md §6.5 Path B — amendment, not new ADR)**: raise the trigger to a **third independent** occurrence and add three qualitative tests, all of which must hold: API stable across all three consumers with **no consumer-specific branching**; testable in isolation without consumer fixtures; duplication is **semantic, not coincidental**. Anything failing stays duplicated and gets **recorded** (F-3), not promoted.

> ⚠️ **This conflicts with a live ADR and must be handled as an amendment.** Writing it as a new ADR would silently override ADR-012.

### F-5 — `code-review` reviews code its own context just wrote 🟡

At Step 9.5, `code-review` runs **inline in the session that just produced the code**. That is structurally the "reviewers under-flag plausible redundancy in code they saw produced" failure mode, baked into our gate.

Correcting the origin document's premise: `.claude/agents/` contains exactly one file, `researcher.md`. **There are no specialist reviewer sub-agents.** `code-review` and `adr-check` are skills, not fresh-context agents. There is no existing contract for a `duplication-reviewer` to match — adopting it would mean establishing that pattern from scratch.

**Fix (r4)**: add a scoped **functional-equivalence check** to `code-review` — for each new exported symbol, is it functionally equivalent to a named existing capability despite different implementation? **Advisory severity only.** If F-6 shows it finds nothing over a project's worth of PRs, the fresh-context sub-agent (r5) is unjustified and we drop it. If it finds real hits, we have the evidence to build it properly.

### F-6 — No baseline 🟡

Every proposed control here is a behavior change with no before-measurement. Ships first, as Task 001.

**Measure** (all cheap, all from artifacts we already have): `<extension>` yes/no ratio across the 448 justifications; count and quality of `<existing>none` answers (**53 today**); import fan-in per `@spaarke/*` package.

Fan-in is the load-bearing one: it tells us which of the 15 packages are actually consumed and which were promoted into disuse — the direct test of whether over-promotion is real.

### F-7 — No dead-export visibility across 15 shared packages 🟢

**Fix**: `knip`, **observation-only, exit 0**, scoped to the 15 shared libs.

> **Cost correction**: there are **no npm workspaces** — root `package.json` has no `workspaces` field and neither do the libs. knip's monorepo auto-discovery does not apply; this is 15 per-package configs. That is why it is scoped to the shared libs only and **not** extended to 37 solutions + 46 PCF controls.

> **Gotcha for any repo-wide scanner**: [.claude/worktrees/](../../.claude/worktrees/) contains full source-tree copies (`agent-*/src/server/api/Sprk.Bff.Api/...`). Plus ~17 active sibling worktrees per [projects/INDEX.md](../../projects/INDEX.md). Any duplication or dead-code scan must exclude these or report enormous phantom findings.

### F-8 — F-2's escalation ladder has no rung to escalate *to* 🟡 (Class 1)

F-2 requires an agent to escalate past a failed lexical grep before writing `<existing>none`. The named next rung is a "shared-package export index." **It does not exist.** Today an agent that greps `"summar"` and misses `DigestBuilder` has nothing further to consult — F-2 is unimplementable as written.

This is a Class-1 artifact, so the §0.5 routing rule binds: **generate it, never hand-write it.** The precedent and its lesson are both in [spaarke-components-inventory.json](../../docs/data-model/spaarke-components-inventory.json) — machine-extracted, 3,042 components across 70 solutions, nobody hand-maintains it, and it is a **snapshot, not a living artifact** — one commit (`809ae3477`, 2026-08-20) and no regeneration wiring in any workflow or script. Repeating the snapshot mistake is the main risk here.

**Fix**: generate an export index from the 15 `src/client/shared/*` barrel files (`src/index.ts`) — symbol name, kind, package, source path. **Minimum scope, deliberately:**

- **Shared libs only.** Not 940 BFF service files, not 37 solutions, not 46 PCF controls. Those were rejected in §1.2 as context-bloat and that rejection stands.
- **Regeneration is wired, or it doesn't ship.** A script nobody runs is worse than no index, because F-2 would then cite a stale authority. Because **CU-1 lands in P3**, F-8 inherits its wiring for free: regenerate inside CU-1's weekly job and report a diff as a finding in the existing rolling issue. No new workflow, no new schedule — the "Class-1 artifacts whose regeneration produces a diff" bullet in CU-1 is written for exactly this. *(Stated economy, not hidden dependency — see P4 §3.)*
- **Consumed by agents, not read by humans.** Sizing constraint: it must be greppable in one pass. If it grows past that, the scope is wrong.

**Class-1 discipline check**: nothing in this index is hand-authored. If a task ever edits it by hand, the generator is broken and that is the bug to fix.

---

### Workstream 2 — Enforcement & continuity

### F-9 — ADR enforcement coverage is 7/49 🔴 (Class 2 — highest durable value in the project)

**Measured 2026-09-03.** [tests/Spaarke.ArchTests/](../../tests/Spaarke.ArchTests/) contains 35 test files. Of the **49** concise ADRs in [.claude/adr/](../../.claude/adr/), exactly **7** carry a named architecture-fitness test:

| Enforced | Test file |
|---|---|
| ADR-001 Minimal API | `ADR001_MinimalApiTests.cs` |
| ADR-002 Thin plugins | `ADR002_PluginTests.cs` |
| ADR-007 SpeFileStore | `ADR007_GraphIsolationTests.cs`, `ADR007_NestedDomainRecordTests.cs` |
| ADR-008 Endpoint filters | `ADR008_AuthorizationTests.cs` |
| ADR-009 Redis caching | `ADR009_CachingTests.cs` |
| ADR-010 DI minimalism | `ADR010_DITests.cs` |
| ADR-013 AI architecture | `ADR013_AiBoundaryTests.cs`, `ADR013_ComposeFacadeTests.cs`, `ADR013_LinearConsumerBoundaryTests.cs` |

Plus `Adr038TestBanGuardTests.cs` (ADR-038 lives in `docs/adr/`). **The other 42 are prose that can be violated silently.**

Two things this measurement does *not* say. First, enforcement is broader than the ADR-named count: ~18 further guard tests encode invariants without an ADR number (`CredentialGuardTests`, `RouteAuthorizationGuardTests`, `FabricatedResultGuardTests`, `LayerDependencyTests`, `TenantIsolation/I1–I6`, …). Second, **not every ADR is mechanically enforceable** — some are genuinely judgment rules, and manufacturing a test for one is the God-class-ratchet mistake in a new costume.

The enforced seven are a complete, proven loop: ADR → executable rule → blocking [ci-tier1-blocking.yml](../../.github/workflows/ci-tier1-blocking.yml) → weekly [adr-audit.yml](../../.github/workflows/adr-audit.yml) auto-filing an idempotent tracking issue. **The mechanism is built and working. Only its coverage is thin.** That is why this is the highest-value item in the project: it is the durable class, the marginal cost per ADR is one test file, and there is no new subsystem to design.

**Fix — bounded, three parts, in order:**

1. **Classify all 49** into `enforceable` / `partially-enforceable` / `judgment-only`, one line of reason each, recorded in [.claude/adr/INDEX.md](../../.claude/adr/INDEX.md). **This is the whole deliverable of part 1** — classification alone converts "42 unenforced" from an unknown into a known, and tells us the real denominator.
2. **Write tests for a bounded batch** of the `enforceable` set, prioritized by blast radius (an ADR whose violation is silent and expensive outranks one whose violation is obvious at review). **Batch size is set after part 1, not now** — proposing a number before the classification exists would be the same unfounded-target error this project is correcting elsewhere.
3. **Make coverage a tracked number.** Add it to the F-6 baseline and to `adr-audit.yml`'s issue body, so `7/49 → n/49` is visible without anyone running an audit.

> ⚠️ **Explicit non-goal: "write 42 arch tests."** That is a different project, and a bad one — it would force tests onto judgment-only ADRs. The deliverable is *classification plus a prioritized batch*, and the classification is what makes the batch defensible.

### CU-1 — Nothing converts elapsed time into a work item 🔴 (the §0.2 finding)

Two scheduled jobs run today ([`nightly-health.yml`](../../.github/workflows/nightly-health.yml), [`adr-audit.yml`](../../.github/workflows/adr-audit.yml)); **both watch code, neither watches the governance surface.** Everything that maintains `.claude/` fires only when a human notices or a project happens to touch that area. §0.2 shows what that produces: two written cadences, zero recurring runs.

**Fix**: one weekly job → **one rolling advisory issue**, listing:

- review stamps older than 120 days, and files carrying no stamp at all
- **broken pointer paths** across `.claude/**` and `docs/**` (the Class-3 control; also closes the missing `Find-SkillReferenceDrift.ps1` that [`ai-procedure-maintenance`](../../.claude/skills/ai-procedure-maintenance/SKILL.md) cites as a deliverable but which does not exist)
- Class-1 artifacts whose regeneration produces a diff
- ADRs still unclassified by F-9 part 1

**Advisory, never blocking.** Clean weeks close the issue; that is the `nightly-health` contract and it works. The specific thing to *not* do is add a gate — see the God-class ratchet in §1.2.

### CU-2 — Review stamps are decorative 🟡

63 of 71 skills carry `last-reviewed` (one is a literal `YYYY-MM` placeholder). **0 of 16 constraints and 0 of 94 pattern files carry any stamp at all** — so for 110 of the 181 governance files, CU-1 would have nothing to measure.

**Fix**: a census test asserting every `.claude/skills/*/SKILL.md`, `constraints/*.md`, and `patterns/**/*.md` carries a parseable `last-reviewed`. Adding an unstamped file fails the build. Same file, same primitive, same discipline as F-1 — [`CredentialCensusTests`](../../tests/Spaarke.ArchTests/CredentialCensusTests.cs): *"a census makes the COUNT itself the assertion."*

> **CU-2 is a prerequisite of CU-1's first bullet**, not an independent nicety. Stamps that don't exist cannot go stale.

### CU-3 — Procedure maintenance has no home in work that already happens 🟡

`/test-diet` proved the project-close hook pattern: at `090-wrapup-*`, reconcile something against a standard, emit reviewer-actionable output, don't auto-execute.

**Fix**: extend the same gate — for every `.claude/` file the project touched, bump the stamp and run [`doc-drift-audit`](../../.claude/skills/doc-drift-audit/SKILL.md) **on the project's diff** (it is already designed to be diff-based, which is why this is cheap). Turns per-project work into procedure maintenance with no separate project and no new skill.

> ⚠️ **Collision risk**: this edits `task-execute` Step 11 — the highest-collision file in the repo across ~17 active worktrees. `/conflict-check` is mandatory immediately before P3 starts **and again before merge**.

> **CU-3 is not optional and not deferrable.** CU-1 without CU-3 is a smoke alarm with no extinguisher: the weekly issue would report ~110 newly-stamped files aging past threshold with nothing that ever clears them. See P3 in §3.

---

## 3. Phases

### 3.0 What a phase is (binding definition)

**A phase is a capability, not a batch of tasks.** Each of P1–P5 must satisfy all three tests, and a phase that fails any of them is mis-scoped and must be re-cut before task-creation:

| Test | Meaning |
|---|---|
| **Deployable** | It merges to master and can be left there **indefinitely** with no follow-on phase. Nothing it ships is inert until a later phase arrives. |
| **Functionally complete** | The capability it names works **end-to-end** — including the case that proves it fires. A rule with no enforcement, or an enforcement with no message telling the reader what to do, is not complete. |
| **Independently valuable** | If r4 stopped the day this phase merged, this phase would still have been worth doing on its own terms. |

**Sizing rule — completeness, not count or budget.** A phase is sized by *what makes its capability whole*. Where a phase covers an enumerated set (P2's arch-test batch), the set is bounded by a **functional criterion**, and the phase is not done until every member of that set is covered — whether that turns out to be four items or fourteen. **No phase is sized by a task count, a wave, or a time box.**

**No phase is a pre-requisite-only phase.** Where a later phase happens to reuse something an earlier one built (P4's generator reuses P3's weekly job), that is *economy*, not dependency — and it is stated explicitly in the phase so the reuse is a deliberate decision rather than a hidden coupling.

| Phase | Capability delivered | Contents | Hot paths |
|---|---|---|---|
| **P1** | The shared surface is bounded | F-1, F-4, F-6 | none |
| **P2** | ADR enforcement is known, enforced, and measured | F-9 (all parts) | `ci-workflows` (edit only) |
| **P3** | The governance surface maintains itself | CU-2, CU-1, **CU-3** | `ci-workflows` (**one** new), `skill-directives` |
| **P4** | The reuse gate can escalate and record | F-8, F-2, F-3, `constraints/reuse.md` | `skill-directives`, `root-claude-md` |
| **P5** | Evidence for r5 | F-5, F-7 | `skill-directives` |

---

### P1 — The shared surface is bounded

**Capability**: a 16th `@spaarke/*` package cannot enter the repo without meeting stated promotion criteria and amending ADR-012.

**Contents**: F-1 (enumerate all 15 + `SharedPackageCensusTests`), F-4 (ADR-012 amendment — trigger raised to a third independent occurrence + the three qualitative tests), F-6 (baseline).

**Complete when**:
- All 15 packages enumerated in ADR-012, one line of reason each — no `"etc."` remains
- `SharedPackageCensusTests` passes on the 15 and **fails on a synthetic 16th**, with positive *and* negative controls per the `SourceScan` discipline (ADR-038)
- **The failure message names the promotion criteria and the amendment requirement** — an agent that trips it knows what to do without opening the ADR. *This is the difference between complete and merely present.*
- F-6 baseline published: `<extension>` yes/no ratio across the 448 justifications, `<existing>none` count (53 today), per-package import fan-in, and `<escalation><trigger>` firing count

**Why it stands alone**: this is the entirety of findings F-1/F-4 and gap G10. Nothing downstream is needed for the gate to work the day it merges.

**Why first**: it is the smallest complete capability in the project, it touches **no workflow and no skill** (the only phase requiring no `/conflict-check`), and it is the reference implementation of two patterns the later phases copy — the census test (P3's CU-2) and the ADR-amendment-with-enforcement pairing (P2).

---

### P2 — ADR enforcement is known, enforced, and measured

**Capability**: we know which of the 49 ADRs can be mechanically enforced; the ones whose violation is *silent and expensive* **are** enforced; and coverage is reported weekly without anyone running an audit.

**Contents**: F-9, all three parts.

**Sizing — the functional criterion (this replaces "pick a batch size")**: the test batch is **every ADR classified `enforceable` whose violation is both *silent* (will not surface at code review or at runtime) and *expensive* (security, tenant isolation, auth, data integrity, or a cross-layer boundary).** That set is discovered by part 1, not chosen now. **The phase is not done until every member of it has a test** — four or fourteen, the criterion is the same. ADRs that are `enforceable` but whose violation is loud or cheap are recorded and left for r5; forcing them in would dilute the phase without completing anything.

**Complete when**:
- All 49 ADRs classified `enforceable` / `partially-enforceable` / `judgment-only` in `.claude/adr/INDEX.md`, one line of reason each
- Every ADR meeting the silent-and-expensive criterion has an arch-test with positive + negative controls
- `adr-audit.yml`'s issue body carries `n/49` plus the classification breakdown, so coverage is visible weekly without running an audit
- **A new ADR added without a classification fails the build.** Without this, part 1 is a snapshot that decays the moment ADR-052 lands — the same mistake as the 2026-08-20 inventory. *This is what makes P2 self-sustaining rather than a one-time census.*

**Why it stands alone**: the highest-value item in the project delivered whole — classification, enforcement of the set that matters, weekly measurement, and a guard that keeps all three current.

**Hot paths**: `ci-workflows=Y`, but **edit-only** — the issue body of an existing workflow. No new workflow, no skill file. Light `/conflict-check`.

---

### P3 — The governance surface maintains itself

**Capability**: two halves of one thing — the surface **reports its own decay** weekly without a human remembering, and every project that touches a governance file **maintains it as part of closing**.

**Contents**: CU-2 (stamp census) → CU-1 (weekly report + link check, closing G3/G12) → **CU-3** (`task-execute` Step 11 hook). In that order.

**Why CU-3 is in this phase and not deferred** — this is the functional-completeness argument, not a scheduling preference: CU-1 without CU-3 is **a smoke alarm with no extinguisher.** The weekly issue would report 110 newly-stamped files aging past 120 days with no mechanism that ever clears them, and within two months it becomes a wall of known-stale entries nobody reads — the precise failure mode of the two dead cadences in §0.2. CU-3 is what converts a report into a loop. **A phase shipping CU-1 without CU-3 would fail the "functionally complete" test.**

**Complete when**:
- Every `.claude/skills/*/SKILL.md`, `constraints/*.md`, and `patterns/**/*.md` carries a parseable `last-reviewed`; the census **fails on a new unstamped file**. (~110 files gain a first stamp: 0/16 constraints, 0/94 patterns, plus 8 unstamped skills and the one `YYYY-MM` placeholder.)
- The weekly job files **one rolling advisory issue** — stamps >120 days, unstamped files, broken pointer paths, Class-1 regeneration diffs, unclassified ADRs — and a clean week closes it
- The link check resolves every path reference under `.claude/**` and `docs/**`; `ai-procedure-maintenance`'s citation of `Find-SkillReferenceDrift.ps1` is either **satisfied by this script or removed** (G12 — the current state is the only unacceptable one)
- `task-execute` Step 11: at `090-wrapup-*`, for every `.claude/` file in the project's diff → bump the stamp and run `doc-drift-audit` **on that diff**. Reviewer-actionable output, **never auto-executed** — the `/test-diet` contract.
- **Proof of life, both halves**: the first weekly issue lands carrying real findings, *and* one project closes through the new Step 11 hook and emerges with bumped stamps.

**Why it stands alone**: the strongest standalone case in r4. §0.2 establishes that this exact deliverable has been declared twice in prose and produced zero recurring runs. Shipping it is the whole point.

**Hot paths**: `ci-workflows=Y` — **the one new workflow in all of r4** — and `skill-directives=Y` for `task-execute` Step 11, **the highest-collision file in the repo** across ~17 active worktrees. ⚠️ `/conflict-check` **mandatory immediately before starting P3 and again before merge.** This is the phase where collision risk is real; it is worth its own coordination pass.

---

### P4 — The reuse gate can escalate and record

**Capability**: an agent whose lexical search finds nothing has a **real second rung** to escalate to before writing `<existing>none`; and a third independent occurrence of the same shape has **somewhere to be recorded** instead of being silently re-implemented.

**Contents**: F-8 (export index + generator) → F-2 (escalation ladder) → F-3 (`promote` verdict + ledger) → `constraints/reuse.md`, written last.

**Complete when**:
- The index is **generated** from the 15 barrel files — symbol, kind, package, source path — and greppable in one pass
- It **regenerates in CU-1's weekly job**, and a diff against the committed copy is reported as a finding. A hand-edit cannot land silently.
- `task-create` and `code-review` require a recorded escalation beyond the lexical pass before `<existing>` may say "none," and record **which passes ran**
- The `promote` verdict files a ledger entry through `adr-audit.yml`'s existing idempotent issue mechanism; consumption is quota-bounded per the `test-diet` precedent, never exhaustive
- `constraints/reuse.md` documents **shipped reality**, 100–200 lines per the [constraints/INDEX.md](../../.claude/constraints/INDEX.md) convention, and **points at** CLAUDE.md §11 and ADR-012 rather than restating them — a fourth home for reuse rules is the failure mode to avoid

**Why it stands alone**: F-2's ladder plus F-3's ledger is a working gate improvement on the day it merges, independent of P5.

**Stated economy (not a hidden dependency)**: F-8's regeneration rides P3's existing weekly job rather than adding a second workflow. If P3 were ever skipped, F-8 would need its own wiring — which would breach r4's one-new-workflow ceiling, and the correct response would be to ship P3 first, not to add the workflow.

**Hot paths**: `skill-directives=Y` (`task-create` Step 3.5.6, `code-review` Step 6.6), `root-claude-md=Y` (§11 pointer). `/conflict-check` mandatory.

---

### P5 — Evidence for r5

**Capability**: a defensible go/no-go on two open hypotheses — that functional-equivalence duplication is slipping past review, and that the 15 shared packages carry dead exports.

**Contents**: F-5 (functional-equivalence check in `code-review`, **advisory severity only**), F-7 (`knip`, **exit 0**, scoped to the 15 shared libs).

**Complete when**:
- F-5 runs on every PR through `code-review` Step 6.6, and its hits and misses are recorded against the F-6 baseline
- `knip` runs over the 15 packages, exits 0, and reports as a section of CU-1's weekly issue rather than as its own channel
- **A written go/no-go for r5's `duplication-reviewer` sub-agent**, citing the observed hit rate

**Why it stands alone**: the deliverable is a decision backed by measurement, and it is worth having even when the answer is *don't build it* — that outcome saves r5 from building the agent the origin document assumed we needed. The advisory checks themselves keep running either way.

**Hot paths**: `skill-directives=Y` (`code-review`). `/conflict-check` before starting.

---

### 3.6 Stopping

Because every phase satisfies §3.0, **r4 can stop cleanly after any phase** — there is no half-built state to unwind and no phase whose value is contingent on the next one. If work is interrupted, the last merged phase is the deliverable.

Ordering is by value-per-collision-risk, not by dependency: **P1** (smallest complete capability, zero collision) → **P2** (highest value) → **P3** (second-highest value, highest collision — hence after the two clean phases) → **P4** → **P5**.

---

## 4. Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>N</bff>                          <!-- No BFF source change. F-1 adds a test under tests/Spaarke.ArchTests/ only. No publish-size impact. -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>        <!-- P2: edit-only (adr-audit.yml issue body carries n/49). P3: CU-1 adds ONE new weekly advisory workflow - the only new workflow in all of r4. F-1 + CU-2 censuses ride existing ArchTests wiring unchanged; F-7 knip + F-8 generator are scripts inside CU-1's job, not workflows of their own. -->
  <skill-directives>Y</skill-directives> <!-- P3: task-execute Step 11 (CU-3) - HIGHEST collision. P4: task-create Step 3.5.6, code-review Step 6.6. P5: code-review Step 6.6. -->
  <root-claude-md>Y</root-claude-md>     <!-- P4: §11 gains a pointer to constraints/reuse.md + the ADR-012 amendment reference -->
</hot-path-declaration>
```

**Collision sequencing follows from this block.** P1 touches nothing hot; P2 is edit-only on one workflow. That is why the two clean phases ship first — neither can collide with the ~17 active worktrees in [projects/INDEX.md](../../projects/INDEX.md). Every hot-path cost is incurred by **P3, P4 and P5** ⇒ **run `/conflict-check` immediately before each of those three phases, and again before each merge.** P3 carries the highest risk in the project (`task-execute` Step 11) and warrants its own coordination pass rather than a shared one.

### ADR Tensions (CLAUDE.md §6.5)

| ADR | Tension | Path |
|---|---|---|
| **ADR-012** | Promotion trigger 2 consumers → 3 + three qualitative tests; sanctioned-package list becomes enumerated + census-enforced | **B (amendment)** — pre-declared here so it is not discovered mid-task |
| ADR-038 | F-1 adds an arch-fitness test; must pair with negative + positive controls per the `SourceScan` discipline, and must not use DI resolution (ban B3) | **C (comply)** |

### Component Justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `SharedPackageCensusTests.cs` | `CredentialCensusTests.cs`, `Adr038TestBanGuardTests.cs` — same pattern, different subject | No — a census asserts one specific enumerated set; folding a second subject into `CredentialCensusTests` would break its per-file locality discipline. Reuses `SourceScan` rather than forking it. | Package #16 is added silently; ADR-012's "new siblings require an ADR amendment" rule is unenforceable and remains so |
| `.claude/constraints/reuse.md` | `bff-extensions.md` (BFF-scoped), CLAUDE.md §11 (principle) | No — §11 is the principle and must stay short (loads every turn); the escalation ladder is procedure and belongs in the on-demand layer | The F-2 ladder gets duplicated into two skills and drifts, which is the exact failure `SourceScan`'s docstring describes |
| **F-8 export index + generator** | `spaarke-components-inventory.json` (Dataverse solution components — a different subject, and a stale snapshot) | No — that artifact indexes Dataverse components extracted from a live environment; this indexes TypeScript exports from barrel files. Different source, different generator, no shared extraction path. | F-2 ships citing an escalation rung that does not exist, and agents keep writing confident `none` on a single failed grep |
| **F-9 arch tests (bounded batch)** | The 35 existing files in `tests/Spaarke.ArchTests/` | **Yes — this IS the extension.** Same project, same `SourceScan` primitive, same CI wiring, same audit workflow. No new surface at all; the only new artifacts are test files in an existing suite. | 42 of 49 ADRs stay silently violable; the one complete enforcement loop we built stays at 14% of its addressable scope |
| **CU-1 weekly workflow + script** | `nightly-health.yml` (nightly, code-focused), `adr-audit.yml` (weekly, runs ArchTests) | **Considered and rejected.** Folding into `nightly-health` would make a governance-surface report share a rolling issue with flake/bundle/CVE findings — different audience, different cadence, and it would dilute a channel that currently gets read. Extending `adr-audit` fails the same way plus it is scoped to a test run. | Two written cadences have already failed to fire (§0.2). A third written cadence is the predictable outcome of not building this. |
| **CU-2 stamp census test** | `CredentialCensusTests.cs`, and `SharedPackageCensusTests.cs` from F-1 | No — F-1's census asserts a *package list*, CU-2 asserts *frontmatter presence across 181 files*. Different subject, different failure message. Both reuse `SourceScan`; neither forks it. | CU-1's staleness report has nothing to measure for 110 of 181 governance files (0/16 constraints, 0/94 patterns carry a stamp) |

---

## 5. Success criteria

Trend-oriented. All computable from artifacts we already produce.

| Criterion | Test |
|---|---|
| Shared-package set is bounded | A 16th `src/client/shared/*` package fails the build without an ADR-012 amendment |
| Absence assertions are honest | `<existing>none` answers record an escalation beyond grep — measured against the F-6 baseline of 53 |
| Promotion is deliberate | Every new shared package cites three named consumers + the three qualitative tests |
| No new judgment gate | F-5, F-7 and **CU-1 are advisory/exit-0**. The three census tests (F-1, CU-2) and F-9's arch tests *are* blocking — that is correct, because each asserts an enumerated fact, not a judgment. **The line r4 must not cross: a gate that blocks on a count-proxy for a judgment question.** That is the God-class-ratchet mistake, retired 2026-08-20. |
| Exactly one new workflow | **CU-1, and nothing else.** No new POML block, no new agent, no new skill, no second reuse-rules home. If a phase proposes a second workflow, it has left scope. |
| Every phase shipped standing alone | Each of P1–P5 satisfied §3.0's three tests — deployable, functionally complete, independently valuable — and delivered its stated capability without any phase after it |
| No phase was sized by count or budget | Each phase's scope was set by what made its capability whole. P2's test batch covered **every** silent-and-expensive enforceable ADR, whatever that number turned out to be. |
| ADR enforcement is measured, then raised | All 49 ADRs classified `enforceable` / `partially` / `judgment-only`; coverage moves from **7/49** to a number visible in `adr-audit.yml`'s issue body without running an audit |
| Class-1 artifacts are generated, never authored | The F-8 index regenerates in CI and fails loudly on divergence. **A hand-edited export index means the generator is broken** — it is not a merge conflict to resolve by hand. |

---

## 6. Open questions for the owner

1. **F-4 threshold** — is Rule-of-Three the right trigger, or is 2-consumers-plus-the-three-qualitative-tests sufficient? The qualitative tests may be doing most of the work; the count may be the weaker half. **Blocks P1** (F-4's amendment text needs the answer).
2. **The existing 11** — does the F-1 amendment **grandfather** all 15 as sanctioned, or does it require a retroactive one-line justification per package? Grandfathering is cheaper; retroactive justification is the only way to learn whether over-promotion actually happened. **Blocks P1.**
3. **CU-1 stamp threshold** — 120 days is a first guess, not evidence. Too tight and the weekly issue becomes noise nobody reads; too loose and it never fires. Start at 120 and tune after two months of real output, or pick a number now? **Blocks P3**, and it is tunable after the fact, so "start at 120" is a safe default if you'd rather not decide now.
4. **F-5 go/no-go bar** — P5 delivers a written recommendation, but what hit-rate over how many PRs would actually convince you to build r5's reviewer sub-agent? Naming it now stops the decision being re-argued from the same data later. **Does not block anything before P5.**

### 6.1 Resolved (recorded so they are not re-litigated)

| Question | Decision | Date |
|---|---|---|
| Scope: stop after the minimum viable cut, or charter the whole thing? | **Full P1–P5**, each phase a deployable, functionally complete, independently valuable capability per §3.0. No "minimum viable cut" — every phase is a valid stopping point by construction. | 2026-09-03, owner |
| F-9 batch sizing — by count or by budget? | **Neither.** Sized by a **functional criterion**: every `enforceable` ADR whose violation is silent *and* expensive. The phase is done when that set is fully covered, whatever its cardinality. | 2026-09-03, owner |
| CU-3 — include or defer, given it edits `task-execute` Step 11? | **Include, in P3.** CU-1 without CU-3 is a smoke alarm with no extinguisher and would fail §3.0's functional-completeness test. Collision risk is handled with a dedicated `/conflict-check` pass, not by deferring. | 2026-09-03, owner |
| One project or split reuse from enforcement? | **One project, two workstreams** — the r3 precedent (surfaces = workstreams, single worktree). A second project would have bought ceremony and *increased* hot-path collision risk. | 2026-09-03, owner |
