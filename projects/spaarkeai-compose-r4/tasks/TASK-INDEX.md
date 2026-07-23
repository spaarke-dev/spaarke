# TASK-INDEX — Spaarke Compose R4 (Shadow Document Architecture)

> **Generated**: 2026-07-22 by `/project-pipeline`
> **Total tasks**: 36 (6 Gate · 4 Ingest · 5 Capture · 8 Patch Engine · 3 AI · 5 Concurrency · 4 Hardening/cutover · 1 wrap-up) — 036 (push-annotations) + 037 (born-in-editor tables) added 2026-07-22: two shipped constructs the closed op-schema can't express; both deferred owner Path B/C decisions blocking Success Criterion 7 (one byte-author).
> **Status legend**: 🔲 not-started · 🔄 in-progress · ✅ complete · 🔁 needs-retry · ⛔ blocked
> **Cutover**: HARD-REPLACE. **Task 006 (Phase 0 gate) is a HARD prerequisite to every deletion/cutover task (023, 032, 060).**

---

## Task Registry

| # | Task | Phase | Tier / Effort | Rigor | Deps | Gate | Status |
|---|---|---|---|---|---|---|---|
| 001 | Shadow-Document ADR + R3 Path-B amendment | 0 Gate | opus / high | FULL | — | startable | ✅ |
| 002 | Fidelity corpus as LFS fixtures (sample-docs) | 0 Gate | sonnet / medium | STANDARD | — | startable | ✅ |
| 003 | Operation schema — the shared spine (FR-11) | 0 Gate | opus / xhigh | FULL | — | startable | ✅ |
| 004 | Round-trip byte-diff harness (NFR-01) | 0 Gate | sonnet / high | FULL | 002 | startable | ✅ |
| 005 | Applier spike on CIPO + patch-engine A/B | 0 Gate | opus / xhigh | FULL | 002,003 | startable | ✅ |
| 006 | **Phase 0 GATE — 🟢 GREEN, cutover authorized** | 0 Gate | opus / high | FULL | 003,004,005 | passed | ✅ |
| 010 | Persist w14:paraId on ingest (FR-01) | 1 Ingest | sonnet / high | FULL | 001 | done | ✅ |
| 011 | Intra-paragraph offset-addressing table (FR-01) | 1 Ingest | opus / xhigh | FULL | 001,003 | done | ✅ |
| 012 | Opaque atoms — server projection (FR-02) | 1 Ingest | sonnet / high | FULL | 011 | done | ✅ |
| 013 | Phase-1 ingest/projection seam slice (NFR-06) | 1 Ingest | sonnet / high | FULL | 010,011,012 | done | ✅ |
| 020 | ProseMirror step→operation interceptor (FR-03) | 2 Capture | opus / xhigh | FULL | 003 | done | ✅ |
| 021 | Opaque-atom node — client schema (FR-02) | 2 Capture | sonnet / high | FULL | 012 | done | ✅ |
| 022 | Rebased operation log per session (FR-03) | 2 Capture | sonnet / xhigh | FULL | 020 | done | ✅ |
| 023 | Delete paragraph-diff export — client (FR-06) | 2 Capture | sonnet / high | FULL | 022,006,031,032 | done | ✅ |
| 024 | Phase-2 client capture tests | 2 Capture | sonnet / high | FULL | 020,021,022 | done | ✅ |
| 030 | ComposeShadowPatchEngine core (FR-04) | 3 Patch Engine | opus / xhigh | FULL | 003,005,**006** | done | ✅ |
| 031 | Structural operations — split/merge/insert/delete (FR-05) + client wiring | 3 Patch Engine | opus / xhigh | FULL | 030 | done | ✅ |
| 032 | Cutover: SAVE→engine + op-log send + retire redline-synthesizer (FR-06, re-scoped) | 3 Patch Engine | opus / high | FULL | 030,031,**006** | done | ✅ |
| 033 | Born-in-editor unification (FR-09) | 3 Patch Engine | sonnet / xhigh | FULL | 030 | resolved: born-in-editor stays on renderer (cited I-5 exception, C-revised) | ✅ |
| 037 | Born-in-editor tables + save unification | 3 Patch Engine | opus / xhigh | FULL | 030,031 | half-1 landed (73ca66e09); unification → **C-revised** (renderer kept, cited I-5 exc.); table gating superseded by 038 | ✅ |
| 038 | **Zero-error guardrail pass** (disable unsupported edit-path controls + op-log loss-proofing) | 3 Patch Engine | opus / xhigh | FULL | 032,036 | done (a5368d5b5) | ✅ |
| 039 | **UAT remediation** — born-in-editor 2nd-save error fix (no-errors) + dup-records + UX polish | 6 Hardening | opus / high | FULL | 032,036,038 | 🔄 startable — dev UAT must-fix | 🔄 |
| 034 | Patch-engine seam slices + corpus proof (NFR-01/02) | 3 Patch Engine | sonnet / high | FULL | 030,031,004 | done | ✅ |
| 035 | Deploy patch-engine core to dev | 3 Patch Engine | sonnet / high | FULL | 032,034 | ✅ DEPLOYED to dev 2026-07-23: BFF (spaarke-bff-dev, hash-verified, save 401/push-annotations 404) + sprk_spaarkeai (Dataverse, published) | ✅ |
| 036 | Retire DocxAnnotationWriter + push-annotations **(Path B ✅ owner)** | 3 Patch Engine | opus / xhigh | FULL | 032 | done (bae44955b) | ✅ |
| 040 | AI generate-window bookmark + resolve-on-return (FR-07) | 4 AI | opus / xhigh | FULL | 020,**006** | done | ✅ |
| 041 | Validate anchors; fuzzy-as-comment fallback (FR-07) | 4 AI | sonnet / xhigh | FULL | 040 | done | ✅ |
| 042 | AI anchoring tests — concurrent-edit | 4 AI | sonnet / high | FULL | 040,041 | done | ✅ |
| 050 | Version-stamp + re-anchor-on-stale (FR-08) | 5 Concurrency | sonnet / xhigh | FULL | 032 | done | ✅ |
| 051 | eTag sequencing for create-on-save (FR-08) | 5 Concurrency | sonnet / xhigh | FULL | 050 | done | ✅ |
| 052 | HTTP 423 lock → ProblemDetails (FR-08) | 5 Concurrency | sonnet / high | FULL | 050 | done | ✅ |
| 053 | Import round-trip — revisions/comments mount (FR-10) | 5 Concurrency | sonnet / xhigh | FULL | 021,030 | done | ✅ |
| 054 | Concurrency + import seam slices (NFR-06/08) | 5 Concurrency | sonnet / high | FULL | 050,051,052,053 | done | ✅ |
| 060 | Hard-replace cutover completion (FR-12); **KEEP renderer (C-revised)** | 6 Hardening | opus / high | FULL | 032,034,**006,036,038** | ✅ done-with-exception: writers gone; mammoth retained for transient mounts → R5 G6 (§6.5 Path-A) | ✅ |
| 061 | Corpus proof + size + CVE + NetArch (NFR-01/04/05) | 6 Hardening | sonnet / high | FULL | 060 | ✅ all green (0a9710cd1): 28/28 byte-diff, 515+531 tests, 46.11 MB, no new CVE, ADR-013 green | ✅ |
| 062 | Deploy full R4 + CIPO operator UAT | 6 Hardening | sonnet / high | FULL | 060,061 | 🔔 **READY — owner-orchestrated deploy (deploy boundary)** | 🔲 |
| 063 | **Flagship gate G — all 8 criteria green** | 6 Hardening | opus / high | FULL | 062 | blocked | 🔲 |
| 090 | Project wrap-up (+ /test-diet gate) | Wrap-up | sonnet / medium | STANDARD | 063 | blocked | 🔲 |

