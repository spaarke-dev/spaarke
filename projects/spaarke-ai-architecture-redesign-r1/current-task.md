# Current Task State — spaarke-ai-architecture-redesign-r1

> **Last Updated**: 2026-07-06 ~00:20 (by context-handoff — FINAL pre-compaction refresh; operator compacts next)
> **Recovery**: Read "Quick Recovery" first. Full project context: this file + tasks/TASK-INDEX.md + plan.md + notes/g-p0-evidence.md + notes/g-p1-uat-round1-findings.md.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 027 — G-P1 gate (browser UAT round 2 pending) + in-flight "027-fix" agent |
| **Step** | 027-fix COMPLETE + COMMITTED (`befcaa5da`) + BOTH SURFACES DEPLOYED to spaarkedev1 (2026-07-06) |
| **Status** | blocked ONLY on operator round-2 browser UAT |
| **Next Action** | Operator runs the round-2 UAT script (below). On PASS: mark 027 ✅ + P1 PHASE COMPLETE in TASK-INDEX, sync portfolio Tasks Completed=27, dispatch W-P2-A (tasks 030, 031 in parallel per wave protocol). On FAIL: triage findings, fix, redeploy, re-UAT. |

### 027-fix wave — COMPLETE, COMMITTED (`befcaa5da`), DEPLOYED (no agents in flight)
What it fixed (3 G-P1 UAT round-1 defects PLUS the operator UX ruling):
1. **Chips missing/inconsistent** — client chip state replaced/cleared between events (ConversationPane/ConsumerChips/DocumentUploadedEventStream) + catalog rows lacked chip declarations.
2. **Bulk batching bug** — 250ms coalescing fired one event POST per file; fix = count-complete batching (fire when ALL files in the attach gesture have documentIds, ~30s fallback) → ONE event per gesture.
3. **"Files not available yet" race** — server-side bounded readiness re-check (~5s) in EventRulesService before giving up to the notice.
4. **OPERATOR UX RULING (2026-07-05)**: upload now AUTO-CLASSIFIES ONLY; summarize becomes a CHIP. Catalog-data change: chat-summarize row `651194cd-3670-f111-ab0e-70a8a590c51c` sprk_oneventbindings → []; chat-classify row `5f3898d8-db78-f111-ab0e-7ced8ddc4cc6` keeps event membership + gains chipTransitions [Summarize → chat-summarize GUID; multi-file: "Summarize all N?"]. Eval GU-037/038 stubs+expectations updated to classify-only. Ruling supersedes spec FR-P1-03's auto-summarize wording — recorded in notes/g-p1-uat-round1-findings.md (agent writes it).

### Round-2 UAT script (operator, spaarkedev1, after redeploy)
1. Upload ONE file, type nothing → classification line + **[Summarize]** chip (NO auto-summary).
2. Click [Summarize] → summary streams + "Summarize again" chip.
3. Upload 3 files together → per-file classification lines + **[Summarize all 3 files?]** chip; NO per-file auto-summaries; no "files not available" notice.
4. Typed command with upload → supersede notice, command wins (legacy "Open Library" no-match reply is EXPECTED until P2).
5. Console clean; dark-mode spot check.

### Files Modified This Session (all COMMITTED + PUSHED through `9a4270a5b`; fix agent's work is UNCOMMITTED in worktree)
Session commits (all on `work/spaarke-ai-architecture-redesign-r1`, pushed):
`c061c621b` init (51 POMLs) · `091ed29d6` budget-300 · `1aa317b35` W-P0-A · `ef771def1` W-P0-B (+healthz split) · `a93bd6dce` W-P0-C · `70d599348` gate 014 G-P0 · `bdfcb06ba` W-P1-A · `a34bb877a` 021 · `6f7622f3c` W-P1-C · `d1d2a1c06` 023b · `9a4270a5b` 022b+026+ADR-039
CI bot pushes Prettier/whitespace commits to this branch — ALWAYS `git pull --rebase` before push.

