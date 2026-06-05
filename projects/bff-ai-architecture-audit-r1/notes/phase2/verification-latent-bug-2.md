# LATENT BUG #2 Empirical Verification — `IToolHandlerRegistry`

> **Author**: Sub-Agent M (verification)
> **Date**: 2026-06-04
> **Hypothesis source**: Sub-Agent L §4.6 (Cat 10 mini-audit)
> **Method**: code analysis at HEAD; no execution; no modification
> **Files inspected**: `AnalysisServicesModule.cs`, `ToolFrameworkExtensions.cs`, `HandlerEndpoints.cs`, `EndpointMappingExtensions.cs`, `StartupDiagnostics.cs`, `ToolHandlerRegistry.cs`

---

## §1 Verdict

**REFUTED** (Sub-Agent L's hypothesis is incorrect on Claim 4 — the load-bearing claim).

**Severity if confirmed (counterfactual)**: HIGH. **Actual severity**: N/A (no bug).

**One-sentence summary**: `MapHandlerEndpoints` is **NOT** mapped unconditionally — it sits **INSIDE** the same compound-AI gate (`DocumentIntelligence:Enabled && Analysis:Enabled`) that gates `AddToolFramework`, so the gates are **symmetric** by construction. There is no DI resolution failure path under compound-AI-OFF.

This is a different code structure than W4 §4.5 LATENT BUG #1 (`IInsightsAi` / `MapInsightsEndpoints`), where the endpoint mapping IS unconditional and the service registration IS compound-gated → asymmetric → bug. Here both are gated → symmetric → no bug.

---

## §2 Per-claim verification (5 claims)

### Claim 1: `IToolHandlerRegistry` registered ONLY in `AddToolFramework`

**Status**: ⚠️ PARTIALLY TRUE (registered in TWO places, both compound-gated; no Null-Object peer in `AddNullObjectsForCompoundOff`).

**Evidence — registration sites for `IToolHandlerRegistry`** (from `grep IToolHandlerRegistry src/server/api/Sprk.Bff.Api`):

1. **`ToolFrameworkExtensions.cs:38`** — inside `AddToolFramework(services, configuration)` (the "Enabled=true" branch path called from `AnalysisServicesModule.AddToolFramework`):
   ```csharp
   // Register the tool handler registry as scoped (matches handler lifetime)
   services.AddScoped<IToolHandlerRegistry, ToolHandlerRegistry>();
   ```
2. **`ToolFrameworkExtensions.cs:66`** — inside the overload `AddToolFramework(services, Action<ToolFrameworkOptions> configureOptions)`:
   ```csharp
   services.AddScoped<IToolHandlerRegistry, ToolHandlerRegistry>();
   ```
   (Not invoked from production code path; convenience overload for tests.)
3. **`AnalysisServicesModule.cs:587`** — inside `AddToolFramework(services, configuration)` private helper, in the **`ToolFramework:Enabled=false`** sub-branch:
   ```csharp
   else
   {
       services.Configure<ToolFrameworkOptions>(
           configuration.GetSection(ToolFrameworkOptions.SectionName));
       services.AddScoped<IToolHandlerRegistry, ToolHandlerRegistry>();  // <-- line 587
       Console.WriteLine("⚠ Tool framework disabled (ToolFramework:Enabled = false), but IToolHandlerRegistry registered for job handlers");
   }
   ```

**Critical observation**: Both registration sites (lines 38 and 587) live INSIDE `AnalysisServicesModule.AddToolFramework` (the private helper at line 575), which is ONLY called inside the compound-ON branch (line 53). So while there are two PHYSICAL registration sites, both are reachable ONLY under compound-AI-ON.

**Verdict**: Claim 1 holds in substance — under compound-AI-OFF, `IToolHandlerRegistry` is NOT registered. ✓ TRUE in effect.

---

### Claim 2: `AddToolFramework` called ONLY in compound-AI-ON branch

**Evidence — `AnalysisServicesModule.cs:43-53`**:
```csharp
var analysisEnabled = configuration.GetValue<bool>("Analysis:Enabled", true);
if (analysisEnabled && documentIntelligenceEnabled)
{
    AddAnalysisOrchestrationServices(services, configuration);
    AddPlaybookServices(services);
    AddBuilderServices(services);
    AddTestingServices(services, configuration);
    AddDeliveryServices(services);
    AddNodeExecutors(services);
    AddRagServices(services, configuration);
    AddToolFramework(services, configuration);   // <-- line 53
    ...
}
```

The call to `AddToolFramework(services, configuration)` at line 53 is gated by `if (analysisEnabled && documentIntelligenceEnabled)` — the compound-AI-ON branch. The two compound-OFF branches (lines 91-96, 97-102) do NOT call `AddToolFramework`.

**Verdict**: ✓ TRUE — `AddToolFramework` is called ONLY under compound-AI-ON.

---

### Claim 3: `AddNullObjectsForCompoundOff` does NOT register `IToolHandlerRegistry` peer

**Evidence — `AnalysisServicesModule.cs:211-271` (full `AddNullObjectsForCompoundOff` method)**:

The method registers these Null-Objects:
- L1: `IBriefingAi` → `NullBriefingAi` (line 214)
- L3: `IPlaybookOrchestrationService` → `NullPlaybookOrchestrationService` (line 217)
- B6: `IPlaybookService` → `NullPlaybookService` (line 220)
- B7: `IRagService` → `NullRagService` (line 223)
- Tier 1.5 round 4: `IVisualizationService` → `NullVisualizationService` (line 235)
- Tier 1.5 round 4: `IFileIndexingService` → `NullFileIndexingService` (line 241)
- B2: `SprkChatAgentFactory` → `NullSprkChatAgentFactory` (line 247-248)
- B3: `PendingPlanManager` → `NullPendingPlanManager` (line 254-255)
- E2: `IInsightsIntentClassifier` → `NullInsightsIntentClassifier` (line 269-270)

**No `IToolHandlerRegistry` Null-Object peer.** No `NullToolHandlerRegistry` type exists in the codebase.

**Verdict**: ✓ TRUE — no peer Null-Object registration in compound-OFF helper.

---

### Claim 4: `MapHandlerEndpoints` is mapped UNCONDITIONALLY ⚠️ **LOAD-BEARING**

**Status**: ❌ **FALSE** — this is the critical refutation.

**Evidence — `EndpointMappingExtensions.cs:119-131`** (the ONLY call site of `MapHandlerEndpoints`):
```csharp
if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
    app.Configuration.GetValue<bool>("Analysis:Enabled", true))
{
    app.MapAnalysisEndpoints();
    app.MapPlaybookEndpoints();
    app.MapPlaybookEmbeddingEndpoints();
    app.MapAiPlaybookBuilderEndpoints();
    app.MapScopeEndpoints();
    app.MapNodeEndpoints();
    app.MapPlaybookRunEndpoints();
    app.MapModelEndpoints();
    app.MapHandlerEndpoints();   // <-- line 130, INSIDE the compound-AI gate
}
```

Verified by `grep -n MapHandlerEndpoints`: the ONLY consumer of `MapHandlerEndpoints` is `EndpointMappingExtensions.cs:130`, which is INSIDE the compound-AI gate. There is no other mapping site.

**Sub-Agent L's specific claim** — quoted verbatim from §4.6:
> `HandlerEndpoints` map UNCONDITIONALLY (`MapHandlerEndpoints` at line 17) and inject `IToolHandlerRegistry` (lines 128 + 182)

Sub-Agent L appears to have looked at `HandlerEndpoints.cs:17` (the `MapHandlerEndpoints` METHOD DEFINITION) and inferred that "the endpoint maps unconditionally". But the method DEFINITION is not the same as the CALL SITE. The CALL SITE in `EndpointMappingExtensions.cs:130` is inside the compound-AI gate.

**Verdict**: ❌ FALSE — `MapHandlerEndpoints` is INVOKED ONLY under compound-AI-ON. Symmetric with the `AddToolFramework` registration gate.

---

### Claim 5: `HandlerEndpoints` handlers inject `IToolHandlerRegistry` directly

**Evidence — `HandlerEndpoints.cs:127-130`**:
```csharp
private static IResult GetHandlers(
    IToolHandlerRegistry registry,
    IMemoryCache cache,
    ILoggerFactory loggerFactory)
```

**Evidence — `HandlerEndpoints.cs:180-183`**:
```csharp
private static IResult GetHandler(
    string handlerId,
    IToolHandlerRegistry registry,
    ILoggerFactory loggerFactory)
```

**Verdict**: ✓ TRUE — handlers do inject `IToolHandlerRegistry` via param-inference. BUT this is irrelevant because Claim 4 is false: the endpoints are only mapped under compound-AI-ON, where `IToolHandlerRegistry` IS registered.

---

### §2.6 Summary table

| # | Claim | Verdict |
|---|---|---|
| 1 | `IToolHandlerRegistry` registered ONLY in `AddToolFramework` (i.e., only under compound-ON) | ✓ TRUE |
| 2 | `AddToolFramework` called ONLY in compound-AI-ON branch | ✓ TRUE |
| 3 | `AddNullObjectsForCompoundOff` does NOT register `IToolHandlerRegistry` peer | ✓ TRUE |
| 4 | `MapHandlerEndpoints` mapped UNCONDITIONALLY | ❌ **FALSE** (load-bearing) |
| 5 | `HandlerEndpoints` inject `IToolHandlerRegistry` directly via param-inference | ✓ TRUE |

Claims 1, 2, 3, 5 are individually correct; Claim 4 — the load-bearing claim that creates the bug — is false. **The hypothesis fails.**

---

## §3 Failure mode under compound-AI-OFF (counterfactual)

The hypothesis would have produced this sequence:

1. Operator sets `DocumentIntelligence:Enabled=false` (or `Analysis:Enabled=false`).
2. `AnalysisServicesModule.cs:23` short-circuits → registers `NullTextExtractor` instead of `OpenAiClient`.
3. `AnalysisServicesModule.cs:44 if (analysisEnabled && documentIntelligenceEnabled)` evaluates false → `AddToolFramework(services, configuration)` NOT called → `IToolHandlerRegistry` NOT registered.
4. **[FAILS at this step in practice]** `EndpointMappingExtensions.cs:130 app.MapHandlerEndpoints()` ALSO NOT called (since it's inside the same compound gate). No endpoints registered for `/api/ai/handlers` or `/api/ai/handlers/{handlerId}`.
5. Client GET `/api/ai/handlers` → routing miss → **404 Not Found** (returned by ASP.NET routing, not 500, not 503).

This is reasonable behavior under compound-OFF: the feature is genuinely unavailable; 404 conveys "no such endpoint exists." It is NOT the W4 §4.5 LATENT BUG #1 pattern.

**Comparison to LATENT BUG #1 (`IInsightsAi`)** — in that case, the endpoint mapping IS unconditional but the service registration IS gated → 500 instead of 503 on DI resolution failure. Here both are gated symmetrically → no DI resolution attempt → no 500 → just a 404.

### §3.1 What about other consumers of `IToolHandlerRegistry`?

`grep IToolHandlerRegistry` reveals other consumer sites:

- `Services/Ai/AppOnlyAnalysisService.cs:31` (field) + `:55` (ctor param) — consumed by `IAppOnlyAnalysisService` (registered at `AnalysisServicesModule.cs:302` inside compound-ON gate; symmetric).
- `Services/Ai/AnalysisOrchestrationService.cs:38` (field) + `:55` (ctor param) — consumed by `IAnalysisOrchestrationService` (registered at line 301 inside compound-ON gate; symmetric).
- `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:81/137` — resolves `IToolHandlerRegistry` from a fresh scope at execution time. `AiAnalysisNodeExecutor` is registered at line 409 inside compound-ON gate; symmetric.
- `Infrastructure/DI/StartupDiagnostics.cs:21` — uses **defensive `GetService<>`** (returns null vs. throws). Already handles the compound-OFF case: logs `IToolHandlerRegistry: NULL` instead of throwing.

Every other consumer is either symmetrically compound-gated OR defensively coded. **No latent failure mode surfaces.**

---

## §4 Recommended remediation

**Recommendation**: **NO CODE CHANGE REQUIRED.** The hypothesis is refuted; there is no bug.

For completeness, here is what each option WOULD look like if it were needed:

### Option A — Move `MapHandlerEndpoints` behind compound-AI gate

Already done. Existing code at `EndpointMappingExtensions.cs:119-131` already places `MapHandlerEndpoints()` inside the gate. **0 LOC change.**

### Option B — Register `NullToolHandlerRegistry` peer in `AddNullObjectsForCompoundOff`

If we wanted to mirror the LATENT BUG #1 fix prophylactically (i.e., to be robust against a future change that promotes `MapHandlerEndpoints` outside the gate):

- Files modified: `AnalysisServicesModule.cs` (+~3 LOC), new file `Services/Ai/NullToolHandlerRegistry.cs` (~30 LOC throwing `FeatureDisabledException` on every method per ADR-032 P3).
- Effort: ~30 min including a focused unit test that confirms `GetHandler` throws `FeatureDisabledException`.
- **Tradeoff**: Adds prophylactic complexity for a non-existent bug. Not justified under ADR-010 (DI minimalism). Reject unless and until evidence emerges that the gate symmetry is at risk.

### Cross-reference to LATENT BUG #1 (W4 §4.5)

W4 §4.5 LATENT BUG #1 (`IInsightsAi` → 500 not 503) recommended **Option A** — move the endpoint behind the gate (symmetric to `AddToolFramework` pattern here). Precedent strongly favors structural symmetry over Null-Object proliferation. Since the structural symmetry is ALREADY in place for `MapHandlerEndpoints`, no further action is needed.

### Sub-Agent M's preference

Neither A nor B is required. The current code is correct. If forced to choose one prophylactic measure (e.g., for a future "defensive in depth" pass): **Option B** is cheaper than A in the COUNTERFACTUAL case where the endpoint mapping moves outside the gate, but A is preferable structurally. Today, do nothing.

---

## §5 Bundle compatibility with PR #1 (LATENT BUG #1 fix)

**Cannot bundle — there is no fix to bundle.**

PR #1 (LATENT BUG #1: `IInsightsAi` 500→503) stands alone. Adding LATENT BUG #2 to the same PR would either:
- (a) Make no code change for LATENT BUG #2 (since it doesn't exist), turning the bundle into a documentation note that says "we verified LB#2 was already correctly structured." This is fine in a PR description but doesn't justify a separate PR slot.
- (b) Apply Option B prophylactically (+~33 LOC) without engineering justification.

**Recommendation**: Document the refutation in the PR #1 body or in `notes/phase2/wave-4-summary.md` (or a Wave 5 summary if one exists). No code delta. No effort delta.

---

## §6 Test plan

**No test required** for LATENT BUG #2 since the bug does not exist.

For LATENT BUG #1 (separate PR), the test that asserts `503 FeatureDisabled` (not `500 InternalServerError`) on a `GET /api/insights/...` under compound-AI-OFF is the analogous coverage that would, if the symmetric structure were ever broken in the `IToolHandlerRegistry` codepath, surface the regression. To future-proof structural symmetry:

- **Optional prophylactic test** (low value, do not block PR #1 on this): an integration test asserting `GET /api/ai/handlers` returns `404 Not Found` (not `500 InternalServerError`) under `DocumentIntelligence:Enabled=false`. Would catch a future regression where someone naively promotes `MapHandlerEndpoints` outside the gate.

Cost: ~10 LOC in an `EndpointMappingTests.cs` fixture; benefit: defensive coverage of a now-symmetric area. Decision: defer to project owner.

---

## §7 Confidence

**Confidence level: HIGH.**

Evidence basis:
- Direct verbatim quotes from `EndpointMappingExtensions.cs:119-131` showing `MapHandlerEndpoints()` inside the compound gate.
- Direct verbatim quotes from `AnalysisServicesModule.cs:44-53` showing `AddToolFramework(...)` inside the same compound gate.
- Exhaustive grep of `MapHandlerEndpoints` confirming a single call site.
- Exhaustive grep of `IToolHandlerRegistry` confirming consumer audit (5 consumers; all symmetric or defensive).
- Direct comparison to W4 §4.5 LATENT BUG #1 structural pattern showing the difference (asymmetric there vs symmetric here).

No counter-evidence found. The hypothesis is decisively refuted.

---

## §8 Note for Sub-Agent L (or future audit triage)

Sub-Agent L's §4.6 inference appears to have been driven by reading `HandlerEndpoints.cs:17` (the method DEFINITION line `public static IEndpointRouteBuilder MapHandlerEndpoints(this IEndpointRouteBuilder app)`) and interpreting "method exists at line 17" as "method is called unconditionally at line 17." This is a category error — the method DEFINITION says nothing about WHERE/WHEN it is invoked. The invocation site is `EndpointMappingExtensions.cs:130`, which is inside the compound gate.

**Methodology note for future audits**: when claiming an endpoint is mapped "unconditionally", always cite the CALL SITE (the `app.MapXxxEndpoints()` invocation in `EndpointMappingExtensions.cs` or `Program.cs`), not the method-definition file. The W4 §4.5 LATENT BUG #1 verification correctly identified this by examining the call site of `MapInsightsEndpoints` and finding it outside the gate; the same discipline applied here would have prevented the false-positive hypothesis.
