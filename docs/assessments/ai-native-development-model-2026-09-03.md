# Spaarke AI-Native Development Model — State Assessment

> **Created**: 2026-09-03
> **Type**: Assessment (state of the system + gap list), not a procedure doc
> **Scope**: The whole development process — principles, components, end-to-end flow, gaps, and the continuous-update mechanism
> **Method**: Every claim below is a count or a path taken from the repo on 2026-09-03. Where a number is an estimate it says so.

---

## 1. Premise

In this repo, **AI does the component architecting and the code writing.** That is not a tooling detail; it changes what the process has to be built out of.

A human-written codebase is navigated by a mental model built through years of daily contact and corrected continuously by it. That model is an index, and it is real — it just isn't written down. It also walks out the door when someone leaves.

Generative AI has no such index and never accumulates one. Every session starts cold. So the process has to supply, per session, what a senior developer carries in their head — and it has to supply it in a form an AI actually consumes: loaded into context, or executed as a check, not filed somewhere for later reading.

**The design object is the AI:human interface** — which decisions the AI makes unilaterally, which it must stop and hand to a human, and what carries the answer back. Section 3 inventories that interface as it actually exists today.

This document is deliberately not a philosophy of AI development. It is *what we have, what's missing, and what to do about it*.

---

## 2. Guiding principles

Seven principles, each with what it rules out and where it is actually implemented. A principle with no implementation column is an aspiration, and is marked as such.

| # | Principle | What it rules out | Implemented in |
|---|---|---|---|
| **P1** | **The unit of work is one context window, not one sprint.** Work is decomposed into units executable in a single context, with all state recoverable from files. | "Keep going until it's done" as a work unit. Any state that lives only in the conversation. | 5,796 POML tasks / 205 project folders; `current-task.md`; `context-handoff` skill; CLAUDE.md §5 checkpoint thresholds |
| **P2** | **Write down only what the code cannot state.** Rationale, rejected alternatives, sanctioned-path choices, cross-file invariants, and scars. Everything derivable gets generated. | Hand-written inventories, hand-maintained API surface lists, docs that restate what a barrel file already owns. | 49 concise ADRs; 16 constraint files; `.claude/FAILURE-MODES.md` (905 lines). **Generation side is nearly absent** — see G1. |
| **P3** | **Documentation must be load-bearing, not descriptive.** A doc something depends on fails loudly; a doc that only describes drifts silently. | Prose rules with no mechanism. "We agreed not to do X" with nothing that fires when someone does X. | ADR → arch-test → blocking CI → weekly audit issue. **Covers 7 of 49 ADRs** — see G6. |
| **P4** | **Instructions are executed literally.** Specs must be closed sets — enumerated acceptance criteria including the negative cases, named files, named reference implementations. | "Handle errors appropriately." "Follow existing patterns." Anything requiring the executor to infer unstated intent. | CLAUDE.md §8.5; `task-create` closed-set acceptance criteria; `<steps mode="directional\|prescriptive">`; `<escalation><trigger>` |
| **P5** | **Generation and verification must not share a context.** The context that wrote the code is the worst available judge of it. | Treating an inline self-review as independent verification. | CI is the only true fresh-context verifier today (22 workflows, tier-1 blocking / tier-2 advisory). **`code-review` runs inline in the generating session** — see G2. |
| **P6** | **The human is the judgment layer, not the typing layer.** Human attention is spent on decisions that are genuinely the human's, and the process must route those decisions explicitly. | Humans reviewing generated code line-by-line as the primary quality mechanism. Also: AI silently deciding things it should have escalated. | CLAUDE.md §6 escalation triggers; §6.5 ADR conflict protocol (paths A/B/C); POML `<escalation>`; confirmation gates in `provision-environment` / deploy skills |
| **P7** | **The procedures obey their own reuse rule.** New skills, agents, gates, and workflows face the same three-question justification as new code. | A parallel governance system alongside a working one. Five overlapping mechanisms where one good one belongs. | CLAUDE.md §11 applied to procedures — e.g. the seven explicit rejections in `projects/code-quality-and-assurance-r4/design.md` §1.2 |

**The routing rule that follows from P2 + P3**, applied before writing any document:

> *Can the code answer this question?*
> **Yes** → generate it, or don't write it.
> **No** → write it, and pin it to a mechanism whose failure mode is a red build.

