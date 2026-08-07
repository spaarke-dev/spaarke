# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (by context-handoff — clean boundary post-Phase-3; next 040)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **040 ✅ + 041 ✅ COMPLETE** — PDF intake end-to-end. Commits: `5ae5a4246` (040 impl) + `2d72046aa` (wire fix) + `f0f9a34ec` (040 triage: all HIGH/MED) + `c73055d33` (041 client) + `48d17ac31` (041 triage: A-HIGH-1 replace-target guard, apply-template both legs, Word-actions gate, grid clamp). Both Step 9.5 reviews PASS-WITH-FINDINGS, every HIGH/MEDIUM fixed same-session except B-MED-3 (container placement — recorded DECISION POINT). Full record: `notes/040-pdf-intake.md`. Server 390/390 + 18/18; client tsc clean + 48/48. |
| **Step** | Phase 4 remainder: task **042** next. **B-MED-3 RESOLVED** (operator 2026-08-07, option C): PDF-sourced create-on-save inherits the source PDF record's ADR-024 link lookups (`81ac8d695` — server inheritance + client sourceDocumentRecordId + 2 contract tests; contract suite 6/6). |
| **Status** | ALL work committed + pushed through `81ac8d695`; no agents in flight |
| **Next Action** | On "continue": task-execute **042** (PDF intake tests + lossiness UX) — BINDING scope = "042 test plan" in `notes/040-pdf-intake.md` (reducer lifecycle, flow negatives, banner, endpoint 503-vs-422 mapping, B-LOW-3 invariant, A-LOW-2 display fix) + an inheritance flow assertion (client sends sourceDocumentRecordId only when pdfSourced ∧ Path-A). Then 052 → 090 wrap-up. Publish baseline 47.00 MB. |

### Critical Context (3 sentences)
Dev environment now runs render-on-save end-to-end; the assistant-enhancements-r2 session's deploys are
FROZEN per operator option A (r2 re-coordinates at its merge — "when it is ready to merge and deploy
we'll coordinate"). UAT defects D1-D7 are triaged in notes/phase1-deploy-uat.md — D1 (dup records per
save-session) is proven PRE-EXISTING (pre-deploy records exist; all 6 rows share one sprk_graphitemid);
D2 (curly quotes → digit 2 in AI-suggested text) lives in the suggestion pipeline NOT Services/Compose,
and D3 (placement failure) is its likely consequence. NONE were hot-patched (gate constraint honored).

### Standing items
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
  UAT evidence: indentation ×84 + paragraph-style ×85 flatten warnings on a REAL NDA → these two
  move to the FRONT of the fidelity-widener backlog (notes §16).
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- /conflict-check before every BFF PR. PR #743 overlap on ComposeWorkspace.tsx pending (second merger
  resolves). Publish baseline now **46.94 MB incl. PDBs** (post-master-merge, task 014).
- Corteva NDA (notes/, UNTRACKED — real signed agreement) = corpus row-4 candidate; needs operator
  confidentiality sign-off before committing (task 060 decision point).
- Execution shape per task: implement → slice → commit → Step 9.5 (two background agents on the SHA) +
  clean-worktree publish → fix commit → close-out.
- Client baselines: tsc 28 pre-existing sibling-dist errors; full-jest 10 pre-existing
  stepOperationInterceptor reds (untouched since compose-r5).

### Remaining project phases
Phases 3-7: 030-033 (template part-merge) · 040-042 (PDF intake) · 050-052 (version-history UX — UAT
finding 9 confirms users look for this in-app) · 060/061 (CI fidelity harness — seed candidates: §1.7
fixture + Corteva NDA pending sign-off) · 090 (wrap-up, /test-diet gate; deps 014 ✅ + 027 ✅ + 033/042/052/061).

## Steps completed this task
(none — no active task)

## Files modified this task
(none)

## Decisions
(none — see notes/phase1-deploy-uat.md for 014's record incl. the anti-clobber option-A decision)
