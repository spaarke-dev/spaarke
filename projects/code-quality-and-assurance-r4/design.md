# Design — Code Quality & Assurance R4 — Governance That Enforces Itself

> **Status**: Design draft (pre `/design-to-spec`)
> **Created**: 2026-09-02 · **Rescoped + renamed** 2026-09-03 (was `reuse-governance-tightening-r1`) · **Revised** 2026-09-03 (owner review — objective sharpened, three findings added, two cut)
> **Origin**: Owner-supplied `reuse-duplication-action-items.md` (Sept 2026 external synopsis), assessed against real repo state 2026-09-02; widened after the [AI-native development model assessment](../../docs/assessments/ai-native-development-model-2026-09-03.md).
> **Character**: **Small and phased. No new subsystem.** Every phase is independently mergeable and ships value on its own. If a phase starts growing a subsystem, it has left scope.
> **Lineage**: r1 (quality *system*) → r2 (first structural remediation) → r3 (multi-surface program, ✅ complete 2026-08-14, forcing-functions live) → **r4 (the governance layer itself)**.
> **Parent frame**: [`docs/assessments/ai-native-development-model-2026-09-03.md`](../../docs/assessments/ai-native-development-model-2026-09-03.md) — owns the taxonomy, the principles, and the full gap register. This design owns the subset r4 executes.

---

## 0. Premise

**r3 hardened the code. r4 hardens the layer that governs the code.**

r3 proved the pattern: it left behind live forcing-functions — ArchTests, analyzers-as-errors, config fail-fast, a naming gate — so that quality *holds* as the codebase grows. r4 applies the same move one level up. Our governance surface (49 ADRs, 16 constraints, 94 patterns, 71 skills) is authored well, enforced thinly, and decays because nothing fires when it does.

The organizing principle, from the parent assessment:

> **Documentation must be load-bearing, not descriptive.** A doc something depends on fails loudly. A doc that only describes drifts silently.

| # | Workstream | Objective |
|---|---|---|
| **1 — Don't rebuild, don't diverge** | F-1, F-2, F-4, F-5, F-6, F-7, F-8, F-11 | We don't rebuild functionality we already have, and we don't run **the same function in two different code paths** |
| **2 — Enforcement & continuity** | F-9, F-10, F-12, F-13, CU-1 … CU-4 | Rules we already wrote are enforced where enforceable, verified for accuracy, and maintained without anyone remembering to — **including the test suite, whose growth and silencing nothing currently tracks** |

### 0.1 Workstream 1 objective (owner-stated, 2026-09-03)

**The objective is code quality and efficiency. Duplication is an indicator of it, not the thing itself.** The origin document framed this as a duplication problem; that framing is too narrow and this design previously inherited it. The two risks that actually matter:

1. **Rebuilding what exists** — wasted work, and a second thing to maintain.
2. **The same function running in two code paths** — a **correctness** risk, not a tidiness one. Two paths drift; one gets fixed, the other doesn't.

The second is the sharper risk and the repo has the receipts. r3's out-of-scope list explicitly parked *"merging the two live `.eml` builders; merging the two distinct R6 financial handlers"*, and r3 separately found a **broken invoice financial-totals path in production**. Known divergent paths, deliberately left, with a real bug in that neighbourhood. **r4 currently has nothing that would surface the next one** — that gap is F-5 and F-11.

Evidence on the existing gate, which is working and should not be replaced:

