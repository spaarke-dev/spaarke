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

**Conclusion**: R3 is no longer a polish sprint. It is a **standing quality *program*** that (a) re-baselines the grade honestly, (b) drives verified per-surface assessments to A+, and (c) hardens the forcing-functions so the grade *holds* as the codebase keeps growing. This document is the umbrella; deep surfaces spin out as tracked child projects.

---

## 1. Problem statement

R1 built the quality *system* (tooling, scorecard, CI/nightly gates). R2 did the first *structural* remediation. Then the program went dormant for ~5 months while the codebase grew faster than the assurance layer could keep up. The result is **grade drift**: new code (much of it AI/Compose/Communication) accumulated debt below the enforcement threshold, and at least one real correctness/security regression shipped. Reaching — and *holding* — an A+ senior-panel grade now requires a coordinated, repeatable, multi-surface program, not another one-off sprint.

---

## 2. Re-baseline (the honest current grade)

The March "A (95/100)" is treated as **stale and unverified.** R3 Phase 0 re-scores every surface against the rubric (§5) using the verified assessment method (§6). What we know today:

- **BFF** — assessed 2026-08-05/06. Structurally strong (A– on DI/gating; publish 46.89 MB compressed, healthy) but carrying 1 broken production path, ~2.7k LOC dead code, and an auth exposure → **surface grade ~B/B–, remediation scoped** (child project #1).
- **Every other surface** — **not yet re-scored.** No credible current grade until Phase 0 runs.

We do **not** publish an aggregate grade until Phase 0 completes. The re-baseline is a deliverable, not an assumption.

---

## 3. Reconciliation with the archived R3 draft (the 12 items)

Best-read disposition; each `VERIFY` is confirmed during the Phase-0 re-baseline of its surface. Full original at [`notes/design-r3-original-2026-03-15.md`](notes/design-r3-original-2026-03-15.md).

| # | Original R3 item | Disposition in this program |
|---|---|---|
| 1 | Complete `OfficeService.cs` decomposition (1,951 LOC God class) | **CARRY → BFF-adjacent / shared-server surface.** Re-measure current LOC (R2 partially decomposed); fold into BFF or a server-surface child. `VERIFY`. |
| 2 | Fix 3 remaining ADR-022 violations (ReactControl pattern) | **CARRY → PCF surface assessment.** Re-scan (controls changed since March). `VERIFY`. |
| 3 | `@spaarke/auth` proper workspace package | **CARRY → shared-client-libs surface.** Directly relevant to the React-version-drift + build-hygiene findings. `VERIFY`. |
| 4 | Integration-test infrastructure (138 failing, KV-dependent) | **CARRY → test-quality horizontal.** Reconcile with ADR-038 + `/test-diet`; re-count. `VERIFY`. |
| 5 | ESLint warning reduction (181) + `--max-warnings 0` gate | **CARRY → forcing-functions (§9) + client surfaces.** Re-count; make the CI gate a make-it-stick deliverable. `VERIFY`. |
| 6 | `console.log` cleanup → shared logger (117) | **CARRY → client surfaces + `no-console` gate.** `VERIFY`. |
| 7 | PCF build infrastructure (memory limits, per-control CI) | **CARRY → build/config-sprawl surface (69 `package.json` roots).** `VERIFY`. |
| 8 | God-class audit (>800 LOC / >12 ctor deps) | **ABSORB → this program's re-baseline method (§6).** Item explicitly said "feeds future R4" — this is it. Generalized into the rubric's "architecture" dimension. |
| 9 | `BaseProxyPlugin` full inversion vs decommission (ADR-002) | **CARRY → plugins/data-model surface.** Small surface; decide invert-vs-decommission. `VERIFY`. |
| 10 | Two parallel Dataverse implementations | **UPGRADED — now a live bug, not just a smell.** BFF pass confirmed `DataverseServiceClientImpl` vs raw-HTTP stack + always-failing casts. Assessment/consolidation split: bug-fix + downcast-collapse owned by **BFF child #1**; full stack-unification is a **separate architecture project (NG)**. |
| 11 | Unit-test coverage gaps in R2-decomposed services | **CARRY → test-quality horizontal.** Coverage = observation, not gate (ADR-038). `VERIFY`. |
| 12 | PCF bundle-size optimization (tree-shaking, `pcf-scripts`-blocked) | **CARRY → client surfaces (informational).** Note the tooling block still applies. `VERIFY`. |

Net: **no item dropped silently.** Most fold into a surface assessment or the forcing-functions layer; #8 becomes the method itself; #10 is upgraded and split across a child + a deferred architecture project.

---

## 4. Program structure — two tiers

The repo already runs this shape (surface deep-dives like `bff-ai-architecture-audit-r1`, `spaarke-ai-code-audit-r1`, `pcf-orphan-cleanup-r1`, `spaarke-ui-functional-cleanup-r1` spun out alongside the `code-quality-and-assurance-rN` program). R3 formalizes it.

**Tier 1 — this umbrella (`code-quality-and-assurance-r3`)** owns:
- The **rubric + scorecard** (§5) and the Phase-0 **re-baseline**.
- The **assessment method** (§6) and **surface sequence** (§7).
- The **horizontal sweeps** (§8) that don't belong to one surface (security, tests, dependencies, observability).
- The **forcing-functions** (§9) — ArchTests, analyzers, CI gates — so the grade holds.
- **Coordination** across surface children + the 30-worktree contention (§10).

**Tier 2 — surface child projects** own the *deep remediation* of one surface when it's substantial enough to warrant its own quiet-window sequencing. Light surfaces fold findings directly into the umbrella.

### Surface-child registry (tracked by this program)

| # | Surface | Child project | Status |
|---|---|---|---|
| 1 | BFF API (`Sprk.Bff.Api`) | [`bff-api-cleanup-remediation-r1`](../bff-api-cleanup-remediation-r1/) | **design.md drafted** 2026-08-06; 2 prod bugs + security decision; A/B tranche split (bugs+hygiene now, wide consolidations in a quiet window) |
| 2 | Shared client libs (`Spaarke.*`, 16 pkgs, ~39k LOC) | `shared-client-libs-audit-r1` (proposed) | not started — **highest leverage next** |
| 3 | Shared server libs (`Spaarke.Core/Dataverse/Scheduling`, ~10k LOC) | fold-in or child TBD | not started — roots the two-Dataverse-stacks question |
| 4 | PCF controls (36, ~49.5k LOC) | `pcf-audit-r1` (proposed; note prior `pcf-orphan-cleanup-r1`) | not started — lifecycle + dead-control sweep |
| 5 | Dataverse data model + solution ALM | `dataverse-model-audit-r1` (proposed) | not started — Power-Apps panel scrutinizes hardest |
| 6 | Code Page solutions (35, ~68k LOC) + build/config sprawl (69 `package.json`) | child TBD | not started |
| — | Horizontals (security · tests · deps · observability) | owned by umbrella (§8) | not started |

Children carry their **own** hot-path declarations + Placement Justifications (§10 governance); the umbrella tracks them and prevents overlap.

---

## 4A. Coordination model — how the umbrella and children stay aligned

**The governing principle: children are aligned by shared *artifacts*, not by task-by-task messaging.** The umbrella and a surface child exchange information at exactly **two seams** — a rubric handed down at the start, a grade rolled up at the end. Between those seams their `/task-execute` runs are fully independent, because the surfaces are *disjoint* (different files, languages, and — usually — deploy targets). You do not coordinate work that never touches the same code.

### The two seams (the entire coordination interface)

| Seam | Direction | When | Mechanism | Frequency |
|---|---|---|---|---|
| **S1 — Rubric + method handoff** | umbrella → child | child kickoff | Child spec references `docs/standards/CODE-QUALITY-RUBRIC.md` (§5) + runs the shared `quality-assessment` workflow (§6). Alignment is by-construction — same ruler, same method. | Once per child |
| **S2 — Grade + findings rollup** | child → umbrella | child wrap-up | Child appends its surface row to the umbrella scorecard `notes/SCORECARD.md` (append-only; no merge dance) and its remediation `design.md` link. | Once per child (updated on re-assessment) |

There is **no S3.** Mid-execution, a child never blocks on, waits for, or negotiates with the umbrella or a sibling.

### The shared artifacts (each written ONCE, referenced by all)

1. **`docs/standards/CODE-QUALITY-RUBRIC.md`** — the single scoring contract (D1–D11). Every child spec cites it, so every surface is measured identically.
2. **The `quality-assessment` workflow/skill** (§6) — every surface runs the same fan-out → adversarial-verify → design method. Consistency lives in the tool, not in a coordinator's head.
3. **`projects/code-quality-and-assurance-r3/notes/SCORECARD.md`** — one living scorecard; each child owns exactly one row, appended at wrap-up. This is the aggregate A+ view.

### What the umbrella owns vs. what children own (no duplication, no negotiation)

| Concern | Owner | Why here |
|---|---|---|
| Rubric, scorecard, assessment method/workflow | **Umbrella** | Single source of truth; children consume, never fork |
| **Forcing-functions** (ArchTests, analyzers, CI gates) — *authoring* | **Umbrella** | Cross-cutting; authored once so children don't duplicate or negotiate them (§9) |
| **Forcing-function *activation* per surface** (flip `TreatWarningsAsErrors`, `--max-warnings 0` for that surface) | **Child** (its final task) | Removes the only real cross-project hazard — see below |
| Horizontal sweeps (security, tests, deps, observability) | **Umbrella** (§8) | Cross-cutting; run once repo-wide, not per surface |
| Deep remediation of one disjoint surface | **Child** | Independent files → independent plan, branch, worktree, PR, quiet-window |

### The one genuine cross-project dependency — and its resolution

The only hazard that could force coordination is flipping a **repo-wide enforcement gate** while a surface is still dirty (it would break that surface's build). **Resolution: gates are activated per-surface, owned by each child as its last task** (design decision, supersedes open-question #4). The umbrella *authors* the gate; each child *turns on its own* enforcement when its surface is clean. No repo-wide big-bang, no inter-project timing negotiation.

### Tracking rollup is automatic (portfolio, not manual)

The two tiers map onto the existing portfolio model with **zero manual typing**:
- **Umbrella r3 → Epic** (portfolio rollup surface).
- **Each surface child → a Project** under that Epic (`Parent issue` = the Epic).
- The 9 `/devops-*` auto-hooks already sync each child's task-count / completion / status to Project #2 on every `/task-execute`; the Epic aggregates them. The umbrella's `SCORECARD.md` is the qualitative companion to the portfolio's quantitative rollup.

### Merge / sequencing coordination reuses existing machinery

Cross-project *file* contention (the real risk, given 30 active worktrees) is handled by the mechanisms every BFF project already uses — **not** a new process: [`projects/INDEX.md`](../INDEX.md) hot-path declarations + `/conflict-check` before each remediation PR. Assessments are read-only → conflict-free → run anytime; only remediation lands in per-surface quiet windows.

### Net coordination cost

Front-loaded and small: **write the rubric + workflow once (S1)**, then each child self-serves. Per child, ongoing coordination = **one scorecard-row append (S2)** + the automatic portfolio sync. No standing back-and-forth, no shared task plan, no cross-project task dependencies. Realistic project count is **umbrella + 2–3 heavy children** (BFF, likely shared-client-libs + dataverse-model); light surfaces fold their remediation directly into the umbrella (§4), further shrinking the surface that needs any coordination at all.

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

**Engine**: at program scale (5+ large surfaces × 11 dimensions × adversarial verify), a **multi-agent Workflow** is the efficient way to run this — parallel finders per dimension, per-finding verification, synthesized report — versus driving agents one at a time. This is an explicit operator opt-in ("use a workflow"); a reusable `quality-assessment` workflow/skill is a proposed deliverable so each surface is one repeatable, verified run.

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

> The umbrella↔child *alignment* contract (the two seams, shared artifacts, per-surface gate ownership, portfolio rollup) is defined in **§4A. Coordination model**. This section covers only *timing/merge* sequencing.

- **Assess now, remediate in windows.** All Phase-0 assessments are read-only → start immediately regardless of the 30-worktree churn. Remediation lands per-surface in coordinated quiet windows.
- **BFF is the most-contested surface** (19 active projects) → its child project already defines an A/B tranche split. Other surfaces (client libs, data model) are far less contested and can remediate sooner.
- **`/conflict-check`** before every remediation PR against [`projects/INDEX.md`](../INDEX.md); surface children register their own hot-path declarations.

## 11. Hot-path declaration (umbrella)

```xml
<hot-path-declaration>
  <bff>N</bff>                 <!-- umbrella owns rubric/scorecard/coordination; BFF code changes belong to child #1 -->
  <spaarkeai>N</spaarkeai>     <!-- client-surface code changes belong to their surface children -->
  <ci-workflows>Y</ci-workflows>   <!-- forcing-functions §9: CVE/size/lint/doc-drift gates in .github/workflows -->
  <skill-directives>Y</skill-directives> <!-- rubric may update .claude/constraints + code-review/adr-check skills -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
Umbrella deliverables are docs, ArchTests, and CI/build-config; direct product-code edits are delegated to surface children (each with its own declaration). `ci-workflows=Y` coordinates with `ci-cd-unit-test-remediation-r1` (owns existing-workflow edits).

## 12. Deliverables

- `docs/standards/CODE-QUALITY-RUBRIC.md` — the standing A+ rubric + scoring scale.
- **Re-baselined scorecard** — verified per-surface + aggregate grade (Phase 0 output; supersedes the March "95/100").
- Per-surface remediation `design.md`s (child projects or fold-ins) — BFF #1 done.
- Expanded `Spaarke.ArchTests` fitness functions + analyzer/lint/CI gate config.
- (Proposed) reusable `quality-assessment` workflow/skill.
- Reconciliation record of the archived R3 draft (§3).

## 13. Success criteria (draft — `/design-to-spec` to close the set)

- [ ] `CODE-QUALITY-RUBRIC.md` published; every surface scored against D1–D11.
- [ ] Honest re-baseline scorecard published (no unverified aggregate grade).
- [ ] Every surface has a verified assessment; deep surfaces have a remediation child project.
- [ ] BFF child #1 executed (bugs fixed, dead code removed, auth closed).
- [ ] Forcing-functions live: ArchTests expanded, analyzers-as-errors on, lint/CVE/size/doc-drift CI gates green.
- [ ] Archived R3 draft fully reconciled (§3) — no item dropped silently.
- [ ] Aggregate grade reaches **A+ (senior-panel standard)** with forcing-functions preventing re-drift.

## 14. Risks

| Risk | Mitigation |
|---|---|
| Program sprawl / never "done" | Fixed rubric + per-surface scorecard makes progress measurable; children are independently shippable |
| Assessment findings rot unremediated | Every assessment → `/design-to-spec` → tracked tasks; forcing-functions prevent re-drift |
| Merge contention on remediation (30 worktrees) | Assess read-only anytime; remediate in per-surface quiet windows; `/conflict-check` each PR |
| False-positive deletions (as in the BFF false positives) | Adversarial verification phase is mandatory; check test-only consumers + data-driven dispatch |
| Overlap with in-flight projects (`ci-cd-unit-test-remediation-r1`, `redis-*`, surface audits) | Umbrella coordinates; cite ownership in §10; reconcile before scoping each surface |

## 15. Open questions (for `/design-to-spec` + owner)

1. **Run the assessments under a multi-agent Workflow** (opt-in "use a workflow") for speed/consistency, or drive agent fan-out manually per surface?
2. **Which surfaces get their own child project vs. fold into the umbrella?** Proposed children: shared-client-libs, PCF, dataverse-model. Confirm.
3. **Portfolio**: register this reframed R3 on Project #2 (Epic parent) and register `bff-api-cleanup-remediation-r1` as a child? (Verify no orphan R3 Issue first.)
4. ~~**Forcing-function aggressiveness** — repo-wide sweep vs per-surface?~~ **RESOLVED** (§4A): per-surface activation, owned by each child as its final task. The umbrella authors the gate; each child turns on its own enforcement when its surface is clean — no repo-wide big-bang.
5. **Grade authority** — self-scored against the rubric, or commission an external senior-panel review as the A+ acceptance gate?
