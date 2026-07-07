# Task 040 — Dataverse changes (FR-P3-01 Remaining consumers as Bindings)

> **Date**: 2026-07-06 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **Idempotent**: yes (three creates; re-runnable by GUID) · **Seed mirror**: `infra/dataverse/sprk_playbookconsumer-insights-rows.json`

## 1. Pre-existing rows verified (no changes needed)

Full-table `read_query` on `sprk_playbookconsumer` (2026-07-06) confirmed every non-Insights consumer in FR-P3-01 scope ALREADY has an enabled Binding row on spaarkedev1 — the config keys being deleted were pure fallback:

| consumerType | row id | sprk_playbook | sprk_action |
|---|---|---|---|
| `document-profile` | `a2bd24e7-8276-f111-ab0e-7ced8ddc4a05` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` (legacy Document Profile playbook — the value clients still submit to `/api/ai/analysis/execute`; the Binding-row compare replaces the `LinearConsumers:PlaybookIds` reverse-lookup) | `bb356968-ebe9-f011-8406-7ced8d1dc988` |
| `email-analysis` | `8b1194cd-3670-f111-ab0e-70a8a590c51c` | `bc71facf-6af1-f011-8406-7ced8d1dc988` (== the deleted hardcoded fallback GUID) | — |
| `matter-pre-fill` | `e5f37faa-2c70-f111-ab0e-7ced8ddc4cc6` | `2d660cad-d418-f111-8343-7ced8d1dc988` | `89cc641a-df18-f111-8343-7c1e520aa4df` |
| `project-pre-fill` | `ab7ac1c5-2c70-f111-ab0e-7ced8ddc4cc6` | `fc343e9c-3460-f111-ab0b-7c1e521b425f` | `1e838114-7919-f111-8343-7ced8d1dc988` |
| `summarize-file` | `271194cd-3670-f111-ab0e-70a8a590c51c` | `4a72f99c-a119-f111-8343-7ced8d1dc988` (== the deleted hardcoded default) | `ddaa441e-9f19-f111-8343-7c1e520aa4df` |
| `ai-summary` | `121194cd-3670-f111-ab0e-70a8a590c51c` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` | — |

## 2. `sprk_playbookconsumer` — Insights rows (CREATED)

