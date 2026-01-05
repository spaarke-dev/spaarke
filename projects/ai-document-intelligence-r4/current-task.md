# Current Task State

> **Purpose**: Context recovery across compaction events.
> **Updated**: 2026-01-05

---

## Active Task

| Field | Value |
|-------|-------|
| Task ID | Bug Fixes + Azure Deployment |
| Task File | N/A - Post-Phase 6 bug fixes |
| Status | Ready for Azure API Deployment |
| Started | 2026-01-05 |

---

## HANDOFF: Azure Deployment Required

### Bug Fixes Completed (This Session)

Three bugs were discovered during user testing and fixed:

#### Bug 1: 400 error when selecting playbook ✅ DEPLOYED
- **Root cause**: Wrong N:N relationship name `sprk_playbook_action` → should be `sprk_analysisplaybook_action`
- **Fixed in**: `AnalysisBuilderApp.tsx` (v2.9.1)
- **PCF deployed to Dataverse**: ✅

#### Bug 2: Different action types produce same "summary" output ✅ CODE READY
- **Root cause**: `ScopeResolverService.GetActionAsync` used hardcoded stub data instead of fetching from Dataverse
- **Fixed by**:
  1. Added `GetAnalysisActionAsync` to `IDataverseService.cs`
  2. Added `AnalysisActionEntity` to `Models.cs`
  3. Implemented in `DataverseWebApiService.cs` and `DataverseServiceClientImpl.cs`
  4. Updated `ScopeResolverService.GetActionAsync` to fetch `sprk_systemprompt` from Dataverse
- **API deployment needed**: ⚠️ YES

#### Bug 3: Chat shows "waiting for session" and doesn't connect ✅ DEPLOYED
- **Root cause**: When no chat history exists, `isSessionResumed` never got set to `true`
- **Fixed in**: `AnalysisWorkspaceApp.tsx` (v1.2.19) - auto-sets `isSessionResumed = true`
- **PCF deployed to Dataverse**: ✅

---

## Next Steps for New Session

### 1. Deploy BFF API to Azure
The API changes for Bug 2 need deployment:

```bash
# Build and deploy API
dotnet publish src/server/api/Sprk.Bff.Api -c Release

# Or use existing deployment workflow
```

### 2. Test All Three Fixes
After API deployment:
- [ ] Select a playbook → should load scopes (Bug 1)
- [ ] Select different actions (Summarize vs Risk Analysis) → should produce different outputs (Bug 2)
- [ ] Open Analysis Workspace with no chat history → chat should be immediately usable (Bug 3)

### 3. Consider Committing Changes
Uncommitted files:
- `src/server/shared/Spaarke.Dataverse/IDataverseService.cs`
- `src/server/shared/Spaarke.Dataverse/Models.cs`
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
- `src/client/pcf/AnalysisBuilder/control/components/AnalysisBuilderApp.tsx`
- `src/client/pcf/AnalysisBuilder/control/ControlManifest.Input.xml`
- `src/client/pcf/AnalysisWorkspace/control/components/AnalysisWorkspaceApp.tsx`
- `src/client/pcf/AnalysisWorkspace/control/ControlManifest.Input.xml`

---

## Progress Summary

### Completed Phases (R4 Project)

| Phase | Status | Summary |
|-------|--------|---------|
| Phase 1: Dataverse Entity Validation | ✅ | All entities validated |
| Phase 2: Seed Data Population | ✅ | 55+ records deployed |
| Phase 3: Tool Handler Implementation | ✅ | 5 handlers, 312+ tests |
| Phase 4: Service Layer Extension | ✅ | Scope endpoints + auth |
| Phase 5: Playbook Assembly | ✅ | 3 MVP playbooks configured |
| Phase 6: UI/PCF Enhancement | ✅ | PCF v2.9.1 + v1.2.19 deployed |
| Bug Fixes | ✅ | 3 bugs fixed, API deployment pending |

### PCF Versions Deployed
- **AnalysisBuilder**: v2.9.1 (N:N relationship fix)
- **AnalysisWorkspace**: v1.2.19 (chat session auto-resume fix)

---

## Files Modified (This Session)

### Backend (API deployment required)
- `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` - Added `GetAnalysisActionAsync`
- `src/server/shared/Spaarke.Dataverse/Models.cs` - Added `AnalysisActionEntity`
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` - Implemented `GetAnalysisActionAsync`
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` - Implemented `GetAnalysisActionAsync`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` - Updated `GetActionAsync` to fetch from Dataverse

### Frontend (PCFs deployed)
- `src/client/pcf/AnalysisBuilder/control/components/AnalysisBuilderApp.tsx` - Fixed N:N relationship names
- `src/client/pcf/AnalysisBuilder/control/ControlManifest.Input.xml` - v2.9.1
- `src/client/pcf/AnalysisWorkspace/control/components/AnalysisWorkspaceApp.tsx` - Added auto-resume
- `src/client/pcf/AnalysisWorkspace/control/ControlManifest.Input.xml` - v1.2.19

---

## Blockers

_None - API deployment is the only remaining action_

---

*For context recovery: This file tracks active task state. Updated by task-execute skill.*
