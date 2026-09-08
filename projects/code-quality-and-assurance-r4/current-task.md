# Current Task State — Code Quality & Assurance R4

> **Last Updated**: 2026-09-04 (by context-handoff)
> **Recovery**: read "Quick Recovery" first — it is sufficient to resume.
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **012 — escalated, awaiting owner sign-off.** P1 complete (001–003 ✅). P2a: 010 ✅ 011 ✅ 012 🔄 013 🔲 |
| **Progress** | **5 of 34 complete** (001, 002, 003, 010, 011) · 1 blocked (012) |
| **Status** | blocked on a decision, **not** on work |
| **Execution** | **AUTONOMOUS** — resume without per-wave confirmation |
| **Next Action** | **(1)** Owner answers the **4 decisions detailed in the section below** (also in [`notes/decisions/012-ESCALATION-security-adrs.md`](notes/decisions/012-ESCALATION-security-adrs.md)). **(2)** Meanwhile **task 013 is unblocked** (deps=011 ✅) — run it. **(3)** Task 020 / all of P2b stays parked until 012 closes. **(4)** P3 (030+) is independent of P2 and can run in parallel if desired. |

## 🔔 The 4 open decisions blocking task 012 (full detail)

Full package: [`notes/decisions/012-ESCALATION-security-adrs.md`](notes/decisions/012-ESCALATION-security-adrs.md). Reproduced here so this file alone is sufficient.

**Why these are blocked**: CLAUDE.md §6.5 — *"Not an excuse to bypass auth, security, or compliance ADRs without explicit human sign-off."* Task 012's own constraint and escalation trigger say the same, and project `CLAUDE.md` lists it as a named hard stop. **A path was NOT chosen for any of the 8.** Partial sign-off is fine — proceed with whatever is decided and leave the rest held.

### Decision 1 — sign off on 8 auth/security/compliance ADRs

Each was checked against its **actual MUST rules**, not its title. That mattered twice: **ADR-005** reads as a storage ADR but mandates where authorization is evaluated, and **ADR-041** reads as UX policy but is a fail-closed safety model.

| ADR | The rule that triggers sign-off | Recommended | Reasoning |
|---|---|---|---|
| **018** Feature Flags | *"Flags never bypass authorization"* | **C — ratify, urgent** | Highest consequence in the set: the invariant that a kill switch cannot disable an authz check. **ADR-032 (Accepted) already exists to enforce ADR-018 (Proposed)** — an accepted ADR enforcing an unratified one is the anomaly to close. |
| **047** Notification Spine | *"MUST NOT place message bodies, privileged content, or pre-authorized action tokens"* on the transport | **C — ratify** *(but see Decision 3)* | Gate effectively reached: `spine-r1` is 21/22 and task 090 does this promotion. |
| **042** Memory Architecture | `retentionClass` → per-item Cosmos TTL at write | **C — ratify** | Gated `Proposed` (gate G-R2-B); 92 implementing files. Same pattern as ADR-039/040, both promoted cleanly. |
| **043** AI Capability Spine | *"hybrid authorization — autonomous low-risk / confirm…"* | **C — ratify** | Gated `Proposed`; 59 files; the dispatch spine other ADRs depend on. |
| **041** Judgment/Confirmation | *"classify request origin deterministically and **fail-closed**"*; writes gated by risk × origin × completeness | **C — ratify** | Gated `Proposed`. The fail-closed default is the conservative one; ratifying makes the safe behaviour binding. |
| **017** Async Job Status | *"MUST enforce authorization on job status endpoints (ADR-008)"* | **C — ratify** | Evidence weak (broad keyword matches). The rule defers to ADR-008, which is Accepted and enforced by a named test. |
| **005** Flat Storage in SPE | *"MUST evaluate permissions via UAC (not SPE native)"* | **B — amend, then ratify** | The permission rule is fine. But the ADR names **`sprk_documentassociation`, which exists nowhere in the repo**. Fix the artifact reference; the authorization rule itself needs no change. |
| **014** AI Caching & Reuse | *"never cache raw content without governance approval"* | **see Decision 2** | Weakest evidence in the set — only 2 matching files. |

### Decision 2 — ADR-014: ratify or **withdraw**?

**This is outside the B/C frame and therefore cannot be decided without you.** Task 012's constraint permits paths B and C only, but for an orphaned `Proposed` ADR the real question is **ratify / amend-then-ratify / withdraw**, and withdraw maps to neither. I did not invent a fourth path.

It bites here specifically: ADR-014 has **2 files of evidence** and may never have shipped. Choosing C would ratify a data-governance policy the codebase does not implement — **a rule that is false on the day it becomes binding**. Withdrawal may be the honest answer.

### Decision 3 — ADR-047: decide here, or run `spine-r1` task 090 first?

**Recommendation: run task 090 first.**

Ratifying ADR-047 asserts the built spine matches the ADR, and **nobody has checked that**. There is a specific reason to doubt it: `spaarke-notification-spine-r1` is 21/22 complete, yet its four producers (`CommunicationArrivedProducer`, `DailyBriefingSuggestionProducer`, `PreferenceDirectiveProducer`, `ICommunicationAssessedProducer`) arrived through *consumer* projects. If the spine was assembled per-consumer rather than built once, that is exactly what ADR-047's core commitment forbids — *"ONE spine built once for all client surfaces (collapses the email-r4/messaging-r3/assistant-r1 forks)."*

Task 090's stated scope is *"ADR-047 Proposed→Accepted, doc-drift reconciliation, repo-cleanup"* — so the conformance check is **already scheduled work, not new scope**.

### Decision 4 — ADR-016: is rate limiting *cost* or *abuse control*?

**I classified it cost and decided it** (path C, ratify) rather than escalating. Basis: it scored **zero** hits on every auth/security/compliance term, and its framing throughout is cost, capacity and backpressure.

But per-endpoint rate limiting is also an abuse-control mechanism, and a reviewer reading it that way would be entitled to require sign-off. **Overrule and I'll move it into the escalated set.** Recorded openly so it can be corrected now rather than discovered later.

### The 5 already decided (no action needed unless you disagree)

**ADR-019** → C ratify (187 files comply; RFC 7807 settled; nothing to amend) · **ADR-033** → B amend (names `WorkingDocumentHandler`/`WorkingDocumentTools.cs`; neither exists; code has `WorkingDocumentService`) · **ADR-023** → C confirm the supersession, keep as a tombstone so the ADR-023→ADR-050 breadcrumb survives · **ADR-020** → C ratify (flagged: evidence weaker than ADR-019's) · **ADR-016** → C ratify (see Decision 4).

**Enforcement block stands for all 13** — decided and escalated alike. A decided path is not an applied one: ADR-033 still needs its amendment, the ratifications still need a status flip. Task 020 must exclude all 13 from the FR-07 criterion set.

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

Tree clean, 0 unpushed, **0 behind master** (synced 2026-09-04). PR [#935](https://github.com/spaarke-dev/spaarke/pull/935), draft.

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
