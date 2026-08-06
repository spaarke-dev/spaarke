# Code Quality & Assurance R3 — Program Design Document

> **Status**: DRAFT — for owner review before `/design-to-spec`
> **Reframed**: 2026-08-06 (was a 2026-03-15 "long-tail polish" draft; **resurrected + rebaselined** as an umbrella program)
> **Provenance**: Supersedes the 2026-03-15 R3 draft (archived verbatim at [`notes/design-r3-original-2026-03-15.md`](notes/design-r3-original-2026-03-15.md)). Same slot, fresh content — the original R3 was never executed (design-only; no spec/tasks/branch), so no shipped work is overwritten. Lineage stays gapless: **r1 (system) → r2 (structural) → r3 (program).**
> **Predecessors**: [`code-quality-and-assurance-r1`](../code-quality-and-assurance-r1/) (executed — tooling, scorecard, PR/nightly automation; grade C→B) · [`code-quality-and-assurance-r2`](../code-quality-and-assurance-r2/) (executed, 17 tasks ✅ — God classes, dead code, memory leaks, deprecated PCF controls; grade B→A-)
> **North star**: *If a panel of senior architects/developers expert in web applications, Power Apps custom development, and enterprise solutions reviewed this codebase, it would earn an A+.*

---

## 0. Why this is being reframed (read first)

The 2026-03-15 R3 draft targeted the "last 5 points to A+" against a March baseline: 12 long-tail items, ~38h, claimed **A (95/100)**. That baseline is **no longer credible.** Since March the codebase roughly doubled in active surface — the entire 2026-Q2/Q3 wave landed (AI architecture redesign r1/r2, Compose r1–r5 + fidelity, Communication/Email r4/r5 + messaging r1–r3, Notification Spine, Modal System, Teams App, external-access SPA, and more — **30 active worktrees, 19 touching the BFF** per [`projects/INDEX.md`](../INDEX.md)).

A single verified surface pass (BFF, 2026-08-05/06) already proves the drift: it found a **broken production path** (invoice financial-totals: `IDataverseService as ServiceClient` casts that always throw), ~**2,700 LOC of dead production code**, 13 copy-pasted downcast helpers, 4 AI-facade boundary violations, and an **unauthenticated Dataverse-write endpoint** — none of which the March grade reflects. The old R3's own item #10 (two parallel Dataverse implementations) and item #8 ("God-class audit… feeds future R4") anticipated exactly this.

**Conclusion**: R3 is no longer a polish sprint. It is a **standing quality *program*** that (a) re-baselines the grade honestly, (b) drives verified per-surface assessments to A+, and (c) hardens the forcing-functions so the grade *holds* as the codebase keeps growing. It runs as a **single project in one worktree** (owner decision, §4), with each surface as a workstream.

---

## 1. Problem statement

R1 built the quality *system* (tooling, scorecard, CI/nightly gates). R2 did the first *structural* remediation. Then the program went dormant for ~5 months while the codebase grew faster than the assurance layer could keep up. The result is **grade drift**: new code (much of it AI/Compose/Communication) accumulated debt below the enforcement threshold, and at least one real correctness/security regression shipped. Reaching — and *holding* — an A+ senior-panel grade now requires a coordinated, repeatable, multi-surface program, not another one-off sprint.

---

## 2. Re-baseline (the honest current grade)

The March "A (95/100)" is treated as **stale and unverified.** R3 Phase 0 re-scores every surface against the rubric (§5) using the verified assessment method (§6). What we know today:

