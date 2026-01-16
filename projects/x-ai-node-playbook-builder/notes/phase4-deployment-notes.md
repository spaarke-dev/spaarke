# Phase 4 Deployment Notes

> **Phase**: 4 - Advanced Features
> **Date**: 2026-01-13
> **Status**: ✅ DEPLOYED

---

## Overview

Phase 4 implements advanced features for the AI Node Playbook Builder:
- Condition branching for conditional logic in playbooks
- Model selection per node (gpt-4o, gpt-4o-mini)
- Confidence display in node outputs
- Template library UI for browsing/cloning playbooks
- Execution history API for viewing past runs

---

## Features Implemented

### Task 031: Condition Node Branching

**Files Modified:**
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` - Condition evaluation logic
- `src/server/api/Sprk.Bff.Api/Services/Ai/Execution/ConditionNodeExecutor.cs` - Condition executor
- `src/server/api/Sprk.Bff.Api/Models/Ai/ConditionExpression.cs` - Expression models

**E2E Test Steps:**
1. Create a playbook with condition nodes
2. Set condition expression (e.g., `confidence > 0.8`)
3. Run playbook and verify branching occurs based on condition result
4. Check that correct branch (then/else) executes

### Task 033: Model Selection

**Files Modified:**
- `src/client/pcf/PlaybookBuilderHost/control/components/Properties/NodePropertiesForm.tsx` - Model dropdown
- `src/server/api/Sprk.Bff.Api/Services/Ai/Execution/TaskNodeExecutor.cs` - Model selection logic

**E2E Test Steps:**
1. Open node properties panel
2. Select different model from dropdown (gpt-4o, gpt-4o-mini)
3. Save playbook and execute
4. Verify node uses selected model (check metrics in run details)

### Task 035: Confidence Display

**Files Modified:**
- `src/client/pcf/PlaybookBuilderHost/control/components/Canvas/NodeCard.tsx` - Confidence badge display
- `src/server/api/Sprk.Bff.Api/Models/Ai/NodeOutput.cs` - Confidence property

**E2E Test Steps:**
1. Execute a playbook with AI nodes
2. View node output in UI
3. Verify confidence score displays (0.0 - 1.0 range)
4. Check confidence appears in run details API response

### Task 037: Template Library UI

**Files Modified:**
- `src/client/pcf/PlaybookBuilderHost/control/components/Templates/TemplateLibraryDialog.tsx` - NEW
- `src/client/pcf/PlaybookBuilderHost/control/stores/templateStore.ts` - NEW
- `src/client/pcf/PlaybookBuilderHost/control/PlaybookBuilderHost.tsx` - Templates button
- `src/client/pcf/PlaybookBuilderHost/control/ControlManifest.Input.xml` - apiBaseUrl property

**E2E Test Steps:**
1. Configure apiBaseUrl property on form (BFF API URL)
2. Click "Templates" button in header (only visible when apiBaseUrl set)
3. Verify template library dialog opens
4. Search for templates by name
5. Select a template and click Clone
6. Verify new playbook is created and form navigates to it

### Task 038: Execution History

**Files Modified:**
- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookRunEndpoints.cs` - New endpoints
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` - History methods
- `src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookRunHistoryDto.cs` - NEW

**API Endpoints:**
- `GET /api/ai/playbooks/{id}/runs` - Paginated run history
  - Query params: `page`, `pageSize`, `state` (filter)
- `GET /api/ai/playbooks/runs/{runId}/detail` - Detailed run with node metrics

**E2E Test Steps:**
1. Execute a playbook multiple times
2. Call GET `/api/ai/playbooks/{id}/runs` - verify list returns
3. Call with `?state=Completed` - verify filtering works
4. Call GET `/api/ai/playbooks/runs/{runId}/detail` - verify node metrics

---

## Deployment Commands

### BFF API Deployment

```powershell
# Build in Release mode
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ./publish

# Deploy to Azure App Service
az webapp deployment source config-zip `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --src ./publish.zip
```

### PCF Control Deployment

```powershell
# Build control
cd src/client/pcf/PlaybookBuilderHost
npm run build

# Push to Dataverse dev environment
pac pcf push --publisher-prefix sprk
```

---

## Version Information

| Component | Version |
|-----------|---------|
| PlaybookBuilderHost PCF | 2.7.0 |
| BFF API | (see Sprk.Bff.Api.csproj) |

---

## Test Results Summary

| Test Category | Result |
|--------------|--------|
| PlaybookOrchestrationService Tests | ✅ All Pass |
| PlaybookRunEndpoints Tests | ✅ All Pass |
| NodeExecutor Tests | ✅ All Pass |
| Total Phase 4 Tests | ✅ 164/165 Pass |

**Note:** 1 pre-existing test failure in AnalysisOrchestrationServiceTests (unrelated to Phase 4).

---

## Known Limitations

1. **Execution History**: Uses in-memory storage, runs are cleared after 1 hour
2. **Template Clone**: Requires apiBaseUrl property to be configured on form
3. **Model Selection**: Requires Azure OpenAI deployments for gpt-4o and gpt-4o-mini

---

## Deployment Record

### BFF API - ✅ DEPLOYED 2026-01-13

| Property | Value |
|----------|-------|
| **App Service** | `spe-api-dev-67e2xz` |
| **Endpoint** | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| **Health Check** | ✅ Healthy |
| **Deployment Method** | `az webapp deploy` (zip deployment) |

### PCF Control - ✅ DEPLOYED 2026-01-13

| Property | Value |
|----------|-------|
| **Namespace** | `Spaarke.Controls.PlaybookBuilderHost` |
| **Publisher Prefix** | `sprk_` |
| **Solution** | `PowerAppsToolsTemp_sprk` (temporary) |
| **Bundle Size** | 240 KB (optimized with platform libraries) |
| **Version** | 2.7.0 |

**Key Changes Made During Deployment:**
- Fixed bundle size from 9MB to 240KB by enabling custom webpack with tree-shaking for `@fluentui/react-icons`
- Changed namespace from `Spaarke.PCF` to `Spaarke.Controls` per conventions
- Cleaned up orphaned managed solution `PlaybookBuilderSolution` (policy: ALWAYS use unmanaged)
- Added `featureconfig.json` with `pcfAllowCustomWebpack: "on"` and `pcfReactPlatformLibraries: "on"`
- Created `webpack.config.js` to optimize tree-shaking

---

## Post-Deployment Verification Checklist

- [x] BFF API health check passes (`GET /healthz`)
- [ ] PCF control loads in Dataverse form
- [ ] Template library dialog opens (when apiBaseUrl configured)
- [ ] Execution history endpoint returns data
- [ ] Condition nodes evaluate correctly
- [ ] Model selection persists and executes
- [ ] Confidence displays in node output

---

*Generated by task-execute skill for Task 039*
