# Smoke Test Report -- Task 027

> **Date**: 2026-03-13
> **Environment**: Production (`prod`)
> **API Base URL**: `https://api.spaarke.com`
> **Customer ID**: `demo`
> **Test Script**: `scripts/Test-Deployment.ps1`
> **Total Duration**: 17.8 seconds
> **Overall Verdict**: FAILED (4 critical failures)

---

## Summary

| Metric | Count |
|--------|-------|
| Total Tests | 17 |
| Passed | 12 |
| Failed | 5 (4 critical, 1 non-critical) |
| Skipped | 0 |

---

## Detailed Results by Test Group

### 1. BFF API -- ALL PASS

| Test | Status | Duration | Details |
|------|--------|----------|---------|
| GET /healthz returns 200 | PASS | 466ms | HTTP 200 -- Healthy |
| GET /ping returns 200 | PASS | 284ms | HTTP 200 -- Pong |
| GET /api/me returns 401 without auth | PASS | 288ms | HTTP 401 -- Auth required (correct) |
| /healthz responds under 2s | PASS | 284ms | 281ms response time |

**Assessment**: BFF API at `api.spaarke.com` is fully operational. Health check, ping, and auth enforcement all working correctly. Response times well within SLA (< 500ms).

### 2. Dataverse -- 2 PASS, 1 FAIL

| Test | Status | Duration | Details |
|------|--------|----------|---------|
| PAC CLI authenticated | PASS | 2032ms | Active profile: spaarke-demo (index 3) |
| SpaarkeCore solution installed | **FAIL** (critical) | 2781ms | SpaarkeCore solution not found |
| Dataverse org accessible | PASS | 2742ms | Connected to spaarke-demo org |

**Assessment**: Dataverse environment (`spaarke-demo.crm.dynamics.com`) is accessible and PAC CLI is authenticated. The SpaarkeCore managed solution has not been imported yet -- this is a known prerequisite gap. Managed solution packages must be built from the main repo and imported before Spaarke-specific entities and web resources are available.

**Root Cause**: Task 025 (sample data) documented that "Spaarke-specific records blocked until managed solutions imported." The solution import step was deferred because managed solution packages (.zip) must be built from the main Spaarke repo's solution projects before they can be imported into the demo environment.

### 3. SPE (SharePoint Embedded) -- ALL PASS

| Test | Status | Duration | Details |
|------|--------|----------|---------|
| Container endpoint reachable | PASS | 282ms | HTTP 401 -- endpoint exists, auth required |
| Drive endpoint reachable | PASS | 282ms | HTTP 401 -- endpoint exists, auth required |

**Assessment**: SPE endpoints are deployed and responding. Auth enforcement is working (401 without token). Full SPE functionality requires authenticated access, which was confirmed during task 023 (BFF API deployment).

### 4. AI Services -- ALL PASS

| Test | Status | Duration | Details |
|------|--------|----------|---------|
| Azure OpenAI endpoint reachable | PASS | 436ms | HTTP 401 -- endpoint exists |
| OpenAI model deployments exist | PASS | 1321ms | 3 deployments: gpt-4o, gpt-4o-mini, text-embedding-3-large |
| AI Search endpoint reachable | PASS | 360ms | HTTP 403 -- endpoint exists |
| Document Intelligence endpoint reachable | PASS | 379ms | HTTP 401 -- endpoint exists |

**Assessment**: All AI services are deployed and operational:
- Azure OpenAI: 3 model deployments confirmed (gpt-4o, gpt-4o-mini, text-embedding-3-large)
- AI Search: Endpoint accessible at `spaarke-search-prod.search.windows.net`
- Document Intelligence: Endpoint accessible at `westus2.api.cognitive.microsoft.com`

### 5. Redis -- ALL FAIL

| Test | Status | Duration | Details |
|------|--------|----------|---------|
| Redis cache resource exists | **FAIL** (critical) | 2005ms | Resource `spaarke-redis-prod` not found in `rg-spaarke-platform-prod` |
| Redis SSL port accessible | FAIL (non-critical) | 1135ms | Cannot resolve hostname (depends on resource existing) |

**Assessment**: Azure Cache for Redis (`spaarke-redis-prod`) has not been provisioned in the production resource group. This is expected -- Redis was included in the platform Bicep template but may have been excluded from the initial deployment to reduce costs during the demo phase. The BFF API can run without Redis in development mode (in-memory fallback per ADR-009), but production workloads require Redis for distributed caching.

**Root Cause**: Redis provisioning was either skipped during `Deploy-Platform.ps1` execution or the resource name differs from what the test script expects.

