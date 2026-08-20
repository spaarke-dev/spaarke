# Task Index — `spaarkeai-compose-r8`

> **Created**: 2026-08-19 via `/project-pipeline` · **Status**: INITIALIZED — execution owner-gated
> **58 tasks / 9 phases** · Legend: 🔲 pending · 🔄 needs retry · ✅ complete · ⛔ blocked

**Phase 4 does not start until Phase 3's gate passes.** A miss is an owner escalation (root §6/§6.5).

---

## Phase 0 — Coordination & prerequisites

| # | Task | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|
| 001 | Land/verify **PR #690** (Git-LFS corpus fixtures in CI); confirm corpus loads | MINIMAL | sonnet/high | ✅ | — | 🔲 |
| 002 | Publish-size baseline (vs 44.96 MB) + `/conflict-check` + net10 verify | MINIMAL | sonnet/high | ✅ | — | 🔲 |
| 003 | Sequence decision for **PR #266** (`DocumentFormat.OpenXml` 3.4.1→3.5.1) | MINIMAL | sonnet/high | ✅ | — | 🔲 |

## Phase 1 — Track S: Save reliability · **P0, SHIPS ALONE** (no architecture dependency)

| # | Task | FR | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|
| 010 | Route client save errors on `ApiError.status`; retire the unreachable `!response.ok` block | FR-S01 | FULL | sonnet/xhigh | ❌ | — | 🔲 |
| 011 | Last-writer-wins + warning; retire the 412 refusal | FR-S02 | FULL | opus/xhigh | ❌ | 010 | 🔲 |
| 012 | `If-Match` on the content PUT (storage-boundary concurrency) | FR-A12 | FULL | sonnet/xhigh | ❌ | 011 | 🔲 |
| 013 | Born-in-editor dirty flag survives a failed POST | FR-S03 | FULL | sonnet/high | ❌ | — | 🔲 |
| 014 | 423 lock → clear message + working Retry | FR-S04 | STANDARD | sonnet/high | ❌ | 010 | 🔲 |
| 015 | Save timeout + `AbortSignal` + in-flight guard | FR-S05 | STANDARD | sonnet/high | ❌ | — | 🔲 |
| 016 | Save-outcome enum on the wire (`SaveComposeDocumentResponse`) | FR-S06 | FULL | opus/xhigh | ❌ | — | 🔲 |
| 017 | Re-anchor download failure must never persist the stale baseline | FR-S07 | FULL | opus/xhigh | ❌ | — | 🔲 |
| 018 | Chunked upload for Compose + remove ~22 MB body ceiling + oversize pre-flight | FR-S08 | FULL | sonnet/xhigh | ❌ | — | 🔲 |
| 019 | Honest-failure set (guards, name modal, tenant precondition, checkout, promote, 429, filesize, draft slot) | FR-S09 | FULL | sonnet/xhigh | ❌ | 010,016 | 🔲 |
| 020 | Save-outcome telemetry | FR-S10 | STANDARD | sonnet/high | ❌ | 016 | 🔲 |
| 021 | Track S seam + contract tests driving the **real thrown `ApiError`** path | NFR-08 | FULL¹ | sonnet/xhigh | ❌ | 010–020 | 🔲 |
| 022 | Track S deploy (BFF + `sprk_spaarkeai` together) + owner UAT | — | STANDARD | sonnet/high | ❌ | 021 | 🔲 |

¹ TEST-MODIFYING → code-review + adr-check unconditional at Step 9.5 (root §8).

## Phase 2 — Oracle & corpus (build the measurement BEFORE the fix)

| # | Task | FR | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|
| 030 | Preservation oracle (untouched-block XML equivalence) | FR-G01 | FULL¹ | opus/xhigh | ❌ | 001 | 🔲 |
| 031 | Outcome-honesty assertion in the gate | FR-G02 | FULL¹ | sonnet/xhigh | ❌ | 016,030 | 🔲 |
| 032 | Two comparison levels + normalization (rsid, proofErr, bookmarks, numId, attr order) | FR-G03 | FULL¹ | opus/xhigh | ❌ | 030 | 🔲 |
| 033 | Corpus: the 3 synthetic R4-breakers (`mc:AlternateContent` dup paraIds, interior text boxes, multi-part collisions) | FR-G04 | STANDARD | sonnet/high | ✅ | 001 | 🔲 |
| 034 | Corpus: near-tier owner documents (char formatting, court spacing, footnotes, REF, content controls) | FR-G04 | STANDARD | sonnet/high | ✅ | 001 | 🔲 |
| 035 | **Control measurement** — run the oracle on current master; publish today's real loss numbers | — | STANDARD | sonnet/high | ❌ | 030–034 | 🔲 |

