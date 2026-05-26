# Phase 3 Baseline — Task 033: App Insights 48h Calendar Gate START

> **This file marks the START of the 48-hour observation window for FR-?
> (App Insights baseline). The COMPLETION file (`app-insights-48h.json`)
> is produced at T+48h.**

## Start

- **Start timestamp**: 2026-05-25 (UTC)
- **App Service**: `spaarke-bff-dev` (Linux, rg-spaarke-dev)
- **App Insights resource**: (to be confirmed via `az` query — see below)
- **Earliest completion**: 2026-05-27 (UTC) — at which point task 033 can
  pull the 48h window and emit `app-insights-48h.json`.

## Window-choice justification (per task 033 step 1)

Per Phase 0 task 005 (UQ-04 resolution):

> "Insights Engine project (`work/ai-spaarke-insights-engine-r1`) verified
> pre-implementation (0 commits ahead of master). Baseline window decision:
> capture Phase 3 baseline NOW (pre-integration) — Engine has not started
> integration so no contamination risk."

The dev environment is also quieter post-Linux-migration (task 019):
old `spe-api-dev-67e2xz` decommissioned 2026-05-25; all traffic now hits
`spaarke-bff-dev`. The 48h window starts from this point of single-target
quiet.

## Metrics to capture at T+48h

Per task 033 POML:

1. **Request count per endpoint**
2. **Error rate per endpoint**
3. **P50 / P95 / P99 latency per endpoint**
4. **Exception counts by type**
5. **Dependency call latency** (Graph, Dataverse, Service Bus, Cosmos,
   Redis) + success rates

## Operator action at T+48h

```powershell
# 1) Identify the App Insights resource attached to spaarke-bff-dev
az monitor app-insights component show \
  --subscription 484bc857-3802-427f-9ea5-ca47b43db0f0 \
  --resource-group rg-spaarke-dev \
  --query "[?contains(name, 'spaarke') || contains(name, 'bff')]" -o table

# 2) Pull the 48h metrics (replace <ai-name> + <rg>)
$start = (Get-Date).AddHours(-48).ToString("o")
$end   = (Get-Date).ToString("o")
az monitor app-insights query \
  --app <ai-name> --resource-group <rg> \
  --analytics-query "requests | where timestamp between (datetime('$start') .. datetime('$end')) | summarize count(), avg_duration=avg(duration), p50=percentile(duration,50), p95=percentile(duration,95), p99=percentile(duration,99) by name, success | order by count_ desc" \
  > projects/sdap-bff-api-remediation-fix/baseline/app-insights-48h-requests.json

az monitor app-insights query \
  --app <ai-name> --resource-group <rg> \
  --analytics-query "exceptions | where timestamp between (datetime('$start') .. datetime('$end')) | summarize count() by type, problemId | order by count_ desc" \
  > projects/sdap-bff-api-remediation-fix/baseline/app-insights-48h-exceptions.json

az monitor app-insights query \
  --app <ai-name> --resource-group <rg> \
  --analytics-query "dependencies | where timestamp between (datetime('$start') .. datetime('$end')) | summarize count(), avg_duration=avg(duration), success_rate=avg(toint(success)) by type, name | order by count_ desc" \
  > projects/sdap-bff-api-remediation-fix/baseline/app-insights-48h-dependencies.json
```

Combine outputs into single `app-insights-48h.json` per task 033 acceptance.

## Phase 4 gate

Per design.md §6 Phase 3, task 037 (BASELINE.md commit + Phase 4 gate)
requires THIS file's completion (`app-insights-48h.json` with full
metrics). Phase 4 work cannot start until the 48h window closes and
the metrics file is committed.

**BASELINE.md issued at Phase 3 close (task 037) will mark this 48h
window as IN-PROGRESS; Phase 4 task 040 (first Outcome A SAFE candidate)
is blocked behind the window close.**