---

## 2.5 The three documentation classes

This is the taxonomy the routing rule operates on, and it is the frame the rest of this document uses. **Projects cite it from here rather than restating it.**

The standing objection to a repo like this one is *"documentation drifts; just read the code."* That objection is correct about one class of document and wrong about the other two. The distinction that matters is **derivable vs. non-derivable** — not human vs. AI.

| Class | What it is | Why it drifts (or doesn't) | Drift control | Spaarke today |
|---|---|---|---|---|
| **1 — Derivable** | Component inventories, export surfaces, endpoint lists, entity schemas. The code already states this authoritatively. | **Copies always drift.** ADR-012's sanctioned-package list drifted from an implied 4 to an actual 15 for exactly this reason. | **Generate it or census-test it. Never hand-write it.** | 🔴 One generated artifact (`spaarke-components-inventory.json`, 478 KB, one commit 2026-08-20, no regeneration wiring). **No code-side equivalent at all.** |
| **2 — Non-derivable** | Why a decision was made; what was rejected and why; which of two valid-looking paths is sanctioned; invariants no single file expresses; failure modes discovered by getting burned. | **Cannot drift from the code, because the code never contained it.** Drifts only when reality changes and nobody amends. | **Make it executable** — an ADR with an arch-test cannot silently reverse. | 🟡 Authoring strong (49 ADRs, 16 constraints, 905-line `FAILURE-MODES.md`); **enforcement 7/49**. |
| **3 — Navigational** | Intent → location. "To do X, start at Y." | Drifts when the target moves. | **Validate the paths** — a broken pointer should be a loud failure, not a stale paragraph. | 🟡 94 pattern files + the CLAUDE.md §17 table, validated by nothing. |

**Why the ordering matters for investment.** Class 2 is the only class that survives a 10× increase in context window or retrieval quality — the information was never in the repo to be found, so no amount of searching recovers it. Class 1 gets *cheaper to generate and less necessary*. Class 3 is partially obsoleted by better search.

`FAILURE-MODES.md` is the clearest case: `AP-1` ("skill prescribes X but X is wrong"), `AP-10` (the JSON-aware renderer that escapes one nesting level). Those are scars — empirically discovered, unrecoverable from reading the code, because the code shows the current state and not the three things that broke on the way there. A human carries these as intuition. An AI must be handed them, every session, forever. **This class gets more valuable as models improve, not less.**

---

## 3. The AI:human interface

This is the part the process is actually organized around. Eight defined handoff points exist today.

| # | Handoff point | Direction | Trigger | Mechanism | Status |
|---|---|---|---|---|---|
| **H1** | Design intake | Human → AI | Start of project | `design.md` authored by the owner, or `/use-case-to-design` | 🟢 Working |
| **H2** | Open questions | AI → Human | End of design, before `/design-to-spec` | "Open questions for the owner" section; blocks spec authoring | 🟢 Working — but answers are recorded ad hoc (G5) |
| **H3** | Rigor declaration | AI → Human (visible) | Task start | Mandatory `🔒 RIGOR LEVEL` output; human may override | 🟢 Working |
| **H4** | ADR conflict | AI → Human | Compliance would produce a worse outcome | CLAUDE.md §6.5 — AI presents paths A/B/C with rationale; **human chooses** | 🟢 Strong design; firing rate unmeasured (G9) |
| **H5** | Escalation trigger | AI → Human | Known judgment boundary hit mid-task | POML `<escalation><trigger>`; stopping is legitimate, not failure | 🟡 Present; firing rate unmeasured (G9) |
| **H6** | Quality gate findings | AI → Human | `task-execute` Step 9.5 | `code-review` + `adr-check` report at max recall; orchestrator filters; human adjudicates at PR | 🟡 Reviewer shares the generating context (G2) |
| **H7** | Irreversible-action confirmation | AI → Human | Deploy, provision, destructive ops | Literal-phrase gates (`proceed with provisioning`); bare "y" explicitly insufficient | 🟢 Working |
| **H8** | Project close | AI → Human | `090-wrapup-*` task | `/test-diet` emits `git rm`/`git mv` for reviewer judgment — read-only, never auto-executes | 🟢 Working |

**What the interface is missing**: every one of these produces a human decision, and there is no queryable record of those decisions. They land in PR comments, `projects/*/notes/`, and chat transcripts. When a later project asks "did we already decide this, and why," the answer is not retrievable — which is precisely the institutional-memory failure the whole model exists to solve, reproduced at the governance layer. See **G5**.

---

## 4. Components — what we have

| Layer | Artifact | Count / size | Class (§2.5) | Status |
|---|---|---|---|---|
| Binding rules, every turn | root `CLAUDE.md` + 4 module `CLAUDE.md` | ~225 lines root | 2 | 🟢 |
| Decision record | `.claude/adr/` concise + `docs/adr/` full | 49 + 48 | 2 | 🟡 7/49 enforced |
| Constraints | `.claude/constraints/` | 16 | 2 | 🟡 no review stamps |
| Navigation | `.claude/patterns/` pointer files | 94 | 3 | 🟡 unvalidated, no stamps |
| Scars | `.claude/FAILURE-MODES.md` | 905 lines | 2 | 🟢 strongest artifact we have |
| Procedure | `.claude/skills/*/SKILL.md` | 71 | process | 🟡 staleness (G4) |
| Sub-agents | `.claude/agents/` | **1** (`researcher.md`) | process | 🔴 underdeveloped (G7) |
| External knowledge | `knowledge/` | 17 topics + `REFRESH-PROCEDURE` + `REFRESH-LOG` | 2 | 🟡 cadence not honored (G4) |
| Work units | POML tasks / project folders | 5,796 / 205 | process | 🟢 |
| Executable enforcement | `tests/Spaarke.ArchTests/` | 35 test files | 3-as-mechanism | 🟡 coverage (G6) |
| CI/CD | `.github/workflows/` | 22 | mechanism | 🟢 tier-1 blocking + tier-2 advisory + weekly `adr-audit` + `nightly-health` |
| **Generated artifacts** | `spaarke-components-inventory.json` | **1**, 478 KB | **1** | 🔴 single commit 2026-08-20, no regeneration wiring anywhere |
| Change record | `.claude/CHANGELOG.md` | 648 lines | process | 🟡 mandatory for `CLAUDE.md` edits only |

**The shape of it**: Class-2 authoring is genuinely strong and unusual — most repos have nothing like `FAILURE-MODES.md` or 49 concise ADRs. Class-2 *enforcement* covers 14% of ADRs. Class-1 generation is one stale file. Class-3 navigation exists at scale (94 pointers) and is validated by nothing.

---

## 5. End-to-end process

| Stage | Entry | Produces | Enforced by |
|---|---|---|---|
| 0. Capture | `/devops-idea-create` | GitHub Issue, Type=Idea | — |
| 1. Promote | `/devops-idea-promote` | Project Issue | — |
| 2. Scaffold | `/devops-project-start` | Folder + worktree + `design.md` skeleton + `projects/INDEX.md` entry | Registry is atomic |
| 3. Design | Human, or `/use-case-to-design` | `design.md` incl. hot-path declaration, ADR tensions, component justification | `project-pipeline` Step 3 hard-warns on missing hot-path block |
| 4. Spec | `/design-to-spec` | `spec.md` — closed-set requirements (P4) | ADR Tensions section surfaces §6.5 conflicts up front |
| 5. Plan → tasks | `/project-pipeline` → `/task-create` | POML tasks with `<justification>`, rigor, model tier, effort, escalation triggers | §11 three-question gate at Step 3.5.6 |
| 6. Execute | `/task-execute` | Code + task notes; checkpoint every 3 steps | Rigor declaration; `current-task.md`; §5 thresholds |
| 7. Verify | Step 9.5 + push | `code-review` + `adr-check` findings; CI tier 1/2 | 35 arch tests, blocking tier 1 (P5 gap: reviewer is in-context) |
| 8. Integrate | `/conflict-check` → `/push-to-github` → PR → `/merge-to-master` | Merged branch | ~17 active worktrees make hot-path collision the live risk |
| 9. Close | `090-wrapup-*` | `/test-diet` report; `/doc-drift-audit`; `/devops-project-archive` | Test-diet gate is a hard warning at project close |
| 10. Maintain | `ai-procedure-maintenance`, `.claude/CHANGELOG.md`, weekly `adr-audit`, `nightly-health` | Propagated procedure updates | **Weakest link — §7** |

Stages 0–9 are well built and demonstrably used at scale (205 projects). **Stage 10 is where the model leaks**, and it leaks in a specific way: every maintenance mechanism we have is *triggered by a human noticing* or by *a project deciding to look*. Nothing converts elapsed time into a work item.

---

## 6. Gaps

Three categories. Every row carries its evidence.

### 6.1 Missing — does not exist today

| ID | Gap | Evidence | Fix | Owner |
|---|---|---|---|---|
| **G1** | **No Class-1 generation over source code.** Zero generated indexes of exports, endpoints, or services. The one generated artifact covers Dataverse solution components, not code. | 1 generated artifact repo-wide; no regeneration wiring in `.github/workflows/` or `scripts/` | Generate a shared-lib export index; regenerate in CI and fail on divergence | **r4 P4** (F-8, scoped to the 15 shared libs; regeneration rides CU-1's job) |
| **G2** | **No fresh-context verification.** `code-review` runs inline in the session that wrote the code (violates P5). | `.claude/agents/` contains exactly 1 file, `researcher.md`. No reviewer sub-agents exist. | Advisory functional-equivalence probe first; build the agent only if it finds real hits | **r4 P5** (F-5 probe) → r5 (the agent, if evidence supports it) |
| **G3** | **No pointer-path validation.** 94 pattern files + the CLAUDE.md §17 pointer table reference paths nothing checks. | `Find-SkillReferenceDrift.ps1` is referenced by `ai-procedure-maintenance` as a deliverable — **it does not exist** | A link-check script over `.claude/**` + `docs/**`, advisory. Class-3 control, cheap. | **r4 P3** — folded into CU-1's job rather than built standalone |
| **G4** | **No recurring review cadence — and we have declared one twice.** All procedure maintenance is project-triggered. | 55 of 71 skills last reviewed ≥3 months ago (35 stamped 2026-05, 20 stamped 2026-06). **`projects/ai-procedure-quality-r1/README.md` declared *"Cadence after launch: monthly proactive audit; per-commit validation; quarterly deep audit"* — and 55 skills still carry that project's own 2026-05/06 stamp.** `knowledge/REFRESH-PROCEDURE.md` states **"Cadence: Monthly"**; `REFRESH-LOG.md` shows 3 refreshes in 4 months, every one project-triggered. **Two written cadences, zero recurring runs.** | §7 CU-1 — a mechanism that fires without a human remembering. **A third written cadence would fail identically.** | **r4 P3** |
| **G5** | **No record of human decisions.** H1–H8 each produce a decision; none has a queryable home. | Decisions live in PR comments, `projects/*/notes/`, and chat | A decision log with a fixed format, written at the point of decision (§6.5 already mandates this for ADR conflicts — generalize it) | **Unowned — deferred out of r4** (see §7.4) |

### 6.2 Underdeveloped — exists but thin

| ID | Gap | Evidence | Fix | Owner |
|---|---|---|---|---|
| **G6** | **ADR enforcement at 7/49 (14%).** The loop is complete and proven — only its coverage is thin. | Named tests exist for ADR-001, 002, 007, 008, 009, 010, 013 (+ ADR-038 ban guard). 42 ADRs are prose that can be violated silently. ~18 further guard tests encode invariants without ADR numbers. | Classify all 49 `enforceable`/`partial`/`judgment-only`, then a prioritized batch by blast radius. **Not "write 42 tests."** | **r4 P2** (classify + enforce + measure, delivered whole) |
| **G7** | **Agent layer is 1 file.** The only sub-agent is `researcher`. | `.claude/agents/researcher.md` | Gated on G2's evidence — do not build agents speculatively (P7) | **r5**, gated on r4 P5's F-5 evidence |
| **G8** | **`knowledge/` refresh is reactive.** Content is good; currency depends on a project needing it. | Last 3 log entries all cite a specific project task (`sdap-SPE-admin-app-r2` task 061, `email-communication-solution-r4` task 076) | Fold into CU-1's staleness report | **r4 P3** |
| **G9** | **Escalation is unmeasured.** We cannot tell whether H4/H5 fire appropriately, too often, or never. | No count of `<escalation><trigger>` firings across 5,796 tasks | Count firings + outcomes in the F-6 baseline | **r4 P1** (F-6, extended) |

### 6.3 Needs update — stated rules that no longer match reality

| ID | Item | Evidence | Fix | Owner |
|---|---|---|---|---|
| **G10** | ADR-012's sanctioned-package list ends in **"etc."** while requiring an amendment for new siblings | 4 named, 11 covered by "etc.", 15 actual | Enumerate + census test | **r4 P1** (F-1 + F-4 amendment, one phase) |
| **G11** | Review stamps are incomplete and absent outside skills | 63/71 skills stamped (1 is a literal `YYYY-MM` placeholder); **0/16 constraints and 0/94 patterns carry any stamp** | Require the stamp; assert the count with a census test — same pattern as `CredentialCensusTests` | **r4 P3** (CU-2) |
| **G12** | `ai-procedure-maintenance` cites tooling that was never built | `Find-SkillReferenceDrift.ps1` — not found | Build it (G3) or remove the reference. Either is fine; the current state is the one that isn't. | **r4 P3** — CU-1's link check makes it real |

---

## 7. Continuous update — the mechanism that keeps this current

This is the load-bearing section. Everything above degrades without it, and the evidence in G4/G8/G11 says degradation is already happening.

### 7.1 What exists

| Mechanism | Trigger | Covers | Honest assessment |
|---|---|---|---|
| `ai-procedure-maintenance` | Human invokes on new ADR/pattern/constraint/skill | Cross-reference propagation | Good checklist. Fires only when someone remembers. |
| `.claude/CHANGELOG.md` | Mandatory for every PR touching root `CLAUDE.md` | The single hottest file | 🟢 Works — because it's mandatory and scoped to one file |
| `doc-drift-audit` | On-demand, "at project transitions" | Stale paths, deleted refs, broken links | Well-designed and diff-based. Not wired to anything automatic. |
| `adr-audit.yml` | **Weekly cron** | Runs `ArchTests`, files an idempotent tracking issue | 🟢 Real automation — but only sees the 7 ADRs that have tests |
| `nightly-health.yml` | **Nightly** | Flake hunt, bundle size, vuln scan | 🟢 Real automation — rolling issue, no doc/procedure coverage |
| `last-reviewed:` frontmatter | Written during review | 63/71 skills | A stamp nobody reads is a date, not a mechanism |
| `test-diet` at project close | `090-wrapup-*` task | Test-suite reconciliation | 🟢 Proof the project-close hook pattern works |

### 7.2 The actual problem

Two automated jobs run on a schedule; both watch code, neither watches the procedure surface. Everything that maintains the AI-native layer is triggered by a human noticing, or by a project happening to touch that area.

The result is measurable, and it is the same failure in three places:

- `projects/ai-procedure-quality-r1` declared **"monthly proactive audit; quarterly deep audit"** at launch — and **55 of 71 skills still carry that project's own 2026-05/06 review stamp.** The audit it chartered has never run.
- `knowledge/REFRESH-PROCEDURE.md` states a **monthly** cadence; actual refreshes are **project-triggered**, 3 in 4 months
- The one Class-1 artifact was generated **once**, on 2026-08-20, and has no regeneration path

**Nothing converts elapsed time into a work item.** That is the whole gap, stated precisely — and the first bullet is the proof that stating it again in prose will not fix it. **We have written the cadence down twice. Neither one fired.** The deliverable has to be a scheduled job, not a policy.

### 7.3 Proposed mechanism — five items, in priority order

Each is small, uses a pattern already proven in this repo, and creates no new subsystem (P7).

| ID | Item | What it does | Cost | Precedent it copies |
|---|---|---|---|---|
| **CU-1** | **Procedure staleness report** | Weekly job → one rolling issue listing: stamps older than 120 days; unstamped files; broken pointer paths (G3); Class-1 artifacts whose regeneration produces a diff; unclassified ADRs. **Advisory, never blocking.** | One workflow file + one script | `nightly-health.yml` — rolling issue, clean runs close it |
| **CU-2** | **Stamp census** | Assert that every `.claude/skills/*/SKILL.md`, `constraints/*.md`, and `patterns/**/*.md` carries a `last-reviewed`. Adding an unstamped file fails the build. | One test file | `CredentialCensusTests` — "the COUNT itself is the assertion" |
| **CU-3** | **Project-close procedure hook** | Extend the `090-wrapup-*` gate: for every `.claude/` file the project touched, bump the stamp and run `doc-drift-audit` on the diff. Turns per-project work into procedure maintenance with no separate project. | `task-execute` Step 11 edit | `/test-diet` at the same gate |
| **CU-4** | **Coverage as a tracked number** | ADR enforcement `n/49` appears in `adr-audit.yml`'s issue body every week, so it is visible without an audit. | Issue-body edit | Already in F-9 part 3 |
| **CU-5** | **Class-1 regeneration policy** | Any generated artifact regenerates in CI and fails on divergence. A hand-edited generated file means the generator is broken. | Policy + per-artifact wiring | F-8's rule, generalized |

**Why this shape and not a bigger one**: the failure mode to avoid is a maintenance system that itself needs maintaining. CU-1 is one advisory issue a week. CU-2 is one test. CU-3 is an edit to a gate that already runs. None of them can block normal work, which is the specific lesson from the retired God-class LOC ratchet (`docs/standards/COMPONENT-COMPLEXITY.md`, retired 2026-08-20).

### 7.4 Ownership — one project, phased

Everything above is owned by **`code-quality-and-assurance-r4`**, a single project in a single worktree with two workstreams — the same structure the owner chose for r3 (surfaces = workstreams, not child projects). Design: [`projects/code-quality-and-assurance-r4/design.md`](../../projects/code-quality-and-assurance-r4/design.md).

Each phase is a **capability**, not a batch of tasks — deployable, functionally complete, and independently valuable on its own (r4 design §3.0). Phases are sized by what makes their capability whole, never by a task count or a time box, so **r4 can stop cleanly after any phase**.

| Phase | Capability | Gaps closed |
|---|---|---|
| **P1 — The shared surface is bounded** | A 16th `@spaarke/*` package cannot enter without meeting criteria and amending ADR-012 | G10, G9 |
| **P2 — ADR enforcement is known, enforced, measured** | Classification of all 49 + tests for every silent-and-expensive enforceable ADR + weekly `n/49` | **G6** |
| **P3 — The governance surface maintains itself** | Weekly decay report **and** the project-close hook that clears it | **G4**, G3, G8, G11, G12 |
| **P4 — The reuse gate can escalate and record** | A real second rung past a failed grep; a `promote` verdict with a ledger | G1 (partial) |
| **P5 — Evidence for r5** | A written go/no-go on the duplication-reviewer agent, backed by measured hit-rate | G2 (probe) |

**On P3 containing CU-3**: CU-1 without CU-3 is a smoke alarm with no extinguisher — a weekly report of ~110 stamps aging past threshold with no mechanism that ever clears them, which is exactly how the two dead cadences failed. Splitting them would leave P3 functionally incomplete, so the `task-execute` Step 11 edit is in scope and handled with a dedicated `/conflict-check` pass.

**Deferred out of r4, deliberately**: **G5** (decision record) and **CU-5** (Class-1 policy generalization) — both real, neither urgent, and adding them would make r4 the large project it is specifically scoped not to be. **G7** (agent layer) is gated on P5's evidence: do not build agents speculatively (P7).

---

## 8. The three things that matter most

1. **G6 / F-9 — ADR enforcement, 7/49.** Highest durable value in the entire list. The mechanism is built, proven, and wired into CI; only coverage is thin, and the marginal cost is one test file per ADR. It is Class 2, which means it stays valuable regardless of how good models or context windows get. → **r4 P2**
2. **G4 + CU-1 — nothing converts elapsed time into work.** Everything in this document decays without it, the decay is already measurable at 55/71 skills, and **we have now declared a written cadence twice without either one firing.** A third declaration would fail identically; the deliverable is a scheduled job, not a policy — paired with the project-close hook that actually clears what it reports. → **r4 P3**
3. **G2 / P5 — the reviewer shares the generating context.** The most structurally significant flaw in the pipeline. Correctly scoped as an advisory probe first (F-5): gather evidence before building the machinery. → **r4 P5, then r5 if the evidence supports it**

---

## 9. What this assessment does not claim

- That the 42 unenforced ADRs are being violated. They are *unverifiable*, which is different, and the F-9 classification is what converts that unknown into a known.
- That every ADR should have a test. Some are judgment rules; forcing tests onto them repeats the God-class-ratchet mistake in a new costume.
- That 15 shared packages is too many. The claim is that we cannot currently tell — there is no list to check against.
- That the process is failing. 205 projects and 5,796 tasks say otherwise. The gaps here are maintenance debt in a working system, not architectural failure.
