# Task 107 — RiskDetectorHandler Completion Notes

**Status**: ✅ Completed (2026-06-07; Wave 5 of Phase A — H-G2 LLM-assisted handler)
**Rigor**: FULL
**FR Binding**: FR-15

## What was built

`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RiskDetectorHandler.cs` (~712 LOC). LLM-assisted risk identification with **code-side deterministic severity scoring** per FR-15.

### Pipeline

```
input text
  → per-tenant cache check (ADR-014; risk-detector:{tenantId}:{sha256(...)})
  → LLM structured-output call (IOpenAiClient.GetStructuredCompletionRawAsync)
    [LLM emits risks with category + description + evidenceSpan + raw confidence]
  → code-side deterministic severity scoring:
      finalScore = categoryWeights[category] × llmConfidence
      severity = bucket(finalScore)
        low      < 0.25
        medium   < 0.50
        high     < 0.75
        critical ≥ 0.75
  → filter (category allow-list, severity allow-list, confidence threshold)
  → dedupe (category | description | start span)
  → sort (severity desc, span asc, category asc, description asc)
  → cache store + structured ToolResult
```

The LLM does NOT emit the final severity. Code-side scoring guarantees byte-identical severity output across runs given identical LLM response + identical configuration (FR-15 binding for legal-review reproducibility, verified by `Determinism_*` tests including 25× stress repetition).

## Severity-scoring algorithm (FR-15)

Two-step:

1. **Per-risk score**: `finalScore = clamp(categoryWeights[cat] × clamp(llmConfidence, 0, 1), 0, 1)`
2. **Bucket** the score into severity:
   - `[0.0, 0.25)` → low
   - `[0.25, 0.50)` → medium
   - `[0.50, 0.75)` → high
   - `[0.75, 1.0]` → critical

`BucketSeverity` is a pure static function (internal) — tested directly at all four boundaries (0.0, 0.249999, 0.25, 0.499999, 0.5, 0.749999, 0.75, 1.0).

### Default category weights (legal-domain priorities)

| Category | Weight |
|---|---|
| legal | 1.0 |
| data-privacy | 1.0 |
| compliance | 0.95 |
| financial | 0.9 |
| contract | 0.8 |
| operational | 0.7 |
| reputational | 0.7 |

All overridable per row via `sprk_configuration.categoryWeights`.

## Configurable surface (sprk_configuration JSON)

- `riskCategories` (array, optional) — subset filter from supported 7 categories
- `severityLevels` (array, optional) — allow-list of severity buckets (default all four)
- `categoryWeights` (object, optional) — per-category weight override
- `confidenceThreshold` (number, optional, default 0.6) — minimum LLM confidence
- `modelDeployment` (string, optional) — Azure OpenAI deployment override

All validated in `Validate(...)` and `ValidateChat(...)` with clear error messages.

## ADR compliance

| ADR | How satisfied |
|---|---|
| ADR-010 (DI minimalism) | Auto-discovered via `ToolFrameworkExtensions.AddToolHandlersFromAssembly`. ZERO manual DI line. |
| ADR-013 (AI architecture) | Lives in `Services/Ai/Handlers/`. No PublicContracts changes. CRUD code never references this handler directly. |
| ADR-014 (caching) | Per-tenant cache key `risk-detector:{tenantId}:{sha256(text+categories+severities+weights+threshold)}`. Verified by `ExecuteAsync_CacheKey_IsTenantScoped_NoCrossTenantHits`. |
| ADR-015 (data governance) | Telemetry emits handler name + outcome + count buckets + severity-distribution buckets + duration ONLY. Risk descriptions + evidence spans + input text NEVER logged. Verified by `Telemetry_DoesNotLogInputTextOrRiskDescriptions` (both playbook + chat paths). |
| ADR-016 (rate limits) | LLM call routes through `IOpenAiClient` which applies `ai-context` rate limit policy. No bypass. |
| ADR-029 (publish hygiene) | BFF size delta +0.029 MB compressed (DLL +99 KB). No new NuGet packages. |

## NFR-13 safety pipeline preserved

LLM call uses the same `IOpenAiClient.GetStructuredCompletionRawAsync` path as all other LLM-assisted handlers — flows through existing PromptShield + Groundedness + Citations + Privilege + Cross-matter middleware. No bypass.

## Tests

**35 tests, all passing in ~112 ms** (full suite of 264 handler tests also passes — no regressions).

