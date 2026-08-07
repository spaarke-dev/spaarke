# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (by context-handoff — clean boundary post-Phase-4-build + FULL master sync PR #747; next 042)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — clean boundary. This session closed **040 ✅ + 041 ✅ + B-MED-3 ✅** (PDF intake end-to-end, both Step 9.5 reviews triaged to zero open HIGH/MED, association inheritance per operator option C) **and ran /worktree-sync FULL: PR #747 MERGED** — ALL R6 work (Phases 0–6 incl. 030-033, 040/041, 050/051, 060/061) is now ON MASTER. Branch = master = main-repo master = `7335a71d1` (verified 0/0/0/0). One merge conflict (ComposeService.cs, FR-C3 dedup vs B-MED-3 inheritance) resolved as union-of-both; post-merge suites 404/404. |
| **Tasks complete** | 001-004, 010-014, 020-027, 030-033, 040-042, 050-052, 060, 061. **Remaining: 090 wrap-up ONLY.** |
| **042/052** | **042 ✅** (`d06f3b34e` impl + `19cfb8d0b` triage — Step 9.5 PASS-WITH-FINDINGS, all 3 MED + 3 LOW fixed/accepted same-session; seam 5/5, client 66/66; full record `notes/042-052-close.md`). **052 ✅** — delivered-early-in-050 (SpeVersionHistoryOboSeamTests covers the whole closed acceptance set; re-verified 7/7; deviation recorded, no duplicate authoring per §11/test-diet). |
| **Status** | ALL committed; push pending this checkpoint; NO agents in flight |
| **Next Action** | On "continue": task-execute **090 wrap-up** (`tasks/090-*.poml`, STANDARD/sonnet; deps ALL ✅): `/test-diet` gate (report to `notes/test-diet-report.md`; reconcile this project's added tests vs the 17-ban classifier — the seam/contract/reducer suites are MAINTAIN-class by design), master reconcile (LIGHT — master synced at #747; only 042-arc commits since: d06f3b34e, 19cfb8d0b + docs), `/conflict-check`, final PR + merge, ADR-049 amendment verify (merged with 001), the 6 success criteria checklist, Corteva-NDA corpus-row-4 decision (needs operator confidentiality sign-off — surface it), anti-clobber deploy coordination note (deploy still pending; r2 freeze stands). |

### Critical Context (3 sentences)
Master now carries everything through B-MED-3 (`7335a71d1`, PR #747) — other projects see the full R6
surface; merging ≠ deploying: the BFF + `sprk_spaarkeai` atomic deploy window is still pending and the
assistant-enhancements-r2 deploy freeze (operator option A) still stands until r2 coordinates its merge.
The PDF pipeline is synthesize-at-intake (PDF → DocumentLayout → canonical model → docx via the ONE
renderer, then the standard pipeline); PDF-sourced saves ALWAYS create a NEW docx (never replace the
.pdf item — guarded server-side at baseline AND target) and inherit the source record's ADR-024 links.

### Standing items
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
  Fidelity-widener backlog front: indentation ×84 + paragraph-style ×85 (real-NDA UAT, notes §16).
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- /conflict-check before every BFF PR. Publish baseline **47.00 MB incl. PDBs** (task 040; ceiling 60).
- Corteva NDA (notes/, UNTRACKED via info/exclude — real signed agreement) = corpus row-4 candidate;
  needs operator confidentiality sign-off before committing (task 060/090 decision point).
- Execution shape per task: implement → slice → commit → Step 9.5 background review agent on the SHA →
  triage fix commit(s) → close-out. Both 040/041 reviews caught real defects — keep the gate.
- Client baselines: 5 pre-existing failing jest suites (4× ComposeWorkspace "Element type is invalid" +
  stepOperationInterceptor) — proven pre-existing via stash bisect; owning-project fix candidate.
- Follow-ups ledger (notes/040-pdf-intake.md + notes §23): LOW-9 buffer copies, LOW-10 facade cause
  preservation, A-LOW-2 display (042), pre-existing jest suites, flaky CreateOnSaveTests FakeTimeProvider.

### UAT items for 041 (manual dark-mode ui-tests — operator)
PDF opens with the "Opened from PDF" notice in light+dark · edit → save creates a NEW .docx alongside
the PDF (same matter association — inheritance) · second save updates that docx · Word/apply-template
actions disabled until first save.

## Steps completed this task
(none — no active task; see notes/040-pdf-intake.md for the full 040/041 record)

## Files modified this task
(none — all committed; key session commits: 5ae5a4246, 2d72046aa, f0f9a34ec, c73055d33, 48d17ac31,
81ac8d695, 633e053cf merge, PR #747 → 7335a71d1)

## Decisions
- B-MED-3 (operator, 2026-08-07): option C — BU-level containers make placement shared by construction;
  the new docx INHERITS the source PDF record's ADR-024 link lookups (matter/relatedmatter/project/
  relatedproject/invoice/workassignment), best-effort, mirror-the-source, Path-A only.
- Merge-conflict resolution (worktree-sync): FR-C3 content-dedup (master) + B-MED-3 inheritance (branch)
  both kept, sequential, disjoint attribute sets; ctor params = union.
