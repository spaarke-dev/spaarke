# Code Quality Rubric (D1–D11)

> **Purpose**: The single standing scoring contract for Spaarke code quality. Defines eleven dimensions (D1–D11) and an A–F scale that composes per-dimension → per-surface → program-aggregate grades. Every quality assessment measures against this one ruler so surface grades are comparable and program progress is measurable.
> **Last Updated**: 2026-08-14
> **Last Reviewed**: 2026-08-14
> **Reviewed By**: code-quality-and-assurance-r3 (task 001, spec FR-01)
> **Status**: New
> **Applies To**: Every Spaarke surface — BFF API, shared client libs, shared server libs, PCF controls, Dataverse data model + ALM, Code Page solutions, plugins — and the horizontal sweeps (security, tests, dependencies, observability, doc-drift).
> **North star**: *If a panel of senior architects/developers expert in web applications, Power Apps custom development, and enterprise solutions reviewed this codebase, it would earn an A+.*

---

## 1. What this rubric is

This is the **ruler**, not a scorecard. It defines the dimensions and the grading scale; it deliberately publishes **no grades**. Actual per-surface and aggregate grades live in [`projects/code-quality-and-assurance-r3/notes/SCORECARD.md`](../../projects/code-quality-and-assurance-r3/notes/SCORECARD.md) (task 002) and are produced by the `quality-assessment` Workflow (task 003) running the verified assessment method. See §5 (How to use this rubric).

The rubric extends the informal R1 scorecard into a fixed, comparable standard so that:
- every surface is scored on the same eleven dimensions;
- progress across the program is measurable against one anchor; and
- the honest Phase-0 re-baseline (task 016) has a ruler against which to supersede the stale March "A (95/100)".

---

## 2. The eleven dimensions (D1–D11)

Copied verbatim from the program design (`design.md` §5). These are the dimensions the senior panel grades. Do not add, rename, or drop dimensions.

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

Each dimension's **"What A+ looks like"** cell above is the explicit A+ criterion for that dimension — a surface earns an A/A+ on a dimension only when it meets that description with no material findings against it.

---

## 3. The A–F scale (per dimension)

Each dimension is graded A–F **per surface**. The letter answers: *how far is this surface's evidence from the "What A+ looks like" criterion for this dimension?*

| Grade | Meaning for a single dimension |
|---|---|
| **A+ / A** | Meets the "What A+ looks like" criterion fully. Zero material findings; only cosmetic nits at most. |
| **A–** | Strong. Meets the criterion in substance; one or two minor findings, none affecting correctness, security, or a contract. |
| **B+ / B / B–** | Solid. Real but non-blocking findings (debt to schedule); no latent broken path and no security exposure. |
| **C+ / C / C–** | Acceptable-with-notable-debt. Findings that should be remediated soon; approaching but not yet a defect. |
| **D+ / D / D–** | Weak. A **material defect** on this dimension — e.g. a latent broken path, a real auth/validation gap, provably dead production code at scale, or a CVE left open. |
| **F** | Failing. A defect that is **shipped and live** on this dimension — e.g. a broken production path in use, an unauthenticated data-mutation endpoint, a HIGH CVE in a deployed dependency. |

**Gating dimensions.** **D2 (Correctness)** and **D3 (Security)** are *gating*: a defect here is disqualifying in a way that a strong score elsewhere cannot offset. Their grade **caps** the surface grade (§4). This encodes the program's lived experience — the BFF re-baseline found a broken invoice-totals path (D2) and an unauthenticated Dataverse-write endpoint (D3); no amount of clean DI or healthy publish-size makes such a surface an "A".

---

## 4. Composition — dimension → surface → aggregate

Grades compose in two roll-ups. Both use a grade-point mapping for the averaging step and then a **cap** for the gating dimensions.

### 4.1 Grade points

| Grade | A+ | A | A– | B+ | B | B– | C+ | C | C– | D+ | D | D– | F |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Points | 4.3 | 4.0 | 3.7 | 3.3 | 3.0 | 2.7 | 2.3 | 2.0 | 1.7 | 1.3 | 1.0 | 0.7 | 0.0 |

Mapping back: round the composed points to the nearest row above.

### 4.2 Dimension → surface grade

