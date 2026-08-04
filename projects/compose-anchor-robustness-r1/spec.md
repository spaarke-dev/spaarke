# Compose Anchor Robustness & Fidelity Hardening — R1

> **Created**: 2026-08-04 · **Branch**: `work/compose-anchor-robustness-r1` (off master)
> **Governing ADR**: [ADR-049 Compose Shadow Document](../../.claude/adr/ADR-049-compose-shadow-document.md) (amendment proposed here)
> **Supersedes the reactive-patch pattern** of compose-r3/r4/r5's per-UAT anchor fixes.

## Why this project exists

Every Compose save failure across R3→R5 has been the **same class**: anchor reconciliation
between the TipTap editor model and the OOXML model diverging, discovered reactively in UAT and
patched one divergence at a time (text-search → paraId; runIndex/offset → paraOffset; now paraId
uniqueness/count). The core architecture (OOXML server-authoritative + surgical ops) is **correct**
and stays. What is under-engineered and causes the treadmill:

1. **Brittle reconciliation** — exact index alignment, all-or-nothing count gates, assumes a clean
   single-branch paragraph model, and **hard-fails (`ParagraphNotFound` → 422)** on any miss. A
   robust resolver already exists (`AnnotationReanchorService`: AUTO/REVIEW/ORPHAN + similarity
   scoring) but only runs on the *stale-base* path — the normal write path throws instead.
2. **Unrepresentative test corpus** — the 24-doc byte-diff corpus is clean; real signed/legal docs
   (text boxes, signature graphics, letterhead, fields, tables, track-changes) are absent, so
   divergence classes surface only in UAT.

## Confirmed root cause (the trigger case)

`AppligentNDA_Signed.docx` (committed fixture, `notes/`): a signature/graphic **text box** wrapped in
`mc:AlternateContent` (7 blocks) → Word emits it **twice** (DrawingML Choice + VML Fallback) with the
**same `w14:paraId`s** → 55 `<w:p>` / **52 unique ids / 3 duplicates**; 49 editable body paragraphs +
6 non-editable text-box paragraphs. Server walks `Descendants<Paragraph>()` everywhere (projection,
`ParaIdPreParser`, `ComposeBaselineParaIdStamper`, engine index) → counts 55; the editor sees ~49.
`ComposeBaselineParaIdStamper`'s **all-or-nothing count gate** (`paragraphs.Count != map.Count`) then
stamps **nothing** → every op fails `ParagraphNotFound` → prong 1 refuses the whole save (0 anchored).
General to ANY document with text boxes / drawings / AlternateContent.

## The systemic fix (robustness-by-design, not per-doc)

### FR-1 — Write path degrades gracefully, never total-fails (the class fix)
Unify write-path anchor resolution with the reanchor scorer: resolve each op
`paraId → paraOffset → positional+text fallback → surface as an unresolved item` (reuse the
AUTO/REVIEW/ORPHAN model + prong-1 `PartialApplySummary`). No single divergence ever produces a hard
422 again. Preserve I-7 (no text-search to *locate*; text may be a *verification/fallback* signal, as
the reanchor path already does).

### FR-2 — Align the paragraph model across editor / projection / stamp / engine
Count and walk the **same editable-body paragraph set** (body `Elements` + explicit table-cell
descent), **excluding** text-box / drawing / AlternateContent-fallback paragraphs. Makes the count
gate pass and ids line up by construction.

### FR-3 — Tolerate duplicate `w14:paraId`s
AlternateContent legitimately repeats ids; the engine index + stamp must de-dup deterministically
(prefer the body/Choice occurrence) instead of being derailed.

### FR-4 — Representative corpus + full round-trip fidelity harness (release GATE)
Grow the corpus with real messy docs (starting with `AppligentNDA_Signed.docx`); a harness that runs
load → apply representative edit ops → save → reopen and asserts (a) save lands, (b) untouched content
byte-identical, (c) edits land correctly. Moves discovery from UAT to CI.

### FR-5 — ADR-049 amendment
Codify: "the write path degrades gracefully — never total-fails on an anchor miss" + "representative-
corpus round-trip is a release gate."

## Phasing / tasks

| Task | Scope | Note |
|---|---|---|
| **001** | **FR-1..FR-3 robust anchor fix** (align model + dedup ids + positional fallback) + `AppligentNDA_Signed.docx` regression (fail-first) + byte-diff corpus green | THE IMMEDIATE FIX — resolves the NDA + the whole text-box/AlternateContent class; done on this branch now |
| 010 | FR-4 corpus + round-trip fidelity harness (CI gate) | needs dedicated worktree; collect real docs |
| 020 | FR-5 ADR-049 amendment (graceful-degradation + corpus-gate principles) | main-session (.claude/ write boundary) |
| 090 | Wrap-up + deploy (anti-clobber) | BFF + sprk_spaarkeai together |

**Worktree**: task 001 lands on `work/compose-anchor-robustness-r1` in the current checkout. Tasks
010+ (multi-task, parallel with other Compose projects) get a dedicated `spaarke-wt-compose-anchor-
robustness-r1` worktree + `projects/INDEX.md` row for conflict coordination.

## Constraints (inherited)
ADR-049 I-1..I-7 (esp. I-4 byte-surgical untouched subtrees, I-7 no write-path text-search-to-locate);
byte-diff corpus stays green; publish ≤60 MB; `/conflict-check` before every BFF PR; NEVER delete
`docxBridge.ts`; deploy BFF + `sprk_spaarkeai` together.