The `Insights:Playbooks:Map` block + `Insights:Playbooks:DefaultName` key are replaced by rows of consumerType `insights-ask` whose `sprk_consumercode` carries the canonical playbook name and whose `sprk_playbook` carries the per-env GUID. Readers use `ResolveBindingAsync(ConsumerTypes.InsightsAsk, consumerCode: name)` with **exact consumer-code matching** (the resolution algorithm's default-row fallback is rejected by callers so unknown names still fail clean; the `default` row itself serves the Assistant path when the classifier supplies no hint).

| Row | id | Key columns |
|---|---|---|
| Insights Ask — `default` | **`f32a7931-8079-f111-ab0e-7ced8ddc4cc6`** | consumerType `insights-ask` / code `default` / env `*` / prio 500 / enabled / playbook → `a0d49d0d-4a65-f111-ab0c-70a8a590c51c` (matter-health-single) / ucid `UC-C-2` / disposition Informational / risk None / capture Loop |
| Insights Ask — `matter-health-single` | **`f82a7931-8079-f111-ab0e-7ced8ddc4cc6`** | same, code `matter-health-single` |
| Insights Search — `default` | **`f89fa738-8079-f111-ab0e-7ced8ddc4cc6`** | consumerType `insights-search` / code `default` / env `*` / enabled / **NO playbook/action target** / ucid `UC-C-2` |

Notes:
- Neither insights row carries `sprk_tooldescription` — deliberately NOT loop-projected yet (loop-side insights capabilities are a later catalog addition; same posture as the task-043 daily-briefing note). GU-021/022/023 flipped `planned → existing` on the catalog-inventory axis (ConsumerTypes.All membership), not on loop projection.
- `insights-search` is a catalog registration only: `POST /api/insights/search` wraps `IRagService` directly (no engine target); the row exists for the FR-P0-04 constants ↔ rows boot-reconciliation parity of `ConsumerTypes.InsightsSearch`. Target-less rows are skipped by resolution (documented "admin error" skip) — nothing resolves it today.
- `universal-ingest@v1` has NO row — **explicit decision (2026-07-06 integration pass)**: `read_query` on `sprk_analysisplaybook` (`LIKE '%ingest%'` and `LIKE '%universal%'`) returned ZERO rows — the playbook itself does not exist on spaarkedev1, so no Binding row can carry its `sprk_playbook` target; a target-less row is skipped by resolution (identical runtime behavior to no row) while polluting the catalog. The deleted config map never carried the entry either ("reserved for r2 catch-up"), so behavior is unchanged: `InsightsOrchestrator.RunIngestAsync` raises its honest unconfigured error via the exact-code miss. When `Deploy-Playbook.ps1` ships universal-ingest@v1, seed one `insights-ask` row with code `universal-ingest@v1` per the `_comment_ingest` instruction in the seed mirror.
- Boot reconciliation (FR-P0-04): `InsightsAsk` + `InsightsSearch` added to `ConsumerTypes.All` in the SAME change as the rows — parity holds in both directions.

## 3. E-2 adapter re-point (code, this task)

`EngineOutputLedgerAdapter` now reverse-resolves the REAL Binding row for the invoked playbook via the new `IConsumerRoutingService.GetBindingByPlaybookIdAsync(playbookId)` (5-min cached; also consumed by `TopicRegistryTtlLookup` replacing the config-map reverse scan). Registered playbooks (e.g. matter-health-single → insights-ask rows) key ledger entries `{bindingId}@t{n}` with the row's ucid/disposition; playbooks with no row keep the task-024 interim `{playbookId}@t{n}` / `engine-playbook` identity as the documented degrade path (ADR-040: unregistered composite outputs must still land in the ledger).

## 3b. Step 9.5 gate fixes (2026-07-06)

- **W-1 (real regression caught by code-review + live App Service verification)**: `az webapp config appsettings list` on `spaarke-bff-dev` showed `LinearConsumers__MaxOutputTokens__summarize_file = 4000` LIVE — the deleted per-consumer cap was load-bearing (SUM-CHAT@v1 emits ~2500 tokens; the null-cap fallback is the 500-token DocumentIntelligence default → mid-JSON truncation). Fix: `ActionRunner.MaxOutputTokensCeiling = 4000` — one deterministic executor ceiling for every prompted Action, no config surface (output length/cost stays bounded by each Action's constrained-decoding schema). The dead env var can be removed at the next hygiene pass.
- **W-2**: reverse lookup (`GetBindingByPlaybookIdAsync`) was ambiguous with two insights-ask rows targeting the same playbook — on equal priority the `default` row id sorts first, so `TopicRegistryTtlLookup` would recover canonical name "default" and silently miss the per-topic TTL. Fix: named row `f82a7931-…` `sprk_priority` **500 → 400** on spaarkedev1 (verified by re-read) + seed mirror rule: named canonical rows 400, `default` alias 500.
- **W-3**: dead `IConfiguration` dependency removed from `AssistantToolCallHandler` (its only read was the deleted default-playbook key).
- **S-1/S-2**: stale Singleton comment fixed in `AnalysisServicesModule`; `InsightEndpoints` exact-code compare now normalizes null/empty `ConsumerCode` → "default" (same rule as the handler).

## 4. App Service configuration follow-up (operator)

The deleted config keys may still EXIST as App Service Application Settings on spaarke-bff-dev (`Insights__Playbooks__Map__*`, `Workspace__*PlaybookId`, `LinearConsumers__*`). They are now dead (no reader) — harmless, but should be removed at next deploy hygiene pass. Guides referencing the old keys (`BUILD-A-NEW-INSIGHT-CARD.md`, `INSIGHTS-ENGINE-GUIDE.md`, `INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md`, `WORKSPACE-AI-PREFILL-GUIDE.md`, `SCOPE-CONFIGURATION-GUIDE.md`) carry doc drift — flagged for doc-drift-audit / task 044 wrap (historical project notes under `projects/**` are archives and stay).
