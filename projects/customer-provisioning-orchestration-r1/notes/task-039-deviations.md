# task-039 — deviations & design notes

Task: **039-wire-app-insights-log-analytics-l2** (Wave C3 batch 2C tail, dispatched 2026-08-17)

## Deviations from POML

### D1 — SDK stack: `Azure.Monitor.OpenTelemetry.AspNetCore` in place of classic `Microsoft.ApplicationInsights.AspNetCore` (Path A per CLAUDE.md §6.5; inherits task 036 D-036-1)

- **What the POML implied (steps 1, 2, 3, 4)**: reference `Microsoft.ApplicationInsights.AspNetCore`, invoke `services.AddApplicationInsightsTelemetry(...)`, customise `AdaptiveSamplingTelemetryProcessor`, emit audit records via `TelemetryClient.TrackEvent("AuditableAction", properties)`.
- **What was implemented**: `Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0` (already referenced by task 036 in place of the classic SDK per D-036-1); `builder.Services.AddOpenTelemetry().UseAzureMonitor()`; no custom sampling policy (see D2); audit records emitted via structured `ILogger.LogInformation` with a stable `"AuditableAction:"` prefix.
- **Why (Path A rationale — inherits task 036 D-036-1)**:
  - `dotnet-10-upgrade-r1` task 014 (FR-06 telemetry consolidation) retired the classic `Microsoft.ApplicationInsights.AspNetCore` package from the BFF in favor of OpenTelemetry → Azure Monitor.
  - The BFF `AgentTelemetry.cs` comment (`src/server/api/Sprk.Bff.Api/Api/Agent/AgentTelemetry.cs`) explicitly documents that `TelemetryClient.TrackEvent(...)` calls have been inert no-ops since redis-cache-remediation-r1 R7-S7 removed `TelemetryClient` from DI, and that ILogger is the canonical emitter.
  - Emitting via structured ILogger with a `"AuditableAction:"` prefix keeps the compliance-query Kusto surface clean:
    ```kql
    traces
    | where message startswith "AuditableAction"
    | project timestamp,
              method    = tostring(customDimensions.Method),
              path      = tostring(customDimensions.Path),
              status    = toint   (customDimensions.StatusCode),
              actorTid  = tostring(customDimensions.ActorTid),
              actorOid  = tostring(customDimensions.ActorOid),
              roles     = tostring(customDimensions.ActorAppRoles),
              requestId = tostring(customDimensions.RequestId),
              traceId   = tostring(customDimensions.TraceId)
    ```
- **Impact on POML acceptance**: unchanged. All 7 acceptance criteria (dotnet build 0/0; POST emits with Actor.*; GET no emit; unauth POST 401 emits with empty actor; sampling 100%; no body reads; no Cosmos/SB wiring) satisfied — the SDK swap is a straight substitution at the pipeline layer.

### D2 — no custom sampling policy (POML step 3 satisfied by Azure Monitor exporter default)

- **What the POML said (step 3)**: "Configure sampling — `AdaptiveSamplingTelemetryProcessor` excludes custom event name `AuditableAction` so audit records are never dropped."
- **What was implemented**: no custom `TelemetryProcessor` or `Sampler` registration.
- **Why**: `Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0` defaults to **100% sampling** (opt-in only, via `AzureMonitorOptions.SamplingRatio`). Audit records satisfy NFR-11's "never sampled" requirement by construction. Introducing a custom `Sampler` to explicitly exclude `AuditableAction` would be defensive coding against a hypothetical future configuration change — a policy decision that belongs with an operational-hardening task if / when adaptive sampling is turned on.
- **Impact**: POML acceptance criterion #5 satisfied. If a future task enables adaptive sampling, the exclusion rule for `traces` where `message startswith "AuditableAction"` MUST be added to that task's scope (documented here as forward-looking guidance).

### D3 — `Modules/TelemetryModule.cs` naming (dispatcher directive named `ObservabilityModule.cs`)

- **What the dispatcher directive named**: `Modules/ObservabilityModule.cs` with `AddObservabilityModule` extension method.
- **What was implemented**: `Modules/TelemetryModule.cs` with `AddTelemetryModule` extension method.
- **Why**: The POML `<outputs>` element names `Modules/TelemetryModule.cs`. The BFF's equivalent module is also named `TelemetryModule` (`src/server/api/Sprk.Bff.Api/Infrastructure/DI/TelemetryModule.cs`). Consistency with (a) the POML `<outputs>` contract and (b) the BFF sibling module naming outweighs the dispatcher's "(or similar)" flexibility hint.
- **Impact**: none — same behavior; naming symmetric with existing modules (`AuthModule`, `SwaggerModule`, `CosmosModule`, `ServiceBusModule`, `TelemetryModule`).

