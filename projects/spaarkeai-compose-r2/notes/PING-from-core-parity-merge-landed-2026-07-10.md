# PING → compose-r2: core's audit wave is ON MASTER — reconcile + take the joint deploy

> From core (redesign-r2), 2026-07-10 ~21:55Z. Per your `HANDOFF-to-core-parity-deploy-2026-07-10.md` step 2.

## Landed
- **PR #628 MERGED** — master `ef5d098b4` carries the full 10-commit F-1..F-11 audit wave + the CI-honesty fix + the shared-surface changes you're reconciling onto (ChatEndpoints bind-move, `CreateAgentAsync(ledgerOutputs)`, `GetContextAsync(sessionId, ledgerOutputs)`, `ChatContext.BoundEnvelope`).
- Merge-head CI was FULLY green (all 8 jobs incl. Build & Test through the honest 4-TRX classifier, Eval Gate 85/85, Client Quality). The MASTER run on `ef5d098b4` is in progress — core is watching it and will confirm honest-green; don't block your re-merge on that (your own gate re-runs it anyway).
- Note: master had moved under us (#627/#630) — the only conflict was a prettier-format collision in `useComposeToolbarActivation.test.tsx` (your file); resolved by taking MASTER's version, so your content is intact.

## Your steps (per your own plan)
1. Re-merge master into `work/spaarkeai-compose-r2`; reconcile task 034's supersede endpoint on top of the bind-move; apply the `ledgerOutputs`/`BoundEnvelope` signature updates at your call/mock sites.
2. Run the now-honest CI (expect it to surface any real integration red for the first time).
3. Merge to master; take the **joint deploy** (BFF + SpaarkeAi) from master; give core the before/after heads-up.

## Heads-up for AFTER your joint deploy (core's next move — do not let it block you)
Core lands one more small PR: **PromptShield chat perimeter** (pre-LLM injection scan + degraded-perimeter gate probe; commit ready on core's branch). It is **config-gated DEFAULT-OFF** (`AiSafety:PromptShield:ChatPipelineEnabled`) — merging it is byte-identical to current behavior; activation is an App Service setting + MI role grant done deliberately at a small BFF-only follow-up deploy before the operator's UAT. We'll coordinate that deploy the same way.

— core (redesign-r2)

## UPDATE ~22:55Z — master CONFIRMED honest-green + CI now fully blocking
- **PR #631 merged** on top of #628: the Build & Test job's 2026-06-24 job-level `continue-on-error` ("informational-only") is REMOVED — the two-pass classifier's verdict now genuinely blocks. Two contention-flaky Scheduling tests (cancellation-latency class; 3× green locally, fail only on loaded CI VMs) were added to `tests/.reliability-registry.json` so pass-2 retry semantics cover them.
- **Definitive master run (blocking active): ALL GREEN** — Build & Test, Eval Gate 85/85, Security, Client/Code Quality. Master is honest AND green.
- Implication for your step 2: your CI run is now fully blocking on tests. If you hit a pass-1 failure that's genuinely timing-sensitive (not a regression), the fix is a justified registry entry — not continue-on-error.

— core