### Critical Context
- **Progress**: 26/51 tasks ✅ (P0 complete + G-P0 passed; Track-B 4/4 batches; P1 code complete). Portfolio Issue #550 (Epic #421) synced Tasks Completed=26; Target 2026-08-15. Draft PR #551.
- **ADR-039 + ADR-040 BOTH ACCEPTED** (both docs/adr + .claude/adr copies) — binding for all remaining phases.
- **Deployed**: BFF spaarke-bff-dev @ 9a4270a5b (46.9 MB; /healthz liveness excludes catalog tag; /healthz/catalog = 200-Degraded BY DESIGN until task 030 escalates orphan handlers); SpaarkeAi web resource sprk_spaarkeai published.
- **Known pre-existing test failures (NOT ours, verified at baseline)**: ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup (5) + AuditLogService & PlaybookDispatcherPhaseB latency flakes under parallel load (pass in isolation). NetArchTest: 5 pre-existing (ADR-010 ceiling 129 vs 76 etc.).
- **eval-gate CI job** (sdap-ci.yml) is merge-blocking; hot-path ci-workflows flipped Y in projects/INDEX.md. Operator TODO: mark job REQUIRED when branch protection returns.
- **F-1 audit ruling** (operator): accept-until-cutover; task 044 extended to delete InvokePlaybookHandler/AnalysisQueryHandler/WorkingDocumentHandler AND must re-home the E-2 EngineOutputLedgerAdapter (task 024 attached it to InvokePlaybookHandler; task 040 re-points BindingId=playbookId interim to real insights Bindings first).
- **Deferrals on record**: ADR-040 inline size-cap enforcement → task 047; widget/tab-state Cosmos clobber /defer candidate (task 001 finding); Seed-JpsActions.ps1 deletion needs main-session skill-file sweep (073); SpaarkeAi 5 non-conversation jest suites pre-existing drift → /defer; Build-AllClientComponents.ps1 missing SpaarkeAi (script drift).
- **ConversationPane budget**: ≤300 lines at task 045 with escalation valve (ask before exceeding).

## Wave protocol (what worked — keep doing this)
1. Dispatch parallel task agents (max 6/wave) via Agent tool, each with: POML path, task-execute protocol, FULL/STANDARD rigor declaration, file-ownership boundaries, "known pre-existing failures" list, no-commit/no-TASK-INDEX/no-.claude boundaries.
2. Stagger intra-wave dependencies (don't flat-dispatch when POML deps cross a wave).
3. Wave end: main session flips TASK-INDEX rows, runs `dotnet build` + full suite, triages failures against the KNOWN list (rerun suspected flakes in isolation; prove new ones at baseline via throwaway worktree if needed), commits (`git pull --rebase` first), pushes, syncs portfolio (`updateProjectV2ItemFieldValue`: project PVT_kwHODW0Pv84BEgWu, item PVTI_lAHODW0Pv84BEgWuzgxza1E, Tasks-Completed field PVTF_lAHODW0Pv84BEgWuzhWPlLY).
4. Verify-dead-first on ALL Track-B/inventory items (inventory §9 was stale 8+ times).
5. `.claude/` edits (ADR twins, catalogs) = MAIN SESSION ONLY.
6. /goal wave conditions exist in TASK-INDEX headers (operator-side pilot; unused so far — offer at wave starts).

## Next Phase: P2 (after G-P1 passes)
- W-P2-A: 030 (agent-turn loop contract — NOTE its added acceptance criterion: orphan-handler health-check dimension Degraded→Unhealthy escalation) + 031 (ONE confirmation gate: PendingPlanManager generalized) in parallel.
- Then 032+033 → 034 (HARD CUTOVER chat NL; parallel-safe FALSE) → 035+036 (dispatcher-stack + Chat/Tools deletions; F-1 legs mostly die here) → 037 (eval + injection) → gate 038 (operator browser UAT: four-outcome contract, session memory, confirmed writes).
- P2 kills the legacy "Open Library" no-match reply the operator saw in round-1 UAT.

## Decisions This Session (chronological, operator-ratified where noted)
1. Stop-after-init pipeline; Target 2026-08-15 (operator).
2. ConversationPane ≤300-line budget + escalation valve (operator).
3. F-1: accept-until-cutover (operator).
4. /healthz vs /healthz/catalog liveness split; duplicate detection keys on sprk_toolid; orphan handlers Degraded until 030 (gate-014 semantic corrections).
5. ComposeSummarize + ChatClassify constants added (constants↔rows parity).
6. OBO mcp.tools spike: FAIL-with-path; native handlers remain runtime path.
7. Upload UX: auto-classify + chip-offered summarize (operator, G-P1 round 1) — catalog-data change.

## Parallel Execution

(none in flight — 027-fix landed in `befcaa5da`; worktree clean; branch pushed through `cfd745e83`)

## 027-fix key outcomes (for post-compaction judgment)
- Root causes: chip strip cleared on click + dispatch stream had no chips chunk; 250ms debounce fired one event POST per file; PARALLEL /documents promotions clobbered the read-modify-write session manifest (the real "files not available" cause — promotions now sequential + server readiness probe 5×1000ms).
- Catalog after ruling (verified on spaarkedev1): chat-summarize `sprk_oneventbindings=[]`; chat-classify has [Summarize] chipTransition (requires_attachments); classify-ALL-per-gesture; bulk chip "Summarize all N files?".
- Suite: 7,865 passed / 5 known pre-existing + 1 isolation-passing flake; eval 11/11 (GU-038 → click channel); conversation jest 316/316; publish 46.91 MB.
- Deferred follow-ups (candidates for /defer or task 045): ConversationPane batch-state extraction; concurrency-safe manifest append; production Task.Delay → TimeProvider in the readiness probe.