## Bug found during Step 9.5 code-review (fixed pre-merge)

### F1 — StatusCode misreport on unhandled downstream exception

- **Discovered by**: Step 9.5 code-review agent.
- **Symptom**: When downstream throws an unhandled exception, Kestrel writes the actual 500 to the socket AFTER the entire middleware chain unwinds. At the moment `AuditLogMiddleware`'s `finally` block runs, `context.Response.StatusCode` is still the pipeline default (`200 OK`). The audit record would misreport a failed operation as a success — the compliance Kusto query `traces | where StatusCode >= 500` would miss unhandled-exception failures, defeating NFR-11's intent.
- **Fix**: wrap `_next(context)` in `try { ... } catch { ... } finally { ... }` — the `catch` assigns `StatusCode = 500` when the response hasn't started and status is still the default 200, then re-throws so upstream `UseExceptionHandler` / pipeline unwinding continues normally.
- **Preservation**: `catch` guards on `!Response.HasStarted && StatusCode == 200` — an upstream exception-handler middleware that already assigned e.g. `502 Bad Gateway` is preserved.
- **Regression coverage** (both tests unconditionally in CI unit suite):
  - `MutatingRequest_WhenDownstreamThrows_EmitsAuditRecord_With500Status` — asserts on the record's `StatusCode == 500` (previously only asserted the record was emitted, not the value).
  - `MutatingRequest_WhenDownstreamThrowsAfterSettingStatus_PreservesUpstreamStatus` — asserts upstream-set 502 is NOT overwritten.

## Design notes (non-deviation)

### Pipeline placement

