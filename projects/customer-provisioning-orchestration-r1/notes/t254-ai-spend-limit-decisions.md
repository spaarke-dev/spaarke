# T254 — optional per-customer OpenAI spend limit: decisions (2026-10-06)

Owner G37 (2026-10-06): "no cap but allow for a per customer spend limit if desired."

## What was found

1. **The gate was not wired.** Task 077 (2026-08-17, `111773ffc9`) put the budget check and the spend accrual inside
   `OpenAiClient`. The merge `28c2c1b385` ("Merge origin/master into work/customer-provisioning-orchestration-r1")
   took master's new `OpenAiClient` constructor (`managedIdentityCredential`) and silently dropped 077's two
   parameters, its eight pre-call checks and its accrual. Since then nothing calls `ITenantBudgetPolicy.EnsureUnderBudget`
   or `ITenantTokenLedger.AddSpend` outside tests. No test caught it: 077's tests exercised the policy, not the wiring.
2. **The chat loop was never covered.** The main user path (the chat agent) calls the `IChatClient` pipeline, not
   `OpenAiClient`; 077 gated only `OpenAiClient`.
3. **Per-`tid` keying cannot work on a dedicated stamp.** The map was `TenantBudget:Tenants:{tenantId}`; the POML said
   "for the run's tenant". On Model 1 the run's `tenantId` is Spaarke's tenant, while users sign in from the customer's
   workforce tenant(s) (INCOMING-141: `WorkforceIdentity__CustomerTenantIds__N`, refused if equal to `AzureAd__TenantId`)
   and External Access users from the CIAM tenant. Background jobs carry no ambient tenant at all. The limit would
   never match a caller.
4. **The ledger was per process.** In-memory: it resets on every restart, deploy and slot swap, and each instance
   counts separately — an N-instance stamp could spend N× the limit.
5. **Only 3 of 10 `OpenAiClient` methods recorded token usage** (non-streaming tools / structured); streaming and vision
   completions were neither metered (observability) nor counted.
6. **The generated `Configure-AppServiceSettings.generated.ps1` cannot take an optional value**: every per-env source is
   a mandatory parameter and every entry is always written, while H4b passes nothing for a skipped `required: false`
   entry. No optional entry existed before this task.

## Decisions

- **D1 — one limit per stamp, not per `tid`.** Under D-12 a stamp is one customer, so the whole BFF's OpenAI spend is
  that customer's spend. Setting `AiSpendLimit__MonthlyLimitUsd` (absent / ≤ 0 = no limit — the default). The
  `TenantBudget` per-tenant map, `TenancyMode` and the master `Enabled` switch are removed (they existed for the retired
  shared tier; "needed → build, else remove"). Model 2 stamps may set it too (it is optional everywhere).
- **D2 — the ledger is Redis in every deployed environment** (ADR-009; 077's own planned successor): integer micro-USD
  under `{InstanceName}ai-spend:month:{yyyy-MM}` (`INCRBY`, expiry on first write), so instances, restarts and both
  slots share one month-to-date figure. In-memory only where Redis is off (Development / Testing). Stamp-wide by
  design → a `SystemCacheKeys` entry (16 of 20).
- **D3 — enforce at both AI client seams**: `OpenAiClient` (every public method) and a `DelegatingChatClient` placed
  inside `UseFunctionInvocation` (so each model round-trip of the chat loop is checked and counted).
- **D4 — fail open on the store, never on the limit.** A Redis failure lets the call through (logged); a known
  over-limit month refuses. Same as 077 §11.
- **D5 — responses**: `AiSpendLimitExceededException` → global handler 429 ProblemDetails, `Retry-After` = seconds to the
  next UTC month start, code `ai_spend_limit_exceeded`. Chat `SendMessage` checks before writing SSE headers (a real 429)
  and maps a mid-turn crossing to an SSE `error` with the same code.
- **D6 — estimate**: USD = input tokens × `InputUsdPer1MTokens` + output × `OutputUsdPer1MTokens` (defaults 2.50 / 10.00,
  configurable). Embeddings are checked but not counted (their price is ~1–5% of a chat token). App Insights
  `ai.metering.tokens` stays the authoritative record.
- **D7 — intake**: optional `openAiMonthlyLimitUsd` (positive decimal ≤ 1,000,000) → H4b writes
  `AiSpendLimit__MonthlyLimitUsd` on both slots only when present (`required: false`). Later change/removal:
  `scripts/Set-AiSpendLimit.ps1` (both slots, idempotent, `-Remove`). A re-run that carries the value re-applies it; a
  re-run without it leaves the setting alone (`appsettings set` merges).

## Known limits (K)

- K1 — endpoints that catch every exception render their own generic error instead of 429; background jobs fail their
  AI step (and retry per the job's policy) while over the limit.
- K2 — the estimate is list-price based, not the invoice; set the limit with headroom.
- K3 — a call already in flight when the limit is crossed completes; the next call is refused.
- K4 — the stamp Redis evicts with `AllKeysLRU` (`redis.bicep`): under memory pressure the month's key can be evicted
  and the figure restarts at 0. Changing the eviction policy affects every key — out of scope; App Insights stays
  authoritative.
- K5 — two paths are outside the limit: the keyless proof (one chat + one embedding call per provisioning run,
  deliberately — a capped stamp must still prove its identity) and Foundry Agent Service runs
  (`Services/Ai/Foundry/AgentServiceClient`, disabled by default — `AgentService:Enabled` is false on every stamp; add a
  check in its `GuardEnabled()` before enabling it on a capped stamp).
- K6 — background AI jobs (indexing, profiling) refuse while over the limit; Service Bus redelivers until the max
  delivery count, then dead-letters. Nothing replays them when the month resets or the limit is raised — re-run them
  (guide §3.2b).
- K7 — a provisioning re-run that carries `openAiMonthlyLimitUsd` re-applies the intake value, overriding a later
  `Set-AiSpendLimit.ps1` change (or re-adding a removed limit). Run upgrades without the value, or with the current one.
- Side effect (correct, not a double count): `ai.metering.tokens` `source=executor` now also records streaming and
  vision completions, which recorded nothing before — executor-token dashboards step up at the first deploy.

## Step 9.5 (2026-10-06)

ADR tensions recorded in `design.md` §17 (T254 bullet): Path A for `provisioning.md`'s "no manual single-setting app
setting writes in production" (`Set-AiSpendLimit.ps1` writes an operator-owned runtime policy value, not deploy
configuration — the owner asked for exactly this change path) and for ADR-010's `ValidateOnStart` (D4: a bad value must
not stop the BFF). Owner approval requested for the 17th `SystemCacheKeys` entry (`AiSpendMonth`).