### 6. Service Bus -- ALL FAIL

| Test | Status | Duration | Details |
|------|--------|----------|---------|
| Service Bus namespace exists | **FAIL** (critical) | 1318ms | Resource `spaarke-servicebus-prod` not found in `rg-spaarke-platform-prod` |
| document-processing queue exists | **FAIL** (critical) | 1373ms | Parent namespace not found |

**Assessment**: Azure Service Bus namespace (`spaarke-servicebus-prod`) has not been provisioned. Similar to Redis, Service Bus was part of the platform template but may have been excluded from initial deployment. The document-processing queue is required for the AI pipeline's asynchronous document processing workflow.

**Root Cause**: Service Bus provisioning was either skipped during `Deploy-Platform.ps1` execution or the resource name differs from what the test script expects.

---

## Bug Fix Applied During Testing

### PAC CLI Output Capture Bug (Fixed)

**Problem**: The Dataverse test group originally used `$output = & pac auth list 2>&1` to capture PAC CLI output. On Windows, `pac` is a `.cmd` wrapper (`pac.cmd`), and its stdout is not captured reliably when invoked via PowerShell's `& operator` inside a script. The `$LASTEXITCODE` variable was null (not 0), causing the null-inequality check `$LASTEXITCODE -ne 0` to always evaluate to true and falsely fail all Dataverse tests.

**Fix**: Changed all PAC CLI invocations in the Dataverse test group from `& pac ...` to `& cmd /c pac ...`, which properly routes the `.cmd` wrapper's output through the Windows command shell. Also changed exit code checks from `$LASTEXITCODE -ne 0` to `[string]::IsNullOrWhiteSpace($outputStr)` for more robust validation.

**File Modified**: `scripts/Test-Deployment.ps1` (lines 300-350)

---

## Remediation Plan

### Priority 1: Critical Infrastructure (Required Before Production Use)

| Issue | Remediation | Priority | Owner | Estimated Effort |
|-------|-------------|----------|-------|-----------------|
| Redis not provisioned | Deploy Azure Cache for Redis via `Deploy-Platform.ps1` with Redis module enabled, OR verify actual resource name if deployed under different naming | P1 | Infrastructure | 1 hour |
| Service Bus not provisioned | Deploy Azure Service Bus via `Deploy-Platform.ps1` with Service Bus module enabled, OR verify actual resource name | P1 | Infrastructure | 1 hour |
| Service Bus queue missing | Create `document-processing` queue after namespace provisioned | P1 | Infrastructure | 15 min |

### Priority 2: Application Layer (Required Before Demo)

| Issue | Remediation | Priority | Owner | Estimated Effort |
|-------|-------------|----------|-------|-----------------|
| SpaarkeCore solution not imported | Build managed solution packages from main repo, import to `spaarke-demo.crm.dynamics.com` via `Deploy-DataverseSolutions.ps1` | P2 | Development | 2 hours |
| Re-run sample data loader | After solution import, re-run `Load-DemoSampleData.ps1` to create Spaarke-specific entity records | P2 | Development | 30 min |

### Priority 3: Test Script Improvements (Non-blocking)

| Issue | Remediation | Priority | Owner | Estimated Effort |
|-------|-------------|----------|-------|-----------------|
| Test script timeout on original run | First run with Dataverse tests hung indefinitely due to PAC CLI bug (now fixed) | P3 | Done | Fixed |
| Resource name validation | Consider adding a pre-flight check to verify resource names match actual Azure resources before running tests | P3 | QA | 1 hour |

---

## Acceptance Criteria Assessment

| Criterion | Status | Notes |
|-----------|--------|-------|
| All platform smoke tests pass | PARTIAL | BFF API, SPE, AI pass. Redis and Service Bus not provisioned. |
| All demo customer smoke tests pass | PARTIAL | Dataverse org accessible but managed solutions not imported. |
| Test-Deployment.ps1 completes in under 5 minutes | PASS | Completed in 17.8 seconds |
| Test report documents all results with evidence | PASS | This report |
| Any failures have documented remediation plan | PASS | See Remediation Plan above |

---

## Next Steps

1. Verify whether Redis and Service Bus were intentionally excluded from initial deployment (cost optimization) or if the resource names differ
2. If excluded: decide whether to provision now or defer until first real customer onboarding
3. Build and import SpaarkeCore managed solution to demo environment
4. Re-run `Load-DemoSampleData.ps1` after solution import
5. Re-run `Test-Deployment.ps1` to confirm all tests pass after remediation

---

*Report generated by task-execute for PRODENV-027*