`AuditLogMiddleware` sits AFTER `UseAuthentication()` (so `HttpContext.User` claims are populated before the audit read) and BEFORE `UseAuthorization()` (so 401 / 403 short-circuits from Authorization are captured — POML acceptance criterion #4 requires unauthenticated POST that fails at 401 to STILL emit with `Actor.Tid` empty). The middleware wraps the downstream pipeline via the classic `_next(context)` convention and inspects `Response.StatusCode` in the `finally` block after `_next()` returns.

### Claim extraction (dual-key)

`AuditLogMiddleware.ExtractTenantId` / `ExtractObjectId` read BOTH the WS-* long claim URI (`http://schemas.microsoft.com/identity/claims/tenantid`, `.../objectidentifier`) and the short JWT form (`tid`, `oid`). Microsoft.Identity.Web's default JwtBearer wiring emits the WS-* form for browser-flow tokens, but hand-crafted test JWTs (short-form only) and non-mapped Microsoft.Identity.Web v3+ flows (`MapInboundClaims=false`) emit the short form. Dual-key read is defensive: any future JWT-lib swap that changes the default form still yields correct extraction. `ExtractAppRoles` reads BOTH `ClaimTypes.Role` (mapped) and raw `roles` (unmapped) and deduplicates.

### DI shape

Program.cs total non-framework DI count is now **5 lines**:

```csharp
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddSwaggerModule();
builder.Services.AddCosmosModule(builder.Configuration);
builder.Services.AddServiceBusModule(builder.Configuration);
builder.Services.AddTelemetryModule(builder.Configuration, builder.Environment.EnvironmentName);
```

ADR-010 target is ≤15 non-framework lines. L2 is well within budget.

### `AzureMonitorGuard` mirror (L2 copy of BFF's guard)

`Infrastructure/Startup/AzureMonitorGuard.cs` re-implements the BFF's `AzureMonitorGuard` (from `spaarke-redis-cache-remediation-r2` FR-06 task 006) in the L2 namespace. Cannot reference the BFF assembly per ADR-010 (L2 is a peer service, not a BFF extension). Behavior identical:
- Deployed env + missing `APPLICATIONINSIGHTS_CONNECTION_STRING` → throws at startup (NFR-05 fail-fast; NFR-11 audit-trail is dead without the exporter).
- Development or Testing env + missing conn string → returns `false` (caller skips wiring — dev convenience + CI-friendly for WAF-based fixtures).
- Any env + non-empty conn string → returns `true`.

### appsettings.template.json update

Added `APPLICATIONINSIGHTS_CONNECTION_STRING` as a flat top-level key (matches the Azure App Service canonical env-var name and the BFF Program.cs read path). Not nested under `ApplicationInsights:*` — the flat key is what App Service KV references bind to.

### Test posture

`AuditLogMiddlewareTests.cs` uses pure-unit-level `DefaultHttpContext` invocation — no `WebApplicationFactory<Program>`. Rationale: `CosmosModule` and `ServiceBusModule` fail-fast at startup without live config (`Cosmos:AccountEndpoint`, `ServiceBus:FullyQualifiedNamespace`), which would prevent WAF booting. Testing the middleware in isolation via `DefaultHttpContext` proves the behavior contract without the boot dependency. When Wave C5 introduces real endpoints + a WAF fixture, the audit-log pipeline integration will be re-verified in end-to-end tests.

The tests use a small local `TestLogger<T>` class implementing `ILogger<T>` that captures records into an in-memory list — the ADR-038-friendly alternative to Moq'ing `ILogger` (which Microsoft docs + xUnit both discourage for `ILogger` extensions).

### Not in this task's scope (deferred)

- **General-purpose BeginScope enrichment** for downstream logs (ADR-028 broader pattern — L2 hardening task; NOT NFR-11's scope).
- **Real POST /api/runs handler**: still 501 Not Implemented placeholder (Wave C5).
- **Adaptive sampling exclusion rule for `AuditableAction`**: not needed today (100% sampling default); MUST be added to any future task that enables adaptive sampling.
- **Log Analytics workspace queries / alerts**: infrastructure-side, provisioned by task 033 `platform-controlplane.bicep`; the L2 code emits the data; the KQL is a separate deliverable.

## Build + test evidence

```
Debug/Release build (Sprk.Provisioning.ControlPlane):         0 warn / 0 err
Debug/Release build (Sprk.Provisioning.ControlPlane.Tests):   0 warn / 0 err (analyzers-as-errors enforced)
Unit tests:  34/34 passed
             (23 AuditLogMiddlewareTests   — new this task
             +  6 ServiceBusSmokeTests     — unit-only paths run; live-SB round-trip env-guarded
             +  5 CosmosSmokeTests         — unit-only paths run; live-Cosmos round-trip env-guarded)
CVE scan:    0 vulnerable packages (transitive + direct)
```

## Publish-size delta

Not applicable — L2 owns its own baseline (task 036 D-036 established 7.67 MB uncompressed / 3.28 MB compressed / 53 files). This task references NO new NuGet packages (task 036 already added `Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0`); the growth from task 036 baseline to now (~15.51 MB uncompressed / ~6.03 MB compressed / 65 files) is the cumulative delta of tasks 037 (Cosmos SDK) + 038 (Service Bus SDK) + 039 (OTel wiring activation). BFF publish size unchanged.

## ADR compliance

| ADR / NFR | Applied | How |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | 5 non-framework DI lines in Program.cs; no new interfaces / seams; TelemetryModule wires ONE OTel pipeline; AuditLogMiddleware takes only ILogger (framework-registered). |
| ADR-028 (Spaarke Auth v2) | ✅ | Middleware reads validated JWT claims from `HttpContext.User` only; never reads `Request.Headers["Authorization"]`, `Request.Body`, or logs raw JWT content. |
| ADR-032 (Null-Object kill-switch) | ✅ (vacuous) | `TelemetryModule` has NO `if (flag) { AddService }` branch. `AzureMonitorGuard` is a fail-fast environment guard, not a feature gate — the ADR-032 pattern explicitly does not apply. No endpoint handler consumes an OTel type via `[FromServices]`; `ILogger` is always registered. |
| NFR-05 (fail-fast config) | ✅ | AzureMonitorGuard throws in deployed envs; Development / Testing allow-listed with rationale. |
| NFR-06 (analyzers-as-errors) | ✅ | 0 warnings; nullable-ref-types respected; `ArgumentNullException.ThrowIfNull` used. |
| NFR-07 (god-class ratchet) | ✅ | Largest file 225 lines (AuditLogMiddleware); all well under 2,000-line freeze. |
| NFR-11 (auditable operator action) | ✅ | Middleware fires on POST/PUT/PATCH/DELETE; extracts `tid` from JWT (dual-key WS-* + short); emits via structured ILogger → OTel Logs → Azure Monitor `traces`; regression test covers 500-on-exception case; 401/403 short-circuits captured. |
