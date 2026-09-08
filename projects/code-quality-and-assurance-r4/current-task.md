# Current Task State — Code Quality & Assurance R4

> **Last Updated**: 2026-09-04 (by context-handoff)
> **Recovery**: read "Quick Recovery" first — it is sufficient to resume.
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **012 — escalated, awaiting owner sign-off.** P1 complete (001–003 ✅). P2a: 010 ✅ 011 ✅ 012 🔄 013 🔲 |
| **Progress** | **6 of 34 tasks complete** |
| **Status** | blocked on a decision, **not** on work |
| **Execution** | **AUTONOMOUS** — resume without per-wave confirmation |
| **Next Action** | **(1)** Owner answers the 4 questions in [`notes/decisions/012-ESCALATION-security-adrs.md`](notes/decisions/012-ESCALATION-security-adrs.md). **(2)** Meanwhile **task 013 is unblocked** (deps=011 ✅) — run it. **(3)** Task 020 / all of P2b stays parked until 012 closes. **(4)** P3 (030+) is independent of P2 and can run in parallel if desired. |

### Open decisions blocking task 012

1. **Sign-off on 8 auth/security/compliance ADRs** — 005, 014, 017, 018, 041, 042, 043, 047. Recommendations supplied; no path chosen. Highest-consequence: **ADR-018** ("flags never bypass authorization"), where ADR-032 (Accepted) already exists to enforce ADR-018 (Proposed).
2. **ADR-014 — ratify or withdraw?** Withdrawal is outside the B/C frame the task allows, so it needs an owner call.
3. **ADR-047 — decide now, or run `spine-r1` task 090 first?** Recommend 090; its scope is literally this promotion + doc-drift reconciliation.
4. **ADR-016 — I classified it *cost*, not security.** Overrule if you read rate limiting as abuse control.

### Also awaiting owner review (not blocking)

- [`notes/adr-inventory-2026-09.md`](notes/adr-inventory-2026-09.md) — all 50 ADRs, sorted by usage, enriched with every 010/011 finding. Built for manual utility review.

---

## Progress

| Task | Status | Outcome |
|---|---|---|
| 001 | ✅ | ADR-012 amended — closed 15-package enumeration + 3 non-gate promotion questions (§6.5 path B) |
| 002 | ✅ | `SharedPackageCensusTests` — 8 tests, 4 negative controls; verified empirically against a real 16th directory |
| 003 | ✅ | Governance baseline, 6 measures with commands. Escalation raised then **withdrawn** |
| 010 | ✅ | 49 ADRs classified on 3 axes; INDEX 36 → 50 rows |
| 011 | ✅ | **50/50 routed**; FR-23 reviewer scope = 28 ADRs |
| 012 | 🔄 | 5 of 13 §6.5 records done; **8 escalated** |
| 013 | 🔲 | **Unblocked — this is the next runnable task** |

### Files modified — all committed and pushed

