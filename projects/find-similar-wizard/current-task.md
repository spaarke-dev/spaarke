# Current Task State - Find Similar Wizard MVP

> **Last Updated**: 2026-04-03 14:00 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Find Similar Wizard MVP — Document lookup + file upload → DocumentRelationshipViewer |
| **Step** | 3 of 3: All implementation complete |
| **Status** | Implementation complete, ready for build & deploy |
| **Next Action** | Build and deploy: (1) BFF API via /bff-deploy, (2) FindSimilarCodePage via /code-page-deploy |

### Files Modified This Session
- `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` - Added POST /related-from-content endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/Visualization/IVisualizationService.cs` - Added IndexTemporaryContentAsync + ContentUploadResult
- `src/server/api/Sprk.Bff.Api/Services/Ai/Visualization/VisualizationService.cs` - Implemented IndexTemporaryContentAsync (text extract → embed → AI Search index)
- `src/solutions/FindSimilarCodePage/src/main.tsx` - Rewrote: @spaarke/auth bootstrap + render FindSimilarApp
- `src/solutions/FindSimilarCodePage/src/App.tsx` - NEW: Full dialog with document lookup + file upload + viewer launch
- `src/solutions/FindSimilarCodePage/package.json` - Added @spaarke/auth, @spaarke/ui-components deps
- `src/solutions/FindSimilarCodePage/vite.config.ts` - Added @spaarke/auth alias

### Critical Context
- BFF: `POST /api/ai/visualization/related-from-content` accepts multipart file, extracts text via ITextExtractor, generates embedding via IOpenAiClient, writes temp VisualizationDocument to AI Search, returns { documentId, success }
- Frontend: Two paths — Path A (Xrm lookup → documentId) or Path B (file upload → BFF → temp documentId). Both open DocumentRelationshipViewer via navigateTo.
- Get Started section already wired: `sprk_findsimilar` card click opens the dialog at 60%×70%
- Temp AI Search entries use tags ["temporary", "find-similar-upload"] for future cleanup
- Build verified: BFF compiles (0 errors), Frontend builds (vite single-file output)

---

## Implementation Summary

### Step 1: BFF Endpoint (COMPLETE)
- Added `ContentUploadResult` record to `IVisualizationService`
- Added `IndexTemporaryContentAsync` to interface and implementation
- VisualizationService constructor now accepts ITextExtractor + IOpenAiClient
- Pipeline: validate → extract text → generate embedding → MergeOrUpload to AI Search
- Temp documents have `temp-viz-` prefix on search ID and "temporary" tag
- Endpoint: file validation (size, extension), tenant ID from query/JWT, returns ContentUploadResult

### Step 2: Frontend Code Page (COMPLETE)
- Transformed from iframe wrapper to full @spaarke/auth-enabled dialog
- Bootstrap: resolveRuntimeConfig → initAuth → getTenantId → render
- Two mutually exclusive paths with visual feedback
- Xrm.Utility.lookupObjects for document selection
- Drag-and-drop + click-to-browse file upload
- File validation (extension, size) on client side
- Opens DocumentRelationshipViewer via Xrm.Navigation.navigateTo

### Step 3: Wiring (ALREADY DONE)
- getStarted.registration.ts already maps "find-similar" → sprk_findsimilar
- WorkspaceGrid handleOpenWizardGeneric provides the dialog chrome
