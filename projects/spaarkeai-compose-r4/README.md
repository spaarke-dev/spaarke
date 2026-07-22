# Spaarke Compose R4 — Shadow Document Architecture

> **Status**: Initialized (pipeline complete — ready for execution)
> **Created**: 2026-07-22
> **Owner**: Ralph Schroeder
> **Branch**: `work/spaarkeai-compose-r4`
> **Codename**: Spaarke Compose R4 (MISSION CRITICAL — rip-and-replace)

> **Portfolio**: _not yet registered_ — run `/devops-project-register` to attach a Project Issue, or leave unregistered.

---

## What this project is

R4 rips out the current Compose translation/save layer and replaces it with a **Shadow Document Architecture**:

- The OOXML `.docx` is the **server-side source of truth** (held at ~100% fidelity by `DocumentFormat.OpenXml` + retained original bytes).
- TipTap/ProseMirror is a **lossy view + controller** — never the author of `.docx` bytes.
- Every edit is captured as a **step-level operation anchored by `(paraId, runIndex, run-local-offset)`** and applied surgically to the retained OOXML by a **single unified Patch Engine** (`ComposeShadowPatchEngine`).

This eliminates the two defect classes that made R1–R3 unshippable:
1. **Fidelity loss** on untouched content (from re-deriving the `.docx` from a lossy editor model).
2. **Insertion-location failures / HTTP 422** (from whole-document text-search anchoring).

**Fidelity lives on the server; editing tools live in TipTap; the bridge between them is the engineered core.**

## Source documents

- **Spec**: [`spec.md`](spec.md) (AI-optimized, the execution source of truth)
- **Design**: [`design.md`](design.md) (rationale + locked decisions D1–D5)
- **Evidence base** (`notes/`):
  - [`senior-reviews-2026-07-22.md`](notes/senior-reviews-2026-07-22.md) — two external reviews verbatim
  - [`research-digest.md`](notes/research-digest.md)
  - [`as-built-inventory.md`](notes/as-built-inventory.md) — KEEP/RIP file:line ground truth
  - [`bridge-prior-art.md`](notes/bridge-prior-art.md) — permissive prior art catalog
- **Phase 0 fidelity corpus (sample docs)**: [`notes/sample-docs/`](notes/sample-docs/) — CIPO patent letter (known 422 + track-changes case), Engagement Letter (formatted contract), Test Matter Create (fields/content-controls). Owner supplies additional worst-offenders in Phase 0.

## Locked decisions (owner-directed 2026-07-22)

| # | Decision |
|---|---|
| **D1** | Step-level operational deltas (ProseMirror steps → operations), NOT `getHTML`/paragraph-diff |
| **D2** | Anchor = `(paraId, runIndex, run-local-offset)` — never run-ids, never absolute doc positions |
| **D3** | `docx` built end-to-end now; pdf/xlsx/pptx are explicit LATER phases |
| **D4** | SPE stays store + open-in-Office launch surface; versioning/lock/423 in scope, WOPI-embed out |
| **D5** | ONE unified `ComposeShadowPatchEngine` replaces BOTH `DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer` |

## Cutover strategy

**Hard replace** (owner-confirmed). No parallel-run A/B. The **Phase 0 proof gate** (operation schema + applier spike on the CIPO doc + corpus byte-diff harness green) is a HARD prerequisite to removing any old path.

## Graduation criteria (from spec §Success Criteria)

1. [ ] **Byte-preserving** — no-op save byte-identical on untouched subtrees across the corpus (byte-diff harness).
2. [ ] **Placement determinism** — every edit/redline/comment (user + AI) lands at its anchor; zero write-path text-search.
3. [ ] **AI drift-proof** — generation with concurrent edits lands at the rebased selection; bad anchors refused.
4. [ ] **Concurrency** — stale-base save re-anchors (no eTag 500); HTTP 423 surfaces cleanly.
5. [ ] **Structural edits** — split/merge/insert/delete round-trip on the corpus.
6. [ ] **Word-native output** — opens in Word-for-web with accept/reject redlines + threaded comments.
7. [ ] **Hard-replace complete** — both legacy writers + paragraph-diff export removed; `mammoth` gone; publish ≤60 MB; ADR + tests green.
8. [ ] **Phase 0 gate passed before cutover** — schema + applier spike on CIPO + corpus harness green PRIOR to any old-path deletion.

## Coordination (hot-path — BFF=Y, SpaarkeAi=Y)

`Services/Compose/` and the SpaarkeAi Compose surface overlap active siblings. **Run `/conflict-check` before every BFF PR.**

- `spaarkeai-compose-r3` — the **deployed base** R4 extends (Phase-1 + Bug-A). Its `ComposeParagraphRedlineSynthesizer` is what R4 retires.
- `spaarkeai-compose-r1` / `spaarkeai-compose-r2` — earlier Compose satellites on `Services/Compose/`.
- `spaarke-ai-architecture-redesign-r2` — **sole owner of `Services/Ai/` internals**; consume `PublicContracts` seams only, **no fork** (ADR-013).
- Resolve the cross-project **notifications build break** on tip-of-master (`spaarke-notification-spine-r1` `@spaarke/notifications` unwired dep) before any SpaarkeAi deploy from tip.

## Layout

```
projects/spaarkeai-compose-r4/
├── README.md            ← this file
├── spec.md              ← AI-optimized specification (source of truth)
├── design.md            ← design + rationale
├── plan.md              ← implementation plan (WBS + discovered resources)
├── CLAUDE.md            ← project AI context
├── current-task.md      ← active-task state tracker (context recovery)
├── tasks/               ← numbered POML task files + TASK-INDEX.md
└── notes/               ← evidence base, handoffs, corpus sample-docs
```

## How to execute

Say **"continue"** or **"work on task 001"** — the `task-execute` skill loads knowledge + ADRs and runs each task with the correct rigor level. See [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for the wave plan and dependency graph.