HEAD `ad2e2bac2`, tree clean, 0 unpushed. PR [#935](https://github.com/spaarke-dev/spaarke/pull/935).

- `.claude/adr/ADR-012-shared-components.md` · `docs/adr/ADR-012-shared-component-library.md` — the amendment
- `.claude/adr/INDEX.md` — 36 → 50 rows + classification + routing sections
- `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` (new) · `SourceScan.cs` (extended)
- `scripts/quality/Verify-AdrNamedArtifacts.py` (new — re-runnable accuracy screen)
- `projects/.../notes/` — baseline, classification, accuracy re-verification, routing, inventory, 2 deviation records
- `projects/.../notes/decisions/` — 5 §6.5 records + 1 escalation package
- `projects/.../spec.md` — FR-10 rewritten; **FR-19b added** (nightly boundary-crossing drift check)
- `projects/.../tasks/046-boundary-crossing-drift-check.poml` (new)

---

## Critical Context

**Three of my own factual claims were wrong this session and were caught — two by the owner.** All are corrected in the artifacts. The pattern is worth carrying: **asserting from a quick grep without verifying.**

1. Claimed fan-in "binds FR-12/16/17/18 downstream" — nothing consumed it. Escalated over an invented dependency.
2. Claimed ADR-047 had zero server-side evidence — the grep guessed identifier names; the spine is fully built.
3. Claimed `spine-r1` was 0/22 — it is **21/22**. Their TASK-INDEX puts Status in column 4; my regex required the ✅ at end of line. **The same column-mismatch bug I had already caught and fixed in the portfolio sync earlier the same session.**

**Rule going forward**: a dependency claim and an existence claim are both factual claims, and both are greppable. Verify before asserting. When counting another project's TASK-INDEX, read its header first — column order varies.

---

## How to run this project (autonomous contract)

Owner direction 2026-09-04: **run autonomously as long as it is safe and accurate.** Full contract in [plan.md §3.5](plan.md).

- Dispatch each task via `task-execute` at its declared `<model-tier>`/`<effort>`; run Step 9.5 gates; mark ✅; continue.
- **Build between waves**: any `.cs` → `dotnet build Spaarke.sln`. ⚠️ **`tests/Spaarke.ArchTests` is NOT in `Spaarke.sln`** — run it separately (`dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj`). A green solution build says nothing about it.
- **A task failing its own verification is not a stop.** Fix and re-run, or mark 🔄 and continue.

**Hard stops** — 012's fired (auth/security sign-off). Remaining: 012 path-B amendment affecting other worktrees · 020 criterion set empty or oversized · 052 headless runner fails → drop FR-23, do **not** build an alternative · `/conflict-check` collision · any fired `<escalation><trigger>`.

---

## Key findings so far

- **6 orphaned `Proposed` ADRs** (014, 016, 017, 018, 019, 020) — no gate, ~2025-12. **ADR-019 has 187 implementing files.** Distinct from the **4 gated `Proposed`** (041, 042, 043, 047), which name a promotion trigger — a legitimate state; ADR-039/040 both promoted that way.
- **Enforcement is 17/50, not 7/49** — 8 named tests + 9 unnamed guards. **ADR-028 is already enforced** by six arch tests, so P2b needs no new test for it, only naming.
- **15 ADRs name no checkable artifact** — including ADR-039 (858 citations), ADR-019 (555), ADR-040 (483). Their accuracy is undecidable by tooling *or* by a human without interpretation. Cheap remedy: one canonical artifact reference each. **Also blocks FR-06 routing from being more than bookkeeping.**
- **Two ADRs have drifted**: ADR-005 names `sprk_documentassociation` (exists nowhere); ADR-033 names `WorkingDocumentHandler`/`WorkingDocumentTools.cs` (code has `WorkingDocumentService`).
- **ADR-021 has 3,062 citations and no mechanical enforcement** — the largest governed surface in the repo with nothing holding it.
- **Five of the six FR-04 baseline measures still have no consumer.** Measure (c) gained one (FR-19b). Carry to wrap-up: point each at a mechanism or drop it.

---

## Live hot-path collisions

- **PR #894** `ci/tier2-unit-scope` — DRAFT. Touches only `ci-tier2-advisory.yml`, `Spaarke.sln`, `per-pr-tests.slnf`. **No overlap** with r4's single new workflow — re-check before P3 merges.
- **`unified-access-control-r2` PR #939 — MERGED.** The skill-directives collision is resolved.
- **`customer-provisioning-orchestration-r1`** (active) — unmerged edits to `.claude/adr/ADR-028`, `.claude/constraints/provisioning.md`, `.claude/patterns/provisioning/*`, `.claude/skills/provision-environment/SKILL.md`. **Will collide with P3 task 032's header backfill.** Mitigation (plan.md R7): the script is idempotent, so re-running after that branch merges is free.

✅ **Synced with `origin/master` 2026-09-04** — merged 13 commits (email-communication-intelligence-r2 wrap-up + the Tier-1 CI fix), **0 behind**. Post-merge verification: `dotnet build Spaarke.sln` 0 errors / 5 pre-existing CA2024 warnings; ArchTests **199/199**.

> **Worth knowing before P3/P5**: master's commit `ce5c2c3d7` — *"fix(ci): Tier 1 compiles the whole solution — closes the shadow window's false green"* — changed `.github/workflows/ci-tier1-blocking.yml`. r4 has not touched that file (its one permitted workflow is task 035), so there was no conflict, but tasks 035/054/056/058 should read it before adding sections.

---

## Quick Reference

- **Project**: code-quality-and-assurance-r4 · **Branch**: `work/code-quality-and-assurance-r4`
- [`CLAUDE.md`](./CLAUDE.md) · [`plan.md`](plan.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)

### Standing constraints (every task)

Exactly **one** new workflow project-wide (NFR-04) · **no threshold** on test count, duplication %, or file
size · reuse `SourceScan`, never fork · Class-1 artifacts generated never hand-authored · never scan
`.claude/worktrees/` or sibling worktrees · `.claude/`-touching tasks are **main-session only**.

---

## Recovery Instructions

1. Read **Quick Recovery** (< 30 seconds)
2. Read **Critical Context** — three corrected errors and the rule they produced
3. If the owner has signed off: finish task 012, then 013, then 020
4. If not: run **task 013** (unblocked), or start P3 at task 030

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
