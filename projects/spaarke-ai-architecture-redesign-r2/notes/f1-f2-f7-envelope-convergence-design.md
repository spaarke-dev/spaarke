# F-1/F-2/F-7 — Interactive envelope convergence + user-memory recall + live budget measurement (design brief)

> **Date**: 2026-07-10 · **Status**: BINDING design for the implementation task (operator directive: audit findings F-1..F-4 are r2 deliverables, do not defer)
> **Source findings**: `notes/e2e-completion-audit-2026-07-10.md` F-1, F-2, F-7. Prior art: `notes/053-implementation-design.md` (the renderer + producers shipped there; this brief completes the cutover 053 scoped out).

## What we know (audited facts)

1. `ContextEnvelopeRenderer` (81 lines, static) exposes `RenderStablePrefixAdditions` (User.Fragment + Business.Fragment), `RenderEnvironmentSuffix` (Workspace.Fragment = date directive), `RenderConversationSystemMessage` (delegates to `ConversationContextProducer.BuildLedgerOutputsContext`). **Zero production callers.**
2. Interactive prompt assembly today: `PlaybookChatContextProvider`/`SprkChatAgentFactory` call the SAME `ContextSliceProducers` directly (053's "no second render path" posture) + `AppendRecordMemoryAsync` (record-memory fragment via `MemoryItemStore.ToRecordPromptFragmentAsync`, any entity type) + date suffix. So the BYTES for existing sections already match what the renderer would produce — cutover is a call-site swap, not a reshape.
3. `ContextBinder.BindAsync` runs per-turn on interactive (`ChatEndpoints.cs:642-675`) but its envelope is discarded (fingerprint only). The renderer does NOT render the Memory slice (references-only there by design, NFR-07) — record-memory prompt text flows only through the legacy provider append.
4. User-scope memory: `GetForUserAsync` consumed ONLY by governance endpoints. **No recall path** (F-2).
5. `EnvelopeBudget.Evaluate` is called with `conversationTokens=0, recordMemoryTokens=0` (`ContextBinder.cs:504`) — volatile tail unmeasured live (F-7).

## Design decisions (binding)

### D1 — Convergence = the interactive prompt CONSUMES the envelope via the renderer; legacy direct-append call sites retire
The system-prompt sections currently assembled from producers/direct appends (host-identity/Business, User, date suffix, record-memory fragment) are produced ONCE in `ContextBinder.AssembleEnvelopeAsync` and rendered via `ContextEnvelopeRenderer`. The provider/factory stop calling producers directly for those sections. One envelope per turn, consumed — the ADR-043/FR-B-04 posture becomes literally true for interactive.

- The bind must therefore happen BEFORE/inside agent construction for the turn. Trace the real sequence (`ChatEndpoints` message handler → `SprkChatAgentFactory` → `PlaybookChatContextProvider.GetContextAsync`) and choose the minimal stitch: preferred shape is the provider (or factory) resolving `IContextBinder` and consuming `BoundInputs` for the sections it currently self-assembles; the per-turn bind in `ChatEndpoints` then reuses/records the same envelope rather than a second bind (ONE bind per turn — do not double-bind). If the existing `ChatEndpoints` bind site is the wrong place after the stitch, move/merge it — fingerprint write stays exactly-once per turn.

### D2 — Byte-parity for existing sections; additive-only for new ones (NO eval re-baseline)
- Existing sections (Business/host-identity, date suffix, conversation ledger message, record-memory fragment): rendered output MUST be byte-identical to today. Falsifier: `BusinessSliceDeterminismContractTests` stays green UNEDITED; add renderer-vs-legacy parity pins for the other sections (render both ways in test, assert equality) BEFORE deleting the legacy call sites.
- NEW section (user-memory fragment): additive, rendered ONLY when the user has memory items. Eval fixtures/golden utterances have no user memory → their prompts stay byte-identical → the 83-case eval gate must stay green WITHOUT re-baselining. If any eval case's prompt changes bytes, that is a defect in the implementation, not a re-baseline event.

### D3 — F-2 user-memory recall shape (mirrors record scope)
- `MemoryItemStore` gains `ToUserPromptFragmentAsync(systemUserId, ...)` mirroring `ToRecordPromptFragmentAsync` (same provenance-aware formatting discipline, deterministic ordering, heading clearly scoped, e.g. "## What I remember about you" style consistent with the record fragment's heading conventions — REUSE the record fragment's formatting helper, do not fork it).
- Envelope `User` slice carries the fragment (alongside 055's CallerContactId); renderer's `RenderStablePrefixAdditions` already renders `User.Fragment` — so F-2 recall ships through the D1 cutover automatically.
- Budget: User slice budget is 300 tokens (054). The fragment builder enforces item-count/size trimming consistent with how the record fragment respects RecordMemory 600 (inspect and mirror).
- Mirror-guard (`DataverseFieldMirrorGuard`) applies to user items exactly as record items at read.
- Placement: user memory is per-user stable within a session (memory writes during a session MAY update it next turn — same semantics as record fragment today). It lives in the stable-prefix additions (User before Business, per the renderer's canonical order already shipped).

### D4 — F-7 live budget measurement
At bind/render time the fragments and the conversation system message are now in hand: pass real token counts (use the SAME token-estimation helper the budget/eval tests use — grep `EnvelopeBudget`/`ContextBudgetBreachEvalTests` for the estimator; do not introduce a new tokenizer) into `EnvelopeBudget.Evaluate(envelope, conversationTokens, recordMemoryTokens, ...)`. Breach stays warn-never-500 (unchanged). `ContextBudgetReport` stays counts-only (NFR-07).

### D5 — Eval + test additions (closed set)
1. Renderer-vs-legacy byte-parity pins for every migrated section (then legacy call sites deleted; pins retarget to renderer-only determinism).
2. `BusinessSliceDeterminismContractTests` green UNEDITED (falsifier).
3. User-memory capture→recall eval: extend `MemoryWriteCaptureRecallEvalTests` with a user-scope case (memory.write scope=user → next-session prompt contains the user fragment via the REAL bind+render path) carrying the `GoldenUtteranceEval` trait (joins the CI gate).
4. Negative: user with zero memory items → prompt bytes identical to pre-change (pin).
5. F-7: budget evaluation test proving real non-zero conversation/memory token counts flow on a live-shaped bind (and that the ~8k conversation worst case now logs a live warn — assert via logger capture, not a 500).
6. Full suite + local eval gate green; publish-size measured per §10 (report line in task notes).

### D6 — PE-D8(b) IS IN SCOPE (operator ruling 2026-07-10): render the envelope into DISPATCH prompts
The operator pulled #619(b) into this task. `ActionRunner` (and the dispatch executor path via `SessionDispatchOrchestrator`) composes the envelope's rendered context into the dispatch prompt — per 053's own remark this is "a one-line composition change" now that 054 budgets are binding:
- Render `RenderStablePrefixAdditions` (User + Business fragments — now incl. user memory per D3) + the record-memory fragment into the dispatched capability's prompt, in a deterministic position consistent with the interactive layout (stable additions before the operand `## Input` section; date suffix per the interactive suffix convention if dispatch prompts carry a date at all — match what exists, don't invent).
- The envelope is ALREADY bound on the dispatch path (`SessionDispatchOrchestrator.cs:389-409`) — consume the existing `BoundInputs`, do NOT add a second bind.
- **Eval handling**: golden-utterance/dispatch evals assert ROUTING/OUTCOME behavior, not prompt bytes — they must stay green as-is. The interactive byte-pins (D2) are scoped to interactive and unaffected. Add a dispatch seam test (tests/integration/seam/**) pinning: (a) a dispatch with host-record memory renders the memory fragment into the executor prompt; (b) a dispatch with NO memory/host renders a prompt byte-identical to pre-change (regression pin). If any existing eval case DOES change outcome due to the added context, STOP and report — that is a behavioral change requiring operator eyes, not a silent re-baseline.
- Update the #619 GitHub issue at consolidation: (b) delivered by this task; (a) remains open (see below).

### Explicitly OUT (unchanged deferrals)
- Schema-card prose consolidation into the Business slice — PE-D8/#619(a) (catalog JSON migration + eval re-baseline; stays deferred).
- Semantic slice conditional-trigger wiring — PE-D6/#617.

## Conflict window
Files this task owns: `ContextBinder.cs`, `ContextEnvelopeRenderer.cs`, `ContextSliceProducers.cs`, `PlaybookChatContextProvider.cs`, `SprkChatAgentFactory.cs` (prompt-assembly region), `ChatEndpoints.cs` (bind site), `MemoryItemStore.cs` (user fragment), their tests. The concurrently-running F-8 agent owns `SideEffectGateAIFunction.cs` (no overlap); F-3 agent owns `OutputRouter`/`CompletionEngine` (no overlap); F-11 agent touches `SprkChatAgentFactory.cs:662-673` COMMENT ONLY — coordinate: rebase over its comment edit if both touch the factory.
