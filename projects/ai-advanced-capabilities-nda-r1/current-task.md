# Current Task — `ai-advanced-capabilities-nda-r1`

> **Last Updated**: 2026-07-27 (by context-handoff, pre-/compact). **Read this block first.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **State** | NDA advisory review **WORKING END-TO-END LIVE** in spaarkedev1 (grounded gpt-5 findings + Review-Summary panel + in-doc highlights + right-gutter comments). |
| **Branch** | `work/ai-nda-r1-followups` (off master 751532d7e; pushed; **NOT merged/PR'd**). HEAD `6330d9ce8`. Working tree CLEAN. |
| **Deployed** | BFF → spaarke-bff-dev (hash-verified, healthy). Code page → `sprk_spaarkeai` (published). Both from current branch HEAD. |
| **Next Action** | Implement the **5-item UAT plan below** (start with #4 — the one real bug). Owner chose: checkpoint now → /compact → resume the build. |
| **App Insights** | appId `6a76b012-46d9-412f-b4ab-4905658a9559` (component `spe-insights-dev-67e2xz`, rg `spe-infrastructure-westus2`). Query via `az monitor app-insights query --app <id> --analytics-query "..."`. This is how every root cause this session was found — USE IT. |

### ✅ Fixes DEPLOYED this session (2026-07-27) — the review pipeline now works
1. **Reasoning-tier request shape** (`OpenAiClient.GetStructuredCompletionRawAsync`): OMIT both `temperature` AND `MaxOutputTokenCount` for the ReasoningModel deployment (gated by `IsReasoningDeployment`). The live blocker was `max_tokens` (the SDK serializes `MaxOutputTokenCount`→`max_tokens` even at api-version 2025-04-01-preview; gpt-5 rejects it). +15 unit tests. Commit `82c087a31`.
2. **Grounding wired into the linear Action path** (`ActionRunner` + `AnalysisAction.AllowsKnowledge` surfaced from `sprk_allowsknowledge` via `AnalysisActionService`): retrieves from `spaarke-rag-references` (TopK=12, MinScore=0) and injects into the prompt. Reuses `ReferenceRetrievalService` incl. its NFR-06 tenant OR-clause. Commit `f53c83397`.
3. **References-index semantic-config name** (`ReferenceRetrievalService`): `knowledge-semantic-config` → **`rag-references-semantic-config`** (matches the live index; the old name 400'd every query). Commit `083b1cf67`. **This was the fix that made grounding actually work** — confirmed live: `Reference grounding: chunks=12 sources=3 chars=22349`, output cited KNW-011 clauses B5/B6/B8/B9 (de-embedded from the prompt → only retrieval could produce them).
4. **Advisory comments on the card dispatch path** (`useConsumerChips` `onDispatchResult` → `ndaReviewAdvisoryComments.emitFromResult`): the "Review an NDA" card dispatches via chips, which never reached the bridge; now it does → gutter comments + highlights + summary render. Commit `6330d9ce8`.
- Also this session: reasoning temperature (round 1, correct but not the blocker); "Review an NDA" card moved to the follow-on strip (`local:nda-review`); UC3 `nda-standard-summary` Action+binding created live (id `27bef356-3889-f111-8077-7ced8ddc4a05`); merged origin/master (`e31fa0902` assistant-r1 UI fix) after an earlier code-page deploy had clobbered it.

### 🔨 UAT round-2 feedback → 5-item PLAN (the work to do next)
| # | Item | Type | Approach / files |
|---|---|---|---|
| **4** | **Advisory comments must survive Save → bake as native Word `w:comment`** (confirmed broken: Word web + desktop show nothing after save) | **BUG** | ROOT CAUSE FOUND via App Insights: `Compose save: ... re-anchored: auto=0 review=0 orphan=5 of 5 comment(s)` — all 5 comments landed in the **ORPHAN** band on a STALE save (stamped eTag `,1` vs live `,3`), and orphaned comments are surfaced-but-NOT-baked. Uploaded NDA has client-minted paraIds; base moved across multiple saves → anchors don't match live baseline. Server bake logic itself is CORRECT (`ComposeService.cs:625` `if (ContentModel is null && (hasOperations \|\| hasComments))` → `_patchEngine.Apply(..., request.Comments, ...)`). Fix = make advisory-comment anchors survive the save (client `getAnchoredComments` capture + server re-anchor/stamp for client-minted-paraId docs). **Needs a CLEAN repro first: fresh-upload a NEW NDA, review, do ONE save (avoid the multi-save stale state) to confirm the non-stale path bakes before fixing the orphan case.** Files: `ComposeService.cs` (re-anchor/`ReanchorStaleSaveAsync`/`ComposeBaselineParaIdStamper`), `ComposeWorkspace.tsx` (save ~1027 `getAnchoredComments`), `ComposeEditor.tsx` (`placeAdvisoryComments`/`getAnchoredComments`). |
| **1+2** | **"Review" toolbar dropdown** — icon-only, RIGHT side; dropdown = "Review Summary" (toggle summary panel) + "Review Notes" (toggle gutter comments) | Feature | `ComposeEditor.tsx` toolbar + visibility state for the summary panel + `ComposeCommentGutter`. Both surfaces already exist; add a toolbar control + show/hide state. |
| **3** | **Review Summary = concise TL;DR** — VERY concise for quick orientation; **STICKY at top** (stays visible while scrolling the doc); **each summary point LINKS to its section** (click → scroll/navigate to that clause position) | Feature | Summary panel component: render overall risk + ranked High/Critical one-liners (not a full duplicate of comments); position sticky; wire each point → editor scroll-to-anchor (reuse the comment-anchor paraId/coordsAtPos the gutter already uses). |
| **5** | **Comment cards truncate** ("...") — add **expand/collapse** to see the full comment text | Feature | `ComposeCommentGutter.tsx` — per-card expand toggle. |
> **Design principle to preserve:** Review Summary = *review chrome* (toolbar-toggled, NEVER baked into the document/.docx). Review Notes/Comments = *annotations on the document* (DO bake as native Word comments so they travel with the file). Original "summary as a first doc page" idea is SUPERSEDED by the toolbar-toggle approach (owner-approved) — do not inject the summary into document content.

### ⚠️ Open follow-ups (non-blocking, deferred)
- **Over-broad grounding gate:** `sprk_allowsknowledge=true` on **57 of ~60 Actions**, so my `action.AllowsKnowledge` gate makes many unrelated Actions (classify, briefing, create-matter…) also query the references index (degrades gracefully — relevance-filtered/caught, but wasteful + could inject irrelevant refs). Proper fix = per-Action knowledge-source LINK (retrieve only from an Action's linked reference sources), not the broad flag. Do this once the review UX is solid.
- **Binding mirror STALE vs live (13 drifts):** `Seed-PlaybookConsumers.ps1 -DiffOnly` shows live `sprk_playbookconsumer` evolved past committed `infra/dataverse/sprk_playbookconsumer-rows.json`. Do NOT run the full seeder (reverts live drift). Reconcile deliberately → `-Export` + commit.
- **Merge/PR:** `work/ai-nda-r1-followups` not merged; PR #690 (CI-LFS) still open.

---

**Mode**: post-build UAT iteration (owner-driven). Branch-only; deploy-to-dev per owner request each round; every root cause found empirically via App Insights (no guessing).

**Status**: NDA review pipeline LIVE + WORKING; 5-item UAT plan pending · **HEAD**: 6330d9ce8 (branch work/ai-nda-r1-followups)

## Done + committed (gated)
- 001 ✅ ADR-039 amendment (Output Determinism Modes; grounding mode-independent; strengthened + signed off)
- 010 ✅ model-tier last-mile (gate CLEAN) · 011 ✅ runtime picker + override composition (gate CLEAN + cache-staleness/ADR-016 fixes applied)
- 012 🔄 KNW-011 source + tenant-pin analysis (live ingest env-blocked)
- 013 ⛔ Reasoning provisioning (config+runbook; Azure external; recommends GPT-5)
- 020 🔄 NDA-REVIEW Action (jps PASS; live run env-blocked) · 021 ✅ standard-summary Action (jps PASS)
- 022 ✅ bindings + card + classification (gate CLEAN) · 023 ✅ whole-doc fan-out (gate CLEAN, zero prod code)
- 030 ✅ review-summary panel · 031 ✅ advisory-comments event/receiver (gate CLEAN) · 041 ✅ Summary-Page DOCX · 042 ✅ SPE-versioning test (gate CLEAN)
- 033 ✅ Draft Alternative + trace activation — VERIFICATION-ONLY, zero prod files changed: the
  bindingId-resolution + trace-surfacing mechanism was already shipped by prior merged
  compose-r2/r4 work (useComposeToolbarActivation, wired in both Compose mount hosts; Binding
  row already seeded live; ContextPaneController auto-opens Execution Trace on any Compose tab).
  38 relevant tests + tsc --noEmit verified clean. See notes/task-033-draft-alternative-trace-activation.md.
- 051 ✅ Golden-utterance dispatch eval (gate CLEAN) — net-new eval family (nda-review-eval-cases.json
  + NdaReviewDispatchEvalTests.cs), joined to the existing Category=GoldenUtteranceEval merge gate
  (same pattern as AssistantEnhancementsR1EvalTests.cs — did NOT touch the shared
  golden-utterances.json). 6 cases: Click (card), 3x Text NL paraphrase, required negative
  (off-target), bonus disambiguation vs nda-standard-summary. `dotnet test --filter
  "Category=GoldenUtteranceEval"` → 101 total (9 new), 0 failed. Not env-blocked — fully
  mechanical/offline (Dataverse-stubbed), matching every sibling family's established
  "live" vocabulary in this suite.
- 032 ✅ right-gutter comment layout (gate CLEAN) — new `ComposeCommentGutter.tsx`: right-rail
  Fluent v9 cards per advisory thread, live position via exported `findCommentAnchorRange`
  (task 040's primitive, never stale `anchorText`) + `coordsAtPos`, pure unit-tested
  collision/stacking (`layoutCommentGutterCards`), reflow on transaction/scroll/resize.
  Code-review caught + fixed a real bug: first-paint height estimate (96px) never got
  re-measured past mount — added a requestAnimationFrame follow-up pass + regression test.
  Metadata passthrough done: riskLevel/sectionRef/standardRef now flow
  PaneEventTypes→ComposeWorkspace→placeAdvisoryComments→createThread→gutter risk badge
  (previously dropped); overallRisk now rides the compose_advisory_comments wire
  (useNdaReviewAdvisoryCommentsBridge dispatches the already-typed-but-dropped field),
  NdaReviewSummaryPanel prefers it over the derived fallback. adr-check CLEAN
  (ADR-049/021/030/012/039/040). Builds clean (AI.Widgets, Compose.Components, SpaarkeAi
  surface-gate 0 new errors); 559/559 Compose.Components + 647/656 SpaarkeAi tests green
  (9 pre-existing unrelated AiSessionProvider e2e failures, already logged below).
- 040 ✅ comment-export wiring fix (gate CLEAN) — root cause: client sent `annotations`
  (DocxAnnotationInput, text-anchored); `SaveComposeDocumentBody` never deserialized that property
  (server only reads `comments`/ComposeAnchoredComment) — every comment silently dropped. Added
  `ComposeAnchoredComment` client type (compose-operations.ts) + `getAnchoredComments()` on
  ComposeEditorHandle (replaces `getCommentThreadAnnotations`), combining BOTH the session Comments
  panel threads AND 031's `getAdvisoryCommentThreads()` — resolved via the EXISTING `resolveRunAnchor`
  (paraId/runIndex/offset) primitive, no new anchoring mechanism. ComposeWorkspace.tsx sends
  `comments: anchoredComments`; the dead `annotations` field path fully removed. New seam test
  `ComposeImportedAnchorsSurviveSaveSeamTests.Save_NewAnchoredComment_ThenReload_RoundTripsViaDocxAnnotationReader`
  proves save→native w:comment→reload round-trip. 526 server + 547 client Compose tests green;
  publish 51.29 MB (delta 0.00 — no server production code touched). Discovered-but-out-of-scope:
  DEF-11/DEF-13 AI-review-flag comments (FR-29 AnchoredAnnotation store) still don't export (separate
  data source, textPattern-anchored) — stale comments corrected, tracked as follow-on below.

## Remaining
- 050 eval harness+rubric · 052 tenant-pin integration test (gated on tenant-pin fix)
- 060 deploy (env-blocked) · 061 UI tests (env-blocked) · 090 wrap-up

## 🔔 Owner decision outstanding
- **Tenant-pin OR-clause fix** (`tenantId eq '{t}' or eq 'system'`, idiom already in repo). Security-adjacent. Gates LIVE grounding + task 052. Recommended: approve.

## Follow-ons backlog (for 090 / deploy)
- sprk_outputdeterminism Dataverse column + BFF read-path (make ADR-039 mode=data; today prompt-enforced)
- ReasoningModel token-leak guard (resolver fallback if `#{...}#` unresolved) — deploy gate 060
- Definitive compressed publish-size measure at 060 (subagents' §10 method ≈51.29 MB, under 60)
- DEF-11/DEF-13 AI-review-flag comments (FR-29 AnchoredAnnotation store, `textPattern`-anchored) do
  not export as native `w:comment` on Save — separate data source from task 040's session/advisory
  comment-thread fix; would need the same paraId+range resolution treatment (likely via
  `resolveTargetSpans('strict')` at save time) if this is wanted before deploy
- born-in-editor (blank/AI-draft) create-on-save skips `comments` application server-side entirely
  (`ComposeService.SaveAsync` only applies comments when `ContentModel is null`) — not exercised by
  NDA-REVIEW (always a loaded doc), but a real gap if Compose ever wants comments on a blank doc's
  first save
- 010 low items: resolver doc, Binding.cs:120 comment, Fast-tier test, config validation
- Worst-case 50-finding output vs ADR-040 128KB inline ledger cap → blob/SPE offload (050/060)
- Pre-existing unrelated test failures to note at 090: Services.Communication.* (5), three-pane-compose-coordination e2e (AiSessionProvider)

## Next action
033 ✅, 051 ✅, 040 ✅, 032 ✅ done (through this pass). Check 050's status before assuming it's still
in-flight (another wave agent may have landed it concurrently — see TASK-INDEX.md for current state).
Hold 052 (tenant-pin decision). Then deploy/wrap-up (env-blocked → report + runbooks).
