# Current Task — `ai-advanced-capabilities-nda-r1`

**Mode**: autonomous wave execution (owner-authorized 2026-07-25). Waves in parallel via subagents; gates after each wave; branch-only (no master merge); env-coupled steps flagged, never faked.

**Active**: Wave 3 — 023 (opus orchestration) + gate011, parallel. 021 (summary) ✅ complete this pass. 022 HELD until 023 commits (both touch BFF dispatch).
**Status**: in-progress

## Committed so far (PR #689)
001✅ · 010✅(gated) · 012🔄 · 013⛔ext · 020🔄(jps PASS) · 011🔄(dispatch-spine, seam tests pass)
Latest HEAD: 06317bbbc. Publish 47.49 MB.
022✅ — Bindings (nda-review/default, nda-standard-summary/default) + "Review an NDA" card +
classification wiring. Zero BFF touched (reused chat-classify + capability-discovery endpoint as-is).
Client build + typecheck pass (0 surface-owned errors); 19 SpaarkeAi Jest suites / 104 tests green;
surfaceLaunchRegistry test added. See notes/task-022-bindings-review-card-classification.md.
NOT YET COMMITTED to git (uncommitted, pending Wave 4 gate/commit per project convention — a parallel
process is concurrently landing 031 in this same worktree; verify no file overlap before committing).

