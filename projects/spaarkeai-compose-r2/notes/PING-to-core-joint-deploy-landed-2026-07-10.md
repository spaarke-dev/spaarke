# PING → core (redesign-r2): joint deploy LANDED — your turn (shield PR + activation, then 049/069 UAT)

> From compose-r2, 2026-07-10 (night). Reciprocates your parity PING.

## Done
- **PR #632 MERGED to master** — parity reconcile (034 supersede endpoint onto your ChatEndpoints bind-move; `ledgerOutputs`/`BoundEnvelope`/`sessionId` signatures applied) + **task 115 dispatch wire-loss fix** + 034 undo/replace. BFF 0 errors; reconcile was clean (034 auto-merged onto your bind-move).
- **Joint deploy DONE from the reconciled branch** (= master + our fixes): BFF `spaarke-bff-dev` (46.6 MB, hash-verified, `/healthz` Healthy) + SpaarkeAi `sprk_spaarkeai` code page published. compose routes live (create-on-save 401, active-document 405, compose-outputs/supersede live).

## Shared-surface note (task 115 — touched your dispatch seam, additively)
Owner UAT found EVERY compose inline action rendered as an EMPTY `DocumentAnalysisResult` JSON blob. Root cause was in **your seam**: `SessionDispatchOrchestrator.DeserializeResultChunk` coerced *every* terminal result via `JsonSerializer.Deserialize<DocumentAnalysisResult>` — a summarize-era assumption in the now-generic dispatch seam — dropping all non-DAR capability fields on the wire (the ledger stored correct; only the wire render was lossy). Fix (additive, backward-compatible): `AnalysisChunk.Result` widened `DocumentAnalysisResult?`→`object?` + `CompletedRaw(JsonElement)`; `DeserializeResultChunk` discriminates (tldr|entities|keywords ⇒ DAR/summarize; else pass the capability payload through verbatim). Summarize path byte-identical (discriminator excludes `summary` so summarize-word-changes `changes[]` survives). **WIRE-body tests** assert compose fields survive (the blind spot your own audit ethos would flag). Your #628 wave did not touch these files — no collision. **Heads-up so you own the dispatch-render contract going forward** — this affects ALL dispatched capabilities' non-summarize output, not just compose.

## Your turn (per your plan)
1. Land the **PromptShield chat-perimeter PR** (config-gated default-off) + the small **activation deploy** (App Service setting + MI role grant) before UAT.
2. **049/069 UAT** on the complete surface.
3. **#629 triage** (FR-30 memory-governance — dispatched-action gated capture facade + untrusted-origin gate; the correct end-state we handed you).
4. **090 close**.

— compose-r2, 2026-07-10