## Phase 3 — Model proof · **THE GATE**

| # | Task | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|
| 040 | Merge prototype: stamp baseline → re-project → per-block compare → clone unchanged | FULL | opus/max | ❌ | 035 | 🔲 |
| 041 | Answer spec §5.3 research questions (granularity, paraId reliability, tracked-changes/comments, perf) | FULL | opus/max | ❌ | 040 | 🔲 |
| 042 | Heavy-restructure (FR-G06) + N-cycle Word round-trip (FR-G07) measurement | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 043 | **GATE DECISION** + draft ADR-049 third amendment. *Escalation trigger: miss → owner, do not improvise* | FULL | opus/max | ❌ | 040–042 | 🔲 |

## Phase 4 — Track A: Faithful save *(blocked until 043 passes)*

| # | Task | FR | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|
| 050 | Baseline paraId stamping on the render path | FR-A01 | FULL | sonnet/xhigh | ❌ | 043 | 🔲 |
| 051 | Server-side re-projection equality oracle (production path) | FR-A02 | FULL | opus/xhigh | ❌ | 050 | 🔲 |
| 052 | Block copy-through for unchanged blocks | FR-A03 | FULL | opus/xhigh | ❌ | 051 | 🔲 |
| 053 | Property inheritance for edited blocks | FR-A04 | FULL | opus/xhigh | ❌ | 052 | 🔲 |
| 054 | Opaque-atom payload carry (write model + `opaqueAtomNode.ts`) | FR-A05 | FULL | opus/xhigh | ❌ | 052 | 🔲 |
| 055 | Table + atom identity | FR-A06 | FULL | sonnet/xhigh | ❌ | 054 | 🔲 |
| 056 | Comment anchors + revision-id seeding under cloning | FR-A11 | FULL | opus/xhigh | ❌ | 052 | 🔲 |
| 057 | Capability gate → read-only + **"Edit a copy"** (`ConfirmModal`, ADR-050) | FR-A07 | FULL | opus/xhigh | ❌ | 052 | 🔲 |
| 058 | Two document classes (Authored/Imported) + PDF version coordinates | FR-A08/09 | FULL | sonnet/xhigh | ❌ | 052 | 🔲 |
| 059 | Residual loss list published + owner sign-off | FR-A10 | STANDARD | sonnet/high | ❌ | 052–058 | 🔲 |
| 060 | **ADR-049 third amendment merged** (7 invariants) — main-session only² | — | FULL | opus/xhigh | ❌ | 043 | 🔲 |

² `.claude/` write — sub-agents cannot write these paths (root §3). Main session executes.

## Phase 5 — Track C: AI edit placement

| # | Task | FR | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|
| 065 | **ADR-043 + ADR-041 assessment** (compose-edit `ActionKind`; "apply anyway?" as a Gate). *Escalation trigger* | — | FULL | opus/xhigh | ✅ | — | 🔲 |
| 066 | Thread the captured `(paraId, span)` request→response→apply | FR-C01 | FULL | opus/xhigh | ❌ | 065 | 🔲 |
| 067 | Wire `CitationResolver` for reference-driven targets | FR-C02 | FULL | sonnet/xhigh | ❌ | 065 | 🔲 |
| 068 | Closed-set paraId return for model-initiated review passes | FR-C03 | FULL | opus/xhigh | ❌ | 066 | 🔲 |
| 069 | Deterministic stale/deleted-target outcomes | FR-C05 | FULL | sonnet/xhigh | ❌ | 066 | 🔲 |
| 070 | Retire the text-search path (validator, `target_text`/`match_mode`, client matchers) | FR-C04 | FULL | opus/xhigh | ❌ | 066–069 | 🔲 |
| 071 | Tolerant matching as a bounded **confirmable** fallback only | FR-C06 | FULL | opus/xhigh | ❌ | 070 | 🔲 |
| 072 | Verify the dead-end is **unreachable**; no UAT-21 regression | FR-C07 | FULL¹ | sonnet/xhigh | ❌ | 070,071 | 🔲 |

