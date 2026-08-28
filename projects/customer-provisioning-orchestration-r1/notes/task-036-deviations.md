# Task 036 — L2 Control-Plane Scaffold Deviations

> **Task**: `036-scaffold-l2-controlplane-project.poml`
> **Author**: Wave C3 tail (customer-provisioning-orchestration-r1)
> **Date**: 2026-08-17
> **Related**: Task 024 (POCO models, ORPHANED until this task), Task 033 (`platform-controlplane.bicep`, sets audience), Tasks 037/038/039 (Cosmos / Service Bus / App Insights wiring)

## Overview

Task 036 scaffolded `src/server/services/Sprk.Provisioning.ControlPlane/` as a .NET 10 minimal-API peer service to Sprk.Bff.Api. Below are deliberate deviations from the POML wording, each within scope and documented per CLAUDE.md §6.5.

## D-036-1: Telemetry package — `Azure.Monitor.OpenTelemetry.AspNetCore` in place of `Microsoft.ApplicationInsights.AspNetCore`

**POML step 2** named the classic `Microsoft.ApplicationInsights.AspNetCore` SDK as a package to "reference now but defer wiring to task 039".

**What we did**: pinned `Azure.Monitor.OpenTelemetry.AspNetCore` **1.6.0** (matches BFF) instead of the classic SDK.

**Rationale (CLAUDE.md §6.5 path A — project-scoped exception)**:
- `dotnet-10-upgrade-r1` task 014 (FR-06 telemetry consolidation) retired the classic Microsoft.ApplicationInsights.AspNetCore package from the BFF in favor of OpenTelemetry → Azure Monitor.
- Adding the deprecated classic SDK to a NEW L2 service would create an immediate rework backlog item for task 039.
- Aligning L2 with the ecosystem-canonical modern telemetry package from birth is strictly better; wiring is still deferred (no `UseAzureMonitor()` call in this task).

**Impact**: task 039 wires `builder.Services.AddOpenTelemetry().UseAzureMonitor()` behind an `AzureMonitorGuard` (parity with BFF Program.cs lines 29–36).

## D-036-2: Azure.Identity floor bumped from 1.15.0 → 1.21.0

**What we did**: The initial csproj pinned `Azure.Identity 1.15.0`. Restore emitted `NU1605` (downgrade) because `Microsoft.Identity.Web.Certificate 4.14.2` requires `≥1.21.0`. Bumped to **1.21.0** (matches BFF).

**Rationale**: single Azure.Identity version across services (parity with BFF); satisfies transitive floor; no security regression.

## D-036-3: Swagger UI enabled unconditionally (no `IsDevelopment()` gate)

**POML step 5** simply says "Swashbuckle at /swagger with bearer-token security scheme" — no gating specified.

**What we did**: `app.UseSwagger()` + `app.UseSwaggerUI()` run in all environments.

**Rationale**: L2 is a Spaarke-internal service (not customer-facing); OpenAPI schema export is an acceptance criterion (spec FR-21). Task 039 or a later hardening pass may gate this by env if operator policy changes.

## D-036-4: `RuntimeIdentifier=linux-x64` + `SelfContained=false` set from birth

**Rationale**: L2 App Service is Linux (per `platform-controlplane.bicep`); this matches the BFF's FR-A1 framework-dependent linux-x64 convention. Confirmed at build: `bin/Release/net10.0/linux-x64/`.

**Local-dev implication**: `dotnet run` on Windows fails with "not a valid application for this OS platform" because the produced launcher is Linux-native. Workaround for cross-platform local smoke testing:

```
cd src/server/services/Sprk.Provisioning.ControlPlane
dotnet bin/Release/net10.0/linux-x64/Sprk.Provisioning.ControlPlane.dll
```

This bypasses the platform-specific launcher and executes the framework-independent IL. Documented for future task-execute smoke tests.

## D-036-5: `appsettings.json` shipped as `appsettings.template.json` (Spaarke convention)