---

## Parallel Execution Groups (waves)

Most `Services/Compose/` tasks share files and are `parallel-safe: false` — a rip-and-replace on a tightly-coupled service is intentionally sequential-heavy. Genuine parallelism exists where a client task runs alongside a server task, or a test/spike runs alongside implementation.

| Wave | Tasks | Prerequisite | Parallel-safe notes |
|---|---|---|---|
| **W0a** | 001, 002, 003 | none | 001 `parallel-safe:false` (main-session ADR write to `.claude/`); 002 + 003 run parallel (fixtures vs new contract files) |
| **W0b** | 004, 005 | 002 (004,005) · 003 (005) | both `parallel-safe:true` (harness vs spike, separate code) |
| **GATE** | 006 | 003,004,005 | sequential judgment — **authorizes all cutover/deletion tasks** |
| **W1** | 010, 011 | 001 (010) · 001,003 (011) | 010 `true` (stamper) ∥ 011 `false` (projection); **012 sequences after 011** (same file) |
| **W1b** | 012 → 013 | 011 (012) · 010,011,012 (013) | 012 `false` (shares `ComposeDocxProjectionBuilder.cs`); 013 seam `true` |
| **W2** | 020, 021 | 003 (020) · 012 (021) | `true` — client interceptor ∥ client atom node (separate files) |
| **W2b** | 022 → 023 → 024 | 020 (022) · 022+**006** (023) · 020,021,022 (024) | 022 `false` (extends 020); **023 gated on 006** (`prescriptive`, deletion); 024 tests `true` |
| **W3** | 030 → 031 · 033 · 034 | 003,005,**006** (030) | 030→031 `false` (same engine file); 033 `false` (save path); 034 seam `true` |
| **W3b** | 032 → 035 | 030,031,**006** (032) · 032,034 (035) | 032 `prescriptive` cutover (gated); 035 `prescriptive` deploy |
| **W4** | 040 → 041 → 042 | 020,**006** (040) | 040→041 `false` (generate/apply path); 042 tests `true` |
| **W5** | 050 → 051 · 052 · 053 → 054 | 032 (050) · 021,030 (053) | 050/051/052 `false` (shared save path); 053 `true` (import surface); 054 seam `true` |
| **W6** | 060 → 061 → 062 → 063 | 032,034,**006** (060) | 060 `prescriptive` (gated cutover); 062 `prescriptive` deploy+UAT; 063 final gate |
| **Wrap** | 090 | 063 | `prescriptive` close-out (+ mandatory `/test-diet`) |

