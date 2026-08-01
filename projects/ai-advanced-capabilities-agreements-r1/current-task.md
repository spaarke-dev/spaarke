# Current Task — `ai-advanced-capabilities-agreements-r1`

> Context-recovery anchor. Updated by `task-execute` at every step; reset at task completion (root CLAUDE.md §7).

## Active task

- **Task**: 060 DEPLOY running (parallel phase ✅ 20/23; last commit 057a41972, pushed; 042 done incl.
  main-session locationLabel wiring in ComposeEditor dispatchNoteToolRequest).
- **060 env-state brief given**: live already = registry/agreement-review Action/Binding flip/packs;
  to deploy = classify Action+schemas+Binding row; CLOBBER HAZARD = mirror behind live ~16 fields
  (surgical ops only); known limitations = NO Reasoning-tier AOAI deployment in dev (owner action),
  PR #690 OPEN (CI seam tests red until merged). Then 061 e2e → 090 wrap-up (+test-diet gate).
- **Wave 7/8 highlights**: 041 ✅ (batch in ComposeEditor via useSerialActionQueue; ConversationPane untouched)
  · 033 ✅ (SESSION-COINCIDENCE: wizard mints ONE Analysis-owned session = document + adopted chat session;
  refined B-i — literal shape would have dispatched into a file-less session; ensureChatSession zero-diff;
  90 suites/832 tests) · 051 ✅ (render-from-persisted memo toolbar; 2 GET siblings; publish 48.25 MB flat).
  PRE-EXISTING (stash-proven, sibling messaging project): AI.Widgets standalone tsc build + 'Messages' test
  broken at clean HEAD — SpaarkeAi vite build (deploy artifact) green; note for 060.
- **Wave 6**: 023 ✅ (one decision point; sanity warn-only; lookup-write corrected A1 stale column; 826/826)
  · 032 ✅ (FR-16 CLOSED: panel restore, Leg-B cap notice, multi-select materialization, supersede
  BindingId-safe, dedupe guard proven; cross-device Leg-B residual documented)
