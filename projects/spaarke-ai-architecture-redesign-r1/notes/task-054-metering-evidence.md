# Task 054 — Per-Tenant Metering: Dev Evidence (FR-P4-05 / NFR-05)

> Captured 2026-07-08 against dev App Insights `spe-insights-dev-67e2xz` (appId `6a76b012-46d9-412f-b4ab-4905658a9559`) after deploying the branch to `spaarke-bff-dev` and exercising all three live entry paths (text turns, document_uploaded Event rule, chip-click dispatch) as user `c74ac1af-…` in tenant `a221a95e-…`.

## Traffic generated

1. **Text path** — chat session `7603bad0…`, 3 fully-drained turns (SSE `done` frames confirmed).
2. **Event path** — session `d8a2d646…`: uploaded `task054-sample-nda.txt` (202, documentId `2918bf8d…`), fired `POST /events/document-uploaded` → `event_classification` (docType=nda, confidence 0.97, ucid `UC-A-7`) + chips + done.
3. **Click path** — dispatched the emitted "Summarize this document" chip (`bindingId 651194cd…`) via `POST /sessions/{id}/dispatch` → `complete` chunk with summary.

## KQL pack outputs (scripts/kql/ai-metering/, run via `az monitor app-insights query`)

### tenant-usage-rollup.kql — THE acceptance query
```
tenantId                             | turns | toolCalls | tokensIn | tokensOut | capabilityInvocations
a221a95e-6abc-4434-aecc-e48338a1b2f2 | 3     | 1         | 40254    | 353       | 2
```

### user-drilldown.kql
```
tenantId      | userId                               | turns | toolCalls | tokensIn | tokensOut | capabilityInvocations
a221a95e-6abc | c74ac1af-ff3b-46fb-83e7-3063616e959c | 3     | 1         | 40254    | 353       | 2
```

### tool-budget-consumption.kql (ADR-016 per-turn cap observable)
```
tenantId      | cap | turns | avgSpent | maxSpent | cappedTurns | turnsWithDenials | deniedCalls
a221a95e-6abc | 8   | 3     | 0        | 0        | 0           | 0                | 0
```

### tokens-by-model.kql — ambient-scope attribution across entry paths
```
tenantId      | model       | entryPath | source   | tokensIn | tokensOut
a221a95e-6abc |             | text      | loop     | 38178    | 107
a221a95e-6abc | gpt-4o-mini | click     | executor | 1219     | 204
a221a95e-6abc | gpt-4o-mini | event     | executor | 857      | 42
```

### capability-usage.kql
```
tenantId      | entryPath | capability | outcome | invocations
a221a95e-6abc | event     | UC-A-7     | success | 1
a221a95e-6abc | click     | UC-A-1     | success | 1
```

### event-daily-budget.kql (NFR-09 consumed-vs-cap)
```
day                  | tenantId      | userId        | executionsToday | cap | pctOfCap
2026-07-08T00:00:00Z | a221a95e-6abc | c74ac1af-ff3b | 1               | 50  | 2
```

## What shipped

- **Counters** (meter `Sprk.Bff.Api.Ai`, `Telemetry/AiTelemetry.cs`): `ai.metering.turns` (with `tool_budget.spent/cap/denied`), `ai.metering.tool_calls`, `ai.metering.tokens` (input/output × loop/executor × entry path × model), `ai.metering.capability_invocations` (entry path × capability × outcome; `budget.cap` on the event path).
- **Ambient attribution**: `Telemetry/AiMeteringContext.cs` (AsyncLocal scope begun at ChatEndpoints [text], DispatchSessionEndpoint + SummarizeSessionEndpoint [click], EventRulesService [event], DailyBriefingCompositeService [coded]) — OpenAiClient records executor usage against the scope with zero signature changes.
- **Export-gap fix**: `Sprk.Bff.Api.EventRules` meter was never `AddMeter`'d — `eventpath.execution` / `eventpath.bound_denial` were silently dropped since task 022. Registered in `TelemetryModule` (NFR-09 "enforced AND telemetered" now actually true in App Insights).
- **§F.1 fix**: `AiTelemetry` moved from the `DocumentIntelligence:Enabled` gate to TRULY UNCONDITIONAL registration (pure Meter wrapper, zero AI deps — same pattern as `EventRulesTelemetry`).
- **KQL pack**: `scripts/kql/ai-metering/` — README (counter schema + runbook) + 6 documented queries.
- **Tests**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/AiMeteringTelemetryTests.cs` — 12 meter-boundary tests (instrument names, dimensions, ambient-scope fallback, NFR-07 closed dimension-key set, scope semantics).

## Coded path note

The `coded` entry path (DailyBriefingCompositeService) is wired and test-covered but was not exercised live (requires briefing Binding traffic); its counter rows will appear with the first briefing render/email in dev.

## NFR-07 compliance

All dimensions are identifiers/counts only: opaque AAD GUIDs (`tid`/`oid`), closed-catalog capability ids (ucid/consumer type), deterministic tool ids, bounded ints. No prompt text, document text, or outputs anywhere. `user.id` per-user dimension is a documented, NFR-05-required exception to the low-cardinality metrics discipline.
