# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (032 starting; agents 051+061+wave-review in flight)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **032 — part-merge endpoint + client wiring** (FULL/opus, main session) STARTING: server half = POST template-merge endpoint in Api/ComposeEndpoints.cs invoking IComposeTemplateSource (031 facade) + ComposeTemplatePartMergeEngine (030) + persist via the EXISTING save path; client half = template-select + apply affordance in ComposeWorkspace.tsx (theme tokens, ADR-021). |
| **Next task** | IN-FLIGHT PARALLEL: exec-051 agent (AllDocuments version-history affordance, isolated worktree), exec-061 agent (sdap-ci.yml self-contained fidelity-gate job, isolated worktree; aligns with PR #690 LFS approach), PLUS the wave-3150 Step 9.5 review agent (031+050+060 commits) — triage its findings on return. After 032: 033 (provenance seam incl. imported-carrier slice per §21). Branch ~41 behind master (reconcile at next PR). |
| **Status** | 032 starting; three agents in flight |
| **Next Action** | Implement 032 server endpoint → commit → client affordance → commit → Step 9.5 → close out. On agent returns: cherry-pick their commits (051/061), triage wave review findings. |

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