- **Wave 5**: 022 ✅ (both envelope legs; NOTIFY doc for hub relay incl. stale-columnName flag) · 031 ✅
  (documentSessionWaiter threads sessionIdOverride across all 4 review dispatch sites; gating verified
  already-correct; bridge KEPT; same-mount externalChange dedupe residual → folded into 032's brief).
- **Wave 4**: 030 ✅ (flip deployed surgically — mirror 16 fields behind live, wrap-up item; findings branch both
  vintages, idempotent, zero server change) · 040 ✅ (selectThread reuse in ComposeEditor common-ancestor;
  sectionRef join never-guess). 022 must NOTIFY hub (deep-threading legs building now — notify doc for owner relay).
- **Wave 3 summary (kept for recovery)**:
- **012**: premise CORRECTED — test never weakened; SOURCE regressed via S1-fallback commit 6a414bbac
  (first-occurrence-on-recurrence + kind collapse). Fixed: discriminated union, multiplicity→ambiguous always,
  unique-fuzzy verbatim-prefix kept; 011's deterministic path untouched. 7/7 advisory; suite 779/15 (exact flip).
- **021**: agreement-classify Binding row (Informational, click-path); "review" vs revise-regex conflict resolved
  (new detector first + exclusions); pack binding = registry→SessionDispatchOrchestrator→KnowledgeSourceIds→
  ActionRunner (closes 003 crowding finding, seam-tested); gate 4 branches via ConsumerChips; race fixed.
  ⚠️ §6.5 PATH A EXCEPTION (cite in PR): session-scoped gate state vs ADR-041 gate-ledger formalism —
  documented in notes/021-execution-notes.md. BFF 9628/0; publish ~47 MB; no new CVE.
- **Wave 2 summary (kept for recovery)**:
- **011**: client-side CitationResolver mirror (composeCitationResolver.ts, §11-justified); deterministic
  sectionRef→paraId ahead of text fallback in placeAdvisoryComments; projection already carried
  computedNumber/listPath (typed compose-contracts.ts); 37/37 new tests; zero regressions (stash A/B).
- **050**: POST /api/ai/chat/sessions/{id}/review-memo (client-supplies-resolved-sections per SummaryPage
  precedent — 051 builds on this); ReviewMemoAssembler pure/stateless; +1 additive method on
  AnalysisResultPersistence (conflict-mitigated); 9625/0 regression; publish 48.24 MB (−1.39). ⚠️ OWNER
  AWARENESS: (a) added missing sprk_value column (Multiline 200K) to sprk_analysisoutput in spaarkedev1 —
  fixes latent pre-existing write bug, §6.5 Path C documented; (b) memo endpoint design = client-supplied
  dispositions — reviewer sign-off suggested at PR time.
- **Wave-2 conflict note**: multi-container-multi-index-r1 branch (STALE since 2026-06-10) has a 71-line
  unmerged refactor of AnalysisResultPersistence.cs — 050 kept its edits there minimal/additive.
- **Wave 1 summary (kept for recovery)**:
- **003**: KNW-011 restructured (B1–B16, 14 chunks) + KNW-012 authored (G1–G16, 13 chunks) at
  projects/x-ai-spaarke-platform-enhancements-r1/notes/design/knowledge-sources/ (shared pack home);
  production-filter retrieval verified + negative tenant check. ⚠️ FINDING: ActionRunner never sets
  KnowledgeSourceIds (whole-corpus retrieval; NDA gets ~2/14 chunks) — registry→KnowledgeSourceIds scoping
  belongs to Phase 2 (021/023 must wire it).
- **020**: agreement-classify Action via ActionRunner (sole Reasoning-resolver caller); registry injection via
  Services/Ai/Classification/ assembler (no new executor/endpoint); publish 47 MB (Δ≈0); 24 new + 80 regression
  tests pass; NO Binding row (021/023 own trigger); live evals env-blocked.
- **052**: export-mapping seam + shared advisoryNoteFormatting.ts; author config; Word UAT → 060/061.
- **MAIN-SESSION HOTFIX (002 fallout, surfaced by 052)**: live-path bridge migrated to post-split shape —
  PaneEventTypes.ComposeAdvisoryCommentItem +flaggedClause/assessment; useNdaReviewAdvisoryCommentsBridge
  accepts both vintages (composes legacy explanation from discrete fields); ComposeWorkspace maps discrete
  fields → placeAdvisoryComments. Bridge tests 14/14; AI.Context/Outputs/Widgets + Compose + SpaarkeAi + BFF
  all build green (sibling node_modules chain installed in this worktree).
- **Wave 0 carry-forward**: 002 generalized IN-PLACE (consumerType "nda-review" retained → 020-023 territory);
  B1–B16 taxonomy hand-off in notes/002-execution-notes.md; KNW-012 forward ref → 003 reconciles; footgun fix
  7e022e7dd still hub-branch-only; ComposeSummaryPageGenerator.cs stale doc-comments → 030; live LLM eval env-blocked.
- **Conflict check (Wave 1)**: compose-r5 FULLY MERGED (risk retired). Soft: multi-container-multi-index-r1 (56
  unmerged) touches ReferenceRetrievalService.cs — 003 reads it only; verify against CURRENT master filter.
- **Next action**: on agent completion — verify, TASK-INDEX ✅, build-verify (BFF if .cs; both Compose/UI libs), commit, Wave 2 (011 ∥ 050).
- **Orchestration**: agents do NOT write .claude/, current-task.md, TASK-INDEX.md, or git commits —
  main session aggregates, runs wave-end build verification (npm builds for UI.Components +
  Compose.Components; dotnet build if 002 touched .cs eval tests), then single wave commit.
- **Next action**: on agent completion — verify acceptance, update TASK-INDEX (🔲→✅), build-verify, commit, start Wave 1 (003 ∥ 020 ∥ 052).

## Coordination state (ALL CLOSED — owner confirmations 2026-07-31)

- Hub answers: [notes/COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md](notes/COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md).
  Q1: deep-threading slice OURS (022; A1/A3-core hub-shipped — never rebuild). Q3: we load the 7 seeds (001).
- **Owner confirmations (2026-07-31, chat)**: ✅ **Q4 `sprk_key` alt-key CREATED** (001 still sanity-verifies via
  describe/dup-test before keying on it). ✅ **Q2 promote-FK fix FIXED** — ⚠️ caveat: no fix commit visible on
  origin/master or the hub branch as of the last fetch; 033 step-0 MUST still verify empirically (promote a
  summary-row-less session → durable FK or non-2xx) before building on promote. ✅ **Q5 Phase-1 UAT OK** — the
  wizard-finish seam (stable + additive, carries `subDomain`) is approved to build on; 033's UAT escalation
  downgrades to a quick seam re-check.

## Steps completed this task

_(none — no active task)_

## Files modified this task

_(none)_

## Decisions this task

_(none)_
