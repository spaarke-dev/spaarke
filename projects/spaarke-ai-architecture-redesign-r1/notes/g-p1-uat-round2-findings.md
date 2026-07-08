# G-P1 Browser UAT — Round 2 Findings (2026-07-06)

> Operator: ralph.schroeder on spaarkedev1 (Spaarke Assistant, SpaarkeAi code page).
> Round-1 findings + fixes: [g-p1-uat-round1-findings.md](g-p1-uat-round1-findings.md).
> Fix commit: `9ee30e672` (deployed to spaarke-bff-dev + sprk_spaarkeai 2026-07-06).

## What round 2 verified as WORKING

- Upload → **auto-classify only** (Engagement Letter.docx → engagement-letter 95%; SEC FORM 4.pdf → other 85%) — the round-1 operator UX ruling ("auto-classify, chip-offered summarize") is live. ✅
- The [Summarize] chip **dispatches correctly when clicked** (operator: "i clicked and it works"). ✅
- Typed "summarize this document" fell through to the legacy "I couldn't find a confident match… Open Library" reply — **EXPECTED until P2 task 034 hard-cutover** (chat NL → agent loop). Not a defect; noted for gate 038.

## Round-2 defects and fixes (all in `9ee30e672`)

### RD-1 — Chip strip stranded at the TOP of the pane
**Observed**: the [Summarize] chip rendered above the entire message transcript (top of the Assistant pane), visually detached and easy to miss; the muted brand-tint styling read as "disabled" up there.
**Root cause**: `ConversationPane` rendered `<ConsumerChips>` before the `SprkChat` flex block — layout-wise above the transcript.
**Fix**: new optional `aboveInputSlot?: React.ReactNode` prop on shared-lib `SprkChat` (pure layout seam, no styling/behavior); `ConversationPane` renders the chip strip through it, so chips sit directly ABOVE THE INPUT ZONE, below the transcript — the conversation's leading edge.

### RD-2 — "Summarize again" froze to the ORIGINAL file set
**Observed**: after summarizing file 1, uploading a new file and clicking the chip still summarized the original file.
**Root cause**: `SessionDispatchOrchestrator.BuildTransitionChips` pre-filled the dispatched batch's `fileIds` into the transition chips — the follow-up chip re-targeted the frozen set forever.
**Fix**: authored `prefill_slots` forward verbatim; otherwise transition chips carry **NO args** — a follow-up click resolves the file set AT DISPATCH TIME (FR-08 default = the full CURRENT session manifest). Contract test assertion inverted (`DispatchSessionEndpointContractTests`: chips segment must NOT contain `"args"`).

### RD-3 (latent, found during triage) — attachment gating used the composer strip
**Observed in code**: attachment-requiring chips gated on SprkChat's composer chips (`status === "ready"`), which SprkChat clears on every stream completion (FR-07) — after any typed message the chip would gray out even though the session manifest still holds the files.
**Fix**: session-level count = manifest-promoted chip ids (`promotedChipIds`, pruned on removal + reset on session change) ∪ composer-ready chips; drives both the disabled-chip UI and the `dispatchConsumer` guard.

## Test evidence
- Dispatch contract tests 15/15 (incl. new no-frozen-args assertion)
- Eval + EventRules + SessionDispatch filter run 38/38
- Shared-lib SprkChat suites 361/361 (new `aboveInputSlot` prop)
- SpaarkeAi conversation jest 316/316 (SprkChat stubs now render the slot)

## Gate disposition
Operator directive 2026-07-06 ("you should also move on to P2 while i sleep so we continue making progress"): task 027 closed as **operator-directed conditional pass** — round-2 defects fixed + redeployed; final visual spot-check of chip placement/behavior happens in the operator's next session (fold into gate 038 UAT if not sooner). Per NFR-11 the browser observations above were made by the operator, not inferred from tests.