## gate011 fixes — APPLY AFTER 023 COMMITS (023 is editing SessionDispatchOrchestrator.cs — do NOT race)
- **[Medium] SessionDispatchOrchestrator ~:550 cache-staleness**: compose against FRESH `action.ModelTier`, drop the cached `binding.ActionModelTier` fallback → `request.ModelTierOverride ?? binding.ModelTierOverride ?? action.ModelTier`. (Stale binding tier could silently substitute in the 5-min TTL window on the no-override path.) One-line fix.
- **[Low] ModelTierOverrideDispatchSeamTests:15** doc comment wrong (Routing/Scope are mocked, not real) — correct comment.
- **[Low ADR-016 MIS-CITATION]** ~6 files (010/011 code comments: ModelTierDeploymentResolver, SessionDispatchOrchestrator:547, ChatEndpoints; + design.md:265, spec.md:87,147) cite ADR-016 for the "single tier→deployment surface / deferred-enhancement" rule. ADR-016 is Cost/Rate-limit/Backpressure — says nothing about tiers. Fix: cite **ADR-039** (single dispatch surface) for the single-mechanism claim; keep ADR-016 ONLY for the budget linkage. (No ADR governs tier resolution — it was established de novo. Accuracy matters — this is the project's whole point.)

## Follow-ons surfaced (backlog)
- sprk_outputdeterminism Dataverse column + BFF read-path (make ADR-039 mode=data; today prompt-enforced). 
- ReasoningModel token-leak guard (resolver fallback if #{...}# unresolved) — deploy gate 060.
- 010 low code-review items (resolver doc overclaim; Binding.cs:120 stale comment; Fast-tier test; config validation).
- Tenant-pin OR-clause fix — AWAITING OWNER.

## Completed / committed
- [x] 030 — Review-summary docked panel (FR-07). `NdaReviewSummaryPanel.tsx` (+ test, 13 tests) mirrors
  ComposeCommentThread's docked-panel convention; mounted in ComposeWorkspace.tsx (sibling banner after
  ComposeBannerStack — not ComposeEditor.tsx, where the sibling panels actually dock; directional
  deviation, documented in the task POML notes). Consumes the SAME `compose_advisory_comments` event
  031 already receives (3 additive lines in 031's existing handler, 031's lines untouched); derives
  overallRisk client-side (max severity of rendered findings) since that event doesn't carry the
  Action's literal overallRisk field — labelled "(from findings)" in the UI; follow-on noted to thread
  the real field once a project owns PaneEventTypes.ts again. Exports added to index.ts. Build +
  typecheck clean; full package Jest suite 547/547 (no regression). UI tests env-blocked (no live org).
  NOT YET COMMITTED — pending Wave 4 gate/commit.
- [x] 001 — ADR-039 amendment (Output Determinism Modes; grounding mode-independent; strengthened). ✅ SIGNED OFF.
- [x] 010 — model-tier last-mile. ✅ Gated CLEAN (commit 9c9696ab3). Publish 47.49 MB. 4 Low follow-ups deferred to 090.
- [~] 012 — KNW-011 source authored + tenant-pin analysis (commit 9c9696ab3). 🔄 live ingest env-blocked; tenant-pin ESCALATION pending owner.
- [x] 021 — NDA-STANDARD-SUMMARY Action (UC3), STANDARD rigor. New Action (§11 reuse test failed for compose-explain-clause/compose-summarize-word-changes): `infra/dataverse/actions/nda-standard-summary.action.json` + input/output schema mirrors. Fast tier, outputDeterminism=advisory, closed contract {overview, sections[]}. jps-validate PASS (adapted mirror-first mode, same as 020). NOT YET COMMITTED — uncommitted new files pending Wave 3 gate/commit. Live sample-run env-blocked + shares nda-review's NFR-06 tenant-pin gate.
- [x] 042 — SPE save + versioning verification (test-only), STANDARD rigor (TEST-MODIFYING → gates apply). New seam test `tests/integration/seam/Compose/SpeSaveVersioningSeamTests.cs` — reuses `ComposeFidelitySeamFixture` (no new fixture). Asserts two real saves mint two distinct SPE versions (ETag differs, `ReplaceFileContentAsUserAsync` called twice) and a third save naming the first version's id as `baselineVersionId` resolves via production `ResolveSaveBaselineAsync` → `DownloadFileVersionAsUserAsync`, proving the prior version stays retrievable. Redis save-stamp concurrency mechanism (FR-08) explicitly NOT re-tested here (already covered by `ConcurrencySaveSeamTests.cs`) — noted in file header per task constraint. Zero production code changed. Build PASS, test PASS (1/1), code-review + adr-check both clean. NOT YET COMMITTED — pending gate/commit alongside other uncommitted Wave 3/4/5 work in this shared worktree.
- [x] 041 — Summary-Page DOCX writer, FULL rigor. New `ComposeSummaryPageGenerator` (pure, deterministic — TL;DR/overview/recommendations templated from the ledgered `{overallRisk, flaggedSections[]}`, NO second LLM call) emits plain `ComposeBlock`s (style/numbering-independent by contract); new `ComposeDocumentRenderer.AppendSection(byte[], IReadOnlyList<ComposeBlock>)` appends a manual-page-break + the blocks as NON-TRACKED content at the true end of the document (detaches/reattaches the trailing body-level `SectionProperties`, mints fresh paraIds) — deliberately NOT a ComposeShadowPatchEngine operation (would emit unwanted tracked `w:ins`). Wired into `ComposeService.SaveAsync` via a new optional `SaveComposeDocumentRequest.SummaryPage` field — appended after any operation-log/comment patch, before the SPE upload. No new NuGet; `DocxAnnotationWriter` untouched/still retired. Seam test `tests/integration/seam/Compose/ComposeSummaryPageSeamTests.cs` (8 tests, real compose-corpus fixtures, no mocks) — page break present, Summary content present, trailing sectPr still last body child + still exactly one, every ORIGINAL paragraph OuterXml byte-identical, appended paraIds unique + minted, zero `w:ins` tracked marks, empty-blocks no-op passthrough. Build PASS; full Compose seam suite 52/52 PASS; publish 51.29 MB (compressed, incl. PDBs) vs 47.49 MB last-recorded baseline (+3.80 MB — NOTE: this worktree carries other already-landed-but-uncommitted Wave 3/4/5 changes (022/031/042/etc.); the delta is NOT attributable to task 041's own ~2 new/changed server files alone, which add only a few KB of IL). Well under the 60 MB ceiling and the 55 MB cumulative-review flag. Sequenced AHEAD of 040 per user instruction (040 not yet started) — verified no functional collision: 040's server-side work is a no-op per its own notes ("server side WORKS + is tested"; the gap is client-side wiring), so 040 should not need to touch the `SaveAsync` block this task added. See `notes/task-041-summary-page-docx-writer.md`. NOT YET COMMITTED — pending gate/commit alongside other uncommitted work in this shared worktree.

## Running (subagents)
- 011 runtime picker (client) · 013 Reasoning provisioning (config+runbook; Azure env-blocked) · 020 NDA-REVIEW Action (opus, critical path)

## 🔔 Pending owner decision
- NFR-06 tenant-pin fix: recommended OR-clause `tenantId eq '{t}' or eq 'system'` (idiom already in repo). Security-adjacent. Gates live grounding + task 052. Does NOT block 020 authoring / Compose UX.

## 010 code-review follow-ups (Low, deferred to wrap-up 090)
- ModelTierDeploymentResolver doc overclaims "ONLY tier→deployment" (ModelSelector coexists for OperationType) — narrow wording.
- Binding.cs:120 EffectiveModelTier comment stale (says ModelSelector decides).
- Add Fast-tier symmetry seam test.
- Optional: startup validation that Fast/StandardModel non-empty when DI enabled.

## Next
On 011/013/020 completion: client build verify, gate, commit each; then Wave 4 (030+031 after 020→022→023). 023 (opus) + 022 come after 020.

## Wave plan (deps-driven)
- Wave 1 (parallel): 010, 012 — deps: 001 ✅
- Wave 2 (parallel): 011, 013 — deps: 010
- Wave 3: 020 (opus) — deps: 001,010,012 → then 022, 023 (opus), 021
- Wave 4 (parallel): 030, 031 — deps: 023; then 032 (after 031), 033 (after 022)
- Wave 5: 040 (after 031), 041 (after 023), 042 (after 023)
- Wave 6 (parallel): 050, 051, 052 — evals
- Wave 7: 060 deploy, 061 UI, 090 wrap-up (env-coupled / final review)

## Env-coupled (implement code/config; flag live steps)
- 012 live ingest + tenant-pin empirical check (Azure AI Search creds)
- 013 Reasoning deployment provisioning (Azure)
- 020 live model calls · 060 deploy · 061 UI tests on live org
- Live verification acceptance criteria across the above → reported as blocked-pending-environment.

## Gates (mandatory, per wave)
dotnet build (BFF) / npm build (client) → code-review + adr-check (Step 9.5) → commit to branch → next wave.

## Next action
Awaiting Wave 1 subagents (010, 012). On completion: build + gates + commit, then launch Wave 2 (011, 013).
