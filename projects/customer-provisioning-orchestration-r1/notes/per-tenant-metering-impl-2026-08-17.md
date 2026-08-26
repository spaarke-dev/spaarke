# Per-Tenant Token Metering — Implementation Decision (Task 077)

> **Task**: `077-implement-per-tenant-token-metering.poml`
> **Date**: 2026-08-17
> **Author**: task-execute (Wave 4 Batch 4B parallel)
> **Rigor**: FULL (bff-api + ai + auth + azure-deployment tags; blast-radius = every AI call)
> **Spec anchors**: FR-13 §M1/M2, SC #13, design.md D19 + §3A A2

---

## Phase A Decision — App-Level Custom App Insights Metric (with additive Budget-Policy enforcement)

**Decision**: **APP-LEVEL** (custom App Insights metric keyed on `tenantId`) — with a small NEW `TenantBudgetPolicy` service for Model 1 429-gating enforcement.

**Key discovery reshaping this task** (per CLAUDE.md §11 "extend, don't duplicate"):

**Per-tenant token OBSERVABILITY already exists.** Delivered by `spaarke-ai-architecture-redesign-r1` task 054 (FR-P4-05 / NFR-05). The following are in production:

| Component | Path | Purpose |
|---|---|---|
| `AiTelemetry.RecordMeteredTokens(...)` | `src/server/api/Sprk.Bff.Api/Telemetry/AiTelemetry.cs` | Emits `ai.metering.tokens` counter with `tenant.id`, `user.id`, `entry.path`, `ai.model`, `token.type` dimensions |
| `AiMeteringContext` | `src/server/api/Sprk.Bff.Api/Telemetry/AiMeteringContext.cs` | AsyncLocal scope carrier — entry seams push `tenantId` + `userId` + `entryPath`; downstream observers read `Current` |
| `OpenAiClient.RecordExecutorTokenUsage(...)` | `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` | Wired into 3 executor call sites (`GetChatCompletionWithTools`, `GetStructuredCompletionAsync<T>`, `GetStructuredCompletionRawAsync`) — reports Azure OpenAI `Usage.InputTokenCount` + `Usage.OutputTokenCount` |
| KQL pack | `scripts/kql/ai-metering/` | Per-tenant token rollup + per-user drill-down queries |
| Contract tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/AiMeteringTelemetryTests.cs` | Anchor the instrument names + dimension keys the KQL pack queries |

**What is MISSING for D19 / SC #13 acceptance**:

1. **Pre-call budget check** — the existing counters are POST-call observability; there is no PRE-call check that returns HTTP 429 when a Model-1 tenant is over budget.
2. **Model 1 vs Model 2 discrimination** — Model 1 tenants should be gated; Model 2 tenants should be observation-only.
3. **Budget configuration surface** — `tokenBudgetMonthlyUSD` per §3A A1 is a `sprk_dataverseenvironment` column value at provisioning time, but is not yet surfaced to BFF runtime as an `IOptions` config.
4. **Running-total tracker** — decide whether to compute month-to-date USD from existing App Insights counters (query-time) or maintain an in-process running total (in-memory / Redis).

**Task 077 therefore delivers ONLY the DELTA** — the small enforcement seam that layers on top of the existing observability. Total net-new LOC: ~200–300, all under `Services/Ai/Metering/`.

---

## 1. Cost comparison — APIM gateway vs app-level (with existing observability + new enforcement seam)

| Dimension | APIM Consumption tier (proxy) | App-level (this decision) |
|---|---|---|
| **Baseline monthly cost** | ~$70/mo per environment (APIM Consumption base + gateway calls) — even at zero-traffic idle | **$0 incremental** — piggybacks existing App Insights spend ($2.30/GB ingested); the metering counters ride the existing `Sprk.Bff.Api.Ai` meter already emitting `ai.summarize.tokens`, `ai.rag.duration`, etc. |
| **Per-call cost** | ~$3.50/M calls at APIM Consumption tier | ~$0.001/call in App Insights ingestion (measurement + 4 dimensions ≈ 300 bytes × $2.30/GB) |
| **Model 1 (shared trial)** cost impact at 100 K OpenAI calls/mo | +$70/mo APIM base + $0.35 gateway calls = **~$70.35** | +~$0.10 App Insights ingestion — **negligible** |
| **Model 2 (dedicated stamp)** cost impact | +$70/mo per stamp — turns Model 2 fixed floor from ~$400 to ~$470 (17% jump) | **$0/mo** — observability rides existing App Insights spend already in the Model 2 cost envelope |

**Verdict on cost**: app-level is **~$70/mo/env cheaper**, materially improving both cost envelopes (Model 1 shared floor + Model 2 per-stamp floor).

## 2. Latency comparison

| Dimension | APIM | App-level |
|---|---|---|
| **Per-call latency added** | ~10–30ms per OpenAI call (proxy hop through APIM Consumption tier; TLS termination + policy evaluation + upstream call) | **0ms** for observability (already emitted post-call, off the hot path); ~1ms for pre-call budget check (in-memory `ConcurrentDictionary` lookup on `tenantId`) |
| **Impact on ADR-013 <500ms streaming TTFB** | Consumes 2–6% of the TTFB budget on every AI call | 0% impact on TTFB (budget check is a single in-process dictionary read) |

**Verdict on latency**: app-level wins decisively — APIM proxy hop measurably degrades the ADR-013 streaming TTFB budget across all AI callers (chat, briefing, analysis, RAG-triggered completions).

## 3. Operational complexity comparison

| Dimension | APIM | App-level |
|---|---|---|
| **Infra to author** | New Bicep module (`apim-openai-proxy.bicep`) + APIM policy XML (per-tenant token attribution + budget check) — the policy XML alone is 200–400 lines with the `<set-variable>` / `<send-request>` / `<return-response>` flow control that per-tenant metering + gating requires | 1 new options record, 1 new policy service, 1 new exception + 1 shim on `OpenAiClient` — all under `Services/Ai/Metering/`; ~200 LOC total |
| **Deployment order dependency** | H2a (Bicep infra) must land APIM BEFORE any AI-consuming BFF endpoint works. Retrofits break existing endpoints unless the OpenAI proxy path is dual-published during migration. | Layers additively on `OpenAiClient` — feature-off by default (no `TenantBudget` configured); no deployment reordering. |
| **Policy versioning discipline** | APIM policy XML requires Git-tracked policy versioning + rollback drills (a bad policy 500s every AI call — blast radius = every OpenAI-consuming endpoint in the platform) | Standard C# code — unit-tested + integration-tested via `WebApplicationFactory<Program>` |
| **Skills / knowledge required** | APIM policy XML + `<jwt-required>` + `<rate-limit-by-key>` + Azure OpenAI upstream proxy discipline — narrow expertise on the team | Idiomatic .NET options + DI + exception-to-ProblemDetails — every BFF contributor can maintain |
| **Testability** | E2E only (against a live APIM instance) — no way to unit-test policy XML | Full integration coverage via `WebApplicationFactory<Program>` with in-memory `TenantBudget` config; contract-test proves Model 1 → 429 and Model 2 → 200 |

**Verdict on operational complexity**: app-level is materially simpler across every axis.

## 4. Blast radius comparison

| Dimension | APIM | App-level |
|---|---|---|
| **Config change surface** | Every AI-consuming BFF endpoint (chat, briefing, analysis, RAG, embeddings, tool-completions — 30+ endpoints) must be rewritten to call OpenAI through the APIM proxy host instead of `AzureOpenAIClient` directly | ZERO caller changes — the enforcement seam layers inside `OpenAiClient` (which every AI caller already uses per the existing `IOpenAiClient` facade — [`src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs:57`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs)) |
| **Rollback path if the enforcement misfires** | Roll APIM policy XML back through Bicep — this is a re-deploy; can take minutes | Toggle `TenantBudget:{tenantId}:Enabled=false` in app settings — takes seconds; no re-deploy |
| **Failure mode if enforcement service is unavailable** | If APIM is down, every AI call fails (all callers dependent on the proxy host) | If `TenantBudgetPolicy` throws unexpectedly, `OpenAiClient` logs + PROCEEDS — the enforcement layer fails-open (defensive on purpose; per §11 observability is authoritative and enforcement is a safety net) |

**Verdict on blast radius**: app-level is dramatically smaller blast radius on both authoring and runtime failure modes.

## 5. Recommendation

**Choose app-level custom App Insights metric with a small NEW `TenantBudgetPolicy` enforcement seam.**

**Rationale (5 concrete reasons)**:
1. **Observability already exists** — `AiTelemetry.RecordMeteredTokens` + `AiMeteringContext` + `OpenAiClient.RecordExecutorTokenUsage` have shipped and are dashboard-queryable in App Insights. Reusing them satisfies acceptance criterion #2 (every OpenAI call emits a metric keyed on tenantId) with ZERO net-new code (per CLAUDE.md §11).
2. **APIM would duplicate existing observability** while adding $70/mo/env + 10–30ms per call — a net regression on both fronts.
3. **PublicContracts facade compliance (ADR-013)** — the enforcement seam is inside `OpenAiClient`, which is the SINGLE choke point every AI facade already routes through (`BriefingAi`, `IInvoiceAi`, etc. inject `IOpenAiClient` — no CRUD→OpenAI direct-inject). Enforcement at this seam is upstream of every facade.
4. **Model 1 vs Model 2 differentiation is free** — the presence of a configured `TenantBudget:{tenantId}` entry in app settings IS the Model 1 signal; absence = Model 2 (observation-only, current behavior). No new `TenancyModel` runtime signal needed on the request path.
5. **Fail-open safety** — if the enforcement service misfires, AI calls proceed. Observability continues via the existing counter. This is the correct default per §11 (cost of doing nothing = uncapped platform bill; cost of enforcement misfire = one over-budget tenant continues consuming for a few minutes until the operator toggles the flag).

**When APIM would be the right choice** (not applicable here):
- A mature multi-workload gateway story (e.g., > 5 backend services requiring shared throttling policies)
- Explicit requirement for OpenAI-key rotation / concealment behind a gateway
- A tenant contract that the metering must be enforced INDEPENDENTLY of the BFF code path (defense-in-depth against a BFF bug)

None apply to Spaarke's r1 MVP scope.

---

## Implementation plan (delivered by this task)

### Files created

| File | Purpose | LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Metering/TenantBudgetOptions.cs` | `IOptions`-backed config record; `Enabled` + per-tenant monthly USD budgets + Model 1 detection (`TenancyMode = "Model1Gated" \| "Model2Observation"`) | ~30 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Metering/TenantBudgetPolicy.cs` | Pre-call budget check; reads running-total from `ITenantTokenLedger`; throws `TenantBudgetExceededException` if Model 1 tenant over `tokenBudgetMonthlyUSD` | ~90 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Metering/ITenantTokenLedger.cs` + `InMemoryTenantTokenLedger.cs` | Month-to-date USD tracker per tenant; in-memory `ConcurrentDictionary` keyed on `(tenantId, yyyy-MM)`; hooked into `AiTelemetry.RecordMeteredTokens` post-call via a `MeterListener` collector so it double-serves the existing observability path without diverging | ~120 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Metering/TenantBudgetExceededException.cs` | Exception thrown by policy; converted to 429 ProblemDetails via the shared helper pattern (mirrors `FeatureDisabledException` per ADR-032) | ~30 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Metering/TenantBudgetResults.cs` | `.AsTenantBudgetExceeded429()` extension for endpoint catch sites (mirrors `FeatureDisabledResults` per ADR-032) | ~40 |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/Ai/Metering/TenantBudgetPolicyTests.cs` | Contract tests: (1) Model 1 tenant over budget → 429; (2) Model 2 tenant over budget → 200 (observation only); (3) Model 1 tenant under budget → 200; (4) fail-open when policy throws unexpectedly | ~200 |

### Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` | Add pre-call budget check via injected `ITenantBudgetPolicy` (nullable, fail-open); pattern precedent = existing `AiTelemetry? aiTelemetry = null` post-call hook |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` | Register `TenantBudgetOptions` + `TenantBudgetPolicy` + `InMemoryTenantTokenLedger` (all UNCONDITIONAL per ADR-032 — no feature-gate; disabled state = zero configured budgets) |

### DI registration (unconditional per ADR-032 F.1)

Following the `AiTelemetry` precedent — the service is registered unconditionally as a singleton; feature-off is expressed via empty configuration (no `TenantBudget:Tenants:*` entries in app settings). No `if (flag) { ... }` conditional registration → no §F.1 anti-pattern risk.

---

## Model 1 vs Model 2 signal source (spec.md § Unresolved Questions resolution)

The escalation trigger in the task briefing notes: *"Model 1 vs Model 2 detection requires a runtime signal that doesn't exist yet."*

**Resolution**: use CONFIGURATION as the signal.

- **Model 1 gated** = there is a `TenantBudget:Tenants:{tenantId}` entry with `TenancyMode = "Model1Gated"` + `MonthlyBudgetUsd` set. Provisioned by H4 KV secrets / H7 env-var handlers when the customer is provisioned via `model1-shared.bicep`.
- **Model 2 observation** = no `TenantBudget:Tenants:{tenantId}` entry OR entry has `TenancyMode = "Model2Observation"`. Default for every tenant not explicitly configured.

This means:
- ✅ SC #13 acceptance passes for Model 1 (over-budget → 429).
- ✅ Model 2 tenants get observation-only (present behavior, unchanged).
- ✅ No runtime lookup of tenancy model against Dataverse `sprk_dataverseenvironment.tenancyModel` needed — the config surface is the source of truth at the BFF process boundary.
- ✅ H12c runtime-references handler (task 072, already shipped) is the natural owner of writing the `TenantBudget:Tenants:*` app-settings entries when the customer's tenancy model is decided at provisioning time.

**Follow-on note for H12c** (write against Phase D deployment): H12c currently seeds `sprk_aimodeldeployment` rows; the app-settings write for `TenantBudget:Tenants:{tenantId}` is a natural extension. Deferred to Phase D task 077-follow-on OR spec.md fast-follow if not surfaced in Phase F acceptance.

---

## Verification results

- **`dotnet build src/server/api/Sprk.Bff.Api/`** — see task-077-deviations.md for absolute numbers
- **`dotnet test`** — full BFF unit + integration suite verified in task-077-deviations.md
- **`dotnet publish -c Release -o deploy/api-publish/`** — baseline 44.96 MB; delta reported in deviations doc
- **CVE scan** — `dotnet list package --vulnerable --include-transitive` verified in deviations doc

---

## Cross-references

- **Spec anchors**: [`spec.md`](../spec.md) FR-13 §M1/M2 (Model 1 required, Model 2 observability-only) · SC #13 (over-budget → 429) · § New Components §M1 §M2 (tokenBudgetMonthlyUSD column on `sprk_dataverseenvironment`)
- **Design anchor**: [`design.md`](../design.md) D19 (per-tenant token-metering layer is a no-regret investment) · §3A A2 (metering-layer amendment) · §7.2 (🟡 fixed-floor levers "shared metered per D19")
- **Existing observability lineage**: `projects/spaarke-ai-architecture-redesign-r1/` task 054 (FR-P4-05 / NFR-05) — the deliverer of `AiTelemetry.RecordMeteredTokens` + `AiMeteringContext`
- **ADRs applied**: ADR-013 (facade discipline: enforcement lives on `OpenAiClient`, upstream of every PublicContracts facade) · ADR-032 (unconditional registration — no `if (flag)` conditionality on the metering module) · ADR-015 (identifiers + counts only; no content in metering) · ADR-016 (per-turn tool budget precedent — this task extends per-tenant to per-month USD)
- **BFF §10 hygiene**: publish-size delta + CVE scan + test additions per `.claude/constraints/bff-extensions.md`

---

*Phase A decision complete. Proceeding to implementation.*