**What we did**: `appsettings.json` is `.gitignore`d project-wide (`**/appsettings.json` rule at `.gitignore:90`). Committed as `appsettings.template.json` (allowlisted — see BFF's `appsettings.template.json` at `src/server/api/Sprk.Bff.Api/`). `<Content Update="appsettings.template.json" CopyToPublishDirectory="Never" />` in the csproj prevents the template from shipping to production.

**Local-dev implication**: to run L2 locally, copy the template:
```
cp src/server/services/Sprk.Provisioning.ControlPlane/appsettings.template.json \
   src/server/services/Sprk.Provisioning.ControlPlane/appsettings.json
```
The scaffold booted successfully with the template values verbatim during runtime smoke testing.

## D-036-6: `Microsoft.AspNetCore.Authentication.JwtBearer` referenced directly despite being transitive via Microsoft.Identity.Web

**POML step 2** listed both packages. `Microsoft.Identity.Web 4.14.2` transitively pulls `Microsoft.AspNetCore.Authentication.JwtBearer`. We kept both references for explicit intent — the direct reference matches the POML wording and documents that JwtBearer is a first-class dependency of this scaffold, not an accidental transitive.

## Publish-size baseline (L2 own ceiling — separate from BFF NFR-01)

```
Framework: net10.0 (linux-x64, framework-dependent)
Files:     53
Uncompressed:  7.67 MB (8,043,885 bytes)
  - PDBs:      0.03 MB (29,844 bytes)
  - Non-PDB:   7.64 MB (8,014,041 bytes)
Compressed .tar.gz (level 9): 3.28 MB (3,439,340 bytes)
```

Reported to establish an L2-scope baseline. Tasks 037/038/039 will add Cosmos SDK v3, Azure.Messaging.ServiceBus, and Azure.Monitor.OpenTelemetry.AspNetCore wiring (already referenced) — each should report delta vs this baseline.

## Runtime smoke test (structural verification)

Ran `dotnet bin/Release/net10.0/linux-x64/Sprk.Provisioning.ControlPlane.dll` from the project folder (correct ContentRoot):

| Endpoint | Result | Acceptance criterion |
|---|---|---|
| `GET /ping` | **200** body `ok` | ✅ FR-20 anonymous smoke test |
| `POST /api/runs` (no bearer) | **401 Unauthorized**, `WWW-Authenticate: Bearer` | ✅ FR-20 mutating endpoint auth check |
| `GET /swagger/v1/swagger.json` | **200** OpenAPI schema | ✅ FR-20 OpenAPI at /swagger |
| `GET /swagger` (UI) | **200** Swagger UI | ✅ FR-20 |

Two remaining acceptance criteria require a real Entra bearer token and are verified by CODE inspection only (a full unit-test suite is outside this task's scope — that arrives in a later Wave C4/C5 task):
- 403 with a valid non-Operator token — enforced by `.RequireAuthorization("Operator")` + `RequireRole("Operator")` policy.
- 501 with a valid Operator token — handler body is `Results.StatusCode(StatusCodes.Status501NotImplemented)`.

## Placement Justification (CLAUDE.md §10 — L2 vs BFF)

**NOT a BFF addition.** L2 is a peer service, not an extension. `.claude/constraints/bff-extensions.md` does NOT apply to this project's csproj/DI graph. No reference to `Sprk.Bff.Api` assemblies or types (verified: `ProjectReference` and `PackageReference` lists in `Sprk.Provisioning.ControlPlane.csproj` show no BFF coupling).

L2 publish-size does NOT count against BFF's ≤60 MB ceiling (NFR-01 scopes to `Sprk.Bff.Api`). BFF publish size is unchanged by this task.

## ADR compliance

| ADR | Applied | How |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | Program.cs has 2 non-framework DI lines (`AddAuthModule`, `AddSwaggerModule`); module extension methods keep composition obvious. |
| ADR-028 (Spaarke Auth v2) | ✅ | `AddMicrosoftIdentityWebApi` for inbound JWT (canonical stack); tenant-specific authority via `AzureAd:TenantId` (NOT common/organizations); audience `api://spaarke-provisioning-controlplane-{env}` matches `platform-controlplane.bicep`. |
| ADR-032 (Null-Object kill-switch) | ✅ (vacuous) | Zero conditional `if (flag) { AddService }` branches. No feature gates in scaffold; if a future task introduces one, it MUST apply P1/P2/P3. |
| NFR-06/07 (analyzers-as-errors, god-class ratchet) | ✅ | Inherits `TreatWarningsAsErrors=true` from `Directory.Build.props`; module-per-feature composition prevents Program.cs god-class. Largest file (`Program.cs`) is ~60 lines; all module files ≤120 lines. |
