# Task Index — `spaarkeai-compose-r8`

> **Created**: 2026-08-19 · **Re-cut**: 2026-08-20 (decomposed by **file-pass**, not by concern)
> **Status**: INITIALIZED — execution owner-gated
> **36 tasks / 9 phases** · Legend: 🔲 pending · 🔄 needs retry · ✅ complete · ⛔ blocked

**Phase 4 does not start until Phase 3's gate passes.** A miss is an owner escalation (root §6/§6.5).

---

## Decomposition principle (why 36 and not 58)

The **entire Compose spine is `parallel-safe: false`** — `Services/Compose/**`, `Api/ComposeEndpoints.cs`,
`ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeAiToolbar.tsx`, `usePendingRedline.ts`. Splitting one
file's changes across N tasks therefore means **N sequential passes**: N context loads, N review cycles, N
merge windows on the most contended files in the repo.

Tasks are decomposed by **file-pass**, not by concern. A consolidated task carries the **union** of the
constraints and a **union acceptance-criteria closed set** (incl. negative cases) — density, not dilution.
**No scope was removed in the re-cut.**

Separate "write the tests" tasks are an ADR-038 anti-pattern and were folded into each task's acceptance
criteria — tests are part of the work, not a follow-on.

## Model-tier principle

Capability-matched: the tier that **fully meets** the required capability. Budget is not a constraint —
code quality is the priority. The discriminator is **"does a subtle miss ship silently?"**

- `opus` — any task where correctness is subtle and a miss ships (i.e. most of this project, by its nature).
- `sonnet` — genuinely mechanical work with a clear oracle: verification notes, corpus fixture authoring,
  baseline measurement, deploy mechanics.
- `effort: max` — the gate and the merge mechanism. `xhigh` — brownfield root-cause work. `high` — mechanical.

---

## Phase 0 — Coordination & prerequisites

| # | Task | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|
| 001 | Land/verify **PR #690** (Git-LFS corpus fixtures in CI); confirm fixtures resolve to real bytes | MINIMAL | sonnet/high | ✅ | — | ✅ |
| 002 | Publish-size baseline (vs 44.96 MB) + `/conflict-check` + **PR #266** (OpenXml 3.5.1) sequencing decision | MINIMAL | sonnet/high | ✅ | — | ✅ |

## Phase 1 — Track S: Save reliability · **P0, SHIPS ALONE** (no architecture dependency)

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 010 | **Client save-error contract** — route on `ApiError.status`, delete the unreachable `!response.ok` block, rebuild tests on the real thrown path | FR-S01 | FULL | opus/xhigh | ❌ | — | ✅ |
| 011 | **Concurrency** — last-writer-wins + warning (retire the 412 loop) **and** `If-Match` at the storage boundary | FR-S02, A12 | FULL | opus/xhigh | ❌ | 010 | ✅ |
| 012 | **Save lifecycle hardening** — dirty flag survives a failed POST · timeout + `AbortSignal` + in-flight guard · working 423 recovery | FR-S03/04/05 | FULL | opus/xhigh | ❌ | 010 | ✅ |
| 013 | **Save-outcome contract + telemetry** — closed enum on the wire; no 200-with-nothing-written; emit the outcome | FR-S06, S10 | FULL | opus/xhigh | ❌ | — | ✅ |
| 014 | **Engine-side integrity** — re-anchor download failure must never persist the stale baseline *(the ONE Half-A defect in Track S)* | FR-S07 | FULL | opus/xhigh | ❌ | — | ✅ |
| 015 | **Document size ceilings** — route to the existing chunked upload; remove the ~22 MB body ceiling; honest oversize pre-flight | FR-S08 | FULL | opus/xhigh | ❌ | 013 | ✅ |
| 016 | **Honest-failure set** — silent guard drops · name-modal gate · tenant precondition · checkout force-close · promote-after-write · 429 mapping · filesize/filepath refresh · per-document draft slot | FR-S09 | FULL | opus/xhigh | ❌ | 010, 013 | ✅ |
| 018 | **Track S enforcement** — run the Compose client suite in CI as a self-contained gate (the Half-B counterpart to `compose-fidelity-gate`); fix sibling-resolution + non-determinism | — | FULL | opus/xhigh | ❌ | 010 | ✅ |
| 017 | **Track S deploy** (BFF + `sprk_spaarkeai` together) + owner UAT | — | STANDARD | sonnet/high | ❌ | 010–016, **018** | ✅ |