### `/goal` wave eligibility (Step 3.85)

**No wave is `goal-eligible`.** R4 is a mission-critical hard-replace: nearly every wave contains an irreversible cutover, a deploy, or a human judgment gate (006, 063), which disqualifies goal-loop auto-pacing. Run waves with per-task `task-execute` dispatch; the operator confirms each gate.

---

## Critical Path

```
003 (op schema) → 005 (applier spike) → 006 (PHASE 0 GATE) → 030 (Patch Engine)
   → 031 (structural) → 032 (retire writers) → 034 (corpus proof) → 060 (hard-replace)
   → 061 → 062 (deploy+UAT) → 063 (flagship gate) → 090 (wrap-up)
```

The operation schema (003) is the spine both ends implement. The Phase 0 gate (006) is the hard prerequisite to every cutover/deletion (023, 032, 060). Structural ops (031) sequence last within the Patch Engine.

## High-Risk Items

- **006 (Phase 0 gate)** — RED gate blocks the entire cutover half. The corpus harness (004) + applier spike (005) are its evidence.
- **011 + 030 (offset→run mapping)** — the hardest bridge surface; opus/xhigh; spiked in 005.
- **031 (paragraph-mark deletion via `w:pPr/w:rPr/w:del`)** — flagged by prior art as the hardest OOXML edge.
- **032 / 060 (hard-replace deletions)** — irreversible, gated on 006; escalate (Path-B) if any legacy-only behavior is uncovered.
- **`Services/Compose/` contention** — overlaps `spaarkeai-compose-r1/r2/r3` + `spaarke-ai-architecture-redesign-r2`. `/conflict-check` before every BFF PR.

## Coordination reminders (every BFF task)

Placement Justification in PR · publish size ≤60 MB (delta vs ~49.63 MB) · seam slice (ADR-038) · no AI-internal injection (ADR-013) · consume `Services/Ai/PublicContracts/`, no fork · `/conflict-check` before the PR.