## Phase 6 — Track B: Durable session files

| # | Task | FR | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|
| 075 | Durable byte store (blob, tenant-partitioned, existing MI RBAC) | FR-B01 | FULL | sonnet/xhigh | ✅ | — | 🔲 |
| 076 | Lazy re-index from the durable copy on recall | FR-B02 | FULL | sonnet/xhigh | ✅ | 075 | 🔲 |
| 077 | `SessionFilesCleanupJob` evicts the hot index only | FR-B03 | FULL | sonnet/high | ✅ | 075 | 🔲 |
| 078 | Retention follows session TTL (incl. `-1` filed = indefinite) | FR-B04 | FULL | sonnet/xhigh | ✅ | 075 | 🔲 |
| 079 | Server-authoritative availability (replace R7's 24h heuristic) | FR-B05 | STANDARD | sonnet/high | ✅ | 076 | 🔲 |
| 080 | Erasure deletes bytes + tenant isolation (ADR-014/015) | FR-B06 | FULL | opus/xhigh | ✅ | 075 | 🔲 |

## Phase 7 — Track D: God-class removal *(interleave with Phases 4–6, do not parallelize)*

| # | Task | File | Rigor | Tier/Effort | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|
| 082 | Decompose `ComposeService.cs` (3,573) | — | FULL | opus/xhigh | ❌ | 052 | 🔲 |
| 083 | Decompose `ComposeDocxProjectionBuilder.cs` (3,085) | — | FULL | opus/xhigh | ❌ | 051 | 🔲 |
| 084 | Decompose `ComposeDocumentRenderer.cs` (2,304) | — | FULL | opus/xhigh | ❌ | 052 | 🔲 |
| 085 | Decompose `Api/ComposeEndpoints.cs` (2,651) | — | FULL | opus/xhigh | ❌ | 016 | 🔲 |
| 086 | Retire `ComposeShadowPatchEngine.cs` (2,999) — confirm at gate **before** deleting | — | FULL | opus/max | ❌ | 043,052 | 🔲 |
| 087 | Delete all five waivers from `GodClassGuardTests.cs`; ArchTests green | — | FULL¹ | sonnet/high | ❌ | 082–086 | 🔲 |

## Phase 8 — Wrap-up

| # | Task | Rigor | ∥-safe | Deps | Status |
|---|---|---|---|---|---|
| 090 | Anti-clobber deploy · `/test-diet` · write-side fidelity doc · lessons-learned · `projects/INDEX.md` update | STANDARD | ❌ | all | 🔲 |

---

## Critical path

```
001 → 030 → 031/032 → 033/034 → 035 → 040 → 041/042 → 043 (GATE)
    → 050 → 051 → 052 → 058 → 059 → 090
```

**Track S (010–022) is off the critical path** — it ships first and independently.

## Parallel groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P0 | 001, 002, 003 | — | Independent coordination tasks |
| C1 | 033, 034 | 001 | Corpus fixture authoring — different files |
| B1 | 075 … 080 | Phase 3 gate | **Track B is the only genuinely parallel-safe track** — touches `Services/Ai/Sessions`, not the Compose spine |
| C2 | 065 | — | ADR assessment is read-only; can run any time |

**Everything else is `parallel-safe: false`.** The Compose spine (`Services/Compose/**`,
`ComposeEndpoints.cs`, `ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeAiToolbar.tsx`,
`usePendingRedline.ts`, `docxBridge.ts`) is the most contended surface in the repo — 13+ active worktrees
touch BFF. Serial execution is deliberate, not an oversight.

## Goal-eligible waves

**None.** Every wave either has a judgment boundary (Phase 3 gate, ADR assessments), touches irreversible
surface (deploys, deletions), or is single-task-serial. Per root §8.5, `/goal` eligibility requires ≥3
well-specified low-ambiguity parallel tasks — no wave here qualifies.

## High-risk items

| Task | Risk |
|---|---|
| 043 | Gate failure re-opens the architecture. **Escalate; do not improvise.** |
| 057 | Capability-gate false positives block documents we could have handled |
| 070 | Retiring the text-search path — verify no consumer outside Compose |
| 086 | Deleting a 3,000-line engine — gate-confirm first |
| 011/012 | Concurrency semantics change; regression risk on the save path |
