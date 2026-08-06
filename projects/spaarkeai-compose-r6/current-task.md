# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-06 (task 012 close-out — clean boundary)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — clean boundary. **012 COMPLETE**: the surgical save path is retired end-to-end (server + THE CLIENT CUTOVER). Post-cutover clients never reach ComposeShadowPatchEngine or the count-gate on ANY save. |
| **Next task** | **013 — Seam + regression tests: NDA saves (no 422), edits land, new version** (`tasks/013-*.poml`; deps 004 ✅ + 012 ✅ satisfied). 027 (fidelity corpus seams) also unblocked but SERIAL — 013 first (critical path 013 → 014 deploy/UAT). |
| **Status** | 012 closed at `7e4cd1822` (+ this close-out commit); working tree to be pushed |
| **Next Action** | On "continue": invoke task-execute for task 013 — FULL rigor, sonnet/high. Read the 013 OBLIGATIONS below FIRST (routed from 012's Step 9.5). |

### Critical Context (3 sentences)
012 commits: 70be80006 (canonical model on all mount doors, one-mint-two-walks id agreement) ·
2fc8ff530 (comment-bake retired — comments ride the model incl. carrier-part APPEND; author fallback;
post-save model return; transitional op-log Warning telemetry) · 3e4a9f456 (P-2 + ADR-049 FR-08 note) ·
09b79eaae (CLIENT CUTOVER: buildImportedContentModel merge mapper + diffTokens redlining; routing flip;
026-F5 warning separation) · 7e4cd1822 (review fixes F1-F5 + ADR-049 point-4 Path-B micro-amendment).
Step 9.5: adr-check PASS 8/8; code-review REQUEST-CHANGES → all fixed same-task. Suites: server Compose
979/982 (3 pre-existing NDA reds — 013/027 OWN THEM), client 78/78, tsc 28-error pre-existing baseline;
publish 46.91 MB clean-worktree (+0.01); no new HIGH CVE.

### 013 OBLIGATIONS (routed from 012 Step 9.5 — read notes §18 triage)
1. The 3 pre-existing NDA suite reds are 013/027's to re-baseline or retire:
   ComposeSummaryPageSeamTests.AppendSection · ComposeBaselineParaIdStamperTests.MintAndPersist ·
   ComposeReadFidelityHarnessSeamTests.TextExactness (all NDA-fixture).
2. F6 (review minor): comment-id collision — session-allocated id can match a carrier comment id absent
   from the loaded model (projection-flattened) → server `comment-id-collision` warn or server-echoed
   allocation floor.
3. F7 (review minor): load-time canonical-projection flatten warnings are server-log-only; return them on
   the mount doors + fold into the FIRST model-path save's saveDegradationWarnings (pairs with FR-08 harness).
4. adr-check residuals: R4.5 T-2 narrative refresh (/project now mints + echoes mutated bytes — still
   stateless); FR-08 harness case for carrier-with-comments + new-comment append; transitional-telemetry
   dashboards note; post-save re-projection perf watch on large docs.
5. 027 additionally owes the REAL multi-author redlined corpus fixture (corpus has ZERO revision markup —
   025-F6).

### Standing items
- **014 deploy note**: BFF + `sprk_spaarkeai` MUST deploy together (old client on new server drops
  separate-comments LOUDLY via `comments-ignored` — acceptable only within the atomic-deploy window).
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
- Execution shape per task: implement → seam/unit slice → commit → Step 9.5 (TWO parallel background
  agents on the committed SHA) + clean-worktree publish (46.91 MB baseline now) → fix commit → close-out.
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- /conflict-check before every BFF PR (Services/Compose most-contested; 012's check: clean window).
- Before the eventual PR: merge origin/master (~67 behind; Crypto.Xml HIGH patched there) + re-run
  /conflict-check.
- Fidelity-widener backlog in notes §16; sign-offs R4/R5 RESOLVED to warned baselines (2026-08-06).

## Steps completed this task
(none — no active task)

## Files modified this task
(none)

## Decisions
(none — see notes §18 for 012's record incl. the Step-9.5 triage)
