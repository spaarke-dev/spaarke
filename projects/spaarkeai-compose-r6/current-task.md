# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (032 starting; agents 051+061+wave-review in flight)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **033 IN FLIGHT** (exec-033 agent, isolated worktree): chrome-provenance seam suite + the §21 imported-carrier wire slice (assess-then-fill-gaps vs 030/032 coverage; test-only). Wave-2 review CLOSED at db1cc3406 (two HIGHs fixed: §F.1 NullComposeTemplateSource + carryDegradationWarnings through requestLoad; + isDirty apply-time re-check hoisted, ispersonal org-shared guard). Publish post-fixes 47.00 MB (ceiling 60); CVE clean. |
| **Next task** | On 033 return: cherry-pick, verify, Step 9.5 (TEST-MODIFYING → FULL), close Phase 3 COMPLETE. Then Phase 4: 040 (PDF intake, opus/FULL) → 041/042. Then 090 wrap-up (test-diet gate; needs 042 + 052? — check TASK-INDEX deps: 090 needs 014✅,027✅,033,042,052,061✅). 052 = check what remains in Phase 5 (051 done). Follow-ups ledger: If-Match apply replace; ApiError 404 branch; 051 popup fallback; #690 double-run dedup; D1-D7 operator approval; flaky CreateOnSaveTests FakeTimeProvider. Branch ~45+ behind master — reconcile at next PR. |
| **Status** | 033 agent running; all pushed through db1cc3406 |
| **Next Action** | Triage 033 return → close-out → 040. |

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