1. **Weighted mean** of the eleven dimension points. Default weights are **equal**; a surface assessment MAY up-weight a dimension for a documented reason (recorded in that surface's `design.md`), but the gating rule below is not waivable.
2. **Gating cap**: the surface grade MUST NOT exceed the **lower** of its D2 (Correctness) and D3 (Security) grades. A surface with a D2 or D3 of `D` is at most a `D` surface, regardless of the mean; an `F` on either gates the surface to `F`.
3. The surface grade is the **capped** result (`min(weighted-mean-grade, D2, D3)`).

### 4.3 Surface → program aggregate

1. **Weighted mean** of the surface grades. Default weight is by assessed surface (equal); a surface MAY be weighted by LOC or blast-radius if the aggregating task (016) documents why.
2. **Gating cap**: the aggregate MUST NOT exceed the weakest surface's gating dimension across the program — i.e. if any surface has a live D3 `F`, the program is not an `A` until it is closed.
3. **Publish rule**: the program publishes **A+** only when **every** surface is ≥ A– **and** no gating dimension (any surface's D2 or D3) is below A–. Until then, the aggregate is reported honestly at its composed value.

### 4.4 Worked illustration (method only — not a real grade)

> Illustrative surface "X" with hypothetical letters, to show the mechanics. This is **not** a published grade for any real surface.

Suppose surface X scores: D1 B, D2 **D**, D3 A–, D4 A–, D5 C, D6 B–, D7 B, D8 A–, D9 B, D10 D+, D11 C+.
- Weighted (equal) mean ≈ `(3.0+1.0+3.7+3.7+2.0+2.7+3.0+3.7+3.0+1.3+2.3)/11 ≈ 2.67` → ~`B–`.
- Gating cap: `min(B–, D2=D, D3=A–)` = **D**.
- **Surface X grade = D.** The single broken-path dimension (D2 = D) gates the surface, exactly as intended — the strong mean does not rescue it.

---

## 5. How to use this rubric

This rubric is consumed by three things; it does not act on its own.

1. **The `quality-assessment` Workflow** (task 003, `design.md` §6) runs the verified assessment method against a surface: parallel read-only finders (one per dimension or dimension-cluster) → **adversarial verification** (Fable — non-negotiable per NFR-05; it caught 2 real BFF bugs *and* corrected 2 false-positive "dead code" claims) → a prioritized remediation `design.md`. Each finder scores its dimension A–F against §3 with `file:line` evidence.
2. **`notes/SCORECARD.md`** (task 002) records **one row per surface** — the eleven dimension letters plus the composed surface grade — appended at that surface's assessment/wrap-up. No aggregate grade is published until every surface is scored (FR-04). The March "A (95/100)" is treated as stale/superseded.
3. **The Phase-0 re-baseline** (task 016) reads every SCORECARD row, applies §4.3, and publishes the honest current aggregate — the first credible program grade since the codebase roughly doubled.

### Grading discipline (binding on every assessment)

- **Evidence, not vibes.** Every dimension letter cites `file:line` findings; a grade without evidence is not a grade.
- **Adversarial verification is mandatory** (NFR-05). A finding that survives the Fable refutation pass counts; a first-pass claim does not — especially "dead code" (check `src/` **and** `tests/`; BFF exposes internals via `InternalsVisibleTo`) and data-driven dispatch (Dataverse `sprk_*` rows are not grep-provable, NFR-08).
- **Gating is not waivable.** A documented weight change (§4.2/§4.3) can shift the mean; it can never lift the D2/D3 cap.
- **The rubric is the ruler; grades live in the scorecard.** Do not publish a per-surface or aggregate grade in this file.

---

## 6. Relationship to other standards

- **ADR-038 (Testing Strategy)** defines D7's KEEP categories and the "coverage = observation, never a gate" rule this rubric inherits — a surface is not penalized on D7 for low coverage %, and is not rewarded for scaffolding tests.
- **`docs/standards/TEST-ARCHITECTURE.md`** is the operational companion for D7.
- **`.claude/constraints/bff-extensions.md`** and root `CLAUDE.md` §10 define the BFF-specific bars (publish ≤ 60 MB, no new NuGet, facade rules) that feed D1/D4/D8 evidence for the BFF surface.
- **`docs/standards/CODING-STANDARDS.md`** and **`ANTI-PATTERNS.md`** supply the concrete conventions D6 grades against.

---

*This document is the standing quality ruler. To change a dimension or the scale, amend here and record the change in the program's `design.md` §5 and the change log; every prior SCORECARD row remains valid because the dimensions are stable by contract.*
