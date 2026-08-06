# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-06 (task 013 close-out — clean boundary)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — clean boundary. **013 COMPLETE**: the NDA 422 is a permanent CI regression (through-the-wire seam + regression tests); the suite floor is ZERO expected reds (987/987); F6/F7 review fixes shipped. |
| **Next task** | **027 — Fidelity seam tests across the corpus** (`tasks/027-*.poml`; deps 021-026 + 012 all ✅). 014 (deploy/UAT) needs BOTH 013 ✅ and 027 — so 027 is the sole critical-path next step. |
| **Status** | 013 closed at `06631ba57` (+ this close-out commit); to be pushed |
| **Next Action** | On "continue": invoke task-execute for task 027 — read the 027 OBLIGATIONS below FIRST. |

### Critical Context (3 sentences)
013 commits: e2adc5130 (RenderOnSaveSeamTests wire round-trip + NdaSaveNo422RegressionTests + the 3
pre-existing NDA reds re-baselined green + F6 comment-id-collision warn + F7 contentModelWarnings
mount-door → first-model-save fold) + 06631ba57 (review minors: F6 test pins, banner copy, dead wrapper,
doc fixes). Step 9.5: adr-check PASS 6/6, code-review APPROVE-WITH-MINORS all fixed. Publish 46.91 MB
±0.00; no new HIGH CVE; suite floor ZERO reds — any future red is a REAL regression.

### 027 OBLIGATIONS (accumulated)
1. **REAL multi-author redlined corpus fixture** (025-F6): the corpus has ZERO revision markup — 027 owes
   a genuine multi-author tracked-changes document (or a Word-authored fixture) exercising 025's
   capture/render round-trip at seam level.
2. Pre-first-save duplicate-paraId reference-resolution probe (013 adr-check residual 1): between load
   and first save the package carries source-duplicate ids under first-wins reference semantics — probe
   citation/reference resolution on a not-yet-saved duplicate-id document.
3. FR-08 harness posture: corpus round-trip breadth (all fixtures through load→model→save→reopen),
   incl. a carrier-with-comments + new-comment append case at seam level.
4. Smaller residuals if in reach: R4.5 T-2 narrative refresh (/project mint+echo); transitional-telemetry
   dashboards note; post-save re-projection perf watch note.

### Standing items
- **014 deploy note**: BFF + `sprk_spaarkeai` deploy together; old client on new server drops
  separate-comments LOUDLY (`comments-ignored`) — acceptable only within the atomic-deploy window.
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
- Execution shape per task: implement → seam/unit slice → commit → Step 9.5 (TWO parallel background
  agents on the committed SHA) + clean-worktree publish (46.91 MB baseline) → fix commit → close-out.
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- /conflict-check before every BFF PR. Before the eventual PR: merge origin/master (~67 behind;
  Crypto.Xml HIGH patched there) + re-run /conflict-check.
- Client tsc baseline: 28 pre-existing sibling-dist errors; client full-jest has 10 pre-existing
  stepOperationInterceptor reds (op-log machinery, untouched since compose-r5) — NOT this project's.
- Fidelity-widener backlog in notes §16; sign-offs R4/R5 RESOLVED to warned baselines.

## Steps completed this task
(none — no active task)

## Files modified this task
(none)

## Decisions
(none — see notes §19 for 013's record)
