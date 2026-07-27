# Current Task — `ai-advanced-capabilities-nda-r1`

> **Last Updated**: 2026-07-27 (UAT round-4 batch B IMPLEMENTED + TESTED + DEPLOYED; awaiting owner UAT). **Read this block first.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **State** | NDA advisory review LIVE in spaarkedev1. **UAT round-5 COMPLETE — all 9 items IMPLEMENTED, TESTED, DEPLOYED** (batch A #2–#8, #1 relocation+doc-location, #9 progress modal). Awaiting owner UAT. Rounds 2/3/4A/4B also deployed. |
| **Branch** | `work/ai-nda-r1-followups` (off master 751532d7e; pushed; **NOT merged/PR'd**). HEAD `2a4263661`. Working tree CLEAN. |
| **Deployed (2026-07-27 r5 #9)** | Code page ONLY (client-only, no BFF) → `sprk_spaarkeai` (id 5206a442-3451-f111-bec7-7ced8d1dc988), 4924 KB, from HEAD 2a4263661. Master since base = only `0e1e30d95` (email-r5 docs) → no clobber. |
| **Next Action** | Owner UAT of full round-5. Then deferred (owner timing): takeaway-field re-seed (outward-facing Action change), over-broad allowsknowledge gate, merge/PR `work/ai-nda-r1-followups`. |

### ✅ UAT round-5 #9 DEPLOYED (2a4263661) — center-screen live-progress modal
- New `NdaReviewProgressModal.tsx` (SpaarkeAi conversation): renders the shared `@spaarke/ui-components` `AiProgressStepper` (variant="card") in a full-viewport React portal → true screen-center + dimmed backdrop. Real phases: reading → retrieving firm standards → analyzing clauses → writing advisory notes.
- **Honest progress**: the NDA dispatch path emits NO per-stage SSE (BFF = single awaited Action call, one terminal `complete` chunk — confirmed by Explore investigation). So pre-hold steps advance on a timer but HOLD on "Analyzing clauses" until the REAL result arrives; terminal state driven by actual outcome (no fake 100%).
- New `useNdaReviewRunProgress.ts`: idle→running→(complete|error) machine. Driven by 3 real transitions in `ConversationPane`: dispatch-start (`onChipDispatched` gated to `ndaReviewBindingId` — covers BOTH the "Review an NDA" card AND its chip), NDA-shaped result (`onDispatchResult` + `isNdaReviewResult` → complete), settle-without-result (`chips.dispatching` effect → fail; complete wins a late fail). Auto-dismisses after briefly showing the terminal state.
- +10 tests (hook 6, modal 4). tsc-surface-gate Surface-owned 0; dispatch suites green.
- **If richer progress wanted later**: real per-stage frames need a BFF change (add progress `AnalysisChunk` kinds in `SessionDispatchOrchestrator.DispatchAsync` + a `progress` case in `dispatchConsumer.consumeChunk` — closed vocabulary, §10 hot-path). Deferred; the synthesized modal meets the "user understands what's happening" ask client-only.

### ✅ UAT round-5 #1 DEPLOYED (9e14e793c) — Review Summary relocated + doc-derived location
- Panel now renders INSIDE `ComposeEditor` top region (below toolbar, in-flow, expands on toggle) — moved out of `ComposeWorkspace` (mount removed; data threaded via extended `reviewSummary` prop {open,hasFindings,onToggle,findings,placementFailureCount}). Wrapper sticky→relative. Nav = editor's own highlightCitedSpan.
- New `ndaClauseLocation.ts`: `findGoverningHeading(doc,pos)` + `deriveClauseLocationLabel(doc,pos,sectionRef)` → "Pg 1 · Sec 3 · Para 1 · <heading>" (page/para from model, section ordinal + heading from live doc; heading = `heading` node OR paragraph pStyle Heading1..6; graceful fallback to formatClauseLocation). Used by BOTH summary (ComposeEditor enriches findings w/ locationLabel via resolveTargetSpans strict) AND gutter notes (via findCommentAnchorRange). Session comments (no sectionRef) keep "Comment".
- Removed unused overallRisk state from ComposeWorkspace. +7 tests; 617/618 green (same pre-existing advisoryComments fail).

### ✅ UAT round-5 — batch A DEPLOYED (b4204df7b, 2026-07-27)
Client-only Review Summary/Notes polish:
- **#2** removed the "Overall risk" banner (summary). **#4** removed the nav chevron (summary rows).
- **#3/#6** one clear location line via shared `formatClauseLocation(sectionRef)` → "Pg N · Sec N · Para N" (or the heading verbatim when the model puts it in sectionRef), REPLACING the "§ Paragraph N (p.1) ¶ N" glyph soup — identical in summary rows AND gutter notes. (Section number + heading FROM THE LIVE DOC still pending #1.)
- **#5** colour recode (ComposeEditor `compose-mark-comment-anchor` + gutter `cardSelected` + summary `findingRowActive`): BASE clause = LIGHT GRAY (`colorNeutralBackground3`); SELECTED clause + SELECTED note + ACTIVE summary row = YELLOW (`colorPaletteYellowBackground2`, coordinated). Reverses batch B's blue/gray.
- **#7** gutter note body splits "Grounded fact" / "Advisory judgment" into bold-labelled separate paragraphs (`parseAdvisoryNote`; older bare "Judgment"→"Advisory judgment"; plain notes unchanged; structured when expanded/short, truncated plain when collapsed).
- **#8** removed the gutter card `top` CSS transition (it made scroll chase the last frame → "choppy"); cards track scroll instantly now.
- Tests +12 (parseAdvisoryNote 5, formatClauseLocation 5, gutter location/#7 2); 610/611 Compose.Components green (same 1 pre-existing advisoryComments failure, unrelated).

### 🔜 Round-5 deferred — need a direction call
- **#1 (structural):** move `NdaReviewSummaryPanel` mount FROM ComposeWorkspace (line ~2347, above `editorSlot`) INTO ComposeEditor's top region (where `ComposeCommentThread` renders, ~line 2434 — the "unused comments" band the user flagged), in-flow so the "Review Summary" toggle EXPANDS that area. Thread findings/failedCount/onNavigate via the existing `reviewSummary` prop. THEN ComposeEditor (which has the doc) can derive the real section heading + section number by walking from each finding's anchor to the nearest heading node → complete the #3/#6 "Pg 1 · Sec 3 · Para 1 · Agreement Not To Disclose…" format in BOTH surfaces. Riskiest item (layout) — hold for owner OK on placement.
- **#9 (new feature):** center-screen popup streaming the SSE progress of a running review (replace the tiny Assistant "Working" icon). Needs: investigate whether the review's SSE already emits progress events the client can surface (dispatch/streaming pipeline), decide client-only vs BFF, and a design. Bigger; its own turn.

### ✅ UAT round-4 — batch B DEPLOYED (ae8169781, 2026-07-27)
**Batch B done (client-only):**
- **#8 Bidirectional linked highlight + colour swap.** New `marks/SelectedCommentExtension.ts` (ProseMirror VIEW decoration, never serialized to DOCX — same rationale as QaHighlightExtension) paints the SELECTED advisory thread's clause yellow via `compose-mark-comment-anchor-selected`. `CommentAnchorMark` base colour changed YELLOW→LIGHT BLUE (`colorPaletteBlueBackground2`) in ComposeEditor useStyles; selected override = yellow. Shared `selectedThreadId` state in ComposeEditor set from BOTH sides: gutter card click (`selectThread` — toggles + scrolls doc to clause via editorScrollRef coordsAtPos) AND doc click on a highlighted clause (editor `click` DOM handler reads `data-comment-id`; click off-anchor deselects). Effect dispatches select/clear meta to the plugin. Gutter: `selectedThreadId`+`onSelectThread` props; selected card = gray (`cardSelected`, aria-pressed); when selection wired, card CLICK selects + the double-arrow cue becomes a real expand BUTTON (stopPropagation) — select-vs-expand. Unwired/library mounts keep round-3 D2 (whole-card-expands) — existing 45 tests still green.
- **#6 Summary active row.** `NdaReviewSummaryPanel` `activeIndex` state; navigated-to row = `findingRowActive` (gray + brand accent) + `aria-current`, moves on each row click, still calls onNavigate.
- **#5 Summary scroll affordance.** Panel restructured wrapper(sticky chrome + FAB containing block) / scroller(panel: maxHeight 32vh, overflow, scrollbar hidden `scrollbarWidth:none`+`::-webkit-scrollbar{display:none}`); down-arrow FAB (`nda-review-summary-scroll-down`) shown only when content overflows (measured like ComposeEditor FIX #9). testid/role/aria stayed on the scroller so all existing queries pass.
- **Tests +11:** SelectedCommentExtension.test.ts (6 — plugin state + decoration class + DOCX-safe getHTML), gutter selection (3 — onSelectThread, selected aria-pressed, select-vs-expand), summary #6 active-row + #5 FAB-absent (2). Full Compose.Components: **598/599 green**.
- **⚠️ 1 pre-existing failure (NOT batch B):** `ComposeEditor.advisoryComments.test.tsx` "unique target resolves…" expects placed=1 but gets 2 (the "appears twice" ambiguous target resolves as unique in THIS env). PROVEN independent of batch B — fails identically with my ComposeEditor.tsx + SelectedCommentExtension.ts stashed. Likely fixture/env drift introduced earlier (round-3/4A) — flag for owner; not a batch-B regression.

### 🗄️ UAT round-4 — batch A DEPLOYED (e21b43501)
**Batch A done (client-only):** #1 summary visual anchor (brand accent + shadow) · #2 sort control (header bar; by section default / by risk) · #3+#9 § / ¶ markers via `formatSectionRef` (summary rows + gutter cards) · #4 default sort = document position · #7 review-notes pane defaults widest (480, `MAX_COMMENT_GUTTER_WIDTH_PX`). 45 summary+gutter tests green.

**Batch B — TODO (design ready):**
- **#5** Review Summary: hide the panel scrollbar (`scrollbarWidth:none` + `::-webkit-scrollbar{display:none}` on `.panel`) and add a down-arrow FAB when scrollable (mirror `ComposeEditor` FIX #9 pattern — track scroll pos, show a circular Button that scrolls the panel down).
- **#6** highlight the summary row currently navigated-to (active-row selected state). Shares the selection state with #8.
- **#8** BIDIRECTIONAL linked highlight + COLOR SWAP (the crux):
  - Shared "selected thread id" state in ComposeEditor (or a hook), synced both ways.
  - Base highlight color = **light blue**; **selected** highlight = **yellow**. Change `CommentAnchorMark` (`marks/CommentAnchorMark.ts`) to color by selected state (it currently renders a fixed highlight span). Likely a ProseMirror decoration keyed on the selected thread id, OR a mark attr toggle.
  - Click a highlighted paragraph in the doc → select that thread → its gutter card highlights (gray) + the paragraph turns yellow.
  - Click a gutter card → select → scroll to + yellow-highlight the paragraph; the card keeps a **gray** selection until another card/click.
  - Wiring: gutter card `onClick` (careful: card is already a click target for expand when truncatable — need a select-vs-expand affordance, e.g. select on click + expand via the chevron only, OR select always + expand toggle separately). ComposeEditor holds `selectedThreadId`, passes to gutter (selected style) + to the mark/decoration (yellow). Editor click→thread resolution via `findCommentAnchorRange`/posAt.
  - Files: `ComposeCommentGutter.tsx` (selected card style + onClick→select), `marks/CommentAnchorMark.ts` (base light-blue, selected yellow), `ComposeEditor.tsx` (selectedThreadId state + editor click handler + decoration), maybe a small `useSelectedAdvisoryThread` hook.
- **Recommend a fresh context (/compact) before batch B** — it's a coordinated multi-file change on the LIVE working highlighting; do it carefully, not at high context.

### ✅ UAT round-3 — DEPLOYED (commits ce4882142 → 6a414bbac, 2026-07-27)
- **#10 comments-to-Word — FIXED + user-confirmed.** Two-part: (a) bake comments on the ContentModel create-on-save path (`ComposeService.SaveAsync` else-if, fail-soft); (b) the REAL cause — `composeSessionCommentThreadsToAnchoredComments` dropped cross-paragraph comments (`start.paraId !== end.paraId`), so 0 comments were sent. Now CLAMPS a cross-paragraph comment to its start paragraph. Also raised Azure OpenAI `NetworkTimeout` to 300s (`OpenAiClient` + `DocumentIntelligenceOptions.OpenAiNetworkTimeoutSeconds`) — the gpt-5-reasoning review was timing out at the SDK-default 100s ("couldn't run that action").
- **S2/S3/S4 summary takeaways** — `NdaReviewSummaryPanel.deriveTakeaway` (prefers a model `takeaway` field, else derives from explanation: Judgment clause, drop "Grounded fact", first sentence, de-"This"-ed). Short, not a copy of the comment.
- **D2** gutter card clickable-to-expand + double-chevron cue. **D1** resizable comment pane (drag handle, [160,480], sessionStorage). **S5** nav scroll to below-fold via coordsAtPos manual scroll (useDocQaHighlight).
- **S1** advisory placement fallbacks (`resolveAdvisoryAnchorSpan` in ComposeEditor: first-occurrence + verbatim-prefix) so a finding highlights even when its excerpt doesn't strictly resolve. Comment-only (redline edits stay strict).
- **D3** standard-clause hover — new BFF `GET /api/ai/nda-standard/clauses/{ref}` (`NdaStandardClauseProvider`, KNW-011 Part B B1-B16, keyed on B{n} token; `NdaStandardEndpoints`; DI in AnalysisServicesModule; mapped in EndpointMappingExtensions). Gutter card "Standard: {ref}" → Popover fetching via authenticatedFetch. Endpoint live (401 unauth).
- App Insights: **workspace-based** — query Log Analytics workspace `74b7349a-f88d-45a7-b180-8728807a85d7` with `App*` tables, NOT `az monitor app-insights query --app` (returns empty). See memory `appinsights-query-path`.
| **App Insights** | appId `6a76b012-46d9-412f-b4ab-4905658a9559` (component `spe-insights-dev-67e2xz`, rg `spe-infrastructure-westus2`). Query via `az monitor app-insights query --app <id> --analytics-query "..."`. This is how every root cause is found — USE IT. |

### ✅ UAT round-2 — 5 items DONE (commit `cd06cf2e6`, deployed 2026-07-27)
- **#4 (BUG) comment-to-Word**: Seam A in `ComposeService.ReanchorStaleSaveAsync` — stamp client-minted paraIds (from `request.ParaIdMap`) into BOTH the retained baseline AND the re-downloaded current bytes before re-anchoring (reuses `ComposeBaselineParaIdStamper`, fail-open/count-gated/text-verified). A benign stale-save (eTag counter moved, content unchanged) now re-anchors AUTO (exact-paraId) → bakes native `w:comment`; a genuinely diverged doc still ORPHANs (no wrong-paragraph stamp). New green seam test `Save_StaleBase_ClientMintedComment_StampsAndBakesNativeComment_ThroughTheWire` (ConcurrencySaveSeamTests, 4/4). **CAVEAT: fully confirmed only by live repro.** Seam B (fuzzy text re-anchor for Word-structural-rewrite case) deferred — see Next Action.
- **#1+2 "Review" toolbar dropdown**: icon-only right-aligned `MenuItemCheckbox` menu (`ComposeFormatToolbar`) toggling Review Summary (host panel) + Review Notes (gutter); shown only when a review exists. Wired via new `ComposeEditor.reviewSummary` prop + local `reviewNotesVisible` state (gates the gutter). +5 tests.
- **#3 concise sticky linked TL;DR**: `NdaReviewSummaryPanel` reworked — ranked most-severe-first, one line per finding (section+risk+clamped explanation; NO quote/standard duplication), `position:sticky` top, each row clicks → `editorRef.highlightCitedSpan(quotedText, sectionRef)` (strict resolve + scrollIntoView). +4 tests (old citation test replaced with concise-contract + rank + navigate tests).
- **#5 gutter expand/collapse**: per-card Show more/less in `ComposeCommentGutter` (collapse budget 140 chars); re-runs collision layout on toggle. +2 tests.
- Verification: Compose.Components tsc clean · 87/87 ComposeEditor+Workspace · gutter/toolbar/summary suites green · BFF build clean + new seam test green.

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