- **BFF** — assessed 2026-08-05/06. Structurally strong (A– on DI/gating; publish 46.89 MB compressed, healthy) but carrying 1 broken production path, ~2.7k LOC dead code, and an auth exposure → **surface grade ~B/B–, remediation scoped** (BFF workstream #1).
- **Every other surface** — **not yet re-scored.** No credible current grade until Phase 0 runs.

We do **not** publish an aggregate grade until Phase 0 completes. The re-baseline is a deliverable, not an assumption.

---

## 3. Reconciliation with the archived R3 draft (the 12 items)

Best-read disposition; each `VERIFY` is confirmed during the Phase-0 re-baseline of its surface. Full original at [`notes/design-r3-original-2026-03-15.md`](notes/design-r3-original-2026-03-15.md).

| # | Original R3 item | Disposition in this program |
|---|---|---|
| 1 | Complete `OfficeService.cs` decomposition (1,951 LOC God class) | **CARRY → BFF / shared-server workstream.** Re-measure current LOC (R2 partially decomposed). `VERIFY`. |
| 2 | Fix 3 remaining ADR-022 violations (ReactControl pattern) | **CARRY → PCF surface assessment.** Re-scan (controls changed since March). `VERIFY`. |
| 3 | `@spaarke/auth` proper workspace package | **CARRY → shared-client-libs surface.** Directly relevant to the React-version-drift + build-hygiene findings. `VERIFY`. |
| 4 | Integration-test infrastructure (138 failing, KV-dependent) | **CARRY → test-quality horizontal.** Reconcile with ADR-038 + `/test-diet`; re-count. `VERIFY`. |
| 5 | ESLint warning reduction (181) + `--max-warnings 0` gate | **CARRY → forcing-functions (§9) + client surfaces.** Re-count; make the CI gate a make-it-stick deliverable. `VERIFY`. |
| 6 | `console.log` cleanup → shared logger (117) | **CARRY → client surfaces + `no-console` gate.** `VERIFY`. |
| 7 | PCF build infrastructure (memory limits, per-control CI) | **CARRY → build/config-sprawl surface (69 `package.json` roots).** `VERIFY`. |
| 8 | God-class audit (>800 LOC / >12 ctor deps) | **ABSORB → this program's re-baseline method (§6).** Item explicitly said "feeds future R4" — this is it. Generalized into the rubric's "architecture" dimension. |
| 9 | `BaseProxyPlugin` full inversion vs decommission (ADR-002) | **CARRY → plugins/data-model surface.** Small surface; decide invert-vs-decommission. `VERIFY`. |
| 10 | Two parallel Dataverse implementations | **UPGRADED — now a live bug, not just a smell.** BFF pass confirmed `DataverseServiceClientImpl` vs raw-HTTP stack + always-failing casts. Assessment/consolidation split: bug-fix + downcast-collapse owned by the **BFF workstream**; full stack-unification is a **separate architecture project (NG)**. |
| 11 | Unit-test coverage gaps in R2-decomposed services | **CARRY → test-quality horizontal.** Coverage = observation, not gate (ADR-038). `VERIFY`. |
| 12 | PCF bundle-size optimization (tree-shaking, `pcf-scripts`-blocked) | **CARRY → client surfaces (informational).** Note the tooling block still applies. `VERIFY`. |

Net: **no item dropped silently.** Most fold into a surface assessment or the forcing-functions layer; #8 becomes the method itself; #10 is upgraded and split across a child + a deferred architecture project.

---

## 4. Program structure — one program, one worktree

**Decision (owner, 2026-08-06): this is a SINGLE project executed in ONE worktree.** There are no separate per-surface worktrees, branches, or PRs — "different child projects" is semantic bookkeeping, and all the work gets done regardless of how it's foldered. Each surface (BFF, shared libs, PCF, Dataverse model, etc.) is a **workstream** — a phase-group in this project's single `TASK-INDEX.md`, executed on the one `work/code-quality-and-assurance-r3` branch.

**This program owns end-to-end:**
- The **rubric + scorecard** (§5) and the Phase-0 **re-baseline**.
- The **assessment method** (§6, a multi-agent Workflow) and **surface sequence** (§7).
- The **horizontal sweeps** (§8) — security, tests, dependencies, observability.
- The **forcing-functions** (§9) — ArchTests, analyzers, CI gates — so the grade holds.
- Every surface **workstream's** remediation, executed as small PRs off the one branch.

Surface *folders* (e.g. [`bff-api-cleanup-remediation-r1`](workstreams/bff-api/)) may exist as **semantic organization for a workstream's design/findings** — they are not separate execution units. Keeping or merging them is cosmetic.

### Surface-workstream registry (all executed in this one project)

| # | Surface / workstream | Design | Status |
|---|---|---|---|
| 1 | BFF API (`Sprk.Bff.Api`) | [`bff-api-cleanup-remediation-r1/design.md`](workstreams/bff-api/) | **assessed + design drafted** 2026-08-06; 2 prod bugs + security decision; A/B tranche split (bugs+hygiene first, wide consolidations later) |
| 2 | Shared client libs (`Spaarke.*`, 16 pkgs, ~39k LOC) | assessment pending | not started — **highest leverage next** |
| 3 | Shared server libs (`Spaarke.Core/Dataverse/Scheduling`, ~10k LOC) | assessment pending | not started — roots the two-Dataverse-stacks question |
| 4 | PCF controls (36, ~49.5k LOC) | assessment pending | not started — lifecycle + dead-control sweep (cf. prior `pcf-orphan-cleanup-r1`) |
| 5 | Dataverse data model + solution ALM | assessment pending | not started — Power-Apps panel scrutinizes hardest |
| 6 | Code Page solutions (35, ~68k LOC) + build/config sprawl (69 `package.json`) | assessment pending | not started |
| — | Horizontals (security · tests · deps · observability) | §8 | not started |

The program carries **one** hot-path declaration (§11) covering every surface it touches — not one per workstream.

---

## 4A. Coordination model — one worktree, so alignment is internal

**The single-worktree decision (§4) is itself the coordination answer.** With all surfaces executed as phases in one `TASK-INDEX.md` on one branch, there are **no inter-project seams to manage** — no cross-child branches, PRs, quiet-windows, or hand-offs to keep in sync. Sequencing surfaces is ordinary intra-project phase ordering. What remains is only *consistency across surfaces* (so every surface is scored and fixed the same way) and *contention with **other teams'** worktrees* (the real external risk). Both are cheap:

### Consistency comes from shared artifacts, written once

1. **`docs/standards/CODE-QUALITY-RUBRIC.md`** — the single scoring contract (D1–D11). Every surface workstream is measured on the same ruler.
2. **The `quality-assessment` multi-agent Workflow** (§6) — every surface runs the identical fan-out → adversarial-verify → design method. Consistency lives in the tool, not in coordination.
3. **`notes/SCORECARD.md`** — one living scorecard in this project; each surface appends its row at wrap-up. The aggregate A+ view.

These are authored once (early phases) and then referenced by every subsequent surface — alignment by construction, not by messaging.

### Forcing-functions: authored once, activated per surface

The program *authors* the enforcement gates (ArchTests, analyzers-as-errors, lint/CVE/size CI gates) in §9, and each surface **flips its own gate on as the last step of its workstream** — so enforcement is never turned on repo-wide while another surface is still dirty. No big-bang, and because it's all one branch, no cross-project timing negotiation at all (supersedes open-question #4).

### External contention (the only real coordination) reuses existing machinery

The one broad branch touches BFF and other hot paths, so it contends with the ~19 other BFF worktrees. This is handled by the mechanisms every BFF project already uses — **no new process**: this program's row in [`projects/INDEX.md`](../INDEX.md) with its hot-path declaration (§11), plus `/conflict-check` before **every** remediation PR. Assessments are read-only → conflict-free → run anytime; remediation lands as **small, per-surface PRs** off the one branch, sequenced into quiet windows for the most-contested surfaces (BFF first-tranche excepted).

### Tracking rollup is automatic (portfolio)

One Project Issue (this program) under the existing **`Code Quality` Epic** (§15 Q3). The `/devops-*` auto-hooks sync task-count / completion / status to Project #2 on every `/task-execute`; the Epic aggregates. `notes/SCORECARD.md` is the qualitative companion to the portfolio's quantitative rollup. No per-surface Issues required.

### Net

The owner's single-worktree choice **eliminates** the cross-project back-and-forth that a multi-project structure would have created: one branch, one TASK-INDEX, one hot-path declaration, one Project Issue. The only standing coordination is `/conflict-check` against other teams before each PR — the same discipline every BFF worktree already follows.

---

## 5. The quality rubric (the A+ anchor)

Every surface is scored against the **same** dimensions so grades are comparable and progress is measurable. This extends the R1 scorecard into a standing standard (deliverable: `docs/standards/CODE-QUALITY-RUBRIC.md`). Dimensions the senior panel grades:

| # | Dimension | What A+ looks like |
|---|---|---|
| D1 | **Architecture & boundaries** | Clean layering; ADR adherence; no cross-boundary coupling (e.g. facade rules); no God classes / captive deps |
| D2 | **Correctness & reliability** | No latent broken paths; defensive edges; deterministic behavior |
| D3 | **Security** | Auth on every data path; secrets in KV; input validation; XSS/injection boundaries; least privilege |
| D4 | **Performance & scalability** | No N+1 where avoidable; bounded caches; publish/bundle-size budgets; async correctness |
| D5 | **DRY / dead code** | One place per concept; no copy-paste; no orphaned/superseded code |
| D6 | **Consistency & conventions** | Uniform naming, structure, error handling, logging; matches surrounding code |
| D7 | **Testability & test quality** | ADR-038 KEEP categories; behavior over mocks; no scaffolding tests; green + trustworthy suite |
| D8 | **Dependency & supply-chain hygiene** | No HIGH CVEs; pinned/consistent versions; fresh lockfiles; no needless transitive bloat |
| D9 | **Observability** | Structured logs + correlation IDs; no PII in logs; telemetry on critical paths |
| D10 | **ALM / build hygiene** (Power-Apps + web) | Solution segmentation; PCF lifecycle correctness; reproducible builds; analyzers-as-errors; clean CI |
| D11 | **Knowledge/doc accuracy** | `.claude/` + `docs/` match code (no drift) |

Scoring: A–F per dimension per surface → surface grade → program aggregate. Published in a living scorecard.

---

## 6. The assessment method (repeatable, verified)

The method proven on the BFF pass. Every surface runs the same three phases:

1. **Fan-out investigation** — parallel read-only agents, one per rubric dimension (or dimension cluster), returning structured findings with `file:line` evidence. Read-only ⇒ **zero merge-conflict risk**; can run *now*, even while the codebase is busy.
2. **Adversarial verification** — a second pass (a stronger model, e.g. Fable) that verifies each finding, checks for test-only consumers / data-driven dispatch, and refutes false positives. *Non-negotiable* — the BFF verification pass caught 2 real bugs **and** corrected 2 false-positive "dead code" claims that were actually load-bearing.
3. **Prioritized remediation `design.md`** — findings → a surface design with severity, LOC, effort, risk, and a tranche split (low-conflict now / wide edits in a quiet window). Fed into `/design-to-spec` → tasks so findings become tracked work, not a doc that rots.

**Engine (locked, owner Q1): a multi-agent Workflow.** At program scale (5+ surfaces × 11 dimensions × adversarial verify), the Workflow tool is the most accurate *and* efficient way to run this — parallel finders per dimension, per-finding verification, synthesized report — and this fan-out+verify shape is its core use case. A reusable **`quality-assessment` workflow** is a first-order deliverable (§12) so each surface is one repeatable, adversarially-verified run. Manual agent fan-out (as used on the BFF pass) is the fallback only. Note: the Workflow tool requires the operator's per-run opt-in ("use a workflow"), so surface-assessment turns are explicitly invoked that way.

---

## 7. Surface assessment sequence

Assessments are read-only and conflict-free, so **assessment can start immediately**; only *remediation* needs quiet windows. Ordered by leverage:

1. **Shared client libs** — nucleus consumed by PCF + Code Pages; fixes multiply. React-version drift, cross-package duplication, dead exports, bundle size.
2. **Shared server libs** — roots the Dataverse-stack question; consumed by BFF + plugins.
3. **PCF controls** — lifecycle/memory correctness, ADR-022, dead/retired controls (e.g. `AssociationResolver` retired per CLAUDE.md but still in-tree), 36-control duplication.
4. **Dataverse data model + ALM** — where Power-Apps expertise bites hardest: naming, option-sets, relationships, solution segmentation, field-mapping config.
5. **Code Page solutions + build/config sprawl** — 7 `Create*Wizard` duplication vs the shared wizard lib; retired-but-present solutions; 69 `package.json` roots; `npm ci` broken on ~14/16 Vite solutions.
6. **Plugins** — small; invert-vs-decommission `BaseProxyPlugin` (old item #9).

(BFF = surface #1, already assessed.)

## 8. Horizontal sweeps (umbrella-owned)

Cross-cutting, run as repo-wide passes rather than per-surface:
- **Security** — auth consistency (BFF pass found gaps), secrets, XSS/injection boundaries, token handling in `@spaarke/auth`, CORS.
- **Test quality** — against ADR-038 KEEP categories + `/test-diet`; the 138-failing-integration-test reconciliation (old item #4).
- **Dependency & CVE hygiene** — `dotnet list package --vulnerable`; `npm audit`; version-pin consistency; lockfile freshness.
- **Observability** — logging consistency, correlation IDs, PII-in-logs.
- **Doc-drift** — `doc-drift-audit` across `.claude/` + `docs/`.

## 9. Forcing-functions — making A+ *hold* (the make-it-stick layer)

A+ is sustained, not achieved once. Convert findings into enforced invariants (extends R1's system):
- **Expand `Spaarke.ArchTests`** into real fitness functions — e.g. "no non-AI code injects AI-internal types," "no `IDataverseService as ServiceClient` casts," layer-dependency rules, God-class LOC/ctor-dep thresholds.
- **Mechanical baseline** — `TreatWarningsAsErrors=true` + Roslyn analyzers + `.editorconfig` (C#); strict ESLint (`--max-warnings 0`, `no-console`) + `tsc --noEmit` (TS). *Table-stakes an A+ panel assumes.*
- **CI gates** — CVE scan, publish/bundle-size budgets, doc-drift — wired into the existing PR/nightly layers from R1.

## 10. Sequencing & coordination

> The alignment model (shared artifacts, per-surface gate activation, single-worktree rationale) is in **§4A**. This section covers only *timing/merge* sequencing.

- **One branch, surfaces as phases.** All surface workstreams execute on `work/code-quality-and-assurance-r3`, sequenced in one `TASK-INDEX.md`. No per-surface worktrees or branches.
- **Assess now, remediate in windows.** All assessments are read-only → start immediately regardless of the 30-worktree churn. Remediation lands as **small, per-surface PRs off the one branch**, sequenced into quiet windows for the most-contested surfaces.
- **BFF is the most-contested surface** (19 active projects) → its workstream uses the A/B tranche split (bugs+hygiene first, wide consolidations later). Less-contested surfaces (client libs, data model) can remediate sooner.
- **`/conflict-check` before every remediation PR** against [`projects/INDEX.md`](../INDEX.md). This program carries **one** hot-path declaration (§11) covering all surfaces it touches.

## 11. Hot-path declaration (single — covers all surfaces)

Because all surface remediation executes in this one worktree (§4), the program touches every hot path directly. **One declaration covers the whole program:**

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- BFF surface workstream: dead-code removal, downcast consolidation, 2 bug fixes, auth, facade -->
  <spaarkeai>Y</spaarkeai>     <!-- client-surface workstreams touch src/solutions/SpaarkeAi/** (shared libs, code pages) -->
  <ci-workflows>Y</ci-workflows>   <!-- forcing-functions §9: CVE/size/lint/doc-drift gates in .github/workflows -->
  <skill-directives>Y</skill-directives> <!-- rubric may update .claude/constraints + code-review/adr-check skills -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (§10 governance):** the code delta is **net-negative** — this program removes/consolidates code (dead-code deletion, 13→1 downcast, folder migration) and adds no new BFF endpoints/services/packages. The only additive surface is the `UnwrapServiceClient` extension (in `Spaarke.Dataverse`, replaces 13 copies) and `PublicContracts/` facade methods (which *reduce* CRUD→AI coupling). Publish size is expected to **drop** (BFF baseline 46.89 MB compressed). `ci-workflows=Y` coordinates with `ci-cd-unit-test-remediation-r1` (owns existing-workflow edits). **This program's row must be added to [`projects/INDEX.md`](../INDEX.md)** so the ~19 other BFF worktrees see it; `/conflict-check` runs before every remediation PR.

## 12. Deliverables

- `docs/standards/CODE-QUALITY-RUBRIC.md` — the standing A+ rubric + scoring scale.
- **Re-baselined scorecard** (`notes/SCORECARD.md`) — verified per-surface + aggregate grade (Phase 0 output; supersedes the March "95/100").
- Per-surface remediation `design.md`s (workstream design docs) — BFF surface done.
- Expanded `Spaarke.ArchTests` fitness functions + analyzer/lint/CI gate config.
- Reusable **`quality-assessment` multi-agent Workflow** (the assessment engine — Q1).
- Reconciliation record of the archived R3 draft (§3).

## 13. Success criteria (draft — `/design-to-spec` to close the set)

- [ ] `CODE-QUALITY-RUBRIC.md` published; every surface scored against D1–D11.
- [ ] Honest re-baseline scorecard published (no unverified aggregate grade).
- [ ] Every surface has a verified assessment; each surface's remediation workstream is executed in this project.
- [ ] BFF surface executed (bugs fixed, dead code removed, auth closed).
- [ ] Forcing-functions live: ArchTests expanded, analyzers-as-errors on, lint/CVE/size/doc-drift CI gates green.
- [ ] Archived R3 draft fully reconciled (§3) — no item dropped silently.
- [ ] Aggregate grade reaches **A+ (senior-panel standard)** with forcing-functions preventing re-drift.

## 14. Risks

| Risk | Mitigation |
|---|---|
| Program sprawl / never "done" | Fixed rubric + `SCORECARD.md` makes progress measurable; each surface workstream is an independently-shippable PR set |
| Assessment findings rot unremediated | Every assessment → tracked tasks in the one TASK-INDEX; forcing-functions prevent re-drift |
| One broad branch contends with 19 BFF worktrees | Assess read-only anytime; remediate as small per-surface PRs in quiet windows; `/conflict-check` each PR; single INDEX.md row keeps peers informed |
| False-positive deletions (as in the BFF false positives) | Adversarial verification phase is mandatory; check test-only consumers + data-driven dispatch |
| Overlap with in-flight projects (`ci-cd-unit-test-remediation-r1`, `redis-*`, surface audits) | Cite ownership in §10; `/conflict-check` + reconcile before scoping each surface |

## 15. Resolved decisions (owner, 2026-08-06)

All five open questions are now resolved; recorded here for the `/design-to-spec` handoff.

1. ~~Assessment engine — Workflow vs manual fan-out?~~ **RESOLVED → multi-agent Workflow** (most accurate + efficient). Build the reusable `quality-assessment` Workflow as the engine; manual fan-out is fallback only. See §6.
2. ~~Separate child projects vs fold-in?~~ **RESOLVED → single project, single worktree.** No per-surface worktrees/branches/PRs — separate "projects" is semantic; all work is done in this one project, surfaces as workstreams. See the reframed §4 + §4A.
3. ~~Portfolio registration?~~ **RESOLVED → register this program under the existing `Code Quality` Epic** (do not create a new epic). One Project Issue for the program; surfaces tracked as workstreams (no per-surface Issues). Verify the Epic Issue number at execution; verify no orphan R3 Issue first.
4. ~~Forcing-function aggressiveness — repo-wide vs per-surface?~~ **RESOLVED → per-surface activation** (§4A): the program authors each gate; each surface flips its own on as its last workstream step. No repo-wide big-bang.
5. ~~Grade authority — self-score vs external panel?~~ **RESOLVED → self-score** against the rubric (D1–D11). No external-panel acceptance gate.
