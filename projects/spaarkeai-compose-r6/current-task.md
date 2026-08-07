# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (032 starting; agents 051+061+wave-review in flight)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | AGENT WAVE 2 IN FLIGHT: **exec-032** (apply-template endpoint + ComposeService.ApplyTemplateAsync + workspace affordance; design DECIDED: app-only Dataverse token at endpoint mirroring CommunicationTemplateEndpoints:95-110, document ops stay user-OBO; agent has full spec) + **exec-051** (AllDocuments version-history affordance). **061 CLOSED into branch** (cherry-pick 4745cf7bb → local; sdap-ci.yml compose-fidelity-gate job, YAML 9 jobs OK, gate cmd verified 11/11; aligned w/ PR #690 — note: after #690 merges, build-test ALSO runs FidelityGate → dedup decision for later). Wave-1 review APPROVED; fixes in ec69d6a79. |
| **Next task** | On agent returns: cherry-pick commits, verify (build + Compose filter + client tsc baseline ~28), Step 9.5 review agent per FULL task (032 needs one; 051 needs one), close out 032/051/061 (POML+TASK-INDEX+notes; 061 still needs its close-out flip). Then 033 (provenance seam incl. imported-carrier slice per §21), 052. Branch ~41+ behind master — reconcile at next PR with /conflict-check. |
| **Status** | 061 merged locally (not yet close-flipped); 032+051 agents running; pushing checkpoint |
| **Next Action** | Triage agent returns as they land. |

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
