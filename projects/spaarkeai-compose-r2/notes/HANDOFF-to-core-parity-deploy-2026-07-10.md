# HANDOFF → core (redesign-r2): parity + clean joint deploy, 2026-07-10 (night)

> From compose-r2. Reciprocates your `HANDOFF-from-core-remediation-wave-2026-07-10.md`. Operator wants both
> projects at parity on master so both can UAT a clean joint deploy. Absorbed your handoff: #621 closed (stopping
> adjudication), F-3 OutcomeCard seam noted (Compose = natural first consumer, no obligation now), envelope
> grounding on dispatch (byte-identical for no-context), F-1/F-2 signature changes noted, CI now honest.

## The collision to manage
Both branches have unmerged work on **`ChatEndpoints.cs`**:
- **You**: moved the per-turn bind out into `PlaybookChatContextProvider` + signature changes (`SprkChatAgentFactory.CreateAgentAsync(ledgerOutputs)`, `IChatContextProvider.GetContextAsync(sessionId, ledgerOutputs)`, `ChatContext.BoundEnvelope`).
- **Us**: task 034 (undo/replace) added a **supersede endpoint** (ledger supersession crosses the wire for durability) touching `ChatEndpoints.cs` + `Models/Ai/Chat/SessionLedgerEntries.cs`.

## Proposed sequence — CORE-FIRST
1. **compose-r2**: finish + commit task 034 → clean branch. *(in progress)*
2. **redesign-r2 (you): merge your 10-commit F-1..F-11 audit wave to master FIRST.** You're ready + own the broader shared-surface changes (bind-move + signatures + the CI-honesty fix). Merging first means compose reconciles onto your published surface, not vice-versa.
3. **compose-r2 (us)**: re-merge master → reconcile 034's supersede endpoint on top of your bind-move + apply the `ledgerOutputs`/`BoundEnvelope` signature updates at our call/mock sites → run the now-honest CI (we expect to fix any real integration red your F-5 classifier fix newly surfaces) → merge to master.
4. **Joint deploy from master**: we run BFF (`Deploy-BffApi.ps1`, hash+health) + SpaarkeAi (`Deploy-SpaarkeAi.ps1`) from the merged master. Both projects then UAT the same live build. We'll give you the before/after deploy heads-up.

## Notes
- We're **holding** compose's remaining wrap-up (DEF-01/02, task 064 FR-32 trace hosting, 071 UI-ack, 014-split) until after the joint deploy so we don't disturb parity.
- FR-30 memory-governance is handed to you separately: **[#629](https://github.com/spaarke-dev/spaarke/issues/629)** + `notes/HANDOFF-to-core-fr30-memory-governance-2026-07-10.md` (dispatched-action gated capture facade + untrusted-origin gate). Not part of this deploy.
- DEF-03 (Compose webhook secrets) provisioned on `spaarke-bff-dev` (dev app settings) today — your BFF deploy from master will keep them (they're App Service config, not code).

Tell us (via operator) when your merge to master lands; we'll re-merge + reconcile + take the joint deploy.

— compose-r2, 2026-07-10 (night)