| Evidence | Finding |
|---|---|
| 448 POML tasks carry a `<justification>` element with concrete, citing answers | The reuse gate exists and is **working**, not rubber-stamping |
| [task-create/SKILL.md:308](../../.claude/skills/task-create/SKILL.md#L308) already requires `<existing>` to cite `file:line` from Grep evidence | "Record the queries, not just the conclusion" is **already the rule** |
| [code-review/SKILL.md:729](../../.claude/skills/code-review/SKILL.md#L729) flags `"None" with no grep evidence → WARNING` | The gate is **enforced at review time**, not just authored |
| 15 `@spaarke/*` package directories under [src/client/shared/](../../src/client/shared/) governed by an ADR-012 whose list ends in `"etc."` | The set is **unknowable**, which is a different problem from being too large |

> **Explicitly NOT the premise**: that 15 shared packages is too many. Owner position, 2026-09-03: **we do not un-package or regress any existing shared component.** Enumeration exists to make the set knowable and a 16th deliberate — not to trigger a cleanup.

### 0.2 Workstream 2 objective — we have now declared this cadence *four* times

The decay is not from never having thought about it. **Four times we specified a recurring quality mechanism. None of them runs.**

| Declared | Where | What actually happened |
|---|---|---|
| *"Cadence after launch: monthly proactive audit; per-commit validation; quarterly deep audit"* | [`projects/ai-procedure-quality-r1/README.md`](../ai-procedure-quality-r1/README.md) | 55 of 71 skills still carry that project's own 2026-05/06 review stamp. **No monthly audit has ever run.** |
| *"**Cadence**: Monthly on the first business day"* | [`knowledge/REFRESH-PROCEDURE.md`](../../knowledge/REFRESH-PROCEDURE.md) | [`REFRESH-LOG.md`](../../knowledge/REFRESH-LOG.md) shows 3 refreshes in 4 months, every one triggered by a project needing the content — never by the calendar. |
| A complete Claude Code headless nightly reviewer | [`scripts/quality/nightly-review-prompt.md`](../../scripts/quality/nightly-review-prompt.md) — 12 KB, five sections incl. `adr-compliance`, strict JSON schema, severity levels | **Referenced by zero workflows, zero scripts, zero skills.** One commit (`50f2d7bfb`, 2026-03-14), never wired. Also stale — describes the repo as ".NET 8"; we moved to .NET 10 in August. |
| **`nightly-quality.yml`** — 5 jobs, SonarCloud, rolling `nightly-quality` issue, *"MUST complete in < 15 minutes"* | `docs/architecture/ci-cd-architecture.md`, `docs/procedures/ci-cd-workflow.md`, `docs/procedures/testing-and-code-quality.md` — all **present tense** | **The workflow does not exist.** No `nightly-quality.yml`; no SonarCloud reference in any of the 22 workflows. `nightly-health.yml` was built instead with different jobs and no AI review. The docs even carry a troubleshooting row for a failure the nonexistent job cannot produce. |

The fourth is the most severe, because the other three are merely silent while it **actively tells you the opposite of the truth**: an agent consulting our own CI procedure doc is informed about a nightly AI review that has never run once.

So workstream 2's premise is not *"we should adopt a cadence."* It is:

> **A fifth written cadence would fail identically. The deliverable is a mechanism that fires without a human remembering, and a check that fails when a doc claims a mechanism that isn't there.**

That is CU-1 — and its shape is already proven by [`nightly-health.yml`](../../.github/workflows/nightly-health.yml) and [`adr-audit.yml`](../../.github/workflows/adr-audit.yml), both of which *do* run, on a schedule, into a rolling issue.

### 0.3 The three documentation classes (compact)

Full table, evidence, and repo-wide status live in the [parent assessment §2.5](../../docs/assessments/ai-native-development-model-2026-09-03.md). r4 needs only the routing rule and the class labels:

| Class | Drift control | r4 findings |
|---|---|---|
| **1 — Derivable** (inventories, export surfaces, per-project scope) | **Generate it. Never hand-write it.** | F-8, **CU-4** |
| **2 — Non-derivable** (rationale, rejections, invariants, scars) | **Make it executable** — an ADR with an arch-test cannot silently reverse | F-1, F-4, **F-9**, **F-10**, CU-2 |
| **3 — Navigational** (intent → location, claims about what runs) | **Validate the paths and the claims** — a broken pointer, or a doc naming a workflow that doesn't exist, fails loudly | CU-1 |

> **Routing rule, binding on every document this project writes**: *Can the code answer this question?* **Yes** → generate it, or don't write it. **No** → write it, and pin it to a mechanism whose failure mode is a red build or a report nobody can ignore.

### 0.4 Verification is mechanical and factual — never a second opinion

**Owner position, 2026-09-03**: reviewer agents that render opinions on code produce differences of opinion between the coding agent and the reviewing agent, without substantive value.

The operative distinction: **reviewer mechanisms are weak at taste and strong at retrieval.** *"Is this good code?"* yields two defensible views and no defect. *"Does a capability that already does this exist, and where?"* resolves to a `file:line` or it doesn't.

Consequences, binding on this design:

- **No reviewer sub-agent.** F-5 becomes a **retrieval check against F-8's generated index**, not a second opinion. The `duplication-reviewer` concept is dropped entirely — from r4, from r5, and from the parent assessment's G2/G7.
- ADR rules are mostly MUST/MUST NOT statements, which is the *factual* category — *"MUST NOT inject `IOpenAiClient` into CRUD code"* is checkable against a diff. But some are genuinely aesthetic ("DI minimalism"). **F-9's classification therefore carries a second bit on judgment-only ADRs: `checkable-by-reading` vs `aesthetic`.** Only the former is routed to the nightly reviewer. Otherwise we rebuild opinion churn inside CI and have to read it every morning.

---

## 1. Scope

### 1.1 Goals

Listed by workstream. **Shipping order is by capability, not by workstream** — see §3, where the two interleave.

**Workstream 1 — don't rebuild, don't diverge:**

1. Make the sanctioned shared set **knowable**, and write down what the promotion evaluation asks (**F-1 + F-4** → P1).
2. Establish a **baseline**, including the usage data we already have but have never read (**F-6** → P1).
3. Generate the **shared-lib export index** (**F-8** → P4) and give the reuse gate a real **escalation rung** past a failed grep (**F-2** → P4).
4. Detect **functional equivalence** — the same function arriving in a second code path — by retrieval against that index (**F-5** → P4).
5. Make the **divergences we already know about** visible instead of parked (**F-11** → P4).
6. Observe **dead exports** across the shared libs (**F-7** → P5).

**Workstream 2 — enforcement & continuity:**

7. Route **all 49 ADRs** to the mechanism that fits them, rather than enforcing 7 and abandoning 42 (**F-9** → P2).
8. Verify the ADRs are **accurate** before making any of them executable (**F-10** → P2).
9. Build the mechanism that **converts elapsed time into a work item**, ranked by usage and drift (**CU-1** → P3), on stamps that are **assertable** (**CU-2** → P3), clearing the mechanical cases **automatically** (**CU-3** → P3).
10. **Wire the nightly reviewer that already exists**, scoped by a **generated per-project review checklist**, and fix the three docs that describe a workflow we never built (**F-12 + CU-4** → P5).
11. Make **test-suite growth and silencing visible** — skips, flaky-tags, net delta — without adding a single threshold (**F-13** → P5).

### 1.2 Non-goals (explicit scope fence)

Assessed and **rejected or cut**, listed so a later task does not silently re-adopt them.

| Rejected | Reason |
|---|---|
| **Un-packaging or regressing any existing shared component** | Owner position 2026-09-03. Breaking change across every consumer for a bookkeeping benefit. Enumeration ≠ cleanup. |
| **`duplication-reviewer` sub-agent (and reviewer sub-agents generally)** | §0.4. Opinion churn without substantive value. The question F-5 asks is retrieval, and retrieval needs an index, not an agent. |
| **F-3 — `promote` verdict + ledger + consumption quota** ⚰️ | **Cut 2026-09-03.** Three new concepts for a signal we have never measured, and it addresses *promotion policy* — neither "don't rebuild" nor "don't diverge." If F-6's baseline shows third-occurrences are common, it returns in r5 with evidence. |
| Generated "capability manifest" document | 940 BFF service files + 46 PCF + 37 solutions. A manifest that large is context-bloat an agent will not read — it re-creates the retrieval failure at a different layer. Replaced by F-1's **scoped census** and F-8's **scoped index**. |
| `ast-grep` + `sgconfig.yml` for C# | [tests/Spaarke.ArchTests/SourceScan.cs](../../tests/Spaarke.ArchTests/SourceScan.cs) already provides structural source scanning, in the **blocking** CI tier, in a language we maintain, with a stated negative-control discipline. |
| `SonarAnalyzer.CSharp` / Roslyn duplicate analyzer | [Directory.Build.props](../../Directory.Build.props) sets `TreatWarningsAsErrors=true` repo-wide. A new analyzer turns every finding across 940 service files into a **build error on day one**. |
| `jscpd` duplication-percentage hard gate | We already ran this experiment. The God-class LOC ratchet was **retired 2026-08-20** ([COMPONENT-COMPLEXITY.md](../../docs/standards/COMPONENT-COMPLEXITY.md)) because it gated on a count-proxy for a judgment question. A duplication-percentage gate is the same instrument class. |
| New `<reuse-evidence>` POML block | Would be a parallel system alongside a working `<justification>`. Extend, per CLAUDE.md §11 applied to our own procedures. |
| LSP/semantic-index MCP sidecar | ~17 active worktrees + agent worktrees ⇒ not one stale index but N, or one index confidently answering about the wrong tree. |
| Repo-wide reuse/duplication audit | Operationally impractical at this scale, same as the test audit. Bounded consumption only. |
| **Re-adopting anything already rejected by a predecessor** | Four projects have worked this surface: [`ai-procedure-refactoring-r1`](../ai-procedure-refactoring-r1/), [`-r2`](../ai-procedure-refactoring-r2/), [`ai-procedure-quality-r1`](../ai-procedure-quality-r1/), and r4. **Before adding any control not listed in §2, check those three for a prior rejection** — the 2026-05-17 CLAUDE.md rewrite and the AIP-00x protocol-layer removal both deleted machinery on purpose. |

---

## 2. Findings

Grouped by workstream for readability. **They do not ship in this order** — §3 sequences by capability.

### Workstream 1 — Don't rebuild, don't diverge

### F-1 — The sanctioned shared set is unenumerable 🔴

[ADR-012](../../.claude/adr/ADR-012-shared-components.md) states:

> **Sanctioned shared packages (as of this amendment)**: `@spaarke/ui-components`, `@spaarke/visuals`, `@spaarke/auth`, plus the domain component libraries (`@spaarke/events-components`, etc.). **New siblings require an ADR amendment.**

The `"etc."` is load-bearing and self-defeating: the ADR requires an amendment for new siblings while making the sanctioned set unbounded. **This is not a claim that any package is wrong.** It is that we cannot tell what the set *is*, and nothing fires when a 16th appears.

**🚨 Census specification correction (measured 2026-09-03).** An earlier draft specified the census as *"the set of `src/client/shared/*/package.json` names."* **That census would be wrong on day one:**

- `Spaarke.LegalWorkspace` has **no `package.json`** yet is imported as `@spaarke/legal-workspace` by two surfaces, resolved via tsconfig/vite alias. A `package.json`-keyed census enumerates **14**, silently missing it — and encodes the miss as sanctioned truth.
- The `@spaarke/` namespace extends **beyond** `src/client/shared/`: `@spaarke/office-addins`, `@spaarke/secure-project-workspace`, `@spaarke/document-upload-wizard` are applications, and `@spaarke/pcf-shared` is imported with no `package.json` at the path it names. A naive `@spaarke/*` scan sweeps applications in as if they were shared libraries.

**Fix**: enumerate the 15 shared-library directories in ADR-012 with a one-line reason each, then add `SharedPackageCensusTests` keyed on **directory presence under `src/client/shared/`** (not `package.json`), with an explicit allow-list for `@spaarke/*` names that are applications rather than libraries. Package #16 becomes a build failure that asks *"why?"* — the [CredentialCensusTests](../../tests/Spaarke.ArchTests/CredentialCensusTests.cs) pattern, whose docstring makes the argument:

> "A census makes the COUNT itself the assertion, so the next miss is a build failure instead of a discovery two projects later."

**The failure message must name the evaluation questions (F-4) and the amendment requirement** — an agent that trips it should know what to do without opening the ADR. That sentence is the difference between a census that teaches and one that merely blocks.

**Cost: one test file + one ADR edit. Zero CI workflow change** — it rides existing ArchTests wiring.

### F-2 — Absence assertions are grep-only 🟡

The gate requires grep evidence, which is correct as far as it goes. But a lexical miss and a genuine absence are indistinguishable in the current output, and short conceptual queries are the weakest possible retrieval input. A grep for `"summar"` that misses `DigestBuilder` produces a confident, evidence-bearing, **wrong** "none found."

**Fix**: add an escalation requirement — if the lexical pass returns nothing, escalate (F-8's export index → symbol/type search) **before** `<existing>` may say "none," and record which passes ran. One rule, authored once in `constraints/reuse.md`, referenced from both skills.

### F-4 — Promotion has no written evaluation 🟡

ADR-012 triggers at **"Used by 2+ modules/surfaces"** with no further criteria.

**Owner position, 2026-09-03**: a count is arbitrary, but **2+ is a good trigger to *evaluate*** whether something is better served as shared. It is not a mandate to promote, and it is not a bar to clear. Anticipatory promotion is legitimate — `@spaarke/visuals` has no current consumers and is still correctly shared.

> An earlier draft proposed raising the trigger to a third independent occurrence. **Withdrawn.** Measured consumer-surface fan-in shows six packages sitting exactly at two, which a Rule-of-Three would have blocked — including several nobody would argue against.

**Fix**: keep 2+ as the evaluation trigger and **write down what the evaluation asks**, so it is a decision aid rather than folklore. Three questions, none of them a gate:

1. Is the API stable across the consumers, or does each need its own branching?
2. Can it be tested in isolation, without consumer fixtures?
3. Is the commonality **semantic**, or coincidental shape?

Anything failing these can still be promoted with a stated reason — that reason is the value, not the verdict.

### F-5 — Nothing detects the same function arriving in a second code path 🔴 (the sharpest workstream-1 finding)

The direct instance of the owner's stated objective. Two implementations of one behavior are not a tidiness problem; they are a **correctness** problem, because they drift and only one gets fixed. This repo has known cases (§0.1), and no mechanism that would surface the next one.

Two properties this must have, per §0.4:

- **Retrieval, not opinion.** The question is *"is this new exported symbol functionally equivalent to a named existing capability, despite a different implementation?"* — answerable against F-8's index as a lookup with a concrete `file:line` answer, or not at all.
- **Advisory severity.** A false positive that blocks is worse than a true positive that informs.

**Fix**: a functional-equivalence check in `code-review` Step 6.6, resolving against the F-8 index. **No sub-agent.** Its hit rate is recorded against the F-6 baseline, which is what tells us in r5 whether it earns more investment.

### F-6 — No baseline 🟡

Every control here is a behavior change with no before-measurement. Ships first.

**Measure** — all cheap, all from artifacts we already have:

- `<extension>` yes/no ratio across the 448 justifications; count and quality of `<existing>none` answers (**53 today**)
- Import fan-in per `@spaarke/*` package — **already measured 2026-09-03**: `ui-components` 54 consumer surfaces, `auth` 37, `communication-components` 8, seven packages at 2, three at 1, two at 0
- **ADR citation counts across POML tasks** — a usage record we have been accumulating for 205 projects and have never read. Top: **ADR-021 (3,061 citations, no arch test)**, ADR-013 (2,131), ADR-038 (1,745), ADR-010 (1,527), **ADR-028 auth (1,521, no ADR-named test)**, ADR-012 (1,210)
- `<escalation><trigger>` firing count, and §6.5 amendment count (feeds F-10)

The citation data is the load-bearing addition: **it prioritises F-9 empirically instead of by guess.** The most-cited ADR in the repo has no enforcement.

### F-7 — No dead-export visibility across the shared packages 🟢

**Fix**: `knip`, **observation-only, exit 0**, scoped to the shared libs, reported as a section of CU-1's issue rather than its own channel.

> **Cost correction**: there are **no npm workspaces** — root `package.json` has no `workspaces` field and neither do the libs. knip's monorepo auto-discovery does not apply; this is per-package configuration, which is why it stays scoped to the shared libs and is **not** extended to 37 solutions + 46 PCF controls.

> **Gotcha for any repo-wide scanner**: [.claude/worktrees/](../../.claude/worktrees/) contains full source-tree copies. Plus ~17 active sibling worktrees per [projects/INDEX.md](../../projects/INDEX.md). Any scan must exclude these or report enormous phantom findings.

### F-8 — F-2 and F-5 have no index to resolve against 🟡 (Class 1)

Both F-2's escalation ladder and F-5's equivalence check name a "shared-package export index." **It does not exist.** Today an agent that greps `"summar"` and misses `DigestBuilder` has nothing further to consult.

Class 1, so the §0.3 routing rule binds: **generate it, never hand-write it.** The precedent and its lesson are both in [spaarke-components-inventory.json](../../docs/data-model/spaarke-components-inventory.json) — machine-extracted, 3,042 components, nobody hand-maintains it, and it is a **snapshot, not a living artifact**: one commit (`809ae3477`, 2026-08-20), no regeneration wiring in any workflow or script.

**Fix**: generate an index from the shared-library barrel files — symbol name, kind, package, source path. **Minimum scope, deliberately:**

- **Shared libs only.** Not 940 BFF service files, not 37 solutions, not 46 PCF controls.
- **Regeneration is wired, or it doesn't ship.** A script nobody runs is worse than no index, because F-2 and F-5 would then cite a stale authority. Because **CU-1 lands in P3**, F-8 inherits its wiring: regenerate inside CU-1's nightly job and report a diff as a finding. No new workflow, no new schedule. *(Stated economy, not hidden dependency — see P4.)*
- **Greppable in one pass.** If it outgrows that, the scope is wrong.

**Class-1 discipline check**: nothing here is hand-authored. If a task edits it by hand, the generator is broken and that is the bug.

### F-11 — The divergences we already know about are parked and invisible 🟡

r3 explicitly deferred *"merging the two live `.eml` builders; merging the two distinct R6 financial handlers; migrating `[Obsolete]` members with live callers."* Those are exactly the risk in §0.1 — and they now live only in a completed project's out-of-scope list, which nothing reads.

**Fix**: a short, enumerated **known-divergence register** — each entry naming both code paths, the behavior they share, and why merging was deferred. Not a remediation project; r4 does **not** merge them. The deliverable is that they stop being invisible, and that F-5's future hits have somewhere to accumulate alongside them.

> Deliberately a *list*, not a mechanism. If it needs tooling, it has left scope.

---

### Workstream 2 — Enforcement & continuity

### F-9 — Enforcement is 7/49, and the other 42 have nowhere to go 🔴

**Measured 2026-09-03.** [tests/Spaarke.ArchTests/](../../tests/Spaarke.ArchTests/) contains 35 test files. Of the **49** concise ADRs, exactly **7** carry a named architecture-fitness test: ADR-001, 002, 007, 008, 009, 010, 013 (plus `Adr038TestBanGuardTests.cs`; ADR-038 lives in `docs/adr/`).

Two things this does *not* say. Enforcement is broader than the ADR-named count — ~18 further guard tests encode invariants without an ADR number (`CredentialGuardTests`, `RouteAuthorizationGuardTests`, `FabricatedResultGuardTests`, `LayerDependencyTests`, `TenantIsolation/I1–I6`). And **not every ADR is mechanically enforceable** — some are judgment rules, and manufacturing a test for one is the God-class-ratchet mistake in a new costume.

**The reframe (2026-09-03): classification is a routing decision, not a filter.** An earlier draft classified ADRs and then *abandoned* the judgment-only ones. With F-12's nightly semantic reviewer, every ADR routes somewhere:

| Class | Mechanism | Failure mode |
|---|---|---|
| `enforceable` | Arch test | Red build, blocking (Tier 1) |
| `partially-enforceable` | Arch test for the mechanical part **+** nightly review for the rest | Split |
| `judgment-only` + `checkable-by-reading` | Nightly semantic review (F-12) | Advisory report |
| `judgment-only` + `aesthetic` | **Nothing.** Recorded as deliberately unenforced | Honest gap |

That takes the story from *"7/49 enforced, 42 abandoned"* to **"49/49 routed, honestly tiered"** — for the same classification work.

**Fix — three parts, in order:**

1. **Classify all 49** on three axes, one line of reason each, recorded in [.claude/adr/INDEX.md](../../.claude/adr/INDEX.md): enforceability (above), accuracy (F-10), and — for judgment-only — `checkable-by-reading` vs `aesthetic` per §0.4.
2. **Write tests for a bounded set**, sized by a **functional criterion, never a count**: every `enforceable` ADR whose violation is both **silent** (won't surface at review or runtime) *and* **expensive** (security, tenant isolation, auth, data integrity, cross-layer boundary), prioritised within that set by **F-6's citation counts** — blast radius measured, not guessed. The phase is done when that set is fully covered, whether it is four ADRs or fourteen.
3. **Make coverage a tracked number** in `adr-audit.yml`'s issue body, with the classification breakdown, so `7/49 → n/49` is visible weekly without anyone running an audit.

Additionally: **a new ADR added without a classification fails the build.** Without it, part 1 is a snapshot that decays the moment ADR-052 lands — the same mistake as the 2026-08-20 inventory.

> ⚠️ **Explicit non-goal: "write 42 arch tests."** That would force tests onto judgment-only ADRs. The deliverable is *classification, routing, and a criterion-bounded set*.

> **`.claude/adr/INDEX.md` is itself incomplete** — it lists **36 of 49** ADRs. Thirteen are invisible to anyone starting from the index CLAUDE.md points at. Part 1 fixes this as a side effect.

### F-10 — ADR accuracy is unverified 🔴 (added 2026-09-03)

**The tension this answers**: making an ADR executable amplifies whatever it says. An unenforced *wrong* ADR gets quietly ignored; an enforced wrong ADR blocks the build and directs agents to write worse code to satisfy it. F-9 without F-10 is a mechanism for encoding our errors faster.

The evidence says some rules are already inaccurate: ADR-012's `"etc."` (F-1); `.claude/adr/INDEX.md` covering 36 of 49; three CI docs describing a workflow that does not exist (§0.2). And there is direct precedent for a rule that was enforced, blocking, wrong, and retired — the God-class LOC ratchet, 2026-08-20.

**The counter-insight**: enforcement is also **the best accuracy detector we have.** An arch-test that starts failing on legitimate new code is *evidence the ADR is wrong* — a signal we never get today, because unenforced ADRs are simply ignored. The failure mode to avoid is not enforcement; it is treating a red test as a verdict rather than a question.

**Fix — three parts, all cheap because they ride F-9's pass:**

1. **A second axis in the classification**: `current` / `stale` / `contested`, one line of reason. The classification pass is the first time anyone reads all 49 ADRs in one sitting in months; asking a single question of that effort is waste. **Nothing gets a test until it is marked `current`.**
2. **Every arch-test failure message names the [CLAUDE.md §6.5](../../CLAUDE.md) challenge path.** *"You violated ADR-X"* directs compliance. *"You violated ADR-X — if the rule is wrong here, paths A/B/C are how you say so"* directs judgment. One sentence per test; it is what stops enforcement calcifying.
3. **Measure the amendment rate** (F-6). §6.5 is well designed but we do not know whether path B has ever fired. Coverage climbing while amendments stay at zero is evidence of calcification, not compliance.

### F-12 — The nightly reviewer exists, was never wired, and three docs claim it runs 🔴 (added 2026-09-03)

See §0.2 for the full evidence. [`scripts/quality/nightly-review-prompt.md`](../../scripts/quality/nightly-review-prompt.md) is a complete Claude Code headless prompt with an `adr-compliance` section and a strict JSON schema, committed 2026-03-14 and **never connected to a trigger**. Three docs describe a `nightly-quality.yml` that does not exist.

This is not a build. It is a **wiring job, a refresh, and a documentation correction.**

**Fix:**

1. **Wire it** as a second section of CU-1's nightly workflow (§1.2's ceiling holds — one workflow, two sections). Scope: the diff since the last run, plus PRs on active branches — **not** the whole repo across ~17 worktrees.
2. **Refresh the prompt**: `.NET 8` → `.NET 10`; align the five sections with what we now route to it (F-9's `judgment-only` + `checkable-by-reading` set); accept CU-4's per-project checklist as scope input.
3. **Correct the three docs.** They currently describe five jobs, SonarCloud, and a `< 15 min` target for a workflow that was never built, and `nightly-health.yml` was built instead. This is not optional cleanup — it is the reason nobody noticed for six months.
4. **Report** into a rolling GitHub issue **per active project**, idempotently updated — the pattern `adr-audit.yml` already uses. At wrap-up that issue *is* the record; there is no file to drift.

**Non-blocking by construction.** Its value is a report reviewable *during* a project and consumable at wrap-up, not a gate.

### CU-1 — Nothing converts elapsed time into a work item 🔴

Two scheduled jobs run today; **both watch code, neither watches the governance surface.** §0.2 shows the result: four specified mechanisms, zero recurring runs.

**Fix**: one nightly job → **one rolling advisory issue**, reporting:

- **Stale primitives, ranked** (below) — not "everything past a date"
- **Broken pointer paths** across `.claude/**` and `docs/**` — the Class-3 control. Also closes the missing `Find-SkillReferenceDrift.ps1` that [`ai-procedure-maintenance`](../../.claude/skills/ai-procedure-maintenance/SKILL.md) cites as a deliverable but which does not exist
- **Broken mechanism claims** — every `*.yml` workflow named in `docs/**` or `.claude/**` must exist in `.github/workflows/`. A few lines; it would have caught §0.2's fourth row on day one. *A doc that names a mechanism is making a checkable assertion, and nothing checks it today.*
- Class-1 artifacts whose regeneration produces a diff (F-8), dead exports (F-7), ADRs still unclassified (F-9)

**Ranking replaces the calendar.** An earlier draft used "older than 120 days," which would have listed ~55 skills in the first issue — a wall of entries, which is how a report becomes noise nobody reads. Instead:

```
priority  =  drift_signal  ×  usage_weight
```

`drift_signal` = did anything the primitive references change since its `last-reviewed`. `usage_weight` is **U-shaped**: heavily used *and* never used both rank high, for the two different reasons — a heavily-used skill has high blast radius if wrong ([`FAILURE-MODES.md` AP-1](../../.claude/FAILURE-MODES.md): *"skill prescribes X but X is wrong"*), and an untouched one is a candidate for stale-or-dead. The middle can wait. The issue reports the **top N by priority**.

**Advisory, never blocking.** Clean nights close the issue — the `nightly-health` contract, which works.

### CU-2 — Stamps are decorative, and usage is unmeasured 🟡

Two halves of the same substrate problem: CU-1's ranking needs both a `last-reviewed` date and a usage count, and neither exists for most primitives.

**Stamps**: 63 of 71 skills carry `last-reviewed` (one is a literal `YYYY-MM` placeholder). **0 of 16 constraints and 0 of 94 patterns carry any stamp** — so for ~110 of 181 governance files CU-1 would have nothing to measure. **Fix**: a census test asserting a parseable `last-reviewed` on every `.claude/skills/*/SKILL.md`, `constraints/*.md`, and `patterns/**/*.md`. Adding an unstamped file fails the build — the F-1 primitive, different subject.

**Usage**: two sources, one retroactive and one forward.

- **Retroactive, free, available today**: ADR citation counts across POML task files (F-6). No instrumentation needed.
- **Forward**: a `PostToolUse` hook appending `timestamp, name` to a usage log — on the **Skill** tool for skills, and on **Read** matching `.claude/{adr,patterns,constraints}/**` for the rest. **Hooks are a proven, live mechanism in this repo**, not a new capability: [`scripts/quality/post-edit-lint.sh`](../../scripts/quality/post-edit-lint.sh) (PostToolUse on Edit) and [`scripts/quality/task-quality-gate.sh`](../../scripts/quality/task-quality-gate.sh) (Stop) have both been running since March. Zero human involvement, sub-second, no false-positive mode.

> Read-based usage is directionally useful but noisy — a read happens for many reasons. It ranks; it does not adjudicate.

### CU-3 — "Review" is undefined, which is why it never happens 🟡

Asking someone to "review a skill" is asking for open-ended re-reading, which is exactly the friction that has killed four cadences. The unlock: **most review is not judgment, it is checking whether the ground moved.**

| Tier | Question | Who | Cost |
|---|---|---|---|
| **1 — Mechanical** | Do its paths still exist? Do its commands still exist? Has anything it references changed since `last-reviewed`? | **Nobody — a script.** If nothing moved, **auto-bump the stamp.** | ~0 |
| **2 — Diff-scoped** | Referenced files *did* change. Does the primitive still describe them correctly? | An agent, reading **only the diff** | ~2 min |
| **3 — Judgment** | Is this still the rule we want? | The owner | Rare, ADR/constraint semantics only |

**Tier 1 is the friction-killer.** In any given week most of 181 files have had nothing move underneath them, and they clear automatically **without a lie** — the stamp means *verified nothing changed*, which is true. Tier 2 is [`doc-drift-audit`](../../.claude/skills/doc-drift-audit/SKILL.md), which is already diff-based; the gap was never the method, only the trigger.

> This **replaces** the earlier CU-3 proposal to bump stamps at `090-wrapup-*` via `task-execute` Step 11. The auto-bump is continuous rather than per-project, and it **removes the highest-collision edit in the project.**

**Plus the signal we are missing entirely.** The highest-quality information about a skill is generated at the moment it is used and turns out to be wrong — and today that evaporates. `AP-1` exists because someone captured it once, by hand. **Fix**: one line in `code-review`'s Step 9.5 output — *did any primitive you loaded turn out to be wrong?* Usually "no." Not a review, a one-bit flag at the point of discovery, picked up by CU-1's nightly job. Reconstructing this later costs vastly more than capturing it now.

### CU-4 — The nightly review has no scope, so it would be generic 🟡 (added 2026-09-03)

A generic reviewer across ~17 active worktrees produces generic findings. F-12's prompt was written before there was anything to scope it with.

**Fix**: a **generated** per-project review checklist (`projects/{name}/review-checklist.md`), derived from artifacts that already exist — `spec.md`'s ADR Tensions, `design.md`'s hot-path declaration and component-justification table, and POML `<constraint>` / `<acceptance-criteria>` / `<justification>` elements.

- **Class 1 ⇒ generated, never hand-written.** A hand-authored checklist drifts from the spec within weeks and becomes another stale doc — the exact failure r4 exists to fix.
- **Two audiences, one file**: structured front-matter (which ADRs are in scope, which files are hot, which invariants must hold) for F-12 and `code-review` to consume; a prose checklist beneath it for the owner at wrap-up.
- **Consumed by three things or it is ceremony**: F-12's nightly reviewer takes it as scope, `code-review` Step 9.5 takes it as focus, wrap-up takes it as the record. If only one consumes it, 205 projects × one more file is not worth it.

### F-13 — Test-suite growth and silencing are untracked 🟡 (added 2026-09-03)

**The structural cause, and why it is an AI-native problem specifically**: test **production is per-task** — distributed across agents, each optimizing locally, none able to see the other 40 tasks writing near-identical tests — while test **deletion is per-project**, centralized at `090-wrapup-*`. The current suite is the integral of that rate mismatch: **9,750 C# test methods** (`[Fact]`/`[Theory]` across `tests/`).

Three accumulations are invisible, all the same shape — a number nothing tracks:

| Accumulation | Today | Why invisible |
|---|---|---|
| **Skipped tests** | **68** `Skip =` occurrences (down from **168** on 2026-08-19) | [The assessment](../../docs/assessments/test-suite-skipped-tests-assessment-2026-08-19.md) says it exactly: *"a skipped test is actively worse than no test — it looks like coverage… **it survives `/test-diet`, whose classifier is aimed at tests that run**… Nothing tracks this number."* |
| **Flaky-tagged tests** | Unmeasured | [`nightly-health.yml`](../../.github/workflows/nightly-health.yml) documents `[Trait("Category","Flaky")]` as a way to *"temporarily silence"* a flake — and its triple-run then **skips** those tests. There is no expiry, so "temporarily" is unbounded. |
| **Net suite growth per project** | Unmeasured | Nobody sees a project add 400 tests until wrap-up, when questioning them is most expensive |

The governing rules already exist and are partly enforced — [ADR-038](../../docs/adr/ADR-038-testing-strategy.md)'s 7 KEEP-path categories and 17 bans, with `Adr038TestBanGuardTests.cs` blocking the banned shapes at build time. **What is missing is not a rule; it is the economics.** Production is cheap and invisible, deletion is expensive and rare.

**Fix — three parts, none of them a new mechanism:**

1. **Before** — one directive at `task-create`: a new test must **name its ADR-038 KEEP category**. A test that cannot name one should not be written. The closed-set principle (unbounded "add tests" → an enumerated obligation).
2. **During** — CU-1's nightly issue gains a **test-suite health section**: `Skip=` count with age per entry, Flaky-trait count with age, and net test delta since the last run. A **section**, not a mechanism.
3. **After** — CU-4's generated checklist carries the project's **stated test obligation**, so `/test-diet` reconciles against *what this project said it would add* rather than against a general classifier — and the skip count travels with it, closing the blind spot the assessment names.

> ⚠️ **Explicit non-goal: any threshold on test count.** A gate on "too many tests" is a count-proxy for a judgment question — the God-class ratchet in a new costume, retired 2026-08-20. All three numbers are **reported, never blocking**.

> **Also explicitly out of scope: remediating the 68 skips.** That analysis is complete and already filed as [#794](https://github.com/spaarke-dev/spaarke/issues/794) under the same Epic #427. r4's job is to make the number *visible* so it stops being invisible for another six months — a fifth instance of work specified and never triggered.

---

## 3. Phases

### 3.0 What a phase is (binding definition)

**A phase is a capability, not a batch of tasks.** Each must satisfy all three tests; a phase failing any of them is mis-scoped and must be re-cut before task-creation:

| Test | Meaning |
|---|---|
| **Deployable** | It merges to master and can be left there **indefinitely** with no follow-on phase. Nothing it ships is inert until a later phase arrives. |
| **Functionally complete** | The capability works **end-to-end**, including the case that proves it fires. A rule with no enforcement, or an enforcement whose message doesn't tell the reader what to do, is not complete. |
| **Independently valuable** | If r4 stopped the day this phase merged, it would still have been worth doing. |

**Sizing rule — completeness, not count or budget.** A phase is sized by *what makes its capability whole*. Where it covers an enumerated set (P2's arch-test batch), the set is bounded by a **functional criterion** and the phase is not done until every member is covered — four items or fourteen. **No phase is sized by a task count, a wave, or a time box.**

**Complexity budget.** Every phase declares its **net new concepts** — things an agent or a human must now hold that they did not before. A phase adding more than one must argue for it. The objective is sophistication, not layers: reusing an existing primitive on a new subject costs zero.

| Phase | Capability | Net new concepts | Hot paths |
|---|---|---|---|
| **P1** | The shared surface is knowable | 0 | none |
| **P2** | Every ADR is routed, accurate, and measured | 1 (the classification) | `ci-workflows` (edit only) |
| **P3** | The governance surface maintains itself | 1 (a nightly issue to read) | **one** new workflow, `.claude/settings.json` |
| **P4** | Don't rebuild, don't diverge | 1 (a generated index you must not hand-edit) | `skill-directives`, `root-claude-md` |
| **P5** | Tailored review that actually runs, over a test suite whose growth is visible | 1 (a per-project checklist) | `skill-directives` |

---

### P1 — The shared surface is knowable

**Capability**: the sanctioned shared set is enumerated, a 16th package is a deliberate decision rather than an accident, and we have a before-measurement for everything r4 changes.

**Contents**: F-1 (enumerate + census, corrected spec), F-4 (the three evaluation questions in ADR-012), F-6 (baseline).

**Complete when**:
- All 15 shared-library directories enumerated in ADR-012, one line each — no `"etc."` remains, and **nothing is un-promoted**
- `SharedPackageCensusTests` passes on the 15, **fails on a synthetic 16th**, is keyed on **directory presence** (not `package.json` — it would miss `Spaarke.LegalWorkspace`), and carries an explicit allow-list for the `@spaarke/*` names that are applications
- The failure message names F-4's three questions and the amendment requirement
- Baseline published, **including the ADR citation counts** — the usage record we have never read

**Why it stands alone**: the entirety of F-1/F-4 and gap G10, working the day it merges.

**Why first**: smallest complete capability, **zero net new concepts**, touches no workflow and no skill — the only phase needing no `/conflict-check` — and it is the reference implementation of the census pattern P3 reuses.

---

### P2 — Every ADR is routed, accurate, and measured

**Capability**: all 49 ADRs are classified for enforceability *and accuracy*, each routed to the mechanism that fits it, the silent-and-expensive ones are enforced, and coverage is reported weekly without anyone running an audit.

**Contents**: F-9 (all three parts, incl. the routing table), F-10 (accuracy axis, challenge-path messages, amendment measurement).

**Sizing — functional criterion**: every `enforceable` **and** `current` ADR whose violation is both silent and expensive, prioritised within that set by F-6's citation counts. Discovered by part 1, not chosen now.

**Complete when**:
- All 49 classified on three axes — enforceability, accuracy (`current`/`stale`/`contested`), and for judgment-only, `checkable-by-reading` vs `aesthetic` — one line of reason each, in `.claude/adr/INDEX.md`, **which also grows from 36 entries to 49**
- **Nothing marked `stale` or `contested` receives a test** until it is amended or confirmed
- Every ADR meeting the criterion has an arch-test with positive + negative controls, and **every failure message names the §6.5 challenge path**
- `adr-audit.yml`'s issue body carries `n/49` plus the classification breakdown and the amendment count
- **A new ADR added without a classification fails the build**

**Why it stands alone**: the highest-value item delivered whole — classification, accuracy verification, enforcement of the set that matters, weekly measurement, and a guard that keeps all three current.

**Stated economy**: the `judgment-only` + `checkable-by-reading` ADRs are *routed* here and *serviced* by F-12 in P5. P2 is complete as "known, accurate, honestly tiered"; P5 completes the coverage. If P5 never ships, the routing table is still an honest statement of what is and isn't enforced — which is strictly better than today.

**Hot paths**: `ci-workflows=Y`, **edit-only** (an existing workflow's issue body). Light `/conflict-check`.

---

### P3 — The governance surface maintains itself

**Capability**: decay is reported nightly, ranked by usage and drift, and the mechanical cases clear themselves without anyone looking.

**Contents**: CU-2 (stamp census + usage instrumentation) → CU-1 (nightly job, ranked report, link + mechanism-claim checks) → CU-3 (three-tier review, Tier-1 auto-bump). In that order — stamps and usage data must exist before anything can rank them.

**Complete when**:
- Every skill / constraint / pattern carries a parseable `last-reviewed`; the census **fails on a new unstamped file** (~110 files gain a first stamp)
- The usage hook is live and writing; **retroactive ADR citation counts are already available** so ranking works from day one rather than after three months of accumulation
- The nightly job files **one rolling advisory issue** with the **top N by `drift × usage`** — stale primitives, broken pointer paths, **broken mechanism claims** (a doc naming a workflow that doesn't exist), Class-1 regeneration diffs, unclassified ADRs — and a clean night closes it
- **Tier-1 auto-bump is live**: primitives whose referenced files have not changed have their stamps bumped by machine, honestly
- `ai-procedure-maintenance`'s citation of `Find-SkillReferenceDrift.ps1` is **satisfied or removed** — the current state is the only unacceptable one
- **Proof of life**: the first nightly issue lands carrying real findings, and at least one primitive clears via auto-bump

**Why it stands alone**: the strongest standalone case in r4. §0.2 establishes this exact deliverable has been specified four times and produced zero recurring runs.

**Hot paths**: **one** new workflow — the only one in r4 — plus `.claude/settings.json` for the hook. Note `.claude/settings.json` is a tracked file each worktree copies, so it is a genuine merge surface even though it is not one of the five declared hot-path categories. `/conflict-check` before starting and again before merge.

> **This phase no longer edits `task-execute`.** The Tier-1 auto-bump replaced the Step 11 hook, removing what was the highest-collision change in the project.

---

### P4 — Don't rebuild, don't diverge

**Capability**: an agent whose search finds nothing has a real second rung; a new symbol that duplicates an existing capability is flagged before it lands; and the divergences we already know about are written down.

**Contents**: F-8 (generated index) → F-2 (escalation ladder) → F-5 (functional-equivalence retrieval check) → F-11 (known-divergence register) → `constraints/reuse.md`, written last.

**Complete when**:
- The index is **generated** from the shared barrels, greppable in one pass, and **regenerates in CU-1's nightly job** with any diff reported as a finding — a hand-edit cannot land silently
- `task-create` and `code-review` require a recorded escalation beyond the lexical pass before `<existing>` may say "none," and record which passes ran
- F-5's check resolves **against the index**, returns a concrete `file:line` or nothing, carries **advisory severity only**, and its hit rate accumulates against the F-6 baseline
- The known-divergence register exists, enumerating r3's parked cases with both paths named
- `constraints/reuse.md` documents **shipped reality**, 100–200 lines per the [constraints/INDEX.md](../../.claude/constraints/INDEX.md) convention, **pointing at** CLAUDE.md §11 and ADR-012 rather than restating them — a fourth home for reuse rules is the failure mode to avoid

**Why it stands alone**: this is the owner's stated objective delivered — a working gate improvement and a divergence detector, independent of P5.

**Stated economy (not a hidden dependency)**: F-8's regeneration rides P3's nightly job rather than adding a second workflow. If P3 were skipped, F-8 would need its own wiring, breaching the one-workflow ceiling — the correct response would be to ship P3 first, not to add a workflow.

**Hot paths**: `skill-directives=Y` (`task-create` 3.5.6, `code-review` 6.6), `root-claude-md=Y` (§11 pointer). `/conflict-check` mandatory.

---

### P5 — Tailored review that actually runs

**Capability**: the nightly reviewer we wrote six months ago runs, scoped to what each project actually declared, reporting somewhere the owner reads during the project and at wrap-up — and the docs stop describing a workflow that doesn't exist.

**Contents**: CU-4 (generated per-project checklist) → F-12 (wire + refresh the prompt, correct the three docs) → **F-13** (test-suite health section + KEEP-category directive + test obligation in the checklist) → F-7 (knip as a report section).

**Complete when**:
- `review-checklist.md` is **generated** per project from spec/design/POML, carries machine-readable front-matter plus a prose checklist, and is consumed by **all three** of F-12, `code-review` Step 9.5, and wrap-up
- The nightly reviewer runs as a **second section of P3's workflow** — scoped to the diff since last run plus PRs on active branches, never the whole repo across ~17 worktrees — takes the checklist as scope, and covers the `judgment-only` + `checkable-by-reading` ADRs P2 routed to it
- The prompt is refreshed: `.NET 8` → `.NET 10`, sections aligned to what is routed to it
- Findings land in a **rolling GitHub issue per active project**, idempotently updated
- **The three CI docs are corrected** — `ci-cd-architecture.md`, `ci-cd-workflow.md`, `testing-and-code-quality.md` no longer describe a `nightly-quality.yml` that does not exist, and CU-1's mechanism-claim check would now catch a recurrence
- `knip` reports as a section of the same issue, exit 0
- **Test-suite health is visible**: the nightly issue reports `Skip=` count with per-entry age, Flaky-trait count with age, and net test delta; `task-create` requires a new test to name its ADR-038 KEEP category; the checklist carries the project's test obligation and `/test-diet` reconciles against it. **No threshold on any of the three numbers**, and the 68 existing skips are left to [#794](https://github.com/spaarke-dev/spaarke/issues/794).

**Why it stands alone**: it converts a six-month-old unwired asset into a running mechanism and removes documentation that actively misleads. Worth doing even if nothing else in r4 had shipped.

**Hot paths**: `skill-directives=Y` (`code-review`). `/conflict-check` before starting.

---

### 3.6 Stopping

Because every phase satisfies §3.0, **r4 can stop cleanly after any phase** — no half-built state, no phase whose value is contingent on the next. If work is interrupted, the last merged phase is the deliverable.

Ordering is by value-per-collision-risk: **P1** (smallest, zero collision) → **P2** (highest value) → **P3** (second-highest, first hot-path exposure) → **P4** → **P5**.

---

## 4. Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>N</bff>                          <!-- No BFF source change. Test-only additions under tests/Spaarke.ArchTests/. No publish-size impact. -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>        <!-- P2: edit-only (adr-audit.yml issue body). P3: ONE new nightly workflow - the only new workflow in all of r4. P5: a second SECTION of that same workflow, not a second workflow. -->
  <skill-directives>Y</skill-directives> <!-- P4: task-create 3.5.6, code-review 6.6. P5: code-review 9.5. NOT task-execute - the CU-3 auto-bump removed that edit. -->
  <root-claude-md>Y</root-claude-md>     <!-- P4: §11 gains a pointer to constraints/reuse.md + the ADR-012 amendment reference -->
</hot-path-declaration>
```

**Collision sequencing follows from this block.** P1 touches nothing hot; P2 is edit-only on one workflow. Those two ship first because neither can collide with the ~17 active worktrees in [projects/INDEX.md](../../projects/INDEX.md). Hot-path cost is incurred by **P3, P4 and P5** ⇒ **run `/conflict-check` immediately before each, and again before each merge.**

> **Undeclared surface worth noting**: P3 edits `.claude/settings.json` to register the usage hook. That file is tracked, so every worktree carries a copy and concurrent edits conflict — but it is not one of the five declared hot-path categories. Flagging it here rather than silently incurring it.

### ADR Tensions (CLAUDE.md §6.5)

| ADR | Tension | Path |
|---|---|---|
| **ADR-012** | Sanctioned list becomes enumerated + census-enforced; the 2+ trigger is restated as an **evaluation trigger** with three written questions (not raised to 3, and **no un-promotion**) | **B (amendment)** — pre-declared here so it is not discovered mid-task |
| ADR-038 | New arch-fitness tests must pair with negative + positive controls per the `SourceScan` discipline, and must not use DI resolution (ban B3) | **C (comply)** |
| **Any ADR marked `stale` or `contested` by F-10** | Discovered mid-project by construction | **B or C, decided per ADR.** F-10 exists to surface these; each gets its own §6.5 record rather than being silently enforced or silently skipped |

### Component Justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `SharedPackageCensusTests.cs` | `CredentialCensusTests.cs`, `Adr038TestBanGuardTests.cs` — same pattern, different subject | No — a census asserts one specific enumerated set; folding a second subject in would break per-file locality. Reuses `SourceScan` rather than forking it. | Package #16 lands silently; ADR-012's amendment rule stays unenforceable |
| **CU-2 stamp census** | `CredentialCensusTests.cs`, `SharedPackageCensusTests.cs` | No — F-1 asserts a *package list*; this asserts *frontmatter presence across 181 files*. Different subject, different failure message. Both reuse `SourceScan`. | CU-1 has nothing to measure for ~110 of 181 governance files |
| **CU-1 nightly workflow + script** | `nightly-health.yml` (nightly, code-focused), `adr-audit.yml` (weekly, test run) | **Considered and rejected.** Folding into `nightly-health` would put a governance report in a rolling issue with flake/bundle/CVE findings — different audience, and it dilutes a channel that currently gets read. | Four specified cadences have already failed to fire (§0.2). A fifth is the predictable outcome of not building this. |
| **CU-2 usage hook** | `post-edit-lint.sh` (PostToolUse/Edit), `task-quality-gate.sh` (Stop) — both live since March | **Yes — same mechanism, new matcher.** No new capability; hooks are proven here. | CU-1's ranking degenerates to a calendar, which is what produced the 55-file wall |
| **F-8 export index + generator** | `spaarke-components-inventory.json` (Dataverse solution components — different subject, and a stale snapshot) | No — that indexes Dataverse components from a live environment; this indexes TypeScript exports from barrels. Different source, different generator. | F-2 and F-5 both cite an escalation rung that does not exist |
| **F-9/F-10 arch tests** | The 35 files in `tests/Spaarke.ArchTests/` | **Yes — this IS the extension.** Same project, same `SourceScan`, same CI wiring, same audit workflow. Only new artifacts are test files in an existing suite. | 42 of 49 ADRs stay silently violable |
| **CU-4 `review-checklist.md`** | `spec.md`, `design.md`, POML tasks — the sources it derives from | **No, and it must not be authored.** It is a *projection* of those three for machine consumption; hand-writing it would create a fourth thing to keep in sync. | F-12's reviewer runs generically across 17 worktrees and produces findings nobody acts on |
| **F-12 wiring** | `scripts/quality/nightly-review-prompt.md` — **already written** | **Yes — pure wiring.** No new prompt, no new tool. | A complete reviewer stays unwired for a seventh month while three docs claim it runs |

---

## 5. Success criteria

Trend-oriented. All computable from artifacts we already produce.

| Criterion | Test |
|---|---|
| The shared set is knowable | A 16th `src/client/shared/*` package fails the build; the message names the evaluation questions; **nothing existing was un-promoted** |
| Absence assertions are honest | `<existing>none` answers record an escalation beyond grep — measured against the F-6 baseline of 53 |
| **Divergence is detectable** | F-5 resolves against a generated index and returns `file:line` or nothing; known divergences are enumerated rather than parked in a closed project's out-of-scope list |
| Every ADR is routed | All 49 classified on three axes; `n/49` and the amendment count visible weekly without running an audit; **nothing `stale` is enforced** |
| **The clock fires without a human** | A nightly issue exists, is ranked by `drift × usage`, and clean nights close it. **The fifth written cadence is a job, not a paragraph.** |
| **Docs cannot claim mechanisms that don't exist** | Every `*.yml` named in `docs/**` or `.claude/**` resolves to a real workflow — the check that would have caught §0.2's fourth row on day one |
| No new judgment gate | F-5, F-7, CU-1 and F-12 are advisory. The censuses and arch tests are blocking — correct, because each asserts an enumerated fact. **The line r4 must not cross: a gate that blocks on a count-proxy for a judgment question.** |
| Exactly one new workflow | CU-1's nightly job, with F-12 as a second **section** of it. **No new agent, no new skill, no new POML block, no second home for reuse rules.** |
| Every phase shipped standing alone | Each of P1–P5 satisfied §3.0's three tests and delivered its capability without the phases after it |
| **Test growth is visible, ungated** | Skip count, Flaky-tag count and net delta appear in the nightly issue; a new test names its KEEP category; **no threshold was added to any of them** |
| Complexity stayed bounded | Every phase declared its net new concepts, and none exceeded one |

---

## 6. Open questions for the owner

1. **`review-checklist.md` naming** — `code-review.md` (owner's original suggestion) reads like the *output* of a review; `review-checklist.md` or `review-scope.md` reads like the input, which is what it is. Minor, but in a repo where an agent picks files by name it is worth a moment. **Blocks nothing before P5.**
2. **The three misleading CI docs** — fix them inside r4 P5 (they are the direct consequence of F-12 and cheap while we are there), or file separately so P5 stays purely mechanical? **Recommendation: fix in P5.** Leaving docs that describe a nonexistent workflow while shipping the real one would be the exact drift r4 exists to stop.
3. **F-9 prioritisation** — confirm that F-6's citation counts are the right blast-radius proxy. It puts **ADR-021 (3,061 citations, no test)** and **ADR-028 auth (1,521, no ADR-named test)** near the front. ADR-028 may already be *effectively* enforced by the unnamed auth guards, which part 1's classification will determine.
4. **CU-1 usage-weight shape** — the U-curve is the right instinct; the thresholds for "heavily used" and "never used" are a first guess. Start with terciles from the F-6 baseline and tune after two months of real output, or set them now?

### 6.1 Resolved (recorded so they are not re-litigated)

| Question | Decision | Date |
|---|---|---|
| Scope: minimum viable cut, or the whole thing? | **Full P1–P5**, each a deployable, functionally complete, independently valuable capability per §3.0. Every phase is a valid stopping point by construction. | 2026-09-03, owner |
| F-9 batch sizing — count or budget? | **Neither.** A **functional criterion**: every `enforceable`+`current` ADR whose violation is silent *and* expensive. Done when the set is covered, whatever its size. | 2026-09-03, owner |
| CU-3 — include or defer the `task-execute` edit? | **Moot.** The three-tier review with a Tier-1 auto-bump removed the Step 11 edit entirely. Capability preserved, highest-collision change eliminated. | 2026-09-03 |
| One project or split reuse from enforcement? | **One project, two workstreams** — the r3 precedent. A second project buys ceremony and *increases* hot-path collision risk. | 2026-09-03, owner |
| Is the objective duplication? | **No.** Code quality and efficiency; duplication is an indicator. The two real risks are **rebuilding what exists** and **the same function in two code paths** (§0.1). | 2026-09-03, owner |
| Raise the ADR-012 promotion trigger to 3 consumers? | **No.** 2+ stays as a trigger to **evaluate**. A count is arbitrary; the three written questions do the work. Anticipatory promotion (`@spaarke/visuals`) is legitimate. | 2026-09-03, owner |
| Retroactively justify or un-promote any of the existing 15? | **No.** They are in the shared library and they stay. Enumeration makes the set knowable; it is not a cleanup exercise. | 2026-09-03, owner |
| Build a `duplication-reviewer` sub-agent (r5)? | **No — dropped entirely.** Reviewer agents produce differences of opinion without substantive value. F-5's question is retrieval and needs an index, not an opinion (§0.4). | 2026-09-03, owner |
| CU-1 threshold — 120 days? | **Replaced.** A calendar would have listed ~55 skills in the first issue. Ranking by `drift_signal × usage_weight` (U-shaped) reports the top N instead. | 2026-09-03, owner |