> **✅ Phase 1 CLOSED — owner UAT GO, 2026-08-21.** Save works; zero Track S failure modes observed. The UAT
> surfaced one genuine Track S defect (the save-degradation banner told the user *"the original file is
> unchanged until you save"* **after** the bytes were written) — fixed + regression-tested the same day; ships
> with the next `sprk_spaarkeai` deploy. The two banners the owner still sees are **not** Track S: the
> formatting-simplified banner is **Track A** (Phases 2–4) and *"wording differs slightly"* is **Track C**
> (051–053, startable now — not gated on 031). Evidence: [`notes/track-s-uat.md`](../notes/track-s-uat.md).

> **Ordering note**: 018 runs **before** 017 — the deps column is authoritative, not the number. 018 was added
> 2026-08-20 after task 010 found that `Spaarke.Compose.Components` (88 suites / 786 tests) is not in CI at
> all, which is the mechanism by which a test validating unreachable code passed for three releases. Track S
> ships with a gate on the client save contract, not a promise of one. Owner decision, 2026-08-20.

## Phase 2 — Oracle & corpus (build the measurement BEFORE the fix)

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 020 | **The gate contract** — preservation oracle + outcome-honesty assertion + two comparison levels with normalization *(all one harness file)* | FR-G01/02/03 | FULL | opus/max | ❌ | 001, 013 | 🔲 |
| 021 | Corpus: the 3 synthetic R4-breakers (`mc:AlternateContent` dup paraIds · interior text boxes · multi-part collisions) | FR-G04 | STANDARD | sonnet/high | ✅ | 001 | 🔲 |
| 022 | Corpus: near-tier owner documents (char formatting · court spacing · footnotes · `REF` · content controls) | FR-G04 | STANDARD | sonnet/high | ✅ | 001 | 🔲 |
| 023 | **Control measurement** — run the oracle on current master; publish today's real loss numbers | — | STANDARD | opus/high | ❌ | 020–022 | 🔲 |

## Phase 3 — Model proof · **THE GATE**

| # | Task | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|
| 030 | **Merge prototype + measurement** — stamp → re-project → per-block compare → clone unchanged; answers spec §5.3; includes heavy-restructure (FR-G06) + N-cycle Word round-trip (FR-G07) | FULL | opus/max | ❌ | 023 | 🔲 |
| 031 | **GATE DECISION** + ADR-049 third-amendment draft. *Escalation trigger: a miss goes to the owner — do not improvise* | FULL | opus/max | ❌ | 030 | 🔲 |

## Phase 4 — Track A: Faithful save *(blocked until 031 passes; POMLs provisional — amendable by 031)*

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 040 | **The merge mechanism** — baseline stamping · server-side re-projection oracle · block copy-through · property inheritance for edited blocks *(ONE mechanism, one file-pass)* | FR-A01/02/03/04 | FULL | opus/max | ❌ | 031 | 🔲 |
| 041 | **Opaque-atom payload carry** + table/atom identity (write model + `opaqueAtomNode.ts`) | FR-A05/06 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 042 | **Comment anchors + revision-id seeding under cloning** (dup-paraId consume-in-order, cross-boundary ranges) | FR-A11 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 043 | **Capability gate** → read-only + **"Edit a copy"** (`ConfirmModal`/ADR-050; fork stamped `Authored`; original never written) | FR-A07 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 044 | **Two document classes** (Authored/Imported; warnings suppressed for Authored) + PDF version-coordinate tracking | FR-A08/09 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 045 | **Residual loss list published + owner sign-off** + **ADR-049 third amendment merged** (7 invariants) — main-session only¹ | FR-A10 | FULL | opus/xhigh | ❌ | 040–044 | 🔲 |

¹ `.claude/` write — sub-agents cannot write these paths (root §3). Main session executes.

## Phase 5 — Track C: AI edit placement

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 050 | **ADR-043 + ADR-041 assessment** (ADR-043 explicitly names "compose edit"; is FR-C05's "apply anyway?" a Gate?). *Escalation trigger* | — | FULL | opus/xhigh | ✅ | — | ✅ |
| 051 | **Anchor supply** — thread the captured `(paraId, span)` request→response→apply · wire `CitationResolver` · closed-set paraId return for review passes *(three sources, one code path)* | FR-C01/02/03 | FULL | opus/max | ❌ | 050 | 🔲 |
| 052 | **Retire the text-search path** (`ComposeEditValidator`, `FindAll`, `target_text`/`match_mode`, client matchers) + deterministic stale/deleted outcomes | FR-C04/05 | FULL | opus/xhigh | ❌ | 051 | 🔲 |
| 053 | **Bounded confirmable fallback** + verify the dead-end is **unreachable**; no UAT-21 regression | FR-C06/07 | FULL | opus/xhigh | ❌ | 052 | 🔲 |

## Phase 6 — Track B: Durable session files *(the only genuinely parallel track)*

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 060 | **Durable byte store** (blob, tenant-partitioned, existing MI RBAC + provisioned container) | FR-B01 | FULL | opus/xhigh | ✅ | — | 🔲 |
| 061 | **Lazy re-index** on recall + `SessionFilesCleanupJob` evicts the **hot index only** | FR-B02/03 | FULL | opus/xhigh | ✅ | 060 | 🔲 |
| 062 | **Retention follows session TTL** (incl. `-1` filed = indefinite) + server-authoritative availability | FR-B04/05 | FULL | opus/xhigh | ✅ | 060 | 🔲 |
| 063 | **Erasure deletes the bytes** + tenant-isolation verification (ADR-014/015) | FR-B06 | FULL | opus/xhigh | ✅ | 060 | 🔲 |

## Phase 7 — Track D: God-class removal *(interleave with Phases 4–6; each task deletes its own waiver)*

| # | Task | File (LOC) | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 070 | Decompose `ComposeService.cs` + delete its waiver | 3,573 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 071 | Decompose `ComposeDocxProjectionBuilder.cs` + delete its waiver | 3,085 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 072 | Decompose `ComposeDocumentRenderer.cs` + delete its waiver | 2,304 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 073 | Decompose `Api/ComposeEndpoints.cs` + delete its waiver | 2,651 | FULL | opus/xhigh | ❌ | 013 | 🔲 |
| 074 | **Retire `ComposeShadowPatchEngine.cs`** + delete its waiver — confirm at the gate **before** deleting | 2,999 | FULL | opus/max | ❌ | 031, 040 | 🔲 |

## Phase 8 — Wrap-up

| # | Task | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|
| 090 | Anti-clobber deploy · `/test-diet` · write-side fidelity doc · lessons-learned · `projects/INDEX.md` + root §17 update | STANDARD | sonnet/high | ❌ | all | 🔲 |

---

## Critical path

```
001 → 020 → 021/022 → 023 → 030 → 031 (GATE) → 040 → 044 → 045 → 090
```

**Track S (010–017) is off the critical path** — it ships first and independently, and needs no gate.

## Parallel groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P0 | 001, 002 | — | Independent coordination |
| C1 | 021, 022 | 001 | Corpus fixture authoring — different files |
| B1 | 060 → 063 | — | **Track B is the only genuinely parallel-safe track** (touches `Services/Ai/Sessions`, not the Compose spine) |
| C2 | 050 | — | ADR assessment is read-only; runs any time |

Everything else is serial **by necessity**, not oversight — 13+ active worktrees contend on the BFF.

## Goal-eligible waves

**None.** Every wave has a judgment boundary, touches irreversible surface, or is single-task-serial. Root §8.5
requires ≥3 well-specified low-ambiguity parallel tasks; no wave here qualifies.

## High-risk items

| Task | Risk |
|---|---|
| **031** | Gate failure re-opens the architecture. **Escalate; do not improvise.** |
| 040 | The merge mechanism — the project's central bet, in one pass |
| 043 | Capability-gate false positives block documents we could have handled |
| 052 | Retiring the text-search path — verify no consumer outside Compose |
| 074 | Deleting a 3,000-line engine — gate-confirm first |
| 011 | Concurrency semantics reversal on the live save path |

## Re-cut record (2026-08-20)

58 → 36 tasks, **zero scope removed**. Merged: client lifecycle (3→1) · concurrency + If-Match (2→1) ·
outcome + telemetry (2→1) · gate contract (3→1) · merge mechanism (4→1) · anchor supply (3→1) ·
Phase-3 proof (3→2) · coordination (3→2). Removed the standalone test task (ADR-038 anti-pattern — folded
into acceptance criteria). Model tiers re-assigned on capability-match, not budget.
