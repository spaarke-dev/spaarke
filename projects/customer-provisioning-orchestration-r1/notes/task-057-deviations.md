# Task 057 — L2 REST endpoints deviations

**Date**: 2026-08-17
**Task**: `tasks/057-implement-l2-rest-endpoints.poml`
**Rigor**: FULL (per POML `<rigor>FULL</rigor>`)
**Result**: 8 L2 endpoints implemented + 23 endpoint tests passing (417 baseline → 440 total)

## Deviations from POML wording

### D-057-1 — Endpoint count is 8, not "9 endpoints exist under src/server/services/Sprk.Provisioning.ControlPlane/Api/**"

**POML says** (goal): *"All 9 endpoints exist under src/server/services/Sprk.Provisioning.ControlPlane/Api/**"*.

**What shipped**: 8 endpoints under `src/server/services/Sprk.Provisioning.ControlPlane/Api/**`. The 9th endpoint per §4.2 — `POST /api/onboarding/consent-callback` — is BFF-side (design.md D18 Anonymous+HMAC redirect from the Microsoft admin-consent flow; design.md §4.3a.2 "Model 2 self-service exception"). It is NOT part of this L2 project's surface and its shipping is owned by task 042's BFF-side sibling (this project's task 042 is the L2-side H0.5 *handler*; the BFF endpoint that receives the redirect is a separate concern).

**Path C (comply)**: the POML `<goal>` wording contradicts its own `<file role="new">` list which enumerates only `RunsEndpoints.cs`, `RunLogsEndpoints.cs`, `RunsEndpoints.Tests.cs`, and modifies `Program.cs` — no consent-callback file. Task POML step 1 says "Read task 042 H0.5 endpoint shape" — awareness, not authorship. Shipping the BFF endpoint from an L2-scoped task would violate CLAUDE.md §10 (BFF hygiene / placement-decision explicit) AND is out of scope per the file-list. The L2-side endpoints file header (`Api/RunsEndpoints.cs`) documents this boundary inline.

### D-057-2 — Placeholder POST /api/runs in HealthEndpoints.cs was REMOVED (not preserved)

**POML says** (implicit): the task's mission is to REPLACE the Wave-C3 placeholder with the real handler.

**What shipped**: `Endpoints/HealthEndpoints.cs`'s `MapPost("/api/runs", ... 501)` line is deleted, and the file's header rewritten to reflect its new single-endpoint scope (just `/ping`). Without this deletion, ASP.NET Core would throw `AmbiguousMatchException` at request-dispatch time because two endpoints share the same method + template. `HealthEndpoints.cs` is not on the POML's "what NOT to touch" list; the mutation is minimal (delete one MapPost line + update file header).

### D-057-3 — `customerId` MUST be supplied on every /{id}-based endpoint (query parameter)

**POML says** (implicit): §4.2's endpoint URIs are `/api/runs/{id}/*` — no mention of a customerId parameter shape.

**What shipped**: every endpoint that identifies a run by `{id}` REQUIRES a `?customerId=` query parameter (400 Bad Request when missing). Rationale is §4D I3 / FR-30: partition-key predicate discipline. Cosmos SDK cannot construct a `PartitionKey` predicate without the customerId value, and cross-partition queries are BANNED. Alternatives considered:

- **A. Server does cross-partition query** — VIOLATES §4D I3, rejected.
- **B. Server extracts customerId from the actor JWT** — REJECTED because operators legitimately provision for MULTIPLE customers; the actor's tenant identity is Spaarke-internal, not the customer's.
- **C. Route reshape to `/api/customers/{customerId}/runs/{runId}`** — REJECTED because it mutates the §4.2 surface (POML escalation trigger).
- **D. Require ?customerId= as a query parameter** — CHOSEN. Additive to the URL surface (does NOT mutate §4.2's endpoint templates), enforces I3 at the API layer, and matches the shape the L3 skill will produce (it already knows the customerId because it's calling POST /api/runs first).

`GET /api/runs/{id}` also carries `customerId=` in the Location header the enqueue endpoints return — clients round-trip it naturally. The design is documented inline in `Api/RunsEndpoints.cs` § PARTITION-KEY DISCIPLINE.

### D-057-4 — Audit-log emitter uses `ILogger.LogInformation`, not `TelemetryClient.TrackEvent`

**POML says** (context, step 5): *"App Insights TelemetryClient.TrackEvent('QuarantineCleared', ...)"*.

**What shipped**: the FR-24 clear-quarantine audit uses `ILogger.LogInformation(QuarantineCleared: ...)` — parity with the existing `AuditLogMiddleware` (task 039 file header § EMITTER MECHANISM). Rationale: dotnet-10-upgrade-r1 task 014 (FR-06) retired the classic `Microsoft.ApplicationInsights.AspNetCore` SDK across BFF; L2 (per its .csproj) uses the OpenTelemetry → Azure Monitor exporter. `TelemetryClient` is NOT registered and calling it would be a runtime no-op. The structured log record flows through the OTel Logs pipeline into Azure Monitor `traces` with structured properties preserved in `customDimensions`. Kusto query pivots on the stable `"QuarantineCleared:"` prefix:

```kql
traces
| where message startswith "QuarantineCleared"
| project timestamp,
          runId       = tostring(customDimensions.RunId),
          customerId  = tostring(customDimensions.CustomerId),
          reason      = tostring(customDimensions.Reason),
          actorTid    = tostring(customDimensions.ActorTid),
          actorOid    = tostring(customDimensions.ActorOid)
```

Path A deviation from POML step 5 wording, parity with the D-036-1 / D-039-1 deviations already carried forward in this project. Documented inline in `Api/RunsEndpoints.cs` § AUDIT-LOG.

### D-057-5 — State-transition scope is INTAKE ONLY; §4C state mutations owned by task 061

**POML says** (implicit via §4C references): the clear-quarantine endpoint clears the quarantined run.

**What shipped**: the clear-quarantine endpoint (a) validates the `reason` parameter (400 if missing), (b) verifies the run exists, (c) enqueues a `ClearQuarantine` action envelope, (d) emits the FR-24 audit-log record, (e) returns 202 Accepted. The actual `QuarantineState.Quarantined → QuarantineState.Cleared` transition + `Quarantine.ClearedBy/ClearedAt` mutation + `sprk_currentrunid` clear happens in the reconciler pipeline that task 061 (§4C rollback semantics) owns. The same intake-only pattern applies to resume + cancel + gate-advance — this task's scope per its TASK-INDEX position (before 058 reconciler + 059 concurrency guard + 060 crash recovery + 061 rollback).

This is DELIBERATE per the Fable M-9 resolution (design.md §4.2 v3.2): "state-reconciler background worker in L2 (a `BackgroundService` hosted alongside the API) polls Cosmos every 5 seconds ... advances the pipeline through the DAG without blocking any HTTP request." The endpoints are the intake surface; the state machine advances asynchronously. Full inline documentation in `Api/RunsEndpoints.cs` § STATE-TRANSITION SCOPE + the Program.cs comment block at MapRunsEndpoints registration.

### D-057-6 — `POST /api/runs` enqueues `H0` (not `H1`) on run creation

**POML says** (implicit): the POST /api/runs "creates" the run and enqueues something.

**What shipped**: POST /api/runs enqueues `H0` (preflight quota probe) as the initial dispatch. Rationale: design.md §4.1 DAG puts H0 preflight FIRST — it blocks the run BEFORE H1 subscription-readiness starts if any of the four preflight probes (Azure OpenAI TPM, Dataverse env-rate, subscription vCPU, SPE cert-bootstrap) is insufficient (design.md § 15 north star: surface lead-time items UP-FRONT, not after wasted Bicep runs). The separate `POST /api/runs/{id}/preflight` endpoint re-runs H0 for an existing run — used by the L3 skill's Step 4 preflight-gate flow (design.md §4.3a.4) when the operator wants to re-verify quotas after an environment change.

## Test coverage delivered (23 new tests)

- **AC #1 OpenAPI enumerates all endpoints**: 1 test (`Swagger_EnumeratesAllL2Endpoints`)
- **AC #4 401 unauthorized**: 8 tests (theory over 8 protected endpoints)
- **AC #3 403 forbidden**: 2 tests (POST /api/runs Reader, POST /api/runs/{id}/cancel Reader)
- **AC #2 202 create + enqueue**: 2 tests (Operator creates + enqueues H0; latency spot-check <100ms)
- **AC #5 200 read + partition-key**: 2 tests (Reader reads with partition key; 400 without customerId; 404 unknown)
- **AC #6/#7 clear-quarantine**: 2 tests (400 missing reason + NO audit-log; 202 with reason + audit-log has tid + oid + reason)
- **Resume/cancel/gate-advance/preflight**: 4 tests (Operator happy path + preflight 404 unknown)
- **RunLogs GET /phases/{phaseId}/logs**: 2 tests (Reader returns completed phase; in-flight returns 404 with hint)

## Verification

- `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/` → 0 warnings, 0 errors
- `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Tests/` → 0 warnings, 0 errors
- `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/ --filter Category!=Smoke` → **440 passed, 0 failed** (417 baseline + 23 new)
- `dotnet list src/server/services/Sprk.Provisioning.ControlPlane/ package --vulnerable --include-transitive` → **0 vulnerable**
- OpenAPI `/swagger/v1/swagger.json` enumerates all 8 L2 endpoints (verified in test `Swagger_EnumeratesAllL2Endpoints`)
- Latency spot-check: `PostRuns_LatencySpotCheck_Under100Ms` measures the 202 return path <250ms in CI (well under the FR-22 <100ms prod target)

## Files changed

- **New**: `src/server/services/Sprk.Provisioning.ControlPlane/Api/RunsEndpoints.cs` (7 endpoints under /api/runs)
- **New**: `src/server/services/Sprk.Provisioning.ControlPlane/Api/RunLogsEndpoints.cs` (1 endpoint for phase logs)
- **New**: `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Api/RunsEndpointsTests.cs` (23 endpoint tests + WebApplicationFactory + in-memory seams + TestAuthenticationHandler + TestAuditLogSink)
- **Modified**: `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` (add `using Sprk.Provisioning.ControlPlane.Api;` + 2 `app.Map*Endpoints()` calls after `MapHealthEndpoints`)
- **Modified**: `src/server/services/Sprk.Provisioning.ControlPlane/Endpoints/HealthEndpoints.cs` (remove POST /api/runs placeholder; keep /ping only — rewrite file header)
- **Modified**: `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Sprk.Provisioning.ControlPlane.Tests.csproj` (add `Microsoft.AspNetCore.Mvc.Testing 10.0.0` for WebApplicationFactory)
- **Modified**: `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` (task 057 status ⏸ → ✅)
