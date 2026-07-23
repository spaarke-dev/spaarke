# HANDOFF from core (redesign-r2) — deploy coordination, 2026-07-10 (evening)

> From the `spaarke-ai-architecture-redesign-r2` session, at operator direction ("if a deploy is
> required let's coordinate with compose-r2"). Reciprocates your
> `HANDOFF-to-core-shared-surface-heads-up.md` — your "master-as-channel, core-first order"
> recommendation is now satisfied: **core is 100% merged to master** as of 2026-07-10 ~16:30Z.

## What core landed on master since your last master re-merge (#620)

| PR | Content | Runtime impact on your surface |
|---|---|---|
| **#622** | Task 054 binding ContextEnvelope budgets (`PlaceholderBudgets` → **`EnvelopeBudget`**, `Evaluate` + `ContextBudgetReport` on every `BindAsync`) + task 056 `AggregateFreshnessPolicy` (ledger-render addition) | Rename in `PublicContracts/ContextEnvelope.cs` — if any compose code references `PlaceholderBudgets`, it must move to `EnvelopeBudget` (we found no compose references). Budget breach = warn-only in prod, eval-gated in CI. |
| **#623** | ADR-042 Memory Architecture & Governance (docs only) | none |
| **#624** | Task 074 audit-container re-key (**writes cut over to NEW `audit-partitioned` container** — already created on `spe-cosmos-dev-ai`; legacy container untouched) + task 075 **workspace-tab tool retirement** (`Get/Update/CloseWorkspaceTabHandler` + legacy send-artifact variants DELETED; the `Workspace` SSE+ack leg you use is byte-preserved; `IWorkspaceStateService` write path removed, READ path intact) + 073 hygiene (2 `Task.Delay` probes → `TimeProvider`, incl. `SessionDispatchOrchestrator` manifest probe — behavior identical) | If compose code calls `UpsertTabAsync`/`PinTabAsync`/`CloseTabAsync` or the 3 retired handlers, the next master merge breaks compile — we grep-verified zero consumers outside the retired cluster, but verify on your side after merging. |
| **#625** | Project notes (gate record + test-diet) | none |

## The coordinated deploy plan (proposed)

1. **You own the next deploy** (your branch is what's live; your UAT is in progress). When convenient:
   re-merge master into `work/spaarkeai-compose-r2`, run your consolidated gate (watch for the
   barrel-export class of merge defect you already documented), redeploy BFF + code page from your
   branch. The running system then carries core M3+hardening AND your unmerged compose work.
2. **Tell core (or the operator relays) immediately before/after that deploy** — core will then
   deactivate the 3 retired `sprk_analysistool` rows (ids in
   `projects/spaarke-ai-architecture-redesign-r2/notes/075-legacy-workspace-tools-verdict.md`).
   Coupling rows-off with the retired-handler deploy keeps `/healthz` catalog parity clean
   (deactivating earlier would flip Degraded + drop the tools from the live catalog mid-UAT).
3. Post-deploy: audit writes start landing in `audit-partitioned` (new, empty, created); the legacy
   `audit` container stays for historical reads — optional copy-forward procedure is in
   `infrastructure/cosmos/audit-container-policy.json` (account name corrected to `spe-cosmos-dev-ai`).

## Known shared-env facts

- `memory-items` + `audit-partitioned` Cosmos containers exist (additive; core created them).
- memory.write catalog row is seeded and live (`2172b721-…`).
- Pre-existing env defect **#621** (session-cleanup GET-after-DELETE 500) fails the
  Changed-Surface Integration Smoke on EVERY AI-surface PR — adjudicated per-PR by both projects
  until root-caused; don't burn time re-diagnosing it as yours.

— core session (redesign-r2), 2026-07-10
