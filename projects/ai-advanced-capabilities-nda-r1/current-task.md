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

## Remaining
- 040 comment-export wiring (client ComposeWorkspace save → ComposeAnchoredComment in `comments`; server ApplyComment; MUST wire 031's `getAdvisoryCommentThreads()`) — deps 031✅/041✅/030✅
- 032 right-gutter layout (after 040 — shares ComposeWorkspace; thread riskLevel/sectionRef through per gate022031 note)
- 033 draft-alternative activation (ComposeAiToolbar bindingId stub) — dep 022✅
- 050 eval harness+rubric · 051 dispatch golden utterances · 052 tenant-pin integration test (gated on tenant-pin fix)
- 060 deploy (env-blocked) · 061 UI tests (env-blocked) · 090 wrap-up

## 🔔 Owner decision outstanding
- **Tenant-pin OR-clause fix** (`tenantId eq '{t}' or eq 'system'`, idiom already in repo). Security-adjacent. Gates LIVE grounding + task 052. Recommended: approve.

## Follow-ons backlog (for 090 / deploy)
- sprk_outputdeterminism Dataverse column + BFF read-path (make ADR-039 mode=data; today prompt-enforced)
- ReasoningModel token-leak guard (resolver fallback if `#{...}#` unresolved) — deploy gate 060
- overallRisk not on the compose_advisory_comments wire (030 derives client-side) — thread real field via PaneEventTypes
- Definitive compressed publish-size measure at 060 (subagents' §10 method ≈51.29 MB, under 60)
- 010 low items: resolver doc, Binding.cs:120 comment, Fast-tier test, config validation
- Worst-case 50-finding output vs ADR-040 128KB inline ledger cap → blob/SPE offload (050/060)
- Pre-existing unrelated test failures to note at 090: Services.Communication.* (5), three-pane-compose-coordination e2e (AiSessionProvider)

## Next action
Launch wave: 040 + 033 + 050 + 051 (disjoint files). Hold 032 (after 040), 052 (tenant-pin decision). Then deploy/wrap-up (env-blocked → report + runbooks).