| Category | Tests |
|---|---|
| 4-point contract | 4 (registered + discoverable + valid metadata + non-empty types) |
| SupportedInvocationContexts | 1 (Both — Playbook + Chat) |
| Playbook validate | 7 (missing text/tenant + unsupported categories/severities + out-of-range confidence + invalid weight) |
| Chat validate | 3 (valid text arg, missing text, malformed JSON) |
| Positive ExecuteAsync | 6 (mixed-category severity assignment, severity-descending order, confidence-threshold filter, riskCategories filter, severityLevels filter, categoryWeights override) |
| Dedupe | 1 |
| **FR-15 determinism (binding)** | 3 — (1) byte-identical output across 2 runs with fresh LLM each time; (2) bucket boundaries exact at 0.25/0.5/0.75; (3) **25× stress repetition** with byte-equality assertion |
| Error paths | 3 (malformed LLM JSON → ModelError, cancellation → Cancelled, LLM exception → InternalError) |
| Cache | 2 (hit on second identical call, per-tenant isolation) |
| Chat path | 1 (ExecuteChatAsync flow) |
| ADR-015 telemetry | 2 (playbook + chat both verify no input/risk-description leak) |
| Execution metadata | 2 (ModelCalls + ModelName, ConfigModelDeployment override) |

## Files

### New

| File | Purpose | LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RiskDetectorHandler.cs` | LLM-assisted risk identification + deterministic severity scoring | ~712 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/RiskDetectorHandlerTests.cs` | 35 unit tests | ~700 |
| `infra/dataverse/sprk_analysistool-risk-detector-row.json` | Seed row metadata (handler class, tool code, JSON schema, default config, available-in-contexts=Both) | 75 |

### Modified

| File | Change |
|---|---|
| `scripts/Seed-TypedHandlers.ps1` | Added `"RiskDetectorHandler"` entry to `$RowFiles` map |
| `projects/spaarke-ai-platform-unification-r6/tasks/107-riskdetector-handler.poml` | Status `not-started` → `completed` |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | Row 107 🔲 → ✅ |

## Dataverse seed row

- `sprk_name`: SYS-Risk Detector
- `sprk_toolcode`: `RISK-DET@1` (**10 chars exactly — fits the 10-char column max** per the caveat raised by tasks 101/104)
- `sprk_handlerclass`: `RiskDetectorHandler`
- `sprk_availableincontexts`: 100000002 (Both — Playbook + Chat)
- `sprk_jsonschema`: Constrains chat-tool args to `{text, riskCategories?, severityLevels?, confidenceThreshold?}` per FR-15 input spec
- `sprk_configuration`: Default category weights (legal=1.0, data-privacy=1.0, compliance=0.95, financial=0.9, contract=0.8, operational=0.7, reputational=0.7), all 7 categories enabled, all 4 severity buckets enabled, confidenceThreshold=0.6

## Build + size + vulnerabilities

- `dotnet build`: 0 errors, 16 pre-existing warnings (unchanged)
- `dotnet test --filter "FullyQualifiedName~RiskDetectorHandler"`: 35 pass, 0 fail
- `dotnet test --filter "FullyQualifiedName~Handlers"`: 264 pass, 0 fail (no regressions in sibling Wave 1/2 handler tests)
- BFF size delta:
  - Uncompressed publish folder: +117 KB
  - Compressed (tar.gz): **+30,584 bytes ≈ +0.029 MB**
  - Target: ≤+0.6 MB single-task; ≤60 MB compressed cumulative (current ~46.39 MB)
- Vuln check: no new HIGH-severity CVE (pre-existing `Microsoft.Kiota.Abstractions` HIGH was already present; not introduced by this task)

## Quality gates

- **code-review**: PASS (clean — 0 critical / 0 warnings / 0 AI smells)
- **adr-check**: PASS (12 compliant ADRs; 0 warnings; 0 violations)

## Caveats / follow-up

None. The `sprk_toolcode` 10-char column-length constraint was respected via `RISK-DET@1` (exactly 10 chars).

## Deployment

Not deployed to Spaarke Dev in this task — deployment is the responsibility of the seed-script invocation (`scripts/Seed-TypedHandlers.ps1 -OnlyHandler RiskDetectorHandler`) at the operator's discretion. The seed row JSON + script entry are in place; the script remains idempotent (UPSERT by `sprk_toolcode`).
