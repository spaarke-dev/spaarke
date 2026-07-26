# Current Task — `ai-advanced-capabilities-nda-r1`

**Mode**: autonomous wave execution (owner-authorized 2026-07-25). Parallel subagents per wave; build + code-review + adr-check gates each wave; branch-only (no master merge); env-coupled steps flagged never faked. PR #689.

**Status**: in-progress · **HEAD**: a7bd05316

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
- 032 right-gutter layout (040 ✅ unblocks it — shares ComposeWorkspace; thread riskLevel/sectionRef through per gate022031 note)
- 050 eval harness+rubric · 052 tenant-pin integration test (gated on tenant-pin fix)
- 060 deploy (env-blocked) · 061 UI tests (env-blocked) · 090 wrap-up

## 🔔 Owner decision outstanding
- **Tenant-pin OR-clause fix** (`tenantId eq '{t}' or eq 'system'`, idiom already in repo). Security-adjacent. Gates LIVE grounding + task 052. Recommended: approve.

## Follow-ons backlog (for 090 / deploy)
- sprk_outputdeterminism Dataverse column + BFF read-path (make ADR-039 mode=data; today prompt-enforced)
- ReasoningModel token-leak guard (resolver fallback if `#{...}#` unresolved) — deploy gate 060
- overallRisk not on the compose_advisory_comments wire (030 derives client-side) — thread real field via PaneEventTypes
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
033 ✅, 051 ✅, 040 ✅ done (this pass). 032 is now unblocked (040 done) — next candidate. Check 050's
status before assuming it's still in-flight (another wave agent may have landed it concurrently — see
TASK-INDEX.md for current state). Hold 052 (tenant-pin decision). Then deploy/wrap-up (env-blocked →
report + runbooks).
