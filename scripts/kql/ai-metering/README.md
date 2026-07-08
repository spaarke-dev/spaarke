# AI Per-Tenant Metering — KQL Query Pack

> **Source**: `spaarke-ai-architecture-redesign-r1` task 054 (FR-P4-05 / NFR-05)
> **Consumes**: App Insights `customMetrics` exported from the BFF meter `Sprk.Bff.Api.Ai` (+ `Sprk.Bff.Api.EventRules` for budget denials)
> **Scope**: counter + telemetry + KQL tier only. The admin metering endpoint and billing/pricing surface are named follow-ons (deferrals filed at task 090).

## Counter schema (emitted by `Sprk.Bff.Api/Telemetry/AiTelemetry.cs`)

All dimensions are identifiers/counts ONLY — never prompt text, document text, or outputs (NFR-07 / ADR-015). `tenant.id` and `user.id` are opaque AAD GUIDs (`tid` / `oid` claims; the coded briefing path uses the Dataverse `systemuserid`). Missing identity = dimension omitted (empty string in KQL), never a sentinel.

| Instrument | Unit | Emitted from | Dimensions |
|---|---|---|---|
| `ai.metering.turns` | {turn} | ChatEndpoints — once per completed agent-turn loop turn (FR-P2-01) | `tenant.id`, `user.id`, `tool_budget.spent`, `tool_budget.cap`, `tool_budget.denied` (ADR-016/NFR-09 consumed-vs-cap) |
| `ai.metering.tool_calls` | {call} | ChatEndpoints — per executed tool call in the turn's ToolChain ledger segments | `tenant.id`, `user.id`, `tool.id`, `outcome` (`executed`) |
| `ai.metering.tokens` | {token} | ChatEndpoints (loop streaming usage) + OpenAiClient (executor structured completions) | `tenant.id`, `user.id`, `token.type` (`input`/`output`), `source` (`loop`/`executor`), `entry.path`, `ai.model` |
| `ai.metering.capability_invocations` | {invocation} | SessionDispatchOrchestrator (click/text), EventRulesService (event), DailyBriefingCompositeService (coded) | `tenant.id`, `user.id`, `entry.path` (`text`/`click`/`event`/`coded`), `capability` (Binding ucid/consumer-type), `outcome` (`success`/`failed`), `budget.cap` (event path only) |
| `eventpath.execution` / `eventpath.bound_denial` | {execution}/{denial} | EventRulesService (meter `Sprk.Bff.Api.EventRules`; export registration fixed by task 054) | `event`, `ucid`, `outcome` / `event`, `reason` (no user dims by design — the per-user view lives on `ai.metering.capability_invocations`) |

Attribution plumbing: entry seams begin an `AiMeteringContext` (AsyncLocal) scope; token usage observed deep in `OpenAiClient` inherits tenant/user/entry-path from that scope.

## Running the pack

```bash
APP="spe-insights-dev-67e2xz"   # dev App Insights component (az monitor app-insights component list to rediscover)
az monitor app-insights query --app "$APP" --analytics-query "$(cat scripts/kql/ai-metering/<query>.kql)" --offset 24h
```

Or paste any `.kql` file into the App Insights **Logs** blade. Each file's header comment documents purpose + expected columns.

## Queries

| File | Purpose |
|---|---|
| `tenant-usage-rollup.kql` | THE acceptance query — one row per tenant: turns, tool calls, tokens (in/out), capability invocations |
| `user-drilldown.kql` | Per-user drill-down within a tenant (top consumers) |
| `tool-budget-consumption.kql` | ADR-016 per-turn tool budget: spent-vs-cap distribution, denied calls, per-tool volumes |
| `event-daily-budget.kql` | NFR-09 per-user daily Event-path budget: consumed-vs-cap per user per day + bound denials |
| `tokens-by-model.kql` | Token spend per tenant per model/deployment per entry path |
| `capability-usage.kql` | Capability invocations by entry path / capability / outcome (includes dispatch_refused context) |

## Notes

- `customMetrics` rows are pre-aggregated by the Azure Monitor OTel exporter per dimension-combination per export interval — always aggregate with `sum(valueSum)` (total) / `sum(valueCount)` (sample count), never `value`.
- Counters appear only after the first emission following deployment; an empty result for a new counter means "no traffic yet", not "broken export".
- Cardinality: `user.id` is per-user by explicit NFR-05 requirement (deliberate, documented exception to the low-cardinality metrics discipline; both ids are bounded by the tenant's user population).
